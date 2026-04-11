using System.Collections.Generic;

using Unity.Entities;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Bovinelabs.Timeline.PlayerInputs.Data
{
    [RequireComponent(typeof(PlayerInput))]
    public sealed class PlayerInputBridge : MonoBehaviour
    {
        public int PlayerIdOverride = -1;

        internal readonly List<(byte Id, InputAction Action)> Buttons = new();
        internal readonly List<(byte Id, InputAction Action)> Axes = new();

        private EntityManager entityManager;
        private Entity providerEntity;

        private void OnEnable()
        {
            var playerInput = this.GetComponent<PlayerInput>();
            if (playerInput.actions == null)
            {
                return;
            }

            var inputKeys = InputSettings.I;
            if (inputKeys != null)
            {
                foreach (var mapping in inputKeys.Mappings)
                {
                    if (mapping.Action == null)
                    {
                        continue;
                    }

                    var action = playerInput.actions.FindAction(mapping.Action.action.id);
                    if (action == null)
                    {
                        continue;
                    }

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
            }

            this.entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
            this.providerEntity = this.entityManager.CreateEntity();

            this.entityManager.AddComponentData(this.providerEntity, new PlayerId { Value = this.GetPlayerId() });
            this.entityManager.AddComponent<InputProviderTag>(this.providerEntity);
            this.entityManager.AddComponentObject(this.providerEntity, new PlayerInputBridgeComponent { Value = this });
            
            this.entityManager.AddComponent<InputState>(this.providerEntity);
            this.entityManager.AddBuffer<InputAxisBuffer>(this.providerEntity);
            this.entityManager.AddBuffer<InputHistory>(this.providerEntity);
            this.entityManager.AddBuffer<InputToConditionEvent>(this.providerEntity);
        }

        private void OnDisable()
        {
            if (this.entityManager != default && this.entityManager.Exists(this.providerEntity))
            {
                this.entityManager.DestroyEntity(this.providerEntity);
            }
            
            this.Buttons.Clear();
            this.Axes.Clear();
        }

        public byte GetPlayerId()
        {
            if (this.PlayerIdOverride >= 0)
            {
                return (byte)this.PlayerIdOverride;
            }
            
            var pi = this.GetComponent<PlayerInput>();
            return (byte)(pi != null ? pi.playerIndex : 0);
        }
    }

    public sealed class PlayerInputBridgeComponent : IComponentData
    {
        public PlayerInputBridge Value;
    }
}