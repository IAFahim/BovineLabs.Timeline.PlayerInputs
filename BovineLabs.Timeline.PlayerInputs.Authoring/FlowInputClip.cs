using BovineLabs.Core.Authoring.EntityCommands;
using BovineLabs.Reaction.Data.Core;
using BovineLabs.Timeline.Authoring;
using BovineLabs.Timeline.EntityLinks.Authoring;
using BovineLabs.Timeline.Grid.Influence.Authoring;
using BovineLabs.Timeline.Grid.Influence.Data.Flows;
using BovineLabs.Timeline.PlayerInputs.Authoring;
using BovineLabs.Timeline.PlayerInputs.Flow.Data.Builders;
using Unity.Entities;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Timeline;

namespace BovineLabs.Timeline.PlayerInputs.Flow.Authoring
{
    public sealed class FlowInputClip : DOTSClip, ITimelineClipAsset
    {
        [Header("Field")]
        [Tooltip("Grid field schema sampled to synthesise the fake axis.")]
        public GridFieldSchemaObject Field;

        [Tooltip("Direction the flow follows the field gradient: Descend goes downhill, Ascend uphill.")]
        public FlowBias Bias = FlowBias.Descend;

        [Header("Routing")]
        [Tooltip("Where to resolve the entity that owns the ConsumerLink from.")]
        public Target ReadRootFrom = Target.Owner;

        [Tooltip("Link to the input consumer whose action axis this fake input replaces.")]
        public EntityLinkSchema ConsumerLink;

        [Tooltip("Movement action whose axis this fake input replaces. " +
                 "Must match the ActionId the consumer's AxisTransform clip reads.")]
        public InputActionReference Action;

        [Header("Shaping")]
        [Range(0f, 1f)]
        [Tooltip("Scales the synthesised axis magnitude. 1 = full flow, 0 = no input.")]
        public float Gain = 1f;

        [Tooltip("Sample point relative to the bound target.")]
        public Vector3 LocalOffset;

        public override double duration => 1;
        public ClipCaps clipCaps => ClipCaps.Blending | ClipCaps.Looping;

        public override void Bake(Entity entity, BakingContext context)
        {
            if (Field == null)
            {
                Debug.LogError($"FlowInputClip '{name}' has no Field schema assigned. Clip will be skipped.", this);
                return;
            }

            if (!EntityLinkAuthoringUtility.TryGetKey(ConsumerLink, out var linkKey))
            {
                Debug.LogError($"FlowInputClip '{name}' missing ConsumerLink schema.", this);
                return;
            }

            byte actionId = byte.MaxValue;
            if (Action == null)
                Debug.LogError($"FlowInputClip '{name}' has no Action assigned; the fake axis will drive no action.", this);
            else if (!MultiInputSettingsAuthoringUtility.TryGetIndex(Action, out actionId))
            {
                actionId = byte.MaxValue;
                Debug.LogError($"FlowInputClip '{name}' action '{Action.name}' not found in MultiInputSettings.", this);
            }

            context.Baker.DependsOn(Field);

            var builder = new FlowInputBuilder
            {
                FieldKey = Field.Id,
                Bias = Bias,
                ActionId = actionId,
                ReadRootFrom = ReadRootFrom,
                ConsumerLinkKey = linkKey,
                LocalOffset = LocalOffset,
                Gain = Gain
            };
            var commands = new BakerCommands(context.Baker, entity);
            builder.ApplyTo(ref commands);

            base.Bake(entity, context);
        }
    }
}