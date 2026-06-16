using BovineLabs.Core.Authoring.Settings;
using BovineLabs.Timeline.PlayerInputs.Data;
using UnityEngine.InputSystem;

namespace BovineLabs.Timeline.PlayerInputs.Authoring
{
    public static class MultiInputSettingsAuthoringUtility
    {
        public static bool TryGetIndex(InputActionReference reference, out byte index)
        {
            var settings = AuthoringSettingsUtility.GetSettings<MultiInputSettings>();
            if (settings != null && settings.TryGet(reference, out index))
            {
                // index 255 (byte.MaxValue) is reserved as the unresolved-action sentinel; an action
                // landing there (only possible at exactly 256 actions) would collide with it, so fail
                // closed rather than alias the sentinel. No effect for any project with <256 actions.
                return index != byte.MaxValue;
            }

            index = 0;
            return false;
        }
    }
}