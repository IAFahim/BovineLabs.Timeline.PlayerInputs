using BovineLabs.Reaction.Data.Conditions;
using Unity.Entities;

namespace BovineLabs.Timeline.Tracks.Data.PlayerInputs
{
    public struct PlayerInputConditionValue : IComponentData
    {
        public byte RequiredActionId;
        public ConditionKey ConditionKey;
    }
}
