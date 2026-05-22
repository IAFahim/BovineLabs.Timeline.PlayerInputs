using BovineLabs.Reaction.Authoring.Conditions;
using BovineLabs.Reaction.Data.Conditions;
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

        public InputActionReference Action;

        [Tooltip("Scales [-1,1] input range. Range=5 means output spans [-5,5]. " +
                 "In Rigidbody modes: max speed (Velocity), force magnitude (Force), or impulse magnitude (Impulse).")]
        public float Range = 1f;

        [Tooltip("Normal of plane movement is applied to. Up=(0,1,0) moves XZ.")]
        public Vector3 Plane = Vector3.up;

        [Tooltip("Lerp speed toward target. 0 = instant snap. " +
                 "In RigidbodyVelocity: lerp speed toward target planar velocity.")]
        public float Smoothing;

        [Tooltip("Position/Velocity: max distance from origin. " +
                 "Rigidbody modes: max planar speed. 0 = unlimited.")]
        public float ClampRadius;

        [Tooltip("RigidbodyForce only: velocity dissipation coefficient per second. " +
                 "Higher = faster deceleration when no force is applied. 0 = no drag.")]
        public float Drag;

        [Tooltip("Position sets target directly. Velocity accumulates into state. " +
                 "LocalSpace rotates basis by Target rotation. CameraRelative rotates basis by CameraMain. " +
                 "RigidbodyVelocity sets PhysicsVelocity.Linear (planar only) each frame. " +
                 "RigidbodyForce accumulates force with Drag dissipation each frame. " +
                 "RigidbodyImpulse fires a single impulse on input rising-edge.")]
        public AxisTransformMode Mode = AxisTransformMode.Position;

        [Header("Options")]
        [Tooltip("Evaluate input strictly in world space without rotating along with the parent transform.")]
        public bool IgnoreParentRotation = true;

        [Tooltip("Instantly snaps back to origin (non-physics) or zeroes planar velocity (rigidbody) when there is no input.")]
        public bool ResetOnNoInput;

        [Header("Events")] public Target EventRouteTo = Target.Self;

        public EntityLinkSchema EventRouteLink;
        public ConditionEventObject OnInputStart;
        public ConditionEventObject OnInputEnd;

        public override double duration => 1;
        public ClipCaps clipCaps => ClipCaps.None;

        public override void Bake(Entity entity, BakingContext context)
        {
            if (!EntityLinkAuthoringUtility.TryGetKey(ConsumerLink, out var linkKey))
            {
                Debug.LogError($"AxisTransformClip '{name}' missing ConsumerLink schema.");
                return;
            }

            EntityLinkAuthoringUtility.TryGetKey(EventRouteLink, out var eventRouteLinkKey);

            byte actionId = 0;
            if (Action != null)
                MultiInputSettings.TryGetIndex(Action, out actionId);

            var modeFlags = Mode;
            if (IgnoreParentRotation) modeFlags |= AxisTransformMode.IgnoreParentRotation;

            context.Baker.AddComponent(entity, new AxisTransformConfig
            {
                ReadRootFrom = ReadRootFrom,
                ConsumerLinkKey = linkKey,
                ActionId = actionId,
                Range = Range,
                Plane = Plane,
                Smoothing = Smoothing,
                ClampRadius = ClampRadius,
                Drag = Drag,
                Mode = modeFlags,
                ResetOnNoInput = ResetOnNoInput,
                EventRouteTo = EventRouteTo,
                EventRouteLinkKey = eventRouteLinkKey,
                OnInputStart = OnInputStart != null ? OnInputStart.Key : ConditionKey.Null,
                OnInputEnd = OnInputEnd != null ? OnInputEnd.Key : ConditionKey.Null
            });

            context.Baker.AddComponent<AxisTransformState>(entity);

            base.Bake(entity, context);
        }
    }
}