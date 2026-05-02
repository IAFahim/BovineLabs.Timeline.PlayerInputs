using System;
using System.Collections.Generic;
using BovineLabs.Core.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BovineLabs.Timeline.PlayerInputs.Data
{
    [RequireComponent(typeof(PlayerInput))]
    public sealed class PlayerInputBridge : MonoBehaviour
    {
        public int PlayerIdOverride = -1;
        public BitArray256 CurrentPressed;
        public readonly List<InputAxis> CurrentAxes = new(16);

        private readonly List<(byte Id, InputAction Action)> axes = new();
        private readonly List<(byte Id, InputAction Action)> buttons = new();

        private World world;
        private EntityManager manager;
        private Entity provider;
        private bool initialized;

        private void Update()
        {
            if (provider == Entity.Null && initialized) TryCreateProvider(out provider);

            CurrentPressed = default;
            foreach (var btn in buttons)
                if (btn.Action.WasPerformedThisFrame())  // was IsPressed()
                    CurrentPressed[btn.Id] = true;

            CurrentAxes.Clear();
            foreach (var axis in axes)
            {
                var isVec2 = axis.Action.expectedControlType == "Vector2";
                var val = isVec2 ? (float2)axis.Action.ReadValue<Vector2>() : new float2(axis.Action.ReadValue<float>(), 0f);
                
                if (math.lengthsq(val) > 0.0001f)
                    CurrentAxes.Add(new InputAxis { ActionId = axis.Id, Value = val });
            }
        }

        private void OnEnable()
        {
            var playerInput = GetComponent<PlayerInput>();
            if (playerInput.actions == null || MultiInputSettings.I == null) return;

            buttons.Clear();
            axes.Clear();
            CurrentAxes.Clear();
            CurrentPressed = default;

            for (byte i = 0; i < MultiInputSettings.I.InputActions.Count; i++)
            {
                var binding = MultiInputSettings.I.InputActions[i];
                if (!TryFindAction(playerInput, binding.Input, out var action)) continue;

                if (action.type == InputActionType.Button) buttons.Add((i, action));
                else if (action.type == InputActionType.Value) axes.Add((i, action));
            }

            initialized = true;
            TryCreateProvider(out provider);
        }

        private void OnDisable()
        {
            if (world != null && world.IsCreated && manager.Exists(provider))
                manager.DestroyEntity(provider);

            provider = Entity.Null;
            world = null;
            initialized = false;
        }

        private bool TryCreateProvider(out Entity entity)
        {
            entity = Entity.Null;
            world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return false;

            manager = world.EntityManager;
            entity = manager.CreateEntity();

            manager.AddComponentData(entity, new PlayerId { Value = GetPlayerId() });
            manager.AddComponent<ProviderTag>(entity);
            manager.AddComponent<InputState>(entity);
            manager.AddBuffer<InputAxis>(entity);
            manager.AddComponentObject(entity, new PlayerInputBridgeComponent { Value = this });

            return true;
        }

        private static bool TryFindAction(PlayerInput input, InputActionReference reference, out InputAction action)
        {
            action = null;
            if (reference?.action == null) return false;
            
            action = input.actions.FindAction(reference.action.id);
            return action != null;
        }

        private byte GetPlayerId() => PlayerIdOverride >= 0 ? (byte)PlayerIdOverride : (byte)(GetComponent<PlayerInput>()?.playerIndex ?? 0);
    }

    public sealed class PlayerInputBridgeComponent : IComponentData, IEquatable<PlayerInputBridgeComponent>, ICloneable
    {
        public PlayerInputBridge Value;

        public object Clone() => new PlayerInputBridgeComponent { Value = Value };
        public bool Equals(PlayerInputBridgeComponent other) => !ReferenceEquals(null, other) && (ReferenceEquals(this, other) || Equals(Value, other.Value));
        public override bool Equals(object obj) => ReferenceEquals(this, obj) || (obj is PlayerInputBridgeComponent other && Equals(other));
        public override int GetHashCode() => Value?.GetHashCode() ?? 0;
    }
}
