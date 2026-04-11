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
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.Dependency = new ClearBufferTransition
            {
                Sources = SystemAPI.GetComponentLookup<InputSource>(true),
                Histories = SystemAPI.GetBufferLookup<InputHistory>()
            }.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(ClipActive), typeof(InputBufferClearTrigger))]
        [WithNone(typeof(ClipActivePrevious))]
        private partial struct ClearBufferTransition : IJobEntity
        {
            [ReadOnly] public ComponentLookup<InputSource> Sources;
            [NativeDisableParallelForRestriction] public BufferLookup<InputHistory> Histories;

            private void Execute(in InputBufferClearTrigger config, in TrackBinding binding)
            {
                var consumer = binding.Value;

                if (!Sources.TryGetComponent(consumer, out var source) || source.Provider == Entity.Null) return;

                if (!Histories.TryGetBuffer(source.Provider, out var history)) return;

                if (config.ClearAll)
                    history.Clear();
                else
                    for (var i = history.Length - 1; i >= 0; i--)
                        if (history[i].ActionId == config.ActionId)
                            history.RemoveAt(i);
            }
        }
    }
}