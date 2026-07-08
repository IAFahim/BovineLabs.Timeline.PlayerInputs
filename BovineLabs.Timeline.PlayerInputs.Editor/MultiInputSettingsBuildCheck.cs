using System.Collections.Generic;
using System.Text;
using BovineLabs.Timeline.PlayerInputs.Data;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace BovineLabs.Timeline.PlayerInputs.Editor
{
    public sealed class MultiInputSettingsBuildCheck : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            var guids = AssetDatabase.FindAssets("t:MultiInputSettings");
            if (guids == null || guids.Length == 0)
            {
                return;
            }

            var path = AssetDatabase.GUIDToAssetPath(guids[0]);
            var settings = AssetDatabase.LoadAssetAtPath<MultiInputSettings>(path);
            if (settings == null)
            {
                return;
            }

            var actions = settings.InputActions;
            var problems = new StringBuilder();

            if (actions.Count > MultiInputSettings.MaxActions)
            {
                problems.Append($"\n- has {actions.Count} actions but only {MultiInputSettings.MaxActions} are usable " +
                                $"(byte {byte.MaxValue} is the reserved sentinel).");
            }

            var seenIds = new Dictionary<string, int>();
            var seenNames = new Dictionary<string, int>();

            for (var i = 0; i < actions.Count; i++)
            {
                var reference = actions[i];
                if (reference == null || reference.action == null)
                {
                    problems.Append($"\n- slot {i} is unassigned; it consumes a byte id and resolves to nothing.");
                    continue;
                }

                var id = reference.action.id.ToString();
                if (seenIds.TryGetValue(id, out var firstById))
                {
                    problems.Append($"\n- slots {firstById} and {i} reference the same action '{reference.action.name}' " +
                                    $"(id {id}); byte ids must map to distinct actions.");
                }
                else
                {
                    seenIds.Add(id, i);
                }

                var name = reference.action.name;
                if (seenNames.TryGetValue(name, out var firstByName))
                {
                    problems.Append($"\n- slots {firstByName} and {i} share the action name '{name}'; " +
                                    "duplicate names break the name->id lookup.");
                }
                else
                {
                    seenNames.Add(name, i);
                }
            }

            if (problems.Length > 0)
            {
                throw new BuildFailedException($"MultiInputSettings ({path}) is misconfigured:{problems}");
            }
        }
    }
}
