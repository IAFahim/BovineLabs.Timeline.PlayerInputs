using System;
using System.Collections.Generic;
using BovineLabs.Core.Keys;
using BovineLabs.Core.Settings;
using Unity.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BovineLabs.Timeline.PlayerInputs.Data
{
    [SettingsGroup("Input")]
    public sealed class MultiInputSettings : KSettingsBase<MultiInputSettings, byte>
    {
        [SerializeField] private InputActionReference[] inputActions = Array.Empty<InputActionReference>();

        public IReadOnlyList<InputActionReference> InputActions => inputActions;

        public override IEnumerable<NameValue<byte>> Keys
        {
            get
            {
                for (byte i = 0; i < inputActions.Length; i++)
                {
                    var binding = inputActions[i];
                    var name = binding?.action != null ? binding.action.name : $"[Unassigned: {i}]";
                    yield return new NameValue<byte>(name, i);
                }
            }
        }

        public static bool TryGetIndex(InputActionReference reference, out byte index)
        {
            index = 0;
            if (I == null || reference?.action == null) return false;

            for (byte i = 0; i < I.inputActions.Length; i++)
            {
                var input = I.inputActions[i];
                if (input?.action == null || input.action.id != reference.action.id) continue;
                
                index = i;
                return true;
            }

            index = NameToKey((FixedString32Bytes)reference.action.name);
            return true;
        }
    }
}
