using BovineLabs.Bridge.Data.Camera;
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
using Unity.Mathematics;
using Unity.Transforms;

namespace BovineLabs.Timeline.PlayerInputs
{
    [UpdateInGroup(typeof(TimelineComponentAnimationGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation)]
    public partial struct AxisTransformSystem : ISystem
    {
        private UnsafeComponentLookup<Targets> _targetsLookup;
        private UnsafeComponentLookup<EntityLinkSource> _sources;
        private UnsafeBufferLookup<EntityLinkEntry> _entries;
        private BufferLookup<InputAxis> _axes;
        private ComponentLookup<PlayerId> _playerIds;
        private ComponentLookup<LocalTransform> _transforms;
        private ComponentLookup<Parent> _parents;
        private ComponentLookup<LocalToWorld> _ltws;

        private EntityQuery _cameraQuery;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<AxisTransformConfig>();
            state.RequireForUpdate<InputRegistry>();
            _targetsLookup = state.GetUnsafeComponentLookup<Targets>(true);
            _sources = state.GetUnsafeComponentLookup<EntityLinkSource>(true);
            _entries = state.GetUnsafeBufferLookup<EntityLinkEntry>(true);
            _axes = state.GetBufferLookup<InputAxis>(true);
            _playerIds = state.GetComponentLookup<PlayerId>(true);
            _transforms = state.GetComponentLookup<LocalTransform>();
            _parents = state.GetComponentLookup<Parent>(true);
            _ltws = state.GetComponentLookup<LocalToWorld>(true);

            _cameraQuery = SystemAPI.QueryBuilder().WithAll<CameraMain>().Build();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            _targetsLookup.Update(ref state);
            _sources.Update(ref state);
            _entries.Update(ref state);
            _axes.Update(ref state);
            _playerIds.Update(ref state);
            _transforms.Update(ref state);
            _parents.Update(ref state);
            _ltws.Update(ref state);

            var cameraRotation = quaternion.identity;
            if (!_cameraQuery.IsEmpty)
            {
                var cameraEntity = _cameraQuery.GetSingletonEntity();

                if (_ltws.TryGetComponent(cameraEntity, out var camLtw))
                    cameraRotation = camLtw.Rotation;
                else if (_transforms.TryGetComponent(cameraEntity, out var camLt) &&
                         !_parents.HasComponent(cameraEntity))
                    cameraRotation = camLt.Rotation;
            }

            var registry = SystemAPI.GetSingleton<InputRegistry>();

            state.Dependency = new InitJob().ScheduleParallel(state.Dependency);

            state.Dependency = new ApplyJob
            {
                TargetsLookup = _targetsLookup,
                Sources = _sources,
                Entries = _entries,
                Registry = registry.ProviderByPlayer,
                Axes = _axes,
                PlayerIds = _playerIds,
                Transforms = _transforms,
                Parents = _parents,
                Ltws = _ltws,
                DeltaTime = SystemAPI.Time.DeltaTime,
                CameraRotation = cameraRotation
            }.Schedule(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(ClipActive))]
        [WithNone(typeof(ClipActivePrevious))]
        private partial struct InitJob : IJobEntity
        {
            private void Execute(ref AxisTransformState state)
            {
                state.Initialized = false;
            }
        }

        [BurstCompile]
        [WithAll(typeof(ClipActive))]
        private partial struct ApplyJob : IJobEntity
        {
            [ReadOnly] public UnsafeComponentLookup<Targets> TargetsLookup;
            [ReadOnly] public UnsafeComponentLookup<EntityLinkSource> Sources;
            [ReadOnly] public UnsafeBufferLookup<EntityLinkEntry> Entries;

            [ReadOnly] [NativeDisableContainerSafetyRestriction]
            public NativeArray<Entity> Registry;

            [ReadOnly] public BufferLookup<InputAxis> Axes;

            [ReadOnly] [NativeDisableContainerSafetyRestriction]
            public ComponentLookup<PlayerId> PlayerIds;

            [ReadOnly] public ComponentLookup<Parent> Parents;
            [ReadOnly] public ComponentLookup<LocalToWorld> Ltws;

            [NativeDisableParallelForRestriction] public ComponentLookup<LocalTransform> Transforms;

            public float DeltaTime;
            public quaternion CameraRotation;

            private void Execute(in TrackBinding binding, in AxisTransformConfig config, ref AxisTransformState state)
            {
                var boundEntity = binding.Value;
                if (boundEntity == Entity.Null) return;
                if (!TargetsLookup.TryGetComponent(boundEntity, out var targets)) return;

                if (!EntityLinkResolver.TryResolve(
                        boundEntity, targets, config.ReadRootFrom, config.ConsumerLinkKey,
                        Sources, Entries, out var consumer)) return;
                if (!PlayerIds.TryGetComponent(consumer, out var pid)) return;
                if (!InputAccess.TryGetAxes(Registry, Axes, pid.Value, out var axesBuf)) return;

                var carrot = boundEntity;
                if (config.AnchorLinkKey != 0 &&
                    EntityLinkResolver.TryResolve(
                        boundEntity, targets, config.ReadRootFrom, config.AnchorLinkKey,
                        Sources, Entries, out var anchor))
                    carrot = anchor;

                if (!Transforms.HasComponent(carrot)) return;

                var axisValue = InputAccess.ReadAxis(axesBuf, config.ActionId);

                var planeNormal = math.lengthsq(config.Plane) > 1e-8f ? math.normalize(config.Plane) : math.up();
                AxisBasis.ComputePlaneBasis(planeNormal, config.Flags.Has(AxisTransformFlags.CameraRelative),
                    CameraRotation, out var basisForward, out var basisRight);
                var inputVec = basisRight * axisValue.x + basisForward * axisValue.y;
                var hasInput = math.lengthsq(inputVec) > 0.0001f;

                var t = Transforms[carrot];
                var writeTransform = false;

                var parented = TryGetParentWorld(carrot, out var parentPos, out var parentRot, out var parentScale);

                if (!state.Initialized)
                {
                    state.HeldWorldRotation = parented ? math.mul(parentRot, t.Rotation) : t.Rotation;
                    state.HeldWorldPosition = parented
                        ? parentPos + math.rotate(parentRot, t.Position * parentScale)
                        : t.Position;
                    state.HasAimed = false;
                    state.Initialized = true;
                }

                if (config.Mode == AxisTransformMode.Move)
                {
                    AxisLead.ComputeMove(inputVec, config.Range, config.LeashRadius,
                        config.Flags.Has(AxisTransformFlags.KeepLead), parented, parentPos, parentRot, parentScale,
                        state.HeldWorldPosition, out var localPos, out var newHeldWorldPos);

                    t.Position = localPos;
                    state.HeldWorldPosition = newHeldWorldPos;
                    writeTransform = true;
                }
                else
                {
                    AxisAim.ComputeAim(inputVec, hasInput, planeNormal, config.Smoothing, DeltaTime, config.AimRadius,
                        state.HasAimed, parented, parentRot, parentScale, state.HeldWorldRotation,
                        out var newHeldWorldRot, out var newHasAimed, out var wroteLocalPos, out var localPos);

                    state.HeldWorldRotation = newHeldWorldRot;
                    state.HasAimed = newHasAimed;

                    t.Rotation = parented
                        ? math.mul(math.inverse(parentRot), state.HeldWorldRotation)
                        : state.HeldWorldRotation;

                    if (wroteLocalPos)
                        t.Position = localPos;

                    writeTransform = true;
                }

                if (writeTransform)
                    Transforms[carrot] = t;
            }

            private bool TryGetParentWorld(Entity carrot, out float3 position, out quaternion rotation, out float scale)
            {
                position = float3.zero;
                rotation = quaternion.identity;
                scale = 1f;

                if (!Parents.TryGetComponent(carrot, out var parent))
                    return false;

                var p = parent.Value;
                var hasLocalTransform = Transforms.HasComponent(p);
                var localTransform = hasLocalTransform ? Transforms[p] : default;
                var hasLtw = Ltws.TryGetComponent(p, out var ltw);

                return AxisParentWorld.TryDecompose(hasLocalTransform, localTransform, Parents.HasComponent(p), hasLtw,
                    ltw, out position, out rotation, out scale);
            }
        }
    }
}