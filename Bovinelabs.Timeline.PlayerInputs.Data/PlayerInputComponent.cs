using Unity.Entities;

namespace Bovinelabs.Timeline.PlayerInputs.Data
{
    public struct PlayerId : IComponentData
    {
        public byte Value;
    }

    public struct InputProviderTag : IComponentData
    {
    }

    public struct InputConsumerTag : IComponentData
    {
    }

    public struct InputSource : IComponentData
    {
        public Entity Provider;
    }

    public struct PlayerInputRegistryTag : IComponentData
    {
    }

    public struct PlayerInputLink : IBufferElementData
    {
        public byte PlayerId;
        public Entity Provider;
    }

    public struct PlayerJoinedEventBuffer : IBufferElementData
    {
        public byte PlayerId;
        public Entity Provider;
    }

    public struct PlayerLeftEventBuffer : IBufferElementData
    {
        public byte PlayerId;
    }
}