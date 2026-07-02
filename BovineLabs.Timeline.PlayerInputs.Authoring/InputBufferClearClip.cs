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
    public sealed class InputBufferClearClip : DOTSClip, ITimelineClipAsset
    {
        [Tooltip("Where to resolve the entity that owns the ConsumerLink from.")]
        public Target ReadRootFrom = Target.Owner;

        [Tooltip("Link to the input consumer whose buffer history this clip clears.")]
        public EntityLinkSchema ConsumerLink;

        [Tooltip("Empty means clear ALL history. Specifics clear only those.")]
        public InputActionReference[] ActionsToClear = Array.Empty<InputActionReference>();

        public override double duration => 1;
        public ClipCaps clipCaps => ClipCaps.None;

        public override void Bake(Entity entity, BakingContext context)
        {
            MultiInputSettingsAuthoringUtility.DependsOnSettings(context.Baker);

            if (!EntityLinkAuthoringUtility.TryGetKey(ConsumerLink, out var consumerLinkKey))
            {
                Debug.LogError($"InputBufferClearClip '{name}' missing ConsumerLink schema.", this);
                return;
            }

            var mask = default(BitArray256);
            var requestedAny = false;
            if (ActionsToClear != null)
                foreach (var action in ActionsToClear)
                {
                    if (action == null) continue;
                    requestedAny = true;
                    if (MultiInputSettingsAuthoringUtility.TryGetIndex(action, out var id))
                        mask[id] = true;
                    else
                        Debug.LogError(
                            $"InputBufferClearClip '{name}' action '{action.name}' not found in MultiInputSettings.",
                            this);
                }

            var commands = new BakerCommands(context.Baker, entity);
            commands.AddComponent(new BufferClearConfig
            {
                ReadRootFrom = ReadRootFrom,
                ConsumerLinkKey = consumerLinkKey,
                ActionMask = mask,

                // Clear-everything only when the designer specified no actions at all; a requested-but-unresolved
                // list must NOT wipe the whole history (the per-action error above already flags the misconfig).
                ClearAll = !requestedAny,
            });
            base.Bake(entity, context);
        }
    }
}