using BovineLabs.Reaction.Data.Conditions;
using Unity.Entities;

namespace Bovinelabs.Timeline.PlayerInputs.Data
{
    public struct PlayerInputConditionBuffer : IBufferElementData
    {
        public ConditionKey ConditionKey;
        public int Value;
    }
}