using BovineLabs.Timeline.Authoring;
using BovineLabs.Reaction.Authoring.Conditions;
using BovineLabs.Reaction.Data.Conditions;
using Bovinelabs.Timeline.PlayerInputs.Data;
using Unity.Entities;
using UnityEngine.Timeline;

namespace Bovinelabs.Timeline.PlayerInputs.Authoring
{
    public sealed class PlayerInputConditionInvokerClip : DOTSClip, ITimelineClipAsset
    {
        public InputSettings.InputMapping Action;
        public InputPhase Phase;
        public ConditionEventObject Condition;
        public int Value = 1;

        public ClipCaps clipCaps => ClipCaps.None;
        public override double duration => 1;

        public override void Bake(Entity clipEntity, BakingContext context)
        {
            context.Baker.AddComponent(clipEntity, new InputInvokerConfig
            {
                ActionId = this.Action.Value,
                Phase = this.Phase,
                Condition = this.Condition ? this.Condition.Key : ConditionKey.Null,
                Value = this.Value
            });

            base.Bake(clipEntity, context);
        }
    }
}