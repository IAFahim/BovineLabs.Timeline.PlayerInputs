using BovineLabs.Core.Authoring.EntityCommands;
using BovineLabs.Reaction.Data.Core;
using BovineLabs.Timeline.Authoring;
using BovineLabs.Timeline.EntityLinks.Authoring;
using BovineLabs.Timeline.Physics;
using BovineLabs.Timeline.Physics.Authoring.Splines;
using BovineLabs.Timeline.PlayerInputs.Authoring;
using BovineLabs.Timeline.PlayerInputs.Flow.Data.Builders;
using Unity.Entities;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Timeline;

namespace BovineLabs.Timeline.PlayerInputs.Flow.Authoring
{
    /// <summary>
    /// Spline sibling of <see cref="FlowInputClip"/>: steers a player along a spline by feeding the spline's tangent into
    /// the input system as a fake axis. Reuses the SplineRegistry (a SplinePathAuthoring must register the same schema)
    /// and the synthetic input provider — no new physics. Author it on the same Targets/AxisTransform setup the field
    /// flow uses; the bound Move clip must be world-relative (not CameraRelative).
    /// </summary>
    public sealed class SplineFlowInputClip : DOTSClip, ITimelineClipAsset
    {
        [Header("Path")]
        [Tooltip("Which path to follow. A SplinePathAuthoring GameObject must register this same schema.")]
        public SplineSchema Spline;

        public SplineTraversal Traversal = SplineTraversal.ConstantSpeed;
        public SplineWrap Wrap = SplineWrap.Loop;

        [Tooltip("Reverse the direction travelled along the spline.")]
        public bool Reverse;

        [Header("Speed")] [Tooltip("ConstantSpeed mode: metres/second along the path.")]
        public float Speed = 5f;

        [Tooltip("OverDuration mode: seconds to traverse the whole path.")]
        public float TraversalSeconds = 4f;

        [Tooltip("Samples the tangent this fraction ahead on the path so the steering anticipates corners.")]
        [Range(0f, 0.3f)]
        public float Lead = 0.03f;

        [Header("Routing")] [Tooltip("Where to resolve the entity that owns the ConsumerLink from.")]
        public Target ReadRootFrom = Target.Owner;

        [Tooltip("Link to the input consumer whose action axis this fake input replaces. " +
                 "The bound AxisTransform Move clip MUST have CameraRelative=false — the synthetic axis is computed in " +
                 "world XZ, so a camera-relative clip would rotate it into the wrong direction.")]
        public EntityLinkSchema ConsumerLink;

        [Tooltip("Movement action whose axis this fake input replaces. " +
                 "Must match the ActionId the consumer's AxisTransform clip reads.")]
        public InputActionReference Action;

        [Header("Shaping")]
        [Range(0f, 1f)]
        [Tooltip("Scales the synthesised axis magnitude. 1 = full flow, 0 = no input.")]
        public float Gain = 1f;

        public override double duration => 1;
        public ClipCaps clipCaps => ClipCaps.Blending | ClipCaps.Looping;

        public override void Bake(Entity entity, BakingContext context)
        {
            MultiInputSettingsAuthoringUtility.DependsOnSettings(context.Baker);

            if (Spline == null)
            {
                Debug.LogError($"SplineFlowInputClip '{name}' has no Spline schema assigned. Clip will be skipped.", this);
                return;
            }

            if (!EntityLinkAuthoringUtility.TryGetKey(ConsumerLink, out var linkKey))
            {
                Debug.LogError($"SplineFlowInputClip '{name}' missing ConsumerLink schema.", this);
                return;
            }

            if (Action == null)
            {
                Debug.LogError($"SplineFlowInputClip '{name}' has no Action assigned. Clip will be skipped.", this);
                return;
            }

            if (!MultiInputSettingsAuthoringUtility.TryGetIndex(Action, out var actionId))
            {
                Debug.LogError($"SplineFlowInputClip '{name}' action '{Action.name}' not found in MultiInputSettings. " +
                               "Clip will be skipped.", this);
                return;
            }

            context.Baker.DependsOn(Spline);

            var builder = new SplineFlowInputBuilder
            {
                SplineKey = Spline.Id,
                Traversal = Traversal,
                Wrap = Wrap,
                Speed = Speed,
                TraversalSeconds = TraversalSeconds,
                Lead = Lead,
                ActionId = actionId,
                ReadRootFrom = ReadRootFrom,
                ConsumerLinkKey = linkKey,
                Gain = Gain,
                Direction = (sbyte)(Reverse ? -1 : 1)
            };
            var commands = new BakerCommands(context.Baker, entity);
            builder.ApplyTo(ref commands);

            base.Bake(entity, context);
        }
    }
}
