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
        private ComponentLookup<PointerProviderTag> _pointerProviders;
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
            _pointerProviders = state.GetComponentLookup<PointerProviderTag>(true);
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
            _pointerProviders.Update(ref state);
            _transforms.Update(ref state);
            _parents.Update(ref state);
            _ltws.Update(ref state);

            var cameraRotation = quaternion.identity;
            if (!_cameraQuery.IsEmpty)
            {
                // Split-screen coop registers multiple CameraMain; take the first instead of GetSingletonEntity,
                // which throws on count != 1 and would break EVERY player's aim that frame. Matches InputCommonSystem.
                using var cameras = _cameraQuery.ToEntityArray(Allocator.Temp);
                var cameraEntity = cameras[0];

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
                PointerProviders = _pointerProviders,
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

            [ReadOnly] [NativeDisableContainerSafetyRestriction]
            public ComponentLookup<PointerProviderTag> PointerProviders;

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

                // Cursor aim reads the global pointer, not the seat's axis buffer - don't make a missing/late buffer
                // silently kill it. Stick aim still requires the buffer.
                var cursorAim = config.Mode == AxisTransformMode.Aim &&
                                config.Flags.Has(AxisTransformFlags.PointFromCursor);
                if (!InputAccess.TryGetAxes(Registry, Axes, pid.Value, out var axesBuf) && !cursorAim) return;

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

                // A zero/near-zero parent scale would make the world->local divides below produce Inf/NaN and poison
                // LocalTransform. Treat a degenerate parent scale as 1.
                if (parented && math.abs(parentScale) < 1e-6f)
                    parentScale = 1f;

                // Capture the carrot's start world pose ONCE per activation, before deriving input: Aim's cursor
                // pivot and its unparented orbit base both read HeldWorldPosition, so it must be set first.
                if (!state.Initialized)
                {
                    state.HeldWorldRotation = parented ? math.mul(parentRot, t.Rotation) : t.Rotation;
                    state.HeldWorldPosition = parented
                        ? parentPos + math.rotate(parentRot, t.Position * parentScale)
                        : t.Position;
                    state.HasAimed = false;
                    state.Initialized = true;
                }

                float3 inputVec;
                if (cursorAim)
                {
                    // Aim at the mouse cursor: project its world ray onto the body's aim plane, face that point.
                    // Off-screen / no camera => inputVec stays zero => hasInput false => ComputeAim holds last aim.
                    inputVec = float3.zero;

                    // Local coop: only the seat that OWNS the pointer device follows the (single, global) cursor.
                    // Other seats (gamepad) hold their last aim instead of snapping to player 1's mouse.
                    var seatProvider = pid.Value < Registry.Length ? Registry[pid.Value] : Entity.Null;
                    var ownsPointer = seatProvider != Entity.Null && PointerProviders.HasComponent(seatProvider);

                    if (CursorValid && ownsPointer)
                    {
                        // Pivot on the BODY, never the carrot's own (AimRadius-displaced) position - that feeds back
                        // and flickers. Parented: the parent body. Unparented (carrot==body): the captured start,
                        // which is stable (t.Position is the already-displaced carrot, so it would feed back).
                        var bodyWorld = parented ? parentPos : state.HeldWorldPosition;
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
                    {
                        // Parented: ComputeAim returns a parent-LOCAL offset (orbit the parent origin = the body).
                        // Unparented: it returns a raw WORLD offset; anchor it to the captured start so the carrot
                        // orbits where it began instead of collapsing toward the world origin.
                        t.Position = parented ? localPos : state.HeldWorldPosition + localPos;
                    }

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