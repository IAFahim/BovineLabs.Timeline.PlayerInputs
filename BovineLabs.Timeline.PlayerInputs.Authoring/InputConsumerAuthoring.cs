using BovineLabs.Core.Authoring.EntityCommands;
using BovineLabs.Timeline.EntityLinks.Authoring;
using BovineLabs.Timeline.PlayerInputs.Data.Builders;
using Unity.Entities;
using UnityEngine;

namespace BovineLabs.Timeline.PlayerInputs.Authoring
{
    public class InputConsumerAuthoring : MonoBehaviour
    {
        public byte PlayerId;

        [Tooltip("Where to route transduced hardware input events. Defaults to self.")]
        public EntityLinkSchema routeEventsTo;

        public class Baker : Baker<InputConsumerAuthoring>
        {
            public override void Bake(InputConsumerAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);
                var targetEntity = entity;

                if (authoring.routeEventsTo != null)
                {
                    var root = authoring.transform.root.GetComponentInChildren<EntityLinkRootAuthoring>();
                    if (root != null)
                        foreach (var link in root.Links)
                            if (link.Schema == authoring.routeEventsTo && link.Target != null)
                            {
                                targetEntity = GetEntity(link.Target, TransformUsageFlags.None);
                                break;
                            }
                }

                var commands = new BakerCommands(this, entity);
                var builder = new InputConsumerBuilder()
                    .WithPlayerId(authoring.PlayerId)
                    .WithRoute(targetEntity);
                builder.ApplyTo(ref commands);
            }
        }
    }
}