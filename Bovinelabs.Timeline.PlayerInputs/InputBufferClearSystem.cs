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
    public partial struct InputBufferClearSystem : ISystem
    {
        private UnsafeComponentLookup<InputSource> sources;
        private UnsafeBufferLookup<InputHistory> histories;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            this.sources = state.GetUnsafeComponentLookup<InputSource>(true);
            this.histories = state.GetUnsafeBufferLookup<InputHistory>(false); // Read/Write
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            this.sources.Update(ref state);
            this.histories.Update(ref state);

            state.Dependency = new ClearBufferTransition
            {
                Sources = this.sources,
                Histories = this.histories
            }.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(ClipActive), typeof(InputBufferClearTrigger))]
        [WithNone(typeof(ClipActivePrevious))]
        private partial struct ClearBufferTransition : IJobEntity
        {
            [ReadOnly] public UnsafeComponentLookup<InputSource> Sources;
            [NativeDisableParallelForRestriction] public UnsafeBufferLookup<InputHistory> Histories;

            private void Execute(in InputBufferClearTrigger config, in TrackBinding binding)
            {
                var consumer = binding.Value;

                if (!this.Sources.TryGetComponent(consumer, out var source) || source.Provider == Entity.Null) return;
                if (!this.Histories.TryGetBuffer(source.Provider, out var history)) return;

                if (config.ClearAll)
                {
                    history.Clear();
                }
                else
                {
                    for (var i = history.Length - 1; i >= 0; i--)
                    {
                        if (history[i].ActionId == config.ActionId)
                        {
                            history.RemoveAt(i);
                        }
                    }
                }
            }
        }
    }
}