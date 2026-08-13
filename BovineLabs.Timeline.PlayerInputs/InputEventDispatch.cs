using System;
using BovineLabs.Core.Collections;
using BovineLabs.Core.Iterators;
using BovineLabs.Reaction.Conditions;
using BovineLabs.Reaction.Data.Conditions;
using BovineLabs.Reaction.Data.Core;
using BovineLabs.Timeline.EntityLinks;
using BovineLabs.Timeline.EntityLinks.Data;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace BovineLabs.Timeline.PlayerInputs
{
    // Shared condition-event dispatch pipeline, previously hand-rolled identically in CommandSequenceSystem and
    // InputEventsSystem. A gather job accumulates (routeTarget -> EventAmount) pairs into EventWriter; one Flush per
    // frame applies the fixed-capacity fallback map, collects the unique target keys, fans the accumulated amounts
    // out to each target's ConditionEventWriter (TriggerEventsJob), then clears the map. Owning it here keeps the
    // fixed-64-overflow + Clear-race handling in a single place instead of duplicated and kept in lockstep by hand.
    internal struct ConditionEventDispatch
    {
        private NativeParallelMultiHashMapFallback<Entity, EventAmount> eventChanges;
        private NativeList<Entity> uniqueKeys;
        private ConditionEventWriter.Lookup writers;
        private ConditionEventWriter.SingletonData writersSingletonData;
        private EntityQuery allocatorQuery;

        public NativeParallelMultiHashMapFallback<Entity, EventAmount>.ParallelWriter EventWriter =>
            this.eventChanges.AsWriter();

        public void Create(ref SystemState state)
        {
            this.eventChanges = new NativeParallelMultiHashMapFallback<Entity, EventAmount>(64, Allocator.Persistent);
            this.uniqueKeys = new NativeList<Entity>(64, Allocator.Persistent);
            this.writersSingletonData.Create(ref state);
            this.writers.Create(ref state);

            // Condition events exist only where Reaction writes them: ConditionsSystemGroup and its
            // ConditionWriteEventsGroup are both WorldSystemFilter(Worlds.ServerLocal), so
            // ConditionEventWriteSystem - the sole creator of ConditionEventPayloadAllocator - never runs in a
            // client world. The dispatching systems advertise ClientSimulation too, so an ungated client-world
            // update throws inside Burst on GetSingleton.
            //
            // Gated here, on the dispatch, rather than with RequireForUpdate on the owning systems. Those systems
            // do more than dispatch - CommandSequenceSystem advances CommandSequenceState and InputHistory, and
            // AccumulateCommandMaskJob reads commandState.IsCompleted on the CLIENT to decide when to stop
            // reserving an input mask. RequireForUpdate would have switched all of that off too, and the mask
            // would never release. Narrowing this to what genuinely has no consumer off-server keeps the rest
            // running everywhere.
            this.allocatorQuery = state.GetEntityQuery(ComponentType.ReadOnly<ConditionEventPayloadAllocator>());
        }

        public void Dispose()
        {
            if (this.uniqueKeys.IsCreated)
            {
                this.eventChanges.Dispose();
                this.uniqueKeys.Dispose();
            }
        }

        // True where Reaction is actually writing condition events, i.e. server and local worlds. False on a
        // client, where the gather jobs still run and still accumulate, but Flush drops the result instead of
        // handing it to writers that do not exist.
        public bool HasWriters => !this.allocatorQuery.IsEmpty;

        // Refresh the writer lookup. Call once at the top of OnUpdate before scheduling the gather job(s).
        public void Update(ref SystemState state)
        {
            if (!this.HasWriters)
            {
                return;
            }

            this.writers.Update(ref state, this.writersSingletonData);
        }

        // uniqueKeySet: a per-frame set (sized to the live clip count) that the gather job(s) populated with the
        // same target keys they wrote to EventWriter. Returns the dependency after the trigger + clear jobs.
        public JobHandle Flush(NativeParallelHashSet<Entity> uniqueKeySet, JobHandle dep)
        {
            if (!this.HasWriters)
            {
                // Still clear: the gather jobs wrote into the map this frame and it is reused next frame.
                return this.eventChanges.Clear(dep);
            }

            dep = this.eventChanges.Apply(dep, out var reader);

            dep = new CollectEventKeysJob
            {
                UniqueKeys = this.uniqueKeys,
                UniqueKeySet = uniqueKeySet,
            }.Schedule(dep);

            dep = new TriggerEventsJob
            {
                Keys = this.uniqueKeys.AsDeferredJobArray(),
                GroupChanges = reader,
                Writers = this.writers,
            }.Schedule(this.uniqueKeys, 64, dep);

            dep = this.eventChanges.Clear(dep);
            return dep;
        }
    }

    internal struct EventAmount : IEquatable<EventAmount>
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

    [BurstCompile]
    internal struct CollectEventKeysJob : IJob
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
    internal struct TriggerEventsJob : IJobParallelForDefer
    {
        [ReadOnly] public NativeArray<Entity> Keys;
        [ReadOnly] public NativeParallelMultiHashMap<Entity, EventAmount>.ReadOnly GroupChanges;
        [NativeDisableParallelForRestriction] public ConditionEventWriter.Lookup Writers;

        public void Execute(int index)
        {
            var key = Keys[index];
            if (Hint.Unlikely(!Writers.TryGet(key, out var writer))) return;

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
            if (EventAccumulation.TryMerge(ref values, value) == MergeResult.Overflow)
                writer.Trigger(value.Event, value.Amount);
        }
    }

    internal enum MergeResult
    {
        Merged,
        Appended,
        Overflow,
    }

    internal static class EventAccumulation
    {
        public static MergeResult TryMerge(ref FixedList4096Bytes<EventAmount> values, in EventAmount value)
        {
            for (var i = 0; i < values.Length; i++)
                if (values[i].Event.Equals(value.Event))
                {
                    var existing = values[i];
                    existing.Amount += value.Amount;
                    values[i] = existing;
                    return MergeResult.Merged;
                }

            if (values.Length < values.Capacity)
            {
                values.Add(value);
                return MergeResult.Appended;
            }

            return MergeResult.Overflow;
        }
    }

    internal static class InputRouting
    {
        public static bool TryResolveRoute(Entity self, in Targets targets, in EntityLinkRef route,
            in ComponentLookup<EntityLinkSource> sources, in BufferLookup<EntityLinkEntry> entries,
            out Entity target)
        {
            if (route.LinkKey == 0 && route.ReadRootFrom is Target.Self or Target.None)
            {
                target = self;
                return true;
            }

            return route.TryResolve(self, targets, sources, entries, out target);
        }
    }
}