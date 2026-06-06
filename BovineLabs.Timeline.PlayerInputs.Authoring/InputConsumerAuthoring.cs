using BovineLabs.Core.Authoring.EntityCommands;
using BovineLabs.Timeline.PlayerInputs.Data;
using Unity.Entities;
using UnityEngine;

namespace BovineLabs.Timeline.PlayerInputs.Authoring
{
    public class InputConsumerAuthoring : MonoBehaviour
    {
        public byte PlayerId;

        public bool Controllable;
        public OverrideTrigger OverrideTrigger = OverrideTrigger.AnyInput;
        public float ReleaseIdleSeconds = 0.25f;

        public class Baker : Baker<InputConsumerAuthoring>
        {
            public override void Bake(InputConsumerAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);
                var commands = new BakerCommands(this, entity);
                InputConsumerBuilder.Build(
                    ref commands,
                    authoring.PlayerId,
                    authoring.Controllable,
                    authoring.OverrideTrigger,
                    authoring.ReleaseIdleSeconds);
            }
        }
    }
}
