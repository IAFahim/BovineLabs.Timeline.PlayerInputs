using System;
using BovineLabs.Reaction.Authoring.Conditions;
using BovineLabs.Reaction.Data.Conditions;
using BovineLabs.Timeline.Authoring;
using BovineLabs.Timeline.EntityLinks.Authoring;
using BovineLabs.Timeline.PlayerInputs.Data;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Timeline;

namespace BovineLabs.Timeline.PlayerInputs.Authoring
{
    [Serializable]
    public struct CommandStepData
    {
        public InputActionReference Action;
        public CommandMode Mode;
    }

    [Serializable]
    public class CommandSequenceData
    {
        public CommandStepData[] Steps = Array.Empty<CommandStepData>();
        public ConditionEventObject Condition;
        public int Value = 1;
    }
        [Tooltip("Sequences are evaluated top-to-bottom. High priority should be first. First successful match triggers event and completes sequence clip.")]

    public sealed class CommandSequenceClip : DOTSClip, ITimelineClipAsset
    {
        public CommandSequenceData[] Sequences = Array.Empty<CommandSequenceData>();
        public EntityLinkSchema RouteTo;

        public override double duration => .5f;
        public ClipCaps clipCaps => ClipCaps.None;

        public override void Bake(Entity entity, BakingContext context)
        {
            // Resolve once for the whole clip — stored in IComponentData, not blob
            var target = context.Target;
            if (RouteTo != null && context.TryResolveLink(RouteTo, out var linked))
                target = context.Baker.GetEntity(linked, TransformUsageFlags.None);

            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<CommandBlob>();
            var seqArray = builder.Allocate(ref root.Sequences, Sequences.Length);

            for (var s = 0; s < Sequences.Length; s++)
            {
                var seqData = Sequences[s];
                seqArray[s].Condition = seqData.Condition ? seqData.Condition.Key : ConditionKey.Null;
                seqArray[s].Value = seqData.Value;
                // NO RouteEntity here

                var stepArray = builder.Allocate(ref seqArray[s].Steps, seqData.Steps.Length);
                for (var i = 0; i < seqData.Steps.Length; i++)
                {
                    if (MultiInputSettings.TryGetIndex(seqData.Steps[i].Action, out var id))
                        stepArray[i] = new CommandStep { ActionId = id, Mode = seqData.Steps[i].Mode };
                }
            }

            var blobRef = builder.CreateBlobAssetReference<CommandBlob>(Allocator.Persistent);
            builder.Dispose();

            context.Baker.AddBlobAsset(ref blobRef, out _);
            context.Baker.AddComponent(entity, new CommandSequenceConfig
            {
                Blob = blobRef,
                RouteEntity = target  // proper IComponentData field — entity remapping works
            });
            context.Baker.AddComponent<CommandSequenceState>(entity);

            base.Bake(entity, context);
        }
    }
}