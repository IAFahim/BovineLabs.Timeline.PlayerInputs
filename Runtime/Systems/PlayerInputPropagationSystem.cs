using PlayerInputs.Data;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;

namespace PlayerInputs.Systems
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct PlayerInputPropagationSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BackingInputEntityTag>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var job = new PropagateBuffersJob
            {
                DownLookup = SystemAPI.GetBufferLookup<InputButtonDownBuffer>(false),
                HeldLookup = SystemAPI.GetBufferLookup<InputButtonHeldBuffer>(false),
                UpLookup = SystemAPI.GetBufferLookup<InputButtonUpBuffer>(false),
                AxisLookup = SystemAPI.GetBufferLookup<InputAxisBuffer>(false)
            };

            state.Dependency = job.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(BackingInputEntityTag))]
        private partial struct PropagateBuffersJob : IJobEntity
        {
            [NativeDisableParallelForRestriction] public BufferLookup<InputButtonDownBuffer> DownLookup;
            [NativeDisableParallelForRestriction] public BufferLookup<InputButtonHeldBuffer> HeldLookup;
            [NativeDisableParallelForRestriction] public BufferLookup<InputButtonUpBuffer> UpLookup;
            [NativeDisableParallelForRestriction] public BufferLookup<InputAxisBuffer> AxisLookup;

            private void Execute(
                in DynamicBuffer<InputSubscribedEntity> subscribers,
                in DynamicBuffer<InputButtonDownBuffer> masterDowns,
                in DynamicBuffer<InputButtonHeldBuffer> masterHelds,
                in DynamicBuffer<InputButtonUpBuffer> masterUps,
                in DynamicBuffer<InputAxisBuffer> masterAxes)
            {
                for (int i = 0; i < subscribers.Length; i++)
                {
                    var target = subscribers[i].Value;

                    if (!DownLookup.HasBuffer(target)) continue;

                    // O(n) unrolled memory copy. Extremely fast.
                    DownLookup[target].CopyFrom(masterDowns);
                    HeldLookup[target].CopyFrom(masterHelds);
                    UpLookup[target].CopyFrom(masterUps);
                    AxisLookup[target].CopyFrom(masterAxes);
                }
            }
        }
    }
}