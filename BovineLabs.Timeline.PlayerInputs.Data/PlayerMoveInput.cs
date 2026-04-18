using Unity.Entities;
using Unity.Mathematics;

namespace BovineLabs.Timeline.PlayerInputs.Data
{
    public struct PlayerMoveInput : IComponentData
    {
        public float2 Value;
    }
}
