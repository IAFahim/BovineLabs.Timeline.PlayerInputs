using PlayerInputs.PlayerInputs.Data;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace PlayerInputs.PlayerInputs.Systems
{
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial struct PlayerInputSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BeginInitializationEntityCommandBufferSystem.Singleton>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var ecbSingleton = SystemAPI.GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

            foreach (var (playerID, entity) in SystemAPI.Query<RefRO<ECSPlayerInputID>>()
                         .WithNone<PlayerInputRegisteredTag, BackingInputEntityTag>().WithEntityAccess())
            foreach (var (backingID, subscribers) in SystemAPI
                         .Query<RefRO<ECSPlayerInputID>, DynamicBuffer<InputSubscribedEntity>>()
                         .WithAll<BackingInputEntityTag>())
                if (backingID.ValueRO.ID == playerID.ValueRO.ID)
                {
                    subscribers.Add(new InputSubscribedEntity { Value = entity });
                    ecb.AddComponent<PlayerInputRegisteredTag>(entity);
                    break;
                }
        }
    }
}
