using System.Collections.Generic;
using PlayerInputs.Data;
using Unity.Entities;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PlayerInputs
{
    /// <summary>
    /// Managed component bridge that automatically binds Unity Input System events 
    /// based on the global InputKeys settings.
    /// </summary>
    [RequireComponent(typeof(PlayerInput))]
    public class PlayerInputBridge : MonoBehaviour
    {
        [Tooltip("Overrides the player index from PlayerInput. Leave at -1 to auto-read.")]
        public int PlayerIdOverride = -1;

        internal readonly List<(byte Id, InputAction Action)> Buttons = new();
        internal readonly List<(byte Id, InputAction Action)> Axes = new();

        private EntityManager _entityManager;
        private Entity _providerEntity;

        private void OnEnable()
        {
            var playerInput = GetComponent<PlayerInput>();
            if (playerInput.actions == null) return;

            // Automatically pull mappings from the global InputKeys settings
            var inputKeys = InputKeys.I;
            if (inputKeys != null)
            {
                foreach (var mapping in inputKeys.Mappings)
                {
                    if (mapping.Action == null) continue;

                    // Find the localized action instance for this specific player's PlayerInput
                    // We use the action's GUID to ensure a 100% accurate match
                    var action = playerInput.actions.FindAction(mapping.Action.action.id);
                    if (action == null) continue;

                    // Auto-categorize into Buttons vs Axes based on the Input Action's type
                    if (action.type == InputActionType.Button)
                    {
                        Buttons.Add((mapping.Value, action));
                    }
                    else if (action.type == InputActionType.Value)
                    {
                        Axes.Add((mapping.Value, action));
                    }
                }
            }

            // Create the ECS Entity dynamically
            _entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
            _providerEntity = _entityManager.CreateEntity();

            _entityManager.AddComponentData(_providerEntity, new PlayerId { Value = GetPlayerId() });
            _entityManager.AddComponent<InputProviderTag>(_providerEntity);
            
            // Allow the ECS Poll System to read this MonoBehaviour
            _entityManager.AddComponentObject(_providerEntity, new PlayerInputBridgeComponent { Value = this });

            _entityManager.AddBuffer<InputButtonDownBuffer>(_providerEntity);
            _entityManager.AddBuffer<InputButtonHeldBuffer>(_providerEntity);
            _entityManager.AddBuffer<InputButtonUpBuffer>(_providerEntity);
            _entityManager.AddBuffer<InputAxisBuffer>(_providerEntity);
        }

        private void OnDisable()
        {
            if (_entityManager != default && _entityManager.Exists(_providerEntity))
            {
                _entityManager.DestroyEntity(_providerEntity);
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

    public class PlayerInputBridgeComponent : IComponentData
    {
        public PlayerInputBridge Value;
    }
}