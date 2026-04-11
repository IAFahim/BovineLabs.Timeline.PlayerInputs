using System;
using System.Collections.Generic;
using BovineLabs.Reaction.Data.Conditions;
using Bovinelabs.Timeline.PlayerInputs.Data;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using InputSettings = Bovinelabs.Timeline.PlayerInputs.Data.InputSettings;

namespace Bovinelabs.Timeline.PlayerInputs
{
    [RequireComponent(typeof(PlayerInput))]
    public sealed class PlayerInputBridge : MonoBehaviour
    {
        public int PlayerIdOverride = -1;

        internal readonly List<(byte Id, InputAction Action)> Buttons = new();
        internal readonly List<(byte Id, InputAction Action)> Axes = new();

        private World capturedWorld;
        private EntityManager entityManager;
        private Entity providerEntity;

        private void OnEnable()
        {
            var playerInput = this.GetComponent<PlayerInput>();
            if (playerInput.actions == null) return;

            var inputKeys = InputSettings.I;
            if (inputKeys == null || inputKeys.Mappings.Count == 0) return;

            foreach (var mapping in inputKeys.Mappings)
            {
                if (mapping.Action == null || mapping.Action.action == null) continue;

                var action = playerInput.actions.FindAction(mapping.Action.action.id);
                if (action == null) continue;

                switch (action.type)
                {
                    case InputActionType.Button:
                        this.Buttons.Add((mapping.Value, action));
                        break;
                    case InputActionType.Value:
                        this.Axes.Add((mapping.Value, action));
                        break;
                }
            }

            this.capturedWorld = World.DefaultGameObjectInjectionWorld;
            if (this.capturedWorld == null || !this.capturedWorld.IsCreated) return;

            this.entityManager = this.capturedWorld.EntityManager;
            this.providerEntity = this.entityManager.CreateEntity();

            this.entityManager.AddComponentData(this.providerEntity, new PlayerId { Value = this.GetPlayerId() });
            this.entityManager.AddComponent<InputProviderTag>(this.providerEntity);
            this.entityManager.AddComponentData(this.providerEntity, new PlayerInputBridgeComponent { Value = this });

            this.entityManager.AddComponent<InputState>(this.providerEntity);
            this.entityManager.AddBuffer<InputAxisBuffer>(this.providerEntity);
            this.entityManager.AddBuffer<InputHistory>(this.providerEntity);
            this.entityManager.AddBuffer<InputToConditionEvent>(this.providerEntity);

            this.entityManager.AddBuffer<ConditionEvent>(this.providerEntity).Initialize();
            this.entityManager.AddComponent<EventsDirty>(this.providerEntity);
            this.entityManager.SetComponentEnabled<EventsDirty>(this.providerEntity, false);
        }

        private void Update()
        {
            if (this.capturedWorld == null || !this.capturedWorld.IsCreated || !this.entityManager.Exists(this.providerEntity)) return;

            var currentHeld = new InputBitmask();
            foreach (var btn in this.Buttons)
            {
                if (btn.Action.IsPressed())
                {
                    currentHeld.Set(btn.Id);
                }
            }

            var previousHeld = this.entityManager.GetComponentData<InputState>(this.providerEntity).Held;

            var newState = new InputState
            {
                Down = currentHeld.AndNot(previousHeld),
                Held = currentHeld,
                Up = previousHeld.AndNot(currentHeld)
            };

            this.entityManager.SetComponentData(this.providerEntity, newState);

            var axes = this.entityManager.GetBuffer<InputAxisBuffer>(this.providerEntity);
            axes.Clear();

            foreach (var axis in this.Axes)
            {
                var val = float2.zero;
                if (axis.Action.expectedControlType == "Vector2") val = axis.Action.ReadValue<Vector2>();
                else val.x = axis.Action.ReadValue<float>();

                if (math.lengthsq(val) > 0.0001f)
                {
                    axes.Add(new InputAxisBuffer { ActionId = axis.Id, Value = val });
                }
            }
        }

        private void OnDisable()
        {
            if (this.capturedWorld != null && this.capturedWorld.IsCreated)
            {
                if (this.entityManager != default && this.entityManager.Exists(this.providerEntity))
                {
                    this.entityManager.DestroyEntity(this.providerEntity);
                }
            }

            this.Buttons.Clear();
            this.Axes.Clear();
        }

        public byte GetPlayerId()
        {
            if (this.PlayerIdOverride >= 0) return (byte)this.PlayerIdOverride;
            var pi = this.GetComponent<PlayerInput>();
            return (byte)(pi != null ? pi.playerIndex : 0);
        }
    }

    public sealed class PlayerInputBridgeComponent : IComponentData, IEquatable<PlayerInputBridgeComponent>, ICloneable
    {
        public PlayerInputBridge Value;

        public bool Equals(PlayerInputBridgeComponent other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return Equals(this.Value, other.Value);
        }

        public override bool Equals(object obj) => ReferenceEquals(this, obj) || obj is PlayerInputBridgeComponent other && this.Equals(other);

        public override int GetHashCode() => this.Value != null ? this.Value.GetHashCode() : 0;

        public object Clone() => new PlayerInputBridgeComponent { Value = this.Value };
    }
}