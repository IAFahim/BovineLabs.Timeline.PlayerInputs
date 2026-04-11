using BovineLabs.Timeline.Authoring;
using Bovinelabs.Timeline.PlayerInputs.Data;
using Unity.Entities;
using UnityEngine.Timeline;

namespace Bovinelabs.Timeline.PlayerInputs.Authoring
{
    public sealed class PlayerInputBufferClearClip : DOTSClip, ITimelineClipAsset
    {
        public bool ClearAll = true;
        public InputSettings.InputMapping ActionToClear;
        public override double duration => 1;

        public ClipCaps clipCaps => ClipCaps.None;

        public override void Bake(Entity clipEntity, BakingContext context)
        {
            context.Baker.AddComponent(clipEntity, new InputBufferClearTrigger
            {
                ClearAll = ClearAll,
                ActionId = ActionToClear.Value
            });

            context.Baker.SetComponentEnabled<InputBufferClearTrigger>(clipEntity, false);

            base.Bake(clipEntity, context);
        }
    }
}