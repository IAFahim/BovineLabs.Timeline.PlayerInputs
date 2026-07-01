using Unity.Burst;
using Unity.Mathematics;

namespace BovineLabs.Timeline.PlayerInputs.Flow.Data
{
    /// <summary>
    /// Pure, Burst-friendly math for NavFlowInputSystem, extracted so the lead-direction + leash logic is unit-testable
    /// in isolation (NavFlowInputMathTests). The only non-pure part — reading the proxy/player transforms and driving
    /// the Traverse agent — stays in the system.
    /// </summary>
    [BurstCompile]
    public static class NavFlowInputMath
    {
        /// <summary>
        /// Direction from the player toward the proxy lead-point on the XZ ground plane, normalised to a stick vector
        /// (float2(x, z)). Returns zero (implicit dead-zone) when the proxy sits on the player. <paramref name="held"/>
        /// is true when the proxy has out-run the player past <paramref name="leashRadius"/> — the caller then pauses
        /// the proxy so the player can close the gap. Direction is still returned when held (the player keeps moving
        /// toward the paused proxy).
        /// </summary>
        public static float2 LeadDirection(float2 proxyXZ, float2 playerXZ, float leashRadius, out bool held)
        {
            var delta = proxyXZ - playerXZ;
            held = leashRadius > 0f && math.lengthsq(delta) > leashRadius * leashRadius;
            return math.normalizesafe(delta);
        }
    }
}
