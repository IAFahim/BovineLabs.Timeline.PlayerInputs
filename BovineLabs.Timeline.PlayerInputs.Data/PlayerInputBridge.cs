using System;
using System.Collections.Generic;
using BovineLabs.Core.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BovineLabs.Timeline.PlayerInputs.Data
{
    [RequireComponent(typeof(PlayerInput))]
    public sealed class PlayerInputBridge : MonoBehaviour
    {
        private const float AxisPublishThresholdSq = 0.0001f;

        public int PlayerIdOverride = -1;

        public BitArray256 CurrentDown;
        public BitArray256 CurrentHeld;
        public BitArray256 CurrentUp;
        public readonly List<InputAxis> CurrentAxes = new(16);

        private readonly List<Subscription> subscriptions = new();

        private readonly List<(byte Id, InputAction Action, bool IsVec2)> valueActions = new();

        private readonly List<(byte Id, InputAction Action)> buttonActions = new();

        private EdgeAccumulator edges;

        private bool initialized;
        private EntityManager manager;
        private Entity provider;
        private World world;

        private bool focused = true;
        private bool wasFocused = true;
        private bool hasPointerTag;

        private void Update()
        {
            if (initialized &&
                (provider == Entity.Null || world == null || !world.IsCreated || !manager.Exists(provider)))
                TryCreateProvider(out provider);

            // Keep the pointer tag in sync with the seat's paired devices (hot-join / device switch).
            if (initialized && world != null && world.IsCreated && manager.Exists(provider))
            {
                var pointer = ControlsPointer();
                if (pointer != hasPointerTag)
                {
                    if (pointer) manager.AddComponent<PointerProviderTag>(provider);
                    else manager.RemoveComponent<PointerProviderTag>(provider);
                    hasPointerTag = pointer;
                }
            }

            if (!focused)
            {
                if (wasFocused)
                {
                    // Snapshot the held set BEFORE resetting so focus-loss emits an Up edge for every held button
                    // (release-edge consumers derive Up solely from state.Up). Mirrors RetireProvider's Up synthesis.
                    edges.Prime(out var heldOnBlur);
                    edges.Reset();
                    CurrentAxes.Clear();
                    edges.Publish(out CurrentDown, out CurrentUp, out CurrentHeld);
                    CurrentUp = heldOnBlur;
                    wasFocused = false;
                }

                return;
            }

            if (!wasFocused)
            {
                // Clear any edges the InputSystem callbacks accumulated while unfocused (reachable when the
                // PlayerInput runs in background / IgnoreFocus), THEN re-seed the currently-held state with no Down
                // edge. Without the Reset those stale press/release pairs would flush as a spurious double-fire.
                edges.Reset();

                foreach (var button in buttonActions)
                    if (button.Action.IsPressed())
                        edges.Seed(button.Id);

                foreach (var axis in valueActions)
                {
                    var resumed = axis.IsVec2
                        ? (float2)axis.Action.ReadValue<Vector2>()
                        : new float2(axis.Action.ReadValue<float>(), 0f);
                    if (math.lengthsq(resumed) > AxisPublishThresholdSq)
                        edges.Seed(axis.Id);
                }

                wasFocused = true;
            }

            CurrentAxes.Clear();
            foreach (var axis in valueActions)
            {
                var val = axis.IsVec2
                    ? (float2)axis.Action.ReadValue<Vector2>()
                    : new float2(axis.Action.ReadValue<float>(), 0f);

                var actuated = math.lengthsq(val) > AxisPublishThresholdSq;
                var was = edges.IsPressed(axis.Id);
                if (actuated && !was) edges.Press(axis.Id);
                else if (!actuated && was) edges.Release(axis.Id);

                if (actuated)
                    CurrentAxes.Add(new InputAxis { ActionId = axis.Id, Value = val });
            }

            foreach (var button in buttonActions)
            {
                var down = button.Action.IsPressed();
                var was = edges.IsPressed(button.Id);
                if (down && !was) edges.Press(button.Id);
                else if (!down && was) edges.Release(button.Id);
            }

            edges.Publish(out CurrentDown, out CurrentUp, out CurrentHeld);
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            focused = hasFocus;
        }

        private void OnEnable()
        {
            var playerInput = GetComponent<PlayerInput>();
            if (playerInput.actions == null)
            {
                Debug.LogWarning($"PlayerInputBridge on '{name}' has a PlayerInput with no actions asset assigned.",
                    this);
                return;
            }

            if (MultiInputSettings.I == null)
            {
                Debug.LogWarning(
                    $"PlayerInputBridge on '{name}' found no MultiInputSettings; no input will be bound for this player.",
                    this);
                return;
            }

            ClearState();

            focused = Application.isFocused;
            wasFocused = focused;

            var count = Math.Min(MultiInputSettings.I.InputActions.Count, MultiInputSettings.MaxActions);
            for (var i = 0; i < count; i++)
            {
                var binding = MultiInputSettings.I.InputActions[i];
                if (!TryFindAction(playerInput, binding, out var action))
                {
                    Debug.LogWarning(
                        $"PlayerInputBridge on '{name}': action slot {i} ('{(binding != null ? binding.name : "null")}') " +
                        "did not resolve in this player's actions asset; that input will never fire.", this);
                    continue;
                }

                var id = (byte)i;
                var isVec2 = IsTwoDimensional(action);
                var isAxis = action.type == InputActionType.Value ||
                             (action.type == InputActionType.PassThrough && isVec2);

                if (isAxis)
                {
                    valueActions.Add((id, action, isVec2));
                }
                else
                {
                    var sub = new Subscription
                    {
                        Action = action,
                        OnStarted = _ => edges.Press(id),
                        OnCanceled = _ => edges.Release(id)
                    };
                    action.started += sub.OnStarted;
                    action.canceled += sub.OnCanceled;
                    subscriptions.Add(sub);
                    buttonActions.Add((id, action));
                }

                if (action.IsPressed())
                    edges.Seed(id);
            }

            edges.Prime(out CurrentHeld);

            initialized = true;
            TryCreateProvider(out provider);
        }

        private void OnDisable()
        {
            foreach (var sub in subscriptions)
            {
                sub.Action.started -= sub.OnStarted;
                sub.Action.canceled -= sub.OnCanceled;
            }

            subscriptions.Clear();
            valueActions.Clear();
            buttonActions.Clear();

            if (world != null && world.IsCreated && manager.Exists(provider))
                RetireProvider();

            provider = Entity.Null;
            world = null;
            initialized = false;
            hasPointerTag = false;
        }

        private bool ControlsPointer()
        {
            var input = GetComponent<PlayerInput>();
            if (input == null)
                return false;

            var devices = input.devices;
            for (var i = 0; i < devices.Count; i++)
                if (devices[i] is Pointer)
                    return true;

            return false;
        }

        private void RetireProvider()
        {
            var held = manager.GetComponentData<InputState>(provider).Held;
            manager.SetComponentData(provider, new InputState { Up = held });
            manager.GetBuffer<InputAxis>(provider).Clear();
            manager.AddComponent<ProviderRetiring>(provider);
            manager.RemoveComponent<PlayerInputBridgeComponent>(provider);
        }

        private void ClearState()
        {
            subscriptions.Clear();
            valueActions.Clear();
            buttonActions.Clear();
            CurrentAxes.Clear();
            CurrentDown = default;
            CurrentHeld = default;
            CurrentUp = default;
            edges.Reset();
        }

        private bool TryCreateProvider(out Entity entity)
        {
            entity = Entity.Null;
            world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return false;

            manager = world.EntityManager;
            try
            {
                entity = manager.CreateEntity();

                manager.AddComponentData(entity, new PlayerId { Value = GetPlayerId() });
                manager.AddComponent<ProviderTag>(entity);
                manager.AddComponent<InputState>(entity);
                manager.AddBuffer<InputAxis>(entity);
                manager.AddComponentObject(entity, new PlayerInputBridgeComponent { Value = this });

                hasPointerTag = ControlsPointer();
                if (hasPointerTag)
                    manager.AddComponent<PointerProviderTag>(entity);
            }
            catch (InvalidOperationException)
            {
                entity = Entity.Null;
                return false;
            }

            return true;
        }

        private static bool IsTwoDimensional(InputAction action)
        {
            var type = action.expectedControlType;
            if (type == "Vector2" || type == "Stick" || type == "Dpad" || type == "Delta") return true;
            if (!string.IsNullOrEmpty(type)) return false;

            var controls = action.controls;
            for (var i = 0; i < controls.Count; i++)
                if (controls[i].valueType == typeof(Vector2))
                    return true;

            return false;
        }

        private static bool TryFindAction(PlayerInput input, InputActionReference reference, out InputAction action)
        {
            action = null;
            if (reference?.action == null) return false;

            action = input.actions.FindAction(reference.action.id);
            return action != null;
        }

        private byte GetPlayerId()
        {
            return PlayerIdOverride >= 0
                ? (byte)PlayerIdOverride
                : (byte)(GetComponent<PlayerInput>()?.playerIndex ?? 0);
        }

        private struct Subscription
        {
            public InputAction Action;
            public Action<InputAction.CallbackContext> OnStarted;
            public Action<InputAction.CallbackContext> OnCanceled;
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
            return Value?.GetHashCode() ?? 0;
        }
    }
}