using Unity.Mathematics;

namespace BovineLabs.Timeline.PlayerInputs.Data
{
    public static class AxisAim
    {
        public static void ComputeAim(float3 inputVec, bool hasInput, float3 planeNormal, float smoothing, float dt,
            float aimRadius, bool hasAimed, bool parented, quaternion parentRot, float parentScale,
            quaternion heldWorldRot, out quaternion newHeldWorldRot, out bool newHasAimed, out bool wroteLocalPos,
            out float3 localPos)
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

            if (aimRadius > 0.0001f && newHasAimed)
            {
                var worldOffset = math.mul(newHeldWorldRot, math.forward()) * aimRadius;
                localPos = parented ? math.rotate(math.inverse(parentRot), worldOffset) / parentScale : worldOffset;
                wroteLocalPos = true;
            }
        }
    }
}
