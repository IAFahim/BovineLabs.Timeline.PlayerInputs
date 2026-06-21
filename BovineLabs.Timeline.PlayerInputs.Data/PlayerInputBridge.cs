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
        // Magnitude² above which an actuated axis publishes its value into CurrentAxes (a small deadzone so a
        // near-neutral stick doesn't emit noise). This gates the analog VALUE only - the Down/Held/Up EDGES come
        // from the Input System's own started/canceled callbacks, which honour each control's real actuation point.
        private const float AxisPublishThresholdSq = 0.0001f;

        public int PlayerIdOverride = -1;

        // Published each frame to the provider entity by ProviderSyncSystem. Down/Up are one-frame edges; Held is
        // the latched hold state. These are byte-for-byte the same contract as before - consumers are unchanged.
        public BitArray256 CurrentDown;
        public BitArray256 CurrentHeld;
        public BitArray256 CurrentUp;

        // The latched-hold + accumulated-edge state machine (driven by callbacks, published once per frame).
        // Accumulating (rather than reading a one-frame poll) means a press+release inside one frame both
        // register, and the published edge lifetime matches the old poll model exactly - it survives the whole
        // ECS frame, so the late TimelineComponentAnimationGroup readers (CommandSequence/InputEvents/...) see it.
        private EdgeAccumulator edges;

        // Every action we subscribed, with its exact delegates, so OnDisable can unsubscribe SYMMETRICALLY.
        // Without this a leaving/rejoining player (normal in local coop) leaks handlers onto the cloned action
        // asset that keep mutating a dead bridge's state.
        private readonly List<Subscription> subscriptions = new();

        // Value-bearing actions (Value or PassThrough-2D), read each frame into CurrentAxes.
        private readonly List<(byte Id, InputAction Action)> valueActions = new();
        public readonly List<InputAxis> CurrentAxes = new(16);

        private bool initialized;
        private EntityManager manager;
        private Entity provider;
        private World world;

        private void Update()
        {
            if (provider == Entity.Null && initialized) TryCreateProvider(out provider);

            // Axis actions derive their Down/Held/Up from the MAGNITUDE crossing (reconciled against the latched
            // pressed state), not from started/canceled - reliable for Value and PassThrough-2D alike, where the
            // phase edges are not. This runs BEFORE Publish so the axis edges land in this frame's snapshot,
            // alongside the button edges the callbacks accumulated.
            CurrentAxes.Clear();
            foreach (var axis in valueActions)
            {
                var isVec2 = IsTwoDimensional(axis.Action);
                var val = isVec2
                    ? (float2)axis.Action.ReadValue<Vector2>()
                    : new float2(axis.Action.ReadValue<float>(), 0f);

                var actuated = math.lengthsq(val) > AxisPublishThresholdSq;
                var was = edges.IsPressed(axis.Id);
                if (actuated && !was) edges.Press(axis.Id);        // left neutral -> Down + Held
                else if (!actuated && was) edges.Release(axis.Id); // returned to neutral -> Up, clear Held

                if (actuated)
                    CurrentAxes.Add(new InputAxis { ActionId = axis.Id, Value = val });
            }

            // Publish this frame's edges and the latched hold, then consume the edges. Button callbacks accumulated
            // edges since the last publish; consuming here (not clearing at the top) means an edge that fired
            // before this Update is published now, and one that fires after is published next frame - never dropped.
            edges.Publish(out CurrentDown, out CurrentUp, out CurrentHeld);
        }

        private void OnEnable()
        {
            var playerInput = GetComponent<PlayerInput>();
            if (playerInput.actions == null)
            {
                Debug.LogWarning($"PlayerInputBridge on '{name}' has a PlayerInput with no actions asset assigned.", this);
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

            for (byte i = 0; i < MultiInputSettings.I.InputActions.Count; i++)
            {
                var binding = MultiInputSettings.I.InputActions[i];
                if (!TryFindAction(playerInput, binding, out var action))
                {
                    Debug.LogWarning(
                        $"PlayerInputBridge on '{name}': action slot {i} ('{(binding != null ? binding.name : "null")}') " +
                        "did not resolve in this player's actions asset; that input will never fire.", this);
                    continue;
                }

                var id = i;
                var isAxis = action.type == InputActionType.Value ||
                             (action.type == InputActionType.PassThrough && IsTwoDimensional(action));

                if (isAxis)
                {
                    // Axes reconcile edges from the magnitude crossing in Update() (see there) - reliable for any
                    // axis type. No callback subscription; the crossing IS the press/release.
                    valueActions.Add((id, action));
                }
                else
                {
                    // Buttons (and 1D actions): edges come from started/canceled callbacks - precise digital edges,
                    // sub-frame taps, and a clean release on device removal / action disable that polling misses.
                    var sub = new Subscription
                    {
                        Action = action,
                        OnStarted = _ => edges.Press(id),
                        OnCanceled = _ => edges.Release(id),
                    };
                    action.started += sub.OnStarted;
                    action.canceled += sub.OnCanceled;
                    subscriptions.Add(sub);
                }

                // Cold start: an action already actuated when we enable will not raise a fresh edge, so latch the
                // hold from the live state (a key/stick already down when the bridge enables) without a spurious Down.
                if (action.IsPressed())
                    edges.Seed(id);
            }

            // Prime CurrentHeld from the seeded holds so a provider sync that runs before the first Update() still
            // sees a coherent hold (no one-frame stale Held, and a seed-then-release nets to neutral, not an orphan Up).
            edges.Prime(out CurrentHeld);

            initialized = true;
            TryCreateProvider(out provider);
        }

        private void OnDisable()
        {
            // Symmetric unsubscribe (the leak guard the callback model requires). Must run before the provider is
            // gone so a rejoining player starts clean.
            foreach (var sub in subscriptions)
            {
                sub.Action.started -= sub.OnStarted;
                sub.Action.canceled -= sub.OnCanceled;
            }

            subscriptions.Clear();
            valueActions.Clear();

            if (world != null && world.IsCreated && manager.Exists(provider))
                RetireProvider();

            provider = Entity.Null;
            world = null;
            initialized = false;
        }

        // A player leaving mid-hold must still deliver a closing release, or a consumer waiting on the Up (a
        // CommandSequence combo) never resolves. So instead of destroying the provider now, stamp it with a final
        // "everything released" InputState, detach it from this bridge so ProviderSyncSystem stops overwriting it,
        // and tag it ProviderRetiring. ProviderRetireSystem destroys it one tick later - after the consumers have
        // read the closing Up. (Destroying here would leave no tick for anyone to read that release.)
        private void RetireProvider()
        {
            var held = manager.GetComponentData<InputState>(provider).Held;
            manager.SetComponentData(provider, new InputState { Up = held }); // Down/Held default = released
            manager.GetBuffer<InputAxis>(provider).Clear();
            manager.AddComponent<ProviderRetiring>(provider);
            manager.RemoveComponent<PlayerInputBridgeComponent>(provider); // ProviderSyncSystem now skips it
        }

        private void ClearState()
        {
            subscriptions.Clear();
            valueActions.Clear();
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
            }
            catch (InvalidOperationException)
            {
                // Wait for AsyncLoadSceneJob to finish
                entity = Entity.Null;
                return false;
            }

            return true;
        }

        private static bool IsTwoDimensional(InputAction action)
        {
            var type = action.expectedControlType;
            if (type == "Vector2" || type == "Stick" || type == "Dpad") return true;
            if (!string.IsNullOrEmpty(type)) return false;

            var controls = action.controls;
            for (var i = 0; i < controls.Count; i++)
                if (controls[i].valueType == typeof(Vector2)) return true;

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
