using System;
using BovineLabs.Core.Authoring.EntityCommands;
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
        public InputPhase Phase;
    }

    [Serializable]
    public class CommandSequenceData
    {
        public CommandStepData[] Steps = Array.Empty<CommandStepData>();
        public ConditionEventObject Condition;
        public int Value = 1;
    }

    [Tooltip(
        "Sequences are evaluated top-to-bottom. High priority should be first. First successful match triggers event and completes sequence clip.")]
    public sealed class CommandSequenceClip : DOTSClip, ITimelineClipAsset
    {
        public CommandSequenceData[] Sequences = Array.Empty<CommandSequenceData>();
        public EntityLinkSchema RouteTo;

        public override double duration => .5f;
        public ClipCaps clipCaps => ClipCaps.None;

        public override void Bake(Entity entity, BakingContext context)
        {
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

                var stepArray = builder.Allocate(ref seqArray[s].Steps, seqData.Steps.Length);
                for (var i = 0; i < seqData.Steps.Length; i++)
                {
                    var actionRef = seqData.Steps[i].Action;
                    if (actionRef != null)
                    {
                        if (MultiInputSettings.TryGetIndex(actionRef, out var id))
                        {
                            stepArray[i] = new CommandStep
                            {
                                ActionId = id,
                                Mode = seqData.Steps[i].Mode,
                                Phase = seqData.Steps[i].Phase
                            };
                        }
                        else
                        {
                            Debug.LogError($"CommandSequenceClip '{name}' action '{actionRef.name}' not found in MultiInputSettings.", this);
                        }
                    }
                }
            }

            var blobRef = builder.CreateBlobAssetReference<CommandBlob>(Allocator.Persistent);
            builder.Dispose();

            var commands = new BakerCommands(context.Baker, entity);
            commands.AddBlobAsset(ref blobRef, out _);
            commands.AddComponent(new CommandSequenceConfig
            {
                Blob = blobRef,
                RouteEntity = target
            });
            commands.AddComponent<CommandSequenceState>();

            base.Bake(entity, context);
        }
    }
}
