using BovineLabs.Core.Groups;
using BovineLabs.Reaction.Conditions;
using BovineLabs.Timeline.PlayerInputs.Data;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace BovineLabs.Timeline.PlayerInputs
{[UpdateInGroup(typeof(BeginSimulationSystemGroup))]
    public partial struct InputTransducerSystem : ISystem
    {
        private ConditionEventWriter.Lookup eventWriterLookup;
        private ComponentLookup<InputState> stateLookup;
        private BufferLookup<InputToConditionEvent> transducerLookup;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            eventWriterLookup.Create(ref state);
            stateLookup = state.GetComponentLookup<InputState>(true);
            transducerLookup = state.GetBufferLookup<InputToConditionEvent>(true);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            eventWriterLookup.Update(ref state);
            stateLookup.Update(ref state);
            transducerLookup.Update(ref state);

            state.Dependency = new TransduceJob
            {
                Writers = eventWriterLookup,
                States = stateLookup,
                Transducers = transducerLookup
            }.Schedule(state.Dependency);
        }[BurstCompile]
        [WithAll(typeof(InputConsumerTag))]
        private partial struct TransduceJob : IJobEntity
        {
            public ConditionEventWriter.Lookup Writers;
            [ReadOnly] public ComponentLookup<InputState> States;
            [ReadOnly] public BufferLookup<InputToConditionEvent> Transducers;

            private void Execute(in InputSource source, in InputConsumerRoute route)
            {
                if (source.Provider == Entity.Null) return;
                if (!States.TryGetComponent(source.Provider, out var state)) return;
                if (!Transducers.TryGetBuffer(source.Provider, out var transducers)) return;
                if (!Writers.TryGet(route.Target, out var writer)) return;

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