using BovineLabs.Reaction.Authoring.Conditions;
using BovineLabs.Reaction.Data.Conditions;
using BovineLabs.Timeline.Authoring;
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
        public override double duration => 1;

        public ClipCaps clipCaps => ClipCaps.None;

        public override void Bake(Entity clipEntity, BakingContext context)
        {
            context.Baker.AddComponent(clipEntity, new InputInvokerConfig
            {
                ActionId = Action.Value,
                Phase = Phase,
                Condition = Condition ? Condition.Key : ConditionKey.Null,
                Value = Value
            });

            base.Bake(clipEntity, context);
        }
    }
}