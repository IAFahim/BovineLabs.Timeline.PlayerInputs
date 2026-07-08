using BovineLabs.Timeline.PlayerInputs.Data;
using Unity.Burst;
using Unity.Collections;
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
        private ComponentLookup<PlayerOverride> _overrides;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<InputRegistry>();
            state.RequireForUpdate<SimulationTick>();
            state.RequireForUpdate<DirectionConfig>();
            _states = state.GetComponentLookup<InputState>(true);
            _axes = state.GetBufferLookup<InputAxis>(true);
            _overrides = state.GetComponentLookup<PlayerOverride>(true);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            _states.Update(ref state);
            _axes.Update(ref state);
            _overrides.Update(ref state);

            state.Dependency = new QuantiseJob
            {
                Slots = SystemAPI.GetSingletonBuffer<ProviderSlot>(true),
                Axes = _axes,
                Overrides = _overrides,
                Tick = SystemAPI.GetSingleton<SimulationTick>().Value
            }.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(ConsumerTag))]
        private partial struct QuantiseJob : IJobEntity
        {
            [ReadOnly] public DynamicBuffer<ProviderSlot> Slots;

            [ReadOnly] public BufferLookup<InputAxis> Axes;

            [ReadOnly] public ComponentLookup<PlayerOverride> Overrides;

            public uint Tick;

            private void Execute(Entity entity, in PlayerId id, in DirectionConfig config, ref DirectionState dir)
            {
                var resolved = Direction.Neutral;

                if (InputAccess.TryGetAxes(Slots, Axes, Overrides, entity, id.Value, out var buffer))
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