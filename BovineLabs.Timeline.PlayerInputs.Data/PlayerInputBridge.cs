#pragma warning disable CS0618 // Managed-component API (AddComponentObject/GetComponentObject/ManagedAPI) deprecated in Entities 6.6; TODO: migrate to UnityObjectRef<T>/unmanaged components.
using System;
using System.Collections.Generic;
using BovineLabs.Core.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Scripting.LifecycleManagement;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BovineLabs.Timeline.PlayerInputs.Data
{
    /// <remarks>
    /// NoAutoStaticsCleanup because this type already resets its one static itself, at a moment it chose:
    /// <see cref="ResetProviderSeq" /> runs on SubsystemRegistration, i.e. before the first frame of a play
    /// session. providerSeqCounter is what InputRegistrySystem uses to break same-kind duplicate ties
    /// deterministically, so WHEN it returns to zero is part of that determinism, not an implementation detail.
    /// Handing the reset to the automatic pass would move it to a lifecycle point we do not control.
    /// </remarks>
    [NoAutoStaticsCleanup]
    [RequireComponent(typeof(PlayerInput))]
    public sealed class PlayerInputBridge : MonoBehaviour
    {
        private const float AxisPublishThresholdSq = 0.0001f;

        // Monotonic seat-join sequence stamped on every created provider so InputRegistrySystem breaks same-kind
        // duplicate ties deterministically instead of leaning on CoreCLR-recycled Entity indices.
        private static uint providerSeqCounter;

        public int PlayerIdOverride = -1;

        // Debug mirrors of the accumulate-and-drain state. These are NO LONGER the authoritative source the ECS reads;
        // ProviderSyncSystem pulls edges via Drain(). They exist so the debug overlays can still show the pending set.
        public BitArray256 CurrentDown;
        public BitArray256 CurrentHeld;
        public BitArray256 CurrentUp;
        public readonly List<InputAxis> CurrentAxes = new(16);

        // Accumulate-and-drain: Update() runs once per RENDER frame and OR-accumulates that frame's Down/Up edges here;
        // ProviderSyncSystem (the single ECS-side consumer, in the default world) calls Drain() once per SIM tick to
        // take + clear them. This decouples the render-frame publish cadence from the sim-tick consume cadence, so a
        // fast tap between two sim ticks is not overwritten (0-consume frame) and a single press is not seen twice
        // (2-consume frame). Held stays level-based (latest wins).
        private BitArray256 pendingDown;
        private BitArray256 pendingUp;

        // Cached once per OnEnable so the per-frame axis reconcile never touches the settings singleton in the hot path.
        private float axisEdgeDeadzone = AxisEdge.DefaultDeadzone;

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
        private bool warnedBadPlayerIndex;

        // CoreCLR keeps statics alive across play sessions (no domain reload) - reset so seq numbers restart each run.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetProviderSeq()
        {
            providerSeqCounter = 0;
        }

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
                    // These Up edges MUST persist in pendingUp until drained - accumulate, don't overwrite.
                    edges.Prime(out var heldOnBlur);
                    edges.Reset();
                    CurrentAxes.Clear();
                    pendingUp |= heldOnBlur;
                    CurrentHeld = default;
                    CurrentDown = pendingDown;
                    CurrentUp = pendingUp;
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
                    // Reseed the HELD state through the same deadzone so a drifting stick does not latch a phantom hold.
                    if (AxisEdge.Actuated(resumed, false, this.axisEdgeDeadzone))
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

                // Split "worth publishing" (stream fine values from a tiny threshold) from "actuated for edge purposes"
                // (a real deadzone + hysteresis), so stick drift never fabricates Down/Up chatter into combo history.
                var publishable = math.lengthsq(val) > AxisPublishThresholdSq;
                var was = edges.IsPressed(axis.Id);
                var actuated = AxisEdge.Actuated(val, was, this.axisEdgeDeadzone);
                if (actuated && !was) edges.Press(axis.Id);
                else if (!actuated && was) edges.Release(axis.Id);

                if (publishable)
                    CurrentAxes.Add(new InputAxis { ActionId = axis.Id, Value = val });
            }

            foreach (var button in buttonActions)
            {
                var down = button.Action.IsPressed();
                var was = edges.IsPressed(button.Id);
                if (down && !was) edges.Press(button.Id);
                else if (!down && was) edges.Release(button.Id);
            }

            // Accumulate this render frame's edges; Drain() (called from the ECS side) takes + clears them.
            edges.Publish(out var frameDown, out var frameUp, out var frameHeld);
            EdgeDrain.Accumulate(ref pendingDown, ref pendingUp, frameDown, frameUp);
            CurrentHeld = frameHeld;
            CurrentDown = pendingDown;
            CurrentUp = pendingUp;
        }

        /// <summary>
        /// Take and clear the edges accumulated since the last drain, plus the latest (level-based) held set. Called
        /// once per simulation tick by <c>ProviderSyncSystem</c> - the single ECS-side consumer of this bridge (the
        /// bridge-backed provider lives only in the default world, so exactly one system drains it). Between drains,
        /// Update() OR-accumulates every render frame's edges, so no press is lost or double-counted when the sim tick
        /// rate differs from the render rate.
        /// </summary>
        public void Drain(out BitArray256 down, out BitArray256 up, out BitArray256 held)
        {
            EdgeDrain.Drain(ref pendingDown, ref pendingUp, out down, out up);
            held = CurrentHeld;

            // Mirrors reflect the drained (now-empty) pending set until the next render-frame Update repopulates them.
            CurrentDown = default;
            CurrentUp = default;
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

            this.axisEdgeDeadzone = MultiInputSettings.AxisEdgeDeadzoneOrDefault;

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
            pendingDown = default;
            pendingUp = default;
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
                manager.AddComponentData(entity, new ProviderSeq { Value = providerSeqCounter++ });
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
            var index = PlayerIdOverride >= 0
                ? PlayerIdOverride
                : GetComponent<PlayerInput>()?.playerIndex ?? 0;

            // playerIndex is -1 before assignment and Unity recycles it on leave/rejoin; (byte)(-1) == 255 collides
            // with the top slot. Warn once and clamp into the valid seat range [0, 254].
            if (index < 0)
            {
                WarnBadPlayerIndex(index);
                return 0;
            }

            if (index > 254)
            {
                WarnBadPlayerIndex(index);
                return 254;
            }

            return (byte)index;
        }

        private void WarnBadPlayerIndex(int index)
        {
            if (warnedBadPlayerIndex) return;
            warnedBadPlayerIndex = true;
            Debug.LogWarning(
                $"PlayerInputBridge on '{name}': resolved player index {index} is outside the valid seat range " +
                "[0, 254]; clamping. Assign a PlayerIdOverride to pin this seat.", this);
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

    /// <summary>
    /// Pure accumulate-and-drain edge bookkeeping used by <see cref="PlayerInputBridge"/>. Extracted so the
    /// render-rate-vs-sim-rate cadence contract is unit-testable without a live PlayerInput: Update() OR-accumulates
    /// each render frame's edges, and the single ECS-side consumer drains (takes + clears) once per sim tick. A tap
    /// between two sim ticks survives a 0-consume frame; a single press is not re-seen on a 2-consume frame.
    /// </summary>
    public static class EdgeDrain
    {
        /// <summary> OR this render frame's Down/Up edges into the pending (not-yet-drained) accumulators. </summary>
        public static void Accumulate(ref BitArray256 pendingDown, ref BitArray256 pendingUp,
            BitArray256 frameDown, BitArray256 frameUp)
        {
            pendingDown |= frameDown;
            pendingUp |= frameUp;
        }

        /// <summary> Take the accumulated edges and clear the pending set (one sim tick's consumption). </summary>
        public static void Drain(ref BitArray256 pendingDown, ref BitArray256 pendingUp,
            out BitArray256 down, out BitArray256 up)
        {
            down = pendingDown;
            up = pendingUp;
            pendingDown = default;
            pendingUp = default;
        }
    }
}