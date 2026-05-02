using BovineLabs.Timeline.PlayerInputs.Data;
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
                
                AddComponent(entity, new PlayerId { Value = authoring.PlayerId });
                AddComponent<ConsumerTag>(entity);
                AddComponent<InputState>(entity);
                AddComponent<ActiveBufferMask>(entity);
                AddComponent(entity, new InputSource { Provider = Entity.Null });
                AddComponent<PlayerMoveInput>(entity);
                AddBuffer<InputHistory>(entity);
                AddBuffer<InputAxis>(entity);
            }
        }
    }
}