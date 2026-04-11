using Unity.Entities;

namespace PlayerInputs.Data
{
    public struct InputCancelWindow : IComponentData, IEnableableComponent
    {
        public InputBitmask AllowedMask;
    }
}