using System;
using BovineLabs.Core.Authoring.EntityCommands;
using BovineLabs.Core.Collections;
using BovineLabs.Timeline.Authoring;
using BovineLabs.Timeline.PlayerInputs.Data;
using Unity.Entities;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Timeline;

namespace BovineLabs.Timeline.PlayerInputs.Authoring
{
    public sealed class InputBufferClearClip : DOTSClip, ITimelineClipAsset
    {
        [Tooltip("Empty means clear ALL history. Specifics clear only those.")]
        public InputActionReference[] ActionsToClear = Array.Empty<InputActionReference>();

        public override double duration => 1;
        public ClipCaps clipCaps => ClipCaps.None;

        public override void Bake(Entity entity, BakingContext context)
        {
            var mask = default(BitArray256);
            if (ActionsToClear != null)
                foreach (var action in ActionsToClear)
                {
                    if (action == null) continue;
                    if (MultiInputSettingsAuthoringUtility.TryGetIndex(action, out var id))
                        mask[id] = true;
                    else
                        Debug.LogError(
                            $"InputBufferClearClip '{name}' action '{action.name}' not found in MultiInputSettings.",
                            this);
                }

            var commands = new BakerCommands(context.Baker, entity);
            commands.AddComponent(new BufferClearConfig { ActionMask = mask });
            base.Bake(entity, context);
        }
    }
}