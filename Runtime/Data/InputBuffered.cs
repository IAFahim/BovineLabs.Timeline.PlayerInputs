using Unity.Entities;
using Unity.Mathematics;

namespace PlayerInputs.Data
{
    // Utility to ensure identical hashing between Authoring and Runtime
    public static class InputUtility
    {
        public static int GetActionID(string actionName)
        {
            return new Unity.Collections.FixedString32Bytes(actionName).GetHashCode();
        }
    }

    [InternalBufferCapacity(8)]
    public struct InputButtonDownBuffer : IBufferElementData
    {
        public int ActionID;
    }

    [InternalBufferCapacity(8)]
    public struct InputButtonHeldBuffer : IBufferElementData
    {
        public int ActionID;
    }

    [InternalBufferCapacity(8)]
    public struct InputButtonUpBuffer : IBufferElementData
    {
        public int ActionID;
    }

    [InternalBufferCapacity(4)]
    public struct InputAxisBuffer : IBufferElementData
    {
        public int ActionID;
        public float2 Value;
    }
}