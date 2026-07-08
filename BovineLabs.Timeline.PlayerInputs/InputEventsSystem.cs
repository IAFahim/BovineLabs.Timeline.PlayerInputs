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
    // Pure enter-frame seed for InputEventsState.WasInputActive. Kept separate so the level-vs-edge decision is
    // unit-testable without the ECS/link scaffolding.
    internal static class InputEventsLogic
    {
        // triggerIfAlreadyHeld: seed false so an already-held input registers as a rising edge (OnInputStart fires).
        // !triggerIfAlreadyHeld: seed to the current level so a held input is "already started" and only a fresh
        // press after activation fires OnInputStart.
        public static bool SeedWasInputActive(bool triggerIfAlreadyHeld, bool hasInput)
        {
            return !triggerIfAlreadyHeld && hasInput;
        }

        // Deactivate edge: when a clip that had an active input turns off, OnInputEnd must fire EXACTLY once.
        // Returns true only on the transition where the end event should be dispatched, and latches
        // wasInputActive to false so a second deactivate frame (a lingering ClipActivePrevious) cannot re-fire.
        public static bool ConsumeDeactivateEnd(ref bool wasInputActive)
        {
            if (!wasInputActive)
            {
                return false;
            }

            wasInputActive = false;
            return true;
        }
    }

    [UpdateInGroup(typeof(TimelineComponentAnimationGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation |
                       WorldSystemFilterFlags.ServerSimulation)]
    public partial struct InputEventsSystem : ISystem
    {
        private UnsafeComponentLookup<Targets> _targetsLookup;
        private UnsafeComponentLookup<EntityLinkSource> _sources;
        private UnsafeBufferLookup<EntityLinkEntry> _entries;
        private BufferLookup<InputAxis> _axes;
        private ComponentLookup<InputState> _states;
        private ComponentLookup<PlayerId> _playerIds;
        private ComponentLookup<PlayerOverride> _overrides;
        private ComponentLookup<ClipActivePrevious> _clipActivePrevious;

        private ConditionEventDispatch _dispatch;

        private EntityQuery _activeClipQuery;
        private EntityQuery _deactivatedClipQuery;

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            _dispatch.Dispose();
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
            _overrides = state.GetComponentLookup<PlayerOverride>(true);
            _clipActivePrevious = state.GetComponentLookup<ClipActivePrevious>(true);

            _dispatch.Create(ref state);

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
            _overrides.Update(ref state);
            _clipActivePrevious.Update(ref state);
            _dispatch.Update(ref state);

            var capacity = math.max(1, _activeClipQuery.CalculateEntityCount() +
                                       _deactivatedClipQuery.CalculateEntityCount());
            var uniqueKeySet = new NativeParallelHashSet<Entity>(capacity, state.WorldUpdateAllocator);

            state.Dependency = new GatherJob
            {
                EventChanges = _dispatch.EventWriter,
                UniqueKeys = uniqueKeySet.AsParallelWriter(),
                TargetsLookup = _targetsLookup,
                Sources = _sources,
                Entries = _entries,
                Slots = SystemAPI.GetSingletonBuffer<ProviderSlot>(true),
                Axes = _axes,
                States = _states,
                PlayerIds = _playerIds,
                Overrides = _overrides,
                ClipActivePrevious = _clipActivePrevious
            }.ScheduleParallel(state.Dependency);

            state.Dependency = new DeactivateJob
            {
                EventChanges = _dispatch.EventWriter,
                UniqueKeys = uniqueKeySet.AsParallelWriter(),
                TargetsLookup = _targetsLookup,
                Sources = _sources,
                Entries = _entries
            }.ScheduleParallel(state.Dependency);

            state.Dependency = _dispatch.Flush(uniqueKeySet, state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(ClipActive))]
        private partial struct GatherJob : IJobEntity
        {
            [ReadOnly] public UnsafeComponentLookup<Targets> TargetsLookup;
            [ReadOnly] public UnsafeComponentLookup<EntityLinkSource> Sources;
            [ReadOnly] public UnsafeBufferLookup<EntityLinkEntry> Entries;

            [ReadOnly] public DynamicBuffer<ProviderSlot> Slots;

            [ReadOnly] public BufferLookup<InputAxis> Axes;

            [ReadOnly] public ComponentLookup<InputState> States;

            [ReadOnly] public ComponentLookup<PlayerId> PlayerIds;

            [ReadOnly] public ComponentLookup<PlayerOverride> Overrides;

            [ReadOnly] public ComponentLookup<ClipActivePrevious> ClipActivePrevious;

            public NativeParallelMultiHashMapFallback<Entity, EventAmount>.ParallelWriter EventChanges;
            public NativeParallelHashSet<Entity>.ParallelWriter UniqueKeys;

            private void Execute(Entity entity, in TrackBinding binding, in InputEventsConfig config,
                ref InputEventsState state)
            {
                var targetEntity = binding.Value;
                if (targetEntity == Entity.Null) return;
                if (!TargetsLookup.TryGetComponent(targetEntity, out var targets)) return;

                if (!config.Consumer.TryResolve(targetEntity, targets, Sources, Entries, out var consumer)) return;

                if (!PlayerIds.TryGetComponent(consumer, out var pid)) return;

                var hasInput = false;
                var foundAxis = false;
                if (InputAccess.TryGetAxes(Slots, Axes, Overrides, consumer, pid.Value, out var axesBuf))
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
                if (!foundAxis && InputAccess.TryGetState(Slots, States, Overrides, consumer, pid.Value, out var inputState))
                {
                    hasInput = inputState.Held[config.ActionId];
                }

                // Enter frame = ClipActive on but ClipActivePrevious not yet mirrored (ClipActivePreviousSystem runs
                // OrderLast). Seed WasInputActive so an already-held input either fires OnInputStart immediately
                // (TriggerIfAlreadyHeld) or is treated as already-started (a fresh press is then required).
                var isEnter = !(ClipActivePrevious.HasComponent(entity) &&
                                ClipActivePrevious.IsComponentEnabled(entity));
                if (isEnter)
                    state.WasInputActive = InputEventsLogic.SeedWasInputActive(config.TriggerIfAlreadyHeld, hasInput);

                var risingEdge = hasInput && !state.WasInputActive;
                var fallingEdge = !hasInput && state.WasInputActive;

                if (risingEdge && !config.OnInputStart.Equals(ConditionKey.Null) &&
                    InputRouting.TryResolveRoute(targetEntity, targets, config.EventRoute,
                        Sources, Entries, out var startTarget))
                {
                    EventChanges.Add(startTarget, new EventAmount(config.OnInputStart, 1));
                    UniqueKeys.Add(startTarget);
                }

                if (fallingEdge && !config.OnInputEnd.Equals(ConditionKey.Null) &&
                    InputRouting.TryResolveRoute(targetEntity, targets, config.EventRoute,
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
                if (!InputEventsLogic.ConsumeDeactivateEnd(ref state.WasInputActive)) return;

                if (config.OnInputEnd.Equals(ConditionKey.Null)) return;

                var targetEntity = binding.Value;
                if (targetEntity == Entity.Null) return;
                if (!TargetsLookup.TryGetComponent(targetEntity, out var targets)) return;

                if (!InputRouting.TryResolveRoute(targetEntity, targets, config.EventRoute,
                        Sources, Entries, out var endTarget)) return;

                EventChanges.Add(endTarget, new EventAmount(config.OnInputEnd, 1));
                UniqueKeys.Add(endTarget);
            }
        }
    }
}