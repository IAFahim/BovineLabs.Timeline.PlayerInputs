using Unity.Entities;

namespace PlayerInputs.Data
{
    [InternalBufferCapacity(16)]
    public struct InputHistoryBuffer : IBufferElementData
    {
        public byte ActionID;
    }
}
