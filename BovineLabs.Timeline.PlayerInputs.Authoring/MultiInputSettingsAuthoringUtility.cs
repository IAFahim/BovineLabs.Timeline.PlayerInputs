using BovineLabs.Core.Authoring.Settings;
using BovineLabs.Timeline.EntityLinks.Authoring;
using BovineLabs.Timeline.PlayerInputs.Data;
using Unity.Entities;
using UnityEngine;
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

        public static bool RequireLink(EntityLinkSchema schema, UnityEngine.Object context, string clipName, string field)
        {
            if (schema != null && EntityLinkAuthoringUtility.TryGetKey(schema, out _)) return true;
            Debug.LogError($"{clipName}: '{field}' is unassigned or unregistered; the clip resolves no consumer. Clip will be skipped.", context);
            return false;
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