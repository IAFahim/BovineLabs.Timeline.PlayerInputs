using BovineLabs.Timeline.PlayerInputs.Data;
using NUnit.Framework;
using Unity.Mathematics;

namespace BovineLabs.Timeline.PlayerInputs.Tests
{
    public class AxisAimTests
    {
        private static readonly float3 Up = new(0, 1, 0);

        [Test]
        public void SmoothingZero_SnapsToDesired()
        {
            var input = new float3(0f, 0f, 1f);
            AxisAim.ComputeAim(input, true, Up, 0f, 0.016f, 0f, false, false, quaternion.identity, 1f,
                quaternion.Euler(0f, math.radians(180f), 0f), out var newRot, out _, out _, out _);

            var desired = quaternion.LookRotationSafe(math.normalize(input), Up);
            Assert.AreEqual(0f, math.length(newRot.value - desired.value), 1e-3f, "snaps fully to desired");
        }

        [Test]
        public void SmoothingPositive_LerpTMatchesExactFormula()
        {
            const float smoothing = 5f;
            const float dt = 0.02f;
            var expectedT = 1f - math.exp(-smoothing * dt);

            var from = quaternion.identity;
            var input = new float3(1f, 0f, 0f);
            AxisAim.ComputeAim(input, true, Up, smoothing, dt, 0f, false, false, quaternion.identity, 1f, from,
                out var newRot, out _, out _, out _);

            var desired = quaternion.LookRotationSafe(math.normalize(input), Up);
            var expected = math.slerp(from, desired, expectedT);
            Assert.AreEqual(0f, math.length(newRot.value - expected.value), 1e-4f, "lerpT == 1 - exp(-s*dt)");
        }

        [Test]
        public void NoInput_HeldRotationPersists()
        {
            var held = quaternion.Euler(0f, math.radians(45f), 0f);
            AxisAim.ComputeAim(float3.zero, false, Up, 5f, 0.02f, 0f, true, false, quaternion.identity, 1f, held,
                out var newRot, out var newHasAimed, out _, out _);

            Assert.AreEqual(0f, math.length(newRot.value - held.value), 1e-4f, "held rotation unchanged");
            Assert.IsTrue(newHasAimed, "hasAimed preserved");
        }

        [Test]
        public void AimRadius_WithHasAimed_WritesForwardOffset()
        {
            var held = quaternion.Euler(0f, math.radians(90f), 0f);
            AxisAim.ComputeAim(float3.zero, false, Up, 0f, 0.016f, 2f, true, false, quaternion.identity, 1f, held,
                out var newRot, out _, out var wrote, out var localPos);

            Assert.IsTrue(wrote, "writes a position offset");
            var expected = math.mul(newRot, math.forward()) * 2f;
            Assert.AreEqual(0f, math.length(localPos - expected), 1e-3f, "offset = held-forward * radius");
        }

        [Test]
        public void AimRadius_WithoutHasAimed_DoesNotWritePosition()
        {
            AxisAim.ComputeAim(float3.zero, false, Up, 0f, 0.016f, 2f, false, false, quaternion.identity, 1f,
                quaternion.identity, out _, out _, out var wrote, out _);

            Assert.IsFalse(wrote, "no position write before first aim");
        }

        [Test]
        public void InputParallelToPlaneNormal_FallsBackWithoutThrow()
        {
            var input = Up;
            Assert.DoesNotThrow(() =>
                AxisAim.ComputeAim(input, true, Up, 0f, 0.016f, 0f, false, false, quaternion.identity, 1f,
                    quaternion.identity, out var newRot, out _, out _, out _));
        }
    }
}
