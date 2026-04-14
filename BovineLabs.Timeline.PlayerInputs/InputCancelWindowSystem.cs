using BovineLabs.Core.Extensions;
using BovineLabs.Core.Iterators;
using BovineLabs.Timeline;
using BovineLabs.Timeline.Data;
using BovineLabs.Timeline.PlayerInputs.Data;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace BovineLabs.Timeline.PlayerInputs
{
    [UpdateInGroup(typeof(TimelineComponentAnimationGroup))]
    public partial struct InputCancelWindowSystem : ISystem
    {
        private UnsafeComponentLookup<InputSource> sources;
        private UnsafeComponentLookup<InputState> states;
        private UnsafeEnableableLookup timelines;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            sources = state.GetUnsafeComponentLookup<InputSource>(true);
            states = state.GetUnsafeComponentLookup<InputState>(true);
            timelines = state.GetUnsafeEnableableLookup();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            sources.Update(ref state);
            states.Update(ref state);
            timelines = state.GetUnsafeEnableableLookup();

            state.Dependency = new EvaluateCancelTransition
            {
                Sources = sources,
                States = states,
                Timelines = timelines
            }.Schedule(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(ClipActive))]
        private partial struct EvaluateCancelTransition : IJobEntity
        {
            [ReadOnly] public UnsafeComponentLookup<InputSource> Sources;
            [ReadOnly] public UnsafeComponentLookup<InputState> States;
            public UnsafeEnableableLookup Timelines;

            private void Execute(in InputCancelWindowConfig config, in TrackBinding binding, in DirectorRoot director)
            {
                var consumer = binding.Value;

                if (!Sources.TryGetComponent(consumer, out var source) || source.Provider == Entity.Null) return;
                if (!States.TryGetComponent(source.Provider, out var state)) return;

                // Fast binary intersection check
                if (!state.Down.BitAnd(config.AllowedMask).AllFalse)
                    if (Timelines.HasComponent(director.Director, ComponentType.ReadWrite<TimelineActive>()))
                        Timelines.SetComponentEnabled(director.Director, ComponentType.ReadWrite<TimelineActive>(),
                            false);
            }
        }
    }
}