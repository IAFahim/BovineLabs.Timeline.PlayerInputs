using PlayerInputs.PlayerInputs.Data;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities;

namespace PlayerInputs.PlayerInputs.Systems
{
    [UpdateBefore(typeof(PlayerInputSystem))]
    public partial struct PlayerInputPreviousStateSystem : ISystem
    {
        private InputCurrentFacet.TypeHandle currentHandle;
        private InputPreviousFacet.TypeHandle previousHandle;
        private EntityQuery query;

        public void OnCreate(ref SystemState state)
        {
            currentHandle.Create(ref state);
            previousHandle.Create(ref state);

            query = SystemAPI.QueryBuilder().WithAll<PlayerMoveInput, PlayerMoveInputPrevious>().Build();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            currentHandle.Update(ref state);
            previousHandle.Update(ref state);

            var job = new ShiftInputStateJob
            {
                CurrentHandle = currentHandle,
                PreviousHandle = previousHandle
            };

            state.Dependency = job.ScheduleParallel(query, state.Dependency);
        }
    }

    [BurstCompile]
    public struct ShiftInputStateJob : IJobChunk
    {
        public InputCurrentFacet.TypeHandle CurrentHandle;
        public InputPreviousFacet.TypeHandle PreviousHandle;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask,
            in v128 chunkEnabledMask)
        {
            var currentResolved = CurrentHandle.Resolve(chunk);
            var previousResolved = PreviousHandle.Resolve(chunk);

            for (var i = 0; i < chunk.Count; i++)
            {
                var current = currentResolved[i];
                var previous = previousResolved[i];

                previous.Active.ValueRW = current.Active.ValueRO;
                previous.Attack.ValueRW = current.Attack.ValueRO;
                previous.Interact.ValueRW = current.Interact.ValueRO;
                previous.Crouch.ValueRW = current.Crouch.ValueRO;
                previous.Jump.ValueRW = current.Jump.ValueRO;
                previous.Prev.ValueRW = current.Prev.ValueRO;
                previous.Next.ValueRW = current.Next.ValueRO;
                previous.Sprint.ValueRW = current.Sprint.ValueRO;

                previous.MoveActive.ValueRW = current.MoveActive.ValueRO;
                previous.LookActive.ValueRW = current.LookActive.ValueRO;

                previous.Move.ValueRW.Value = current.Move.ValueRO.Value;
                previous.Look.ValueRW.Value = current.Look.ValueRO.Value;
            }
        }
    }
}
