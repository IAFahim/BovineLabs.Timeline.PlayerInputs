using BovineLabs.Timeline.PlayerInputs.Data;
using NUnit.Framework;
using Unity.Mathematics;

namespace BovineLabs.Timeline.PlayerInputs.Tests
{
    public class DirectionMathTests
    {
        [Test]
        public void Quantise_InsideDeadZone_IsNeutral()
        {
            Assert.AreEqual(Direction.Neutral, DirectionMath.Quantise(new float2(0.1f, 0.1f), 0.3f, 1));
            Assert.AreEqual(Direction.Neutral, DirectionMath.Quantise(float2.zero, 0.3f, 1));
        }

        [Test]
        public void Quantise_CardinalsMapAsExpected()
        {
            Assert.AreEqual(Direction.Forward, DirectionMath.Quantise(new float2(1f, 0f), 0.2f, 1));
            Assert.AreEqual(Direction.Back, DirectionMath.Quantise(new float2(-1f, 0f), 0.2f, 1));
            Assert.AreEqual(Direction.Up, DirectionMath.Quantise(new float2(0f, 1f), 0.2f, 1));
            Assert.AreEqual(Direction.Down, DirectionMath.Quantise(new float2(0f, -1f), 0.2f, 1));
        }

        [Test]
        public void Quantise_DiagonalsMapAsExpected()
        {
            Assert.AreEqual(Direction.DownForward, DirectionMath.Quantise(new float2(0.7f, -0.7f), 0.2f, 1));
            Assert.AreEqual(Direction.UpBack, DirectionMath.Quantise(new float2(-0.7f, 0.7f), 0.2f, 1));
        }

        [Test]
        public void Quantise_FacingFlipsBackForward()
        {
            Assert.AreEqual(Direction.Back, DirectionMath.Quantise(new float2(1f, 0f), 0.2f, -1));
            Assert.AreEqual(Direction.Forward, DirectionMath.Quantise(new float2(-1f, 0f), 0.2f, -1));

            Assert.AreEqual(Direction.Up, DirectionMath.Quantise(new float2(0f, 1f), 0.2f, -1));
        }

        [Test]
        public void Quantise_IsTotal_NeverThrows_AcrossUnitCircle()
        {
            for (var deg = 0; deg < 360; deg++)
            {
                var r = math.radians(deg);
                var v = new float2(math.cos(r), math.sin(r));
                var d = DirectionMath.Quantise(v, 0.2f, 1);
                Assert.AreNotEqual(Direction.Neutral, d, $"angle {deg} should resolve to a direction");
            }
        }
    }
}