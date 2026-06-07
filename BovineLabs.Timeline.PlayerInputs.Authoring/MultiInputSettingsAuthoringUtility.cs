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
            if (settings != null)
            {
                return settings.TryGet(reference, out index);
            }
            index = 0;
            return false;
        }
    }
}
