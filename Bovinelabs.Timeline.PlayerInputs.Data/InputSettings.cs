using System;
using System.Collections.Generic;
using BovineLabs.Core.Keys;
using BovineLabs.Core.Settings;
using Unity.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Bovinelabs.Timeline.PlayerInputs.Data
{
    [SettingsGroup("Input")]
    public class InputSettings : KSettingsBase<InputSettings, byte>
    {
        [SerializeField] private InputActionReference[] inputActionReferences = Array.Empty<InputActionReference>();

        public override IEnumerable<NameValue<byte>> Keys
        {
            get
            {
                for (byte index = 0; index < inputActionReferences.Length; index++)
                {
                    var mapping = inputActionReferences[index];
                    var actionName = mapping != null && mapping.action != null
                        ? mapping.action.name
                        : $"[Unassigned Action ID: {index}]";

                    yield return new NameValue<byte>(actionName, index);
                }
            }
        }

        public IReadOnlyList<InputActionReference> InputActionReferences => inputActionReferences;

        public static byte GetIndex(InputActionReference inputActionReference)
        {
            return NameToKey((FixedString32Bytes)inputActionReference.action.name);
        }
    }
}