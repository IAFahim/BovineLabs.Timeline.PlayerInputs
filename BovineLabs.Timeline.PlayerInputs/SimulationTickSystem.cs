using BovineLabs.Timeline.PlayerInputs.Data;
using Unity.Burst;
using Unity.Entities;

namespace BovineLabs.Timeline.PlayerInputs
{
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [UpdateBefore(typeof(InputRegistrySystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation |
                       WorldSystemFilterFlags.ServerSimulation)]
    public partial struct SimulationTickSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            var entity = state.EntityManager.CreateEntity();
            state.EntityManager.SetName(entity, "SimulationTick");
            state.EntityManager.AddComponentData(entity, new SimulationTick { Value = 0 });
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            ref var tick = ref SystemAPI.GetSingletonRW<SimulationTick>().ValueRW;
            tick.Value++;
        }
    }
}
