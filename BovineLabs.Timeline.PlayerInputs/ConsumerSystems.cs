using BovineLabs.Core.Groups;
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
        // ADD: field to hold the lookup
        private ComponentLookup<ActiveBufferMask> masks;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            // ADD: initialize lookup as writable
            masks = state.GetComponentLookup<ActiveBufferMask>(false);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            // ADD: update lookup each frame
            masks.Update(ref state);

            state.Dependency = new ResetMaskJob().ScheduleParallel(state.Dependency);
            state.Dependency = new AccumulateMaskJob { Masks = masks }.Schedule(state.Dependency);
            //                                                          ^^^^^^^^ single-threaded: writes via lookup
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
            // CHANGED: lookup instead of direct ref — reaches consumer via TrackBinding.Value
            public ComponentLookup<ActiveBufferMask> Masks;

            private void Execute(in BufferWindowConfig config, in TrackBinding binding)
            {
                if (binding.Value == Entity.Null) return;
                if (!Masks.TryGetComponent(binding.Value, out var mask)) return;
                mask.Value = mask.Value.BitOr(config.AllowedActions);
                Masks[binding.Value] = mask;  // write back
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
                if (state.Down.AllFalse || mask.Value.AllFalse) return;

                var filteredDown = state.Down.BitAnd(mask.Value);
                if (filteredDown.AllFalse) return;

                for (byte i = 0; i < 255; i++)
                {
                    if (!filteredDown[i]) continue;
                    if (history.Length >= history.Capacity) history.RemoveAt(0);
                    history.Add(new InputHistory { ActionId = i, Tick = Tick });
                }
            }
        }
    }
}