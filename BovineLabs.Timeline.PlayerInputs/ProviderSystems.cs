using BovineLabs.Timeline.PlayerInputs.Data;
using Unity.Entities;

namespace BovineLabs.Timeline.PlayerInputs
{
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation |
                       WorldSystemFilterFlags.ServerSimulation)]
    public partial class ProviderSyncSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            foreach (var (state, axes, bridge) in SystemAPI
                         .Query<RefRW<InputState>, DynamicBuffer<InputAxis>, PlayerInputBridgeComponent>()
                         .WithAll<ProviderTag>())
            {
                if (bridge.Value == null) continue;

                state.ValueRW = new InputState
                {
                    Down = bridge.Value.CurrentDown,
                    Held = bridge.Value.CurrentHeld,
                    Up = bridge.Value.CurrentUp
                };

                axes.Clear();
                foreach (var axis in bridge.Value.CurrentAxes) axes.Add(axis);
            }
        }
    }
}