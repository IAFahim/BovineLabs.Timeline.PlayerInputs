using BovineLabs.Reaction.Authoring.Conditions;
using BovineLabs.Reaction.Data.Conditions;
using BovineLabs.Timeline.Authoring;
using BovineLabs.Timeline.EntityLinks.Authoring;
using BovineLabs.Timeline.PlayerInputs.Data;
using Unity.Entities;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Timeline;

namespace BovineLabs.Timeline.PlayerInputs.Authoring
{
    public sealed class PlayerInputConditionInvokerClip : DOTSClip, ITimelineClipAsset
    {
        public InputActionReference inputActionReference;
        public InputPhase phase;
        public ConditionEventObject condition;
        public int value = 1;

        [Tooltip("EntityLink to route the condition event to. If empty, routes to the bound track target.")]
        public EntityLinkSchema routeTo;

        public override double duration => 1;
        public ClipCaps clipCaps => ClipCaps.None;

        public override void Bake(Entity clipEntity, BakingContext context)
        {
            var targetEntity = context.Target;
            if (routeTo != null && context.TryResolveLink(routeTo, out var linked))
                targetEntity = context.Baker.GetEntity(linked, TransformUsageFlags.None);

            context.Baker.AddComponent(clipEntity, new InputInvokerConfig
            {
                ActionId = MuliInputSettings.GetIndex(inputActionReference),
                Phase = phase,
                Condition = condition ? condition.Key : ConditionKey.Null,
                Value = value,
                RouteEntity = targetEntity
            });

            base.Bake(clipEntity, context);
        }
    }
}