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
        internal readonly List<(byte Id, InputAction Action)> Axes = new();
        internal readonly List<(byte Id, InputAction Action)> Buttons = new();

        public BitArray256 CurrentHeld;
        public List<InputAxisBuffer> CurrentAxes = new();

        private World capturedWorld;
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
                    var v2 = axis.Action.ReadValue<UnityEngine.Vector2>();
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
            var playerInput = this.GetComponent<PlayerInput>();
            if (playerInput.actions == null)
            {
                return;
            }

            var inputSettings = MuliInputSettings.I;
            if (inputSettings == null || inputSettings.InputActions.Count == 0)
            {
                return;
            }

            this.Buttons.Clear();
            this.Axes.Clear();
            this.CurrentAxes.Clear();
            this.CurrentHeld = new BitArray256();

            for (byte index = 0; index < inputSettings.InputActions.Count; index++)
            {
                var binding = inputSettings.InputActions[index];
                if (!TryFindAction(playerInput, binding.Input, out var action))
                {
                    continue;
                }

                switch (action.type)
                {
                    case InputActionType.Button:
                        this.Buttons.Add((index, action));
                        break;

                    case InputActionType.Value:
                        this.Axes.Add((index, action));
                        break;
                }
            }

            this.capturedWorld = World.DefaultGameObjectInjectionWorld;
            if (this.capturedWorld == null)
            {
                return;
            }

            this.entityManager = this.capturedWorld.EntityManager;
            this.providerEntity = this.entityManager.CreateEntity();

            this.entityManager.AddComponentData(this.providerEntity, new PlayerId { Value = this.GetPlayerId() });
            this.entityManager.AddComponent<InputProviderTag>(this.providerEntity);
            this.entityManager.AddComponentObject(this.providerEntity, new PlayerInputBridgeComponent { Value = this });

            this.entityManager.AddComponent<InputState>(this.providerEntity);
            this.entityManager.AddBuffer<InputAxisBuffer>(this.providerEntity);
            this.entityManager.AddBuffer<InputHistory>(this.providerEntity);

            var transducers = this.entityManager.AddBuffer<InputToConditionEvent>(this.providerEntity);
            AddTransducers(transducers, inputSettings);

            this.entityManager.AddBuffer<ConditionEvent>(this.providerEntity).Initialize();

            this.entityManager.AddComponent<EventsDirty>(this.providerEntity);
            this.entityManager.SetComponentEnabled<EventsDirty>(this.providerEntity, false);
        }

        private static bool TryFindAction(PlayerInput playerInput, InputActionReference reference,
            out InputAction action)
        {
            action = null;

            if (reference == null || reference.action == null)
            {
                return false;
            }

            action = playerInput.actions.FindAction(reference.action.id);
            return action != null;
        }

        private static void AddTransducers(DynamicBuffer<InputToConditionEvent> transducers, MuliInputSettings settings)
        {
            transducers.Clear();

            for (byte actionId = 0; actionId < settings.InputActions.Count; actionId++)
            {
                var binding = settings.InputActions[actionId];
                if (binding.Events == null)
                {
                    continue;
                }

                foreach (var evt in binding.Events)
                {
                    if (evt == null)
                    {
                        continue;
                    }

                    if (evt.TryBake(actionId, out var transducer))
                    {
                        transducers.Add(transducer);
                    }
                }
            }
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