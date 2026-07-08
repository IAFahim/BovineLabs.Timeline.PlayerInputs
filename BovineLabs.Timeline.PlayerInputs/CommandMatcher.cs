using BovineLabs.Core.Collections;
using BovineLabs.Timeline.PlayerInputs.Data;
using Unity.Entities;

namespace BovineLabs.Timeline.PlayerInputs
{
    // Accumulator threaded through a sequence's steps. Ordering is enforced on Tick (a monotonic per-frame sequence
    // number); the timing WINDOW is enforced on Millis (wall-clock), so a motion input's gap tolerance is identical
    // regardless of framerate. HasPrior replaces the old uint.MaxValue sentinel (no aliasing at the max tick).
    internal struct MatchWindow
    {
        public bool HasPrior;
        public uint LastTick;
        public uint LastMillis;
    }

    internal static class CommandMatcher
    {
        public static bool Evaluate(ref CommandStep step, in InputState state,
            in DynamicBuffer<InputHistory> history, ref BitArray256 consumeMask, ref int searchIndex,
            ref MatchWindow window)
        {
            switch (step.Mode)
            {
                case CommandMode.None:

                    return EvaluateLiveState(in step, in state);
                case CommandMode.Contains:
                case CommandMode.Consume:
                    return EvaluateContains(in step, in history, ref consumeMask, ref window,
                        step.Mode == CommandMode.Consume);
                case CommandMode.FirstConsume:
                    return EvaluateFirstConsume(in step, in history, ref consumeMask, ref window);
                case CommandMode.LastConsume:
                    return EvaluateLastConsume(in step, in history, ref consumeMask, ref window);
                case CommandMode.OrderedContains:
                case CommandMode.OrderedConsume:
                    return EvaluateOrdered(in step, in history, ref consumeMask, ref searchIndex,
                        ref window, step.Mode == CommandMode.OrderedConsume);
                case CommandMode.OrderedFirstConsume:
                    return EvaluateOrderedFirstConsume(in step, in history, ref consumeMask, ref searchIndex,
                        ref window);
                case CommandMode.OrderedLastConsume:
                    return EvaluateOrderedLastConsume(in step, in history, ref consumeMask, ref searchIndex,
                        ref window);
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
            ref BitArray256 consumeMask, ref MatchWindow window, bool consume)
        {
            for (var i = 0; i < history.Length; i++)
            {
                if (consumeMask[i] || history[i].ActionId != step.ActionId ||
                    history[i].Phase != step.Phase) continue;

                if (!WithinWindow(history[i].Tick, history[i].Millis, step.MaxGapMillis, ref window)) continue;
                if (consume) consumeMask[i] = true;
                return true;
            }

            return false;
        }

        public static bool EvaluateFirstConsume(in CommandStep step, in DynamicBuffer<InputHistory> history,
            ref BitArray256 consumeMask, ref MatchWindow window)
        {
            for (var i = 0; i < history.Length; i++)
            {
                if (consumeMask[i]) continue;
                if (history[i].ActionId != step.ActionId || history[i].Phase != step.Phase) return false;
                if (!WithinWindow(history[i].Tick, history[i].Millis, step.MaxGapMillis, ref window)) return false;
                consumeMask[i] = true;
                return true;
            }

            return false;
        }

        public static bool EvaluateLastConsume(in CommandStep step, in DynamicBuffer<InputHistory> history,
            ref BitArray256 consumeMask, ref MatchWindow window)
        {
            for (var i = history.Length - 1; i >= 0; i--)
            {
                if (consumeMask[i]) continue;
                if (history[i].ActionId != step.ActionId || history[i].Phase != step.Phase) return false;
                if (!WithinWindow(history[i].Tick, history[i].Millis, step.MaxGapMillis, ref window)) return false;
                consumeMask[i] = true;
                return true;
            }

            return false;
        }

        public static bool EvaluateOrdered(in CommandStep step, in DynamicBuffer<InputHistory> history,
            ref BitArray256 consumeMask, ref int searchIndex, ref MatchWindow window, bool consume)
        {
            for (var i = searchIndex; i < history.Length; i++)
            {
                if (consumeMask[i] || history[i].ActionId != step.ActionId ||
                    history[i].Phase != step.Phase) continue;

                if (!WithinWindow(history[i].Tick, history[i].Millis, step.MaxGapMillis, ref window)) continue;
                if (consume) consumeMask[i] = true;
                searchIndex = i + 1;
                return true;
            }

            return false;
        }

        public static bool EvaluateOrderedFirstConsume(in CommandStep step,
            in DynamicBuffer<InputHistory> history, ref BitArray256 consumeMask, ref int searchIndex,
            ref MatchWindow window)
        {
            for (var i = searchIndex; i < history.Length; i++)
            {
                if (consumeMask[i]) continue;
                if (history[i].ActionId != step.ActionId || history[i].Phase != step.Phase) return false;
                if (!WithinWindow(history[i].Tick, history[i].Millis, step.MaxGapMillis, ref window)) return false;
                consumeMask[i] = true;
                searchIndex = i + 1;
                return true;
            }

            return false;
        }

        public static bool EvaluateOrderedLastConsume(in CommandStep step,
            in DynamicBuffer<InputHistory> history, ref BitArray256 consumeMask, ref int searchIndex,
            ref MatchWindow window)
        {
            for (var i = history.Length - 1; i >= searchIndex; i--)
            {
                if (consumeMask[i] || history[i].ActionId != step.ActionId ||
                    history[i].Phase != step.Phase) continue;
                if (!WithinWindow(history[i].Tick, history[i].Millis, step.MaxGapMillis, ref window)) return false;
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

        // Order is enforced on Tick (matchTick may not be older than the last matched entry); the timing window is
        // enforced on Millis (framerate-independent). maxGapMillis == 0 disables the window.
        public static bool WithinWindow(uint matchTick, uint matchMillis, ushort maxGapMillis, ref MatchWindow window)
        {
            if (window.HasPrior)
            {
                if (matchTick < window.LastTick) return false;
                if (maxGapMillis != 0 && matchMillis - window.LastMillis > maxGapMillis) return false;
            }

            window.HasPrior = true;
            window.LastTick = matchTick;
            window.LastMillis = matchMillis;
            return true;
        }
    }
}
