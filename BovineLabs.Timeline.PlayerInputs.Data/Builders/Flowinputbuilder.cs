using BovineLabs.Core.EntityCommands;
using BovineLabs.Reaction.Data.Core;
using BovineLabs.Timeline.Grid.Influence.Data.Flows;
using BovineLabs.Timeline.PlayerInputs.Flow.Data;
using Unity.Mathematics;

namespace BovineLabs.Timeline.PlayerInputs.Flow.Data.Builders
{
    public struct FlowInputBuilder
    {
        public ushort FieldKey;
        public FlowBias Bias;
        public byte ActionId;
        public Target ReadRootFrom;
        public ushort ConsumerLinkKey;
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
                ReadRootFrom = ReadRootFrom,
                ConsumerLinkKey = ConsumerLinkKey,
                LocalOffset = LocalOffset,
                Gain = Gain
            });
        }
    }
}