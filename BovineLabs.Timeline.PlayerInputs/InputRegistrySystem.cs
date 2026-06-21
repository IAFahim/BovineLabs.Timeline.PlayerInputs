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
                    // A retiring provider (a player who just left, kept one tick to deliver its closing
                    // release) must still occupy its slot when it is the ONLY provider for that id - that is
                    // how consumers read the release. But if a fresh, live provider also claims the id (a
                    // same-id leave+rejoin overlap), the live one must win: route the rejoined player's input,
                    // not the dying corpse. This is the expected overlap, so it is NOT a duplicate error.
                    var existingRetiring = SystemAPI.HasComponent<ProviderRetiring>(existing);
                    var entityRetiring = SystemAPI.HasComponent<ProviderRetiring>(entity);
                    if (existingRetiring != entityRetiring)
                    {
                        if (entityRetiring) continue;   // keep the live existing, ignore the corpse
                        next[slot] = entity;            // replace the corpse with the live provider
                        continue;
                    }

                    // Two live (or two retiring) providers for one id is a genuine misconfiguration.
                    // Deterministic tie-break: keep the lower entity so the registry is independent of
                    // archetype/query iteration order across structural changes (client/server agree).
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