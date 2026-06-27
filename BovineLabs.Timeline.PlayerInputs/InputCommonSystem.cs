using BovineLabs.Bridge.Data.Camera;
using BovineLabs.Timeline.PlayerInputs.Data;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BovineLabs.Timeline.PlayerInputs
{
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation)]
    public partial class InputCommonSystem : SystemBase
    {
        protected override void OnCreate()
        {
            var entity = EntityManager.CreateEntity(typeof(InputCommon));
            EntityManager.SetName(entity, "InputCommon");
        }

        protected override void OnUpdate()
        {
            var common = new InputCommon
            {
                ApplicationFocus = Application.isFocused,
                ScreenSize = new int2(Screen.width, Screen.height),
            };

            var mouse = Mouse.current;
            if (mouse != null)
            {
                common.HasMouse = true;
                common.CursorScreenPoint = mouse.position.ReadValue();
            }
            else
            {
                common.CursorScreenPoint = new float2(-1f, -1f);
            }

            common.CursorViewPoint = common.ScreenSize.x > 0 && common.ScreenSize.y > 0
                ? common.CursorScreenPoint / (float2)common.ScreenSize
                : float2.zero;
            common.CursorInViewPort = InViewPort(common.CursorViewPoint);

            foreach (var bridge in SystemAPI.Query<RefRO<CameraBridge>>().WithAll<CameraMain>())
            {
                var camera = bridge.ValueRO.Value.Value;
                if (camera == null)
                {
                    continue;
                }

                common.HasCamera = true;
                if (common.HasMouse && common.CursorInViewPort)
                {
                    var ray = camera.ScreenPointToRay(
                        new Vector3(common.CursorScreenPoint.x, common.CursorScreenPoint.y, 0f));
                    common.CameraRay = new CameraRay { Origin = ray.origin, Direction = ray.direction };
                }

                break;
            }

            SystemAPI.SetSingleton(common);
        }

        private static bool InViewPort(float2 point)
        {
            const float eps = 0.0001f;
            return point.x >= -eps && point.x <= 1f + eps && point.y >= -eps && point.y <= 1f + eps;
        }
    }
}
