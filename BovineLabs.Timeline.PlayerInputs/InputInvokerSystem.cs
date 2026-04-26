using BovineLabs.Core.Extensions;
using BovineLabs.Core.Iterators;
using BovineLabs.Reaction.Conditions;
using BovineLabs.Timeline.Data;
using BovineLabs.Timeline.PlayerInputs.Data;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace BovineLabs.Timeline.PlayerInputs
{[UpdateInGroup(typeof(TimelineComponentAnimationGroup))]
    public partial struct InputInvokerSystem : ISystem
    {
        private ConditionEventWriter.Lookup writers;
        private UnsafeComponentLookup<InputSource> sources;
        private UnsafeComponentLookup<InputState> states;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            writers.Create(ref state);
            sources = state.GetUnsafeComponentLookup<InputSource>(true);
            states = state.GetUnsafeComponentLookup<InputState>(true);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            writers.Update(ref state);
            sources.Update(ref state);
            states.Update(ref state);

            state.Dependency = new EvaluateInvokerTransition
            {
                Writers = writers,
                Sources = sources,
                States = states
            }.Schedule(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(ClipActive))]
        private partial struct EvaluateInvokerTransition : IJobEntity
        {
            public ConditionEventWriter.Lookup Writers;
            [ReadOnly] public UnsafeComponentLookup<InputSource> Sources;
            [ReadOnly] public UnsafeComponentLookup<InputState> States;

            private void Execute(in InputInvokerConfig config, in TrackBinding binding)
            {
                var consumer = binding.Value;

                if (!Sources.TryGetComponent(consumer, out var source) || source.Provider == Entity.Null) return;
                if (!States.TryGetComponent(source.Provider, out var state)) return;

                var active = config.Phase switch
                {
                    InputPhase.Down => state.Down[config.ActionId],
                    InputPhase.Held => state.Held[config.ActionId],
                    InputPhase.Up => state.Up[config.ActionId],
                    _ => false
                };

                if (active && Writers.TryGet(config.RouteEntity, out var writer)) writer.Trigger(config.Condition, config.Value);
            }
        }
    }
}