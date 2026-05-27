using BovineLabs.Core.Utility;
using BovineLabs.Timeline.Data;
using BovineLabs.Timeline.PlayerInputs.Data;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace BovineLabs.Timeline.PlayerInputs
{
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [UpdateAfter(typeof(ProviderLinkSystem))]
    [Unity.Entities.WorldSystemFilter(Unity.Entities.WorldSystemFilterFlags.LocalSimulation | Unity.Entities.WorldSystemFilterFlags.ClientSimulation | Unity.Entities.WorldSystemFilterFlags.ServerSimulation)]
    public partial struct ConsumerSyncSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.Dependency = new SyncStateJob
            {
                Providers = SystemAPI.GetComponentLookup<InputState>(true),
                Axes = SystemAPI.GetBufferLookup<InputAxis>(true)
            }.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(ConsumerTag))]
        private partial struct SyncStateJob : IJobEntity
        {
            [ReadOnly] [NativeDisableContainerSafetyRestriction]
            public ComponentLookup<InputState> Providers;

            [ReadOnly] [NativeDisableContainerSafetyRestriction]
            public BufferLookup<InputAxis> Axes;

            private void Execute(in InputSource source, ref InputState state, ref PlayerMoveInput move,
                ref DynamicBuffer<InputAxis> consumerAxes)
            {
                if (source.Provider == Entity.Null || !Providers.TryGetComponent(source.Provider, out state))
                {
                    state = default;
                    move.Value = float2.zero;
                    consumerAxes.Clear();
                    return;
                }

                consumerAxes.Clear();
                if (Axes.TryGetBuffer(source.Provider, out var providerAxes))
                {
                    if (providerAxes.Length > 0) move.Value = providerAxes[0].Value;
                    else move.Value = float2.zero;

                    foreach (var axis in providerAxes) consumerAxes.Add(axis);
                }
            }
        }
    }

    [UpdateInGroup(typeof(TimelineComponentAnimationGroup))]
    [Unity.Entities.WorldSystemFilter(Unity.Entities.WorldSystemFilterFlags.LocalSimulation | Unity.Entities.WorldSystemFilterFlags.ClientSimulation | Unity.Entities.WorldSystemFilterFlags.ServerSimulation)]
    public partial struct ConsumerBufferMaskSystem : ISystem
    {
        private ComponentLookup<ActiveBufferMask> masks;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            masks = state.GetComponentLookup<ActiveBufferMask>();
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            masks.Update(ref state);

            state.Dependency = new ResetMaskJob().ScheduleParallel(state.Dependency);
            state.Dependency = new AccumulateMaskJob
            {
                Masks = masks
            }.Schedule(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(ConsumerTag))]
        private partial struct ResetMaskJob : IJobEntity
        {
            private void Execute(ref ActiveBufferMask mask)
            {
                mask.Value = default;
            }
        }

        [BurstCompile]
        [WithAll(typeof(ClipActive))]
        private partial struct AccumulateMaskJob : IJobEntity
        {
            [NativeDisableParallelForRestriction] public ComponentLookup<ActiveBufferMask> Masks;

            private void Execute(in BufferWindowConfig config, in TrackBinding binding)
            {
                if (binding.Value == Entity.Null) return;

                if (!Masks.TryGetComponent(binding.Value, out var mask)) return;
                mask.Value = mask.Value.BitOr(config.AllowedActions);
                Masks[binding.Value] = mask;
            }
        }
    }

    [UpdateInGroup(typeof(TimelineComponentAnimationGroup))]
    [UpdateAfter(typeof(ConsumerBufferMaskSystem))]
    [Unity.Entities.WorldSystemFilter(Unity.Entities.WorldSystemFilterFlags.LocalSimulation | Unity.Entities.WorldSystemFilterFlags.ClientSimulation | Unity.Entities.WorldSystemFilterFlags.ServerSimulation)]
    public partial struct ConsumerHistorySystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.Dependency = new RecordHistoryJob
            {
                Tick = (uint)(SystemAPI.Time.ElapsedTime * 1000.0)
            }.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(ConsumerTag))]
        private partial struct RecordHistoryJob : IJobEntity
        {
            public uint Tick;

            private void Execute(in InputState state, in ActiveBufferMask mask, ref DynamicBuffer<InputHistory> history)
            {
                if (mask.Value.AllFalse) return;

                var downFiltered = state.Down.BitAnd(mask.Value);
                var upFiltered = state.Up.BitAnd(mask.Value);

                var totalToAdd = downFiltered.CountBits() + upFiltered.CountBits();
                if (totalToAdd == 0) return;

                var removeCount = math.max(0, history.Length + totalToAdd - history.Capacity);
                if (removeCount > 0) history.RemoveRange(0, removeCount);

                ProcessULong(downFiltered.Data1, 0, InputPhase.Down, ref history, Tick);
                ProcessULong(downFiltered.Data2, 64, InputPhase.Down, ref history, Tick);
                ProcessULong(downFiltered.Data3, 128, InputPhase.Down, ref history, Tick);
                ProcessULong(downFiltered.Data4, 192, InputPhase.Down, ref history, Tick);

                ProcessULong(upFiltered.Data1, 0, InputPhase.Up, ref history, Tick);
                ProcessULong(upFiltered.Data2, 64, InputPhase.Up, ref history, Tick);
                ProcessULong(upFiltered.Data3, 128, InputPhase.Up, ref history, Tick);
                ProcessULong(upFiltered.Data4, 192, InputPhase.Up, ref history, Tick);
            }

            private static void ProcessULong(ulong data, byte offset, InputPhase phase,
                ref DynamicBuffer<InputHistory> history, uint tick)
            {
                while (data != 0)
                {
                    var bit = math.tzcnt(data);
                    data ^= 1ul << bit;
                    history.Add(new InputHistory { ActionId = (byte)(offset + bit), Phase = phase, Tick = tick });
                }
            }
        }
    }
}