using BovineLabs.Core.Groups;
using BovineLabs.Reaction.Conditions;
using Bovinelabs.Timeline.PlayerInputs.Data;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace Bovinelabs.Timeline.PlayerInputs
{
    [UpdateInGroup(typeof(BeginSimulationSystemGroup))]
    public partial struct InputTransducerSystem : ISystem
    {
        private ConditionEventWriter.Lookup eventWriterLookup;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            eventWriterLookup.Create(ref state);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            eventWriterLookup.Update(ref state);

            state.Dependency = new TransduceJob
            {
                Writers = eventWriterLookup
            }.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(InputProviderTag))]
        private partial struct TransduceJob : IJobEntity
        {
            [NativeDisableParallelForRestriction] public ConditionEventWriter.Lookup Writers;

            private void Execute(Entity entity, in InputState state,
                in DynamicBuffer<InputToConditionEvent> transducers)
            {
                if (!Writers.TryGet(entity, out var writer)) return;

                foreach (var transducer in transducers)
                {
                    var active = transducer.Phase switch
                    {
                        InputPhase.Down => state.Down[transducer.ActionId],
                        InputPhase.Held => state.Held[transducer.ActionId],
                        InputPhase.Up => state.Up[transducer.ActionId],
                        _ => false
                    };

                    if (active) writer.Trigger(transducer.Condition, transducer.Value);
                }
            }
        }
    }
}