using BovineLabs.Timeline.PlayerInputs.Data;
using NUnit.Framework;
using Unity.Mathematics;

namespace BovineLabs.Timeline.PlayerInputs.Tests
{
    public class AxisEdgeTests
    {
        private const float Dz = AxisEdge.DefaultDeadzone; // 0.125

        [Test]
        public void RestingStickDrift_DoesNotActuate()
        {
            // 5% drift is well inside a 12.5% deadzone and must NOT synthesise an edge.
            Assert.IsFalse(AxisEdge.Actuated(new float2(0.05f, 0f), false, Dz));
            Assert.IsFalse(AxisEdge.Actuated(new float2(0.03f, 0.04f), false, Dz));
        }

        [Test]
        public void PushPastDeadzone_Actuates()
        {
            Assert.IsTrue(AxisEdge.Actuated(new float2(0.2f, 0f), false, Dz));
            Assert.IsTrue(AxisEdge.Actuated(new float2(0f, 1f), false, Dz));
        }

        [Test]
        public void ExactlyAtDeadzone_DoesNotActuateFromRest()
        {
            // Press band is strict '>' so sitting exactly on the deadzone is not a press.
            Assert.IsFalse(AxisEdge.Actuated(new float2(Dz, 0f), false, Dz));
        }

        [Test]
        public void Hysteresis_StaysActuatedInTheBand()
        {
            // Once actuated, a value inside the (lower) release band keeps it actuated - no chatter.
            var inBand = new float2(Dz * 0.95f, 0f); // between release band (0.9*Dz) and press band (Dz)
            Assert.IsTrue(AxisEdge.Actuated(inBand, true, Dz), "must latch on inside the hysteresis band");
            Assert.IsFalse(AxisEdge.Actuated(inBand, false, Dz), "but must not fresh-press from inside the band");
        }

        [Test]
        public void Hysteresis_ReleasesBelowLowerBand()
        {
            var belowRelease = new float2(Dz * 0.5f, 0f);
            Assert.IsFalse(AxisEdge.Actuated(belowRelease, true, Dz));
        }

        [Test]
        public void NoChatter_HoveringOnDeadzone()
        {
            // Simulate a stick hovering right at the deadzone boundary across frames: at most one transition.
            var wasActuated = false;
            var transitions = 0;
            var samples = new[] { 0.124f, 0.126f, 0.123f, 0.127f, 0.122f, 0.128f };
            foreach (var s in samples)
            {
                var now = AxisEdge.Actuated(new float2(s, 0f), wasActuated, Dz);
                if (now != wasActuated) transitions++;
                wasActuated = now;
            }

            // Without hysteresis this would toggle on every sample; with it, one press and then it holds.
            Assert.LessOrEqual(transitions, 1);
        }

        [Test]
        public void ZeroDeadzone_ActuatesOnAnyNonZero()
        {
            Assert.IsTrue(AxisEdge.Actuated(new float2(0.0001f, 0f), false, 0f));
            Assert.IsFalse(AxisEdge.Actuated(float2.zero, false, 0f));
        }

        [Test]
        public void NegativeDeadzone_ClampedToZero_DoesNotThrow()
        {
            Assert.IsTrue(AxisEdge.Actuated(new float2(0.0001f, 0f), false, -1f));
        }
    }
}
