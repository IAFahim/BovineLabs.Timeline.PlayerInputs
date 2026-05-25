using System;
using BovineLabs.Core.Collections;
using BovineLabs.Timeline.Authoring;
using BovineLabs.Timeline.PlayerInputs.Data;
using Unity.Entities;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Timeline;

namespace BovineLabs.Timeline.PlayerInputs.Authoring
{
    public sealed class InputBufferWindowClip : DOTSClip, ITimelineClipAsset
    {
        [Tooltip("Empty means ALL inputs buffered. Specifics mean ONLY those are buffered.")]
        public InputActionReference[] AllowedActions = Array.Empty<InputActionReference>();

        public override double duration => 1;
        public ClipCaps clipCaps => ClipCaps.None;

        public override void Bake(Entity entity, BakingContext context)
        {
            var mask = default(BitArray256);
            if (AllowedActions == null || AllowedActions.Length == 0)
                for (var i = 0; i < 256; i++)
                    mask[i] = true;
            else
                foreach (var action in AllowedActions)
                {
                    if (action == null) continue;
                    if (MultiInputSettings.TryGetIndex(action, out var id))
                    {
                        mask[id] = true;
                    }
                    else
                    {
                        Debug.LogError($"InputBufferWindowClip '{name}' action '{action.name}' not found in MultiInputSettings.", this);
                    }
                }

            context.Baker.AddComponent(entity, new BufferWindowConfig { AllowedActions = mask });
            base.Bake(entity, context);
        }
    }
}