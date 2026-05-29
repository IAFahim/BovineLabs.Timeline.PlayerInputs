using BovineLabs.Core.Extensions;
using BovineLabs.Core.Iterators;
using BovineLabs.Reaction.Conditions;
using BovineLabs.Reaction.Data.Conditions;
using BovineLabs.Reaction.Data.Core;
using BovineLabs.Timeline.Data;
using BovineLabs.Timeline.EntityLinks;
using BovineLabs.Timeline.EntityLinks.Data;
using BovineLabs.Timeline.PlayerInputs.Data;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace BovineLabs.Timeline.PlayerInputs
{
    [UpdateInGroup(typeof(TimelineComponentAnimationGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation)]
    public partial struct InputEventsSystem : ISystem
    {
        private ComponentLookup<Targets> _targetsLookup;
        private UnsafeComponentLookup<EntityLinkSource> _sources;
        private UnsafeBufferLookup<EntityLinkEntry> _entries;
        private BufferLookup<InputAxis> _axes;
        private ComponentLookup<PlayerId> _playerIds;
        private ConditionEventWriter.Lookup _writers;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<InputEventsConfig>();
            state.RequireForUpdate<InputRegistry>();
            _targetsLookup = state.GetComponentLookup<Targets>(true);
            _sources = state.GetUnsafeComponentLookup<EntityLinkSource>(true);
            _entries = state.GetUnsafeBufferLookup<EntityLinkEntry>(true);
            _axes = state.GetBufferLookup<InputAxis>(true);
            _playerIds = state.GetComponentLookup<PlayerId>(true);
            _writers.Create(ref state);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            _targetsLookup.Update(ref state);
            _sources.Update(ref state);
            _entries.Update(ref state);
            _axes.Update(ref state);
            _playerIds.Update(ref state);
            _writers.Update(ref state);

            var registry = SystemAPI.GetSingleton<InputRegistry>();

            state.Dependency = new ApplyJob
            {
                TargetsLookup = _targetsLookup,
                Sources = _sources,
                Entries = _entries,
                Registry = registry.ProviderByPlayer,
                Axes = _axes,
                PlayerIds = _playerIds,
                Writers = _writers
            }.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(ClipActive))]
        private partial struct ApplyJob : IJobEntity
        {
            [ReadOnly] public ComponentLookup<Targets> TargetsLookup;
            [ReadOnly] public UnsafeComponentLookup<EntityLinkSource> Sources;
            [ReadOnly] public UnsafeBufferLookup<EntityLinkEntry> Entries;

            [ReadOnly] [NativeDisableContainerSafetyRestriction] public NativeArray<Entity> Registry;
            [ReadOnly] public BufferLookup<InputAxis> Axes;
            [ReadOnly] [NativeDisableContainerSafetyRestriction] public ComponentLookup<PlayerId> PlayerIds;

            [NativeDisableParallelForRestriction] public ConditionEventWriter.Lookup Writers;

            private void Execute(in TrackBinding binding, in InputEventsConfig config, ref InputEventsState state)
            {
                var targetEntity = binding.Value;
                if (targetEntity == Entity.Null) return;
                if (!TargetsLookup.TryGetComponent(targetEntity, out var targets)) return;

                if (!EntityLinkResolver.TryResolve(
                        targetEntity, targets, config.ReadRootFrom, config.ConsumerLinkKey,
                        Sources, Entries, out var consumer)) return;

                if (!PlayerIds.TryGetComponent(consumer, out var pid)) return;
                if (!InputAccess.TryGetAxes(Registry, Axes, pid.Value, out var axesBuf)) return;

                var hasInput = false;
                for (var i = 0; i < axesBuf.Length; i++)
                {
                    if (axesBuf[i].ActionId != config.ActionId) continue;
                    hasInput = math.lengthsq(axesBuf[i].Value) > 0.0001f;
                    break;
                }

                var risingEdge = hasInput && !state.WasInputActive;
                var fallingEdge = !hasInput && state.WasInputActive;

                if (risingEdge && config.OnInputStart != ConditionKey.Null &&
                    TryResolveTarget(config.EventRouteTo, config.EventRouteLinkKey, targetEntity, targets,
                        out var startTarget))
                    if (Writers.TryGet(startTarget, out var w)) w.Trigger(config.OnInputStart, 1);

                if (fallingEdge && config.OnInputEnd != ConditionKey.Null &&
                    TryResolveTarget(config.EventRouteTo, config.EventRouteLinkKey, targetEntity, targets,
                        out var endTarget))
                    if (Writers.TryGet(endTarget, out var w)) w.Trigger(config.OnInputEnd, 1);

                state.WasInputActive = hasInput;
            }

            private bool TryResolveTarget(Target mode, ushort linkKey, Entity self, in Targets targets, out Entity target)
            {
                if (mode == Target.Self)
                {
                    target = self;
                    return true;
                }

                target = EntityLinkResolver.TryResolve(self, targets, mode, linkKey, Sources, Entries, out var t) ? t : Entity.Null;
                return target != Entity.Null;
            }
        }
    }
}
