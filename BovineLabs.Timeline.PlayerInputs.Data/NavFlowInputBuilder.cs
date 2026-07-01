using BovineLabs.Core.EntityCommands;
using BovineLabs.Reaction.Data.Core;
using Unity.Mathematics;

namespace BovineLabs.Timeline.PlayerInputs.Flow.Data.Builders
{
    public struct NavFlowInputBuilder
    {
        public byte ActionId;
        public Target ReadRootFrom;
        public ushort ConsumerLinkKey;
        public ushort ProxyLinkKey;
        public Target Destination;
        public float3 WorldPosition;
        public bool Follow;
        public half Extents;
        public byte QueryFilterType;
        public float Gain;
        public float LeashRadius;

        public void ApplyTo<T>(ref T commands)
            where T : struct, IEntityCommands
        {
            commands.AddComponent(new NavFlowInputConfig
            {
                ActionId = ActionId,
                ReadRootFrom = ReadRootFrom,
                ConsumerLinkKey = ConsumerLinkKey,
                ProxyLinkKey = ProxyLinkKey,
                Destination = Destination,
                WorldPosition = WorldPosition,
                Follow = Follow,
                Extents = Extents,
                QueryFilterType = QueryFilterType,
                Gain = Gain,
                LeashRadius = LeashRadius,
            });
        }
    }
}
