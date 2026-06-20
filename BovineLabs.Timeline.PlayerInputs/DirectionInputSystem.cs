using BovineLabs.Timeline.PlayerInputs.Data;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace BovineLabs.Timeline.PlayerInputs
{
    [UpdateInGroup(typeof(TimelineComponentAnimationGroup))]
    [UpdateAfter(typeof(ConsumerBufferMaskSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation |
                       WorldSystemFilterFlags.ServerSimulation)]
    public partial struct DirectionInputSystem : ISystem
    {
        private ComponentLookup<InputState> _states;
        private BufferLookup<InputAxis> _axes;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<InputRegistry>();
            state.RequireForUpdate<SimulationTick>();
            state.RequireForUpdate<DirectionConfig>();
            _states = state.GetComponentLookup<InputState>(true);
            _axes = state.GetBufferLookup<InputAxis>(true);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            _states.Update(ref state);
            _axes.Update(ref state);

            var registry = SystemAPI.GetSingleton<InputRegistry>();

            state.Dependency = new QuantiseJob
            {
                Registry = registry.ProviderByPlayer,
                Axes = _axes,
                Tick = SystemAPI.GetSingleton<SimulationTick>().Value
            }.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(ConsumerTag))]
        private partial struct QuantiseJob : IJobEntity
        {
            [ReadOnly] [NativeDisableContainerSafetyRestriction]
            public NativeArray<Entity> Registry;

            [ReadOnly] public BufferLookup<InputAxis> Axes;

            public uint Tick;

            private void Execute(in PlayerId id, in DirectionConfig config, ref DirectionState dir)
            {
                var resolved = Direction.Neutral;

                var provider = Registry[id.Value];
                if (provider != Entity.Null && Axes.TryGetBuffer(provider, out var buffer))
                {
                    var value = InputAccess.ReadAxis(buffer, config.ActionId);
                    resolved = DirectionMath.Quantise(value, config.DeadZone, config.Facing);
                }

                if (resolved != dir.Current)
                {
                    dir.Previous = dir.Current;
                    dir.Current = resolved;
                    dir.ChangedTick = Tick;
                }
            }
        }
    }
}
