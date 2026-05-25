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

    public enum AxisTransformMode : byte
    {
        Position = 0,
        Velocity = 1,
        RigidbodyVelocity = 2,
        RigidbodyForce = 3,
        RigidbodyImpulse = 4,
    }

    [Flags]
    public enum AxisTransformFlags : byte
    {
        None = 0,
        IgnoreParentRotation = 1 << 0,
        KeepLastPosition = 1 << 1,
        LocalSpace = 1 << 2,
        CameraRelative = 1 << 3,
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
        public ushort AnchorLinkKey;
        public byte ActionId;
        public float Range;
        public float3 Plane;
        public float Smoothing;
        public float LeashRadius;
        public float Drag;
        public float DecayRate;
        public AxisTransformMode Mode;
        public AxisTransformFlags Flags;
        public Target EventRouteTo;
        public ushort EventRouteLinkKey;
        public ConditionKey OnInputStart;
        public ConditionKey OnInputEnd;
    }

    public static class AxisTransformModeExtensions
    {
        public static bool IsRigidbody(this AxisTransformMode m)
            => m >= AxisTransformMode.RigidbodyVelocity;
    }

    public static class AxisTransformFlagsExtensions
    {
        public static bool Has(this AxisTransformFlags flags, AxisTransformFlags flag)
            => (flags & flag) != 0;
    }

    public struct AxisTransformState : IComponentData
    {
        public float3 AnchorOrigin;
        public float3 DesiredOffset;
        public float3 SmoothedOffset;
        public float2 LastInput;
        public bool HasInput;
        public bool WasInputActive;
        public bool Initialized;
    }
}