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

        public override IEnumerable<NameValue<byte>> Keys
        {
            get
            {
                foreach (var mapping in mappings)
                {
                    string actionName = mapping.Action != null && mapping.Action.action != null 
                        ? mapping.Action.action.name 
                        : $"[Unassigned Action ID: {mapping.Value}]";

                    yield return new NameValue<byte>(actionName, mapping.Value);
                }
            }
        }

        public IReadOnlyList<InputMapping> Mappings => mappings;
    }
}