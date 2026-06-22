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

                var axisValue = float2.zero;
                for (var i = 0; i < axesBuf.Length; i++)
                {
                    if (axesBuf[i].ActionId != config.ActionId) continue;
                    axisValue = axesBuf[i].Value;
                    break;
                }

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

                    // Seed the held world position from the carrot's current world pose so a Move clip that
                    // starts already-released holds where it is rather than snapping to a stale/zero point.
                    state.HeldWorldPosition = parented
                        ? parentPos + math.rotate(parentRot, t.Position * parentScale)
                        : t.Position;
                    state.HasAimed = false;
                    state.Initialized = true;
                }

                if (config.Mode == AxisTransformMode.Move)
                {
                    // MOVE: the stick offsets the carrot ahead of the body (the Pos carrot the body chases).
                    var keepLead = config.Flags.Has(AxisTransformFlags.KeepLead);

                    if (hasInput)
                    {
                        var worldOffset = inputVec * config.Range;
                        if (config.LeashRadius > 0f)
                        {
                            var len = math.length(worldOffset);
                            if (len > config.LeashRadius)
                                worldOffset *= config.LeashRadius / len;
                        }

                        // Offset-relative parent-local (parent origin cancels analytically; exact 0 at zero input).
                        t.Position = parented ? math.rotate(math.inverse(parentRot), worldOffset) / parentScale : worldOffset;
                        // Track the live world lead point so a release captures exactly where the carrot was.
                        state.HeldWorldPosition = parented ? parentPos + math.rotate(parentRot, worldOffset) : worldOffset;
                    }
                    else if (keepLead)
                    {
                        // Released, KeepLead: pin to the captured WORLD point, re-derived to parent-local each
                        // frame. As the body travels to it the offset shrinks and the body STOPS - no runaway.
                        t.Position = parented
                            ? math.rotate(math.inverse(parentRot), state.HeldWorldPosition - parentPos) / parentScale
                            : state.HeldWorldPosition;
                    }
                    else
                    {
                        // Released, default: snap the lead back onto the body (local zero) so the body stops.
                        t.Position = float3.zero;
                    }

                    writeTransform = true;
                }
                else
                {
                    // AIM: the stick points the carrot's facing (the Rot carrot the body turns to). The indicator
                    // accumulates in WORLD space and is HELD on release - you keep facing where you last aimed.
                    if (hasInput)
                    {
                        var worldDesired = quaternion.LookRotationSafe(math.normalize(inputVec), planeNormal);
                        var lerpT = config.Smoothing <= 0.0001f ? 1f : 1f - math.exp(-config.Smoothing * DeltaTime);
                        state.HeldWorldRotation = math.slerp(state.HeldWorldRotation, worldDesired, lerpT);
                        state.HasAimed = true;
                    }

                    t.Rotation = parented
                        ? math.mul(math.inverse(parentRot), state.HeldWorldRotation)
                        : state.HeldWorldRotation;

                    // AimRadius: also slide the sphere out to the arrow's tip (held aim dir x radius) around the body
                    // and HOLD there - the held rotation already holds, so the position holds with it.
                    if (config.AimRadius > 0.0001f && state.HasAimed)
                    {
                        var worldOffset = math.mul(state.HeldWorldRotation, math.forward()) * config.AimRadius;
                        t.Position = parented
                            ? math.rotate(math.inverse(parentRot), worldOffset) / parentScale
                            : worldOffset;
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
                if (Transforms.HasComponent(p) && !Parents.HasComponent(p))
                {
                    var lt = Transforms[p];
                    position = lt.Position;
                    rotation = lt.Rotation;

                    scale = math.abs(lt.Scale) > 1e-6f ? lt.Scale : 1f;
                    return true;
                }

                if (Ltws.TryGetComponent(p, out var ltw))
                {
                    position = ltw.Position;
                    rotation = ltw.Rotation;
                    var s = math.length(ltw.Value.c0.xyz);
                    scale = s > 1e-6f ? s : 1f;
                    return true;
                }

                return false;
            }
        }
    }
}