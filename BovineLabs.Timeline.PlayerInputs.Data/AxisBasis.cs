using Unity.Mathematics;

namespace BovineLabs.Timeline.PlayerInputs.Data
{
    // Ground-plane basis (forward/right) an axis input is projected onto. Pure math, no ECS, so it is unit
    // testable (see AxisBasisTests) and Burst-callable from AxisTransformSystem. CameraRelative derives the
    // basis from the camera's world rotation projected onto the plane; otherwise a fixed world basis is used.
    public static class AxisBasis
    {
        public static void ComputePlaneBasis(float3 planeNormal, bool cameraRelative, quaternion cameraRotation,
            out float3 forward, out float3 right)
        {
            if (cameraRelative)
            {
                var camForward = math.mul(cameraRotation, new float3(0, 0, 1));
                var camRight = math.mul(cameraRotation, new float3(1, 0, 0));

                var projForward = camForward - math.dot(camForward, planeNormal) * planeNormal;
                var projRight = camRight - math.dot(camRight, planeNormal) * planeNormal;

                // A camera looking along the plane normal (e.g. top-down with an up-plane) collapses the projected
                // forward to zero; derive the missing axis from the surviving one, falling back to a world basis
                // only when both degenerate.
                if (math.lengthsq(projForward) > 1e-6f)
                {
                    forward = math.normalize(projForward);
                    right = math.lengthsq(projRight) > 1e-6f
                        ? math.normalize(projRight)
                        : math.normalize(math.cross(planeNormal, forward));
                }
                else if (math.lengthsq(projRight) > 1e-6f)
                {
                    right = math.normalize(projRight);
                    forward = math.normalize(math.cross(right, planeNormal));
                }
                else
                {
                    WorldBasis(planeNormal, out forward, out right);
                }
            }
            else
            {
                WorldBasis(planeNormal, out forward, out right);
            }
        }

        public static void WorldBasis(float3 planeNormal, out float3 forward, out float3 right)
        {
            if (math.abs(math.dot(planeNormal, new float3(0, 1, 0))) > 0.99f)
            {
                forward = new float3(0, 0, 1);
                right = new float3(1, 0, 0);
            }
            else
            {
                right = math.normalize(math.cross(new float3(0, 1, 0), planeNormal));
                forward = math.cross(planeNormal, right);
            }
        }
    }
}
