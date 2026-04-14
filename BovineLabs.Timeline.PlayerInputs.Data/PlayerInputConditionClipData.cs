using BovineLabs.Reaction.Data.Conditions;
using Unity.Entities;

namespace BovineLabs.Timeline.PlayerInputs.Data
{
    public struct PlayerInputConditionBuffer : IBufferElementData
    {
        public ConditionKey ConditionKey;
        public int Value;
    }
}