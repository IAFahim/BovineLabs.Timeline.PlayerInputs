using BovineLabs.Core;
using Unity.Entities;

namespace PlayerInputs.PlayerInputs.Data
{
    public partial struct InputCurrentFacet : IFacet
    {
        public EnabledRefRO<ECSPlayerInputActiveThisFrame> Active;
        public EnabledRefRO<InputAttack> Attack;
        public EnabledRefRO<InputInteract> Interact;
        public EnabledRefRO<InputCrouch> Crouch;
        public EnabledRefRO<InputJump> Jump;
        public EnabledRefRO<InputPrevious> Prev;
        public EnabledRefRO<InputNext> Next;
        public EnabledRefRO<InputSprint> Sprint;

        public EnabledRefRO<PlayerMoveInputActive> MoveActive;
        public EnabledRefRO<PlayerLookInputActive> LookActive;

        public RefRO<PlayerMoveInput> Move;
        public RefRO<PlayerLookInput> Look;
    }

    public partial struct InputPreviousFacet : IFacet
    {
        public EnabledRefRW<ECSPlayerInputActivePreviousFrame> Active;
        public EnabledRefRW<InputAttackPrevious> Attack;
        public EnabledRefRW<InputInteractPrevious> Interact;
        public EnabledRefRW<InputCrouchPrevious> Crouch;
        public EnabledRefRW<InputJumpPrevious> Jump;
        public EnabledRefRW<InputPreviousPrevious> Prev;
        public EnabledRefRW<InputNextPrevious> Next;
        public EnabledRefRW<InputSprintPrevious> Sprint;

        public EnabledRefRW<PlayerMoveInputActivePrevious> MoveActive;
        public EnabledRefRW<PlayerLookInputActivePrevious> LookActive;

        public RefRW<PlayerMoveInputPrevious> Move;
        public RefRW<PlayerLookPrevious> Look;
    }
}
