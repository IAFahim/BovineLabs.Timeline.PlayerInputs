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
            // Baked synthetics can't use the bridge's static counter; stamp the max so they tie-break LAST in a
            // synthetic-vs-synthetic duplicate (they occupy their own slot, so this only matters against each other).
            commands.AddComponent(new ProviderSeq { Value = uint.MaxValue });
            commands.AddComponent<ProviderTag>();
            commands.AddComponent<SyntheticProviderTag>();
            commands.AddComponent<InputState>();
            commands.AddBuffer<InputAxis>();
        }
    }
}