using BovineLabs.Core.Authoring.EntityCommands;
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
    public sealed class InputEventsClip : DOTSClip, ITimelineClipAsset
    {
        [Tooltip("Link to the input consumer whose action this clip watches.")]
        [UnityEngine.Serialization.FormerlySerializedAs("ConsumerLink")]
        public EntityLinkSchema consumerLink;

        [Tooltip("Link used to resolve the event target when EventRouteTo needs one.")]
        [UnityEngine.Serialization.FormerlySerializedAs("EventRouteLink")]
        public EntityLinkSchema eventRouteLink;

        [Tooltip("Where to resolve the entity that owns the ConsumerLink from.")]
        public Target ReadRootFrom = Target.Owner;

        [Tooltip("Input action whose start/end edges fire the events below.")]
        public InputActionReference Action;

        [Header("Events")] [Tooltip("Where to resolve the entity that receives the fired events from.")]
        public Target EventRouteTo = Target.Self;

        [Tooltip("Condition event fired when the action input begins.")]
        public ConditionEventObject OnInputStart;

        [Tooltip("Condition event fired when the action input ends.")]
        public ConditionEventObject OnInputEnd;

        public override double duration => 1;
        public ClipCaps clipCaps => ClipCaps.None;

        public override void Bake(Entity entity, BakingContext context)
        {
            MultiInputSettingsAuthoringUtility.DependsOnSettings(context.Baker);

            var actionId = byte.MaxValue;
            if (Action == null)
            {
                Debug.LogError(
                    $"InputEventsClip '{name}' has no Action assigned; it will watch no action and fire no events.",
                    this);
            }
            else if (!MultiInputSettingsAuthoringUtility.TryGetIndex(Action, out actionId))
            {
                actionId = byte.MaxValue;
                Debug.LogError($"InputEventsClip '{name}' action '{Action.name}' not found in MultiInputSettings.",
                    this);
            }

            var commands = new BakerCommands(context.Baker, entity);
            context.Baker.DependsOn(OnInputStart);
            context.Baker.DependsOn(OnInputEnd);
            commands.AddComponent(new InputEventsConfig
            {
                Consumer = EntityLinkAuthoringUtility.BakeRef(context.Baker, consumerLink, ReadRootFrom),
                ActionId = actionId,
                EventRoute = EntityLinkAuthoringUtility.BakeRef(context.Baker, eventRouteLink, EventRouteTo),
                OnInputStart = OnInputStart != null ? new ConditionKey(OnInputStart.Key) : ConditionKey.Null,
                OnInputEnd = OnInputEnd != null ? new ConditionKey(OnInputEnd.Key) : ConditionKey.Null
            });

            commands.AddComponent<InputEventsState>();

            base.Bake(entity, context);
        }
    }
}