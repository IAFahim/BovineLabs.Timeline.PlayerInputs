using Unity.Entities;
using Unity.Mathematics;

namespace BovineLabs.Timeline.PlayerInputs.Data
{
    public struct InputButtonDownBuffer : IBufferElementData
    {
        public byte ActionId;
    }

    public struct InputButtonHeldBuffer : IBufferElementData
    {
        public byte ActionId;
    }

    public struct InputButtonUpBuffer : IBufferElementData
    {
        public byte ActionId;
    }

    public struct InputAxisBuffer : IBufferElementData
    {
        public byte ActionId;
        public float2 Value;
    }
}