using BovineLabs.Timeline.PlayerInputs.Data;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace BovineLabs.Timeline.PlayerInputs
{
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial class ProviderSyncSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            foreach (var (state, axes, bridge) in SystemAPI.Query<RefRW<InputState>, DynamicBuffer<InputAxis>, PlayerInputBridgeComponent>().WithAll<ProviderTag>())
            {
                if (bridge.Value == null) continue;

                var current = bridge.Value.CurrentHeld;
                var previous = state.ValueRO.Held;

                state.ValueRW = new InputState
                {
                    Down = current.BitAnd(previous.BitNot()),
                    Held = current,
                    Up = previous.BitAnd(current.BitNot())
                };

                axes.Clear();
                foreach (var axis in bridge.Value.CurrentAxes) axes.Add(axis);
            }
        }
    }

    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [UpdateAfter(typeof(ProviderSyncSystem))]
    public partial struct ProviderLinkSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var map = new NativeHashMap<byte, Entity>(16, state.WorldUpdateAllocator);
            
            foreach (var (id, entity) in SystemAPI.Query<RefRO<PlayerId>>().WithAll<ProviderTag>().WithEntityAccess())
                map.TryAdd(id.ValueRO.Value, entity);

            state.Dependency = new AssignProviderJob { Map = map }.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(ConsumerTag))]
        private partial struct AssignProviderJob : IJobEntity
        {
            [ReadOnly] public NativeHashMap<byte, Entity> Map;

            private void Execute(in PlayerId id, ref InputSource source)
            {
                source.Provider = Map.TryGetValue(id.Value, out var provider) ? provider : Entity.Null;
            }
        }
    }
}
