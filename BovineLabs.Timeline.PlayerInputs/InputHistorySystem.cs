using BovineLabs.Core.Groups;
using BovineLabs.Timeline.PlayerInputs.Data;
using Unity.Burst;
using Unity.Entities;

namespace BovineLabs.Timeline.PlayerInputs
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

            private void Execute(in InputState state, ref InputHistoryState historyState,
                ref DynamicBuffer<InputHistory> history)
            {
                if (state.Down.AllFalse && state.Up.AllFalse) return;

                for (byte i = 0; i < 255; i++)
                    if (state.Down[i])
                        AddHistory(ref historyState, history,
                            new InputHistory { ActionId = i, Phase = InputPhase.Down, Tick = Tick });
                    else if (state.Up[i])
                        AddHistory(ref historyState, history,
                            new InputHistory { ActionId = i, Phase = InputPhase.Up, Tick = Tick });
            }

            private static void AddHistory(ref InputHistoryState historyState, DynamicBuffer<InputHistory> history,
                InputHistory entry)
            {
                if (history.Length < history.Capacity)
                {
                    history.Add(entry);
                    return;
                }

                if (history.Length == 0) return;

                var head = historyState.Head;
                if ((uint)head >= history.Length) head = 0;

                history[head] = entry;
                head++;

                if (head >= history.Length) head = 0;
                historyState.Head = head;
            }
        }
    }
}
