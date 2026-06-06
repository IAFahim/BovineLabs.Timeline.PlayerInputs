using Unity.Collections;
using Unity.Entities;

namespace BovineLabs.Timeline.PlayerInputs.Data
{
    public enum OverrideTrigger : byte
    {
        Manual = 0,
        AnyInput = 1,
        Action = 2
    }

    public struct InputRegistry : IComponentData
    {
        public NativeArray<Entity> ProviderByPlayer;
        public uint Version;
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
}