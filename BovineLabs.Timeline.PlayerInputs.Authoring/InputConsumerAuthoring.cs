using BovineLabs.Core.Authoring.EntityCommands;
using BovineLabs.Timeline.PlayerInputs.Data;
using Unity.Entities;
using UnityEngine;

namespace BovineLabs.Timeline.PlayerInputs.Authoring
{
    public class InputConsumerAuthoring : MonoBehaviour
    {
        [Tooltip("Which joined player this consumer reads input from.")]
        public byte PlayerId;

        [Tooltip("When enabled, this consumer can be taken over by a timeline so authored input overrides the live player.")]
        public bool Controllable;

        [Tooltip("Only takes effect when Controllable is enabled. Selects which input edge hands control to the override.")]
        public OverrideTrigger OverrideTrigger = OverrideTrigger.AnyInput;

        [Tooltip("Only used when Controllable and OverrideTrigger=Action: the action whose press/hold hands control to the override.")]
        public UnityEngine.InputSystem.InputActionReference OverrideAction;

        [Tooltip("Only takes effect when Controllable is enabled. Seconds of input idle before control is released back.")]
        public float ReleaseIdleSeconds = 0.25f;

        [Range(1, 256)]
        [Tooltip("Max buffered input transitions kept for this consumer. Oldest entries are evicted first.")]
        public ushort HistoryLimit = 64;

        [Header("Direction (optional)")]
        [Tooltip("Enable to quantise a movement axis into an eight-way Direction each tick.")]
        public bool TrackDirection;

        [Tooltip("Axis action quantised into an eight-way Direction each tick.")]
        public UnityEngine.InputSystem.InputActionReference DirectionAction;

        [Range(0f, 1f)]
        [Tooltip("Axis magnitude below this is treated as no direction.")]
        public float DirectionDeadZone = 0.3f;

        [Tooltip("Only the sign matters: >=0 faces +X (+1 forward), <0 faces -X (-1 back). " +
                 "Other magnitudes are coerced to +/-1 at bake. Flips Back/Forward.")]
        public sbyte DirectionFacing = 1;

        public class Baker : Baker<InputConsumerAuthoring>
        {
            public override void Bake(InputConsumerAuthoring authoring)
            {
                MultiInputSettingsAuthoringUtility.DependsOnSettings(this);

                var entity = GetEntity(TransformUsageFlags.None);
                var commands = new BakerCommands(this, entity);

                byte overrideActionId = 0;
                if (authoring.Controllable && authoring.OverrideTrigger == OverrideTrigger.Action)
                {
                    if (authoring.OverrideAction == null)
                    {
                        overrideActionId = byte.MaxValue;
                        UnityEngine.Debug.LogError(
                            $"InputConsumerAuthoring '{authoring.name}' uses OverrideTrigger=Action but no OverrideAction is " +
                            "assigned; override will never engage.", authoring);
                    }
                    else if (!MultiInputSettingsAuthoringUtility.TryGetIndex(authoring.OverrideAction, out overrideActionId))
                    {
                        overrideActionId = byte.MaxValue;
                        UnityEngine.Debug.LogError(
                            $"InputConsumerAuthoring override action '{authoring.OverrideAction.name}' " +
                            "not found in MultiInputSettings.", authoring);
                    }
                }

                InputConsumerBuilder.Build(
                    ref commands,
                    authoring.PlayerId,
                    authoring.Controllable,
                    authoring.OverrideTrigger,
                    authoring.ReleaseIdleSeconds,
                    authoring.HistoryLimit,
                    overrideActionId);

                if (authoring.TrackDirection)
                {
                    byte actionId = byte.MaxValue;
                    if (authoring.DirectionAction == null)
                        UnityEngine.Debug.LogError(
                            $"InputConsumerAuthoring '{authoring.name}' has TrackDirection enabled but no DirectionAction " +
                            "assigned; no direction will be produced.", authoring);
                    else if (!MultiInputSettingsAuthoringUtility.TryGetIndex(authoring.DirectionAction, out actionId))
                    {
                        actionId = byte.MaxValue;
                        UnityEngine.Debug.LogError(
                            $"InputConsumerAuthoring direction action '{authoring.DirectionAction.name}' " +
                            "not found in MultiInputSettings.", authoring);
                    }

                    var facing = authoring.DirectionFacing >= 0 ? (sbyte)1 : (sbyte)-1;
                    InputConsumerBuilder.AddDirection(ref commands, actionId, authoring.DirectionDeadZone, facing);
                }
            }
        }
    }
}