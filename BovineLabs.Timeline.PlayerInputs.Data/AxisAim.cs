using Unity.Mathematics;

namespace BovineLabs.Timeline.PlayerInputs.Data
{
    public static class AxisAim
    {
        // Intersect a cursor world ray with the aim plane (through bodyPos, normal planeNormal) to get the aim point.
        // Returns false when the ray is parallel to the plane or points away from it (hit behind the camera).
        public static bool TryProjectCursorToPlane(float3 rayOrigin, float3 rayDir, float3 bodyPos, float3 planeNormal,
            out float3 aimPoint)
        {
            aimPoint = bodyPos;
            var denom = math.dot(rayDir, planeNormal);
            if (math.abs(denom) < 1e-6f) return false;

            var t = math.dot(bodyPos - rayOrigin, planeNormal) / denom;
            if (t <= 0f) return false;

            aimPoint = rayOrigin + (rayDir * t);
            return true;
        }

        public static void ComputeAim(float3 inputVec, bool hasInput, float3 planeNormal, float smoothing, float dt,
            float aimRadius, float lateralOffset, bool rotateInPlace, bool hasAimed, bool parented,
            quaternion parentRot, float parentScale, quaternion heldWorldRot, out quaternion newHeldWorldRot,
            out bool newHasAimed, out bool wroteLocalPos, out float3 localPos)
        {
            newHeldWorldRot = heldWorldRot;
            newHasAimed = hasAimed;
            wroteLocalPos = false;
            localPos = float3.zero;

            if (hasInput)
            {
                var worldDesired = quaternion.LookRotationSafe(math.normalize(inputVec), planeNormal);
                var lerpT = smoothing <= 0.0001f ? 1f : 1f - math.exp(-smoothing * dt);
                newHeldWorldRot = math.slerp(heldWorldRot, worldDesired, lerpT);
                newHasAimed = true;
            }

            if (!newHasAimed)
            {
                return;
            }

            // Radial = slide along the aim ("sphere at the arrow tip"); suppressed by RotateInPlace (decals/turrets).
            // Lateral = fixed sideways shift perpendicular to the aim, so two clips at +/-X make parallel lines.
            var radial = !rotateInPlace && aimRadius > 0.0001f ? aimRadius : 0f;
            var hasLateral = math.abs(lateralOffset) > 0.0001f;
            if (radial == 0f && !hasLateral)
            {
                return;
            }

            var worldOffset = (math.mul(newHeldWorldRot, math.forward()) * radial) +
                              (math.mul(newHeldWorldRot, math.right()) * lateralOffset);
            localPos = parented ? math.rotate(math.inverse(parentRot), worldOffset) / parentScale : worldOffset;
            wroteLocalPos = true;
        }
    }
}
