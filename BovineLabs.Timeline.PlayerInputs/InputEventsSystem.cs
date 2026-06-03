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
using Unity.Jobs;
using System;

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
        
        private BovineLabs.Core.Collections.NativeParallelMultiHashMapFallback<Entity, EventAmount> _eventChanges;
        private NativeParallelHashSet<Entity> _uniqueKeySet;
        private NativeList<Entity> _uniqueKeys;
        private ConditionEventWriter.Lookup _writers;


        [BurstCompile]
        
        public void OnDestroy(ref SystemState state)
        {
            if (_uniqueKeys.IsCreated)
            {
                _eventChanges.Dispose();
                _uniqueKeySet.Dispose();
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
            
            _eventChanges = new BovineLabs.Core.Collections.NativeParallelMultiHashMapFallback<Entity, EventAmount>(64, Allocator.Persistent);
            _uniqueKeySet = new NativeParallelHashSet<Entity>(64, Allocator.Persistent);
            _uniqueKeys = new NativeList<Entity>(64, Allocator.Persistent);
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

            _uniqueKeySet.Clear();

            state.Dependency = new GatherJob
            {
                EventChanges = _eventChanges.AsWriter(),
                UniqueKeys = _uniqueKeySet.AsParallelWriter(),
                TargetsLookup = _targetsLookup,
                Sources = _sources,
                Entries = _entries,
                Registry = registry.ProviderByPlayer,
                Axes = _axes,
                PlayerIds = _playerIds
            }.ScheduleParallel(state.Dependency);

            state.Dependency = _eventChanges.Apply(state.Dependency, out var reader);

            state.Dependency = new GetKeysJob
            {
                UniqueKeys = _uniqueKeys,
                UniqueKeySet = _uniqueKeySet
            }.Schedule(state.Dependency);

            state.Dependency = new ApplyJob
            {
                Keys = _uniqueKeys.AsDeferredJobArray(),
                GroupChanges = reader,
                Writers = _writers
            }.Schedule(_uniqueKeys, 64, state.Dependency);

            state.Dependency = _eventChanges.Clear(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(ClipActive))]
        private partial struct GatherJob : IJobEntity
        {
            [ReadOnly] public UnsafeComponentLookup<Targets> TargetsLookup;
            [ReadOnly] public UnsafeComponentLookup<EntityLinkSource> Sources;
            [ReadOnly] public UnsafeBufferLookup<EntityLinkEntry> Entries;

            [ReadOnly] [NativeDisableContainerSafetyRestriction] public NativeArray<Entity> Registry;
            [ReadOnly] public BufferLookup<InputAxis> Axes;
            [ReadOnly] [NativeDisableContainerSafetyRestriction] public ComponentLookup<PlayerId> PlayerIds;

            
            public BovineLabs.Core.Collections.NativeParallelMultiHashMapFallback<Entity, EventAmount>.ParallelWriter EventChanges;
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
                    TryResolveTarget(config.EventRouteTo, config.EventRouteLinkKey, targetEntity, targets,
                        out var startTarget))
                    
                {
                    EventChanges.Add(startTarget, new EventAmount(config.OnInputStart, 1));
                    UniqueKeys.Add(startTarget);
                }

                if (fallingEdge && config.OnInputEnd != ConditionKey.Null &&
                    TryResolveTarget(config.EventRouteTo, config.EventRouteLinkKey, targetEntity, targets,
                        out var endTarget))
                    
                {
                    EventChanges.Add(endTarget, new EventAmount(config.OnInputEnd, 1));
                    UniqueKeys.Add(endTarget);
                }

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

        [BurstCompile]
        private struct GetKeysJob : IJob
        {
            public NativeList<Entity> UniqueKeys;
            [ReadOnly] public NativeParallelHashSet<Entity> UniqueKeySet;

            public void Execute()
            {
                UniqueKeys.Clear();
                foreach (var key in UniqueKeySet)
                    UniqueKeys.Add(key);
            }
        }

        [BurstCompile]
        private struct ApplyJob : IJobParallelForDefer
        {
            [ReadOnly] public NativeArray<Entity> Keys;
            [ReadOnly] public NativeParallelMultiHashMap<Entity, EventAmount>.ReadOnly GroupChanges;
            [NativeDisableParallelForRestriction] public ConditionEventWriter.Lookup Writers;

            public void Execute(int index)
            {
                var key = Keys[index];
                if (Unity.Burst.CompilerServices.Hint.Unlikely(!Writers.TryGet(key, out var writer))) return;

                var values = new FixedList4096Bytes<EventAmount>();

                if (GroupChanges.TryGetFirstValue(key, out var value, out var it))
                {
                    AddOrAccumulate(ref values, value, ref writer);

                    while (GroupChanges.TryGetNextValue(out value, ref it))
                        AddOrAccumulate(ref values, value, ref writer);
                }

                foreach (var e in values) writer.Trigger(e.Event, e.Amount);
            }

            private static void AddOrAccumulate(ref FixedList4096Bytes<EventAmount> values, EventAmount value,
                ref ConditionEventWriter writer)
            {
                for (var i = 0; i < values.Length; i++)
                    if (values[i].Event.Equals(value.Event))
                    {
                        var existing = values[i];
                        existing.Amount += value.Amount;
                        values[i] = existing;
                        return;
                    }

                if (values.Length < values.Capacity)
                {
                    values.Add(value);
                    return;
                }

                writer.Trigger(value.Event, value.Amount);
            }
        }

        private struct EventAmount : IEquatable<EventAmount>
        {
            public readonly ConditionKey Event;
            public int Amount;

            public EventAmount(ConditionKey evt, int amount)
            {
                Event = evt;
                Amount = amount;
            }

            public bool Equals(EventAmount other)
            {
                return Event.Equals(other.Event);
            }

            public override int GetHashCode()
            {
                return Event.GetHashCode();
            }
        }
    }
}
