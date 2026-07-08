using BovineLabs.Timeline.PlayerInputs.Data;
using NUnit.Framework;

namespace BovineLabs.Timeline.PlayerInputs.Tests
{
    public class HistoryMathTests
    {
        [Test]
        public void EvictCount_EmptyBufferWithIncomingEntries_RemovesNothing()
        {
            Assert.AreEqual(0, HistoryMath.EvictCount(0, 1, HistoryMath.DefaultLimit));
            Assert.AreEqual(0, HistoryMath.EvictCount(0, 512, 1));
        }

        [Test]
        public void EvictCount_NeverExceedsExistingLength()
        {
            Assert.AreEqual(3, HistoryMath.EvictCount(3, 512, 64));
            Assert.AreEqual(0, HistoryMath.EvictCount(0, 0, 1));
        }

        [Test]
        public void EvictCount_AtLimit_EvictsExactlyTheOverhang()
        {
            Assert.AreEqual(1, HistoryMath.EvictCount(64, 1, 64));
            Assert.AreEqual(0, HistoryMath.EvictCount(63, 1, 64));
            Assert.AreEqual(2, HistoryMath.EvictCount(64, 2, 64));
        }

        [Test]
        public void OverflowCount_TrimsBackDownToLimit()
        {
            Assert.AreEqual(0, HistoryMath.OverflowCount(64, 64));
            Assert.AreEqual(36, HistoryMath.OverflowCount(100, 64));
            Assert.AreEqual(0, HistoryMath.OverflowCount(0, 64));
        }

        [Test]
        public void ClampLimit_IsTotalOverAllInputs()
        {
            Assert.AreEqual(1, HistoryMath.ClampLimit(0));
            Assert.AreEqual(1, HistoryMath.ClampLimit(-5));
            Assert.AreEqual(64, HistoryMath.ClampLimit(64));
            Assert.AreEqual(256, HistoryMath.ClampLimit(9999));
        }

        [Test]
        public void Invariant_LengthAfterFrameNeverExceedsLimit()
        {
            for (var length = 0; length <= 300; length += 7)
            for (var toAdd = 0; toAdd <= 512; toAdd += 13)
            for (var limit = 1; limit <= 256; limit *= 4)
            {
                var afterEvict = length - HistoryMath.EvictCount(length, toAdd, limit);
                Assert.GreaterOrEqual(afterEvict, 0);

                var afterAppend = afterEvict + toAdd;
                var final = afterAppend - HistoryMath.OverflowCount(afterAppend, limit);
                Assert.LessOrEqual(final, limit,
                    $"length {length} toAdd {toAdd} limit {limit}");
            }
        }
    }
}