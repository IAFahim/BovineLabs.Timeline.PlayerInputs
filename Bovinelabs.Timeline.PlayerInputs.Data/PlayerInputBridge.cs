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
            if (playerInput.actions == null)
            {
                Debug.LogWarning("[PlayerInputBridge] PlayerInput component has no actions assigned.", this);
                return;
            }

            var inputKeys = InputSettings.I;
            if (inputKeys == null || inputKeys.Mappings.Count == 0)
            {
                Debug.LogWarning("[PlayerInputBridge] InputSettings is null or contains no Mappings.", this);
                return;
            }

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

            // Core Tags
            this.entityManager.AddComponentData(this.providerEntity, new PlayerId { Value = this.GetPlayerId() });
            this.entityManager.AddComponent<InputProviderTag>(this.providerEntity);
            
            // Buffers & State
            this.entityManager.AddComponent<InputState>(this.providerEntity);
            this.entityManager.AddBuffer<InputAxisBuffer>(this.providerEntity);
            this.entityManager.AddBuffer<InputHistory>(this.providerEntity);
            this.entityManager.AddBuffer<InputToConditionEvent>(this.providerEntity);

            // Reaction Publisher Requirements
            this.entityManager.AddBuffer<ConditionEvent>(this.providerEntity).Initialize();
            this.entityManager.AddComponent<EventsDirty>(this.providerEntity);
            this.entityManager.SetComponentEnabled<EventsDirty>(this.providerEntity, false);
        }

        private void Update()
        {
            if (this.capturedWorld == null || !this.capturedWorld.IsCreated || !this.entityManager.Exists(this.providerEntity))
            {
                return; // Failsafe
            }

            // 1. Calculate new Held state
            var currentHeld = new InputBitmask();
            foreach (var btn in this.Buttons)
            {
                if (btn.Action.IsPressed())
                {
                    currentHeld.Set(btn.Id);
                }
            }

            // 2. Fetch previous state and algebraically calculate Down/Up
            var previousHeld = this.entityManager.GetComponentData<InputState>(this.providerEntity).Held;
            
            var newState = new InputState
            {
                Down = currentHeld.AndNot(previousHeld),
                Held = currentHeld,
                Up = previousHeld.AndNot(currentHeld)
            };

            // 3. Inject back to ECS
            this.entityManager.SetComponentData(this.providerEntity, newState);

            // 4. Inject Axes
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
            // Failsafe: Ensures we don't try to access a destroyed ECS World when exiting Play Mode
            if (this.capturedWorld != null && this.capturedWorld.IsCreated)
            {
                if (this.entityManager.Exists(this.providerEntity))
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
}