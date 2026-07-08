using BovineLabs.Timeline.PlayerInputs.Data;
using Unity.Burst;
using Unity.Collections;
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

            state.Dependency = new AuthorityJob
            {
                Slots = SystemAPI.GetSingletonBuffer<ProviderSlot>(true),
                States = _states,
                DeltaTime = SystemAPI.Time.DeltaTime
            }.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(Controllable))]
        [WithPresent(typeof(PlayerOverride), typeof(TimelineOverride))]
        private partial struct AuthorityJob : IJobEntity
        {
            [ReadOnly] public DynamicBuffer<ProviderSlot> Slots;

            [ReadOnly] public ComponentLookup<InputState> States;

            public float DeltaTime;

            private void Execute(in PlayerId id, in OverridePolicy policy, ref OverrideState authority,
                EnabledRefRW<PlayerOverride> driving, EnabledRefRO<TimelineOverride> timelineOverride)
            {
                if (policy.Trigger == OverrideTrigger.Manual) return;

                // A ControlOverride clip owns the PlayerOverride bit while it drives - never fight it.
                if (timelineOverride.ValueRO) return;

                // Authority keys off the LIVE human input, never the synthetic feed an override would enable, so read
                // the human slot directly (a self-triggering loop otherwise).
                var active = false;
                var human = Slots[id.Value].Human;
                if (human != Entity.Null && States.TryGetComponent(human, out var state))
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