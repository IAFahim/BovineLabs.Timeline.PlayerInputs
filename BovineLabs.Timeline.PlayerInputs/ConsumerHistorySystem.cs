using BovineLabs.Timeline.PlayerInputs.Data;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
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
        private ComponentLookup<InputState> states;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<InputRegistry>();
            states = state.GetComponentLookup<InputState>(true);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            states.Update(ref state);

            var registry = SystemAPI.GetSingleton<InputRegistry>();

            state.Dependency = new RecordHistoryJob
            {
                Registry = registry.ProviderByPlayer,
                States = states,
                Tick = (uint)(SystemAPI.Time.ElapsedTime * 1000.0)
            }.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(ConsumerTag))]
        private partial struct RecordHistoryJob : IJobEntity
        {
            [ReadOnly] [NativeDisableContainerSafetyRestriction]
            public NativeArray<Entity> Registry;

            [ReadOnly] [NativeDisableContainerSafetyRestriction]
            public ComponentLookup<InputState> States;

            public uint Tick;

            private void Execute(in PlayerId id, in ActiveBufferMask mask, ref DynamicBuffer<InputHistory> history)
            {
                if (mask.Value.AllFalse) return;
                if (!InputAccess.TryGetState(Registry, States, id.Value, out var state)) return;

                var downFiltered = state.Down.BitAnd(mask.Value);
                var upFiltered = state.Up.BitAnd(mask.Value);

                var totalToAdd = downFiltered.CountBits() + upFiltered.CountBits();
                if (totalToAdd == 0) return;

                var removeCount = math.max(0, history.Length + totalToAdd - history.Capacity);
                if (removeCount > 0) history.RemoveRange(0, removeCount);

                Emit(downFiltered.Data1, 0, InputPhase.Down, ref history, Tick);
                Emit(downFiltered.Data2, 64, InputPhase.Down, ref history, Tick);
                Emit(downFiltered.Data3, 128, InputPhase.Down, ref history, Tick);
                Emit(downFiltered.Data4, 192, InputPhase.Down, ref history, Tick);

                Emit(upFiltered.Data1, 0, InputPhase.Up, ref history, Tick);
                Emit(upFiltered.Data2, 64, InputPhase.Up, ref history, Tick);
                Emit(upFiltered.Data3, 128, InputPhase.Up, ref history, Tick);
                Emit(upFiltered.Data4, 192, InputPhase.Up, ref history, Tick);
            }

            private static void Emit(ulong data, byte offset, InputPhase phase,
                ref DynamicBuffer<InputHistory> history, uint tick)
            {
                while (data != 0)
                {
                    var bit = math.tzcnt(data);
                    data ^= 1ul << bit;
                    history.Add(new InputHistory
                    {
                        ActionId = (byte)(offset + bit),
                        Phase = phase,
                        Tick = tick
                    });
                }
            }
        }
    }
}