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
        public Target ReadRootFrom = Target.Owner;
        public EntityLinkSchema ConsumerLink;

        [Tooltip("Entity whose world position the carrot is tethered to. " +
                 "Without this, leash clamps relative to the carrot's initial position.")]
        public EntityLinkSchema AnchorLink;

        public InputActionReference Action;

        [Tooltip("Scales [-1,1] input range. Range=5 means output spans [-5,5]. " +
                 "In Rigidbody modes: max speed (Velocity), force magnitude (Force), or impulse magnitude (Impulse).")]
        public float Range = 2f;

        [Tooltip("Normal of plane movement is applied to. Up=(0,1,0) moves XZ.")]
        public Vector3 Plane = Vector3.up;

        [Tooltip("Lerp speed toward target. 0 = instant snap. " +
                 "In RigidbodyVelocity: lerp speed toward target planar velocity.")]
        public float Smoothing;

        [Tooltip("Max distance the carrot can be from the anchor. 0 = unlimited. " +
                 "Rigidbody modes: max planar speed.")]
        public float LeashRadius = 2;

        [Tooltip("RigidbodyForce only: velocity dissipation coefficient per second. " +
                 "Higher = faster deceleration when no force is applied. 0 = no drag.")]
        public float Drag;

        [Tooltip("Rate at which offset decays toward zero when no input. " +
                 "0 = hold position. Higher values return faster. " +
                 "Rigidbody modes: rate of planar velocity decay.")]
        public float DecayRate;

        [Tooltip("Position sets target directly. Velocity accumulates into state. " +
                 "RigidbodyVelocity sets PhysicsVelocity.Linear (planar only) each frame. " +
                 "RigidbodyForce accumulates force with Drag dissipation each frame. " +
                 "RigidbodyImpulse fires a single impulse on input rising-edge.")]
        public AxisTransformMode Mode = AxisTransformMode.Position;

        [Header("Options")]
        [Tooltip("Evaluate input strictly in world space without rotating along with the parent transform.")]
        public bool IgnoreParentRotation = true;

        [Tooltip("Do not reset input to zero when input is released.")]
        public bool KeepLastPosition;

        [Tooltip("Evaluate input in local space of target.")]
        public bool LocalSpace;

        [Tooltip("Evaluate input relative to Main Camera.")]
        public bool CameraRelative;

        public override double duration => 1;
        public ClipCaps clipCaps => ClipCaps.None;

        public override void Bake(Entity entity, BakingContext context)
        {
            if (!EntityLinkAuthoringUtility.TryGetKey(ConsumerLink, out var linkKey))
            {
                Debug.LogError($"AxisTransformClip '{name}' missing ConsumerLink schema.");
                return;
            }

            EntityLinkAuthoringUtility.TryGetKey(AnchorLink, out var anchorLinkKey);

            byte actionId = 0;
            if (Action != null)
                if (!BovineLabs.Timeline.PlayerInputs.Authoring.MultiInputSettingsAuthoringUtility.TryGetIndex(Action, out actionId))
                    Debug.LogError(
                        $"AxisTransformClip '{name}' action '{Action.name}' not found in MultiInputSettings.", this);

            var flags = AxisTransformFlags.None;
            if (IgnoreParentRotation) flags |= AxisTransformFlags.IgnoreParentRotation;
            if (KeepLastPosition) flags |= AxisTransformFlags.KeepLastPosition;
            if (LocalSpace) flags |= AxisTransformFlags.LocalSpace;
            if (CameraRelative) flags |= AxisTransformFlags.CameraRelative;

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
                Drag = Drag,
                DecayRate = DecayRate,
                Mode = Mode,
                Flags = flags
            });

            commands.AddComponent<AxisTransformState>();

            base.Bake(entity, context);
        }
    }
}