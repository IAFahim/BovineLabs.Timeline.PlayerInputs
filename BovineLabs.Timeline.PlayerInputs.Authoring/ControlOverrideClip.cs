using BovineLabs.Core.Authoring.EntityCommands;
using BovineLabs.Reaction.Data.Core;
using BovineLabs.Timeline.Authoring;
using BovineLabs.Timeline.EntityLinks.Authoring;
using BovineLabs.Timeline.PlayerInputs.Data;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Timeline;

namespace BovineLabs.Timeline.PlayerInputs.Authoring
{
    public sealed class ControlOverrideClip : DOTSClip, ITimelineClipAsset
    {
        [Tooltip("Link to the controllable input consumer this clip takes over while active.")]
        public EntityLinkSchema consumerLink;

        [Tooltip("Where to resolve the entity that owns the ConsumerLink from.")]
        public Target ReadRootFrom = Target.Owner;

        public override double duration => 1;
        public ClipCaps clipCaps => ClipCaps.None;

        public override void Bake(Entity entity, BakingContext context)
        {
            MultiInputSettingsAuthoringUtility.DependsOnSettings(context.Baker);

            if (!MultiInputSettingsAuthoringUtility.RequireLink(consumerLink, this, $"ControlOverrideClip '{name}'",
                    "consumerLink"))
                return;

            var commands = new BakerCommands(context.Baker, entity);
            commands.AddComponent(new ControlOverrideConfig
            {
                Consumer = EntityLinkAuthoringUtility.BakeRef(context.Baker, consumerLink, ReadRootFrom)
            });
            base.Bake(entity, context);
        }
    }
}
