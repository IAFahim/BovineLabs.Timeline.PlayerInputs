using BovineLabs.Core.Collections;
using BovineLabs.Timeline.PlayerInputs.Data;
using NUnit.Framework;

namespace BovineLabs.Timeline.PlayerInputs.Tests
{
    /// <summary>
    /// Pins the accumulate-and-drain cadence contract (bridge render-rate publish vs ECS sim-tick consume) on the pure
    /// <see cref="EdgeDrain"/> helper, so it can be verified without a live PlayerInput / play mode.
    /// </summary>
    public class BridgeDrainTests
    {
        private static BitArray256 Bit(byte id)
        {
            var b = default(BitArray256);
            b[id] = true;
            return b;
        }

        [Test]
        public void Drain_TakesAndClears()
        {
            BitArray256 pd = default, pu = default;
            EdgeDrain.Accumulate(ref pd, ref pu, Bit(5), default);

            EdgeDrain.Drain(ref pd, ref pu, out var down, out var up);
            Assert.IsTrue(down[5]);
            Assert.IsTrue(up.AllFalse);

            // A second drain with no new accumulate must be empty (edges are one-shot).
            EdgeDrain.Drain(ref pd, ref pu, out var down2, out var up2);
            Assert.IsTrue(down2.AllFalse);
            Assert.IsTrue(up2.AllFalse);
        }

        [Test]
        public void ZeroConsumeFrame_TapBetweenTicksSurvives()
        {
            // Render frame A: tap 5 (down+up), no sim tick consumes it. Render frame B: nothing.
            // The single drain that eventually runs must still see the tap - it was not overwritten.
            BitArray256 pd = default, pu = default;
            EdgeDrain.Accumulate(ref pd, ref pu, Bit(5), Bit(5)); // frame A: press+release
            EdgeDrain.Accumulate(ref pd, ref pu, default, default); // frame B: idle

            EdgeDrain.Drain(ref pd, ref pu, out var down, out var up);
            Assert.IsTrue(down[5], "the tap's Down must survive a 0-consume frame");
            Assert.IsTrue(up[5], "the tap's Up must survive a 0-consume frame");
        }

        [Test]
        public void TwoConsumeFrame_PressSeenExactlyOnce()
        {
            // Render frame publishes one press; two sim ticks drain before the next publish.
            BitArray256 pd = default, pu = default;
            EdgeDrain.Accumulate(ref pd, ref pu, Bit(7), default);

            EdgeDrain.Drain(ref pd, ref pu, out var d1, out _);
            EdgeDrain.Drain(ref pd, ref pu, out var d2, out _);

            Assert.IsTrue(d1[7], "first sim tick sees the press");
            Assert.IsFalse(d2[7], "second sim tick must NOT re-see the same press");
        }

        [Test]
        public void AccumulateAcrossFrames_OrsEdges()
        {
            BitArray256 pd = default, pu = default;
            EdgeDrain.Accumulate(ref pd, ref pu, Bit(1), default);
            EdgeDrain.Accumulate(ref pd, ref pu, Bit(2), Bit(1));

            EdgeDrain.Drain(ref pd, ref pu, out var down, out var up);
            Assert.IsTrue(down[1]);
            Assert.IsTrue(down[2]);
            Assert.IsTrue(up[1]);
        }
    }
}
