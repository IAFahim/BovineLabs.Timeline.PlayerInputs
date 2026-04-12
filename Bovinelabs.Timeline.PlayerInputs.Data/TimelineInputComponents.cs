using BovineLabs.Core.Collections;
using BovineLabs.Reaction.Data.Conditions;
using Unity.Entities;

namespace Bovinelabs.Timeline.PlayerInputs.Data
{
    public struct InputInvokerConfig : IComponentData
    {
        public byte ActionId;
        public InputPhase Phase;
        public ConditionKey Condition;
        public int Value;
    }

    public struct InputBufferClearTrigger : IComponentData, IEnableableComponent
    {
        public bool ClearAll;
        public byte ActionId;
    }

    public struct InputCancelWindowConfig : IComponentData
    {
        public BitArray256 AllowedMask;
    }
}