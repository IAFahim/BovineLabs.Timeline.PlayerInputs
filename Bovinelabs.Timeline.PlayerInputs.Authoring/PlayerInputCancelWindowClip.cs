using System.Collections.Generic;
using BovineLabs.Timeline.Authoring;
using Bovinelabs.Timeline.PlayerInputs.Data;
using Unity.Entities;
using UnityEngine.Timeline;

namespace Bovinelabs.Timeline.PlayerInputs.Authoring
{
    public sealed class PlayerInputCancelWindowClip : DOTSClip, ITimelineClipAsset
    {
        public List<InputSettings.InputMapping> AllowedActions = new();

        public ClipCaps clipCaps => ClipCaps.None;
        public override double duration => 1;

        public override void Bake(Entity clipEntity, BakingContext context)
        {
            var mask = new InputBitmask();
            foreach (var mapping in this.AllowedActions)
            {
                mask.Set(mapping.Value);
            }

            context.Baker.AddComponent(clipEntity, new InputCancelWindowConfig
            {
                AllowedMask = mask
            });

            base.Bake(clipEntity, context);
        }
    }
}