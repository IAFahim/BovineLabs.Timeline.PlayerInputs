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
    [UpdateAfter(typeof(CommandSequenceResetSystem))]
    public partial struct CommandSequenceSystem : ISystem
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

            state.Dependency = new EvaluateSequenceJob
            {
                Writers = writers,
                States = states,
                Histories = histories
            }.Schedule(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(ClipActive))]
        private partial struct EvaluateSequenceJob : IJobEntity
        {
            public ConditionEventWriter.Lookup Writers;
            [ReadOnly] public ComponentLookup<InputState> States;
            public BufferLookup<InputHistory> Histories;

            private void Execute(ref CommandSequenceState commandState, in CommandSequenceConfig config, in TrackBinding binding)
            {
                if (commandState.IsCompleted || binding.Value == Entity.Null) return;
                if (!States.TryGetComponent(binding.Value, out var state)) return;
                if (!Histories.TryGetBuffer(binding.Value, out var history)) return;

                ref var sequences = ref config.Blob.Value.Sequences;

                for (var s = 0; s < sequences.Length; s++)
                {
                    ref var seq = ref sequences[s];
                    if (seq.Steps.Length == 0) continue;

                    var consumeMask = default(BitArray256);
                    var searchIndex = 0;
                    var matched = true;

                    for (var i = 0; i < seq.Steps.Length; i++)
                    {
                        if (!Evaluate(ref seq.Steps[i], state, history, ref consumeMask, ref searchIndex))
                        {
                            matched = false;
                            break;
                        }
                    }

                    if (!matched) continue;

                    CommitConsumes(history, ref consumeMask);

                    // config.RouteEntity — correct, remapped, valid at runtime
                    if (Hint.Likely(Writers.TryGet(config.RouteEntity, out var writer)))
                        writer.Trigger(seq.Condition, seq.Value);

                    commandState.IsCompleted = true;
                    return;
                }
            }

            private static bool Evaluate(ref CommandStep step, in InputState state, in DynamicBuffer<InputHistory> history, ref BitArray256 consumeMask, ref int searchIndex)
            {
                switch (step.Mode)
                {
                    case CommandMode.None:
                        return state.Pressed[step.ActionId];

                    case CommandMode.Contains:
                    case CommandMode.Consume:
                    {
                        for (var i = 0; i < history.Length; i++)
                        {
                            if (consumeMask[i] || history[i].ActionId != step.ActionId) continue;
                            if (step.Mode == CommandMode.Consume) consumeMask[i] = true;
                            return true;
                        }
                        return false;
                    }

                    case CommandMode.FirstConsume:
                    {
                        for (var i = 0; i < history.Length; i++)
                        {
                            if (consumeMask[i]) continue;
                            if (history[i].ActionId != step.ActionId) return false;
                            consumeMask[i] = true;
                            return true;
                        }
                        return false;
                    }

                    case CommandMode.LastConsume:
                    {
                        for (var i = history.Length - 1; i >= 0; i--)
                        {
                            if (consumeMask[i]) continue;
                            if (history[i].ActionId != step.ActionId) return false;
                            consumeMask[i] = true;
                            return true;
                        }
                        return false;
                    }

                    case CommandMode.OrderedContains:
                    case CommandMode.OrderedConsume:
                    {
                        for (var i = searchIndex; i < history.Length; i++)
                        {
                            if (consumeMask[i] || history[i].ActionId != step.ActionId) continue;
                            if (step.Mode == CommandMode.OrderedConsume) consumeMask[i] = true;
                            searchIndex = i + 1;
                            return true;
                        }
                        return false;
                    }

                    case CommandMode.OrderedFirstConsume:
                    {
                        for (var i = searchIndex; i < history.Length; i++)
                        {
                            if (consumeMask[i]) continue;
                            if (history[i].ActionId != step.ActionId) return false;
                            consumeMask[i] = true;
                            searchIndex = i + 1;
                            return true;
                        }
                        return false;
                    }

                    case CommandMode.OrderedLastConsume:
                    {
                        for (var i = history.Length - 1; i >= searchIndex; i--)
                        {
                            if (consumeMask[i]) continue;
                            if (history[i].ActionId != step.ActionId) return false;
                            consumeMask[i] = true;
                            searchIndex = i + 1;
                            return true;
                        }
                        return false;
                    }

                    case CommandMode.NotContains:
                    {
                        for (var i = 0; i < history.Length; i++)
                        {
                            if (consumeMask[i]) continue;
                            if (history[i].ActionId == step.ActionId) return false;
                        }
                        return true;
                    }

                    case CommandMode.NotFirst:
                    {
                        for (var i = 0; i < history.Length; i++)
                        {
                            if (consumeMask[i]) continue;
                            return history[i].ActionId != step.ActionId;
                        }
                        return true;
                    }

                    case CommandMode.NotLast:
                    {
                        for (var i = history.Length - 1; i >= 0; i--)
                        {
                            if (consumeMask[i]) continue;
                            return history[i].ActionId != step.ActionId;
                        }
                        return true;
                    }

                    default: return false;
                }
            }

            private static void CommitConsumes(DynamicBuffer<InputHistory> history, ref BitArray256 consumeMask)
            {
                if (consumeMask.AllFalse) return;

                var write = 0;
                for (var read = 0; read < history.Length; read++)
                {
                    if (consumeMask[read]) continue;
                    if (write != read) history[write] = history[read];
                    write++;
                }

                history.Length = write;
            }
        }
    }
}