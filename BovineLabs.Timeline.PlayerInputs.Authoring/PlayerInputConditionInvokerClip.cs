using BovineLabs.Reaction.Authoring.Conditions;
using BovineLabs.Reaction.Data.Conditions;
using BovineLabs.Timeline.Authoring;
using BovineLabs.Timeline.PlayerInputs.Data;
using Unity.Entities;
using UnityEngine.InputSystem;
using UnityEngine.Timeline;
using InputSettings = BovineLabs.Timeline.PlayerInputs.Data.InputSettings;

namespace BovineLabs.Timeline.PlayerInputs.Authoring
{
    public sealed class PlayerInputConditionInvokerClip : DOTSClip, ITimelineClipAsset
    {
        public InputActionReference inputActionReference;
        public InputPhase phase;
        public ConditionEventObject condition;
        public int value = 1;
        public override double duration => 1;

        public ClipCaps clipCaps => ClipCaps.None;

        public override void Bake(Entity clipEntity, BakingContext context)
        {
            context.Baker.AddComponent(clipEntity, new InputInvokerConfig
            {
                ActionId = InputSettings.GetIndex(inputActionReference),
                Phase = phase,
                Condition = condition ? condition.Key : ConditionKey.Null,
                Value = value
            });

            base.Bake(clipEntity, context);
        }
    }
}