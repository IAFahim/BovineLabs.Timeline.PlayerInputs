using BovineLabs.Core.Collections;
using BovineLabs.Timeline.PlayerInputs.Data;
using Unity.Entities;

namespace BovineLabs.Timeline.PlayerInputs
{
    internal static class CommandMatcher
    {
        private const uint NoPriorMatch = uint.MaxValue;

        public static bool Evaluate(ref CommandStep step, in InputState state,
            in DynamicBuffer<InputHistory> history, ref BitArray256 consumeMask, ref int searchIndex,
            ref uint lastMatchTick)
        {
            switch (step.Mode)
            {
                case CommandMode.None:

                    return EvaluateLiveState(in step, in state);
                case CommandMode.Contains:
                case CommandMode.Consume:
                    return EvaluateContains(in step, in history, ref consumeMask, ref lastMatchTick,
                        step.Mode == CommandMode.Consume);
                case CommandMode.FirstConsume:
                    return EvaluateFirstConsume(in step, in history, ref consumeMask, ref lastMatchTick);
                case CommandMode.LastConsume:
                    return EvaluateLastConsume(in step, in history, ref consumeMask, ref lastMatchTick);
                case CommandMode.OrderedContains:
                case CommandMode.OrderedConsume:
                    return EvaluateOrdered(in step, in history, ref consumeMask, ref searchIndex,
                        ref lastMatchTick, step.Mode == CommandMode.OrderedConsume);
                case CommandMode.OrderedFirstConsume:
                    return EvaluateOrderedFirstConsume(in step, in history, ref consumeMask, ref searchIndex,
                        ref lastMatchTick);
                case CommandMode.OrderedLastConsume:
                    return EvaluateOrderedLastConsume(in step, in history, ref consumeMask, ref searchIndex,
                        ref lastMatchTick);
                case CommandMode.NotContains:
                    return EvaluateNotContains(in step, in history, ref consumeMask);
                case CommandMode.NotFirst:
                    return EvaluateNotFirst(in step, in history, ref consumeMask);
                case CommandMode.NotLast:
                    return EvaluateNotLast(in step, in history, ref consumeMask);
                default:
                    return false;
            }
        }

        public static bool EvaluateLiveState(in CommandStep step, in InputState state)
        {
            return step.Phase switch
            {
                InputPhase.Down => state.Down[step.ActionId],
                InputPhase.Held => state.Held[step.ActionId],
                InputPhase.Up => state.Up[step.ActionId],
                _ => false
            };
        }

        public static bool EvaluateContains(in CommandStep step, in DynamicBuffer<InputHistory> history,
            ref BitArray256 consumeMask, ref uint lastMatchTick, bool consume)
        {
            for (var i = 0; i < history.Length; i++)
            {
                if (consumeMask[i] || history[i].ActionId != step.ActionId ||
                    history[i].Phase != step.Phase) continue;

                if (!WithinWindow(history[i].Tick, step.MaxGapTicks, ref lastMatchTick)) continue;
                if (consume) consumeMask[i] = true;
                return true;
            }

            return false;
        }

        public static bool EvaluateFirstConsume(in CommandStep step, in DynamicBuffer<InputHistory> history,
            ref BitArray256 consumeMask, ref uint lastMatchTick)
        {
            for (var i = 0; i < history.Length; i++)
            {
                if (consumeMask[i]) continue;
                if (history[i].ActionId != step.ActionId || history[i].Phase != step.Phase) return false;
                if (!WithinWindow(history[i].Tick, step.MaxGapTicks, ref lastMatchTick)) return false;
                consumeMask[i] = true;
                return true;
            }

            return false;
        }

        public static bool EvaluateLastConsume(in CommandStep step, in DynamicBuffer<InputHistory> history,
            ref BitArray256 consumeMask, ref uint lastMatchTick)
        {
            for (var i = history.Length - 1; i >= 0; i--)
            {
                if (consumeMask[i]) continue;
                if (history[i].ActionId != step.ActionId || history[i].Phase != step.Phase) return false;
                if (!WithinWindow(history[i].Tick, step.MaxGapTicks, ref lastMatchTick)) return false;
                consumeMask[i] = true;
                return true;
            }

            return false;
        }

        public static bool EvaluateOrdered(in CommandStep step, in DynamicBuffer<InputHistory> history,
            ref BitArray256 consumeMask, ref int searchIndex, ref uint lastMatchTick, bool consume)
        {
            for (var i = searchIndex; i < history.Length; i++)
            {
                if (consumeMask[i] || history[i].ActionId != step.ActionId ||
                    history[i].Phase != step.Phase) continue;

                if (!WithinWindow(history[i].Tick, step.MaxGapTicks, ref lastMatchTick)) continue;
                if (consume) consumeMask[i] = true;
                searchIndex = i + 1;
                return true;
            }

            return false;
        }

        public static bool EvaluateOrderedFirstConsume(in CommandStep step,
            in DynamicBuffer<InputHistory> history, ref BitArray256 consumeMask, ref int searchIndex,
            ref uint lastMatchTick)
        {
            for (var i = searchIndex; i < history.Length; i++)
            {
                if (consumeMask[i]) continue;
                if (history[i].ActionId != step.ActionId || history[i].Phase != step.Phase) return false;
                if (!WithinWindow(history[i].Tick, step.MaxGapTicks, ref lastMatchTick)) return false;
                consumeMask[i] = true;
                searchIndex = i + 1;
                return true;
            }

            return false;
        }

        public static bool EvaluateOrderedLastConsume(in CommandStep step,
            in DynamicBuffer<InputHistory> history, ref BitArray256 consumeMask, ref int searchIndex,
            ref uint lastMatchTick)
        {
            for (var i = history.Length - 1; i >= searchIndex; i--)
            {
                if (consumeMask[i] || history[i].ActionId != step.ActionId ||
                    history[i].Phase != step.Phase) continue;
                if (!WithinWindow(history[i].Tick, step.MaxGapTicks, ref lastMatchTick)) return false;
                consumeMask[i] = true;
                searchIndex = i + 1;
                return true;
            }

            return false;
        }

        public static bool EvaluateNotContains(in CommandStep step, in DynamicBuffer<InputHistory> history,
            ref BitArray256 consumeMask)
        {
            for (var i = 0; i < history.Length; i++)
            {
                if (consumeMask[i]) continue;
                if (history[i].ActionId == step.ActionId && history[i].Phase == step.Phase) return false;
            }

            return true;
        }

        public static bool EvaluateNotFirst(in CommandStep step, in DynamicBuffer<InputHistory> history,
            ref BitArray256 consumeMask)
        {
            for (var i = 0; i < history.Length; i++)
            {
                if (consumeMask[i]) continue;
                return history[i].ActionId != step.ActionId || history[i].Phase != step.Phase;
            }

            return true;
        }

        public static bool EvaluateNotLast(in CommandStep step, in DynamicBuffer<InputHistory> history,
            ref BitArray256 consumeMask)
        {
            for (var i = history.Length - 1; i >= 0; i--)
            {
                if (consumeMask[i]) continue;
                return history[i].ActionId != step.ActionId || history[i].Phase != step.Phase;
            }

            return true;
        }

        public static bool WithinWindow(uint matchTick, ushort maxGapTicks, ref uint lastMatchTick)
        {
            if (lastMatchTick != NoPriorMatch)
            {
                if (matchTick < lastMatchTick) return false;
                if (maxGapTicks != 0 && matchTick - lastMatchTick > maxGapTicks) return false;
            }

            lastMatchTick = matchTick == NoPriorMatch ? NoPriorMatch - 1 : matchTick;
            return true;
        }
    }
}