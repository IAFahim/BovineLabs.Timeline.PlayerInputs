using BovineLabs.Core.Authoring.EntityCommands;
using BovineLabs.Reaction.Data.Core;
using BovineLabs.Timeline.Authoring;
using BovineLabs.Timeline.EntityLinks.Authoring;
using BovineLabs.Timeline.PlayerInputs.Data;
using Unity.Entities;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Timeline;

namespace BovineLabs.Timeline.PlayerInputs.Authoring
{
    public sealed class AxisTransformClip : DOTSClip, ITimelineClipAsset
    {
        [Tooltip("Where to resolve the entity that owns the ConsumerLink from.")]
        public Target ReadRootFrom = Target.Owner;

        [Tooltip("Link to the input consumer whose action axis drives this clip.")]
        public EntityLinkSchema ConsumerLink;

        [Tooltip("Optional: the body actually moved/turned. Empty drives the bound entity itself; set, drives the " +
                 "linked entity (the bound marker then just carries the player link).")]
        public EntityLinkSchema AnchorLink;

        [Tooltip("Action whose [-1,1] / stick axis drives this clip.")]
        public InputActionReference Action;

        [Tooltip("Plane the axis moves/turns on (its normal is the ground up). Up=(0,1,0) moves on XZ.")]
        public Vector3 Plane = Vector3.up;

        [Header("Translate")]
        [Tooltip("Offset the target along the axis (absolute offset from its rest pose). Recenters onto the rest " +
                 "pose when released, unless Hold Last Position is enabled.")]
        public bool Translate = true;

        [Tooltip("Offset distance at full axis deflection.")]
        public float Range = 5f;

        [Tooltip("Translate only: max distance the target may travel from where the clip started. 0 = unlimited.")]
        public float LeashRadius;

        [Tooltip("Translate only: when the axis is released, hold the target at its last position instead of " +
                 "recentering onto its rest pose (no snap-back). Makes the leash a place-and-hold lead point.")]
        public bool HoldLastPosition;

        [Header("Face Direction")]
        [Tooltip("Turn the target to face the axis direction. Pure aim = enable this and disable Translate.")]
        public bool FaceDirection;

        [Tooltip("Face turn speed. 0 = instant snap.")]
        public float Smoothing;

        [Header("Options")] [Tooltip("Interpret the axis relative to the Main Camera instead of world axes.")]
        public bool CameraRelative = true;

        public override double duration => 1;
        public ClipCaps clipCaps => ClipCaps.None;

        public override void Bake(Entity entity, BakingContext context)
        {
            MultiInputSettingsAuthoringUtility.DependsOnSettings(context.Baker);

            if (!EntityLinkAuthoringUtility.TryGetKey(ConsumerLink, out var linkKey))
            {
                Debug.LogError($"AxisTransformClip '{name}' missing ConsumerLink schema.");
                return;
            }

            EntityLinkAuthoringUtility.TryGetKey(AnchorLink, out var anchorLinkKey);

            var actionId = byte.MaxValue;
            if (Action == null)
            {
                Debug.LogError(
                    $"AxisTransformClip '{name}' has no Action assigned; it will read no axis (the clip does nothing).",
                    this);
            }
            else if (!MultiInputSettingsAuthoringUtility.TryGetIndex(Action, out actionId))
            {
                actionId = byte.MaxValue;
                Debug.LogError($"AxisTransformClip '{name}' action '{Action.name}' not found in MultiInputSettings.",
                    this);
            }

            var flags = AxisTransformFlags.None;
            if (Translate) flags |= AxisTransformFlags.Translate;
            if (FaceDirection) flags |= AxisTransformFlags.FaceDirection;
            if (CameraRelative) flags |= AxisTransformFlags.CameraRelative;
            if (HoldLastPosition) flags |= AxisTransformFlags.HoldLastPosition;

            if (!Translate && !FaceDirection)
                Debug.LogWarning(
                    $"AxisTransformClip '{name}' has neither Translate nor Face Direction enabled; it will do nothing.",
                    this);

            var commands = new BakerCommands(context.Baker, entity);
            commands.AddComponent(new AxisTransformConfig
            {
                ReadRootFrom = ReadRootFrom,
                ConsumerLinkKey = linkKey,
                AnchorLinkKey = anchorLinkKey,
                ActionId = actionId,
                Range = Range,
                Plane = Plane,
                Smoothing = Smoothing,
                LeashRadius = LeashRadius,
                Flags = flags
            });

            commands.AddComponent<AxisTransformState>();

            base.Bake(entity, context);
        }
    }
}