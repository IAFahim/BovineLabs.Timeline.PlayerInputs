using BovineLabs.Core;
using BovineLabs.Core.Collections;
using BovineLabs.Timeline.PlayerInputs.Data;
using BovineLabs.Timeline.PlayerInputs.Flow.Data;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace BovineLabs.Timeline.PlayerInputs
{
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation |
                       WorldSystemFilterFlags.ServerSimulation)]
    public partial struct InputRegistrySystem : ISystem
    {
        private const int SlotCount = 256;

        private ComponentLookup<SyntheticProviderTag> synthetic;
        private ComponentLookup<ProviderRetiring> retiring;
        private ComponentLookup<ProviderSeq> seqs;

        // Per-slot latch so a duplicate seat logs exactly ONCE per occurrence; the bit clears when the duplicate
        // condition resolves so a NEW duplicate (after a clean frame) logs again.
        private BitArray256 reportedDup;

        public void OnCreate(ref SystemState state)
        {
            synthetic = state.GetComponentLookup<SyntheticProviderTag>(true);
            retiring = state.GetComponentLookup<ProviderRetiring>(true);
            seqs = state.GetComponentLookup<ProviderSeq>(true);

            var entity = state.EntityManager.CreateEntity();
            state.EntityManager.SetName(entity, "InputRegistry");
            state.EntityManager.AddBuffer<PlayerJoined>(entity);
            state.EntityManager.AddBuffer<PlayerLeft>(entity);
            var slots = state.EntityManager.AddBuffer<ProviderSlot>(entity);
            slots.Resize(SlotCount, NativeArrayOptions.ClearMemory);
            state.EntityManager.AddComponentData(entity, new InputRegistry { Version = 0 });
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            synthetic.Update(ref state);
            retiring.Update(ref state);
            seqs.Update(ref state);

            var nextHuman = CollectionHelper.CreateNativeArray<Entity>(SlotCount, state.WorldUpdateAllocator);
            var nextSynthetic = CollectionHelper.CreateNativeArray<Entity>(SlotCount, state.WorldUpdateAllocator);
            var dupThisFrame = default(BitArray256);

            foreach (var (id, entity) in
                     SystemAPI.Query<RefRO<PlayerId>>().WithAll<ProviderTag>().WithEntityAccess())
            {
                var slot = id.ValueRO.Value;
                var isSynthetic = synthetic.HasComponent(entity);
                var existing = isSynthetic ? nextSynthetic[slot] : nextHuman[slot];

                if (existing != Entity.Null)
                {
                    // Prefer the live provider over a retiring one on the same sub-slot.
                    var existingRetiring = retiring.HasComponent(existing);
                    var entityRetiring = retiring.HasComponent(entity);
                    if (existingRetiring != entityRetiring)
                    {
                        if (entityRetiring) continue;
                    }
                    else
                    {
                        // Same-kind duplicate on the same seat: keep the deterministic winner and flag the slot.
                        dupThisFrame[slot] = true;
                        if (ExistingWins(existing, entity)) continue;
                    }
                }

                if (isSynthetic) nextSynthetic[slot] = entity;
                else nextHuman[slot] = entity;
            }

            ref var registry = ref SystemAPI.GetSingletonRW<InputRegistry>().ValueRW;
            var slots = SystemAPI.GetSingletonBuffer<ProviderSlot>();

            var joined = SystemAPI.GetSingletonBuffer<PlayerJoined>();
            var left = SystemAPI.GetSingletonBuffer<PlayerLeft>();
            joined.Clear();
            left.Clear();

            var changed = false;
            for (var slot = 0; slot < SlotCount; slot++)
            {
                var before = slots[slot];
                var afterHuman = nextHuman[slot];
                var afterSynthetic = nextSynthetic[slot];

                // Join/Leave events track the HUMAN slot only - synthetic providers are feeds, not "players".
                if (before.Human != afterHuman)
                {
                    if (before.Human != Entity.Null) left.Add(new PlayerLeft { PlayerId = (byte)slot });
                    if (afterHuman != Entity.Null)
                        joined.Add(new PlayerJoined { PlayerId = (byte)slot, Provider = afterHuman });
                    changed = true;
                }

                if (before.Synthetic != afterSynthetic) changed = true;

                slots[slot] = new ProviderSlot { Human = afterHuman, Synthetic = afterSynthetic };

                // Report a NEW duplicate once via the Burst-safe BLLogger singleton — this OnUpdate is
                // Burst-compiled, so a UnityEngine.Debug.LogError would be [BurstDiscard]'d out of a player
                // build (exactly where the diagnostic is needed). The latch only advances to "reported"
                // once a logger actually exists, so the message is never lost if the logger singleton
                // appears a frame late; it clears when the slot is clean again so a fresh duplicate re-logs.
                if (dupThisFrame[slot])
                {
                    if (!reportedDup[slot] && SystemAPI.TryGetSingleton<BLLogger>(out var logger))
                    {
                        var msg = new FixedString512Bytes();
                        msg.Append((FixedString128Bytes)"Duplicate same-kind provider for PlayerId ");
                        msg.Append(slot);
                        msg.Append((FixedString128Bytes)"; keeping the stable winner.");
                        logger.LogError512(msg);
                        reportedDup[slot] = true;
                    }
                }
                else
                {
                    reportedDup[slot] = false;
                }
            }

            if (changed) registry.Version++;
        }

        // Lowest ProviderSeq wins; when either provider lacks a ProviderSeq stamp, fall back to lowest Entity index.
        private bool ExistingWins(Entity existing, Entity candidate)
        {
            if (seqs.TryGetComponent(existing, out var se) && seqs.TryGetComponent(candidate, out var sc))
                return se.Value <= sc.Value;

            return existing.Index <= candidate.Index;
        }
    }
}
