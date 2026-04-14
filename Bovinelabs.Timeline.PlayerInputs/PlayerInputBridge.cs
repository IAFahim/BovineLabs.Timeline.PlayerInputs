using System;
using System.Collections.Generic;
using BovineLabs.Core.Collections;
using BovineLabs.Reaction.Data.Conditions;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Bovinelabs.Timeline.PlayerInputs.Data
{
    [RequireComponent(typeof(PlayerInput))]
    public sealed class PlayerInputBridge : MonoBehaviour
    {
        public int PlayerIdOverride = -1;
        internal readonly List<(byte Id, InputAction Action)> Axes = new();
        internal readonly List<(byte Id, InputAction Action)> Buttons = new();

        private World capturedWorld;
        private EntityManager entityManager;
        private Entity providerEntity;

        private void Update()
        {
            if (capturedWorld == null || !capturedWorld.IsCreated || !entityManager.Exists(providerEntity)) return;

            var currentHeld = new BitArray256();
            foreach (var btn in Buttons)
                if (btn.Action.IsPressed())
                    currentHeld[btn.Id] = true;

            var previousHeld = entityManager.GetComponentData<InputState>(providerEntity).Held;

            // S-Tier Core optimization: Native Bitwise calculations without branching
            var newState = new InputState
            {
                Down = currentHeld.BitAnd(previousHeld.BitNot()),
                Held = currentHeld,
                Up = previousHeld.BitAnd(currentHeld.BitNot())
            };

            entityManager.SetComponentData(providerEntity, newState);

            var axes = entityManager.GetBuffer<InputAxisBuffer>(providerEntity);
            axes.Clear();

            foreach (var axis in Axes)
            {
                var val = axis.Action.expectedControlType == "Vector2"
                    ? axis.Action.ReadValue<float2>()
                    : new float2(axis.Action.ReadValue<float>(), 0f);

                if (math.lengthsq(val) > 0.0001f) axes.Add(new InputAxisBuffer { ActionId = axis.Id, Value = val });
            }
        }

        private void OnEnable()
        {
            var playerInput = GetComponent<PlayerInput>();
            if (playerInput.actions == null) return;

            var inputKeys = InputSettings.I;
            if (inputKeys == null || inputKeys.InputActionReferences.Count == 0) return;

            for (byte index = 0; index < inputKeys.InputActionReferences.Count; index++)
            {
                var mapping = inputKeys.InputActionReferences[index];
                if (mapping == null || mapping.action == null) continue;

                var action = playerInput.actions.FindAction(mapping.action.id);
                if (action == null) continue;

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
            entityManager.AddComponentData(providerEntity, new PlayerInputBridgeComponent { Value = this });

            entityManager.AddComponent<InputState>(providerEntity);
            entityManager.AddBuffer<InputAxisBuffer>(providerEntity);
            entityManager.AddBuffer<InputHistory>(providerEntity);
            entityManager.AddBuffer<InputToConditionEvent>(providerEntity);

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
            return new PlayerInputBridgeComponent { Value = this.Value };
        }

        public bool Equals(PlayerInputBridgeComponent other)
        {
            return !ReferenceEquals(null, other) && (ReferenceEquals(this, other) || Equals(this.Value, other.Value));
        }

        public override bool Equals(object obj)
        {
            return ReferenceEquals(this, obj) || obj is PlayerInputBridgeComponent other && Equals(other);
        }

        public override int GetHashCode()
        {
            return this.Value != null ? this.Value.GetHashCode() : 0;
        }
    }
}