using BovineLabs.Testing;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;

namespace BovineLabs.Timeline.PlayerInputs.Tests
{
    // Pins the cross-clip CommandSequence arbitration order: lower Priority evaluates first (first crack at
    // consuming shared history); equal Priority breaks on the clip entity so the order is deterministic per run.
    public class SequencePriorityTests : ECSTestsFixture
    {
        [Test]
        public void LowerPriority_SortsFirst_RegardlessOfEntityOrder()
        {
            var early = Manager.CreateEntity(); // lower Entity.Index (created first)
            var late = Manager.CreateEntity();

            var keys = new NativeList<ClipSortKey>(2, Allocator.Temp);
            keys.Add(new ClipSortKey { Priority = 5, Clip = early });
            keys.Add(new ClipSortKey { Priority = -1, Clip = late });
            keys.Sort();

            Assert.AreEqual(late, keys[0].Clip, "priority -1 wins over 5 even though its entity is higher");
            Assert.AreEqual(early, keys[1].Clip);
            keys.Dispose();
        }

        [Test]
        public void EqualPriority_BreaksOnEntity_Stable()
        {
            var first = Manager.CreateEntity();
            var second = Manager.CreateEntity();

            var keys = new NativeList<ClipSortKey>(2, Allocator.Temp);
            // Add in reverse entity order to prove the sort - not insertion order - decides it.
            keys.Add(new ClipSortKey { Priority = 0, Clip = second });
            keys.Add(new ClipSortKey { Priority = 0, Clip = first });
            keys.Sort();

            Assert.AreEqual(first, keys[0].Clip, "equal priority -> lower entity first");
            Assert.AreEqual(second, keys[1].Clip);
            keys.Dispose();
        }

        [Test]
        public void CompareTo_PriorityDominatesEntity()
        {
            var lowEntity = Manager.CreateEntity();
            var highEntity = Manager.CreateEntity();

            var highPriorityLowEntity = new ClipSortKey { Priority = 10, Clip = lowEntity };
            var lowPriorityHighEntity = new ClipSortKey { Priority = 0, Clip = highEntity };

            Assert.Less(lowPriorityHighEntity.CompareTo(highPriorityLowEntity), 0,
                "priority 0 orders before priority 10 despite the higher entity");
        }
    }
}
