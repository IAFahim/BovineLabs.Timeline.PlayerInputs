// Runtime/Data/PlayerInputComponent.cs
using Unity.Entities;

namespace PlayerInputs.Data
{
    public struct PlayerId : IComponentData
    {
        public byte Value;
    }

    public struct InputProviderTag : IComponentData { }
    public struct InputConsumerTag : IComponentData { }

    public struct InputSource : IComponentData
    {
        public Entity Provider;
    }

    // --- New Registry & Event Primitives ---

    public struct PlayerInputRegistryTag : IComponentData { }

    [InternalBufferCapacity(16)]
    public struct PlayerInputLink : IBufferElementData
    {
        public byte PlayerId;
        public Entity Provider;
    }

    [InternalBufferCapacity(4)]
    public struct PlayerJoinedEventBuffer : IBufferElementData
    {
        public byte PlayerId;
        public Entity Provider;
    }

    [InternalBufferCapacity(4)]
    public struct PlayerLeftEventBuffer : IBufferElementData
    {
        public byte PlayerId;
    }
}