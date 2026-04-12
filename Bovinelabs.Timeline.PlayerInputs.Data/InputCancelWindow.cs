using BovineLabs.Core.Collections;
using Unity.Entities;

namespace Bovinelabs.Timeline.PlayerInputs.Data
{
    public struct InputCancelWindow : IComponentData, IEnableableComponent
    {
        public BitArray256 AllowedMask;
    }
}