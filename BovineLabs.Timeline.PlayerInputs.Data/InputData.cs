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

    // A carrot driver does ONE verb. Move drives the carrot's POSITION from the stick (the Pos carrot the body
    // chases via its Force/Linear PID); Aim drives the carrot's FACING (the Rot carrot the body turns to via its
    // Angular PID). The physics body is never written here - that lives in BovineLabs.Timeline.Physics.
    public enum AxisTransformMode : byte
    {
        Move = 0,
        Aim = 1
    }

    [Flags]
    public enum AxisTransformFlags : byte
    {
        None = 0,

        // Interpret the stick relative to the main camera's ground projection instead of world axes.
        CameraRelative = 1 << 0,

        // MOVE only: when the stick is released, keep the carrot at its last lead point instead of snapping it
        // back onto the body. Off (default) the lead recenters on the body so the body stops and can't diverge.
        // AIM ignores this - it ALWAYS holds the last input direction (you keep facing where you aimed).
        KeepLead = 1 << 1
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

    [InternalBufferCapacity(0)]
    public struct InputAxis : IBufferElementData
    {
        public byte ActionId;
        public float2 Value;
    }

    [InternalBufferCapacity(0)]
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

    public struct ProviderRetiring : IComponentData
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

        public ushort MaxGapTicks;
    }

    public struct CommandSequence
    {
        public BlobArray<CommandStep> Steps;
        public ConditionKey Condition;
        public int Value;

        public byte Repeat;
    }

    public struct CommandBlob
    {
        public BlobArray<CommandSequence> Sequences;
    }

    public struct CommandSequenceConfig : IComponentData
    {
        public BlobAssetReference<CommandBlob> Blob;

        public BitArray256 Actions;

        public Target ReadRootFrom;
        public ushort ConsumerLinkKey;

        public Target EventRouteTo;
        public ushort EventRouteLinkKey;
    }

    public struct CommandSequenceState : IComponentData
    {
        public bool IsCompleted;
    }

    public struct BufferWindowConfig : IComponentData
    {
        public Target ReadRootFrom;
        public ushort ConsumerLinkKey;
        public BitArray256 AllowedActions;
    }

    public struct BufferClearConfig : IComponentData, IEnableableComponent
    {
        public Target ReadRootFrom;
        public ushort ConsumerLinkKey;
        public BitArray256 ActionMask;
    }

    public struct AxisTransformConfig : IComponentData
    {
        public Target ReadRootFrom;
        public ushort ConsumerLinkKey;

        public ushort AnchorLinkKey;

        public byte ActionId;

        // MOVE: lead distance at full stick deflection. AIM ignores it (only the stick DIRECTION is used).
        public float Range;

        // Plane the stick moves/turns on; its normal is the ground up. Up=(0,1,0) => XZ plane.
        public float3 Plane;

        // AIM: turn speed toward the stick direction (0 = instant snap). MOVE ignores it.
        public float Smoothing;

        // AIM: if > 0, the carrot also TRANSLATES to (held aim direction × this radius) around the body, so the
        // sphere sits at the arrow's tip and holds there on release. 0 = rotation-only (legacy). MOVE ignores it.
        public float AimRadius;

        // MOVE: max lead distance from the body. 0 = unlimited. AIM ignores it.
        public float LeashRadius;

        public AxisTransformMode Mode;
        public AxisTransformFlags Flags;
    }

    public static class AxisTransformFlagsExtensions
    {
        public static bool Has(this AxisTransformFlags flags, AxisTransformFlags flag)
        {
            return (flags & flag) != 0;
        }
    }

    public struct AxisTransformState : IComponentData
    {
        public quaternion HeldWorldRotation;

        // MOVE + KeepLead: the world-space lead point the carrot is pinned to on release. Tracked live while
        // driving and re-derived to parent-local each frame so the body travels to a FIXED world point and stops.
        public float3 HeldWorldPosition;
        public bool Initialized;

        // AIM + AimRadius: true once the stick has been pushed at least once this activation, so the radial offset
        // only kicks in after the first aim (no startup jump onto the ring before the player has aimed).
        public bool HasAimed;
    }

    public struct InputEventsConfig : IComponentData
    {
        public Target ReadRootFrom;
        public ushort ConsumerLinkKey;
        public byte ActionId;
        public Target EventRouteTo;
        public ushort EventRouteLinkKey;
        public ConditionKey OnInputStart;
        public ConditionKey OnInputEnd;
    }

    public struct InputEventsState : IComponentData
    {
        public bool WasInputActive;
    }

    public struct SimulationTick : IComponentData
    {
        public uint Value;
    }

    public enum Direction : byte
    {
        Neutral = 5,
        Down = 2,
        Up = 8,
        Back = 4,
        Forward = 6,
        DownBack = 1,
        DownForward = 3,
        UpBack = 7,
        UpForward = 9
    }

    public struct DirectionState : IComponentData
    {
        public Direction Current;
        public Direction Previous;
        public uint ChangedTick;
    }

    public struct DirectionConfig : IComponentData
    {
        public byte ActionId;
        public float DeadZone;
        public sbyte Facing;
    }

    public struct InputHistoryLimit : IComponentData
    {
        public ushort Value;
    }

    public static class HistoryMath
    {
        public const int DefaultLimit = 64;
        public const int MaxLimit = 256;

        public static int ClampLimit(int limit)
        {
            return math.clamp(limit, 1, MaxLimit);
        }

        public static int EvictCount(int length, int toAdd, int limit)
        {
            return math.clamp(length + toAdd - limit, 0, length);
        }

        public static int OverflowCount(int length, int limit)
        {
            return math.max(0, length - limit);
        }
    }

    public static class DirectionMath
    {
        private const float OctantSplit = 0.41421356f;

        public static Direction Quantise(float2 value, float deadZone, sbyte facing)
        {
            if (math.lengthsq(value) <= deadZone * deadZone)
                return Direction.Neutral;

            var x = facing < 0 ? -value.x : value.x;
            var y = value.y;

            var ax = math.abs(x);
            var ay = math.abs(y);

            var horiz = ax > ay * OctantSplit ? (int)math.sign(x) : 0;
            var vert = ay > ax * OctantSplit ? (int)math.sign(y) : 0;

            return (horiz, vert) switch
            {
                (0, 1) => Direction.Up,
                (0, -1) => Direction.Down,
                (-1, 0) => Direction.Back,
                (1, 0) => Direction.Forward,
                (-1, 1) => Direction.UpBack,
                (1, 1) => Direction.UpForward,
                (-1, -1) => Direction.DownBack,
                (1, -1) => Direction.DownForward,
                _ => Direction.Neutral
            };
        }
    }
}