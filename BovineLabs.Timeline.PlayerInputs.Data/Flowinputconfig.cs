using BovineLabs.Timeline.EntityLinks.Data;
using BovineLabs.Timeline.Grid.Influence.Data.Flows;
using Unity.Entities;
using Unity.Mathematics;

namespace BovineLabs.Timeline.PlayerInputs.Flow.Data
{
    public struct FlowInputConfig : IComponentData
    {
        public ushort FieldKey;
        public FlowBias Bias;
        public byte ActionId;
        public EntityLinkRef Consumer;
        public float3 LocalOffset;
        public float Gain;
    }

    public struct SyntheticProviderTag : IComponentData
    {
    }
}