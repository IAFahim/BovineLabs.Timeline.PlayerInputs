using System;
using System.Collections.Generic;
using BovineLabs.Timeline.Authoring;
using Bovinelabs.Timeline.PlayerInputs.Data;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Timeline;
using InputSettings = Bovinelabs.Timeline.PlayerInputs.Data.InputSettings;

namespace Bovinelabs.Timeline.PlayerInputs.Authoring
{
    public sealed class PlayerInputBufferClearClip : DOTSClip, ITimelineClipAsset
    {
        [Tooltip("Zero Means Clear all")]
        public InputActionReference[] actionsToClear = Array.Empty<InputActionReference>();

        public override double duration => 1;
        public ClipCaps clipCaps => ClipCaps.None;

        public override void Bake(Entity clipEntity, BakingContext context)
        {
            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<BlobArray<byte>>();

            if (actionsToClear.Length > 0)
            {
                var resolved = new List<byte>(actionsToClear.Length);
                foreach (var inputActionReference in actionsToClear)
                {
                    var indexOf = InputSettings.GetIndex(inputActionReference);
                    resolved.Add(indexOf);
                }

                var array = builder.Allocate(ref root, resolved.Count);
                for (var i = 0; i < resolved.Count; i++)
                    array[i] = resolved[i];
            }

            var blobRef = builder.CreateBlobAssetReference<BlobArray<byte>>(Allocator.Persistent);
            builder.Dispose();

            context.Baker.AddBlobAsset(ref blobRef, out _);
            context.Baker.AddComponent(clipEntity, new InputBufferClearTrigger { ActionIds = blobRef });
            context.Baker.SetComponentEnabled<InputBufferClearTrigger>(clipEntity, false);
            base.Bake(clipEntity, context);
        }
    }
}