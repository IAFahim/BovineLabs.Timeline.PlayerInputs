using BovineLabs.Core.Groups;
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
            [ReadOnly] [NativeDisableContainerSafetyRestriction] public ComponentLookup<InputState> Providers;
            [ReadOnly] [NativeDisableContainerSafetyRestriction] public BufferLookup<InputAxis> Axes;

            private void Execute(in InputSource source, ref InputState state, ref PlayerMoveInput move, ref DynamicBuffer<InputAxis> consumerAxes)
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
    public partial struct ConsumerBufferMaskSystem : ISystem
    {
        private ComponentLookup<ActiveBufferMask> masks;
        private EntityLock entityLock;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            this.masks = state.GetComponentLookup<ActiveBufferMask>(false);
            this.entityLock = new EntityLock(Allocator.Persistent);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            this.entityLock.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            this.masks.Update(ref state);

            state.Dependency = new ResetMaskJob().ScheduleParallel(state.Dependency);
            state.Dependency = new AccumulateMaskJob 
            { 
                Masks = this.masks,
                EntityLock = this.entityLock
            }.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(ConsumerTag))]
        private partial struct ResetMaskJob : IJobEntity
        {
            private void Execute(ref ActiveBufferMask mask) => mask.Value = default;
        }

        [BurstCompile]
        [WithAll(typeof(ClipActive))]
        private partial struct AccumulateMaskJob : IJobEntity
        {
            [NativeDisableParallelForRestriction]
            public ComponentLookup<ActiveBufferMask> Masks;
            public EntityLock EntityLock;

            private void Execute(in BufferWindowConfig config, in TrackBinding binding)
            {
                if (binding.Value == Entity.Null) return;
                
                using (this.EntityLock.Acquire(binding.Value))
                {
                    if (!this.Masks.TryGetComponent(binding.Value, out var mask)) return;
                    mask.Value = mask.Value.BitOr(config.AllowedActions);
                    this.Masks[binding.Value] = mask;
                }
            }
        }
    }

    [UpdateInGroup(typeof(TimelineComponentAnimationGroup))]
    [UpdateAfter(typeof(ConsumerBufferMaskSystem))]
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
                if (state.Pressed.AllFalse || mask.Value.AllFalse) return;

                var filtered = state.Pressed.BitAnd(mask.Value);
                if (filtered.AllFalse) return;

                var totalToAdd = filtered.CountBits();
                var removeCount = math.max(0, history.Length + totalToAdd - history.Capacity);
                
                if (removeCount > 0)
                {
                    history.RemoveRange(0, removeCount);
                }

                ProcessULong(filtered.Data1, 0, ref history, this.Tick);
                ProcessULong(filtered.Data2, 64, ref history, this.Tick);
                ProcessULong(filtered.Data3, 128, ref history, this.Tick);
                ProcessULong(filtered.Data4, 192, ref history, this.Tick);
            }

            private static void ProcessULong(ulong data, byte offset, ref DynamicBuffer<InputHistory> history, uint tick)
            {
                while (data != 0)
                {
                    var bit = math.tzcnt(data);
                    data ^= 1ul << bit;
                    history.Add(new InputHistory { ActionId = (byte)(offset + bit), Tick = tick });
                }
            }
        }
    }
}