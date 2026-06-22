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
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation |
                       WorldSystemFilterFlags.ServerSimulation)]
    public partial struct ControlAuthoritySystem : ISystem
    {
        private ComponentLookup<InputState> _states;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<InputRegistry>();
            state.RequireForUpdate<Controllable>();
            _states = state.GetComponentLookup<InputState>(true);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            _states.Update(ref state);

            var registry = SystemAPI.GetSingleton<InputRegistry>();

            state.Dependency = new AuthorityJob
            {
                Registry = registry.ProviderByPlayer,
                States = _states,
                DeltaTime = SystemAPI.Time.DeltaTime
            }.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(Controllable))]
        private partial struct AuthorityJob : IJobEntity
        {
            [ReadOnly] [NativeDisableContainerSafetyRestriction]
            public NativeArray<Entity> Registry;

            [ReadOnly] [NativeDisableContainerSafetyRestriction]
            public ComponentLookup<InputState> States;

            public float DeltaTime;

            private void Execute(in PlayerId id, in OverridePolicy policy, ref OverrideState authority,
                EnabledRefRW<PlayerOverride> driving)
            {
                if (policy.Trigger == OverrideTrigger.Manual) return;

                var active = false;
                if (InputAccess.TryGetState(Registry, States, id.Value, out var state))
                {
                    active = OverrideDecision.IsActive(policy.Trigger,
                        !state.Down.AllFalse, !state.Held.AllFalse,
                        state.Down[policy.TriggerActionId], state.Held[policy.TriggerActionId]);
                }

                OverrideDecision.Step(active, driving.ValueRO, authority.IdleSeconds, policy.ReleaseIdleSeconds,
                    DeltaTime, out var nextDriving, out var nextIdle);

                driving.ValueRW = nextDriving;
                authority.IdleSeconds = nextIdle;
            }
        }
    }
}