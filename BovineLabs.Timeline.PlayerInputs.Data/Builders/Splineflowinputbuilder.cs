using BovineLabs.Core.EntityCommands;
using BovineLabs.Reaction.Data.Core;
using BovineLabs.Timeline.Physics;

namespace BovineLabs.Timeline.PlayerInputs.Flow.Data.Builders
{
    public struct SplineFlowInputBuilder
    {
        public ushort SplineKey;
        public SplineTraversal Traversal;
        public SplineWrap Wrap;
        public float Speed;
        public float TraversalSeconds;
        public float Lead;
        public byte ActionId;
        public Target ReadRootFrom;
        public ushort ConsumerLinkKey;
        public float Gain;
        public sbyte Direction;

        public void ApplyTo<T>(ref T commands)
            where T : struct, IEntityCommands
        {
            commands.AddComponent(new SplineFlowInputConfig
            {
                SplineKey = SplineKey,
                Traversal = Traversal,
                Wrap = Wrap,
                Speed = Speed,
                TraversalSeconds = TraversalSeconds,
                Lead = Lead,
                ActionId = ActionId,
                ReadRootFrom = ReadRootFrom,
                ConsumerLinkKey = ConsumerLinkKey,
                Gain = Gain,
                Direction = Direction
            });
            commands.AddComponent(new SplineFlowInputState());
        }
    }
}
