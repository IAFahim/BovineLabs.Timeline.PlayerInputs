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
        [Header("Field")] public GridFieldSchemaObject Field;

        public FlowBias Bias = FlowBias.Descend;

        [Header("Routing")] public Target ReadRootFrom = Target.Owner;

        public EntityLinkSchema ConsumerLink;

        [Tooltip("Movement action whose axis this fake input replaces. " +
                 "Must match the ActionId the consumer's AxisTransform clip reads.")]
        public InputActionReference Action;

        [Header("Shaping")] [Range(0f, 1f)] public float Gain = 1f;

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

            byte actionId = 0;
            if (Action != null && !MultiInputSettingsAuthoringUtility.TryGetIndex(Action, out actionId))
                Debug.LogError($"FlowInputClip '{name}' action '{Action.name}' not found in MultiInputSettings.", this);

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