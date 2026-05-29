using BovineLabs.Core.Collections;
using BovineLabs.Reaction.Conditions;
using BovineLabs.Timeline.Data;
using BovineLabs.Timeline.PlayerInputs.Data;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using System;
using BovineLabs.Reaction.Data.Conditions;
using Unity.Entities;

namespace BovineLabs.Timeline.PlayerInputs
{
    [UpdateInGroup(typeof(TimelineComponentAnimationGroup))]
    [UpdateAfter(typeof(CommandSequenceResetSystem))]
    [Unity.Entities.WorldSystemFilter(Unity.Entities.WorldSystemFilterFlags.LocalSimulation | Unity.Entities.WorldSystemFilterFlags.ClientSimulation | Unity.Entities.WorldSystemFilterFlags.ServerSimulation)]
    public partial struct CommandSequenceSystem : ISystem
    {
        
        private BovineLabs.Core.Collections.NativeParallelMultiHashMapFallback<Entity, EventAmount> _eventChanges;
        private NativeParallelHashSet<Entity> _uniqueKeySet;
        private NativeList<Entity> _uniqueKeys;
        private ConditionEventWriter.Lookup writers;

        private ComponentLookup<InputState> states;
        private ComponentLookup<PlayerId> playerIds;
        private BufferLookup<InputHistory> histories;

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
            state.RequireForUpdate<InputRegistry>();
            
            _eventChanges = new BovineLabs.Core.Collections.NativeParallelMultiHashMapFallback<Entity, EventAmount>(64, Allocator.Persistent);
            _uniqueKeySet = new NativeParallelHashSet<Entity>(64, Allocator.Persistent);
            _uniqueKeys = new NativeList<Entity>(64, Allocator.Persistent);
            writers.Create(ref state);

            states = state.GetComponentLookup<InputState>(true);
            playerIds = state.GetComponentLookup<PlayerId>(true);
            histories = state.GetBufferLookup<InputHistory>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            writers.Update(ref state);
            states.Update(ref state);
            playerIds.Update(ref state);
            histories.Update(ref state);

            var registry = SystemAPI.GetSingleton<InputRegistry>();

            _uniqueKeySet.Clear();

            state.Dependency.Complete();

            new GatherJob
            {
                EventChanges = _eventChanges.AsWriter(),
                UniqueKeys = _uniqueKeySet.AsParallelWriter(),
                Registry = registry.ProviderByPlayer,
                States = states,
                PlayerIds = playerIds,
                Histories = histories
            }.Run();

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
                Writers = writers
            }.Schedule(_uniqueKeys, 64, state.Dependency);

            state.Dependency = _eventChanges.Clear(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(ClipActive))]
        private partial struct GatherJob : IJobEntity
        {
            
            public BovineLabs.Core.Collections.NativeParallelMultiHashMapFallback<Entity, EventAmount>.ParallelWriter EventChanges;
            public NativeParallelHashSet<Entity>.ParallelWriter UniqueKeys;

            [ReadOnly] [NativeDisableContainerSafetyRestriction] public NativeArray<Entity> Registry;
            [ReadOnly] [NativeDisableContainerSafetyRestriction] public ComponentLookup<InputState> States;
            [ReadOnly] [NativeDisableContainerSafetyRestriction] public ComponentLookup<PlayerId> PlayerIds;
            public BufferLookup<InputHistory> Histories;

            private void Execute(ref CommandSequenceState commandState, in CommandSequenceConfig config,
                in TrackBinding binding)
            {
                if (commandState.IsCompleted || binding.Value == Entity.Null) return;
                if (!PlayerIds.TryGetComponent(binding.Value, out var pid)) return;
                if (!InputAccess.TryGetState(Registry, States, pid.Value, out var state)) return;
                if (!Histories.TryGetBuffer(binding.Value, out var history)) return;

                ref var sequences = ref config.Blob.Value.Sequences;

                for (var s = 0; s < sequences.Length; s++)
                {
                    ref var seq = ref sequences[s];
                    if (seq.Steps.Length == 0) continue;

                    var consumeMask = default(BitArray256);
                    var searchIndex = 0;
                    var matched = true;

                    for (var i = 0; i < seq.Steps.Length; i++)
                        if (!Evaluate(ref seq.Steps[i], state, history, ref consumeMask, ref searchIndex))
                        {
                            matched = false;
                            break;
                        }

                    if (!matched) continue;

                    CommitConsumes(history, ref consumeMask);

                    EventChanges.Add(config.RouteEntity, new EventAmount(seq.Condition, seq.Value));
                    UniqueKeys.Add(config.RouteEntity);

                    commandState.IsCompleted = true;
                    return;
                }
            }

            private static bool Evaluate(ref CommandStep step, in InputState state,
                in DynamicBuffer<InputHistory> history, ref BitArray256 consumeMask, ref int searchIndex)
            {
                switch (step.Mode)
                {
                    case CommandMode.None:
                        return step.Phase switch
                        {
                            InputPhase.Down => state.Down[step.ActionId],
                            InputPhase.Held => state.Held[step.ActionId],
                            InputPhase.Up => state.Up[step.ActionId],
                            _ => false
                        };

                    case CommandMode.Contains:
                    case CommandMode.Consume:
                    {
                        for (var i = 0; i < history.Length; i++)
                        {
                            if (consumeMask[i] || history[i].ActionId != step.ActionId ||
                                history[i].Phase != step.Phase) continue;
                            if (step.Mode == CommandMode.Consume) consumeMask[i] = true;
                            return true;
                        }

                        return false;
                    }

                    case CommandMode.FirstConsume:
                    {
                        for (var i = 0; i < history.Length; i++)
                        {
                            if (consumeMask[i]) continue;
                            if (history[i].ActionId != step.ActionId || history[i].Phase != step.Phase) return false;
                            consumeMask[i] = true;
                            return true;
                        }

                        return false;
                    }

                    case CommandMode.LastConsume:
                    {
                        for (var i = history.Length - 1; i >= 0; i--)
                        {
                            if (consumeMask[i]) continue;
                            if (history[i].ActionId != step.ActionId || history[i].Phase != step.Phase) return false;
                            consumeMask[i] = true;
                            return true;
                        }

                        return false;
                    }

                    case CommandMode.OrderedContains:
                    case CommandMode.OrderedConsume:
                    {
                        for (var i = searchIndex; i < history.Length; i++)
                        {
                            if (consumeMask[i] || history[i].ActionId != step.ActionId ||
                                history[i].Phase != step.Phase) continue;
                            if (step.Mode == CommandMode.OrderedConsume) consumeMask[i] = true;
                            searchIndex = i + 1;
                            return true;
                        }

                        return false;
                    }

                    case CommandMode.OrderedFirstConsume:
                    {
                        for (var i = searchIndex; i < history.Length; i++)
                        {
                            if (consumeMask[i]) continue;
                            if (history[i].ActionId != step.ActionId || history[i].Phase != step.Phase) return false;
                            consumeMask[i] = true;
                            searchIndex = i + 1;
                            return true;
                        }

                        return false;
                    }

                    case CommandMode.OrderedLastConsume:
                    {
                        for (var i = history.Length - 1; i >= searchIndex; i--)
                        {
                            if (consumeMask[i] || history[i].ActionId != step.ActionId || (byte)history[i].Phase != (byte)step.Phase) continue;
                            consumeMask[i] = true;
                            searchIndex = i + 1;
                            return true;
                        }

                        return false;
                    }

                    case CommandMode.NotContains:
                    {
                        for (var i = 0; i < history.Length; i++)
                        {
                            if (consumeMask[i]) continue;
                            if (history[i].ActionId == step.ActionId && history[i].Phase == step.Phase) return false;
                        }

                        return true;
                    }

                    case CommandMode.NotFirst:
                    {
                        for (var i = 0; i < history.Length; i++)
                        {
                            if (consumeMask[i]) continue;
                            return history[i].ActionId != step.ActionId || history[i].Phase != step.Phase;
                        }

                        return true;
                    }

                    case CommandMode.NotLast:
                    {
                        for (var i = history.Length - 1; i >= 0; i--)
                        {
                            if (consumeMask[i]) continue;
                            return history[i].ActionId != step.ActionId || history[i].Phase != step.Phase;
                        }

                        return true;
                    }

                    default: return false;
                }
            }

            private static void CommitConsumes(DynamicBuffer<InputHistory> history, ref BitArray256 consumeMask)
            {
                if (consumeMask.AllFalse) return;

                var write = 0;
                for (var read = 0; read < history.Length; read++)
                {
                    if (consumeMask[read]) continue;
                    if (write != read) history[write] = history[read];
                    write++;
                }

                history.Length = write;
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
