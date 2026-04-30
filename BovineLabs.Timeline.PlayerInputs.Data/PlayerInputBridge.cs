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

        public BitArray256 CurrentHeld;
        internal readonly List<(byte Id, InputAction Action)> Axes = new();
        internal readonly List<(byte Id, InputAction Action)> Buttons = new();

        private World capturedWorld;
        public List<InputAxisBuffer> CurrentAxes = new(16);
        private EntityManager entityManager;
        private Entity providerEntity;
        private bool bindingsInitialized;

        private void Update()
        {
            if (providerEntity == Entity.Null && bindingsInitialized)
                TryCreateProviderEntity();

            CurrentHeld = default;
            foreach (var btn in Buttons)
                if (btn.Action.IsPressed())
                    CurrentHeld[btn.Id] = true;

            CurrentAxes.Clear();
            foreach (var axis in Axes)
            {
                float2 val;
                if (axis.Action.expectedControlType == "Vector2")
                {
                    var v2 = axis.Action.ReadValue<Vector2>();
                    val = new float2(v2.x, v2.y);
                }
                else
                {
                    val = new float2(axis.Action.ReadValue<float>(), 0f);
                }

                if (math.lengthsq(val) > 0.0001f)
                    CurrentAxes.Add(new InputAxisBuffer { ActionId = axis.Id, Value = val });
            }
        }

        private void OnEnable()
        {
            var playerInput = GetComponent<PlayerInput>();
            if (playerInput.actions == null) return;

            var inputSettings = MultiInputSettings.I;
            if (inputSettings == null || inputSettings.InputActions.Count == 0) return;

            Buttons.Clear();
            Axes.Clear();
            CurrentAxes.Clear();
            CurrentHeld = default;

            var actionCount = inputSettings.InputActions.Count;
            if (Buttons.Capacity < actionCount) Buttons.Capacity = actionCount;
            if (Axes.Capacity < actionCount) Axes.Capacity = actionCount;
            if (CurrentAxes.Capacity < actionCount) CurrentAxes.Capacity = actionCount;

            for (byte index = 0; index < inputSettings.InputActions.Count; index++)
            {
                var binding = inputSettings.InputActions[index];
                if (!TryFindAction(playerInput, binding.Input, out var action)) continue;

                switch (action.type)
                {
                    case InputActionType.Button:
                        Buttons.Add((index, action));
                        break;

                    case InputActionType.Value:
                        Axes.Add((index, action));
                        break;
                }
            }

            bindingsInitialized = true;
            TryCreateProviderEntity();
        }

        private void TryCreateProviderEntity()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;

            capturedWorld = world;
            entityManager = world.EntityManager;
            providerEntity = entityManager.CreateEntity();

            entityManager.AddComponentData(providerEntity, new PlayerId { Value = GetPlayerId() });
            entityManager.AddComponent<InputProviderTag>(providerEntity);
            entityManager.AddComponentObject(providerEntity, new PlayerInputBridgeComponent { Value = this });

            entityManager.AddComponent<InputState>(providerEntity);
            entityManager.AddBuffer<InputAxisBuffer>(providerEntity);
            entityManager.AddBuffer<InputHistory>(providerEntity);
            entityManager.AddComponentData(providerEntity, new InputHistoryState { Head = 0 });
            entityManager.AddComponent<EventsDirty>(providerEntity);
            entityManager.SetComponentEnabled<EventsDirty>(providerEntity, false);
        }

        private void OnDisable()
        {
            if (capturedWorld != null && capturedWorld.IsCreated && entityManager.Exists(providerEntity))
                entityManager.DestroyEntity(providerEntity);

            providerEntity = Entity.Null;
            capturedWorld = null;
            bindingsInitialized = false;
            Buttons.Clear();
            Axes.Clear();
            CurrentAxes.Clear();
            CurrentHeld = default;
        }

        private static bool TryFindAction(PlayerInput playerInput, InputActionReference reference,
            out InputAction action)
        {
            action = null;

            if (reference == null || reference.action == null) return false;

            action = playerInput.actions.FindAction(reference.action.id);
            return action != null;
        }

        public byte GetPlayerId()
        {
            return PlayerIdOverride >= 0
                ? (byte)PlayerIdOverride
                : (byte)(GetComponent<PlayerInput>()?.playerIndex ?? 0);
        }
    }

    public sealed class PlayerInputBridgeComponent : IComponentData, IEquatable<PlayerInputBridgeComponent>, ICloneable
    {
        public PlayerInputBridge Value;

        public object Clone()
        {
            return new PlayerInputBridgeComponent { Value = Value };
        }

        public bool Equals(PlayerInputBridgeComponent other)
        {
            return !ReferenceEquals(null, other) && (ReferenceEquals(this, other) || Equals(Value, other.Value));
        }

        public override bool Equals(object obj)
        {
            return ReferenceEquals(this, obj) || (obj is PlayerInputBridgeComponent other && Equals(other));
        }

        public override int GetHashCode()
        {
            return Value != null ? Value.GetHashCode() : 0;
        }
    }
}