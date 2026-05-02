using BovineLabs.Core.Collections;
using BovineLabs.Reaction.Data.Conditions;
using Unity.Entities;
using Unity.Mathematics;

namespace BovineLabs.Timeline.PlayerInputs.Data
{
    public enum InputMode : byte
    {
        RealtimeDown = 0,
        RealtimeHeld = 1,
        RealtimeUp = 2,
        BufferContains = 3,
        BufferConsume = 4,
        BufferFirstConsume = 5,
        BufferLastConsume = 6
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
        public uint Tick;
    }

    public struct PlayerId : IComponentData { public byte Value; }
    public struct ProviderTag : IComponentData { }
    public struct ConsumerTag : IComponentData { }
    public struct InputSource : IComponentData { public Entity Provider; }
    public struct PlayerMoveInput : IComponentData { public float2 Value; }

    public struct TransducerRequirement
    {
        public byte ActionId;
        public InputMode Mode;
    }

    public struct TransducerBlob
    {
        public BlobArray<TransducerRequirement> Requirements;
    }

    public struct TransducerConfig : IComponentData
    {
        public BlobAssetReference<TransducerBlob> Blob;
        public ConditionKey Condition;
        public int Value;
        public Entity RouteEntity;
    }

    public struct BufferWindowConfig : IComponentData
    {
        public BitArray256 AllowedActions;
    }

    public struct BufferClearConfig : IComponentData, IEnableableComponent
    {
        public BlobAssetReference<BlobArray<byte>> ActionIds;
    }
}