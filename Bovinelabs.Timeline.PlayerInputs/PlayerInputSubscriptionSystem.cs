using BovineLabs.Core.Groups;
using PlayerInputs.Data;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace PlayerInputs.Systems
{
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [UpdateAfter(typeof(PlayerInputRegistrySystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation)]
    public partial struct PlayerInputSubscriptionSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<PlayerInputRegistryTag>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var registryEntity = SystemAPI.GetSingletonEntity<PlayerInputRegistryTag>();
            var links = SystemAPI.GetBuffer<PlayerInputLink>(registryEntity);

            var map = new NativeHashMap<byte, Entity>(links.Length, state.WorldUpdateAllocator);
            foreach (var link in links)
            {
                map.TryAdd(link.PlayerId, link.Provider);
            }

            state.Dependency = new AssignSourceJob
            {
                ProvidersMap = map,
            }.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(InputConsumerTag))]
        private partial struct AssignSourceJob : IJobEntity
        {
            [ReadOnly] public NativeHashMap<byte, Entity> ProvidersMap;

            private void Execute(in PlayerId id, ref InputSource source)
            {
                if (ProvidersMap.TryGetValue(id.Value, out var providerEntity))
                {
                    source.Provider = providerEntity;
                }
                else
                {
                    source.Provider = Entity.Null;
                }
            }
        }
    }
}

