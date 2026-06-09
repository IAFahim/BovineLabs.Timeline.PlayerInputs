using BovineLabs.Core.Authoring.EntityCommands;
using BovineLabs.Timeline.PlayerInputs.Data;
using Unity.Entities;
using UnityEngine;

namespace BovineLabs.Timeline.PlayerInputs.Authoring
{
    public class InputConsumerAuthoring : MonoBehaviour
    {
        public byte PlayerId;

        public bool Controllable;
        public OverrideTrigger OverrideTrigger = OverrideTrigger.AnyInput;
        public float ReleaseIdleSeconds = 0.25f;

        [Range(1, 256)]
        [Tooltip("Max buffered input transitions kept for this consumer. Oldest entries are evicted first.")]
        public ushort HistoryLimit = 64;

        [Header("Direction (optional)")]
        public bool TrackDirection;

        [Tooltip("Axis action quantised into an eight-way Direction each tick.")]
        public UnityEngine.InputSystem.InputActionReference DirectionAction;

        [Range(0f, 1f)] public float DirectionDeadZone = 0.3f;

        [Tooltip("+1 if the character faces +X, -1 if it faces -X. Flips Back/Forward.")]
        public sbyte DirectionFacing = 1;

        public class Baker : Baker<InputConsumerAuthoring>
        {
            public override void Bake(InputConsumerAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);
                var commands = new BakerCommands(this, entity);
                InputConsumerBuilder.Build(
                    ref commands,
                    authoring.PlayerId,
                    authoring.Controllable,
                    authoring.OverrideTrigger,
                    authoring.ReleaseIdleSeconds,
                    authoring.HistoryLimit);

                if (authoring.TrackDirection)
                {
                    byte actionId = 0;
                    if (authoring.DirectionAction != null &&
                        !MultiInputSettingsAuthoringUtility.TryGetIndex(authoring.DirectionAction, out actionId))
                        UnityEngine.Debug.LogError(
                            $"InputConsumerAuthoring direction action '{authoring.DirectionAction.name}' " +
                            "not found in MultiInputSettings.", authoring);

                    var facing = authoring.DirectionFacing >= 0 ? (sbyte)1 : (sbyte)-1;
                    InputConsumerBuilder.AddDirection(ref commands, actionId, authoring.DirectionDeadZone, facing);
                }
            }
        }
    }
}