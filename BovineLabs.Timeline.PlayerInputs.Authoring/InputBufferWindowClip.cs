using System;
using BovineLabs.Core.Authoring.EntityCommands;
using BovineLabs.Core.Collections;
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
    public sealed class InputBufferWindowClip : DOTSClip, ITimelineClipAsset
    {
        [Tooltip("Link to the input consumer whose buffer this window opens.")]
        [UnityEngine.Serialization.FormerlySerializedAs("ConsumerLink")]
        public EntityLinkSchema consumerLink;

        [Tooltip("Where to resolve the entity that owns the ConsumerLink from.")]
        public Target ReadRootFrom = Target.Owner;

        [Tooltip("Empty means ALL inputs buffered. Specifics mean ONLY those are buffered. " +
                 "Empty = ALL actions. Broad windows can evict earlier combo steps under mashy input; " +
                 "prefer listing only the actions your sequences read.")]
        public InputActionReference[] AllowedActions = Array.Empty<InputActionReference>();

        public override double duration => 1;
        public ClipCaps clipCaps => ClipCaps.None;

        public override void Bake(Entity entity, BakingContext context)
        {
            MultiInputSettingsAuthoringUtility.DependsOnSettings(context.Baker);

            if (!MultiInputSettingsAuthoringUtility.RequireLink(consumerLink, this, $"InputBufferWindowClip '{name}'", "consumerLink"))
                return;

            var mask = default(BitArray256);
            if (AllowedActions == null || AllowedActions.Length == 0)
                for (var i = 0; i < MultiInputSettings.MaxActions; i++)
                    mask[i] = true;
            else
                foreach (var action in AllowedActions)
                {
                    if (action == null) continue;
                    if (MultiInputSettingsAuthoringUtility.TryGetIndex(action, out var id))
                        mask[id] = true;
                    else
                        Debug.LogError(
                            $"InputBufferWindowClip '{name}' action '{action.name}' not found in MultiInputSettings.",
                            this);
                }

            var commands = new BakerCommands(context.Baker, entity);
            commands.AddComponent(new BufferWindowConfig
            {
                Consumer = EntityLinkAuthoringUtility.BakeRef(context.Baker, consumerLink, ReadRootFrom),
                AllowedActions = mask
            });
            base.Bake(entity, context);
        }
    }
}