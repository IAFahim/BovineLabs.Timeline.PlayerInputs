using BovineLabs.Timeline.PlayerInputs.Data;
using NUnit.Framework;

namespace BovineLabs.Timeline.PlayerInputs.Tests
{
    public class EdgeAccumulatorTests
    {
        [Test]
        public void Press_SetsDownAndHeld()
        {
            var acc = new EdgeAccumulator();
            acc.Press(5);
            acc.Publish(out var down, out var up, out var held);

            Assert.IsTrue(down[5]);
            Assert.IsTrue(held[5]);
            Assert.IsFalse(up[5]);
        }

        [Test]
        public void Publish_ConsumesDownUp_KeepsHeld()
        {
            var acc = new EdgeAccumulator();
            acc.Press(5);
            acc.Publish(out _, out _, out _);

            acc.Publish(out var down, out var up, out var held);
            Assert.IsFalse(down[5], "Down is a one-frame edge and must be consumed");
            Assert.IsFalse(up[5]);
            Assert.IsTrue(held[5], "Held is latched level-state and must survive across frames");
        }

        [Test]
        public void Release_SetsUp_ClearsHeld()
        {
            var acc = new EdgeAccumulator();
            acc.Press(5);
            acc.Publish(out _, out _, out _);

            acc.Release(5);
            acc.Publish(out var down, out var up, out var held);
            Assert.IsFalse(down[5]);
            Assert.IsTrue(up[5]);
            Assert.IsFalse(held[5]);
        }

        [Test]
        public void PressAndReleaseSameFrame_BothEdgesSurface()
        {
            var acc = new EdgeAccumulator();
            acc.Press(5);
            acc.Release(5);
            acc.Publish(out var down, out var up, out var held);

            Assert.IsTrue(down[5]);
            Assert.IsTrue(up[5]);
            Assert.IsFalse(held[5]);
        }

        [Test]
        public void Seed_LatchesHeldWithoutDownEdge()
        {
            var acc = new EdgeAccumulator();
            acc.Seed(6);
            acc.Publish(out var down, out var up, out var held);

            Assert.IsTrue(held[6]);
            Assert.IsFalse(down[6]);
            Assert.IsFalse(up[6]);
        }

        [Test]
        public void IndependentIds_DoNotInterfere()
        {
            var acc = new EdgeAccumulator();
            acc.Press(5);
            acc.Release(200);
            acc.Publish(out var down, out var up, out var held);

            Assert.IsTrue(down[5]);
            Assert.IsTrue(held[5]);
            Assert.IsTrue(up[200]);
            Assert.IsFalse(held[200]);
            Assert.IsFalse(down[200]);
        }

        [Test]
        public void IsPressed_ReflectsLatchedHold_ForAxisReconcile()
        {
            var acc = new EdgeAccumulator();
            Assert.IsFalse(acc.IsPressed(5));
            acc.Press(5);
            Assert.IsTrue(acc.IsPressed(5));
            acc.Release(5);
            Assert.IsFalse(acc.IsPressed(5));
        }

        [Test]
        public void Prime_SnapshotsHeld_WithoutConsumingEdges()
        {
            var acc = new EdgeAccumulator();
            acc.Press(5);
            acc.Prime(out var held);
            Assert.IsTrue(held[5]);

            acc.Publish(out var down, out _, out var held2);
            Assert.IsTrue(down[5], "Prime must not consume the pending Down edge");
            Assert.IsTrue(held2[5]);
        }

        [Test]
        public void Seed_ThenPrime_ShowsHeldBeforeAnyPublish()
        {
            var acc = new EdgeAccumulator();
            acc.Seed(7);
            acc.Prime(out var held);
            Assert.IsTrue(held[7]);
        }

        [Test]
        public void Reset_ClearsEverything()
        {
            var acc = new EdgeAccumulator();
            acc.Press(5);
            acc.Reset();
            acc.Publish(out var down, out var up, out var held);

            Assert.IsTrue(down.AllFalse);
            Assert.IsTrue(up.AllFalse);
            Assert.IsTrue(held.AllFalse);
        }
    }
}