using BovineLabs.Core.EntityCommands;
using BovineLabs.Timeline.PlayerInputs.Data;

namespace BovineLabs.Timeline.PlayerInputs.Flow.Data.Builders
{
    public static class SyntheticProviderBuilder
    {
        public static void Build<T>(ref T commands, byte playerId)
            where T : struct, IEntityCommands
        {
            commands.AddComponent(new PlayerId { Value = playerId });
            commands.AddComponent<ProviderTag>();
            commands.AddComponent<SyntheticProviderTag>();
            commands.AddComponent<InputState>();
            commands.AddBuffer<InputAxis>();
        }
    }
}