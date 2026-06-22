using NUnit.Framework;
using Unity.Collections;

namespace BovineLabs.Timeline.PlayerInputs.Tests
{
    public class EventAccumulationTests
    {
        [Test]
        public void TryMerge_SameKey_SumsAmount_Merged()
        {
            var values = new FixedList4096Bytes<EventAmount>();
            values.Add(new EventAmount(7, 3));

            var result = EventAccumulation.TryMerge(ref values, new EventAmount(7, 4));

            Assert.AreEqual(MergeResult.Merged, result);
            Assert.AreEqual(1, values.Length);
            Assert.AreEqual(7, values[0].Amount);
        }

        [Test]
        public void TryMerge_NewKeyWithRoom_Appended()
        {
            var values = new FixedList4096Bytes<EventAmount>();
            values.Add(new EventAmount(7, 3));

            var result = EventAccumulation.TryMerge(ref values, new EventAmount(8, 5));

            Assert.AreEqual(MergeResult.Appended, result);
            Assert.AreEqual(2, values.Length);
            Assert.AreEqual(5, values[1].Amount);
        }

        [Test]
        public void TryMerge_AtCapacity_NewKey_Overflow()
        {
            var values = new FixedList4096Bytes<EventAmount>();
            var key = 0;
            while (values.Length < values.Capacity)
                values.Add(new EventAmount(++key, 1));

            var result = EventAccumulation.TryMerge(ref values, new EventAmount(key + 1, 9));

            Assert.AreEqual(MergeResult.Overflow, result);
            Assert.AreEqual(values.Capacity, values.Length);
        }

        [Test]
        public void TryMerge_AtCapacity_ExistingKey_StillMerges()
        {
            var values = new FixedList4096Bytes<EventAmount>();
            var key = 0;
            while (values.Length < values.Capacity)
                values.Add(new EventAmount(++key, 1));

            var result = EventAccumulation.TryMerge(ref values, new EventAmount(1, 10));

            Assert.AreEqual(MergeResult.Merged, result);
            Assert.AreEqual(11, values[0].Amount);
        }
    }
}
