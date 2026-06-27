using Unity.Entities;
using Unity.Mathematics;

namespace BovineLabs.Timeline.PlayerInputs.Data
{
    public struct InputCommon : IComponentData
    {
        public int2 ScreenSize;
        public float2 CursorScreenPoint;
        public float2 CursorViewPoint;
        public bool CursorInViewPort;
        public bool ApplicationFocus;
        public bool HasMouse;
        public bool HasCamera;
        public CameraRay CameraRay;

        public bool InViewWithFocus => CursorInViewPort && ApplicationFocus;
    }
}
