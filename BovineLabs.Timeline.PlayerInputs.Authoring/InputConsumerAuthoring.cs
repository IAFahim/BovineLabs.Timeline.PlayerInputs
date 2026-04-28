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

                var commands = new BakerCommands(this, entity);
                var builder = new InputConsumerBuilder()
                    .WithPlayerId(authoring.PlayerId);
                builder.ApplyTo(ref commands);
            }
        }
    }
}