using System;
using System.Collections.Generic;
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
            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<BlobArray<byte>>();

            var resolved = new List<byte>();
            if (ActionsToClear != null)
            {
                foreach (var action in ActionsToClear)
                    if (MultiInputSettings.TryGetIndex(action, out var id))
                        resolved.Add(id);
            }

            var array = builder.Allocate(ref root, resolved.Count);
            for (var i = 0; i < resolved.Count; i++) array[i] = resolved[i];

            var blobRef = builder.CreateBlobAssetReference<BlobArray<byte>>(Allocator.Persistent);
            builder.Dispose();

            context.Baker.AddBlobAsset(ref blobRef, out _);
            context.Baker.AddComponent(entity, new BufferClearConfig { ActionIds = blobRef });
            context.Baker.SetComponentEnabled<BufferClearConfig>(entity, false);
            base.Bake(entity, context);
        }
    }
}