using BovineLabs.Timeline.PlayerInputs.Flow.Data;
using NUnit.Framework;
using Unity.Mathematics;

namespace BovineLabs.Timeline.PlayerInputs.Tests
{
    public class NavFlowInputMathTests
    {
        [Test]
        public void LeadDirection_PointsFromPlayerToProxy_Normalised()
        {
            var dir = NavFlowInputMath.LeadDirection(new float2(3f, 4f), float2.zero, 0f, out _);
            Assert.AreEqual(0.6f, dir.x, 1e-4f);
            Assert.AreEqual(0.8f, dir.y, 1e-4f);
            Assert.AreEqual(1f, math.length(dir), 1e-4f);
        }

        [Test]
        public void LeadDirection_ProxyOnPlayer_IsZero_NoNaN()
        {
            var dir = NavFlowInputMath.LeadDirection(new float2(2f, 2f), new float2(2f, 2f), 4f, out _);
            Assert.IsTrue(math.all(dir == float2.zero));
            Assert.IsFalse(math.any(math.isnan(dir)));
        }

        [Test]
        public void LeadDirection_WithinLeash_NotHeld()
        {
            NavFlowInputMath.LeadDirection(new float2(3f, 4f), float2.zero, 10f, out var held); // dist 5 < 10
            Assert.IsFalse(held);
        }

        [Test]
        public void LeadDirection_BeyondLeash_Held()
        {
            NavFlowInputMath.LeadDirection(new float2(3f, 4f), float2.zero, 4f, out var held); // dist 5 > 4
            Assert.IsTrue(held);
        }

        [Test]
        public void LeadDirection_ZeroLeash_NeverHeld()
        {
            NavFlowInputMath.LeadDirection(new float2(300f, 400f), float2.zero, 0f, out var held);
            Assert.IsFalse(held, "leash 0 disables the leash entirely");
        }

        [Test]
        public void LeadDirection_Held_StillReturnsDirection()
        {
            // Held pauses the proxy but the player must keep moving toward it, so dir is still valid.
            var dir = NavFlowInputMath.LeadDirection(new float2(3f, 4f), float2.zero, 4f, out var held);
            Assert.IsTrue(held);
            Assert.AreEqual(1f, math.length(dir), 1e-4f);
        }
    }
}
