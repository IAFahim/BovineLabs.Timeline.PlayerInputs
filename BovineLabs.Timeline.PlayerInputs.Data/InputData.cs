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

        // Rotate the target instead of moving it: the X axis yaws around the Plane normal. Range = degrees
        // per second at full deflection. Lets a Vector2 axis (e.g. mouse Look) drive rotation.
        Rotation = 5,
    }

    [Flags]
    public enum AxisTransformFlags : byte
    {
        None = 0,
        IgnoreParentRotation = 1 << 0,
        KeepLastPosition = 1 << 1,
        LocalSpace = 1 << 2,
        CameraRelative = 1 << 3
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

    // Append-only ring of discrete input transitions for one consumer. Records
    // Down and Up edges only — never Held — because Held is true every frame and
    // would flood the buffer. Sequences that need a sustained hold must express it
    // with CommandMode.None (a live-state probe), not a buffered Held step.
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

    // A leaving player's provider is tagged with this instead of being destroyed immediately. Its InputState
    // carries a closing Up for everything that was held, so consumers (CommandSequence waiting on a release)
    // get one tick to read it; ProviderRetireSystem then destroys the entity. See PlayerInputBridge.OnDisable.
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

        // Maximum simulation ticks allowed between this step's match and the
        // previous matched step. 0 means no timing constraint (link is unbounded).
        // This is what makes motion inputs (236P) and frame links expressible:
        // a step that must follow within N deterministic ticks of its predecessor.
        public ushort MaxGapTicks;
    }

    public struct CommandSequence
    {
        public BlobArray<CommandStep> Steps;
        public ConditionKey Condition;
        public int Value;

        // Non-zero: the sequence re-arms after firing instead of latching
        // CommandSequenceState.IsCompleted for the rest of the clip. Pair with
        // consuming step modes so the matched history is removed on fire;
        // otherwise a still-matching history retriggers every frame.
        public byte Repeat;
    }

    public struct CommandBlob
    {
        public BlobArray<CommandSequence> Sequences;
    }

    public struct CommandSequenceConfig : IComponentData
    {
        public BlobAssetReference<CommandBlob> Blob;

        // Union of the action ids this clip reads from HISTORY - the combo modes only (Contains/Consume/
        // Ordered* families); None/Held/Not* are excluded (CommandSequenceClip.ReadsHistory). While the
        // clip is active and not yet completed, ConsumerBufferMaskSystem ORs this into the consumer's
        // ActiveBufferMask so a combo self-buffers its own actions' Down/Up edges with no separate
        // InputBufferWindow track. None Down/Up/Held is a live probe and never goes through here.
        public BitArray256 Actions;

        // "Get from": where to resolve the entity that owns ConsumerLink, then the link to the input
        // consumer (PlayerId holder) whose history/state the sequence reads.
        public Target ReadRootFrom;
        public ushort ConsumerLinkKey;

        // "Route to": where the matched sequence's condition event is fired (Self = the bound entity).
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
        public float Range;
        public float3 Plane;
        public float Smoothing;
        public float LeashRadius;
        public float Drag;
        public float DecayRate;
        public AxisTransformMode Mode;
        public AxisTransformFlags Flags;

        // "Route to": where the axis edge events are fired (Self = the bound entity).
        public Target EventRouteTo;
        public ushort EventRouteLinkKey;

        // Fired when the axis leaves neutral (rising edge) / returns to neutral (falling edge).
        // OnAxisStop is the "axis input is off" event. ConditionKey.Null = no event.
        public ConditionKey OnAxisStart;
        public ConditionKey OnAxisStop;
    }

    public static class AxisTransformModeExtensions
    {
        public static bool IsRigidbody(this AxisTransformMode m)
        {
            return m >= AxisTransformMode.RigidbodyVelocity;
        }
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
        public float3 AnchorOrigin;
        public float3 DesiredOffset;
        public float3 SmoothedOffset;
        public float2 LastInput;
        public bool HasInput;
        public bool WasInputActive;
        public bool Initialized;
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

    // Monotonic counter advanced once per update by SimulationTickSystem (currently a per-FRAME
    // counter, not a fixed-step one). Used instead of wall-clock time for input history and timing
    // windows. NOTE: because it counts frames at the variable update rate, it is NOT guaranteed
    // identical across client/server/replay - do not rely on it for netcode/replay determinism
    // until SimulationTickSystem is driven from a fixed simulation step.
    public struct SimulationTick : IComponentData
    {
        public uint Value;
    }

    // Quantised eight-way direction derived from a stick/axis, plus Neutral.
    // The numeric layout follows fighting-game numpad notation so motion inputs
    // read the way designers think about them (236 = Down, DownForward, Forward).
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

    // Per-consumer resolved facing-relative direction for one axis action.
    // Written by DirectionInputSystem, read by command evaluation and gameplay.
    public struct DirectionState : IComponentData
    {
        public Direction Current;
        public Direction Previous;
        public uint ChangedTick;
    }

    // Configures how a consumer's analog axis is quantised into a Direction.
    // DeadZone gates Neutral; Facing flips Back/Forward when the character faces -X.
    public struct DirectionConfig : IComponentData
    {
        public byte ActionId;
        public float DeadZone;
        public sbyte Facing; // +1 faces +X, -1 faces -X
    }

    // Optional per-consumer cap on InputHistory length. Absent, consumers use
    // HistoryMath.DefaultLimit. Values are clamped to [1, HistoryMath.MaxLimit];
    // the upper bound exists because command evaluation tracks consumed entries
    // in a BitArray256 indexed by history position.
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

        // Number of oldest entries to evict so that length + toAdd fits in limit.
        // Total: never negative, never exceeds length (you cannot remove entries
        // that do not exist — the previous capacity-based version did exactly that
        // on an empty heap buffer whose Capacity starts at zero, and crashed).
        public static int EvictCount(int length, int toAdd, int limit)
        {
            return math.clamp(length + toAdd - limit, 0, length);
        }

        // Entries to drop from the back after appending when a single frame adds
        // more than the limit itself (toAdd > limit). Keeps the most recent 'limit'.
        public static int OverflowCount(int length, int limit)
        {
            return math.max(0, length - limit);
        }
    }

    public static class DirectionMath
    {
        // tan(22.5deg): the half-angle of an octant. A component is "active" when it
        // exceeds the other component scaled by this, giving clean 45-degree octants.
        private const float OctantSplit = 0.41421356f;

        // Pure eight-way quantisation of an axis into numpad-notation Direction.
        // Total: every input maps to exactly one Direction; inside the dead zone
        // (or at the origin) the result is Neutral. Facing < 0 mirrors X so that
        // Back/Forward stay relative to the character's facing.
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
