using BovineLabs.Core.Authoring.Settings;
using BovineLabs.Timeline.PlayerInputs.Data;
using Unity.Entities;
using UnityEngine.InputSystem;

namespace BovineLabs.Timeline.PlayerInputs.Authoring
{
    public static class MultiInputSettingsAuthoringUtility
    {
        public static void DependsOnSettings(IBaker baker)
        {
            if (AuthoringSettingsUtility.TryGetSettings<MultiInputSettings>(out var settings) && settings != null)
                baker.DependsOn(settings);
        }

        public static bool TryGetIndex(InputActionReference reference, out byte index)
        {
            if (AuthoringSettingsUtility.TryGetSettings<MultiInputSettings>(out var settings) && settings != null &&
                settings.TryGet(reference, out index)) return index != byte.MaxValue;

            index = 0;
            return false;
        }
    }
}