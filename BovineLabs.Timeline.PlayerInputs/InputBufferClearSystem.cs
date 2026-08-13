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
    [UpdateAfter(typeof(ConsumerBufferMaskSystem))]
    [UpdateBefore(typeof(ConsumerHistorySystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation |
                       WorldSystemFilterFlags.ServerSimulation)]
    public partial struct InputBufferClearSystem : ISystem
    {
        private ComponentLookup<Targets> _targetsLookup;
        private ComponentLookup<EntityLinkSource> _sources;
        private BufferLookup<EntityLinkEntry> _entries;
        private BufferLookup<InputHistory> _histories;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            _targetsLookup = state.GetComponentLookup<Targets>(true);
            _sources = state.GetComponentLookup<EntityLinkSource>(true);
            _entries = state.GetBufferLookup<EntityLinkEntry>(true);
            _histories = state.GetBufferLookup<InputHistory>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            _targetsLookup.Update(ref state);
            _sources.Update(ref state);
            _entries.Update(ref state);
            _histories.Update(ref state);
            state.Dependency = new ClearBufferJob
            {
                TargetsLookup = _targetsLookup,
                Sources = _sources,
                Entries = _entries,
                Histories = _histories
            }.Schedule(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(ClipActive), typeof(BufferClearConfig))]
        [WithNone(typeof(ClipActivePrevious))]
        private partial struct ClearBufferJob : IJobEntity
        {
            [ReadOnly] public ComponentLookup<Targets> TargetsLookup;
            [ReadOnly] public ComponentLookup<EntityLinkSource> Sources;
            [ReadOnly] public BufferLookup<EntityLinkEntry> Entries;
            public BufferLookup<InputHistory> Histories;

            private void Execute(in BufferClearConfig config, in TrackBinding binding)
            {
                if (binding.Value == Entity.Null) return;
                if (!TargetsLookup.TryGetComponent(binding.Value, out var targets)) return;

                if (!config.Consumer.TryResolve(binding.Value, targets, Sources, Entries, out var consumer)) return;

                if (!Histories.TryGetBuffer(consumer, out var history)) return;

                if (config.ClearAll)
                {
                    history.Clear();
                    return;
                }

                // Actions were requested but none resolved (AllFalse mask, ClearAll false): compact removes
                // nothing — a harmless no-op — rather than silently wiping the entire history.
                if (config.ActionMask.AllFalse)
                {
                    return;
                }

                var mask = config.ActionMask;
                HistoryCompaction.Compact(history, ref mask, CompactMode.ByActionId);
            }
        }
    }
}