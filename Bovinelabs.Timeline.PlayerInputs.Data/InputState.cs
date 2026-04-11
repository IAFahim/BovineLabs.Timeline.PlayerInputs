using System;
using Unity.Entities;

namespace Bovinelabs.Timeline.PlayerInputs.Data
{
    public struct InputState : IComponentData
    {
        public InputBitmask Down;
        public InputBitmask Held;
        public InputBitmask Up;
    }

    public struct InputBitmask : IEquatable<InputBitmask>
    {
        public ulong Chunk0;
        public ulong Chunk1;
        public ulong Chunk2;
        public ulong Chunk3;

        public void Set(byte id)
        {
            var chunk = id >> 6;
            var bit = id & 63;
            if (chunk == 0) Chunk0 |= 1ul << bit;
            else if (chunk == 1) Chunk1 |= 1ul << bit;
            else if (chunk == 2) Chunk2 |= 1ul << bit;
            else Chunk3 |= 1ul << bit;
        }

        public readonly bool Has(byte id)
        {
            var chunk = id >> 6;
            var bit = id & 63;
            return chunk switch
            {
                0 => (Chunk0 & (1ul << bit)) != 0,
                1 => (Chunk1 & (1ul << bit)) != 0,
                2 => (Chunk2 & (1ul << bit)) != 0,
                3 => (Chunk3 & (1ul << bit)) != 0,
                _ => false
            };
        }

        public readonly InputBitmask AndNot(InputBitmask other)
        {
            return new InputBitmask
            {
                Chunk0 = Chunk0 & ~other.Chunk0,
                Chunk1 = Chunk1 & ~other.Chunk1,
                Chunk2 = Chunk2 & ~other.Chunk2,
                Chunk3 = Chunk3 & ~other.Chunk3
            };
        }

        public readonly bool ContainsAll(InputBitmask mask)
        {
            return (Chunk0 & mask.Chunk0) == mask.Chunk0 &&
                   (Chunk1 & mask.Chunk1) == mask.Chunk1 &&
                   (Chunk2 & mask.Chunk2) == mask.Chunk2 &&
                   (Chunk3 & mask.Chunk3) == mask.Chunk3;
        }

        public readonly bool Overlaps(InputBitmask mask)
        {
            return (Chunk0 & mask.Chunk0) != 0 ||
                   (Chunk1 & mask.Chunk1) != 0 ||
                   (Chunk2 & mask.Chunk2) != 0 ||
                   (Chunk3 & mask.Chunk3) != 0;
        }

        public bool Equals(InputBitmask other)
        {
            return Chunk0 == other.Chunk0 &&
                   Chunk1 == other.Chunk1 &&
                   Chunk2 == other.Chunk2 &&
                   Chunk3 == other.Chunk3;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = Chunk0.GetHashCode();
                hashCode = (hashCode * 397) ^ Chunk1.GetHashCode();
                hashCode = (hashCode * 397) ^ Chunk2.GetHashCode();
                hashCode = (hashCode * 397) ^ Chunk3.GetHashCode();
                return hashCode;
            }
        }
    }
}
