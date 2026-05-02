using BovineLabs.Core.Collections;
using BovineLabs.Reaction.Conditions;
using BovineLabs.Timeline.Data;
using BovineLabs.Timeline.PlayerInputs.Data;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Collections;
using Unity.Entities;

namespace BovineLabs.Timeline.PlayerInputs
{
    [UpdateInGroup(typeof(TimelineComponentAnimationGroup))]
    [UpdateAfter(typeof(ConsumerHistorySystem))]
    public partial struct InputTransducerSystem : ISystem
    {
        private ConditionEventWriter.Lookup writers;
        private ComponentLookup<InputState> states;
        private BufferLookup<InputHistory> histories;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            writers.Create(ref state);
            states = state.GetComponentLookup<InputState>(true);
            histories = state.GetBufferLookup<InputHistory>(false);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            writers.Update(ref state);
            states.Update(ref state);
            histories.Update(ref state);

            state.Dependency = new EvaluateComboJob
            {
                Writers = writers,
                States = states,
                Histories = histories
            }.Schedule(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(ClipActive))]
        private partial struct EvaluateComboJob : IJobEntity
        {
            public ConditionEventWriter.Lookup Writers;
            [ReadOnly] public ComponentLookup<InputState> States;
            public BufferLookup<InputHistory> Histories;

            private void Execute(in TransducerConfig config, in TrackBinding binding)
            {
                if (binding.Value == Entity.Null) return;
                if (!States.TryGetComponent(binding.Value, out var state)) return;
                if (!Histories.TryGetBuffer(binding.Value, out var history)) return;

                ref var reqs = ref config.Blob.Value.Requirements;
                if (reqs.Length == 0) return;

                var consumeMask = default(BitArray256); // Track consumed indices

                for (var i = 0; i < reqs.Length; i++)
                {
                    if (!Evaluate(reqs[i], state, history, ref consumeMask))
                        return; // Atomic fail. Abort.
                }

                // Atomic success. Trigger event.
                if (Hint.Likely(Writers.TryGet(config.RouteEntity, out var writer)))
                    writer.Trigger(config.Condition, config.Value);

                // Commit consumes
                if (consumeMask.AllFalse) return;

                var write = 0;
                for (var read = 0; read < history.Length; read++)
                {
                    if (consumeMask[read]) continue;
                    if (write != read) history[write] = history[read];
                    write++;
                }

                for (var i = history.Length - 1; i >= write; i--) history.RemoveAt(i);
            }

            private static bool Evaluate(TransducerRequirement req, in InputState state,
                in DynamicBuffer<InputHistory> history, ref BitArray256 consumeMask)
            {
                switch (req.Mode)
                {
                    case BufferMode.None:
                        return state.Pressed[req.ActionId];

                    case BufferMode.Contains:
                    case BufferMode.Consume:
                        for (var i = 0; i < history.Length; i++)
                        {
                            if (consumeMask[i] || history[i].ActionId != req.ActionId) continue;
                            if (req.Mode == BufferMode.Consume) consumeMask[i] = true;
                            return true;
                        }

                        return false;

                    case BufferMode.FirstConsume:
                        for (var i = 0; i < history.Length; i++)
                        {
                            if (consumeMask[i]) continue;
                            if (history[i].ActionId != req.ActionId) return false;
                            consumeMask[i] = true;
                            return true;
                        }

                        return false;

                    case BufferMode.LastConsume:
                        for (var i = history.Length - 1; i >= 0; i--)
                        {
                            if (consumeMask[i]) continue;
                            if (history[i].ActionId != req.ActionId) return false;
                            consumeMask[i] = true;
                            return true;
                        }

                        return false;

                    default: return false;
                }
            }
        }
    }

    [UpdateInGroup(typeof(TimelineComponentAnimationGroup))]
    [UpdateAfter(typeof(InputTransducerSystem))]
    public partial struct InputBufferClearSystem : ISystem
    {
        private BufferLookup<InputHistory> histories;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            histories = state.GetBufferLookup<InputHistory>(false);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            histories.Update(ref state);
            state.Dependency = new ClearBufferJob { Histories = histories }.Schedule(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(ClipActive), typeof(BufferClearConfig))]
        [WithNone(typeof(ClipActivePrevious))]
        private partial struct ClearBufferJob : IJobEntity
        {
            public BufferLookup<InputHistory> Histories;

            private void Execute(in BufferClearConfig config, in TrackBinding binding)
            {
                if (binding.Value == Entity.Null || !Histories.TryGetBuffer(binding.Value, out var history)) return;

                ref var ids = ref config.ActionIds.Value;
                if (ids.Length == 0)
                {
                    history.Clear();
                    return;
                }

                var filter = default(BitArray256);
                for (var i = 0; i < ids.Length; i++) filter[ids[i]] = true;

                var write = 0;
                for (var read = 0; read < history.Length; read++)
                {
                    if (filter[history[read].ActionId]) continue;
                    if (write != read) history[write] = history[read];
                    write++;
                }

                for (var i = history.Length - 1; i >= write; i--) history.RemoveAt(i);
            }
        }
    }
}