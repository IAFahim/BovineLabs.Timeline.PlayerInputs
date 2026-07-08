using BovineLabs.Core.Extensions;
using BovineLabs.Core.Iterators;
using BovineLabs.Reaction.Data.Core;
using BovineLabs.Timeline.Data;
using BovineLabs.Timeline.EntityLinks;
using BovineLabs.Timeline.EntityLinks.Data;
using BovineLabs.Timeline.PlayerInputs.Data;
using Unity.Entities;

namespace BovineLabs.Timeline.PlayerInputs
{
    /// <summary>
    /// The missing "timeline takes over the player" affordance. While a <c>ControlOverrideClip</c> is active it enables
    /// <see cref="PlayerOverride"/> (so every consumer-side reader routes to the seat's synthetic slot) plus
    /// <see cref="TimelineOverride"/> (so <see cref="ControlAuthoritySystem"/> stops fighting the bit), and clears both
    /// on the exit edge. Runs before the buffer-mask/history pipeline so the override takes effect the same frame the
    /// clip opens.
    /// </summary>
    [UpdateInGroup(typeof(TimelineComponentAnimationGroup))]
    [UpdateBefore(typeof(ConsumerBufferMaskSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation |
                       WorldSystemFilterFlags.ServerSimulation)]
    public partial struct ControlOverrideSystem : ISystem
    {
        private UnsafeComponentLookup<Targets> _targets;
        private UnsafeComponentLookup<EntityLinkSource> _sources;
        private UnsafeBufferLookup<EntityLinkEntry> _entries;
        private ComponentLookup<PlayerOverride> _overrides;
        private ComponentLookup<TimelineOverride> _timelineOverrides;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ControlOverrideConfig>();
            _targets = state.GetUnsafeComponentLookup<Targets>(true);
            _sources = state.GetUnsafeComponentLookup<EntityLinkSource>(true);
            _entries = state.GetUnsafeBufferLookup<EntityLinkEntry>(true);
            _overrides = state.GetComponentLookup<PlayerOverride>(false);
            _timelineOverrides = state.GetComponentLookup<TimelineOverride>(false);
        }

        public void OnUpdate(ref SystemState state)
        {
            _targets.Update(ref state);
            _sources.Update(ref state);
            _entries.Update(ref state);
            _overrides.Update(ref state);
            _timelineOverrides.Update(ref state);

            // Enter edge (ClipActive && !ClipActivePrevious): hand the seat to the synthetic feed.
            foreach (var (config, binding) in
                     SystemAPI.Query<RefRO<ControlOverrideConfig>, RefRO<TrackBinding>>()
                         .WithAll<ClipActive>()
                         .WithNone<ClipActivePrevious>())
            {
                if (TryResolveConsumer(config.ValueRO, binding.ValueRO, out var consumer))
                    SetOverride(consumer, true);
            }

            // Exit edge (ClipActivePrevious && !ClipActive): return the seat to live human input.
            foreach (var (config, binding) in
                     SystemAPI.Query<RefRO<ControlOverrideConfig>, RefRO<TrackBinding>>()
                         .WithAll<ClipActivePrevious>()
                         .WithNone<ClipActive>())
            {
                if (TryResolveConsumer(config.ValueRO, binding.ValueRO, out var consumer))
                    SetOverride(consumer, false);
            }
        }

        private bool TryResolveConsumer(in ControlOverrideConfig config, in TrackBinding binding, out Entity consumer)
        {
            consumer = Entity.Null;
            var bound = binding.Value;
            if (bound == Entity.Null) return false;
            if (!_targets.TryGetComponent(bound, out var targets)) return false;

            return config.Consumer.TryResolve(bound, targets, _sources, _entries, out consumer);
        }

        private void SetOverride(Entity consumer, bool enabled)
        {
            // Guard: only controllable consumers carry these enableable bits (InputConsumerBuilder adds them).
            if (_overrides.HasComponent(consumer)) _overrides.SetComponentEnabled(consumer, enabled);
            if (_timelineOverrides.HasComponent(consumer)) _timelineOverrides.SetComponentEnabled(consumer, enabled);
        }
    }
}
