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

            // Cursor world ray for PointFromCursor aim (one shared system pointer). Optional: no InputCommon / no
            // camera / cursor off-screen or unfocused => CursorValid false => aim holds its last direction.
            var cursorRay = default(CameraRay);
            var cursorValid = false;
            if (SystemAPI.TryGetSingleton<InputCommon>(out var common))
            {
                cursorRay = common.CameraRay;
                cursorValid = common.HasCamera && common.InViewWithFocus;
            }

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
                CameraRotation = cameraRotation,
                Cursor = cursorRay,
                CursorValid = cursorValid
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
            public CameraRay Cursor;
            public bool CursorValid;

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

                var planeNormal = math.lengthsq(config.Plane) > 1e-8f ? math.normalize(config.Plane) : math.up();
                AxisBasis.ComputePlaneBasis(planeNormal, config.Flags.Has(AxisTransformFlags.CameraRelative),
                    CameraRotation, out var basisForward, out var basisRight);

                var t = Transforms[carrot];
                var writeTransform = false;

                var parented = TryGetParentWorld(carrot, out var parentPos, out var parentRot, out var parentScale);

                float3 inputVec;
                if (config.Mode == AxisTransformMode.Aim && config.Flags.Has(AxisTransformFlags.PointFromCursor))
                {
                    // Aim at the mouse cursor: project its world ray onto the body's aim plane, face that point.
                    // Off-screen / no camera => inputVec stays zero => hasInput false => ComputeAim holds last aim.
                    inputVec = float3.zero;
                    if (CursorValid)
                    {
                        // Pivot on the BODY (the carrot's parent), never the carrot's own position: with AimRadius>0
                        // the carrot is displaced along the aim dir each frame, so using it as the pivot feeds back
                        // and flickers between two spots (worse at Smoothing=0, no damping). parentPos is the body.
                        var bodyWorld = parented ? parentPos : t.Position;
                        if (AxisAim.TryProjectCursorToPlane(Cursor.Origin, Cursor.Direction, bodyWorld, planeNormal,
                                out var aimPoint))
                        {
                            var toAim = aimPoint - bodyWorld;
                            inputVec = toAim - (planeNormal * math.dot(toAim, planeNormal));
                        }
                    }
                }
                else
                {
                    var axisValue = InputAccess.ReadAxis(axesBuf, config.ActionId);
                    inputVec = (basisRight * axisValue.x) + (basisForward * axisValue.y);
                }

                var hasInput = math.lengthsq(inputVec) > 0.0001f;

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

                    // Lateral offset (Move): fixed sideways shift along the plane-right basis, so two clips at +/-X
                    // make two parallel movement trails. Same perpendicular intent as Aim, but along a fixed axis
                    // (Move has no facing to be perpendicular to).
                    if (config.LateralOffset != 0f)
                    {
                        var lateralWorld = basisRight * config.LateralOffset;
                        t.Position += parented
                            ? math.rotate(math.inverse(parentRot), lateralWorld) / parentScale
                            : lateralWorld;
                    }

                    writeTransform = true;
                }
                else
                {
                    AxisAim.ComputeAim(inputVec, hasInput, planeNormal, config.Smoothing, DeltaTime, config.AimRadius,
                        config.LateralOffset, config.Flags.Has(AxisTransformFlags.RotateInPlace), state.HasAimed,
                        parented, parentRot, parentScale, state.HeldWorldRotation,
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