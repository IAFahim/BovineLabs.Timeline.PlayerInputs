using BovineLabs.Core.Groups;
using Bovinelabs.Timeline.PlayerInputs.Data;
using Unity.Burst;
using Unity.Entities;

namespace Bovinelabs.Timeline.PlayerInputs
{
    [UpdateInGroup(typeof(BeginSimulationSystemGroup))]
    public partial struct InputHistorySystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var tick = SystemAPI.Time.ElapsedTime;

            state.Dependency = new RecordHistoryJob
            {
                Tick = (uint)(tick * 1000.0)
            }.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(InputProviderTag))]
        private partial struct RecordHistoryJob : IJobEntity
        {
            public uint Tick;

            private void Execute(in InputState state, ref DynamicBuffer<InputHistory> history)
            {
                if (state.Down.Chunk0 == 0 && state.Down.Chunk1 == 0 && state.Down.Chunk2 == 0 &&
                    state.Down.Chunk3 == 0) return;

                for (byte i = 0; i < 255; i++)
                    if (state.Down.Has(i))
                    {
                        if (history.Length >= history.Capacity) history.RemoveAt(0);

                        history.Add(new InputHistory { ActionId = i, Tick = Tick });
                    }
            }
        }
    }
}