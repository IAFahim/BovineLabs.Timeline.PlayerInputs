using NUnit.Framework;

namespace BovineLabs.Timeline.PlayerInputs.Tests
{
    // Pins the InputEventsSystem deactivate-edge semantics: OnInputEnd fires EXACTLY once when a clip that had an
    // active input turns off, and a lingering ClipActivePrevious frame cannot re-fire it. The system's
    // DeactivateJob routes through InputEventsLogic.ConsumeDeactivateEnd, so testing the pure helper pins the
    // fires-once behaviour without the ECS/link/event-dispatch scaffolding.
    public class InputEventsDeactivateTests
    {
        [Test]
        public void Deactivate_WhileInputActive_FiresEndOnce_ThenNeverAgain()
        {
            // After an active frame with a held/active input the system leaves WasInputActive = true.
            var wasInputActive = true;

            // First deactivate frame: the end event fires and the latch clears.
            Assert.IsTrue(InputEventsLogic.ConsumeDeactivateEnd(ref wasInputActive),
                "OnInputEnd fires on the clip deactivate edge while input was active");
            Assert.IsFalse(wasInputActive, "latch cleared so the end event cannot re-fire");

            // Subsequent deactivate frames (ClipActivePrevious lingering) must NOT fire OnInputEnd again.
            Assert.IsFalse(InputEventsLogic.ConsumeDeactivateEnd(ref wasInputActive), "no duplicate OnInputEnd");
            Assert.IsFalse(InputEventsLogic.ConsumeDeactivateEnd(ref wasInputActive), "still no duplicate OnInputEnd");
        }

        [Test]
        public void Deactivate_WhenInputWasNotActive_DoesNotFireEnd()
        {
            // Clip ends while no input was active: there is no press to close, so no OnInputEnd.
            var wasInputActive = false;

            Assert.IsFalse(InputEventsLogic.ConsumeDeactivateEnd(ref wasInputActive),
                "no OnInputEnd when nothing was active");
            Assert.IsFalse(wasInputActive);
        }

        [Test]
        public void FullLifecycle_HeldThroughDeactivate_EndsExactlyOnce()
        {
            // Enter frame with default TriggerIfAlreadyHeld and a held input seeds WasInputActive = false.
            var wasInputActive = InputEventsLogic.SeedWasInputActive(triggerIfAlreadyHeld: true, hasInput: true);
            Assert.IsFalse(wasInputActive);

            // Each active frame the GatherJob ends by writing WasInputActive = hasInput (still held => true).
            wasInputActive = true;

            // Clip deactivates while still held: OnInputEnd fires once, and only once.
            Assert.IsTrue(InputEventsLogic.ConsumeDeactivateEnd(ref wasInputActive));
            Assert.IsFalse(InputEventsLogic.ConsumeDeactivateEnd(ref wasInputActive));
        }
    }
}
