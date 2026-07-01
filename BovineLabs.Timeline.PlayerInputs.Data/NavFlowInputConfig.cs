using BovineLabs.Reaction.Data.Core;
using Unity.Entities;
using Unity.Mathematics;

namespace BovineLabs.Timeline.PlayerInputs.Flow.Data
{
    /// <summary>
    /// Nav sibling of <see cref="SplineFlowInputConfig"/> and <see cref="FlowInputConfig"/>: instead of a spline tangent
    /// or a grid gradient, the fake axis is synthesised from a Traverse navmesh proxy. A hidden non-physics agent
    /// (resolved via <see cref="ProxyLinkKey"/>) pathfinds toward the destination as a moving "lead point"; the direction
    /// from the player toward that proxy (projected to the XZ ground plane) is accumulated into the synthetic provider's
    /// <see cref="BovineLabs.Timeline.PlayerInputs.Data.InputAxis"/> buffer — the same seam spline + field + real stick
    /// ride, so they all sum as independent layers. The player physically chases the proxy via the carrot + physics
    /// motors, pathing around walls, with no new physics. The bound AxisTransform Move clip must be world-relative
    /// (not CameraRelative) — the axis is world XZ.
    /// </summary>
    public struct NavFlowInputConfig : IComponentData
    {
        public byte ActionId;
        public Target ReadRootFrom;

        /// <summary> Link to the input consumer whose action axis this fake input replaces. </summary>
        public ushort ConsumerLinkKey;

        /// <summary> Link to the hidden Traverse proxy agent (MoveAgentAuthoring) this player chases. </summary>
        public ushort ProxyLinkKey;

        /// <summary> Destination resolved from the bound entity's Targets. Set to None to use WorldPosition instead. </summary>
        public Target Destination;

        /// <summary> World-space destination, used when Destination == None. </summary>
        public float3 WorldPosition;

        /// <summary> Re-resolve and re-write the destination every active frame (chase a moving target). </summary>
        public bool Follow;

        /// <summary> Half-extents of the navmesh snap search box. Zero uses the agent default. </summary>
        public half Extents;

        /// <summary> Index into the navmesh query filters for this move. </summary>
        public byte QueryFilterType;

        /// <summary> Scales the synthesised axis magnitude. 1 = full, 0 = no input. </summary>
        public float Gain;

        /// <summary>
        /// Max distance the proxy lead-point may run ahead of the player. Beyond it the proxy is held (pathfinding
        /// disabled) until the player closes the gap, so a blocked player never chases a proxy that already reached the
        /// goal. Zero = no leash.
        /// </summary>
        public float LeashRadius;
    }
}
