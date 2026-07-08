using BovineLabs.Core.Collections;
using BovineLabs.Testing;
using BovineLabs.Timeline.PlayerInputs.Data;
using NUnit.Framework;
using Unity.Entities;

namespace BovineLabs.Timeline.PlayerInputs.Tests
{
    public class CommandMatcherTests : ECSTestsFixture
    {
        [Test]
        public void Contains_MatchesActionInHistory()
        {
            var history = History((5, InputPhase.Down, 0, 0));
            Assert.IsTrue(EvalOnce(Step(CommandMode.Contains, 5), history));
        }

        [Test]
        public void Contains_DifferentAction_ReturnsFalse()
        {
            var history = History((5, InputPhase.Down, 0, 0));
            Assert.IsFalse(EvalOnce(Step(CommandMode.Contains, 7), history));
        }

        [Test]
        public void Contains_DifferentPhase_ReturnsFalse()
        {
            var history = History((5, InputPhase.Down, 0, 0));
            Assert.IsFalse(EvalOnce(Step(CommandMode.Contains, 5, InputPhase.Up), history));
        }

        [Test]
        public void Consume_MarksHistory_SecondMatchOnSameMaskFails()
        {
            var history = History((5, InputPhase.Down, 0, 0));
            var step = Step(CommandMode.Consume, 5);

            var mask = default(BitArray256);
            var searchIndex = 0;
            var window = default(MatchWindow);
            var first = step;
            Assert.IsTrue(CommandMatcher.Evaluate(ref first, default, history, ref mask, ref searchIndex,
                ref window));

            searchIndex = 0;
            window = default;
            var second = step;
            Assert.IsFalse(CommandMatcher.Evaluate(ref second, default, history, ref mask, ref searchIndex,
                ref window));
        }

        // The gap window is now measured in MILLISECONDS (wall-clock), not ticks. Same physical timing must produce
        // the same match regardless of the tick cadence (framerate).
        [Test]
        public void WithinWindow_GapExceedsMaxGapMillis_Fails()
        {
            // tick 0->1 (monotonic order ok), but 10 ms elapsed and the window is 5 ms.
            var history = History((1, InputPhase.Down, 0, 0), (2, InputPhase.Down, 1, 10));
            var mask = default(BitArray256);
            var searchIndex = 0;
            var window = default(MatchWindow);

            var first = Step(CommandMode.Contains, 1);
            Assert.IsTrue(CommandMatcher.Evaluate(ref first, default, history, ref mask, ref searchIndex,
                ref window));

            var second = Step(CommandMode.Contains, 2, InputPhase.Down, maxGapMillis: 5);
            Assert.IsFalse(CommandMatcher.Evaluate(ref second, default, history, ref mask, ref searchIndex,
                ref window));
        }

        [Test]
        public void WithinWindow_GapInsideMaxGapMillis_Succeeds()
        {
            var history = History((1, InputPhase.Down, 0, 0), (2, InputPhase.Down, 1, 3));
            var mask = default(BitArray256);
            var searchIndex = 0;
            var window = default(MatchWindow);

            var first = Step(CommandMode.Contains, 1);
            CommandMatcher.Evaluate(ref first, default, history, ref mask, ref searchIndex, ref window);

            var second = Step(CommandMode.Contains, 2, InputPhase.Down, maxGapMillis: 5);
            Assert.IsTrue(CommandMatcher.Evaluate(ref second, default, history, ref mask, ref searchIndex,
                ref window));
        }

        // The heart of the framerate-independence fix: identical elapsed-time streams sampled at 60 fps vs 240 fps
        // (very different tick spacing) yield IDENTICAL match results, because the window compares Millis not Tick.
        [Test]
        public void WithinWindow_SamePhysicalTiming_DifferentFps_MatchesIdentically()
        {
            // 100 ms between the two presses either way. maxGap 150 ms -> both match; 50 ms -> both fail.
            var at60 = History((1, InputPhase.Down, 0, 0), (2, InputPhase.Down, 6, 100));   // 6 frames @ ~16.7 ms
            var at240 = History((1, InputPhase.Down, 0, 0), (2, InputPhase.Down, 24, 100)); // 24 frames @ ~4.2 ms

            Assert.IsTrue(MatchTwoStep(at60, maxGapMillis: 150), "60 fps within 150 ms window");
            Assert.IsTrue(MatchTwoStep(at240, maxGapMillis: 150), "240 fps within 150 ms window (tick gap is 24!)");

            Assert.IsFalse(MatchTwoStep(at60, maxGapMillis: 50), "60 fps outside 50 ms window");
            Assert.IsFalse(MatchTwoStep(at240, maxGapMillis: 50), "240 fps outside 50 ms window");
        }

        // Order is still enforced on Tick: an entry recorded on an EARLIER tick than the previous match cannot count,
        // even if its millis would fit the window.
        [Test]
        public void WithinWindow_OutOfTickOrder_Fails()
        {
            var history = History((1, InputPhase.Down, 5, 50), (2, InputPhase.Down, 2, 20));
            var mask = default(BitArray256);
            var searchIndex = 0;
            var window = default(MatchWindow);

            var first = Step(CommandMode.Contains, 1); // matches entry 0, tick 5
            Assert.IsTrue(CommandMatcher.Evaluate(ref first, default, history, ref mask, ref searchIndex,
                ref window));

            var second = Step(CommandMode.Contains, 2, InputPhase.Down, maxGapMillis: 1000);
            Assert.IsFalse(CommandMatcher.Evaluate(ref second, default, history, ref mask, ref searchIndex,
                ref window), "entry 1 is on an earlier tick than the previous match");
        }

        [Test]
        public void OrderedConsume_InOrderSucceeds()
        {
            var history = History((1, InputPhase.Down, 0, 0), (2, InputPhase.Down, 1, 1));
            var mask = default(BitArray256);
            var searchIndex = 0;
            var window = default(MatchWindow);

            var a = Step(CommandMode.OrderedConsume, 1);
            var b = Step(CommandMode.OrderedConsume, 2);
            Assert.IsTrue(CommandMatcher.Evaluate(ref a, default, history, ref mask, ref searchIndex, ref window));
            Assert.IsTrue(CommandMatcher.Evaluate(ref b, default, history, ref mask, ref searchIndex, ref window));
        }

        [Test]
        public void OrderedConsume_OutOfOrderFails()
        {
            var history = History((1, InputPhase.Down, 0, 0), (2, InputPhase.Down, 1, 1));
            var mask = default(BitArray256);
            var searchIndex = 0;
            var window = default(MatchWindow);

            var b = Step(CommandMode.OrderedConsume, 2);
            var a = Step(CommandMode.OrderedConsume, 1);
            Assert.IsTrue(CommandMatcher.Evaluate(ref b, default, history, ref mask, ref searchIndex, ref window));
            Assert.IsFalse(CommandMatcher.Evaluate(ref a, default, history, ref mask, ref searchIndex, ref window));
        }

        [Test]
        public void OrderedLastConsume_PicksLatestMatchingEntry()
        {
            // Two Downs of action 1; OrderedLastConsume scans from the end and consumes the newest.
            var history = History((1, InputPhase.Down, 0, 0), (1, InputPhase.Down, 5, 80));
            var mask = default(BitArray256);
            var searchIndex = 0;
            var window = default(MatchWindow);

            var step = Step(CommandMode.OrderedLastConsume, 1);
            Assert.IsTrue(CommandMatcher.Evaluate(ref step, default, history, ref mask, ref searchIndex, ref window));
            Assert.IsTrue(mask[1], "newest (index 1) entry consumed");
            Assert.IsFalse(mask[0], "oldest (index 0) entry left for another step");
        }

        [Test]
        public void NotContains_AbsentTrue_PresentFalse()
        {
            var history = History((5, InputPhase.Down, 0, 0));
            Assert.IsTrue(EvalOnce(Step(CommandMode.NotContains, 7), history));
            Assert.IsFalse(EvalOnce(Step(CommandMode.NotContains, 5), history));
        }

        [Test]
        public void None_ProbesLiveInputState_AllPhases_IgnoresHistory()
        {
            var state = default(InputState);
            state.Down[5] = true;
            state.Held[6] = true;
            state.Up[7] = true;

            var history = History((5, InputPhase.Down, 0, 0), (6, InputPhase.Down, 0, 0), (7, InputPhase.Up, 0, 0));

            Assert.IsTrue(EvalOnce(Step(CommandMode.None, 5), history, state));
            Assert.IsTrue(EvalOnce(Step(CommandMode.None, 6, InputPhase.Held), history, state));
            Assert.IsTrue(EvalOnce(Step(CommandMode.None, 7, InputPhase.Up), history, state));

            Assert.IsFalse(EvalOnce(Step(CommandMode.None, 8), history, state));
            Assert.IsFalse(EvalOnce(Step(CommandMode.None, 5, InputPhase.Up), history, state));
        }

        [Test]
        public void None_DoesNotConsumeHistory()
        {
            var history = History((5, InputPhase.Down, 0, 0));
            var state = default(InputState);
            state.Down[5] = true;

            var mask = default(BitArray256);
            var searchIndex = 0;
            var window = default(MatchWindow);
            var none = Step(CommandMode.None, 5);
            Assert.IsTrue(CommandMatcher.Evaluate(ref none, state, history, ref mask, ref searchIndex, ref window));

            var consume = Step(CommandMode.Consume, 5);
            Assert.IsTrue(CommandMatcher.Evaluate(ref consume, state, history, ref mask, ref searchIndex,
                ref window));
        }

        [Test]
        public void Contains_SkipsOutOfOrderEntry_AndMatchesLaterInWindowEntry()
        {
            var history = History((1, InputPhase.Down, 0, 0), (2, InputPhase.Down, 1, 3), (1, InputPhase.Down, 2, 5));
            var mask = default(BitArray256);
            var searchIndex = 0;
            var window = default(MatchWindow);

            var first = Step(CommandMode.Contains, 2); // matches entry 1 (tick 1)
            Assert.IsTrue(CommandMatcher.Evaluate(ref first, default, history, ref mask, ref searchIndex,
                ref window));

            // entry 0 (action 1, tick 0) is before the previous match -> skipped; entry 2 (tick 2, +2 ms) matches.
            var second = Step(CommandMode.Contains, 1, InputPhase.Down, maxGapMillis: 10);
            Assert.IsTrue(CommandMatcher.Evaluate(ref second, default, history, ref mask, ref searchIndex,
                ref window));
        }

        // Evaluates a two-step Contains sequence (action 1 then action 2) over the given history, threading one
        // MatchWindow exactly as CommandSequenceSystem does. Returns whether both steps matched.
        private static bool MatchTwoStep(in DynamicBuffer<InputHistory> history, ushort maxGapMillis)
        {
            var mask = default(BitArray256);
            var searchIndex = 0;
            var window = default(MatchWindow);

            var a = Step(CommandMode.Contains, 1);
            var b = Step(CommandMode.Contains, 2, InputPhase.Down, maxGapMillis);
            return CommandMatcher.Evaluate(ref a, default, history, ref mask, ref searchIndex, ref window)
                   && CommandMatcher.Evaluate(ref b, default, history, ref mask, ref searchIndex, ref window);
        }

        private DynamicBuffer<InputHistory> History(
            params (byte action, InputPhase phase, uint tick, uint millis)[] entries)
        {
            var entity = Manager.CreateEntity(typeof(InputHistory));
            var buffer = Manager.GetBuffer<InputHistory>(entity);
            foreach (var (action, phase, tick, millis) in entries)
                buffer.Add(new InputHistory { ActionId = action, Phase = phase, Tick = tick, Millis = millis });

            return buffer;
        }

        private static CommandStep Step(CommandMode mode, byte action, InputPhase phase = InputPhase.Down,
            ushort maxGapMillis = 0)
        {
            return new CommandStep { Mode = mode, ActionId = action, Phase = phase, MaxGapMillis = maxGapMillis };
        }

        private static bool EvalOnce(CommandStep step, in DynamicBuffer<InputHistory> history,
            InputState state = default)
        {
            var mask = default(BitArray256);
            var searchIndex = 0;
            var window = default(MatchWindow);
            return CommandMatcher.Evaluate(ref step, state, history, ref mask, ref searchIndex, ref window);
        }
    }
}
