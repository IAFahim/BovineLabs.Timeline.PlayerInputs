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

                // Accumulate-and-drain: pull the edges the bridge accumulated across every render frame since the last
                // sim tick (see PlayerInputBridge.Drain). This is the ONLY drain site - the bridge-backed provider is
                // created in the default world only, so exactly one ProviderSyncSystem instance ever consumes it.
                bridge.Value.Drain(out var down, out var up, out var held);

                state.ValueRW = new InputState
                {
                    Down = down,
                    Held = held,
                    Up = up
                };

                axes.Clear();
                foreach (var axis in bridge.Value.CurrentAxes) axes.Add(axis);
            }
        }
    }
}