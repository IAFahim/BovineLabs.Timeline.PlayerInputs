using Unity.Entities;

namespace BovineLabs.Timeline.PlayerInputs.Data
{
    [InternalBufferCapacity(32)]
    public struct InputHistory : IBufferElementData
    {
        public byte ActionId;
        public InputPhase Phase;
        public uint Tick;
    }

    public struct InputHistoryState : IComponentData
    {
        public int Head;
    }
}
