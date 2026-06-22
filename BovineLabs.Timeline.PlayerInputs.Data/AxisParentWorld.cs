using Unity.Mathematics;
using Unity.Transforms;

namespace BovineLabs.Timeline.PlayerInputs.Data
{
    public static class AxisParentWorld
    {
        public static bool TryDecompose(bool hasLocalTransform, in LocalTransform localTransform, bool parentHasParent,
            bool hasLtw, in LocalToWorld ltw, out float3 position, out quaternion rotation, out float scale)
        {
            position = float3.zero;
            rotation = quaternion.identity;
            scale = 1f;

            if (hasLocalTransform && !parentHasParent)
            {
                position = localTransform.Position;
                rotation = localTransform.Rotation;
                scale = math.abs(localTransform.Scale) > 1e-6f ? localTransform.Scale : 1f;
                return true;
            }

            if (hasLtw)
            {
                position = ltw.Position;
                rotation = ltw.Rotation;
                var s = math.length(ltw.Value.c0.xyz);
                scale = s > 1e-6f ? s : 1f;
                return true;
            }

            return false;
        }
    }
}
