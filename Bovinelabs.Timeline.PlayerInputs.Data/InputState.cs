using Unity.Entities;

namespace PlayerInputs.Data
{
    public struct InputState : IComponentData
    {
        public InputBitmask Down;
        public InputBitmask Held;
        public InputBitmask Up;
    }

    public struct InputBitmask
    {
        public ulong Chunk0;
        public ulong Chunk1;
        public ulong Chunk2;
        public ulong Chunk3;

        public void Set(byte id)
        {
            var chunk = id >> 6;
            var bit = id & 63;
            if (chunk == 0) this.Chunk0 |= 1ul << bit;
            else if (chunk == 1) this.Chunk1 |= 1ul << bit;
            else if (chunk == 2) this.Chunk2 |= 1ul << bit;
            else this.Chunk3 |= 1ul << bit;
        }

        public readonly bool Has(byte id)
        {
            var chunk = id >> 6;
            var bit = id & 63;
            return chunk switch
            {
                0 => (this.Chunk0 & (1ul << bit)) != 0,
                1 => (this.Chunk1 & (1ul << bit)) != 0,
                2 => (this.Chunk2 & (1ul << bit)) != 0,
                3 => (this.Chunk3 & (1ul << bit)) != 0,
                _ => false
            };
        }

        public readonly bool ContainsAll(InputBitmask mask)
        {
            return (this.Chunk0 & mask.Chunk0) == mask.Chunk0 &&
                   (this.Chunk1 & mask.Chunk1) == mask.Chunk1 &&
                   (this.Chunk2 & mask.Chunk2) == mask.Chunk2 &&
                   (this.Chunk3 & mask.Chunk3) == mask.Chunk3;
        }
    }
}