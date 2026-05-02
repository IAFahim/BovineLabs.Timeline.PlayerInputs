using BovineLabs.Core.Collections;
using BovineLabs.Reaction.Data.Conditions;
using Unity.Entities;
using Unity.Mathematics;

namespace BovineLabs.Timeline.PlayerInputs.Data
{
    public enum CommandMode : byte
    {
        None = 0,               // Live State 
        
        // Unordered Searches
        Contains = 1,
        Consume = 2,
        FirstConsume = 3,
        LastConsume = 4,
        
        // Ordered Searches (Progressive index)
        OrderedContains = 16,
        OrderedConsume = 17,
        OrderedFirstConsume = 18,
        OrderedLastConsume = 19,
        
        // Negative Requirements
        NotContains = 32,
        NotFirst = 33,
        NotLast = 34,
    }

    public struct InputState : IComponentData
    {
        public BitArray256 Pressed;
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

    public struct CommandStep
    {
        public byte ActionId;
        public CommandMode Mode;
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
}