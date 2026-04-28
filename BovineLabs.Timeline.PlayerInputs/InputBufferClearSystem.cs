using BovineLabs.Core.Extensions;
using BovineLabs.Core.Iterators;
using BovineLabs.Core.Collections;
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
        private UnsafeComponentLookup<InputHistoryState> historyStates;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            sources = state.GetUnsafeComponentLookup<InputSource>(true);
            histories = state.GetUnsafeBufferLookup<InputHistory>();
            historyStates = state.GetUnsafeComponentLookup<InputHistoryState>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            sources.Update(ref state);
            histories.Update(ref state);
            historyStates.Update(ref state);
            state.Dependency = new ClearBufferTransition
            {
                Sources = sources,
                Histories = histories,
                HistoryStates = historyStates
            }.Schedule(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(ClipActive), typeof(InputBufferClearTrigger))]
        [WithNone(typeof(ClipActivePrevious))]
        private partial struct ClearBufferTransition : IJobEntity
        {
            [ReadOnly] public UnsafeComponentLookup<InputSource> Sources;
            public UnsafeBufferLookup<InputHistory> Histories;
            public UnsafeComponentLookup<InputHistoryState> HistoryStates;

            private void Execute(in InputBufferClearTrigger config, in TrackBinding binding)
            {
                var consumer = binding.Value;
                if (!Sources.TryGetComponent(consumer, out var source) || source.Provider == Entity.Null) return;
                if (!Histories.TryGetBuffer(source.Provider, out var history)) return;
                if (!HistoryStates.TryGetComponent(source.Provider, out var historyState)) return;

                ref var actionIds = ref config.ActionIds.Value;
                Normalize(ref history, ref historyState);

                if (actionIds.Length == 0)
                {
                    history.Clear();
                    historyState.Head = 0;
                    HistoryStates[source.Provider] = historyState;
                    return;
                }

                var filter = default(BitArray256);
                for (var i = 0; i < actionIds.Length; i++)
                    filter[actionIds[i]] = true;

                var writeIndex = 0;
                for (var readIndex = 0; readIndex < history.Length; readIndex++)
                {
                    var entry = history[readIndex];
                    if (filter[entry.ActionId]) continue;

                    if (writeIndex != readIndex) history[writeIndex] = entry;
                    writeIndex++;
                }

                for (var i = history.Length - 1; i >= writeIndex; i--)
                    history.RemoveAt(i);

                historyState.Head = 0;
                HistoryStates[source.Provider] = historyState;
            }

            private static void Normalize(ref UnsafeDynamicBuffer<InputHistory> history, ref InputHistoryState historyState)
            {
                if (history.Length <= 1 || historyState.Head == 0) return;

                var head = historyState.Head;
                if ((uint)head >= history.Length) head = 0;
                RotateLeft(ref history, head);
                historyState.Head = 0;
            }

            private static void RotateLeft(ref UnsafeDynamicBuffer<InputHistory> history, int pivot)
            {
                if (pivot <= 0 || pivot >= history.Length) return;

                Reverse(ref history, 0, pivot - 1);
                Reverse(ref history, pivot, history.Length - 1);
                Reverse(ref history, 0, history.Length - 1);
            }

            private static void Reverse(ref UnsafeDynamicBuffer<InputHistory> history, int left, int right)
            {
                while (left < right)
                {
                    var tmp = history[left];
                    history[left] = history[right];
                    history[right] = tmp;
                    left++;
                    right--;
                }
            }
        }
    }
}
