using BovineLabs.Core.EntityCommands;
using Unity.Entities;

namespace BovineLabs.Timeline.PlayerInputs.Data
{
    public static class InputConsumerBuilder
    {
        public static void Build<T>(
            ref T commands,
            byte playerId,
            bool controllable,
            OverrideTrigger overrideTrigger,
            float releaseIdleSeconds)
            where T : struct, IEntityCommands
        {
            commands.AddComponent(new PlayerId { Value = playerId });
            commands.AddComponent<ConsumerTag>();
            commands.AddComponent<ActiveBufferMask>();
            commands.AddBuffer<InputHistory>();

            if (controllable)
            {
                commands.AddComponent<Controllable>();
                commands.AddComponent<PlayerOverride>();
                commands.SetComponentEnabled<PlayerOverride>(false);
                commands.AddComponent(new OverridePolicy
                {
                    Trigger = overrideTrigger,
                    TriggerActionId = 0,
                    ReleaseIdleSeconds = releaseIdleSeconds,
                });
                commands.AddComponent<OverrideState>();
            }
        }
    }
}
