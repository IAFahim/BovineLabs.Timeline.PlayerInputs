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

                AddComponent(entity, new PlayerId { Value = authoring.PlayerId });
                AddComponent<ConsumerTag>(entity);
                AddComponent<ActiveBufferMask>(entity);
                AddBuffer<InputHistory>(entity);

                if (authoring.Controllable)
                {
                    AddComponent<Controllable>(entity);
                    AddComponent<PlayerOverride>(entity);
                    SetComponentEnabled<PlayerOverride>(entity, false);
                    AddComponent(entity, new OverridePolicy
                    {
                        Trigger = authoring.OverrideTrigger,
                        TriggerActionId = 0,
                        ReleaseIdleSeconds = authoring.ReleaseIdleSeconds,
                    });
                    AddComponent<OverrideState>(entity);
                }
            }
        }
    }
}
