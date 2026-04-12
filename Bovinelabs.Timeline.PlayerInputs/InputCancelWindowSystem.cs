using BovineLabs.Core.Extensions;
using BovineLabs.Core.Iterators;
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
        private UnsafeComponentLookup<InputSource> sources;
        private UnsafeComponentLookup<InputState> states;
        private UnsafeEnableableLookup timelines;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            this.sources = state.GetUnsafeComponentLookup<InputSource>(true);
            this.states = state.GetUnsafeComponentLookup<InputState>(true);
            this.timelines = state.GetUnsafeEnableableLookup();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            this.sources.Update(ref state);
            this.states.Update(ref state);
            this.timelines = state.GetUnsafeEnableableLookup();

            state.Dependency = new EvaluateCancelTransition
            {
                Sources = this.sources,
                States = this.states,
                Timelines = this.timelines
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

                if (!this.Sources.TryGetComponent(consumer, out var source) || source.Provider == Entity.Null) return;
                if (!this.States.TryGetComponent(source.Provider, out var state)) return;

                // Fast binary intersection check
                if (!state.Down.BitAnd(config.AllowedMask).AllFalse)
                {
                    if (this.Timelines.HasComponent(director.Director, ComponentType.ReadWrite<TimelineActive>()))
                    {
                        this.Timelines.SetComponentEnabled(director.Director, ComponentType.ReadWrite<TimelineActive>(), false);
                    }
                }
            }
        }
    }
}