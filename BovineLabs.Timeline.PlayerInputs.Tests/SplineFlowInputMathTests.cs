using BovineLabs.Timeline.PlayerInputs.Flow.Data;
using BovineLabs.Timeline.Physics;
using NUnit.Framework;
using Unity.Mathematics;

namespace BovineLabs.Timeline.PlayerInputs.Tests
{
    public class SplineFlowInputMathTests
    {
        // --- Delta: progress increment per frame ----------------------------------------------------------------

        [Test]
        public void Delta_ConstantSpeed_IsSpeedTimesDtOverLength()
        {
            var d = SplineFlowInputMath.Delta(SplineTraversal.ConstantSpeed, 10f, 999f, 0.1f, 5f);
            Assert.AreEqual(10f * 0.1f / 5f, d, 1e-5f);
        }

        [Test]
        public void Delta_OverDuration_IsDtOverSeconds_IgnoresSpeed()
        {
            var d = SplineFlowInputMath.Delta(SplineTraversal.OverDuration, 999f, 4f, 0.1f, 123f);
            Assert.AreEqual(0.1f / 4f, d, 1e-5f);
        }

        [Test]
        public void Delta_GuardsZeroLengthAndSeconds()
        {
            Assert.IsFalse(float.IsInfinity(SplineFlowInputMath.Delta(SplineTraversal.ConstantSpeed, 10f, 4f, 0.1f, 0f)));
            Assert.IsFalse(float.IsInfinity(SplineFlowInputMath.Delta(SplineTraversal.OverDuration, 10f, 0f, 0.1f, 5f)));
        }

        // --- Sign / reverse --------------------------------------------------------------------------------------

        [Test]
        public void Sign_ForwardIsPositive_ReverseIsNegative()
        {
            Assert.AreEqual(1f, SplineFlowInputMath.Sign(1));
            Assert.AreEqual(1f, SplineFlowInputMath.Sign(0));
            Assert.AreEqual(-1f, SplineFlowInputMath.Sign(-1));
        }

        // --- Sample: lead direction + tangent sign ---------------------------------------------------------------

        [Test]
        public void Sample_Forward_LeadsAhead_TangentForward()
        {
            SplineFlowInputMath.Sample(0.2f, 0.05f, 1f, SplineWrap.Clamp, out var t, out var sign);
            Assert.AreEqual(0.25f, t, 1e-5f, "lead adds in forward travel");
            Assert.AreEqual(1f, sign);
        }

        [Test]
        public void Sample_Reverse_LeadsBackwards_TangentInverted()
        {
            SplineFlowInputMath.Sample(0.5f, 0.05f, -1f, SplineWrap.Clamp, out var t, out var sign);
            Assert.AreEqual(0.45f, t, 1e-5f, "lead subtracts when reversed (looks ahead in travel dir)");
            Assert.AreEqual(-1f, sign, "tangent points backward along the path");
        }

        // --- Sample: PingPong reflection (the bug the adversarial review caught) ----------------------------------

        [Test]
        public void Sample_PingPong_ForwardPhase_KeepsSign()
        {
            // progress in [0,1): forward leg
            SplineFlowInputMath.Sample(0.3f, 0f, 1f, SplineWrap.PingPong, out _, out var sign);
            Assert.AreEqual(1f, sign);
        }

        [Test]
        public void Sample_PingPong_ReflectionPhase_InvertsSign()
        {
            // progress in [1,2): the bounce-back leg — tangent must flip
            SplineFlowInputMath.Sample(1.3f, 0f, 1f, SplineWrap.PingPong, out var t, out var sign);
            Assert.AreEqual(-1f, sign, "dt/dProgress = -1 on the reflection leg");
            // SplineWrapEval folds 1.3 -> 0.7 (1 - |1 - 1.3|)
            Assert.AreEqual(0.7f, t, 1e-5f);
        }

        [Test]
        public void Sample_PingPong_SecondForwardPhase_RestoresSign()
        {
            // progress in [2,3): forward again
            SplineFlowInputMath.Sample(2.4f, 0f, 1f, SplineWrap.PingPong, out _, out var sign);
            Assert.AreEqual(1f, sign);
        }

        [Test]
        public void Sample_PingPong_Reverse_CompoundsWithReflection()
        {
            // reversed clip on the reflection leg: clip Direction (-1) AND reflection (-1) -> +1
            SplineFlowInputMath.Sample(1.3f, 0f, -1f, SplineWrap.PingPong, out _, out var sign);
            Assert.AreEqual(1f, sign);
        }

        // --- Project: XZ ground plane ----------------------------------------------------------------------------

        [Test]
        public void Project_DropsYAndNormalises()
        {
            var dir = SplineFlowInputMath.Project(new float3(3f, 99f, 4f), 1f);
            Assert.AreEqual(0.6f, dir.x, 1e-4f, "x/|xz|");
            Assert.AreEqual(0.8f, dir.y, 1e-4f, "z/|xz| -> stick y");
            Assert.AreEqual(1f, math.length(dir), 1e-4f);
        }

        [Test]
        public void Project_AppliesTangentSign()
        {
            var fwd = SplineFlowInputMath.Project(new float3(1f, 0f, 0f), 1f);
            var rev = SplineFlowInputMath.Project(new float3(1f, 0f, 0f), -1f);
            Assert.IsTrue(math.all(fwd == -rev), "sign flips the stick direction");
        }

        [Test]
        public void Project_ZeroTangent_IsZero_NoNaN()
        {
            var dir = SplineFlowInputMath.Project(float3.zero, 1f);
            Assert.IsTrue(math.all(dir == float2.zero));
            Assert.IsFalse(math.any(math.isnan(dir)));
        }
    }
}
