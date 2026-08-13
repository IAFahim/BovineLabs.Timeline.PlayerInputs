using BovineLabs.Core.Collections;
using BovineLabs.Core.Extensions;
using BovineLabs.Core.Iterators;
using BovineLabs.Reaction.Conditions;
using BovineLabs.Reaction.Data.Core;
using BovineLabs.Timeline.Data;
using BovineLabs.Timeline.EntityLinks;
using BovineLabs.Timeline.EntityLinks.Data;
using BovineLabs.Timeline.PlayerInputs.Data;
using Unity.Burst;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;

namespace BovineLabs.Timeline.PlayerInputs
{
    // Cross-clip CommandSequence evaluation order. Lower Priority first (first crack at Consuming shared history);
    // the clip entity is the stable tiebreak so the order is deterministic within a run.
    internal struct ClipSortKey : System.IComparable<ClipSortKey>
    {
        public int Priority;
        public Entity Clip;

        public int CompareTo(ClipSortKey other)
        {
            return Priority != other.Priority ? Priority.CompareTo(other.Priority) : Clip.CompareTo(other.Clip);
        }
    }

    [UpdateInGroup(typeof(TimelineComponentAnimationGroup))]
    [UpdateAfter(typeof(CommandSequenceResetSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation |
                       WorldSystemFilterFlags.ServerSimulation)]
    public partial struct CommandSequenceSystem : ISystem
    {
        private ConditionEventDispatch _dispatch;

        private ComponentLookup<Targets> _targetsLookup;
        private ComponentLookup<EntityLinkSource> _sources;
        private BufferLookup<EntityLinkEntry> _entries;
        private ComponentLookup<InputState> _states;
        private ComponentLookup<PlayerId> _playerIds;
        private ComponentLookup<PlayerOverride> _overrides;
        private BufferLookup<InputHistory> _histories;

        private ComponentLookup<CommandSequenceConfig> _configs;
        private ComponentLookup<TrackBinding> _bindings;
        private ComponentLookup<CommandSequenceState> _commandStates;

        private EntityQuery _clipQuery;

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            _dispatch.Dispose();
        }

        [BurstCompile]
        public void OnCreate(ref SystemState state)

        {
            state.RequireForUpdate<InputRegistry>();

            _dispatch.Create(ref state);

            _targetsLookup = state.GetComponentLookup<Targets>(true);
            _sources = state.GetComponentLookup<EntityLinkSource>(true);
            _entries = state.GetBufferLookup<EntityLinkEntry>(true);
            _states = state.GetComponentLookup<InputState>(true);
            _playerIds = state.GetComponentLookup<PlayerId>(true);
            _overrides = state.GetComponentLookup<PlayerOverride>(true);
            _histories = state.GetBufferLookup<InputHistory>();

            _configs = state.GetComponentLookup<CommandSequenceConfig>(true);
            _bindings = state.GetComponentLookup<TrackBinding>(true);
            _commandStates = state.GetComponentLookup<CommandSequenceState>();

            _clipQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAllRW<CommandSequenceState>()
                .WithAll<CommandSequenceConfig, TrackBinding, ClipActive>()
                .Build(ref state);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            _dispatch.Update(ref state);
            _targetsLookup.Update(ref state);
            _sources.Update(ref state);
            _entries.Update(ref state);
            _states.Update(ref state);
            _playerIds.Update(ref state);
            _overrides.Update(ref state);
            _histories.Update(ref state);
            _configs.Update(ref state);
            _bindings.Update(ref state);
            _commandStates.Update(ref state);

            // Fresh per-frame set (auto-freed): sizing to the live clip count removes the old fixed-64 overflow, and
            // allocating anew each frame removes the main-thread Clear()-vs-in-flight-job race on a persistent set.
            var uniqueKeySet = new NativeParallelHashSet<Entity>(
                math.max(1, _clipQuery.CalculateEntityCount()), state.WorldUpdateAllocator);

            var activeClips = _clipQuery.ToEntityListAsync(Allocator.TempJob, state.Dependency, out var gatherInput);
            state.Dependency = gatherInput;

            state.Dependency = new GatherJob
            {
                Clips = activeClips,
                EventChanges = _dispatch.EventWriter,
                UniqueKeys = uniqueKeySet.AsParallelWriter(),
                Configs = _configs,
                Bindings = _bindings,
                CommandStates = _commandStates,
                TargetsLookup = _targetsLookup,
                Sources = _sources,
                Entries = _entries,
                Slots = SystemAPI.GetSingletonBuffer<ProviderSlot>(true),
                States = _states,
                PlayerIds = _playerIds,
                Overrides = _overrides,
                Histories = _histories
            }.Schedule(state.Dependency);

            state.Dependency = activeClips.Dispose(state.Dependency);

            state.Dependency = _dispatch.Flush(uniqueKeySet, state.Dependency);
        }

        [BurstCompile]
        private struct GatherJob : IJob
        {
            public NativeList<Entity> Clips;

            public NativeParallelMultiHashMapFallback<Entity, EventAmount>.ParallelWriter EventChanges;
            public NativeParallelHashSet<Entity>.ParallelWriter UniqueKeys;

            [ReadOnly] public ComponentLookup<CommandSequenceConfig> Configs;
            [ReadOnly] public ComponentLookup<TrackBinding> Bindings;
            public ComponentLookup<CommandSequenceState> CommandStates;

            [ReadOnly] public ComponentLookup<Targets> TargetsLookup;
            [ReadOnly] public ComponentLookup<EntityLinkSource> Sources;
            [ReadOnly] public BufferLookup<EntityLinkEntry> Entries;

            [ReadOnly] public DynamicBuffer<ProviderSlot> Slots;

            [ReadOnly] public ComponentLookup<InputState> States;

            [ReadOnly] public ComponentLookup<PlayerId> PlayerIds;

            [ReadOnly] public ComponentLookup<PlayerOverride> Overrides;

            public BufferLookup<InputHistory> Histories;

            public void Execute()
            {
                // Cross-clip order is author-controlled: sort by (Priority asc, Entity asc). Lower Priority gets the
                // first crack at Consuming shared history; the Entity tiebreak keeps it deterministic per run even
                // when CoreCLR recycles entity indices. (Raw Clips.Sort() was entity-order only - meaningless to a
                // designer, and it shifted with scene/subscene load order.)
                var keys = new NativeList<ClipSortKey>(Clips.Length, Allocator.Temp);
                for (var c = 0; c < Clips.Length; c++)
                {
                    var clip = Clips[c];
                    keys.Add(new ClipSortKey { Priority = Configs[clip].Priority, Clip = clip });
                }

                keys.Sort();

                for (var c = 0; c < keys.Length; c++)
                {
                    var clip = keys[c].Clip;
                    var commandState = CommandStates[clip];
                    Evaluate(clip, ref commandState, Configs[clip], Bindings[clip]);
                    CommandStates[clip] = commandState;
                }

                keys.Dispose();
            }

            private void Evaluate(Entity clip, ref CommandSequenceState commandState,
                in CommandSequenceConfig config, in TrackBinding binding)
            {
                if (commandState.IsCompleted || binding.Value == Entity.Null) return;
                if (!TargetsLookup.TryGetComponent(binding.Value, out var targets)) return;

                if (!config.Consumer.TryResolve(binding.Value, targets, Sources, Entries, out var consumer)) return;

                if (!PlayerIds.TryGetComponent(consumer, out var pid)) return;
                if (!InputAccess.TryGetState(Slots, States, Overrides, consumer, pid.Value, out var state)) return;
                if (!Histories.TryGetBuffer(consumer, out var history)) return;

                ref var sequences = ref config.Blob.Value.Sequences;

                for (var s = 0; s < sequences.Length; s++)
                {
                    ref var seq = ref sequences[s];
                    if (seq.Steps.Length == 0) continue;

                    var consumeMask = default(BitArray256);
                    var searchIndex = 0;
                    var matched = true;

                    var window = default(MatchWindow);

                    for (var i = 0; i < seq.Steps.Length; i++)
                        if (!CommandMatcher.Evaluate(ref seq.Steps[i], state, history, ref consumeMask,
                                ref searchIndex, ref window))
                        {
                            matched = false;
                            break;
                        }

                    if (!matched) continue;

                    CommitConsumes(history, ref consumeMask);

                    if (InputRouting.TryResolveRoute(binding.Value, targets, config.EventRoute,
                            Sources, Entries, out var routeTarget))
                    {
                        EventChanges.Add(routeTarget, new EventAmount(seq.Condition, seq.Value));
                        UniqueKeys.Add(routeTarget);
                    }

                    if (seq.Repeat == 0) commandState.IsCompleted = true;
                    return;
                }
            }

            private static void CommitConsumes(DynamicBuffer<InputHistory> history, ref BitArray256 consumeMask)
            {
                if (consumeMask.AllFalse) return;
                HistoryCompaction.Compact(history, ref consumeMask, CompactMode.ByPosition);
            }
        }
    }
}