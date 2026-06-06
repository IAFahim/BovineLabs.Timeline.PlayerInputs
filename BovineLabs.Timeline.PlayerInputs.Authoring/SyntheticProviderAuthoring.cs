using BovineLabs.Core.Authoring.EntityCommands;
using BovineLabs.Timeline.PlayerInputs.Flow.Data.Builders;
using Unity.Entities;
using UnityEngine;

namespace BovineLabs.Timeline.PlayerInputs.Flow.Authoring
{
    public class SyntheticProviderAuthoring : MonoBehaviour
    {
        public byte PlayerId;

        public class Baker : Baker<SyntheticProviderAuthoring>
        {
            public override void Bake(SyntheticProviderAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);
                var commands = new BakerCommands(this, entity);
                SyntheticProviderBuilder.Build(ref commands, authoring.PlayerId);
            }
        }
    }
}