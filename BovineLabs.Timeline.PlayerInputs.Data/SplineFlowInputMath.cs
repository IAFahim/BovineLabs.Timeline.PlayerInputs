using BovineLabs.Timeline.Physics;
using Unity.Burst;
using Unity.Mathematics;

namespace BovineLabs.Timeline.PlayerInputs.Flow.Data
{
    /// <summary>
    /// Pure, Burst-friendly math for SplineFlowInputSystem, extracted so the branchy direction/wrap/reflection logic is
    /// unit-testable in isolation (SplineFlowInputMathTests). The only non-pure part — sampling the spline blob — stays
    /// in the system.
    /// </summary>
    [BurstCompile]
    public static class SplineFlowInputMath
    {
        /// <summary> Per-frame progress increment (0..1 units along the path), before the direction sign. </summary>
        public static float Delta(SplineTraversal traversal, float speed, float traversalSeconds, float dt, float length)
        {
            return traversal == SplineTraversal.ConstantSpeed
                ? speed * dt / math.max(length, 1e-3f)
                : dt / math.max(traversalSeconds, 1e-3f);
        }

        /// <summary> +1 forward, -1 reverse. </summary>
        public static float Sign(sbyte direction)
        {
            return direction < 0 ? -1f : 1f;
        }

        /// <summary>
        /// Resolve the normalised spline param to sample and the sign to apply to its tangent. Lead looks ahead in the
        /// travel direction (so it subtracts when reversed). Under PingPong the traversal physically reverses in the
        /// reflection periods [1,2),[3,4),… (dt/dProgress = -1), so the tangent must invert there on top of the clip's
        /// own Direction.
        /// </summary>
        public static void Sample(float progress, float lead, float sign, SplineWrap wrap, out float t, out float tangentSign)
        {
            var sampleProgress = progress + (lead * sign);
            t = SplineWrapEval.Evaluate(sampleProgress, wrap);

            tangentSign = sign;
            if (wrap == SplineWrap.PingPong && math.abs(sampleProgress) % 2f >= 1f)
            {
                tangentSign = -tangentSign;
            }
        }

        /// <summary> Project a world-space spline tangent onto the XZ ground plane (with sign) and normalise to a stick. </summary>
        public static float2 Project(float3 tangent, float tangentSign)
        {
            return math.normalizesafe(new float2(tangent.x, tangent.z) * tangentSign);
        }
    }
}
