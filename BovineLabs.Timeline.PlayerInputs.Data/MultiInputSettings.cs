using System;
using System.Collections.Generic;
using BovineLabs.Core.Keys;
using BovineLabs.Core.Settings;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BovineLabs.Timeline.PlayerInputs.Data
{
    [SettingsGroup("Input")]
    public sealed partial class MultiInputSettings : KSettingsBase<MultiInputSettings, byte>
    {
        public const int MaxActions = 255;

        [SerializeField] private InputActionReference[] inputActions = Array.Empty<InputActionReference>();

        [SerializeField]
        [Tooltip("Magnitude an analog (stick/axis) action must exceed before the bridge synthesises a Down/Up edge for " +
                 "combo history. Kept ABOVE typical stick drift (1-5%) so a resting pad does not flood InputHistory. " +
                 "Axis VALUES still stream from a much smaller threshold; this only gates edge synthesis. " +
                 "Prefer an action-level deadzone processor where possible; this is the fallback floor.")]
        [Range(0f, 1f)]
        private float axisEdgeDeadzone = AxisEdge.DefaultDeadzone;

        public IReadOnlyList<InputActionReference> InputActions => inputActions;

        /// <summary>
        /// Deadzone magnitude used by <see cref="AxisEdge"/> to decide when an axis action counts as "pressed" for
        /// Down/Up edge synthesis. See the serialized field tooltip for the full contract.
        /// </summary>
        public float AxisEdgeDeadzone => this.axisEdgeDeadzone;

        /// <summary> Convenience accessor - falls back to <see cref="AxisEdge.DefaultDeadzone"/> when no settings asset exists. </summary>
        public static float AxisEdgeDeadzoneOrDefault => I != null ? I.axisEdgeDeadzone : AxisEdge.DefaultDeadzone;

        public override IEnumerable<NameValue<byte>> Keys
        {
            get
            {
                var count = Math.Min(inputActions.Length, MaxActions);
                var seen = new HashSet<string>();
                for (var i = 0; i < count; i++)
                {
                    var id = (byte)i;
                    var binding = inputActions[i];
                    var actionName = binding?.action != null ? binding.action.name : $"[Unassigned: {id}]";
                    while (!seen.Add(actionName))
                        actionName += "'";
                    yield return new NameValue<byte>(actionName, id);
                }
            }
        }

        public bool TryGet(InputActionReference reference, out byte index)
        {
            index = 0;
            if (reference?.action == null) return false;

            var count = Math.Min(inputActions.Length, MaxActions);

            for (var i = 0; i < count; i++)
            {
                var input = inputActions[i];
                if (input?.action != null && input.action.id == reference.action.id)
                {
                    index = (byte)i;
                    return true;
                }
            }

            for (var i = 0; i < count; i++)
            {
                var input = inputActions[i];
                if (input?.action != null && input.action.name == reference.action.name)
                {
                    index = (byte)i;
                    return true;
                }
            }

            return false;
        }

        public static bool TryGetIndex(InputActionReference reference, out byte index)
        {
            if (I != null) return I.TryGet(reference, out index);
            index = 0;
            return false;
        }
    }

    /// <summary>
    /// Pure, Burst-friendly deadzone + re-press hysteresis math for turning an analog axis value into a boolean
    /// "actuated" state that drives Down/Up edge synthesis in <see cref="PlayerInputBridge"/>. Split out so it is
    /// trivially unit-testable and has no dependency on the InputSystem or on a live settings asset.
    ///
    /// Two bands avoid chatter around the boundary: the value must exceed the (upper) press band to actuate, and must
    /// then fall below the (lower) release band to de-actuate. A stick hovering exactly on the deadzone therefore emits
    /// at most one edge, not a stream of them.
    /// </summary>
    public static class AxisEdge
    {
        /// <summary> Default edge deadzone magnitude (~12.5%), matching common gamepad platform conventions. </summary>
        public const float DefaultDeadzone = 0.125f;

        /// <summary> Release band as a fraction of the press band; &lt;1 gives the hysteresis gap that kills chatter. </summary>
        public const float ReleaseRatio = 0.9f;

        /// <summary>
        /// Returns whether <paramref name="value"/> counts as actuated for edge purposes, applying press/release
        /// hysteresis around <paramref name="deadzone"/>.
        /// </summary>
        /// <param name="value"> The axis value this frame (x, or xy for a 2D stick). </param>
        /// <param name="wasActuated"> Whether the axis was actuated last frame (the current latched edge state). </param>
        /// <param name="deadzone"> The press-band magnitude; the release band is <see cref="ReleaseRatio"/> of it. </param>
        public static bool Actuated(float2 value, bool wasActuated, float deadzone)
        {
            var dz = math.max(0f, deadzone);
            var press = dz * dz;
            var releaseMag = dz * ReleaseRatio;
            var release = releaseMag * releaseMag;
            var magSq = math.lengthsq(value);

            // Latch: once actuated, stay actuated until the value drops below the (lower) release band.
            return wasActuated ? magSq >= release : magSq > press;
        }
    }
}