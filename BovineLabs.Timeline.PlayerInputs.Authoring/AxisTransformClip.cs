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

        [Tooltip("Scales [-1,1] input range. Range=5 means output spans [-5,5].")]
        public float Range = 1f;

        [Tooltip("Normal of plane movement applied. Up=(0,1,0) moves XZ.")]
        public Vector3 Plane = Vector3.up;

        [Tooltip("Lerp speed toward target position. 0 = instant snap.")]
        public float Smoothing = 0f;

        [Tooltip("Max distance from origin target can move. 0 = unlimited.")]
        public float ClampRadius = 0f;

        [Tooltip("Position sets target directly. Velocity accumulates. LocalSpace rotates basis by Target. CameraRelative rotates basis by CameraMain.")]
        public AxisTransformMode Mode = AxisTransformMode.Position;

        public override double duration => 1;
        public ClipCaps clipCaps => ClipCaps.None;

        public override void Bake(Entity entity, BakingContext context)
        {
            if (!EntityLinkAuthoringUtility.TryGetKey(ConsumerLink, out var linkKey))
            {
                Debug.LogError($"AxisTransformClip '{name}' missing ConsumerLink schema.");
                return;
            }

            byte actionId = 0;
            if (Action != null)
                MultiInputSettings.TryGetIndex(Action, out actionId);

            context.Baker.AddComponent(entity, new AxisTransformConfig
            {
                ReadRootFrom = ReadRootFrom,
                ConsumerLinkKey = linkKey,
                ActionId = actionId,
                Range = Range,
                Plane = Plane,
                Smoothing = Smoothing,
                ClampRadius = ClampRadius,
                Mode = Mode
            });

            context.Baker.AddComponent<AxisTransformState>(entity);

            base.Bake(entity, context);
        }
    }
}