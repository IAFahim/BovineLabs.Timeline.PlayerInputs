using BovineLabs.Core.Extensions;
using BovineLabs.Core.Iterators;
using BovineLabs.Reaction.Data.Core;
using BovineLabs.Timeline.Data;
using BovineLabs.Timeline.EntityLinks;
using BovineLabs.Timeline.EntityLinks.Data;
using BovineLabs.Timeline.Physics;
using BovineLabs.Timeline.PlayerInputs.Data;
using BovineLabs.Timeline.PlayerInputs.Flow.Data;
using Unity.Entities;
using Unity.Mathematics;

namespace BovineLabs.Timeline.PlayerInputs.Flow
{
    /// <summary>
    /// Spline sibling of <see cref="GridFlowInputSystem"/>. Advances a per-clip cursor along the bound spline and
    /// accumulates the tangent (projected to the XZ plane) into the resolved player's synthetic InputAxis buffer, so the
    /// existing AxisTransform carrot + physics motors steer the body along the path with no new physics.
    ///
    /// Ordering: explicitly AFTER SyntheticProviderClearSystem (the buffer must be cleared before we accumulate — this
    /// constraint holds even when GridFlowInputSystem is absent, which orphans the UpdateAfter below) and AFTER
    /// GridFlowInputSystem so the field flow's contribution survives (both only accumulate; their sum is order-free).
    ///
    /// World filter is LocalSimulation-only to MATCH the sole consumer AxisTransformSystem: synthetic input computed in
    /// Server/Client worlds is never read there, and the per-world Progress cursor would diverge across them. (GridFlow
    /// owns its own clear in those worlds; do not delete it.)
    ///
    /// ponytail: tangent is taken on the XZ ground plane (float2(x, z)) — same frame the field flow emits and the same
    /// frame an AxisTransform Move clip consumes in WORLD mode. The bound Move clip must NOT be CameraRelative, exactly
    /// like the field flow. Tilted influence planes would need GridBasis(InfluenceGridSettings.PlaneNormal) here.
    /// </summary>
    [UpdateInGroup(typeof(TimelineComponentAnimationGroup))]
    [UpdateAfter(typeof(SyntheticProviderClearSystem))]
    [UpdateAfter(typeof(GridFlowInputSystem))]
    [UpdateBefore(typeof(AxisTransformSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation)]
    public partial struct SplineFlowInputSystem : ISystem
    {
        private UnsafeComponentLookup<Targets> _targets;
        private UnsafeComponentLookup<EntityLinkSource> _sources;
        private UnsafeBufferLookup<EntityLinkEntry> _entries;
        private ComponentLookup<PlayerId> _playerIds;
        private BufferLookup<InputAxis> _axisBuffers;
        private ComponentLookup<SyntheticProviderTag> _synthetic;

#if UNITY_EDITOR
        private double _nextMissingSplineWarn;
#endif

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<SplineRegistry>();
            state.RequireForUpdate<InputRegistry>();
            state.RequireForUpdate<SplineFlowInputConfig>();

            _targets = state.GetUnsafeComponentLookup<Targets>(true);
            _sources = state.GetUnsafeComponentLookup<EntityLinkSource>(true);
            _entries = state.GetUnsafeBufferLookup<EntityLinkEntry>(true);
            _playerIds = state.GetComponentLookup<PlayerId>(true);
            _axisBuffers = state.GetBufferLookup<InputAxis>(false);
            _synthetic = state.GetComponentLookup<SyntheticProviderTag>(true);
        }

        public void OnUpdate(ref SystemState state)
        {
            // Required (matches GridFlowInputSystem): we read Targets/EntityLink* via Unsafe*Lookup and write the
            // InputAxis buffer on the MAIN thread, which bypasses the safety system's auto-completion. When GridFlow
            // runs first it already drained the graph so this is ~free; when GridFlow is absent this is the only sync
            // that makes the unsafe main-thread reads safe. Cheaper alternative is a single-threaded IJobChunk — deferred
            // until profiling shows this handful-of-clips system actually matters.
            state.CompleteDependency();

            _targets.Update(ref state);
            _sources.Update(ref state);
            _entries.Update(ref state);
            _playerIds.Update(ref state);
            _axisBuffers.Update(ref state);
            _synthetic.Update(ref state);

            var splines = SystemAPI.GetSingleton<SplineRegistry>().Map;
            var registry = SystemAPI.GetSingleton<InputRegistry>().ProviderByPlayer;
            var dt = SystemAPI.Time.DeltaTime;

            foreach (var (config, stateRef, binding, weight) in
                     SystemAPI.Query<RefRO<SplineFlowInputConfig>, RefRW<SplineFlowInputState>, RefRO<TrackBinding>, RefRO<ClipWeight>>()
                         .WithAll<ClipActive>())
            {
                var cfg = config.ValueRO;

                if (cfg.ActionId == byte.MaxValue)
                    continue;

                if (!splines.TryGetValue(cfg.SplineKey, out var spline) || !spline.IsCreated)
                {
#if UNITY_EDITOR
                    // The #1 designer trap unique to this clip: a SplineSchema is assigned but no SplinePathAuthoring in
                    // the scene registers it, so the key never reaches SplineRegistry — otherwise a silent, clean-console
                    // "nothing happens". Warn loudly but throttled so it never spams the log.
                    if (SystemAPI.Time.ElapsedTime >= _nextMissingSplineWarn)
                    {
                        _nextMissingSplineWarn = SystemAPI.Time.ElapsedTime + 3.0;
                        UnityEngine.Debug.LogWarning(
                            $"SplineFlowInput: an active clip references spline key {cfg.SplineKey} but no SplinePathAuthoring " +
                            "registered it in this scene — the clip will steer nothing. Add a SplinePathAuthoring with that schema.");
                    }
#endif
                    continue;
                }

                var target = binding.ValueRO.Value;
                if (target == Entity.Null)
                    continue;

                if (!_targets.TryGetComponent(target, out var targets))
                    continue;

                if (!EntityLinkResolver.TryResolve(
                        target, targets, cfg.ReadRootFrom, cfg.ConsumerLinkKey, _sources, _entries, out var consumer))
                    continue;

                if (!_playerIds.TryGetComponent(consumer, out var playerId))
                    continue;

                var provider = registry[playerId.Value];
                if (provider == Entity.Null || !_axisBuffers.HasBuffer(provider))
                    continue;

                if (!_synthetic.HasComponent(provider))
                    continue;

                var delta = SplineFlowInputMath.Delta(cfg.Traversal, cfg.Speed, cfg.TraversalSeconds, dt, spline.Value.Length);
                var sign = SplineFlowInputMath.Sign(cfg.Direction);
                stateRef.ValueRW.Progress += delta * sign;

                SplineFlowInputMath.Sample(stateRef.ValueRO.Progress, cfg.Lead, sign, cfg.Wrap, out var t, out var tangentSign);

                var dir = SplineFlowInputMath.Project(spline.Value.EvaluateTangent(t), tangentSign);
                if (math.all(dir == float2.zero))
                    continue;

                Accumulate(_axisBuffers[provider], cfg.ActionId, dir * (cfg.Gain * weight.ValueRO.Value));
            }
        }

        private static void Accumulate(DynamicBuffer<InputAxis> axes, byte actionId, float2 value)
        {
            for (var i = 0; i < axes.Length; i++)
            {
                if (axes[i].ActionId != actionId)
                    continue;

                var entry = axes[i];
                entry.Value += value;
                axes[i] = entry;
                return;
            }

            axes.Add(new InputAxis { ActionId = actionId, Value = value });
        }
    }
}
