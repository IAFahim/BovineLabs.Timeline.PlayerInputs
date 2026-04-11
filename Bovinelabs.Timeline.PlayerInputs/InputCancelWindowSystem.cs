using BovineLabs.Timeline;
using BovineLabs.Timeline.Data;
using Bovinelabs.Timeline.PlayerInputs.Data;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace Bovinelabs.Timeline.PlayerInputs
{
    [UpdateInGroup(typeof(TimelineComponentAnimationGroup))]
    public partial struct InputCancelWindowSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.Dependency = new EvaluateCancelTransition
            {
                Sources = SystemAPI.GetComponentLookup<InputSource>(true),
                States = SystemAPI.GetComponentLookup<InputState>(true),
                Timelines = SystemAPI.GetComponentLookup<TimelineActive>()
            }.Schedule(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(ClipActive))]
        private partial struct EvaluateCancelTransition : IJobEntity
        {
            [ReadOnly] public ComponentLookup<InputSource> Sources;
            [ReadOnly] public ComponentLookup<InputState> States;
            public ComponentLookup<TimelineActive> Timelines;

            private void Execute(in InputCancelWindowConfig config, in TrackBinding binding, in DirectorRoot director)
            {
                var consumer = binding.Value;

                if (!Sources.TryGetComponent(consumer, out var source) || source.Provider == Entity.Null) return;

                if (!States.TryGetComponent(source.Provider, out var state)) return;

                if (state.Down.Overlaps(config.AllowedMask))
                    if (Timelines.HasComponent(director.Director))
                        Timelines.SetComponentEnabled(director.Director, false);
            }
        }
    }
}