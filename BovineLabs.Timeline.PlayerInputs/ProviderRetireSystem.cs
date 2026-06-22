using BovineLabs.Timeline.PlayerInputs.Data;
using Unity.Burst;
using Unity.Entities;

namespace BovineLabs.Timeline.PlayerInputs
{
    [UpdateInGroup(typeof(TimelineComponentAnimationGroup))]
    [UpdateAfter(typeof(CommandSequenceSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation |
                       WorldSystemFilterFlags.ServerSimulation)]
    public partial struct ProviderRetireSystem : ISystem
    {
        private EntityQuery retiring;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            retiring = SystemAPI.QueryBuilder().WithAll<ProviderRetiring>().Build();
            state.RequireForUpdate(retiring);
        }

        public void OnUpdate(ref SystemState state)
        {
            state.EntityManager.DestroyEntity(retiring);
        }
    }
}