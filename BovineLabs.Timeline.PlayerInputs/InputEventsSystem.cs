using BovineLabs.Core.Collections;
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
using Unity.Jobs;
using Unity.Mathematics;

namespace BovineLabs.Timeline.PlayerInputs
{
    [UpdateInGroup(typeof(TimelineComponentAnimationGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation)]
    public partial struct InputEventsSystem : ISystem
    {
        private UnsafeComponentLookup<Targets> _targetsLookup;
        private UnsafeComponentLookup<EntityLinkSource> _sources;
        private UnsafeBufferLookup<EntityLinkEntry> _entries;
        private BufferLookup<InputAxis> _axes;
        private ComponentLookup<InputState> _states;
        private ComponentLookup<PlayerId> _playerIds;

        private NativeParallelMultiHashMapFallback<Entity, EventAmount> _eventChanges;
        private NativeList<Entity> _uniqueKeys;
        private ConditionEventWriter.Lookup _writers;

        private EntityQuery _activeClipQuery;
        private EntityQuery _deactivatedClipQuery;

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            if (_uniqueKeys.IsCreated)
            {
                _eventChanges.Dispose();
                _uniqueKeys.Dispose();
            }
        }

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<InputEventsConfig>();
            state.RequireForUpdate<InputRegistry>();
            _targetsLookup = state.GetUnsafeComponentLookup<Targets>(true);
            _sources = state.GetUnsafeComponentLookup<EntityLinkSource>(true);
            _entries = state.GetUnsafeBufferLookup<EntityLinkEntry>(true);
            _axes = state.GetBufferLookup<InputAxis>(true);
            _states = state.GetComponentLookup<InputState>(true);
            _playerIds = state.GetComponentLookup<PlayerId>(true);

            _eventChanges = new NativeParallelMultiHashMapFallback<Entity, EventAmount>(64, Allocator.Persistent);
            _uniqueKeys = new NativeList<Entity>(64, Allocator.Persistent);
            _writers.Create(ref state);

            _activeClipQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<ClipActive, InputEventsConfig, InputEventsState>()
                .Build(ref state);
            _deactivatedClipQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<ClipActivePrevious, InputEventsConfig, InputEventsState>()
                .WithNone<ClipActive>()
                .Build(ref state);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            _targetsLookup.Update(ref state);
            _sources.Update(ref state);
            _entries.Update(ref state);
            _axes.Update(ref state);
            _states.Update(ref state);
            _playerIds.Update(ref state);
            _writers.Update(ref state);

            var registry = SystemAPI.GetSingleton<InputRegistry>();

            var capacity = math.max(1, _activeClipQuery.CalculateEntityCount() +
                                       _deactivatedClipQuery.CalculateEntityCount());
            var uniqueKeySet = new NativeParallelHashSet<Entity>(capacity, state.WorldUpdateAllocator);

            state.Dependency = new InitJob().ScheduleParallel(state.Dependency);

            state.Dependency = new GatherJob
            {
                EventChanges = _eventChanges.AsWriter(),
                UniqueKeys = uniqueKeySet.AsParallelWriter(),
                TargetsLookup = _targetsLookup,
                Sources = _sources,
                Entries = _entries,
                Registry = registry.ProviderByPlayer,
                Axes = _axes,
                States = _states,
                PlayerIds = _playerIds
            }.ScheduleParallel(state.Dependency);

            state.Dependency = new DeactivateJob
            {
                EventChanges = _eventChanges.AsWriter(),
                UniqueKeys = uniqueKeySet.AsParallelWriter(),
                TargetsLookup = _targetsLookup,
                Sources = _sources,
                Entries = _entries
            }.ScheduleParallel(state.Dependency);

            state.Dependency = _eventChanges.Apply(state.Dependency, out var reader);

            state.Dependency = new CollectEventKeysJob
            {
                UniqueKeys = _uniqueKeys,
                UniqueKeySet = uniqueKeySet
            }.Schedule(state.Dependency);

            state.Dependency = new TriggerEventsJob
            {
                Keys = _uniqueKeys.AsDeferredJobArray(),
                GroupChanges = reader,
                Writers = _writers
            }.Schedule(_uniqueKeys, 64, state.Dependency);

            state.Dependency = _eventChanges.Clear(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(ClipActive))]
        [WithNone(typeof(ClipActivePrevious))]
        private partial struct InitJob : IJobEntity
        {
            private void Execute(ref InputEventsState state)
            {
                state.WasInputActive = false;
            }
        }

        [BurstCompile]
        [WithAll(typeof(ClipActive))]
        private partial struct GatherJob : IJobEntity
        {
            [ReadOnly] public UnsafeComponentLookup<Targets> TargetsLookup;
            [ReadOnly] public UnsafeComponentLookup<EntityLinkSource> Sources;
            [ReadOnly] public UnsafeBufferLookup<EntityLinkEntry> Entries;

            [ReadOnly] [NativeDisableContainerSafetyRestriction]
            public NativeArray<Entity> Registry;

            [ReadOnly] public BufferLookup<InputAxis> Axes;

            [ReadOnly] [NativeDisableContainerSafetyRestriction]
            public ComponentLookup<InputState> States;

            [ReadOnly] [NativeDisableContainerSafetyRestriction]
            public ComponentLookup<PlayerId> PlayerIds;

            public NativeParallelMultiHashMapFallback<Entity, EventAmount>.ParallelWriter EventChanges;
            public NativeParallelHashSet<Entity>.ParallelWriter UniqueKeys;

            private void Execute(in TrackBinding binding, in InputEventsConfig config, ref InputEventsState state)
            {
                var targetEntity = binding.Value;
                if (targetEntity == Entity.Null) return;
                if (!TargetsLookup.TryGetComponent(targetEntity, out var targets)) return;

                if (!EntityLinkResolver.TryResolve(
                        targetEntity, targets, config.ReadRootFrom, config.ConsumerLinkKey,
                        Sources, Entries, out var consumer)) return;

                if (!PlayerIds.TryGetComponent(consumer, out var pid)) return;

                var hasInput = false;
                var foundAxis = false;
                if (InputAccess.TryGetAxes(Registry, Axes, pid.Value, out var axesBuf))
                {
                    for (var i = 0; i < axesBuf.Length; i++)
                    {
                        if (axesBuf[i].ActionId != config.ActionId) continue;
                        hasInput = math.lengthsq(axesBuf[i].Value) > 0.0001f;
                        foundAxis = true;
                        break;
                    }
                }

                // Button-type actions never appear in the axis buffer — the bridge only writes axes.
                // Fall back to the held bit in InputState so start/end edges fire for buttons too.
                if (!foundAxis && InputAccess.TryGetState(Registry, States, pid.Value, out var inputState))
                {
                    hasInput = inputState.Held[config.ActionId];
                }

                var risingEdge = hasInput && !state.WasInputActive;
                var fallingEdge = !hasInput && state.WasInputActive;

                if (risingEdge && config.OnInputStart != ConditionKey.Null &&
                    InputRouting.TryResolveRoute(targetEntity, targets, config.EventRouteTo, config.EventRouteLinkKey,
                        Sources, Entries, out var startTarget))
                {
                    EventChanges.Add(startTarget, new EventAmount(config.OnInputStart, 1));
                    UniqueKeys.Add(startTarget);
                }

                if (fallingEdge && config.OnInputEnd != ConditionKey.Null &&
                    InputRouting.TryResolveRoute(targetEntity, targets, config.EventRouteTo, config.EventRouteLinkKey,
                        Sources, Entries, out var endTarget))
                {
                    EventChanges.Add(endTarget, new EventAmount(config.OnInputEnd, 1));
                    UniqueKeys.Add(endTarget);
                }

                state.WasInputActive = hasInput;
            }
        }

        [BurstCompile]
        [WithAll(typeof(ClipActivePrevious))]
        [WithNone(typeof(ClipActive))]
        private partial struct DeactivateJob : IJobEntity
        {
            [ReadOnly] public UnsafeComponentLookup<Targets> TargetsLookup;
            [ReadOnly] public UnsafeComponentLookup<EntityLinkSource> Sources;
            [ReadOnly] public UnsafeBufferLookup<EntityLinkEntry> Entries;

            public NativeParallelMultiHashMapFallback<Entity, EventAmount>.ParallelWriter EventChanges;
            public NativeParallelHashSet<Entity>.ParallelWriter UniqueKeys;

            private void Execute(in TrackBinding binding, in InputEventsConfig config, ref InputEventsState state)
            {
                if (!state.WasInputActive) return;
                state.WasInputActive = false;

                if (config.OnInputEnd == ConditionKey.Null) return;

                var targetEntity = binding.Value;
                if (targetEntity == Entity.Null) return;
                if (!TargetsLookup.TryGetComponent(targetEntity, out var targets)) return;

                if (!InputRouting.TryResolveRoute(targetEntity, targets, config.EventRouteTo, config.EventRouteLinkKey,
                        Sources, Entries, out var endTarget)) return;

                EventChanges.Add(endTarget, new EventAmount(config.OnInputEnd, 1));
                UniqueKeys.Add(endTarget);
            }
        }
    }
}