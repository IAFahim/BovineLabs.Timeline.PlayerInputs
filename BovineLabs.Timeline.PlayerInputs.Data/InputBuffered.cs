using Unity.Entities;
using Unity.Mathematics;

namespace BovineLabs.Timeline.PlayerInputs.Data
{
    public struct InputAxisBuffer : IBufferElementData
    {
        public byte ActionId;
        public float2 Value;
    }
}