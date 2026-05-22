using System;
using BovineLabs.Core.Collections;
using BovineLabs.Reaction.Data.Conditions;
using BovineLabs.Reaction.Data.Core;
using Unity.Entities;
using Unity.Mathematics;

namespace BovineLabs.Timeline.PlayerInputs.Data
{
    public enum InputPhase : byte
    {
        Down = 0,
        Held = 1,
        Up = 2
    }

    public enum CommandMode : byte
    {
        None = 0,

        Contains = 1,
        Consume = 2,
        FirstConsume = 3,
        LastConsume = 4,

        OrderedContains = 16,
        OrderedConsume = 17,
        OrderedFirstConsume = 18,
        OrderedLastConsume = 19,

        NotContains = 32,
        NotFirst = 33,
        NotLast = 34
    }

    [Flags]
    public enum AxisTransformMode : byte
    {
        Position = 0,
        Velocity = 1 << 0,
        KeepLastPosition = 1 << 1,
        LocalSpace = 1 << 2,
        CameraRelative = 1 << 3,
        IgnoreParentRotation = 1 << 4,

        // Physics modes — writes to PhysicsVelocity, never LocalTransform.
        // Planar component is controlled; perpendicular (e.g. gravity) is preserved.
        RigidbodyVelocity = 1 << 5,  // sets linear velocity to input direction each frame
        RigidbodyForce = 1 << 6,     // accumulates velocity via force; Drag dissipates it
        RigidbodyImpulse = 1 << 7,   // fires a single impulse on input rising-edge
    }

    public struct InputState : IComponentData
    {
        public BitArray256 Down;
        public BitArray256 Held;
        public BitArray256 Up;
    }

    public struct ActiveBufferMask : IComponentData
    {
        public BitArray256 Value;
    }

    public struct InputAxis : IBufferElementData
    {
        public byte ActionId;
        public float2 Value;
    }

    [InternalBufferCapacity(32)]
    public struct InputHistory : IBufferElementData
    {
        public byte ActionId;
        public InputPhase Phase;
        public uint Tick;
    }

    public struct PlayerId : IComponentData
    {
        public byte Value;
    }

    public struct ProviderTag : IComponentData
    {
    }

    public struct ConsumerTag : IComponentData
    {
    }

    public struct InputSource : IComponentData
    {
        public Entity Provider;
    }

    public struct PlayerMoveInput : IComponentData
    {
        public float2 Value;
    }

    public struct CommandStep
    {
        public byte ActionId;
        public CommandMode Mode;
        public InputPhase Phase;
    }

    public struct CommandSequence
    {
        public BlobArray<CommandStep> Steps;
        public ConditionKey Condition;
        public int Value;
    }

    public struct CommandBlob
    {
        public BlobArray<CommandSequence> Sequences;
    }

    public struct CommandSequenceConfig : IComponentData
    {
        public BlobAssetReference<CommandBlob> Blob;
        public Entity RouteEntity;
    }

    public struct CommandSequenceState : IComponentData
    {
        public bool IsCompleted;
    }

    public struct BufferWindowConfig : IComponentData
    {
        public BitArray256 AllowedActions;
    }

    public struct BufferClearConfig : IComponentData, IEnableableComponent
    {
        public BitArray256 ActionMask;
    }

    public struct AxisTransformConfig : IComponentData
    {
        public Target ReadRootFrom;
        public ushort ConsumerLinkKey;
        public byte ActionId;
        public float Range;
        public float3 Plane;
        public float Smoothing;
        public float ClampRadius;

        // RigidbodyForce: velocity dissipation per second (0 = no drag).
        // RigidbodyVelocity: ignored (smoothing lerps to zero instead).
        public float Drag;

        public AxisTransformMode Mode;

        public bool ResetOnNoInput;
        public Target EventRouteTo;
        public ushort EventRouteLinkKey;
        public ConditionKey OnInputStart;
        public ConditionKey OnInputEnd;
    }

    public static class AxisTransformModeExtensions
    {
        public static bool IsVelocity(this AxisTransformMode m)
            => (m & AxisTransformMode.Velocity) != 0;

        public static bool KeepLast(this AxisTransformMode m)
            => (m & AxisTransformMode.KeepLastPosition) != 0;

        public static bool IsLocal(this AxisTransformMode m)
            => (m & AxisTransformMode.LocalSpace) != 0;

        public static bool IsCameraRelative(this AxisTransformMode m)
            => (m & AxisTransformMode.CameraRelative) != 0;

        public static bool IgnoreParentRotation(this AxisTransformMode m)
            => (m & AxisTransformMode.IgnoreParentRotation) != 0;

        public static bool IsRigidbodyVelocity(this AxisTransformMode m)
            => (m & AxisTransformMode.RigidbodyVelocity) != 0;

        public static bool IsRigidbodyForce(this AxisTransformMode m)
            => (m & AxisTransformMode.RigidbodyForce) != 0;

        public static bool IsRigidbodyImpulse(this AxisTransformMode m)
            => (m & AxisTransformMode.RigidbodyImpulse) != 0;

        public static bool IsRigidbody(this AxisTransformMode m)
            => (m & (AxisTransformMode.RigidbodyVelocity
                   | AxisTransformMode.RigidbodyForce
                   | AxisTransformMode.RigidbodyImpulse)) != 0;
    }

    public struct AxisTransformState : IComponentData
    {
        public float3 Origin;
        public float2 LastInput;
        public float3 CurrentPosition;
        public float3 Velocity;
        public bool HasInput;
        public bool WasInputActive;
        public bool Initialized;
    }
}