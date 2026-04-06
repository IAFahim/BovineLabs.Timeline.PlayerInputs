using PlayerInputs;
using PlayerInputs.Data;
using Unity.Entities;

namespace PlayerInputs.Authoring
{
    public class PlayerInputBridgeAuthoring : Baker<PlayerInputBridge>
    {
        public override void Bake(PlayerInputBridge authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent(entity, new PlayerId { Value = authoring.GetPlayerId() });
            AddComponent<InputProviderTag>(entity);

            AddComponentObject(entity, new PlayerInputBridgeComponent { Value = authoring });

            AddBuffer<InputButtonDownBuffer>(entity);
            AddBuffer<InputButtonHeldBuffer>(entity);
            AddBuffer<InputButtonUpBuffer>(entity);
            AddBuffer<InputAxisBuffer>(entity);
        }
    }
}
