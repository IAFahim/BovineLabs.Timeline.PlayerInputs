using BovineLabs.Core.Groups;
using PlayerInputs.Data;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PlayerInputs.Systems
{
    [UpdateInGroup(typeof(BeginSimulationSystemGroup))]
    public partial class PlayerInputPollSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            foreach (var (bridgeComp, downs, helds, ups, axes) in SystemAPI.Query<
                PlayerInputBridgeComponent,
                DynamicBuffer<InputButtonDownBuffer>,
                DynamicBuffer<InputButtonHeldBuffer>,
                DynamicBuffer<InputButtonUpBuffer>,
                DynamicBuffer<InputAxisBuffer>>().WithAll<InputProviderTag>())
            {
                var bridge = bridgeComp.Value;
                if (bridge == null) continue;

                downs.Clear();
                helds.Clear();
                ups.Clear();
                axes.Clear();

                foreach (var btn in bridge.Buttons)
                {
                    if (btn.Action.WasPressedThisFrame())
                        downs.Add(new InputButtonDownBuffer { ActionId = btn.Id });
                    if (btn.Action.IsInProgress())
                        helds.Add(new InputButtonHeldBuffer { ActionId = btn.Id });
                    if (btn.Action.WasReleasedThisFrame())
                        ups.Add(new InputButtonUpBuffer { ActionId = btn.Id });
                }

                foreach (var axis in bridge.Axes)
                {
                    float2 val = float2.zero;
                    if (axis.Action.expectedControlType == "Vector2")
                        val = axis.Action.ReadValue<Vector2>();
                    else if (axis.Action.expectedControlType == "Axis" ||
                             axis.Action.expectedControlType == "Button")
                        val.x = axis.Action.ReadValue<float>();

                    if (math.lengthsq(val) > 0.0001f)
                    {
                        axes.Add(new InputAxisBuffer { ActionId = axis.Id, Value = val });
                    }
                }
            }
        }
    }
}
