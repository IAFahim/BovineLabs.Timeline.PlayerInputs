using PlayerInputs.Data;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace PlayerInputs.Systems
{
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial struct PlayerInputRegistrySystem : ISystem
    {
        private NativeHashMap<byte, Entity> _previousProviders;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            _previousProviders = new NativeHashMap<byte, Entity>(4, Allocator.Persistent);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            if (_previousProviders.IsCreated)
                _previousProviders.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            // Ensure single immutable-by-default truth exists
            if (!SystemAPI.TryGetSingletonEntity<PlayerInputRegistryTag>(out var registryEntity))
            {
                registryEntity = state.EntityManager.CreateEntity();
                state.EntityManager.AddComponent<PlayerInputRegistryTag>(registryEntity);
                state.EntityManager.AddBuffer<PlayerInputLink>(registryEntity);
                state.EntityManager.AddBuffer<PlayerJoinedEventBuffer>(registryEntity);
                state.EntityManager.AddBuffer<PlayerLeftEventBuffer>(registryEntity);
            }

            var currentProviders = new NativeHashMap<byte, Entity>(4, state.WorldUpdateAllocator);

            foreach (var (id, entity) in SystemAPI.Query<RefRO<PlayerId>>().WithAll<InputProviderTag>().WithEntityAccess())
            {
                currentProviders.Add(id.ValueRO.Value, entity);
            }

            var joinedBuffer = SystemAPI.GetBuffer<PlayerJoinedEventBuffer>(registryEntity);
            var leftBuffer = SystemAPI.GetBuffer<PlayerLeftEventBuffer>(registryEntity);
            
            joinedBuffer.Clear();
            leftBuffer.Clear();

            // Detect Joins
            foreach (var kvp in currentProviders)
            {
                if (!_previousProviders.ContainsKey(kvp.Key))
                {
                    joinedBuffer.Add(new PlayerJoinedEventBuffer { PlayerId = kvp.Key, Provider = kvp.Value });
                }
            }

            // Detect Leaves
            foreach (var kvp in _previousProviders)
            {
                if (!currentProviders.ContainsKey(kvp.Key))
                {
                    leftBuffer.Add(new PlayerLeftEventBuffer { PlayerId = kvp.Key });
                }
            }

            // Swap States
            _previousProviders.Clear();
            foreach (var kvp in currentProviders)
            {
                _previousProviders.Add(kvp.Key, kvp.Value);
            }

            // Update Link Buffer
            var linkBuffer = SystemAPI.GetBuffer<PlayerInputLink>(registryEntity);
            linkBuffer.Clear();
            foreach (var kvp in currentProviders)
            {
                linkBuffer.Add(new PlayerInputLink { PlayerId = kvp.Key, Provider = kvp.Value });
            }
        }
    }
}