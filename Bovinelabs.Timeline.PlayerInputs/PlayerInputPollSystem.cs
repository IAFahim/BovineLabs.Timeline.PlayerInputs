using BovineLabs.Core.Groups;
using PlayerInputs.Data;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.InputSystem;

namespace PlayerInputs.Systems
{
    [UpdateInGroup(typeof(BeginSimulationSystemGroup))]
    public partial class PlayerInputPollSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            foreach (var (bridgeComp, state, axes) in SystemAPI.Query<PlayerInputBridgeComponent, RefRW<InputState>, DynamicBuffer<InputAxisBuffer>>().WithAll<InputProviderTag>())
            {
                var bridge = bridgeComp.Value;
                if (bridge == null)
                {
                    continue;
                }

                var down = new InputBitmask();
                var held = new InputBitmask();
                var up = new InputBitmask();

                foreach (var btn in bridge.Buttons)
                {
                    if (btn.Action.WasPressedThisFrame()) down.Set(btn.Id);
                    if (btn.Action.IsInProgress()) held.Set(btn.Id);
                    if (btn.Action.WasReleasedThisFrame()) up.Set(btn.Id);
                }

                state.ValueRW = new InputState
                {
                    Down = down,
                    Held = held,
                    Up = up
                };

                axes.Clear();

                foreach (var axis in bridge.Axes)
                {
                    var val = float2.zero;
                    
                    if (axis.Action.expectedControlType == "Vector2")
                    {
                        val = axis.Action.ReadValue<UnityEngine.Vector2>();
                    }
                    else if (axis.Action.expectedControlType == "Axis" || axis.Action.expectedControlType == "Button")
                    {
                        val.x = axis.Action.ReadValue<float>();
                    }

                    if (math.lengthsq(val) > 0.0001f)
                    {
                        axes.Add(new InputAxisBuffer { ActionId = axis.Id, Value = val });
                    }
                }
            }
        }
    }
}