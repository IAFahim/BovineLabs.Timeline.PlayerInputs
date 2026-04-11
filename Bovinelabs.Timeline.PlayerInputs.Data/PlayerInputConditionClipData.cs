using BovineLabs.Reaction.Data.Conditions;
using Unity.Entities;

namespace BovineLabs.Timeline.Tracks.Data.PlayerInputs
{
    public struct PlayerInputConditionBuffer : IBufferElementData
    {
        public ConditionKey ConditionKey;
        public int Value;
    }
}
