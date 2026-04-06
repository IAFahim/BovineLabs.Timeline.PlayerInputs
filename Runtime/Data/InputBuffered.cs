using Unity.Entities;
using Unity.Mathematics;

namespace PlayerInputs.Data
{
    [InternalBufferCapacity(8)]
    public struct InputButtonDownBuffer : IBufferElementData
    {
        public byte ActionId;
    }

    [InternalBufferCapacity(8)]
    public struct InputButtonHeldBuffer : IBufferElementData
    {
        public byte ActionId;
    }

    [InternalBufferCapacity(8)]
    public struct InputButtonUpBuffer : IBufferElementData
    {
        public byte ActionId;
    }

    [InternalBufferCapacity(4)]
    public struct InputAxisBuffer : IBufferElementData
    {
        public byte ActionId;
        public float2 Value;
    }
}
