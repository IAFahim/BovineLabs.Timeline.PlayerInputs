using BovineLabs.Core.Authoring.EntityCommands;
using BovineLabs.Timeline.PlayerInputs.Data.Builders;
using Unity.Entities;
using UnityEngine;

namespace BovineLabs.Timeline.PlayerInputs.Authoring
{
    public class InputConsumerAuthoring : MonoBehaviour
    {
        public byte PlayerId;

        public class Baker : Baker<InputConsumerAuthoring>
        {
            public override void Bake(InputConsumerAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);

                var commands = new BakerCommands(this, entity);
                var builder = new InputConsumerBuilder()
                    .WithPlayerId(authoring.PlayerId);
                builder.ApplyTo(ref commands);
            }
        }
    }
}