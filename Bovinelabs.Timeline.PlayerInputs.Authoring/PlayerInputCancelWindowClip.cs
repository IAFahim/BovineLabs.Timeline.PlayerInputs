using System.Collections.Generic;
using BovineLabs.Core.Collections;
using BovineLabs.Timeline.Authoring;
using Bovinelabs.Timeline.PlayerInputs.Data;
using Unity.Entities;
using UnityEngine.Timeline;

namespace Bovinelabs.Timeline.PlayerInputs.Authoring
{
    public sealed class PlayerInputCancelWindowClip : DOTSClip, ITimelineClipAsset
    {
        public List<InputSettings.InputMapping> allowedActions = new();
        public override double duration => 1;

        public ClipCaps clipCaps => ClipCaps.None;

        public override void Bake(Entity clipEntity, BakingContext context)
        {
            var mask = new BitArray256();
            foreach (var mapping in allowedActions) 
            {
                mask[mapping.Value] = true;
            }

            context.Baker.AddComponent(clipEntity, new InputCancelWindowConfig
            {
                AllowedMask = mask
            });

            base.Bake(clipEntity, context);
        }
    }
}