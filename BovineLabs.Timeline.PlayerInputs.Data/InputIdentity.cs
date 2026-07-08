using BovineLabs.Timeline.EntityLinks.Data;
using Unity.Entities;

namespace BovineLabs.Timeline.PlayerInputs.Data
{
    public enum OverrideTrigger : byte
    {
        Manual = 0,
        AnyInput = 1,
        Action = 2
    }

    // The registry singleton marker. The seat->provider table now lives in a dependency-tracked
    // DynamicBuffer<ProviderSlot> on the same entity (see InputRegistrySystem); this component only carries the
    // change counter. The TYPE must keep existing as an IComponentData: the source generator emits
    // RequireForUpdate<InputRegistry>() on every typed-input projection system.
    public struct InputRegistry : IComponentData
    {
        public uint Version;
    }

    // One seat's two provider slots. Human = the live PlayerInputBridge provider; Synthetic = the baked/timeline
    // synthetic provider that can take the seat over. Both non-null on the same seat is VALID - that is exactly the
    // takeover topology (an override consumer reads Synthetic, everything else reads Human).
    [InternalBufferCapacity(0)]
    public struct ProviderSlot : IBufferElementData
    {
        public Entity Human;
        public Entity Synthetic;
    }

    // Stable creation sequence stamped on each provider, used to break same-kind duplicate ties deterministically
    // (lowest value wins) instead of relying on Entity.Index, which CoreCLR recycles across runs.
    public struct ProviderSeq : IComponentData
    {
        public uint Value;
    }

    public struct PlayerJoined : IBufferElementData
    {
        public byte PlayerId;
        public Entity Provider;
    }

    public struct PlayerLeft : IBufferElementData
    {
        public byte PlayerId;
    }

    public struct Controllable : IComponentData
    {
    }

    public struct PlayerOverride : IComponentData, IEnableableComponent
    {
    }

    // Enabled while a ControlOverride timeline clip is driving the consumer. When set, ControlAuthoritySystem leaves
    // the PlayerOverride bit alone (the clip owns it); the clip clears both on its exit edge.
    public struct TimelineOverride : IComponentData, IEnableableComponent
    {
    }

    public struct OverridePolicy : IComponentData
    {
        public OverrideTrigger Trigger;
        public byte TriggerActionId;
        public float ReleaseIdleSeconds;
    }

    public struct OverrideState : IComponentData
    {
        public float IdleSeconds;
    }

    // Baked by ControlOverrideClip. While the clip is active it enables PlayerOverride + TimelineOverride on the
    // resolved consumer, handing input for that seat to the synthetic slot.
    public struct ControlOverrideConfig : IComponentData
    {
        public EntityLinkRef Consumer;
    }
}
