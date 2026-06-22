using BovineLabs.Core.Collections;
using Unity.Entities;

namespace BovineLabs.Timeline.PlayerInputs.Data
{
    public enum CompactMode
    {
        ByPosition,
        ByActionId,
    }

    public static class HistoryCompaction
    {
        public static void Compact(DynamicBuffer<InputHistory> history, ref BitArray256 mask, CompactMode mode)
        {
            var write = 0;
            for (var read = 0; read < history.Length; read++)
            {
                if (IsMasked(history, ref mask, mode, read)) continue;
                if (write != read) history[write] = history[read];
                write++;
            }

            history.Length = write;
        }

        private static bool IsMasked(DynamicBuffer<InputHistory> history, ref BitArray256 mask, CompactMode mode,
            int read)
        {
            if (mode == CompactMode.ByActionId) return mask[history[read].ActionId];
            return mask[read];
        }
    }
}
