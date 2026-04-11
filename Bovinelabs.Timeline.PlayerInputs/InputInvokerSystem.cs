using BovineLabs.Reaction.Conditions;
using BovineLabs.Timeline;
using BovineLabs.Timeline.Data;
using Bovinelabs.Timeline.PlayerInputs.Data;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace Bovinelabs.Timeline.PlayerInputs
{
    [UpdateInGroup(typeof(TimelineComponentAnimationGroup))]
    public partial struct InputInvokerSystem : ISystem
    {
        private ConditionEventWriter.Lookup writers;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            writers.Create(ref state);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            writers.Update(ref state);

            state.Dependency = new EvaluateInvokerTransition
            {
                Writers = writers,
                Sources = SystemAPI.GetComponentLookup<InputSource>(true),
                States = SystemAPI.GetComponentLookup<InputState>(true)
            }.Schedule(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(ClipActive))]
        private partial struct EvaluateInvokerTransition : IJobEntity
        {
            public ConditionEventWriter.Lookup Writers;
            [ReadOnly] public ComponentLookup<InputSource> Sources;
            [ReadOnly] public ComponentLookup<InputState> States;

            private void Execute(in InputInvokerConfig config, in TrackBinding binding)
            {
                var consumer = binding.Value;

                if (!Sources.TryGetComponent(consumer, out var source) || source.Provider == Entity.Null) return;

                if (!States.TryGetComponent(source.Provider, out var state)) return;

                var active = config.Phase switch
                {
                    InputPhase.Down => state.Down.Has(config.ActionId),
                    InputPhase.Held => state.Held.Has(config.ActionId),
                    InputPhase.Up => state.Up.Has(config.ActionId),
                    _ => false
                };

                if (active && Writers.TryGet(consumer, out var writer)) writer.Trigger(config.Condition, config.Value);
            }
        }
    }
}