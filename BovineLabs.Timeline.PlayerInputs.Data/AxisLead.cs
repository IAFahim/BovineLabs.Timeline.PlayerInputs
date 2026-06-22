using Unity.Mathematics;

namespace BovineLabs.Timeline.PlayerInputs.Data
{
    public static class AxisLead
    {
        public static void ComputeMove(float3 inputVec, float range, float leashRadius, bool keepLead, bool parented,
            float3 parentPos, quaternion parentRot, float parentScale, float3 heldWorldPos, out float3 localPos,
            out float3 newHeldWorldPos)
        {
            newHeldWorldPos = heldWorldPos;

            if (math.lengthsq(inputVec) > 0.0001f)
            {
                var worldOffset = inputVec * range;
                if (leashRadius > 0f)
                {
                    var len = math.length(worldOffset);
                    if (len > leashRadius)
                        worldOffset *= leashRadius / len;
                }

                localPos = parented ? math.rotate(math.inverse(parentRot), worldOffset) / parentScale : worldOffset;
                newHeldWorldPos = parented ? parentPos + math.rotate(parentRot, worldOffset) : worldOffset;
                return;
            }

            if (keepLead)
            {
                localPos = parented
                    ? math.rotate(math.inverse(parentRot), heldWorldPos - parentPos) / parentScale
                    : heldWorldPos;
                return;
            }

            localPos = float3.zero;
        }
    }
}
