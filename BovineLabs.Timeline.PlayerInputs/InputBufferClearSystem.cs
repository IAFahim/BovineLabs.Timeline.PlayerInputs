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
    [UpdateAfter(typeof(CommandSequenceSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation |
                       WorldSystemFilterFlags.ServerSimulation)]
    public partial struct InputBufferClearSystem : ISystem
    {
        private UnsafeComponentLookup<Targets> _targetsLookup;
        private UnsafeComponentLookup<EntityLinkSource> _sources;
        private UnsafeBufferLookup<EntityLinkEntry> _entries;
        private BufferLookup<InputHistory> _histories;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            _targetsLookup = state.GetUnsafeComponentLookup<Targets>(true);
            _sources = state.GetUnsafeComponentLookup<EntityLinkSource>(true);
            _entries = state.GetUnsafeBufferLookup<EntityLinkEntry>(true);
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
            [ReadOnly] public UnsafeComponentLookup<Targets> TargetsLookup;
            [ReadOnly] public UnsafeComponentLookup<EntityLinkSource> Sources;
            [ReadOnly] public UnsafeBufferLookup<EntityLinkEntry> Entries;
            public BufferLookup<InputHistory> Histories;

            private void Execute(in BufferClearConfig config, in TrackBinding binding)
            {
                if (binding.Value == Entity.Null) return;
                if (!TargetsLookup.TryGetComponent(binding.Value, out var targets)) return;

                if (!EntityLinkResolver.TryResolve(
                        binding.Value, targets, config.ReadRootFrom, config.ConsumerLinkKey,
                        Sources, Entries, out var consumer)) return;

                if (!Histories.TryGetBuffer(consumer, out var history)) return;

                if (config.ActionMask.AllFalse)
                {
                    history.Clear();
                    return;
                }

                var write = 0;
                for (var read = 0; read < history.Length; read++)
                {
                    if (config.ActionMask[history[read].ActionId]) continue;
                    if (write != read) history[write] = history[read];
                    write++;
                }

                history.Length = write;
            }
        }
    }
}