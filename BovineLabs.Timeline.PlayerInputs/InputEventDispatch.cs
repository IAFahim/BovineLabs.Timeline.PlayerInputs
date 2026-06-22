using System;
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

    internal static class InputRouting
    {
        public static bool TryResolveRoute(Entity self, in Targets targets, Target routeTo, ushort routeLinkKey,
            in UnsafeComponentLookup<EntityLinkSource> sources, in UnsafeBufferLookup<EntityLinkEntry> entries,
            out Entity target)
        {
            if (routeLinkKey == 0 && routeTo is Target.Self or Target.None)
            {
                target = self;
                return true;
            }

            target = EntityLinkResolver.TryResolve(self, targets, routeTo, routeLinkKey, sources, entries, out var t)
                ? t
                : Entity.Null;
            return target != Entity.Null;
        }
    }
}