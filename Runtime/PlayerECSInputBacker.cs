using System;
using System.Collections.Generic;
using PlayerInputs.Data;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PlayerInputs
{
    [RequireComponent(typeof(PlayerInput))]
    public class PlayerInputBridge : MonoBehaviour
    {
        private struct CachedButton
        {
            public int ID;
            public InputAction Action;
        }

        private struct CachedAxis
        {
            public int ID;
            public InputAction Action;
        }

        public byte PlayerID;

        private EntityManager _entityManager;
        private Entity _backingEntity;
        private readonly List<CachedButton> _buttons = new();
        private readonly List<CachedAxis> _axes = new();

        private void Start()
        {
            var playerInput = GetComponent<PlayerInput>();
            if (playerInput.actions == null) return;

            // 1. Dynamically read ALL actions in the asset
            foreach (var action in playerInput.actions)
            {
                int actionID = InputUtility.GetActionID(action.name);

                if (action.type == InputActionType.Button)
                {
                    _buttons.Add(new CachedButton { ID = actionID, Action = action });
                }
                else if (action.type == InputActionType.Value)
                {
                    _axes.Add(new CachedAxis { ID = actionID, Action = action });
                }
            }

            _entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;

            // 2. Create the backing entity with our dynamic buffers
            var archetype = _entityManager.CreateArchetype(
                typeof(BackingInputEntityTag),
                typeof(ECSPlayerInputID),
                typeof(InputSubscribedEntity),
                typeof(InputButtonDownBuffer),
                typeof(InputButtonHeldBuffer),
                typeof(InputButtonUpBuffer),
                typeof(InputAxisBuffer)
            );

            _backingEntity = _entityManager.CreateEntity(archetype);
            _entityManager.SetComponentData(_backingEntity, new ECSPlayerInputID { ID = PlayerID });
        }

        private void Update()
        {
            if (!_entityManager.Exists(_backingEntity)) return;

            // 1. Get and clear buffers (O(1) memory clear, no allocations)
            var downs = _entityManager.GetBuffer<InputButtonDownBuffer>(_backingEntity);
            var helds = _entityManager.GetBuffer<InputButtonHeldBuffer>(_backingEntity);
            var ups = _entityManager.GetBuffer<InputButtonUpBuffer>(_backingEntity);
            var axes = _entityManager.GetBuffer<InputAxisBuffer>(_backingEntity);

            downs.Clear();
            helds.Clear();
            ups.Clear();
            axes.Clear();

            // 2. Populate Buttons
            foreach (var btn in _buttons)
            {
                if (btn.Action.WasPressedThisFrame())
                    downs.Add(new InputButtonDownBuffer { ActionID = btn.ID });

                if (btn.Action.IsInProgress())
                    helds.Add(new InputButtonHeldBuffer { ActionID = btn.ID });

                if (btn.Action.WasReleasedThisFrame())
                    ups.Add(new InputButtonUpBuffer { ActionID = btn.ID });
            }

            // 3. Populate Axes (Vector2 and 1D Floats)
            foreach (var axis in _axes)
            {
                float2 val = float2.zero;

                if (axis.Action.expectedControlType == "Vector2")
                    val = axis.Action.ReadValue<Vector2>();
                else if (axis.Action.expectedControlType == "Axis" || axis.Action.expectedControlType == "Button")
                    val.x = axis.Action.ReadValue<float>();

                if (math.lengthsq(val) > 0.0001f)
                    axes.Add(new InputAxisBuffer { ActionID = axis.ID, Value = val });
            }
        }

        private void OnDisable()
        {
            if (World.DefaultGameObjectInjectionWorld != null &&
                World.DefaultGameObjectInjectionWorld.IsCreated &&
                _entityManager != default &&
                _entityManager.Exists(_backingEntity))
            {
                _entityManager.DestroyEntity(_backingEntity);
            }
        }
    }
}