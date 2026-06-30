using BovineLabs.Reaction.Data.Core;
using BovineLabs.Timeline.Physics;
using Unity.Entities;

namespace BovineLabs.Timeline.PlayerInputs.Flow.Data
{
    /// <summary>
    /// Sibling of <see cref="FlowInputConfig"/>: instead of a grid field gradient, this synthesises the fake axis from a
    /// spline's tangent. The spline tangent at the current traversal point is projected onto the ground (XZ) plane and
    /// accumulated into the synthetic provider's <see cref="BovineLabs.Timeline.PlayerInputs.Data.InputAxis"/> buffer —
    /// exactly the same seam the field flow rides, so spline + field + real stick sum as independent layers.
    /// </summary>
    public struct SplineFlowInputConfig : IComponentData
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

        /// <summary> +1 follows the spline forward, -1 reverses it. </summary>
        public sbyte Direction;
    }

    /// <summary> Per-clip traversal cursor advanced each active frame. Mirrors PhysicsSplineFollowState.Progress. </summary>
    public struct SplineFlowInputState : IComponentData
    {
        public float Progress;
    }
}
