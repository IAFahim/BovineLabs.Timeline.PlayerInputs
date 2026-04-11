using BovineLabs.Core.Groups;
using BovineLabs.Reaction.Conditions;
using PlayerInputs.Data;
using Unity.Burst;
using Unity.Entities;

namespace PlayerInputs.Systems
{
    [UpdateInGroup(typeof(BeginSimulationSystemGroup))]
    [UpdateAfter(typeof(PlayerInputPollSystem))]
    public partial struct InputTransducerSystem : ISystem
    {
        private ConditionEventWriter.Lookup eventWriterLookup;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            this.eventWriterLookup.Create(ref state);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            this.eventWriterLookup.Update(ref state);

            state.Dependency = new TransduceJob
            {
                Writers = this.eventWriterLookup
            }.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(InputProviderTag))]
        private partial struct TransduceJob : IJobEntity
        {
            public ConditionEventWriter.Lookup Writers;

            private void Execute(Entity entity, in InputState state, in DynamicBuffer<InputToConditionEvent> transducers)
            {
                if (!this.Writers.TryGet(entity, out var writer))
                {
                    return;
                }

                foreach (var transducer in transducers)
                {
                    var active = transducer.Phase switch
                    {
                        InputPhase.Down => state.Down.Has(transducer.ActionId),
                        InputPhase.Held => state.Held.Has(transducer.ActionId),
                        InputPhase.Up => state.Up.Has(transducer.ActionId),
                        _ => false
                    };

                    if (active)
                    {
                        writer.Trigger(transducer.Condition, transducer.Value);
                    }
                }
            }
        }
    }
}