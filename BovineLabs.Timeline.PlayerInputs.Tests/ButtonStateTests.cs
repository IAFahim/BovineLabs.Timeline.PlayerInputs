using BovineLabs.Timeline.PlayerInputs.Data;
using NUnit.Framework;

namespace BovineLabs.Timeline.PlayerInputs.Tests
{
    public class ButtonStateTests
    {
        [Test]
        public void Started_SetsDownAndPressed()
        {
            var state = default(ButtonState);

            state.Started();

            Assert.IsTrue(state.Down);
            Assert.IsTrue(state.Pressed);
            Assert.IsFalse(state.Up);
        }

        [Test]
        public void Cancelled_SetsPressedFalseAndUpTrue()
        {
            var state = default(ButtonState);
            state.Started();

            state.Cancelled();

            Assert.IsFalse(state.Pressed);
            Assert.IsTrue(state.Up);
        }

        [Test]
        public void Reset_ClearsDownAndUp_WithoutChangingPressed()
        {
            var state = default(ButtonState);
            state.Started();

            state.Reset();

            Assert.IsFalse(state.Down);
            Assert.IsFalse(state.Up);
            Assert.IsTrue(state.Pressed);
        }

        [Test]
        public void Reset_AfterCancelled_LeavesAllEdgesClearAndNotPressed()
        {
            var state = default(ButtonState);
            state.Started();
            state.Cancelled();

            state.Reset();

            Assert.IsFalse(state.Down);
            Assert.IsFalse(state.Up);
            Assert.IsFalse(state.Pressed);
        }
    }
}
