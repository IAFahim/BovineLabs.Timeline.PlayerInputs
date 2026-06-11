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
            var history = History((5, InputPhase.Down, 0));
            Assert.IsTrue(EvalOnce(Step(CommandMode.Contains, 5), history));
        }

        [Test]
        public void Contains_DifferentAction_ReturnsFalse()
        {
            var history = History((5, InputPhase.Down, 0));
            Assert.IsFalse(EvalOnce(Step(CommandMode.Contains, 7), history));
        }

        [Test]
        public void Contains_DifferentPhase_ReturnsFalse()
        {
            var history = History((5, InputPhase.Down, 0));
            Assert.IsFalse(EvalOnce(Step(CommandMode.Contains, 5, InputPhase.Up), history));
        }

        [Test]
        public void Consume_MarksHistory_SecondMatchOnSameMaskFails()
        {
            var history = History((5, InputPhase.Down, 0));
            var step = Step(CommandMode.Consume, 5);

            var mask = default(BitArray256);
            var searchIndex = 0;
            var lastTick = uint.MaxValue;
            var first = step;
            Assert.IsTrue(CommandMatcher.Evaluate(ref first, default, history, ref mask, ref searchIndex, ref lastTick));

            searchIndex = 0;
            lastTick = uint.MaxValue;
            var second = step;
            Assert.IsFalse(CommandMatcher.Evaluate(ref second, default, history, ref mask, ref searchIndex,
                ref lastTick));
        }

        [Test]
        public void WithinWindow_GapExceedsMaxGapTicks_Fails()
        {
            var history = History((1, InputPhase.Down, 0), (2, InputPhase.Down, 10));
            var mask = default(BitArray256);
            var searchIndex = 0;
            var lastTick = uint.MaxValue;

            var first = Step(CommandMode.Contains, 1);
            Assert.IsTrue(CommandMatcher.Evaluate(ref first, default, history, ref mask, ref searchIndex, ref lastTick));

            var second = Step(CommandMode.Contains, 2, InputPhase.Down, 5);
            Assert.IsFalse(CommandMatcher.Evaluate(ref second, default, history, ref mask, ref searchIndex,
                ref lastTick));
        }

        [Test]
        public void WithinWindow_GapInsideMaxGapTicks_Succeeds()
        {
            var history = History((1, InputPhase.Down, 0), (2, InputPhase.Down, 3));
            var mask = default(BitArray256);
            var searchIndex = 0;
            var lastTick = uint.MaxValue;

            var first = Step(CommandMode.Contains, 1);
            CommandMatcher.Evaluate(ref first, default, history, ref mask, ref searchIndex, ref lastTick);

            var second = Step(CommandMode.Contains, 2, InputPhase.Down, 5);
            Assert.IsTrue(CommandMatcher.Evaluate(ref second, default, history, ref mask, ref searchIndex,
                ref lastTick));
        }

        [Test]
        public void OrderedConsume_InOrderSucceeds()
        {
            var history = History((1, InputPhase.Down, 0), (2, InputPhase.Down, 1));
            var mask = default(BitArray256);
            var searchIndex = 0;
            var lastTick = uint.MaxValue;

            var a = Step(CommandMode.OrderedConsume, 1);
            var b = Step(CommandMode.OrderedConsume, 2);
            Assert.IsTrue(CommandMatcher.Evaluate(ref a, default, history, ref mask, ref searchIndex, ref lastTick));
            Assert.IsTrue(CommandMatcher.Evaluate(ref b, default, history, ref mask, ref searchIndex, ref lastTick));
        }

        [Test]
        public void OrderedConsume_OutOfOrderFails()
        {
            var history = History((1, InputPhase.Down, 0), (2, InputPhase.Down, 1));
            var mask = default(BitArray256);
            var searchIndex = 0;
            var lastTick = uint.MaxValue;

            var b = Step(CommandMode.OrderedConsume, 2);
            var a = Step(CommandMode.OrderedConsume, 1);
            Assert.IsTrue(CommandMatcher.Evaluate(ref b, default, history, ref mask, ref searchIndex, ref lastTick));
            Assert.IsFalse(CommandMatcher.Evaluate(ref a, default, history, ref mask, ref searchIndex, ref lastTick));
        }

        [Test]
        public void NotContains_AbsentTrue_PresentFalse()
        {
            var history = History((5, InputPhase.Down, 0));
            Assert.IsTrue(EvalOnce(Step(CommandMode.NotContains, 7), history));
            Assert.IsFalse(EvalOnce(Step(CommandMode.NotContains, 5), history));
        }

        [Test]
        public void None_ProbesLiveInputState()
        {
            var history = History();
            var state = default(InputState);
            state.Down[5] = true;

            Assert.IsTrue(EvalOnce(Step(CommandMode.None, 5, InputPhase.Down), history, state));
            Assert.IsFalse(EvalOnce(Step(CommandMode.None, 6, InputPhase.Down), history, state));
        }

        private DynamicBuffer<InputHistory> History(params (byte action, InputPhase phase, uint tick)[] entries)
        {
            var entity = Manager.CreateEntity(typeof(InputHistory));
            var buffer = Manager.GetBuffer<InputHistory>(entity);
            foreach (var (action, phase, tick) in entries)
            {
                buffer.Add(new InputHistory { ActionId = action, Phase = phase, Tick = tick });
            }

            return buffer;
        }

        private static CommandStep Step(CommandMode mode, byte action, InputPhase phase = InputPhase.Down,
            ushort maxGapTicks = 0)
        {
            return new CommandStep { Mode = mode, ActionId = action, Phase = phase, MaxGapTicks = maxGapTicks };
        }

        private static bool EvalOnce(CommandStep step, in DynamicBuffer<InputHistory> history,
            InputState state = default)
        {
            var mask = default(BitArray256);
            var searchIndex = 0;
            var lastTick = uint.MaxValue;
            return CommandMatcher.Evaluate(ref step, state, history, ref mask, ref searchIndex, ref lastTick);
        }
    }
}
