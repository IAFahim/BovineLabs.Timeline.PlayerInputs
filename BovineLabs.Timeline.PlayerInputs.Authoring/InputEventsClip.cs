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
        [Tooltip("Where to resolve the entity that owns the ConsumerLink from.")]
        public Target ReadRootFrom = Target.Owner;

        [Tooltip("Link to the input consumer whose action this clip watches.")]
        public EntityLinkSchema ConsumerLink;

        [Tooltip("Input action whose start/end edges fire the events below.")]
        public InputActionReference Action;

        [Header("Events")]
        [Tooltip("Where to resolve the entity that receives the fired events from.")]
        public Target EventRouteTo = Target.Self;

        [Tooltip("Link used to resolve the event target when EventRouteTo needs one.")]
        public EntityLinkSchema EventRouteLink;

        [Tooltip("Condition event fired when the action input begins.")]
        public ConditionEventObject OnInputStart;

        [Tooltip("Condition event fired when the action input ends.")]
        public ConditionEventObject OnInputEnd;

        public override double duration => 1;
        public ClipCaps clipCaps => ClipCaps.None;

        public override void Bake(Entity entity, BakingContext context)
        {
            if (!EntityLinkAuthoringUtility.TryGetKey(ConsumerLink, out var linkKey))
            {
                Debug.LogError($"InputEventsClip '{name}' missing ConsumerLink schema.");
                return;
            }

            EntityLinkAuthoringUtility.TryGetKey(EventRouteLink, out var eventRouteLinkKey);

            byte actionId = byte.MaxValue;
            if (Action == null)
                Debug.LogError($"InputEventsClip '{name}' has no Action assigned; it will watch no action and fire no events.", this);
            else if (!MultiInputSettingsAuthoringUtility.TryGetIndex(Action, out actionId))
            {
                actionId = byte.MaxValue;
                Debug.LogError($"InputEventsClip '{name}' action '{Action.name}' not found in MultiInputSettings.", this);
            }

            var commands = new BakerCommands(context.Baker, entity);
            commands.AddComponent(new InputEventsConfig
            {
                ReadRootFrom = ReadRootFrom,
                ConsumerLinkKey = linkKey,
                ActionId = actionId,
                EventRouteTo = EventRouteTo,
                EventRouteLinkKey = eventRouteLinkKey,
                OnInputStart = OnInputStart != null ? OnInputStart.Key : ConditionKey.Null,
                OnInputEnd = OnInputEnd != null ? OnInputEnd.Key : ConditionKey.Null
            });

            commands.AddComponent<InputEventsState>();

            base.Bake(entity, context);
        }
    }
}