using BovineLabs.Core.Extensions;
using BovineLabs.Core.Iterators;
using BovineLabs.Reaction.Data.Core;
using BovineLabs.Timeline.Data;
using BovineLabs.Timeline.EntityLinks;
using BovineLabs.Timeline.EntityLinks.Data;
using BovineLabs.Timeline.PlayerInputs.Data;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace BovineLabs.Timeline.PlayerInputs
{
    [UpdateInGroup(typeof(TimelineComponentAnimationGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation |
                       WorldSystemFilterFlags.ServerSimulation)]
    public partial struct ConsumerBufferMaskSystem : ISystem
    {
        private ComponentLookup<Targets> _targetsLookup;
        private ComponentLookup<EntityLinkSource> _sources;
        private BufferLookup<EntityLinkEntry> _entries;
        private ComponentLookup<ActiveBufferMask> _masks;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            _targetsLookup = state.GetComponentLookup<Targets>(true);
            _sources = state.GetComponentLookup<EntityLinkSource>(true);
            _entries = state.GetBufferLookup<EntityLinkEntry>(true);
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

            state.Dependency = new AccumulateCommandMaskJob
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
            [ReadOnly] public ComponentLookup<Targets> TargetsLookup;
            [ReadOnly] public ComponentLookup<EntityLinkSource> Sources;
            [ReadOnly] public BufferLookup<EntityLinkEntry> Entries;
            [NativeDisableParallelForRestriction] public ComponentLookup<ActiveBufferMask> Masks;

            private void Execute(in BufferWindowConfig config, in TrackBinding binding)
            {
                if (binding.Value == Entity.Null) return;
                if (!TargetsLookup.TryGetComponent(binding.Value, out var targets)) return;

                if (!config.Consumer.TryResolve(binding.Value, targets, Sources, Entries, out var consumer)) return;

                if (!Masks.TryGetComponent(consumer, out var mask)) return;
                mask.Value = mask.Value.BitOr(config.AllowedActions);
                Masks[consumer] = mask;
            }
        }

        [BurstCompile]
        [WithAll(typeof(ClipActive))]
        private partial struct AccumulateCommandMaskJob : IJobEntity
        {
            [ReadOnly] public ComponentLookup<Targets> TargetsLookup;
            [ReadOnly] public ComponentLookup<EntityLinkSource> Sources;
            [ReadOnly] public BufferLookup<EntityLinkEntry> Entries;
            [NativeDisableParallelForRestriction] public ComponentLookup<ActiveBufferMask> Masks;

            private void Execute(in CommandSequenceConfig config, in CommandSequenceState commandState,
                EnabledRefRO<ClipActivePrevious> activePrevious, in TrackBinding binding)
            {
                if (commandState.IsCompleted && activePrevious.ValueRO) return;
                if (config.Actions.AllFalse || binding.Value == Entity.Null) return;
                if (!TargetsLookup.TryGetComponent(binding.Value, out var targets)) return;

                if (!config.Consumer.TryResolve(binding.Value, targets, Sources, Entries, out var consumer)) return;

                if (!Masks.TryGetComponent(consumer, out var mask)) return;
                mask.Value = mask.Value.BitOr(config.Actions);
                Masks[consumer] = mask;
            }
        }
    }
}