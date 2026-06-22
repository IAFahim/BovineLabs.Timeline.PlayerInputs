using BovineLabs.Timeline.PlayerInputs.Data;
using NUnit.Framework;
using Unity.Mathematics;

namespace BovineLabs.Timeline.PlayerInputs.Tests
{
    public class AxisBasisTests
    {
        private static readonly float3 Up = new(0, 1, 0);

        private static bool Near(float3 a, float3 b, float e = 1e-3f)
        {
            return math.lengthsq(a - b) < e * e;
        }

        [Test]
        public void CameraRelative_Yaw0_StickMapsToWorldAxes()
        {
            var cam = quaternion.Euler(math.radians(30f), 0f, 0f);
            AxisBasis.ComputePlaneBasis(Up, true, cam, out var forward, out var right);
            Assert.IsTrue(Near(forward, new float3(0, 0, 1)), "forward projects to +Z");
            Assert.IsTrue(Near(right, new float3(1, 0, 0)), "right is +X");
        }

        [Test]
        public void CameraRelative_Yaw90_HasCorrectHandedness()
        {
            var cam = quaternion.Euler(math.radians(20f), math.radians(90f), 0f);
            AxisBasis.ComputePlaneBasis(Up, true, cam, out var forward, out var right);
            Assert.IsTrue(Near(forward, new float3(1, 0, 0)), "forward projects to +X");
            Assert.IsTrue(Near(right, new float3(0, 0, -1)), "right is -Z (Unity left-handed screen-right)");
        }

        [Test]
        public void CameraRelative_Handedness_HoldsAcrossAllYaws()
        {
            for (var deg = 0; deg < 360; deg += 15)
            {
                var cam = quaternion.Euler(math.radians(25f), math.radians(deg), 0f);
                AxisBasis.ComputePlaneBasis(Up, true, cam, out var forward, out var right);

                Assert.IsTrue(Near(right, math.normalize(math.cross(Up, forward)), 2e-3f), $"handedness at yaw {deg}");
                Assert.Less(math.abs(math.dot(forward, Up)), 1e-3f, $"forward stays planar at yaw {deg}");
            }
        }

        [Test]
        public void CameraRelative_TopDown_StaysFiniteAndOrthonormal()
        {
            var cam = quaternion.Euler(math.radians(90f), 0f, 0f);
            AxisBasis.ComputePlaneBasis(Up, true, cam, out var forward, out var right);
            Assert.IsTrue(math.all(math.isfinite(forward)) && math.all(math.isfinite(right)), "no NaN");
            Assert.AreEqual(1f, math.length(forward), 1e-3f);
            Assert.AreEqual(1f, math.length(right), 1e-3f);
            Assert.Less(math.abs(math.dot(forward, right)), 1e-3f, "orthogonal");
        }

        [Test]
        public void TogglingCameraRelative_DoesNotInvertControls()
        {
            AxisBasis.ComputePlaneBasis(Up, false, quaternion.identity, out var wf, out var wr);
            AxisBasis.ComputePlaneBasis(Up, true, quaternion.identity, out var cf, out var cr);
            Assert.IsTrue(Near(wf, cf) && Near(wr, cr), "world basis matches identity-camera basis");
        }
    }
}