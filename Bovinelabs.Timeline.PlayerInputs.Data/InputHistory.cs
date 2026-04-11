using Unity.Entities;

namespace PlayerInputs.Data
{
    [InternalBufferCapacity(32)]
    public struct InputHistory : IBufferElementData
    {
        public byte ActionId;
        public uint Tick;
    }
}