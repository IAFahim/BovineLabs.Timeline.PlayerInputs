using BovineLabs.Timeline.PlayerInputs.Data;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace BovineLabs.Timeline.PlayerInputs
{
    [UpdateInGroup(typeof(TimelineComponentAnimationGroup))]
    [UpdateAfter(typeof(ConsumerBufferMaskSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation |
                       WorldSystemFilterFlags.ServerSimulation)]
    public partial struct ConsumerHistorySystem : ISystem
    {
        private ComponentLookup<InputState> _states;
        private ComponentLookup<InputHistoryLimit> _limits;
        private ComponentLookup<PlayerOverride> _overrides;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<InputRegistry>();
            state.RequireForUpdate<SimulationTick>();
            _states = state.GetComponentLookup<InputState>(true);
            _limits = state.GetComponentLookup<InputHistoryLimit>(true);
            _overrides = state.GetComponentLookup<PlayerOverride>(true);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            _states.Update(ref state);
            _limits.Update(ref state);
            _overrides.Update(ref state);

            state.Dependency = new RecordHistoryJob
            {
                Slots = SystemAPI.GetSingletonBuffer<ProviderSlot>(true),
                States = _states,
                Limits = _limits,
                Overrides = _overrides,
                Tick = SystemAPI.GetSingleton<SimulationTick>().Value,
                Millis = (uint)(SystemAPI.Time.ElapsedTime * 1000.0)
            }.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(ConsumerTag))]
        private partial struct RecordHistoryJob : IJobEntity
        {
            [ReadOnly] public DynamicBuffer<ProviderSlot> Slots;

            [ReadOnly] public ComponentLookup<InputState> States;

            [ReadOnly] public ComponentLookup<InputHistoryLimit> Limits;

            [ReadOnly] public ComponentLookup<PlayerOverride> Overrides;

            public uint Tick;

            public uint Millis;

            private void Execute(Entity entity, in PlayerId id, in ActiveBufferMask mask,
                ref DynamicBuffer<InputHistory> history)
            {
                if (mask.Value.AllFalse) return;
                if (!InputAccess.TryGetState(Slots, States, Overrides, entity, id.Value, out var state)) return;

                var downFiltered = state.Down.BitAnd(mask.Value);
                var upFiltered = state.Up.BitAnd(mask.Value);

                var totalToAdd = downFiltered.CountBits() + upFiltered.CountBits();
                if (totalToAdd == 0) return;

                var limit = Limits.TryGetComponent(entity, out var configured)
                    ? HistoryMath.ClampLimit(configured.Value)
                    : HistoryMath.DefaultLimit;

                var evict = HistoryMath.EvictCount(history.Length, totalToAdd, limit);
                if (evict > 0) history.RemoveRange(0, evict);

                EmitWord(downFiltered.Data1, upFiltered.Data1, state.Held.Data1, 0, ref history, Tick, Millis);
                EmitWord(downFiltered.Data2, upFiltered.Data2, state.Held.Data2, 64, ref history, Tick, Millis);
                EmitWord(downFiltered.Data3, upFiltered.Data3, state.Held.Data3, 128, ref history, Tick, Millis);
                EmitWord(downFiltered.Data4, upFiltered.Data4, state.Held.Data4, 192, ref history, Tick, Millis);

                var overflow = HistoryMath.OverflowCount(history.Length, limit);
                if (overflow > 0) history.RemoveRange(0, overflow);
            }

            // Records one 64-action word. An action carrying BOTH a Down and an Up this tick (press+release, or
            // release+press, collapsed into one frame at low fps / on a lag spike) is ordered by the live Held bit:
            // still held => the press was the last transition => emit Up then Down; not held => Down then Up. This
            // stops a frame-collapse from recording a physical release->press as Down,Up (which no Up-then-Down
            // sequence could match). Single-phase actions record in the historical Down-group-then-Up-group order.
            private static void EmitWord(ulong down, ulong up, ulong held, byte offset,
                ref DynamicBuffer<InputHistory> history, uint tick, uint millis)
            {
                var both = down & up;
                EmitBits(down & ~both, offset, InputPhase.Down, ref history, tick, millis);
                EmitBits(up & ~both, offset, InputPhase.Up, ref history, tick, millis);

                var b = both;
                while (b != 0)
                {
                    var bit = math.tzcnt(b);
                    b ^= 1ul << bit;
                    var id = (byte)(offset + bit);
                    var pressLast = (held & (1ul << bit)) != 0;
                    var first = pressLast ? InputPhase.Up : InputPhase.Down;
                    var second = pressLast ? InputPhase.Down : InputPhase.Up;
                    history.Add(new InputHistory { ActionId = id, Phase = first, Tick = tick, Millis = millis });
                    history.Add(new InputHistory { ActionId = id, Phase = second, Tick = tick, Millis = millis });
                }
            }

            private static void EmitBits(ulong data, byte offset, InputPhase phase,
                ref DynamicBuffer<InputHistory> history, uint tick, uint millis)
            {
                while (data != 0)
                {
                    var bit = math.tzcnt(data);
                    data ^= 1ul << bit;
                    history.Add(new InputHistory
                    {
                        ActionId = (byte)(offset + bit),
                        Phase = phase,
                        Tick = tick,
                        Millis = millis
                    });
                }
            }
        }
    }
}