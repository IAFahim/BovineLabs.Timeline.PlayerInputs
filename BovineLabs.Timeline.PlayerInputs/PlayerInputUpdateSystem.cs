using BovineLabs.Timeline.PlayerInputs.Data;
using Unity.Entities;

namespace BovineLabs.Timeline.PlayerInputs
{
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial class PlayerInputUpdateSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            foreach (var (state, axes, bridge) in SystemAPI
                         .Query<RefRW<InputState>, DynamicBuffer<InputAxisBuffer>, PlayerInputBridgeComponent>())
            {
                if (bridge.Value == null) continue;

                var currentHeld = bridge.Value.CurrentHeld;
                var previousHeld = state.ValueRO.Held;

                state.ValueRW = new InputState
                {
                    Down = currentHeld.BitAnd(previousHeld.BitNot()),
                    Held = currentHeld,
                    Up = previousHeld.BitAnd(currentHeld.BitNot())
                };

                axes.Clear();
                foreach (var axis in bridge.Value.CurrentAxes)
                    axes.Add(axis);
            }
        }
    }
}