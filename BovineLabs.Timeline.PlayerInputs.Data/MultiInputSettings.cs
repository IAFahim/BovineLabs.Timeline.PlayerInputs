using System;
using System.Collections.Generic;
using BovineLabs.Core.Keys;
using BovineLabs.Core.Settings;
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
                    var actionName = binding?.action != null ? binding.action.name : $"[Unassigned: {i}]";
                    yield return new NameValue<byte>(actionName, i);
                }
            }
        }

        public bool TryGet(InputActionReference reference, out byte index)
        {
            index = 0;
            if (reference?.action == null) return false;

            for (byte i = 0; i < inputActions.Length; i++)
            {
                var input = inputActions[i];
                if (input?.action != null && input.action.id == reference.action.id)
                {
                    index = i;
                    return true;
                }
            }

            for (byte i = 0; i < inputActions.Length; i++)
            {
                var input = inputActions[i];
                if (input?.action != null && input.action.name == reference.action.name)
                {
                    index = i;
                    return true;
                }
            }

            return false;
        }

        public static bool TryGetIndex(InputActionReference reference, out byte index)
        {
            if (I != null) return I.TryGet(reference, out index);
            index = 0;
            return false;
        }
    }
}