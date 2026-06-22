using BovineLabs.Timeline.PlayerInputs.Data;
using NUnit.Framework;
using Unity.Mathematics;
using Unity.Transforms;

namespace BovineLabs.Timeline.PlayerInputs.Tests
{
    public class AxisParentWorldTests
    {
        private static bool Near(float3 a, float3 b, float e = 1e-3f)
        {
            return math.lengthsq(a - b) < e * e;
        }

        [Test]
        public void LocalTransformPath_ReturnsPoseAndScale()
        {
            var lt = new LocalTransform
            {
                Position = new float3(1f, 2f, 3f),
                Rotation = quaternion.Euler(0f, math.radians(45f), 0f),
                Scale = 2f,
            };

            var ok = AxisParentWorld.TryDecompose(true, lt, false, false, default, out var pos, out var rot,
                out var scale);

            Assert.IsTrue(ok);
            Assert.IsTrue(Near(pos, lt.Position));
            Assert.AreEqual(0f, math.length(rot.value - lt.Rotation.value), 1e-4f);
            Assert.AreEqual(2f, scale, 1e-4f);
        }

        [Test]
        public void LocalTransformPath_TinyScaleFallsBackToOne()
        {
            var lt = new LocalTransform { Position = float3.zero, Rotation = quaternion.identity, Scale = 1e-7f };

            AxisParentWorld.TryDecompose(true, lt, false, false, default, out _, out _, out var scale);

            Assert.AreEqual(1f, scale, 1e-6f, "|scale| <= 1e-6 falls back to 1");
        }

        [Test]
        public void LocalToWorldPath_DerivesUniformScaleFromColumn()
        {
            var trs = float4x4.TRS(new float3(4f, 0f, 0f), quaternion.Euler(0f, math.radians(90f), 0f),
                new float3(3f, 3f, 3f));
            var ltw = new LocalToWorld { Value = trs };

            var ok = AxisParentWorld.TryDecompose(true, default, true, true, ltw, out var pos, out _, out var scale);

            Assert.IsTrue(ok);
            Assert.IsTrue(Near(pos, ltw.Position));
            Assert.AreEqual(3f, scale, 1e-3f, "uniform scale from |c0.xyz|");
        }

        [Test]
        public void LocalToWorldPath_TinyScaleFallsBackToOne()
        {
            var ltw = new LocalToWorld { Value = float4x4.zero };

            AxisParentWorld.TryDecompose(false, default, false, true, ltw, out _, out _, out var scale);

            Assert.AreEqual(1f, scale, 1e-6f, "|c0.xyz| <= 1e-6 falls back to 1");
        }

        [Test]
        public void NeitherComponentPresent_ReturnsFalse()
        {
            var ok = AxisParentWorld.TryDecompose(false, default, false, false, default, out var pos, out var rot,
                out var scale);

            Assert.IsFalse(ok);
            Assert.IsTrue(Near(pos, float3.zero));
            Assert.AreEqual(0f, math.length(rot.value - quaternion.identity.value), 1e-6f);
            Assert.AreEqual(1f, scale, 1e-6f);
        }
    }
}
