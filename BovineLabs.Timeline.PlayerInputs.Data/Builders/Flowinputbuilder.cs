using BovineLabs.Core.EntityCommands;
using BovineLabs.Timeline.EntityLinks.Data;
using BovineLabs.Timeline.Grid.Influence.Data.Flows;
using Unity.Mathematics;

namespace BovineLabs.Timeline.PlayerInputs.Flow.Data.Builders
{
    public struct FlowInputBuilder
    {
        public ushort FieldKey;
        public FlowBias Bias;
        public byte ActionId;
        public EntityLinkRef Consumer;
        public float3 LocalOffset;
        public float Gain;

        public void ApplyTo<T>(ref T commands)
            where T : struct, IEntityCommands
        {
            commands.AddComponent(new FlowInputConfig
            {
                FieldKey = FieldKey,
                Bias = Bias,
                ActionId = ActionId,
                Consumer = Consumer,
                LocalOffset = LocalOffset,
                Gain = Gain
            });
        }
    }
}