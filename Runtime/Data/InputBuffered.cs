using Unity.Entities;

namespace PlayerInputs.PlayerInputs.Data
{
    public struct InputCurrentBuffer : IBufferElementData
    {
        public ushort Id;
    }

    public struct InputPreviousBuffer : IBufferElementData
    {
        public ushort Id;
    }
}
