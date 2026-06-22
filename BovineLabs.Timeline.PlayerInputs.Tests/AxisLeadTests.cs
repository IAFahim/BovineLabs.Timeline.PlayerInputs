using BovineLabs.Timeline.PlayerInputs.Data;
using NUnit.Framework;
using Unity.Mathematics;

namespace BovineLabs.Timeline.PlayerInputs.Tests
{
    public class AxisLeadTests
    {
        private static bool Near(float3 a, float3 b, float e = 1e-3f)
        {
            return math.lengthsq(a - b) < e * e;
        }

        [Test]
        public void Unparented_Move_SetsLocalToInputTimesRange()
        {
            var input = new float3(0.5f, 0f, 0.25f);
            AxisLead.ComputeMove(input, 4f, 0f, false, false, float3.zero, quaternion.identity, 1f, float3.zero,
                out var localPos, out var newHeld);

            Assert.IsTrue(Near(localPos, input * 4f), "local = input * range");
            Assert.IsTrue(Near(newHeld, input * 4f), "held tracks world lead");
        }

        [Test]
        public void Leash_ClampsOffsetLengthToLeashRadius()
        {
            var input = new float3(1f, 0f, 0f);
            AxisLead.ComputeMove(input, 10f, 3f, false, false, float3.zero, quaternion.identity, 1f, float3.zero,
                out var localPos, out var newHeld);

            Assert.AreEqual(3f, math.length(localPos), 1e-3f, "clamped to leash radius");
            Assert.AreEqual(3f, math.length(newHeld), 1e-3f, "held clamped too");
        }

        [Test]
        public void Released_KeepLead_PinsToHeldWorldPosition_OnBody()
        {
            var held = new float3(2f, 0f, -1f);
            AxisLead.ComputeMove(float3.zero, 4f, 0f, true, false, float3.zero, quaternion.identity, 1f, held,
                out var localPos, out var newHeld);

            Assert.IsTrue(Near(localPos, held), "pins to held world point");
            Assert.IsTrue(Near(newHeld, held), "held unchanged when released");
        }

        [Test]
        public void Released_Default_SnapsLocalToZero()
        {
            var held = new float3(2f, 0f, -1f);
            AxisLead.ComputeMove(float3.zero, 4f, 0f, false, false, float3.zero, quaternion.identity, 1f, held,
                out var localPos, out var newHeld);

            Assert.IsTrue(Near(localPos, float3.zero), "snaps back to body");
            Assert.IsTrue(Near(newHeld, held), "held unchanged when released");
        }

        [Test]
        public void Parented_InverseRotateScale_RoundTrips()
        {
            var parentRot = quaternion.Euler(0f, math.radians(90f), 0f);
            var parentPos = new float3(5f, 0f, 0f);
            var parentScale = 2f;
            var input = new float3(1f, 0f, 0f);

            AxisLead.ComputeMove(input, 3f, 0f, false, true, parentPos, parentRot, parentScale, float3.zero,
                out var localPos, out var newHeld);

            var worldOffset = input * 3f;
            var world = parentPos + math.rotate(parentRot, localPos * parentScale);
            Assert.IsTrue(Near(world, parentPos + worldOffset), "parent-local round-trips to world offset");
            Assert.IsTrue(Near(newHeld, parentPos + math.rotate(parentRot, worldOffset)), "held is world lead");
        }

        [Test]
        public void Live_Move_NewHeldTracksLeadPoint()
        {
            var input = new float3(0f, 0f, 1f);
            AxisLead.ComputeMove(input, 5f, 0f, false, false, float3.zero, quaternion.identity, 1f,
                new float3(99f, 99f, 99f), out _, out var newHeld);

            Assert.IsTrue(Near(newHeld, input * 5f), "live input overwrites held with current lead");
        }
    }
}
