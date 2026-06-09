using BovineLabs.Timeline.PlayerInputs.Data;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace BovineLabs.Timeline.PlayerInputs
{
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation |
                       WorldSystemFilterFlags.ServerSimulation)]
    public partial struct InputRegistrySystem : ISystem
    {
        private const int SlotCount = 256;

        public void OnCreate(ref SystemState state)
        {
            var entity = state.EntityManager.CreateEntity();
            state.EntityManager.SetName(entity, "InputRegistry");
            state.EntityManager.AddBuffer<PlayerJoined>(entity);
            state.EntityManager.AddBuffer<PlayerLeft>(entity);
            state.EntityManager.AddComponentData(entity, new InputRegistry
            {
                ProviderByPlayer = new NativeArray<Entity>(SlotCount, Allocator.Persistent),
                Version = 0
            });
        }

        public void OnDestroy(ref SystemState state)
        {
            if (SystemAPI.TryGetSingleton<InputRegistry>(out var registry) && registry.ProviderByPlayer.IsCreated)
                registry.ProviderByPlayer.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var next = CollectionHelper.CreateNativeArray<Entity>(
                SlotCount, state.WorldUpdateAllocator);

            foreach (var (id, entity) in
                     SystemAPI.Query<RefRO<PlayerId>>().WithAll<ProviderTag>().WithEntityAccess())
            {
                var slot = id.ValueRO.Value;
                var existing = next[slot];
                if (existing != Entity.Null)
                {
                    // Deterministic tie-break: keep the lower entity so the registry
                    // is independent of archetype/query iteration order across
                    // structural changes. Without this, which duplicate "wins" could
                    // differ between client and server for the same world state.
                    ReportDuplicate(slot);
                    if (existing.Index <= entity.Index) continue;
                }

                next[slot] = entity;
            }

            ref var registry = ref SystemAPI.GetSingletonRW<InputRegistry>().ValueRW;
            var current = registry.ProviderByPlayer;

            var joined = SystemAPI.GetSingletonBuffer<PlayerJoined>();
            var left = SystemAPI.GetSingletonBuffer<PlayerLeft>();
            joined.Clear();
            left.Clear();

            for (var slot = 0; slot < SlotCount; slot++)
            {
                var before = current[slot];
                var after = next[slot];
                if (before == after) continue;

                if (before != Entity.Null) left.Add(new PlayerLeft { PlayerId = (byte)slot });
                if (after != Entity.Null) joined.Add(new PlayerJoined { PlayerId = (byte)slot, Provider = after });
            }

            next.CopyTo(current);
            registry.Version++;
        }

        [BurstDiscard]
        private static void ReportDuplicate(int slot)
        {
            Debug.LogError($"Duplicate provider for PlayerId {slot}; keeping first.");
        }
    }
}