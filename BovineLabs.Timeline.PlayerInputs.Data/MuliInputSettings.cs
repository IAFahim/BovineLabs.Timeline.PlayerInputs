using System;
using System.Collections.Generic;
using BovineLabs.Core.Keys;
using BovineLabs.Core.Settings;
using BovineLabs.Reaction.Data.Conditions;
using Unity.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BovineLabs.Timeline.PlayerInputs.Data
{
    [SettingsGroup("Input")]
    public class MuliInputSettings : KSettingsBase<MuliInputSettings, byte>
    {
        [SerializeField] private InputActionBinding[] inputActions = Array.Empty<InputActionBinding>();

        public IReadOnlyList<InputActionBinding> InputActions => inputActions;

        public override IEnumerable<NameValue<byte>> Keys
        {
            get
            {
                for (byte index = 0; index < inputActions.Length; index++)
                {
                    var binding = inputActions[index];
                    var actionName = binding.Input != null && binding.Input.action != null
                        ? binding.Input.action.name
                        : $"[Unassigned Action ID: {index}]";

                    yield return new NameValue<byte>(actionName, index);
                }
            }
        }

        public static byte GetIndex(InputActionReference inputActionReference)
        {
            if (TryGetIndex(inputActionReference, out var index)) return index;

            return NameToKey((FixedString32Bytes)inputActionReference.action.name);
        }

        public static bool TryGetIndex(InputActionReference inputActionReference, out byte index)
        {
            index = 0;

            if (I == null || inputActionReference == null || inputActionReference.action == null) return false;

            for (byte i = 0; i < I.inputActions.Length; i++)
            {
                var input = I.inputActions[i].Input;
                if (input == null || input.action == null) continue;

                if (input.action.id != inputActionReference.action.id) continue;

                index = i;
                return true;
            }

            return false;
        }

        [Serializable]
        public class InputActionBinding
        {
            public InputActionReference Input;
            public InputConditionEventBinding[] Events = Array.Empty<InputConditionEventBinding>();
        }

        [Serializable]
        public class InputConditionEventBinding
        {
            public InputPhase Phase = InputPhase.Down;
            public ConditionKey Condition;
            public int Value = 1;

            public bool TryBake(byte actionId, out InputToConditionEvent result)
            {
                result = default;

                if (Condition == null) return false;

                result = new InputToConditionEvent
                {
                    ActionId = actionId,
                    Phase = Phase,
                    Condition = Condition,
                    Value = Value
                };

                return true;
            }
        }
    }
}