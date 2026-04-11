using Bovinelabs.Timeline.PlayerInputs.Data;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace Bovinelabs.Timeline.PlayerInputs
{
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial struct PlayerInputRegistrySystem : ISystem
    {
        private NativeHashMap<byte, Entity> _previousProviders;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            this._previousProviders = new NativeHashMap<byte, Entity>(4, Allocator.Persistent);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            if (this._previousProviders.IsCreated)
                this._previousProviders.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
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
            var linkBuffer = SystemAPI.GetBuffer<PlayerInputLink>(registryEntity);

            joinedBuffer.Clear();
            leftBuffer.Clear();
            linkBuffer.Clear();

            // S-Tier Fix: GetKeyArray bypasses enumerator struct ambiguity across Unity versions
            var currentKeys = currentProviders.GetKeyArray(state.WorldUpdateAllocator);
            var previousKeys = this._previousProviders.GetKeyArray(state.WorldUpdateAllocator);

            foreach (var key in currentKeys)
            {
                var provider = currentProviders[key];
                
                if (!this._previousProviders.ContainsKey(key))
                {
                    joinedBuffer.Add(new PlayerJoinedEventBuffer { PlayerId = key, Provider = provider });
                }
                
                linkBuffer.Add(new PlayerInputLink { PlayerId = key, Provider = provider });
            }

            foreach (var key in previousKeys)
            {
                if (!currentProviders.ContainsKey(key))
                {
                    leftBuffer.Add(new PlayerLeftEventBuffer { PlayerId = key });
                }
            }

            this._previousProviders.Clear();
            foreach (var key in currentKeys)
            {
                this._previousProviders.Add(key, currentProviders[key]);
            }
        }
    }
}