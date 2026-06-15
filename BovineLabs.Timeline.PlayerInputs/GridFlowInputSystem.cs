using BovineLabs.Core.Extensions;
using BovineLabs.Core.Iterators;
using BovineLabs.Reaction.Data.Core;
using BovineLabs.Timeline.Data;
using BovineLabs.Timeline.EntityLinks;
using BovineLabs.Timeline.EntityLinks.Data;
using BovineLabs.Timeline.Grid.Influence.Data;
using BovineLabs.Timeline.Grid.Influence.Data.Flows;
using BovineLabs.Timeline.PlayerInputs.Data;
using BovineLabs.Timeline.PlayerInputs.Flow.Data;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace BovineLabs.Timeline.PlayerInputs.Flow
{
    [UpdateInGroup(typeof(TimelineComponentAnimationGroup))]
    [UpdateBefore(typeof(AxisTransformSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ServerSimulation |
                       WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.Editor)]
    public partial struct GridFlowInputSystem : ISystem
    {
        private UnsafeComponentLookup<Targets> _targets;
        private UnsafeComponentLookup<EntityLinkSource> _sources;
        private UnsafeBufferLookup<EntityLinkEntry> _entries;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<InfluenceGridSettings>();
            state.RequireForUpdate<FieldRegistrySingleton>();
            state.RequireForUpdate<InputRegistry>();
            state.RequireForUpdate<FlowInputConfig>();

            _targets = state.GetUnsafeComponentLookup<Targets>(true);
            _sources = state.GetUnsafeComponentLookup<EntityLinkSource>(true);
            _entries = state.GetUnsafeBufferLookup<EntityLinkEntry>(true);
        }

        public void OnUpdate(ref SystemState state)
        {
            state.CompleteDependency();

            _targets.Update(ref state);
            _sources.Update(ref state);
            _entries.Update(ref state);

            var settings = SystemAPI.GetSingleton<InfluenceGridSettings>();
            ref var reg = ref SystemAPI.GetSingletonRW<FieldRegistrySingleton>().ValueRW.Registry;

            for (var i = 0; i < reg.Count; i++)
            {
                ref var pair = ref reg.Slot(i);
                pair.WriterDependency.Complete();
                pair.WriterDependency = default;
                pair.Front.Complete();
                if (pair.DoubleBuffered)
                    pair.Back.Complete();
            }

            var registry = SystemAPI.GetSingleton<InputRegistry>().ProviderByPlayer;
            var cellSize = math.max(0.0001f, settings.CellSize);
            var basis = new GridBasis(settings.PlaneNormal);

            var playerIds = SystemAPI.GetComponentLookup<PlayerId>(true);
            var transforms = SystemAPI.GetComponentLookup<LocalTransform>(true);
            var axisBuffers = SystemAPI.GetBufferLookup<InputAxis>();
            var synthetic = SystemAPI.GetComponentLookup<SyntheticProviderTag>(true);

            foreach (var axes in SystemAPI.Query<DynamicBuffer<InputAxis>>()
                         .WithAll<ProviderTag, SyntheticProviderTag>())
                axes.Clear();

            foreach (var (config, binding, weight) in
                     SystemAPI.Query<RefRO<FlowInputConfig>, RefRO<TrackBinding>, RefRO<ClipWeight>>()
                         .WithAll<ClipActive>())
            {
                var cfg = config.ValueRO;

                var target = binding.ValueRO.Value;
                if (target == Entity.Null || !transforms.HasComponent(target))
                    continue;

                if (!_targets.TryGetComponent(target, out var targets))
                    continue;

                if (!EntityLinkResolver.TryResolve(
                        target, targets, cfg.ReadRootFrom, cfg.ConsumerLinkKey, _sources, _entries, out var consumer))
                    continue;

                if (!playerIds.TryGetComponent(consumer, out var playerId))
                    continue;

                var provider = registry[playerId.Value];
                if (provider == Entity.Null || !axisBuffers.HasBuffer(provider))
                    continue;

                if (!synthetic.HasComponent(provider))
                    continue;

                if (!reg.KeyToSlot.TryGetValue(cfg.FieldKey, out var slotIndex))
                    continue;

                var field = reg.Front(new FieldId(slotIndex));
                if (!field.IsCreated)
                    continue;

                var transform = transforms[target];
                var world = transform.Position + math.rotate(transform.Rotation, cfg.LocalOffset);
                var projected = basis.ToGridSpace(world);
                var cell = new int2(
                    (int)math.floor(projected.x / cellSize),
                    (int)math.floor(projected.y / cellSize));

                var gradient = FieldGradient.Normalized(cfg.Bias.Sign() * FieldGradient.Ascent(field.AsReader(), cell));
                if (math.all(gradient == float2.zero))
                    continue;

                Accumulate(axisBuffers[provider], cfg.ActionId, gradient * (cfg.Gain * weight.ValueRO.Value));
            }
        }

        // Accumulate, don't overwrite: multiple FlowInput clips (e.g. a crossfade) can drive the
        // same provider action in one frame. Summing the weighted contributions blends them and is
        // order-independent, where last-writer-wins would collapse to one clip and depend on the
        // non-deterministic query iteration order. The buffer is cleared once per frame above.
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