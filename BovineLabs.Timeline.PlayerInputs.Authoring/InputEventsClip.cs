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
        public Target ReadRootFrom = Target.Owner;
        public EntityLinkSchema ConsumerLink;

        public InputActionReference Action;

        [Header("Events")] public Target EventRouteTo = Target.Self;

        public EntityLinkSchema EventRouteLink;
        public ConditionEventObject OnInputStart;
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

            byte actionId = 0;
            if (Action != null)
                if (!MultiInputSettings.TryGetIndex(Action, out actionId))
                    Debug.LogError(
                        $"InputEventsClip '{name}' action '{Action.name}' not found in MultiInputSettings.", this);

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