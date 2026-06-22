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
                var count = Math.Min(inputActions.Length, 256);
                for (var i = 0; i < count; i++)
                {
                    var id = (byte)i;
                    var binding = inputActions[i];
                    var actionName = binding?.action != null ? binding.action.name : $"[Unassigned: {id}]";
                    yield return new NameValue<byte>(actionName, id);
                }
            }
        }

        public bool TryGet(InputActionReference reference, out byte index)
        {
            index = 0;
            if (reference?.action == null) return false;

            var count = Math.Min(inputActions.Length, 256);

            for (var i = 0; i < count; i++)
            {
                var input = inputActions[i];
                if (input?.action != null && input.action.id == reference.action.id)
                {
                    index = (byte)i;
                    return true;
                }
            }

            for (var i = 0; i < count; i++)
            {
                var input = inputActions[i];
                if (input?.action != null && input.action.name == reference.action.name)
                {
                    index = (byte)i;
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