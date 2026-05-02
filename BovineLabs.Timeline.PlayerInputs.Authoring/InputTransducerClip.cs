using System;
using BovineLabs.Reaction.Authoring.Conditions;
using BovineLabs.Reaction.Data.Conditions;
using BovineLabs.Timeline.Authoring;
using BovineLabs.Timeline.EntityLinks.Authoring;
using BovineLabs.Timeline.PlayerInputs.Data;
using Unity.Collections;
using Unity.Entities;
using UnityEngine.Timeline;

namespace BovineLabs.Timeline.PlayerInputs.Authoring
{
    public sealed class InputTransducerClip : DOTSClip, ITimelineClipAsset
    {
        public RequirementAuthoring[] Requirements = Array.Empty<RequirementAuthoring>();
        public ConditionEventObject Condition;
        public int Value = 1;
        public EntityLinkSchema RouteTo;

        public override double duration => 1;
        public ClipCaps clipCaps => ClipCaps.None;

        public override void Bake(Entity entity, BakingContext context)
        {
            var target = context.Target;
            if (RouteTo != null && context.TryResolveLink(RouteTo, out var linked))
                target = context.Baker.GetEntity(linked, TransformUsageFlags.None);

            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<TransducerBlob>();
            var array = builder.Allocate(ref root.Requirements, Requirements.Length);

            for (var i = 0; i < Requirements.Length; i++)
            {
                if (!MultiInputSettings.TryGetIndex(Requirements[i].Action, out var id)) continue;
                array[i] = new TransducerRequirement { ActionId = id, Mode = Requirements[i].BufferMode };
            }

            var blobRef = builder.CreateBlobAssetReference<TransducerBlob>(Allocator.Persistent);
            builder.Dispose();

            context.Baker.AddBlobAsset(ref blobRef, out _);
            context.Baker.AddComponent(entity, new TransducerConfig
            {
                Blob = blobRef,
                Condition = Condition ? Condition.Key : ConditionKey.Null,
                Value = Value,
                RouteEntity = target
            });

            base.Bake(entity, context);
        }
    }
}