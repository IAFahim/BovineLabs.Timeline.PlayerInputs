using BovineLabs.Timeline.PlayerInputs.Data;
using NUnit.Framework;

namespace BovineLabs.Timeline.PlayerInputs.Tests
{
    public class OverrideDecisionTests
    {
        [Test]
        public void AnyInput_TrueWhenDownSet()
        {
            Assert.IsTrue(OverrideDecision.IsActive(OverrideTrigger.AnyInput, true, false, false, false));
        }

        [Test]
        public void AnyInput_TrueWhenHeldSet()
        {
            Assert.IsTrue(OverrideDecision.IsActive(OverrideTrigger.AnyInput, false, true, false, false));
        }

        [Test]
        public void AnyInput_FalseWhenBothEmpty()
        {
            Assert.IsFalse(OverrideDecision.IsActive(OverrideTrigger.AnyInput, false, false, true, true));
        }

        [Test]
        public void Action_KeysOnlyOnActionFlags()
        {
            Assert.IsTrue(OverrideDecision.IsActive(OverrideTrigger.Action, false, false, true, false));
            Assert.IsTrue(OverrideDecision.IsActive(OverrideTrigger.Action, false, false, false, true));
            Assert.IsFalse(OverrideDecision.IsActive(OverrideTrigger.Action, true, true, false, false));
        }

        [Test]
        public void Manual_AlwaysFalse()
        {
            Assert.IsFalse(OverrideDecision.IsActive(OverrideTrigger.Manual, true, true, true, true));
        }

        [Test]
        public void UnknownTrigger_False()
        {
            Assert.IsFalse(OverrideDecision.IsActive((OverrideTrigger)99, true, true, true, true));
        }

        [Test]
        public void Step_ActiveEngagesAndResetsIdle()
        {
            OverrideDecision.Step(true, false, 5f, 2f, 1f, out var driving, out var idle);
            Assert.IsTrue(driving);
            Assert.AreEqual(0f, idle);
        }

        [Test]
        public void Step_InactiveNotDriving_StaysReleased()
        {
            OverrideDecision.Step(false, false, 1.5f, 2f, 1f, out var driving, out var idle);
            Assert.IsFalse(driving);
            Assert.AreEqual(1.5f, idle);
        }

        [Test]
        public void Step_ReleaseIdleZero_HoldsDrivingForever()
        {
            OverrideDecision.Step(false, true, 100f, 0f, 1f, out var driving, out var idle);
            Assert.IsTrue(driving);
            Assert.AreEqual(100f, idle);
        }

        [Test]
        public void Step_InactiveAccumulatesIdleByDelta()
        {
            OverrideDecision.Step(false, true, 0.5f, 2f, 0.25f, out var driving, out var idle);
            Assert.IsTrue(driving);
            Assert.AreEqual(0.75f, idle, 1e-6f);
        }

        [Test]
        public void Step_ReleasesAtThresholdAndResetsIdle()
        {
            OverrideDecision.Step(false, true, 1.5f, 2f, 0.5f, out var driving, out var idle);
            Assert.IsFalse(driving);
            Assert.AreEqual(0f, idle);
        }

        [Test]
        public void Step_JustBelowThreshold_HoldsDriving()
        {
            OverrideDecision.Step(false, true, 1.0f, 2f, 0.999f, out var driving, out var idle);
            Assert.IsTrue(driving);
            Assert.AreEqual(1.999f, idle, 1e-6f);
        }
    }
}
