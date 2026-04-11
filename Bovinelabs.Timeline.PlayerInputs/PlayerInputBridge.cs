using System;
using System.Collections.Generic;
using BovineLabs.Reaction.Data.Conditions;
using Bovinelabs.Timeline.PlayerInputs.Data;
using Unity.Entities;
using UnityEngine;
using UnityEngine.InputSystem;
using InputSettings = Bovinelabs.Timeline.PlayerInputs.Data.InputSettings;

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

        private void OnEnable()
        {
            var playerInput = GetComponent<PlayerInput>();
            if (playerInput.actions == null)
            {
                Debug.LogWarning("[PlayerInputBridge] PlayerInput component has no actions assigned.", this);
                return;
            }

            var inputKeys = InputSettings.I;
            if (inputKeys == null || inputKeys.Mappings.Count == 0)
                Debug.LogWarning("[PlayerInputBridge] InputSettings is null or contains no Mappings. No inputs will be registered.", this);
            else
                foreach (var mapping in inputKeys.Mappings)
                {
                    if (mapping.Action == null || mapping.Action.action == null) continue;

                    var action = playerInput.actions.FindAction(mapping.Action.action.id);
                    if (action == null)
                    {
                        Debug.LogWarning($"[PlayerInputBridge] Could not find action {mapping.Action.action.name} in the active PlayerInput asset.", this);
                        continue;
                    }

                    switch (action.type)
                    {
                        case InputActionType.Button:
                            Buttons.Add((mapping.Value, action));
                            break;
                        case InputActionType.Value:
                            Axes.Add((mapping.Value, action));
                            break;
                    }
                }

            this.capturedWorld = World.DefaultGameObjectInjectionWorld;
            if (this.capturedWorld == null) return;

            this.entityManager = this.capturedWorld.EntityManager;
            this.providerEntity = this.entityManager.CreateEntity();

            this.entityManager.AddComponentData(this.providerEntity, new PlayerId { Value = GetPlayerId() });
            this.entityManager.AddComponent<InputProviderTag>(this.providerEntity);
            
            // S-Tier Fix: Use AddComponentData for managed IComponentData classes, NOT AddComponentObject!
            this.entityManager.AddComponentData(this.providerEntity, new PlayerInputBridgeComponent { Value = this });

            this.entityManager.AddComponent<InputState>(this.providerEntity);
            this.entityManager.AddBuffer<InputAxisBuffer>(this.providerEntity);
            this.entityManager.AddBuffer<InputHistory>(this.providerEntity);
            this.entityManager.AddBuffer<InputToConditionEvent>(this.providerEntity);

            this.entityManager.AddBuffer<ConditionEvent>(this.providerEntity).Initialize();
            this.entityManager.AddComponent<EventsDirty>(this.providerEntity);
            this.entityManager.SetComponentEnabled<EventsDirty>(this.providerEntity, false);
        }

        private void OnDisable()
        {
            // S-Tier Fix: Avoid ObjectDisposedException when exiting Play Mode
            if (this.capturedWorld != null && this.capturedWorld.IsCreated)
            {
                if (this.entityManager != default && this.entityManager.Exists(this.providerEntity))
                {
                    this.entityManager.DestroyEntity(this.providerEntity);
                }
            }

            Buttons.Clear();
            Axes.Clear();
        }

        public byte GetPlayerId()
        {
            if (PlayerIdOverride >= 0) return (byte)PlayerIdOverride;

            var pi = GetComponent<PlayerInput>();
            return (byte)(pi != null ? pi.playerIndex : 0);
        }
    }

    // S-Tier Fix: Managed components MUST implement IEquatable and ICloneable
    public sealed class PlayerInputBridgeComponent : IComponentData, IEquatable<PlayerInputBridgeComponent>, ICloneable
    {
        public PlayerInputBridge Value;

        public bool Equals(PlayerInputBridgeComponent other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return Equals(this.Value, other.Value);
        }

        public override bool Equals(object obj) => ReferenceEquals(this, obj) || obj is PlayerInputBridgeComponent other && Equals(other);

        public override int GetHashCode() => this.Value != null ? this.Value.GetHashCode() : 0;

        public object Clone() => new PlayerInputBridgeComponent { Value = this.Value };
    }
}