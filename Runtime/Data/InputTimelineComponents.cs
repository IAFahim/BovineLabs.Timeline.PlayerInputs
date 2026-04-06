using BovineLabs.Reaction.Data.Conditions;
using Unity.Entities;

namespace PlayerInputs.Data
{
    public enum InputPressType : byte
    {
        Down,
        Held,
        Up
    }

    public struct InputAllowedComponent : IComponentData
    {
        public byte ActionID;
        public InputPressType PressType;
        public ConditionKey EventKey;
    }

    public struct InputRecordComponent : IComponentData
    {
    }

    public struct ComboBlob
    {
        public BlobArray<byte> Sequence;
    }

    public struct InputConsumeComponent : IComponentData
    {
        public BlobAssetReference<ComboBlob> Combo;
        public ConditionKey EventKey;
        public bool ClearOnConsume;
    }

    public struct InputClearComponent : IComponentData
    {
    }
}
