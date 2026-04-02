using System;
using PlayerInputs.PlayerInputs.Data;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace PlayerInputs.PlayerInputs.Authoring
{
    [Serializable]
    public struct InputState
    {
        public bool active;
        public bool previous;
    }

    public class PlayerInputAuthoring : MonoBehaviour
    {
        public byte PlayerID;

        [Header("Initial Input State")] public InputState attack;

        public InputState interact;
        public InputState crouch;
        public InputState jump;
        public InputState previous;
        public InputState next;
        public InputState sprint;

        public bool moveInputBlocked;
        public bool moveInputActive;
        public bool lookInputBlocked;
        public bool lookInputActive;
        public bool inputActiveThisFrame;
        public bool inputActivePreviousFrame;

        [Header("Movement Defaults")] public Vector2 defaultMoveInput = Vector2.zero;

        public Vector2 defaultLookInput = Vector2.zero;

        public class PlayerInputBaker : Baker<PlayerInputAuthoring>
        {
            public override void Bake(PlayerInputAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);

                AddComponent(entity, new ECSPlayerInputID { ID = authoring.PlayerID });

                AddComponent(entity, new PlayerMoveInput
                {
                    Value = new float2(authoring.defaultMoveInput.x, authoring.defaultMoveInput.y)
                });

                AddComponent<PlayerMoveInputActive>(entity);
                AddComponent<PlayerMoveInputActivePrevious>(entity);

                SetComponentEnabled<PlayerMoveInputActive>(entity, authoring.moveInputActive);
                SetComponentEnabled<PlayerMoveInputActivePrevious>(entity, false);

                AddComponent(entity, new PlayerMoveInputPrevious
                {
                    Value = new float2(authoring.defaultMoveInput.x, authoring.defaultMoveInput.y)
                });

                AddComponent<PlayerLookInputActive>(entity);
                AddComponent<PlayerLookInputActivePrevious>(entity);

                SetComponentEnabled<PlayerLookInputActive>(entity, authoring.lookInputActive);
                SetComponentEnabled<PlayerLookInputActivePrevious>(entity, false);

                AddComponent(entity, new PlayerLookInput
                {
                    Value = new float2(authoring.defaultLookInput.x, authoring.defaultLookInput.y)
                });
                AddComponent(entity, new PlayerLookPrevious
                {
                    Value = new float2(authoring.defaultLookInput.x, authoring.defaultLookInput.y)
                });

                AddComponent<ECSPlayerInputActiveThisFrame>(entity);
                AddComponent<ECSPlayerInputActivePreviousFrame>(entity);
                SetComponentEnabled<ECSPlayerInputActiveThisFrame>(entity, authoring.inputActiveThisFrame);
                SetComponentEnabled<ECSPlayerInputActivePreviousFrame>(entity, authoring.inputActivePreviousFrame);

                Setup<InputAttack, InputAttackPrevious>(entity, authoring.attack);
                Setup<InputInteract, InputInteractPrevious>(entity, authoring.interact);
                Setup<InputCrouch, InputCrouchPrevious>(entity, authoring.crouch);
                Setup<InputJump, InputJumpPrevious>(entity, authoring.jump);
                Setup<InputPrevious, InputPreviousPrevious>(entity, authoring.previous);
                Setup<InputNext, InputNextPrevious>(entity, authoring.next);
                Setup<InputSprint, InputSprintPrevious>(entity, authoring.sprint);
            }

            private void Setup<TCurrent, TPrevious>(Entity entity, InputState state)
                where TCurrent : unmanaged, IComponentData, IEnableableComponent
                where TPrevious : unmanaged, IComponentData, IEnableableComponent
            {
                AddComponent<TCurrent>(entity);
                SetComponentEnabled<TCurrent>(entity, state.active);

                AddComponent<TPrevious>(entity);
                SetComponentEnabled<TPrevious>(entity, state.previous);
            }
        }
    }
}
