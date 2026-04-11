using BovineLabs.Reaction.Data.Conditions;
using Unity.Entities;

namespace Bovinelabs.Timeline.PlayerInputs.Data
{
    public enum InputPhase : byte
    {
        Down = 0,
        Held = 1,
        Up = 2
    }

    [InternalBufferCapacity(16)]
    public struct InputToConditionEvent : IBufferElementData
    {
        public byte ActionId;
        public InputPhase Phase;
        public ConditionKey Condition;
        public int Value;
    }
}