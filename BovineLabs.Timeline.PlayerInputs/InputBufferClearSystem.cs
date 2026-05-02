using BovineLabs.Core.Collections;
using BovineLabs.Timeline.Data;
using BovineLabs.Timeline.PlayerInputs.Data;
using Unity.Burst;
using Unity.Entities;

namespace BovineLabs.Timeline.PlayerInputs
{
    [UpdateInGroup(typeof(TimelineComponentAnimationGroup))]
    [UpdateAfter(typeof(CommandSequenceSystem))]
    public partial struct InputBufferClearSystem : ISystem
    {
        private BufferLookup<InputHistory> histories;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            histories = state.GetBufferLookup<InputHistory>(false);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            histories.Update(ref state);
            state.Dependency = new ClearBufferJob { Histories = histories }.Schedule(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(ClipActive), typeof(BufferClearConfig))]
        [WithNone(typeof(ClipActivePrevious))]
        private partial struct ClearBufferJob : IJobEntity
        {
            public BufferLookup<InputHistory> Histories;

            private void Execute(in BufferClearConfig config, in TrackBinding binding)
            {
                if (binding.Value == Entity.Null || !Histories.TryGetBuffer(binding.Value, out var history)) return;

                ref var ids = ref config.ActionIds.Value;
                if (ids.Length == 0)
                {
                    history.Clear();
                    return;
                }

                var filter = default(BitArray256);
                for (var i = 0; i < ids.Length; i++) filter[ids[i]] = true;

                var write = 0;
                for (var read = 0; read < history.Length; read++)
                {
                    if (filter[history[read].ActionId]) continue;
                    if (write != read) history[write] = history[read];
                    write++;
                }

                for (var i = history.Length - 1; i >= write; i--) history.RemoveAt(i);
            }
        }
    }
}