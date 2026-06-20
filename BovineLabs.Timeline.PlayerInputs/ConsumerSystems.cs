using BovineLabs.Core.Extensions;
using BovineLabs.Core.Iterators;
using BovineLabs.Reaction.Data.Core;
using BovineLabs.Timeline.Data;
using BovineLabs.Timeline.EntityLinks;
using BovineLabs.Timeline.EntityLinks.Data;
using BovineLabs.Timeline.PlayerInputs.Data;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace BovineLabs.Timeline.PlayerInputs
{
    [UpdateInGroup(typeof(TimelineComponentAnimationGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation |
                       WorldSystemFilterFlags.ServerSimulation)]
    public partial struct ConsumerBufferMaskSystem : ISystem
    {
        private UnsafeComponentLookup<Targets> _targetsLookup;
        private UnsafeComponentLookup<EntityLinkSource> _sources;
        private UnsafeBufferLookup<EntityLinkEntry> _entries;
        private ComponentLookup<ActiveBufferMask> _masks;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            _targetsLookup = state.GetUnsafeComponentLookup<Targets>(true);
            _sources = state.GetUnsafeComponentLookup<EntityLinkSource>(true);
            _entries = state.GetUnsafeBufferLookup<EntityLinkEntry>(true);
            _masks = state.GetComponentLookup<ActiveBufferMask>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            _targetsLookup.Update(ref state);
            _sources.Update(ref state);
            _entries.Update(ref state);
            _masks.Update(ref state);

            state.Dependency = new ResetMaskJob().ScheduleParallel(state.Dependency);
            state.Dependency = new AccumulateMaskJob
            {
                TargetsLookup = _targetsLookup,
                Sources = _sources,
                Entries = _entries,
                Masks = _masks
            }.Schedule(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(ConsumerTag))]
        private partial struct ResetMaskJob : IJobEntity
        {
            private void Execute(ref ActiveBufferMask mask)
            {
                mask.Value = default;
            }
        }

        [BurstCompile]
        [WithAll(typeof(ClipActive))]
        private partial struct AccumulateMaskJob : IJobEntity
        {
            [ReadOnly] public UnsafeComponentLookup<Targets> TargetsLookup;
            [ReadOnly] public UnsafeComponentLookup<EntityLinkSource> Sources;
            [ReadOnly] public UnsafeBufferLookup<EntityLinkEntry> Entries;
            [NativeDisableParallelForRestriction] public ComponentLookup<ActiveBufferMask> Masks;

            private void Execute(in BufferWindowConfig config, in TrackBinding binding)
            {
                if (binding.Value == Entity.Null) return;
                if (!TargetsLookup.TryGetComponent(binding.Value, out var targets)) return;

                if (!EntityLinkResolver.TryResolve(
                        binding.Value, targets, config.ReadRootFrom, config.ConsumerLinkKey,
                        Sources, Entries, out var consumer)) return;

                if (!Masks.TryGetComponent(consumer, out var mask)) return;
                mask.Value = mask.Value.BitOr(config.AllowedActions);
                Masks[consumer] = mask;
            }
        }
    }
}