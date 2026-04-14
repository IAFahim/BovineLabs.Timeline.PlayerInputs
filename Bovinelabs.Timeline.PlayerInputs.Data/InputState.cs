using BovineLabs.Core.Collections;
using Unity.Entities;

namespace Bovinelabs.Timeline.PlayerInputs.Data
{
    public struct InputState : IComponentData
    {
        public BitArray256 Down;
        public BitArray256 Held;
        public BitArray256 Up;
    }
}