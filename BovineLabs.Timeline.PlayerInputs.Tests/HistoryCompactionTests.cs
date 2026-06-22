using BovineLabs.Core.Collections;
using BovineLabs.Testing;
using BovineLabs.Timeline.PlayerInputs.Data;
using NUnit.Framework;
using Unity.Entities;

namespace BovineLabs.Timeline.PlayerInputs.Tests
{
    public class HistoryCompactionTests : ECSTestsFixture
    {
        [Test]
        public void ByPosition_RemovesMaskedSlots_KeepsOrder()
        {
            var history = History((10, 0), (11, 1), (12, 2), (13, 3));
            var mask = default(BitArray256);
            mask[1] = true;
            mask[3] = true;

            HistoryCompaction.Compact(history, ref mask, CompactMode.ByPosition);

            Assert.AreEqual(2, history.Length);
            Assert.AreEqual(10, history[0].ActionId);
            Assert.AreEqual(12, history[1].ActionId);
        }

        [Test]
        public void ByPosition_NoneMasked_Unchanged()
        {
            var history = History((10, 0), (11, 1), (12, 2));
            var mask = default(BitArray256);

            HistoryCompaction.Compact(history, ref mask, CompactMode.ByPosition);

            Assert.AreEqual(3, history.Length);
            Assert.AreEqual(10, history[0].ActionId);
            Assert.AreEqual(11, history[1].ActionId);
            Assert.AreEqual(12, history[2].ActionId);
        }

        [Test]
        public void ByPosition_AllMasked_LengthZero()
        {
            var history = History((10, 0), (11, 1), (12, 2));
            var mask = default(BitArray256);
            mask[0] = true;
            mask[1] = true;
            mask[2] = true;

            HistoryCompaction.Compact(history, ref mask, CompactMode.ByPosition);

            Assert.AreEqual(0, history.Length);
        }

        [Test]
        public void ByActionId_RemovesEntriesWhoseActionBitSet()
        {
            var history = History((5, 0), (6, 1), (5, 2), (7, 3));
            var mask = default(BitArray256);
            mask[5] = true;

            HistoryCompaction.Compact(history, ref mask, CompactMode.ByActionId);

            Assert.AreEqual(2, history.Length);
            Assert.AreEqual(6, history[0].ActionId);
            Assert.AreEqual(7, history[1].ActionId);
        }

        [Test]
        public void ByActionId_NoneMatch_Unchanged()
        {
            var history = History((5, 0), (6, 1));
            var mask = default(BitArray256);
            mask[9] = true;

            HistoryCompaction.Compact(history, ref mask, CompactMode.ByActionId);

            Assert.AreEqual(2, history.Length);
            Assert.AreEqual(5, history[0].ActionId);
            Assert.AreEqual(6, history[1].ActionId);
        }

        [Test]
        public void EmptyBuffer_IsNoOp()
        {
            var history = History();
            var byPos = default(BitArray256);
            HistoryCompaction.Compact(history, ref byPos, CompactMode.ByPosition);
            Assert.AreEqual(0, history.Length);

            var byId = default(BitArray256);
            byId[3] = true;
            HistoryCompaction.Compact(history, ref byId, CompactMode.ByActionId);
            Assert.AreEqual(0, history.Length);
        }

        private DynamicBuffer<InputHistory> History(params (byte action, uint tick)[] entries)
        {
            var entity = Manager.CreateEntity(typeof(InputHistory));
            var buffer = Manager.GetBuffer<InputHistory>(entity);
            foreach (var (action, tick) in entries)
                buffer.Add(new InputHistory { ActionId = action, Phase = InputPhase.Down, Tick = tick });
            return buffer;
        }
    }
}
