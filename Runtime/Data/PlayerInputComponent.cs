using Unity.Entities;

namespace PlayerInputs.Data
{
    public struct ECSPlayerInputID : IComponentData
    {
        public byte ID;
    }

    [InternalBufferCapacity(16)]
    public struct InputSubscribedEntity : IBufferElementData
    {
        public Entity Value;
    }

    public struct PlayerInputRegisteredTag : IComponentData { }
    public struct BackingInputEntityTag : IComponentData { }
}