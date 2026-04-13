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
        public int playerIdOverride = -1;
        private readonly List<(byte Id, InputAction Action)> _axes = new();
        private readonly List<(byte Id, InputAction Action)> _buttons = new();

        private World capturedWorld;
        private EntityManager entityManager;
        private Entity providerEntity;

        private void OnEnable()
        {
            var playerInput = GetComponent<PlayerInput>();
            if (playerInput.actions == null) return;

            var inputKeys = InputSettings.I;

            for (byte i = 0; i < inputKeys.InputActionReferences.Count; i++)
            {
                var inputActionReference = inputKeys.InputActionReferences[i];
                if (inputActionReference == null || inputActionReference.action == null) continue;

                var action = playerInput.actions.FindAction(inputActionReference.action.id);
                if (action == null) continue;

                switch (action.type)
                {
                    case InputActionType.Button:
                        _buttons.Add((i, action)); // Use 'i' as the ID
                        break;
                    case InputActionType.Value:
                        _axes.Add((i, action));    // Use 'i' as the ID
                        break;
                }
            }

            this.capturedWorld = World.DefaultGameObjectInjectionWorld;
            if (this.capturedWorld == null) return;

            this.entityManager = this.capturedWorld.EntityManager;
            this.providerEntity = this.entityManager.CreateEntity();

            this.entityManager.AddComponentData(this.providerEntity, new PlayerId { Value = GetPlayerId() });
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

            var currentHeld = new BitArray256();
            foreach (var btn in this._buttons)
            {
                if (btn.Action.IsPressed())
                {
                    currentHeld[btn.Id] = true;
                }
            }

            var previousHeld = this.entityManager.GetComponentData<InputState>(this.providerEntity).Held;

            // S-Tier Core optimization: Native Bitwise calculations without branching
            var newState = new InputState
            {
                Down = currentHeld.BitAnd(previousHeld.BitNot()),
                Held = currentHeld,
                Up = previousHeld.BitAnd(currentHeld.BitNot())
            };

            this.entityManager.SetComponentData(this.providerEntity, newState);

            var axes = this.entityManager.GetBuffer<InputAxisBuffer>(this.providerEntity);
            axes.Clear();

            foreach (var axis in this._axes)
            {
                var val = axis.Action.expectedControlType == "Vector2" 
                    ? axis.Action.ReadValue<Vector2>() 
                    : new Vector2(axis.Action.ReadValue<float>(), 0f);

                if (math.lengthsq(val) > 0.0001f)
                {
                    axes.Add(new InputAxisBuffer { ActionId = axis.Id, Value = val });
                }
            }
        }

        private void OnDisable()
        {
            if (this.capturedWorld != null && this.capturedWorld.IsCreated && this.entityManager.Exists(this.providerEntity))
            {
                this.entityManager.DestroyEntity(this.providerEntity);
            }

            _buttons.Clear();
            _axes.Clear();
        }

        public byte GetPlayerId() => playerIdOverride >= 0 ? (byte)playerIdOverride : (byte)(GetComponent<PlayerInput>()?.playerIndex ?? 0);
    }

    public sealed class PlayerInputBridgeComponent : IComponentData, IEquatable<PlayerInputBridgeComponent>, ICloneable
    {
        public PlayerInputBridge Value;
        public bool Equals(PlayerInputBridgeComponent other) => !ReferenceEquals(null, other) && (ReferenceEquals(this, other) || Equals(this.Value, other.Value));
        public override bool Equals(object obj) => ReferenceEquals(this, obj) || obj is PlayerInputBridgeComponent other && Equals(other);
        public override int GetHashCode() => this.Value != null ? this.Value.GetHashCode() : 0;
        public object Clone() => new PlayerInputBridgeComponent { Value = this.Value };
    }
}