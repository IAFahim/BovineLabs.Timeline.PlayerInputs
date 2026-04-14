// InputBufferClearSystem.cs

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
    public partial struct InputBufferClearSystem : ISystem
    {
        private UnsafeComponentLookup<InputSource> sources;
        private UnsafeBufferLookup<InputHistory> histories;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            sources = state.GetUnsafeComponentLookup<InputSource>(true);
            histories = state.GetUnsafeBufferLookup<InputHistory>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            sources.Update(ref state);
            histories.Update(ref state);
            state.Dependency = new ClearBufferTransition
            {
                Sources = sources,
                Histories = histories
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
                if (!Sources.TryGetComponent(consumer, out var source) || source.Provider == Entity.Null) return;
                if (!Histories.TryGetBuffer(source.Provider, out var history)) return;

                ref var actionIds = ref config.ActionIds.Value;

                // Empty blob = clear all
                if (actionIds.Length == 0)
                {
                    history.Clear();
                    return;
                }

                for (var i = history.Length - 1; i >= 0; i--)
                {
                    var historyActionId = history[i].ActionId;
                    for (var j = 0; j < actionIds.Length; j++)
                        if (historyActionId == actionIds[j])
                        {
                            history.RemoveAt(i);
                            break;
                        }
                }
            }
        }
    }
}