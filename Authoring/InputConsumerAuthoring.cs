// Authoring/InputConsumerAuthoring.cs
using PlayerInputs.Data;
using Unity.Entities;
using UnityEngine;

namespace PlayerInputs.Authoring
{
    public class InputConsumerAuthoring : MonoBehaviour
    {
        public byte PlayerId;

        public class Baker : Baker<InputConsumerAuthoring>
        {
            public override void Bake(InputConsumerAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);

                AddComponent(entity, new PlayerId { Value = authoring.PlayerId });
                AddComponent<InputConsumerTag>(entity);
                AddComponent(entity, new InputSource { Provider = Entity.Null });
                
                // DELETED: Buffer additions. Consumers just hold the reference.
            }
        }
    }
}