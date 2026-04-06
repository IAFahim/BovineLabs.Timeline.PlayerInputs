using PlayerInputs.Data;
using Unity.Entities;
using UnityEngine;

namespace PlayerInputs.Authoring
{
    public class PlayerInputAuthoring : MonoBehaviour
    {
        public byte PlayerID;

        public class Baker : Baker<PlayerInputAuthoring>
        {
            public override void Bake(PlayerInputAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);

                AddComponent(entity, new ECSPlayerInputID { ID = authoring.PlayerID });

                // Add the gameplay buffers
                AddBuffer<InputButtonDownBuffer>(entity);
                AddBuffer<InputButtonHeldBuffer>(entity);
                AddBuffer<InputButtonUpBuffer>(entity);
                AddBuffer<InputAxisBuffer>(entity);
            }
        }
    }
}