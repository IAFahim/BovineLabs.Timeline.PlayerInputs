using BovineLabs.Core.Collections;
using BovineLabs.Timeline.PlayerInputs.Data;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace BovineLabs.Timeline.PlayerInputs
{
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [UpdateAfter(typeof(InputRegistrySystem))]
    [UpdateAfter(typeof(ProviderSyncSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.ServerSimulation)]
    public partial struct ControlAuthoritySystem : ISystem
    {
        private ComponentLookup<InputState> states;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<InputRegistry>();
            state.RequireForUpdate<Controllable>();
            states = state.GetComponentLookup<InputState>(true);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            states.Update(ref state);

            var registry = SystemAPI.GetSingleton<InputRegistry>();

            state.Dependency = new AuthorityJob
            {
                Registry = registry.ProviderByPlayer,
                States = states,
                DeltaTime = SystemAPI.Time.DeltaTime,
            }.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(Controllable))]
        private partial struct AuthorityJob : IJobEntity
        {
            [ReadOnly] [NativeDisableContainerSafetyRestriction] public NativeArray<Entity> Registry;
            [ReadOnly] [NativeDisableContainerSafetyRestriction] public ComponentLookup<InputState> States;
            public float DeltaTime;

            private void Execute(in PlayerId id, in OverridePolicy policy, ref OverrideState authority,
                EnabledRefRW<PlayerOverride> driving)
            {
                if (policy.Trigger == OverrideTrigger.Manual) return;

                if (IsActive(id.Value, policy))
                {
                    authority.IdleSeconds = 0f;
                    driving.ValueRW = true;
                    return;
                }

                if (!driving.ValueRO || policy.ReleaseIdleSeconds <= 0f) return;

                authority.IdleSeconds += DeltaTime;
                if (authority.IdleSeconds >= policy.ReleaseIdleSeconds)
                {
                    driving.ValueRW = false;
                    authority.IdleSeconds = 0f;
                }
            }

            private bool IsActive(byte playerId, in OverridePolicy policy)
            {
                if (!InputAccess.TryGetState(Registry, States, playerId, out var state))
                    return false;

                return policy.Trigger switch
                {
                    OverrideTrigger.AnyInput => !state.Down.AllFalse || !state.Held.AllFalse,
                    OverrideTrigger.Action => state.Down[policy.TriggerActionId] || state.Held[policy.TriggerActionId],
                    _ => false,
                };
            }
        }
    }
}
