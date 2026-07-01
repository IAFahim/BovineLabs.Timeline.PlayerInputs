using BovineLabs.Core.Authoring.EntityCommands;
using BovineLabs.Reaction.Data.Core;
using BovineLabs.Timeline.Authoring;
using BovineLabs.Timeline.EntityLinks.Authoring;
using BovineLabs.Timeline.PlayerInputs.Authoring;
using BovineLabs.Timeline.PlayerInputs.Flow.Data.Builders;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Timeline;

namespace BovineLabs.Timeline.PlayerInputs.Flow.Authoring
{
    /// <summary>
    /// Nav sibling of <see cref="SplineFlowInputClip"/>: steers a player to a destination by feeding the direction toward
    /// a hidden Traverse navmesh proxy into the input system as a fake axis, so the player physically walks there —
    /// pathing around walls — via the carrot + physics, no new physics. Needs a hidden MoveAgentAuthoring proxy agent
    /// linked via <see cref="ProxyLink"/> (like a SplinePathAuthoring registers a spline for the spline clip). The bound
    /// AxisTransform Move clip must be world-relative (not CameraRelative).
    /// </summary>
    public sealed class NavFlowInputClip : DOTSClip, ITimelineClipAsset
    {
        [Header("Destination")]
        [Tooltip("Entity to path toward, resolved from the bound entity's Targets. Set to None to use World Position.")]
        public Target Destination = Target.Target;

        [Tooltip("World-space point to path toward when Destination is None.")]
        public Vector3 WorldPosition;

        [Tooltip("Re-resolve and re-write the destination every active frame. Required to chase a moving target.")]
        public bool Follow;

        [Header("Proxy")]
        [Tooltip("Link to the hidden Traverse proxy agent (a MoveAgentAuthoring, non-physics) this player chases. " +
                 "A MoveAgentAuthoring GameObject must be linked to this same schema.")]
        public EntityLinkSchema ProxyLink;

        [Min(0f)]
        [Tooltip("Max distance the proxy lead-point may run ahead of the player before it is held. 0 = no leash.")]
        public float LeashRadius = 4f;

        [Header("Query")]
        [Min(0f)] [Tooltip("Half-extents of the navmesh snap search box. Zero uses the agent default.")]
        public float Extents;

        [Tooltip("Index into the navmesh query filters for this move.")]
        public byte QueryFilterType;

        [Header("Routing")]
        [Tooltip("Where to resolve the entity that owns the Consumer/Proxy links from.")]
        public Target ReadRootFrom = Target.Owner;

        [Tooltip("Link to the input consumer whose action axis this fake input replaces. " +
                 "The bound AxisTransform Move clip MUST have CameraRelative=false — the synthetic axis is world XZ.")]
        public EntityLinkSchema ConsumerLink;

        [Tooltip("Movement action whose axis this fake input replaces. " +
                 "Must match the ActionId the consumer's AxisTransform clip reads.")]
        public InputActionReference Action;

        [Header("Shaping")]
        [Range(0f, 1f)]
        [Tooltip("Scales the synthesised axis magnitude. 1 = full, 0 = no input.")]
        public float Gain = 1f;

        public override double duration => 1;
        public ClipCaps clipCaps => ClipCaps.Blending | ClipCaps.Looping;

        public override void Bake(Entity entity, BakingContext context)
        {
            MultiInputSettingsAuthoringUtility.DependsOnSettings(context.Baker);

            if (!EntityLinkAuthoringUtility.TryGetKey(ConsumerLink, out var consumerKey))
            {
                Debug.LogError($"NavFlowInputClip '{name}' missing ConsumerLink schema.", this);
                return;
            }

            if (!EntityLinkAuthoringUtility.TryGetKey(ProxyLink, out var proxyKey))
            {
                Debug.LogError($"NavFlowInputClip '{name}' missing ProxyLink schema.", this);
                return;
            }

            if (Action == null)
            {
                Debug.LogError($"NavFlowInputClip '{name}' has no Action assigned. Clip will be skipped.", this);
                return;
            }

            if (!MultiInputSettingsAuthoringUtility.TryGetIndex(Action, out var actionId))
            {
                Debug.LogError($"NavFlowInputClip '{name}' action '{Action.name}' not found in MultiInputSettings. " +
                               "Clip will be skipped.", this);
                return;
            }

            var builder = new NavFlowInputBuilder
            {
                ActionId = actionId,
                ReadRootFrom = ReadRootFrom,
                ConsumerLinkKey = consumerKey,
                ProxyLinkKey = proxyKey,
                Destination = Destination,
                WorldPosition = WorldPosition,
                Follow = Follow,
                Extents = (half)Extents,
                QueryFilterType = QueryFilterType,
                Gain = Gain,
                LeashRadius = LeashRadius,
            };
            var commands = new BakerCommands(context.Baker, entity);
            builder.ApplyTo(ref commands);

            base.Bake(entity, context);
        }
    }
}
