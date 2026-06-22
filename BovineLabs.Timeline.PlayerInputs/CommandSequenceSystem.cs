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
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;

namespace BovineLabs.Timeline.PlayerInputs
{
    [UpdateInGroup(typeof(TimelineComponentAnimationGroup))]
    [UpdateAfter(typeof(CommandSequenceResetSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation |
                       WorldSystemFilterFlags.ServerSimulation)]
    public partial struct CommandSequenceSystem : ISystem
    {
        private NativeParallelMultiHashMapFallback<Entity, EventAmount> _eventChanges;
        private NativeParallelHashSet<Entity> _uniqueKeySet;
        private NativeList<Entity> _uniqueKeys;
        private ConditionEventWriter.Lookup _writers;

        private UnsafeComponentLookup<Targets> _targetsLookup;
        private UnsafeComponentLookup<EntityLinkSource> _sources;
        private UnsafeBufferLookup<EntityLinkEntry> _entries;
        private ComponentLookup<InputState> _states;
        private ComponentLookup<PlayerId> _playerIds;
        private BufferLookup<InputHistory> _histories;

        private ComponentLookup<CommandSequenceConfig> _configs;
        private ComponentLookup<TrackBinding> _bindings;
        private ComponentLookup<CommandSequenceState> _commandStates;

        private EntityQuery _clipQuery;

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            if (_uniqueKeys.IsCreated)
            {
                _eventChanges.Dispose();
                _uniqueKeySet.Dispose();
                _uniqueKeys.Dispose();
            }
        }

        [BurstCompile]
        public void OnCreate(ref SystemState state)

        {
            state.RequireForUpdate<InputRegistry>();

            _eventChanges = new NativeParallelMultiHashMapFallback<Entity, EventAmount>(64, Allocator.Persistent);
            _uniqueKeySet = new NativeParallelHashSet<Entity>(64, Allocator.Persistent);
            _uniqueKeys = new NativeList<Entity>(64, Allocator.Persistent);
            _writers.Create(ref state);

            _targetsLookup = state.GetUnsafeComponentLookup<Targets>(true);
            _sources = state.GetUnsafeComponentLookup<EntityLinkSource>(true);
            _entries = state.GetUnsafeBufferLookup<EntityLinkEntry>(true);
            _states = state.GetComponentLookup<InputState>(true);
            _playerIds = state.GetComponentLookup<PlayerId>(true);
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
            _writers.Update(ref state);
            _targetsLookup.Update(ref state);
            _sources.Update(ref state);
            _entries.Update(ref state);
            _states.Update(ref state);
            _playerIds.Update(ref state);
            _histories.Update(ref state);
            _configs.Update(ref state);
            _bindings.Update(ref state);
            _commandStates.Update(ref state);

            var registry = SystemAPI.GetSingleton<InputRegistry>();

            _uniqueKeySet.Clear();

            var activeClips = _clipQuery.ToEntityListAsync(Allocator.TempJob, state.Dependency, out var gatherInput);
            state.Dependency = gatherInput;

            state.Dependency = new GatherJob
            {
                Clips = activeClips,
                EventChanges = _eventChanges.AsWriter(),
                UniqueKeys = _uniqueKeySet.AsParallelWriter(),
                Configs = _configs,
                Bindings = _bindings,
                CommandStates = _commandStates,
                TargetsLookup = _targetsLookup,
                Sources = _sources,
                Entries = _entries,
                Registry = registry.ProviderByPlayer,
                States = _states,
                PlayerIds = _playerIds,
                Histories = _histories
            }.Schedule(state.Dependency);

            state.Dependency = activeClips.Dispose(state.Dependency);

            state.Dependency = _eventChanges.Apply(state.Dependency, out var reader);

            state.Dependency = new CollectEventKeysJob
            {
                UniqueKeys = _uniqueKeys,
                UniqueKeySet = _uniqueKeySet
            }.Schedule(state.Dependency);

            state.Dependency = new TriggerEventsJob
            {
                Keys = _uniqueKeys.AsDeferredJobArray(),
                GroupChanges = reader,
                Writers = _writers
            }.Schedule(_uniqueKeys, 64, state.Dependency);

            state.Dependency = _eventChanges.Clear(state.Dependency);
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

            [ReadOnly] public UnsafeComponentLookup<Targets> TargetsLookup;
            [ReadOnly] public UnsafeComponentLookup<EntityLinkSource> Sources;
            [ReadOnly] public UnsafeBufferLookup<EntityLinkEntry> Entries;

            [ReadOnly] [NativeDisableContainerSafetyRestriction]
            public NativeArray<Entity> Registry;

            [ReadOnly] [NativeDisableContainerSafetyRestriction]
            public ComponentLookup<InputState> States;

            [ReadOnly] [NativeDisableContainerSafetyRestriction]
            public ComponentLookup<PlayerId> PlayerIds;

            public BufferLookup<InputHistory> Histories;

            public void Execute()
            {
                Clips.Sort();

                for (var c = 0; c < Clips.Length; c++)
                {
                    var clip = Clips[c];
                    var commandState = CommandStates[clip];
                    Evaluate(clip, ref commandState, Configs[clip], Bindings[clip]);
                    CommandStates[clip] = commandState;
                }
            }

            private void Evaluate(Entity clip, ref CommandSequenceState commandState,
                in CommandSequenceConfig config, in TrackBinding binding)
            {
                if (commandState.IsCompleted || binding.Value == Entity.Null) return;
                if (!TargetsLookup.TryGetComponent(binding.Value, out var targets)) return;

                if (!EntityLinkResolver.TryResolve(
                        binding.Value, targets, config.ReadRootFrom, config.ConsumerLinkKey,
                        Sources, Entries, out var consumer)) return;

                if (!PlayerIds.TryGetComponent(consumer, out var pid)) return;
                if (!InputAccess.TryGetState(Registry, States, pid.Value, out var state)) return;
                if (!Histories.TryGetBuffer(consumer, out var history)) return;

                ref var sequences = ref config.Blob.Value.Sequences;

                for (var s = 0; s < sequences.Length; s++)
                {
                    ref var seq = ref sequences[s];
                    if (seq.Steps.Length == 0) continue;

                    var consumeMask = default(BitArray256);
                    var searchIndex = 0;
                    var matched = true;

                    var lastMatchTick = uint.MaxValue;

                    for (var i = 0; i < seq.Steps.Length; i++)
                        if (!CommandMatcher.Evaluate(ref seq.Steps[i], state, history, ref consumeMask,
                                ref searchIndex, ref lastMatchTick))
                        {
                            matched = false;
                            break;
                        }

                    if (!matched) continue;

                    CommitConsumes(history, ref consumeMask);

                    if (InputRouting.TryResolveRoute(binding.Value, targets, config.EventRouteTo,
                            config.EventRouteLinkKey, Sources, Entries, out var routeTarget))
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

                var write = 0;
                for (var read = 0; read < history.Length; read++)
                {
                    if (consumeMask[read]) continue;
                    if (write != read) history[write] = history[read];
                    write++;
                }

                history.Length = write;
            }
        }
    }
}