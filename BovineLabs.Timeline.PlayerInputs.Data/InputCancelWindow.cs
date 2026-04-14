using BovineLabs.Core.Collections;
using Unity.Entities;

namespace BovineLabs.Timeline.PlayerInputs.Data
{
    public struct InputCancelWindow : IComponentData, IEnableableComponent
    {
        public BitArray256 AllowedMask;
    }
}