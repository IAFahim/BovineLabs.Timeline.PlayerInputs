using BovineLabs.Timeline.PlayerInputs.Data;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace BovineLabs.Timeline.PlayerInputs
{
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [UpdateAfter(typeof(PlayerInputUpdateSystem))]
    public partial struct PlayerMoveInputUpdateSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.Dependency = new CopyMoveInputJob
            {
                AxisLookup = SystemAPI.GetBufferLookup<InputAxisBuffer>(true),
                SourceLookup = SystemAPI.GetComponentLookup<InputSource>(true)
            }.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(InputConsumerTag))]
        private partial struct CopyMoveInputJob : IJobEntity
        {
            [ReadOnly] public BufferLookup<InputAxisBuffer> AxisLookup;
            [ReadOnly] public ComponentLookup<InputSource> SourceLookup;

            private void Execute(ref PlayerMoveInput moveInput, in InputSource source)
            {
                if (source.Provider == Entity.Null) return;
                if (!AxisLookup.TryGetBuffer(source.Provider, out var axes)) return;

                for (var i = 0; i < axes.Length; i++)
                {
                    var axis = axes[i];
                    if (math.lengthsq(axis.Value) > 0.0001f)
                    {
                        moveInput.Value = axis.Value;
                        return;
                    }
                }

                moveInput.Value = float2.zero;
            }
        }
    }
}