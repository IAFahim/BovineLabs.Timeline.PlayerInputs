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
            state.Dependency = new RecordHistoryJob
            {
                Tick = (uint)(SystemAPI.Time.ElapsedTime * 1000.0)
            }.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(InputProviderTag))]
        private partial struct RecordHistoryJob : IJobEntity
        {
            public uint Tick;

            private void Execute(in InputState state, ref DynamicBuffer<InputHistory> history)
            {
                if (state.Down.AllFalse && state.Up.AllFalse) return;

                for (byte i = 0; i < 255; i++)
                    if (state.Down[i])
                    {
                        if (history.Length >= history.Capacity) history.RemoveAt(0);
                        history.Add(new InputHistory { ActionId = i, Phase = InputPhase.Down, Tick = Tick });
                    }
                    else if (state.Up[i])
                    {
                        if (history.Length >= history.Capacity) history.RemoveAt(0);
                        history.Add(new InputHistory { ActionId = i, Phase = InputPhase.Up, Tick = Tick });
                    }
            }
        }
    }
}