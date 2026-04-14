using System;
using BovineLabs.Core.Collections;
using BovineLabs.Timeline.Authoring;
using Bovinelabs.Timeline.PlayerInputs.Data;
using Unity.Entities;
using UnityEngine.InputSystem;
using UnityEngine.Timeline;
using InputSettings = Bovinelabs.Timeline.PlayerInputs.Data.InputSettings;

namespace Bovinelabs.Timeline.PlayerInputs.Authoring
{
    public sealed class PlayerInputCancelWindowClip : DOTSClip, ITimelineClipAsset
    {
        public InputActionReference[] inputActionReferences = Array.Empty<InputActionReference>();
        public override double duration => 1;

        public ClipCaps clipCaps => ClipCaps.None;

        public override void Bake(Entity clipEntity, BakingContext context)
        {
            var mask = new BitArray256();

            foreach (var inputActionReference in inputActionReferences)
            {
                var index = InputSettings.GetIndex(inputActionReference);
                mask[index] = true;
            }

            context.Baker.AddComponent(clipEntity, new InputCancelWindowConfig
            {
                AllowedMask = mask
            });

            base.Bake(clipEntity, context);
        }
    }
}