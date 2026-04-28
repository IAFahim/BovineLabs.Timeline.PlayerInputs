using System;
using System.Collections.Generic;
using BovineLabs.Core.Collections;
using BovineLabs.Reaction.Data.Conditions;
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
        public List<InputAxisBuffer> CurrentAxes = new();
        private EntityManager entityManager;
        private Entity providerEntity;

        private void Update()
        {
            CurrentHeld = new BitArray256();
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
            CurrentHeld = new BitArray256();

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

            capturedWorld = World.DefaultGameObjectInjectionWorld;
            if (capturedWorld == null) return;

            entityManager = capturedWorld.EntityManager;
            providerEntity = entityManager.CreateEntity();

            entityManager.AddComponentData(providerEntity, new PlayerId { Value = GetPlayerId() });
            entityManager.AddComponent<InputProviderTag>(providerEntity);
            entityManager.AddComponentObject(providerEntity, new PlayerInputBridgeComponent { Value = this });

            entityManager.AddComponent<InputState>(providerEntity);
            entityManager.AddBuffer<InputAxisBuffer>(providerEntity);
            entityManager.AddBuffer<InputHistory>(providerEntity);

            var transducers = entityManager.AddBuffer<InputToConditionEvent>(providerEntity);
            AddTransducers(transducers, inputSettings);

            entityManager.AddBuffer<ConditionEvent>(providerEntity).Initialize();

            entityManager.AddComponent<EventsDirty>(providerEntity);
            entityManager.SetComponentEnabled<EventsDirty>(providerEntity, false);
        }

        private void OnDisable()
        {
            if (capturedWorld != null && capturedWorld.IsCreated && entityManager.Exists(providerEntity))
                entityManager.DestroyEntity(providerEntity);

            Buttons.Clear();
            Axes.Clear();
            CurrentAxes.Clear();
            CurrentHeld = new BitArray256();
        }

        private static bool TryFindAction(PlayerInput playerInput, InputActionReference reference,
            out InputAction action)
        {
            action = null;

            if (reference == null || reference.action == null) return false;

            action = playerInput.actions.FindAction(reference.action.id);
            return action != null;
        }

        private static void AddTransducers(DynamicBuffer<InputToConditionEvent> transducers,
            MultiInputSettings settings)
        {
            transducers.Clear();

            for (byte actionId = 0; actionId < settings.InputActions.Count; actionId++)
            {
                var binding = settings.InputActions[actionId];
                if (binding.Events == null) continue;

                foreach (var evt in binding.Events)
                {
                    if (evt == null) continue;

                    if (evt.TryBake(actionId, out var transducer)) transducers.Add(transducer);
                }
            }
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