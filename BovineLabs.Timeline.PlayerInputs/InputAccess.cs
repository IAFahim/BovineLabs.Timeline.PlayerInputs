using BovineLabs.Timeline.PlayerInputs.Data;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace BovineLabs.Timeline.PlayerInputs
{
    public static class InputAccess
    {
        public static Entity Provider(NativeArray<Entity> registry, byte playerId)
        {
            return registry[playerId];
        }

        public static bool TryGetState(NativeArray<Entity> registry, ComponentLookup<InputState> states,
            byte playerId, out InputState state)
        {
            var provider = registry[playerId];
            if (provider != Entity.Null && states.TryGetComponent(provider, out state))
                return true;

            state = default;
            return false;
        }

        public static bool TryGetAxes(NativeArray<Entity> registry, BufferLookup<InputAxis> axes,
            byte playerId, out DynamicBuffer<InputAxis> buffer)
        {
            var provider = registry[playerId];
            if (provider != Entity.Null && axes.TryGetBuffer(provider, out buffer))
                return true;

            buffer = default;
            return false;
        }

        public static float2 ReadAxis(DynamicBuffer<InputAxis> axes, byte actionId)
        {
            for (var i = 0; i < axes.Length; i++)
                if (axes[i].ActionId == actionId)
                    return axes[i].Value;

            return float2.zero;
        }
    }
}
