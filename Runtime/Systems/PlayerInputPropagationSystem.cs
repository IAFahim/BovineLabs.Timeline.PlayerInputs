using BovineLabs.Core.Extensions;
using BovineLabs.Core.Iterators;
using PlayerInputs.PlayerInputs.Data;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;

namespace PlayerInputs.PlayerInputs.Systems
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PlayerInputPreviousStateSystem))]
    public partial struct PlayerInputPropagationSystem : ISystem
    {
        private InputCurrentFacet.TypeHandle currentHandle;
        private BufferTypeHandle<InputSubscribedEntity> subscribersHandle;
        private EntityQuery query;

        private UnsafeEnableableLookup enableableLookup;
        private UnsafeComponentLookup<PlayerMoveInput> moveLookup;
        private UnsafeComponentLookup<PlayerLookInput> lookLookup;

        public void OnCreate(ref SystemState state)
        {
            currentHandle.Create(ref state);
            subscribersHandle = state.GetBufferTypeHandle<InputSubscribedEntity>(true);

            query = SystemAPI.QueryBuilder()
                .WithAll<BackingInputEntityTag, InputSubscribedEntity>()
                .Build();

            enableableLookup = state.GetUnsafeEnableableLookup();
            moveLookup = state.GetUnsafeComponentLookup<PlayerMoveInput>();
            lookLookup = state.GetUnsafeComponentLookup<PlayerLookInput>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            currentHandle.Update(ref state);
            subscribersHandle.Update(ref state);

            moveLookup.Update(ref state);
            lookLookup.Update(ref state);

            var job = new PropagateInputJob
            {
                CurrentHandle = currentHandle,
                SubscribersHandle = subscribersHandle,
                EnableableLookup = enableableLookup,
                MoveLookup = moveLookup,
                LookLookup = lookLookup,

                ActiveType = ComponentType.ReadWrite<ECSPlayerInputActiveThisFrame>(),
                AttackType = ComponentType.ReadWrite<InputAttack>(),
                InteractType = ComponentType.ReadWrite<InputInteract>(),
                CrouchType = ComponentType.ReadWrite<InputCrouch>(),
                JumpType = ComponentType.ReadWrite<InputJump>(),
                PrevType = ComponentType.ReadWrite<InputPrevious>(),
                NextType = ComponentType.ReadWrite<InputNext>(),
                SprintType = ComponentType.ReadWrite<InputSprint>(),
                MoveActiveType = ComponentType.ReadWrite<PlayerMoveInputActive>(),
                LookActiveType = ComponentType.ReadWrite<PlayerLookInputActive>()
            };

            state.Dependency = job.ScheduleParallel(query, state.Dependency);
        }
    }

    [BurstCompile]
    public struct PropagateInputJob : IJobChunk
    {
        public InputCurrentFacet.TypeHandle CurrentHandle;
        [ReadOnly] public BufferTypeHandle<InputSubscribedEntity> SubscribersHandle;

        public UnsafeEnableableLookup EnableableLookup;
        public UnsafeComponentLookup<PlayerMoveInput> MoveLookup;
        public UnsafeComponentLookup<PlayerLookInput> LookLookup;

        public ComponentType ActiveType;
        public ComponentType AttackType;
        public ComponentType InteractType;
        public ComponentType CrouchType;
        public ComponentType JumpType;
        public ComponentType PrevType;
        public ComponentType NextType;
        public ComponentType SprintType;
        public ComponentType MoveActiveType;
        public ComponentType LookActiveType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask,
            in v128 chunkEnabledMask)
        {
            var currentResolved = CurrentHandle.Resolve(chunk);
            var subscribersAccessor = chunk.GetBufferAccessor(ref SubscribersHandle);

            for (var i = 0; i < chunk.Count; i++)
            {
                var current = currentResolved[i];
                var subscribers = subscribersAccessor[i];

                var active = current.Active.ValueRO;
                var attack = current.Attack.ValueRO;
                var interact = current.Interact.ValueRO;
                var crouch = current.Crouch.ValueRO;
                var jump = current.Jump.ValueRO;
                var prev = current.Prev.ValueRO;
                var next = current.Next.ValueRO;
                var sprint = current.Sprint.ValueRO;

                var moveActive = current.MoveActive.ValueRO;
                var lookActive = current.LookActive.ValueRO;
                var move = current.Move.ValueRO;
                var look = current.Look.ValueRO;

                for (var j = 0; j < subscribers.Length; j++)
                {
                    var sub = subscribers[j].Value;

                    if (!MoveLookup.HasComponent(sub)) continue;

                    EnableableLookup.SetComponentEnabled(sub, ActiveType, active);
                    EnableableLookup.SetComponentEnabled(sub, AttackType, attack);
                    EnableableLookup.SetComponentEnabled(sub, InteractType, interact);
                    EnableableLookup.SetComponentEnabled(sub, CrouchType, crouch);
                    EnableableLookup.SetComponentEnabled(sub, JumpType, jump);
                    EnableableLookup.SetComponentEnabled(sub, PrevType, prev);
                    EnableableLookup.SetComponentEnabled(sub, NextType, next);
                    EnableableLookup.SetComponentEnabled(sub, SprintType, sprint);

                    EnableableLookup.SetComponentEnabled(sub, MoveActiveType, moveActive);
                    EnableableLookup.SetComponentEnabled(sub, LookActiveType, lookActive);

                    MoveLookup[sub] = move;
                    LookLookup[sub] = look;
                }
            }
        }
    }
}
