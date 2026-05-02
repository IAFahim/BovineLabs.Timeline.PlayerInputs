using System;
using System.Collections.Generic;
using BovineLabs.Core.Collections;
using BovineLabs.Timeline.Authoring;
using BovineLabs.Timeline.PlayerInputs.Data;
using Unity.Collections;
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
            {
                foreach (var action in ActionsToClear)
                {
                    if (MultiInputSettings.TryGetIndex(action, out var id))
                    {
                        mask[id] = true;
                    }
                }
            }

            context.Baker.AddComponent(entity, new BufferClearConfig { ActionMask = mask });
            context.Baker.SetComponentEnabled<BufferClearConfig>(entity, false);
            base.Bake(entity, context);
        }
    }
}