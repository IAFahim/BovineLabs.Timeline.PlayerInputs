using BovineLabs.Reaction.Data.Conditions;
using Unity.Entities;

namespace BovineLabs.Timeline.Tracks.Data.PlayerInputs
{
    public enum PlayerInputType : byte
    {
        Attack,
        Interact,
        Crouch,
        Jump,
        Previous,
        Next,
        Sprint,
        Move,
        Look
    }

    public struct PlayerInputConditionValue : IComponentData
    {
        public PlayerInputType PlayerInputType;
        public ConditionKey ConditionKey;
    }
}
