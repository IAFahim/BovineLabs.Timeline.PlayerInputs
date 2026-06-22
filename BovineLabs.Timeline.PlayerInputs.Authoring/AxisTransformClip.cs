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

        [Tooltip("What this clip drives. Move = push the carrot ahead of the body (the body chases it). " +
                 "Aim = point the carrot's facing (the body turns to it). Bind Move to your Pos carrot, Aim to Rot.")]
        public AxisTransformMode Mode = AxisTransformMode.Move;

        [Tooltip("Plane the stick moves/turns on (its normal is the ground up). Up=(0,1,0) works on XZ.")]
        public Vector3 Plane = Vector3.up;

        [Header("Move")]
        [Tooltip("Move only: lead distance at full stick deflection.")]
        public float Range = 5f;

        [Tooltip("Move only: max lead distance from the body. 0 = unlimited.")]
        public float LeashRadius;

        [Tooltip("Move only: when the stick is released, snap the lead back onto the body so it stops and can't " +
                 "drift. Turn OFF to keep the lead where you left it (the body keeps travelling there, then stops).")]
        public bool SnapBackOnRelease = true;

        [Header("Aim")]
        [Tooltip("Aim only: turn speed toward the stick direction. 0 = instant snap. On release the facing HOLDS " +
                 "the last input direction.")]
        public float Smoothing;

        [Tooltip("Aim only: if > 0, the sphere also moves to (aim direction x this radius) around the body - it " +
                 "sits at the arrow's tip and holds there on release. 0 = rotate in place only.")]
        public float AimRadius;

        [Header("Options")]
        [Tooltip("Interpret the stick relative to the Main Camera instead of world axes.")]
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
            if (CameraRelative) flags |= AxisTransformFlags.CameraRelative;
            if (Mode == AxisTransformMode.Move && !SnapBackOnRelease) flags |= AxisTransformFlags.KeepLead;

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
                AimRadius = AimRadius,
                LeashRadius = LeashRadius,
                Mode = Mode,
                Flags = flags
            });

            commands.AddComponent<AxisTransformState>();

            base.Bake(entity, context);
        }
    }
}