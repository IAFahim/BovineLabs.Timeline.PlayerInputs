using BovineLabs.Timeline.Data;
using BovineLabs.Timeline.PlayerInputs.Data;
using Unity.Burst;
using Unity.Entities;

namespace BovineLabs.Timeline.PlayerInputs
{
    // Destroys a leaving player's provider one tick AFTER PlayerInputBridge.OnDisable stamped it with a closing
    // release and tagged it ProviderRetiring. Ordered after CommandSequenceSystem (which itself runs after
    // ConsumerHistorySystem) so every combo reader has seen the closing Up - recorded into history and live-probed
    // - before the provider entity disappears. The provider keeps its ProviderTag + PlayerId this tick, so
    // InputRegistry still routes consumers to it; next tick it is gone and InputRegistrySystem fires the leave.
    // Note: a buffered Up step records the closing Up into history (it persists), but a live None Up step only
    // catches it if its recogniser clip is still ClipActive on this tick - the same "span or loop the clip" rule
    // that already governs an ordinary release.
    [UpdateInGroup(typeof(TimelineComponentAnimationGroup))]
    [UpdateAfter(typeof(CommandSequenceSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation |
                       WorldSystemFilterFlags.ServerSimulation)]
    public partial struct ProviderRetireSystem : ISystem
    {
        private EntityQuery retiring;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            this.retiring = SystemAPI.QueryBuilder().WithAll<ProviderRetiring>().Build();
            state.RequireForUpdate(this.retiring);
        }

        public void OnUpdate(ref SystemState state)
        {
            state.EntityManager.DestroyEntity(this.retiring);
        }
    }
}
