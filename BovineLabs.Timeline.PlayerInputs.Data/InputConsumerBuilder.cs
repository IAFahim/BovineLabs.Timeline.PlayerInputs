using BovineLabs.Core.EntityCommands;

namespace BovineLabs.Timeline.PlayerInputs.Data
{
    public static class InputConsumerBuilder
    {
        public static void Build<T>(
            ref T commands,
            byte playerId,
            bool controllable,
            OverrideTrigger overrideTrigger,
            float releaseIdleSeconds,
            ushort historyLimit = HistoryMath.DefaultLimit,
            byte overrideTriggerActionId = 0)
            where T : struct, IEntityCommands
        {
            commands.AddComponent(new PlayerId { Value = playerId });
            commands.AddComponent<ConsumerTag>();
            commands.AddComponent<ActiveBufferMask>();
            commands.AddBuffer<InputHistory>();
            commands.AddComponent(new InputHistoryLimit
            {
                Value = (ushort)HistoryMath.ClampLimit(historyLimit)
            });

            if (controllable)
            {
                commands.AddComponent<Controllable>();
                commands.AddComponent<PlayerOverride>();
                commands.SetComponentEnabled<PlayerOverride>(false);
                commands.AddComponent(new OverridePolicy
                {
                    Trigger = overrideTrigger,
                    TriggerActionId = overrideTriggerActionId,
                    ReleaseIdleSeconds = releaseIdleSeconds
                });
                commands.AddComponent<OverrideState>();
            }
        }

        public static void AddDirection<T>(
            ref T commands,
            byte actionId,
            float deadZone,
            sbyte facing)
            where T : struct, IEntityCommands
        {
            commands.AddComponent(new DirectionConfig
            {
                ActionId = actionId,
                DeadZone = deadZone,
                Facing = facing
            });
            commands.AddComponent(new DirectionState
            {
                Current = Direction.Neutral,
                Previous = Direction.Neutral,
                ChangedTick = 0
            });
        }
    }
}