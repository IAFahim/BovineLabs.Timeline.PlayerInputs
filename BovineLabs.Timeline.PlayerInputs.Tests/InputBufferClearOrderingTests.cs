using System.Linq;
using NUnit.Framework;
using Unity.Entities;

namespace BovineLabs.Timeline.PlayerInputs.Tests
{
    // Regression for the buffer-clear ordering fix (High TODO #1): the Clear clip must take effect BEFORE this
    // frame's history is recorded and matched, so stale buffered inputs cannot fire a sequence on the very frame
    // a Clear clip opens (the input-buffer-dormant incident class). A manual system.Update() ignores ordering
    // attributes, so the ordering is pinned directly on the attributes — a revert to the pre-fix
    // [UpdateAfter(CommandSequenceSystem)] placement flips these and fails the test.
    public class InputBufferClearOrderingTests
    {
        [Test]
        public void ClearSystem_RunsAfterMask_AndBeforeHistory_HenceBeforeMatching()
        {
            var clearSystem = typeof(InputBufferClearSystem);

            var after = clearSystem.GetCustomAttributes(typeof(UpdateAfterAttribute), false)
                .Cast<UpdateAfterAttribute>().Select(a => a.SystemType).ToArray();
            var before = clearSystem.GetCustomAttributes(typeof(UpdateBeforeAttribute), false)
                .Cast<UpdateBeforeAttribute>().Select(a => a.SystemType).ToArray();

            // Runs after the mask is built for the frame (so it only wipes already-recorded, stale history).
            Assert.Contains(typeof(ConsumerBufferMaskSystem), after,
                "clear must run after ConsumerBufferMaskSystem");

            // Runs before history is recorded — which itself runs before CommandSequenceSystem matches, so a
            // Clear on frame N wins the race against a stale-input sequence match on frame N.
            Assert.Contains(typeof(ConsumerHistorySystem), before,
                "clear must run before ConsumerHistorySystem (and thus before CommandSequenceSystem matches)");

            // The pre-fix bug ran the clear AFTER matching; assert it is not scheduled after the sequence system.
            Assert.IsFalse(after.Contains(typeof(CommandSequenceSystem)),
                "clear must not run after CommandSequenceSystem (the stale-input race this fix closed)");
        }
    }
}
