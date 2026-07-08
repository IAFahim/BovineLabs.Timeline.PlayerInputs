using BovineLabs.Timeline.PlayerInputs.Data;
using Unity.Entities;
using Unity.Mathematics;

namespace BovineLabs.Timeline.PlayerInputs
{
    public static class InputAccess
    {
        // Pick a seat's provider. preferSynthetic: take Synthetic if present else Human; otherwise Human if present
        // else Synthetic. A seat with only one kind filled always resolves to that kind regardless of preference.
        public static Entity Provider(in DynamicBuffer<ProviderSlot> slots, byte playerId, bool preferSynthetic)
        {
            var slot = slots[playerId];
            if (preferSynthetic)
                return slot.Synthetic != Entity.Null ? slot.Synthetic : slot.Human;

            return slot.Human != Entity.Null ? slot.Human : slot.Synthetic;
        }

        // Authority-aware state read: a consumer whose PlayerOverride is present AND enabled reads the synthetic slot
        // (the timeline feed), everyone else reads the human slot.
        public static bool TryGetState(in DynamicBuffer<ProviderSlot> slots, ComponentLookup<InputState> states,
            ComponentLookup<PlayerOverride> overrides, Entity consumer, byte playerId, out InputState state)
        {
            var preferSynthetic = overrides.HasComponent(consumer) && overrides.IsComponentEnabled(consumer);
            var provider = Provider(slots, playerId, preferSynthetic);
            if (provider != Entity.Null && states.TryGetComponent(provider, out state))
                return true;

            state = default;
            return false;
        }

        public static bool TryGetAxes(in DynamicBuffer<ProviderSlot> slots, BufferLookup<InputAxis> axes,
            ComponentLookup<PlayerOverride> overrides, Entity consumer, byte playerId, out DynamicBuffer<InputAxis> buffer)
        {
            var preferSynthetic = overrides.HasComponent(consumer) && overrides.IsComponentEnabled(consumer);
            var provider = Provider(slots, playerId, preferSynthetic);
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
