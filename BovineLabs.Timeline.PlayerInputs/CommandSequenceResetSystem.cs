using BovineLabs.Timeline.Data;
using BovineLabs.Timeline.PlayerInputs.Data;
using Unity.Burst;
using Unity.Entities;

namespace BovineLabs.Timeline.PlayerInputs
{
    [UpdateInGroup(typeof(TimelineComponentAnimationGroup))]
    [UpdateAfter(typeof(ConsumerHistorySystem))]
    public partial struct CommandSequenceResetSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            // Cleanly reset Sequence State when clip enters active state
            foreach (var commandState in SystemAPI.Query<RefRW<CommandSequenceState>>()
                         .WithAll<ClipActive>()
                         .WithNone<ClipActivePrevious>())
            {
                commandState.ValueRW.IsCompleted = false;
            }
        }
    }
}