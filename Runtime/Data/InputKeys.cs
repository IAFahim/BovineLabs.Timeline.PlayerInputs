using System;
using System.Collections.Generic;
using BovineLabs.Core.Keys;
using BovineLabs.Core.Settings;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PlayerInputs.Data
{
    [SettingsGroup("Input")]
    public class InputKeys : KSettingsBase<InputKeys, byte>
    {
        [Serializable]
        public struct InputMapping
        {
            [Tooltip("The ECS byte ID used under the hood.")]
            public byte Value;
            
            [Tooltip("The Unity Input Action to bind to this ID. The name is extracted automatically.")]
            public InputActionReference Action;
        }

        [SerializeField]
        private InputMapping[] mappings = Array.Empty<InputMapping>();

        // Fulfills KSettingsBase: Automatically registers these into the K dropdown system
        public override IEnumerable<NameValue<byte>> Keys
        {
            get
            {
                foreach (var mapping in mappings)
                {
                    // Dynamically grab the name straight from the Unity Input Action!
                    // Fallback to an error string if the user forgot to assign the reference.
                    string actionName = mapping.Action != null && mapping.Action.action != null 
                        ? mapping.Action.action.name 
                        : $"[Unassigned Action ID: {mapping.Value}]";

                    yield return new NameValue<byte>(actionName, mapping.Value);
                }
            }
        }

        // Expose the raw mappings for the runtime bridge to read
        public IReadOnlyList<InputMapping> Mappings => mappings;
    }
}