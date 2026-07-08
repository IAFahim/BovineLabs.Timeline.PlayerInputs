using NUnit.Framework;

namespace BovineLabs.Timeline.PlayerInputs.Tests
{
    // Pins the TriggerIfAlreadyHeld level-vs-edge decision on clip activation. The system seeds
    // InputEventsState.WasInputActive from this on the enter frame, then rising = hasInput && !WasInputActive.
    public class InputEventsHeldSeedTests
    {
        [Test]
        public void TriggerIfAlreadyHeld_True_HeldOnEnter_FiresStart()
        {
            // Seed false -> rising edge = held && !false = true -> OnInputStart fires on frame 1 (legacy behaviour).
            var seed = InputEventsLogic.SeedWasInputActive(triggerIfAlreadyHeld: true, hasInput: true);
            Assert.IsFalse(seed);
            Assert.IsTrue(RisingEdge(hasInput: true, wasActive: seed), "already-held input fires OnInputStart");
        }

        [Test]
        public void TriggerIfAlreadyHeld_False_HeldOnEnter_DoesNotFireStart()
        {
            // Seed true -> rising edge = held && !true = false -> no OnInputStart until a fresh press.
            var seed = InputEventsLogic.SeedWasInputActive(triggerIfAlreadyHeld: false, hasInput: true);
            Assert.IsTrue(seed);
            Assert.IsFalse(RisingEdge(hasInput: true, wasActive: seed),
                "already-held input is treated as already started");
        }

        [Test]
        public void EitherMode_NotHeldOnEnter_SeedsInactive()
        {
            Assert.IsFalse(InputEventsLogic.SeedWasInputActive(triggerIfAlreadyHeld: true, hasInput: false));
            Assert.IsFalse(InputEventsLogic.SeedWasInputActive(triggerIfAlreadyHeld: false, hasInput: false));
        }

        [Test]
        public void TriggerIfAlreadyHeld_False_FreshPressAfterEnter_FiresStart()
        {
            // Enter frame: not held -> seed inactive. Next frame the player presses -> rising edge fires.
            var seed = InputEventsLogic.SeedWasInputActive(triggerIfAlreadyHeld: false, hasInput: false);
            Assert.IsTrue(RisingEdge(hasInput: true, wasActive: seed), "a fresh press still fires OnInputStart");
        }

        private static bool RisingEdge(bool hasInput, bool wasActive)
        {
            return hasInput && !wasActive;
        }
    }
}
