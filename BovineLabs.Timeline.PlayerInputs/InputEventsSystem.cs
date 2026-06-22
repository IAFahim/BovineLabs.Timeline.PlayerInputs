using System;
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
using Unity.Burst.CompilerServices;
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
        private ComponentLookup<PlayerId> _playerIds;

        private NativeParallelMultiHashMapFallback<Entity, EventAmount> _eventChanges;
        private NativeList<Entity> _uniqueKeys;
        private ConditionEventWriter.Lookup _writers;

        // Upper bounds on the distinct route-target keys produced this frame: GatherJob writes one per
        // active clip (rising/falling edge) and DeactivateJob one per clip that deactivated while held.
        // Sizing the per-frame ParallelWriter set from these counts removes the fixed 64-entity ceiling
        // that ThrowFull()'d the parallel set at scale (F5).
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
            _playerIds = state.GetComponentLookup<PlayerId>(true);

            _eventChanges = new NativeParallelMultiHashMapFallback<Entity, EventAmount>(64, Allocator.Persistent);
            _uniqueKeys = new NativeList<Entity>(64, Allocator.Persistent);
            _writers.Create(ref state);

            // The entity sets GatherJob and DeactivateJob iterate; their counts bound the distinct keys
            // each can add, so they size the per-frame ParallelWriter set's fixed capacity (F5).
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
            _playerIds.Update(ref state);
            _writers.Update(ref state);

            var registry = SystemAPI.GetSingleton<InputRegistry>();

            // A NativeParallelHashSet written through a ParallelWriter does NOT grow, so size it to this
            // frame's actual upper bound: one key per active clip (GatherJob) plus one per clip that
            // deactivated while held (DeactivateJob). A fixed cap would ThrowFull at scale (F5).
            var capacity = math.max(1, _activeClipQuery.CalculateEntityCount() +
                _deactivatedClipQuery.CalculateEntityCount());
            var uniqueKeySet = new NativeParallelHashSet<Entity>(capacity, state.WorldUpdateAllocator);

            // Edge detection is stateful (WasInputActive). Reset it on the first active frame so a
            // clip re-activated while the input is still held emits a fresh start edge instead of
            // inheriting the previous activation's state (missed OnInputStart / spurious OnInputEnd).
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
                PlayerIds = _playerIds
            }.ScheduleParallel(state.Dependency);

            // The falling edge that fires OnInputEnd is only observable while ClipActive (GatherJob is gated
            // on it). When a clip deactivates with the input still held, that edge is never seen and the end
            // event is lost; a later re-activation then emits a second unbalanced OnInputStart. Flush the
            // pending end here on the clip's exit frame so start/end stays balanced (F6).
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

        // Falling clip edge: ClipActivePrevious set, ClipActive cleared. A clip that deactivated this frame
        // while its input was still held never had its falling input edge observed by GatherJob (which is
        // gated on ClipActive), so flush the pending OnInputEnd here and clear the latch so a later
        // re-activation starts balanced (F6). Mirrors GatherJob's end-edge resolution exactly.
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