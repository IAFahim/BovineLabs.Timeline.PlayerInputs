#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BovineLabs.Timeline.PlayerInputs.Data
{
    public sealed partial class MultiInputSettings
    {
        // Unity calls OnValidate DURING deserialization/import/domain-reload. At that point the
        // InputActionReference elements (and their .action lookups) are not guaranteed to be resolved:
        // the referenced .inputactions asset can still be pending import, so every slot transiently reads
        // as null. Validating synchronously therefore spams false "slot N is unassigned" errors on a
        // perfectly valid asset on every reload. Defer to delayCall, by which point the AssetDatabase has
        // settled and references resolve correctly. Genuine problems (truly-empty slots, duplicate ids/names,
        // overflow) still surface; the transient import race no longer does. delayCall is coalesced (-= then
        // +=) so repeated OnValidate calls queue at most one validation pass.
        private void OnValidate()
        {
            EditorApplication.delayCall -= this.Validate;
            EditorApplication.delayCall += this.Validate;
        }

        private void Validate()
        {
            if (this == null)
            {
                // The asset was destroyed or reimported before the deferred callback ran.
                return;
            }

            if (inputActions == null)
            {
                return;
            }

            if (inputActions.Length > MaxActions)
            {
                Debug.LogError(
                    $"MultiInputSettings has {inputActions.Length} actions but only {MaxActions} are usable " +
                    $"(byte {byte.MaxValue} is reserved as the unresolved sentinel). " +
                    $"Remove {inputActions.Length - MaxActions} action(s); slots {MaxActions}+ never resolve.",
                    this);
            }

            var seenIds = new Dictionary<string, int>();
            var seenNames = new Dictionary<string, int>();
            var count = inputActions.Length < MaxActions ? inputActions.Length : MaxActions;

            for (var i = 0; i < count; i++)
            {
                var reference = inputActions[i];
                if (reference == null || reference.action == null)
                {
                    Debug.LogError(
                        $"MultiInputSettings slot {i} is unassigned; it consumes a byte id and resolves to nothing. " +
                        "Assign an action or remove the slot.",
                        this);
                    continue;
                }

                var id = reference.action.id.ToString();
                if (seenIds.TryGetValue(id, out var firstById))
                {
                    Debug.LogError(
                        $"MultiInputSettings slots {firstById} and {i} reference the same action " +
                        $"'{reference.action.name}' (id {id}); byte ids must map to distinct actions.",
                        this);
                }
                else
                {
                    seenIds.Add(id, i);
                }

                var name = reference.action.name;
                if (seenNames.TryGetValue(name, out var firstByName))
                {
                    Debug.LogError(
                        $"MultiInputSettings slots {firstByName} and {i} share the action name '{name}'; " +
                        "duplicate names break the name->id lookup. Keep action names unique within the registry.",
                        this);
                }
                else
                {
                    seenNames.Add(name, i);
                }
            }
        }
    }
}
#endif
