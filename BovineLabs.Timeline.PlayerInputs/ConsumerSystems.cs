using BovineLabs.Timeline.Data;
using BovineLabs.Timeline.PlayerInputs.Data;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace BovineLabs.Timeline.PlayerInputs
{
    [UpdateInGroup(typeof(TimelineComponentAnimationGroup))]
    [Unity.Entities.WorldSystemFilter(Unity.Entities.WorldSystemFilterFlags.LocalSimulation | Unity.Entities.WorldSystemFilterFlags.ClientSimulation | Unity.Entities.WorldSystemFilterFlags.ServerSimulation)]
    public partial struct ConsumerBufferMaskSystem : ISystem
    {
        private ComponentLookup<ActiveBufferMask> masks;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            masks = state.GetComponentLookup<ActiveBufferMask>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            masks.Update(ref state);

            state.Dependency = new ResetMaskJob().ScheduleParallel(state.Dependency);
            state.Dependency = new AccumulateMaskJob
            {
                Masks = masks
            }.Schedule(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(ConsumerTag))]
        private partial struct ResetMaskJob : IJobEntity
        {
            private void Execute(ref ActiveBufferMask mask)
            {
                mask.Value = default;
            }
        }

        [BurstCompile]
        [WithAll(typeof(ClipActive))]
        private partial struct AccumulateMaskJob : IJobEntity
        {
            [NativeDisableParallelForRestriction] public ComponentLookup<ActiveBufferMask> Masks;

            private void Execute(in BufferWindowConfig config, in TrackBinding binding)
            {
                if (binding.Value == Entity.Null) return;

                if (!Masks.TryGetComponent(binding.Value, out var mask)) return;
                mask.Value = mask.Value.BitOr(config.AllowedActions);
                Masks[binding.Value] = mask;
            }
        }
    }
}
