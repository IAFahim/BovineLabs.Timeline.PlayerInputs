using System;
using System.Runtime.CompilerServices;
using Unity.Entities;
using Unity.Mathematics;

namespace PlayerInputs.PlayerInputs.Data
{
    public struct ECSPlayerInputActiveThisFrame : IComponentData, IEnableableComponent
    {
    }

    public struct ECSPlayerInputActivePreviousFrame : IComponentData, IEnableableComponent
    {
    }

    public struct InputAttack : IComponentData, IEnableableComponent
    {
    }

    public struct InputAttackPrevious : IComponentData, IEnableableComponent
    {
    }

    public struct InputInteract : IComponentData, IEnableableComponent
    {
    }

    public struct InputInteractPrevious : IComponentData, IEnableableComponent
    {
    }

    public struct InputCrouch : IComponentData, IEnableableComponent
    {
    }

    public struct InputCrouchPrevious : IComponentData, IEnableableComponent
    {
    }

    public struct InputJump : IComponentData, IEnableableComponent
    {
    }

    public struct InputJumpPrevious : IComponentData, IEnableableComponent
    {
    }

    public struct InputPrevious : IComponentData, IEnableableComponent
    {
    }

    public struct InputPreviousPrevious : IComponentData, IEnableableComponent
    {
    }

    public struct InputNext : IComponentData, IEnableableComponent
    {
    }

    public struct InputNextPrevious : IComponentData, IEnableableComponent
    {
    }

    public struct InputSprint : IComponentData, IEnableableComponent
    {
    }

    public struct InputSprintPrevious : IComponentData, IEnableableComponent
    {
    }

    public struct ECSPlayerInputID : IComponentData
    {
        public byte ID;
    }

    public struct PlayerMoveInputActive : IComponentData, IEnableableComponent
    {
    }

    public struct PlayerMoveInputActivePrevious : IComponentData, IEnableableComponent
    {
    }

    [Serializable]
    public struct PlayerMoveInput : IComponentData
    {
        public float2 Value;
    }

    [Serializable]
    public struct PlayerMoveInputPrevious : IComponentData
    {
        public float2 Value;
    }

    [Serializable]
    public struct PlayerLookInput : IComponentData
    {
        public float2 Value;
    }


    public struct PlayerLookInputActive : IComponentData, IEnableableComponent
    {
    }

    public struct PlayerLookInputActivePrevious : IComponentData, IEnableableComponent
    {
    }

    [Serializable]
    public struct PlayerLookPrevious : IComponentData
    {
        public float2 Value;
    }

    [InternalBufferCapacity(16)]
    public struct InputSubscribedEntity : IBufferElementData
    {
        public Entity Value;
    }

    public struct PlayerInputRegisteredTag : IComponentData
    {
    }

    public struct BackingInputEntityTag : IComponentData
    {
    }

    [Flags]
    public enum InputInitialState : byte
    {
        None = 0,
        Attack = 1 << 0,
        Interact = 1 << 1,
        Crouch = 1 << 2,
        Jump = 1 << 3,
        Previous = 1 << 4,
        Next = 1 << 5,
        Sprint = 1 << 6
    }

    public static class InputInitialStateImpl
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool HasFlagFast(this InputInitialState initialState, InputInitialState flag)
        {
            return (initialState & flag) != 0;
        }
    }
}
