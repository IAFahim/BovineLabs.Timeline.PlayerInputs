using BovineLabs.Core.Collections;
using BovineLabs.Reaction.Data.Conditions;
using Unity.Entities;

namespace BovineLabs.Timeline.PlayerInputs.Data
{
    public struct InputInvokerConfig : IComponentData
    {
        public byte ActionId;
        public InputPhase Phase;
        public ConditionKey Condition;
        public int Value;
        public Entity RouteEntity;
    }

    public struct InputConsumerRoute : IComponentData
    {
        public Entity Target;
    }

    public struct InputBufferClearTrigger : IComponentData, IEnableableComponent
    {
        public BlobAssetReference<BlobArray<byte>> ActionIds;
    }

    public struct InputCancelWindowConfig : IComponentData
    {
        public BitArray256 AllowedMask;
    }
}