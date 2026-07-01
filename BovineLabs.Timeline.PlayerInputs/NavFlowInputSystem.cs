using BovineLabs.Core.Extensions;
using BovineLabs.Core.Iterators;
using BovineLabs.Movement.Data;
using BovineLabs.Reaction.Data.Core;
using BovineLabs.Timeline.Data;
using BovineLabs.Timeline.EntityLinks;
using BovineLabs.Timeline.EntityLinks.Data;
using BovineLabs.Timeline.PlayerInputs.Data;
using BovineLabs.Timeline.PlayerInputs.Flow.Data;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace BovineLabs.Timeline.PlayerInputs.Flow
{
    /// <summary>
    /// Nav sibling of <see cref="SplineFlowInputSystem"/> / <see cref="GridFlowInputSystem"/>. A hidden non-physics
    /// Traverse agent (the "proxy", resolved via <see cref="NavFlowInputConfig.ProxyLinkKey"/>) pathfinds toward the
    /// clip's destination; the direction from the player toward that proxy lead-point (projected to XZ) is accumulated
    /// into the resolved player's synthetic InputAxis buffer, so the existing AxisTransform carrot + physics motors make
    /// the player physically chase the proxy — pathing around walls — with no new physics.
    ///
    /// The proxy reads its position (stable across frames), NOT DesiredVelocity (which MoveApplySystem zeroes after
    /// reading — tapping it would need fragile cross-group ordering). This makes the proxy the direct navmesh analog of
    /// the spline cursor: a moving lead-point the player chases.
    ///
    /// Ordering / world filter mirror SplineFlowInputSystem: after the once-per-frame SyntheticProviderClearSystem,
    /// after the other synthetic sources (all only accumulate — order-free), before AxisTransformSystem, LocalSimulation
    /// only (to match the sole consumer + the Progress-free per-world semantics).
    ///
    /// ponytail: re-enables proxy pathfinding each non-held frame; near the goal that flip-flops with the package's
    /// auto-arrival-disable, but the proxy is hidden and the player's input dead-zones out at the goal, so it is
    /// invisible. Upgrade path: gate the re-enable on an explicit arrived check if a visible proxy ever needs it.
    /// </summary>
    [UpdateInGroup(typeof(TimelineComponentAnimationGroup))]
    [UpdateAfter(typeof(SyntheticProviderClearSystem))]
    [UpdateAfter(typeof(SplineFlowInputSystem))]
    [UpdateBefore(typeof(AxisTransformSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation)]
    public partial struct NavFlowInputSystem : ISystem
    {
        private UnsafeComponentLookup<Targets> _targets;
        private UnsafeComponentLookup<EntityLinkSource> _sources;
        private UnsafeBufferLookup<EntityLinkEntry> _entries;
        private ComponentLookup<PlayerId> _playerIds;
        private BufferLookup<InputAxis> _axisBuffers;
        private ComponentLookup<SyntheticProviderTag> _synthetic;
        private ComponentLookup<LocalToWorld> _ltws;
        private ComponentLookup<LocalTransform> _localTransforms;
        private ComponentLookup<CrowdAgentData> _agentData;
        private ComponentLookup<IsPathfinding> _pathfinding;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<InputRegistry>();
            state.RequireForUpdate<NavFlowInputConfig>();

            _targets = state.GetUnsafeComponentLookup<Targets>(true);
            _sources = state.GetUnsafeComponentLookup<EntityLinkSource>(true);
            _entries = state.GetUnsafeBufferLookup<EntityLinkEntry>(true);
            _playerIds = state.GetComponentLookup<PlayerId>(true);
            _axisBuffers = state.GetBufferLookup<InputAxis>(false);
            _synthetic = state.GetComponentLookup<SyntheticProviderTag>(true);
            _ltws = state.GetComponentLookup<LocalToWorld>(true);
            _localTransforms = state.GetComponentLookup<LocalTransform>(false);
            _agentData = state.GetComponentLookup<CrowdAgentData>(false);
            _pathfinding = state.GetComponentLookup<IsPathfinding>(false);
        }

        public void OnUpdate(ref SystemState state)
        {
            // Same reason as SplineFlowInputSystem: we read Targets/EntityLink* via Unsafe*Lookup and write the InputAxis
            // buffer + drive the proxy on the MAIN thread, which bypasses the safety system's auto-completion.
            state.CompleteDependency();

            _targets.Update(ref state);
            _sources.Update(ref state);
            _entries.Update(ref state);
            _playerIds.Update(ref state);
            _axisBuffers.Update(ref state);
            _synthetic.Update(ref state);
            _ltws.Update(ref state);
            _localTransforms.Update(ref state);
            _agentData.Update(ref state);
            _pathfinding.Update(ref state);

            var registry = SystemAPI.GetSingleton<InputRegistry>().ProviderByPlayer;

            // --- Active: drive the proxy + feed the fake axis --------------------------------------------------------
            foreach (var (config, binding, weight, activePrev) in
                     SystemAPI.Query<RefRO<NavFlowInputConfig>, RefRO<TrackBinding>, RefRO<ClipWeight>,
                             EnabledRefRO<ClipActivePrevious>>()
                         .WithAll<ClipActive>()
                         .WithPresent<ClipActivePrevious>()) // enter frame has ClipActivePrevious disabled; match it anyway.
            {
                var cfg = config.ValueRO;
                if (cfg.ActionId == byte.MaxValue)
                    continue;

                var target = binding.ValueRO.Value;
                if (target == Entity.Null || !_targets.TryGetComponent(target, out var targets))
                    continue;

                // consumer -> player -> synthetic provider (identical to SplineFlowInputSystem)
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

                // proxy agent
                if (!EntityLinkResolver.TryResolve(
                        target, targets, cfg.ReadRootFrom, cfg.ProxyLinkKey, _sources, _entries, out var proxy))
                    continue;

                if (!_agentData.HasComponent(proxy) || !_pathfinding.HasComponent(proxy) ||
                    !_localTransforms.HasComponent(proxy) || !_ltws.HasComponent(proxy))
                    continue;

                if (!_ltws.TryGetComponent(target, out var playerLtw))
                    continue;

                var playerPos = playerLtw.Position;
                var isFirstFrame = !activePrev.ValueRO;

                // (Re)write the destination on enter, and every frame when following a moving target.
                if (isFirstFrame || cfg.Follow)
                {
                    var haveDest = false;
                    var destPos = float3.zero;
                    if (cfg.Destination == Target.None)
                    {
                        destPos = cfg.WorldPosition;
                        haveDest = true;
                    }
                    else
                    {
                        var dest = targets.Get(cfg.Destination, target);
                        if (dest != Entity.Null && _ltws.TryGetComponent(dest, out var destLtw))
                        {
                            destPos = destLtw.Position;
                            haveDest = true;
                        }
                    }

                    if (haveDest)
                    {
                        var d = _agentData[proxy];
                        d.TargetPosition = destPos;
                        d.TargetPositionExtents = cfg.Extents;
                        d.QueryFilterType = cfg.QueryFilterType;
                        _agentData[proxy] = d;
                    }
                    else if (isFirstFrame)
                    {
                        continue; // can't start pathing without a destination
                    }
                }

                if (isFirstFrame)
                {
                    // Teleport the proxy onto the player so the corridor starts here, then start pathing.
                    // No input on the setup frame (proxy == player -> dir would be zero anyway; LtW is still stale).
                    var lt = _localTransforms[proxy];
                    lt.Position = playerPos;
                    _localTransforms[proxy] = lt;
                    _pathfinding.SetComponentEnabled(proxy, true);
                    continue;
                }

                var proxyLtw = _ltws[proxy];
                var dir = NavFlowInputMath.LeadDirection(proxyLtw.Position.xz, playerPos.xz, cfg.LeashRadius, out var held);

                // Leash: hold the proxy when it out-runs the player; resume when the player closes the gap.
                _pathfinding.SetComponentEnabled(proxy, !held);

                if (math.all(dir == float2.zero))
                    continue;

                Accumulate(_axisBuffers[provider], cfg.ActionId, dir * (cfg.Gain * weight.ValueRO.Value));
            }

            // --- Exit: stop the hidden proxy so it does not wander to the goal after the clip ends -------------------
            foreach (var (config, binding) in
                     SystemAPI.Query<RefRO<NavFlowInputConfig>, RefRO<TrackBinding>>()
                         .WithDisabled<ClipActive>()
                         .WithAll<ClipActivePrevious>())
            {
                var target = binding.ValueRO.Value;
                if (target == Entity.Null || !_targets.TryGetComponent(target, out var targets))
                    continue;

                if (!EntityLinkResolver.TryResolve(
                        target, targets, config.ValueRO.ReadRootFrom, config.ValueRO.ProxyLinkKey, _sources, _entries,
                        out var proxy))
                    continue;

                if (_pathfinding.HasComponent(proxy))
                    _pathfinding.SetComponentEnabled(proxy, false);
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
