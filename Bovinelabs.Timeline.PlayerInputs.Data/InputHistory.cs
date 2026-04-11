using Unity.Entities;

namespace Bovinelabs.Timeline.PlayerInputs.Data
{
    [InternalBufferCapacity(32)]
    public struct InputHistory : IBufferElementData
    {
        public byte ActionId;
        public InputPhase Phase;
        public uint Tick;
    }
}