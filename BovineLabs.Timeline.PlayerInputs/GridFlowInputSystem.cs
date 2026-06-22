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
        private ComponentLookup<PlayerId> _playerIds;
        private ComponentLookup<LocalTransform> _transforms;
        private BufferLookup<InputAxis> _axisBuffers;
        private ComponentLookup<SyntheticProviderTag> _synthetic;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<InfluenceGridSettings>();
            state.RequireForUpdate<FieldRegistrySingleton>();
            state.RequireForUpdate<InputRegistry>();
            state.RequireForUpdate<FlowInputConfig>();

            _targets = state.GetUnsafeComponentLookup<Targets>(true);
            _sources = state.GetUnsafeComponentLookup<EntityLinkSource>(true);
            _entries = state.GetUnsafeBufferLookup<EntityLinkEntry>(true);
            _playerIds = state.GetComponentLookup<PlayerId>(true);
            _transforms = state.GetComponentLookup<LocalTransform>(true);
            _axisBuffers = state.GetBufferLookup<InputAxis>(false);
            _synthetic = state.GetComponentLookup<SyntheticProviderTag>(true);
        }

        public void OnUpdate(ref SystemState state)
        {
            state.CompleteDependency();

            _targets.Update(ref state);
            _sources.Update(ref state);
            _entries.Update(ref state);
            _playerIds.Update(ref state);
            _transforms.Update(ref state);
            _axisBuffers.Update(ref state);
            _synthetic.Update(ref state);

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

            foreach (var axes in SystemAPI.Query<DynamicBuffer<InputAxis>>()
                         .WithAll<ProviderTag, SyntheticProviderTag>())
                axes.Clear();

            foreach (var (config, binding, weight) in
                     SystemAPI.Query<RefRO<FlowInputConfig>, RefRO<TrackBinding>, RefRO<ClipWeight>>()
                         .WithAll<ClipActive>())
            {
                var cfg = config.ValueRO;

                var target = binding.ValueRO.Value;
                if (target == Entity.Null || !_transforms.HasComponent(target))
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

                if (!reg.KeyToSlot.TryGetValue(cfg.FieldKey, out var slotIndex))
                    continue;

                var field = reg.Front(new FieldId(slotIndex));
                if (!field.IsCreated)
                    continue;

                if (cfg.ActionId == byte.MaxValue)
                    continue;

                var cell = GridProjection.ToCell(_transforms[target], cfg.LocalOffset, basis, cellSize);

                var gradient = FieldGradient.Normalized(cfg.Bias.Sign() * FieldGradient.Ascent(field.AsReader(), cell));
                if (math.all(gradient == float2.zero))
                    continue;

                Accumulate(_axisBuffers[provider], cfg.ActionId, gradient * (cfg.Gain * weight.ValueRO.Value));
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