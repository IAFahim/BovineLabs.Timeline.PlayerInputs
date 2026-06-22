using BovineLabs.Bridge.Data.Camera;
using BovineLabs.Core.Collections;
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
using Unity.Jobs;
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

            // Don't gate on LocalToWorld: the auto-created CameraMain entity (CameraMainSystem, no authoring in the
            // scene) has only LocalTransform, so requiring LocalToWorld made CameraRelative silently no-op there.
            // Match on CameraMain alone and pick the world rotation source per-entity in OnUpdate.
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

                // Prefer the baked world matrix - the CameraMainAuthoring camera is parented to a scene root, so
                // LocalTransform.Rotation is PARENT-relative and only equals world rotation while the root is at
                // identity. Fall back to LocalTransform only when the camera is unparented (the auto-created entity,
                // which has no LocalToWorld). Lookups are already Update()'d above.
                if (_ltws.TryGetComponent(cameraEntity, out var camLtw))
                    cameraRotation = camLtw.Rotation;
                else if (_transforms.TryGetComponent(cameraEntity, out var camLt) && !_parents.HasComponent(cameraEntity))
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
                CameraRotation = cameraRotation,
            }.Schedule(state.Dependency); // Single-threaded: two clips can share the same TrackBinding body, so the
                                          // [NativeDisableParallelForRestriction] read-modify-write on LocalTransform
                                          // would tear/last-writer-win across worker threads (see Physics SharedTrackJobs).
        }

        [BurstCompile]
        [WithAll(typeof(ClipActive))]
        [WithNone(typeof(ClipActivePrevious))]
        private partial struct InitJob : IJobEntity
        {
            private void Execute(ref AxisTransformState state)
            {
                // Re-capture the leash origin on each activation (StartPosition is set lazily in ApplyJob).
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

                // Which player drives this clip.
                if (!EntityLinkResolver.TryResolve(
                        boundEntity, targets, config.ReadRootFrom, config.ConsumerLinkKey,
                        Sources, Entries, out var consumer)) return;
                if (!PlayerIds.TryGetComponent(consumer, out var pid)) return;
                if (!InputAccess.TryGetAxes(Registry, Axes, pid.Value, out var axesBuf)) return;

                // AxisTransform drives the BOUND carrier - the "carrot" - NOT a physics body. The carrot is typically
                // a CHILD of the body that chases it, so its LocalTransform offset is how far in front of the player
                // the carrot sits. Keeping the carrot kinematic is the whole point: it never fights the physics solver;
                // the body pursues it via its own Physics tracks (Force / Angular LookAt PID targeting the carrot).
                // AnchorLink (optional) redirects motion onto a separate driven body; the bound marker then just
                // carries the player/consumer link. Unset (0) - or a failed resolve - drives the bound entity itself.
                var carrot = boundEntity;
                if (config.AnchorLinkKey != 0 &&
                    EntityLinkResolver.TryResolve(
                        boundEntity, targets, config.ReadRootFrom, config.AnchorLinkKey,
                        Sources, Entries, out var anchor))
                {
                    carrot = anchor;
                }

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

                // The carrot's parent is the anchor it offsets FROM. inputVec is a camera-relative WORLD direction, so
                // convert offsets/facing into the parent's local frame (the carrot rides in front of the player in the
                // stick's world direction regardless of which way the player is currently turned).
                var parented = false;
                var parentRot = quaternion.identity;
                if (Parents.TryGetComponent(carrot, out var parent) &&
                    Ltws.TryGetComponent(parent.Value, out var parentLtw))
                {
                    parented = true;
                    parentRot = parentLtw.Rotation;
                }

                // HoldLastPosition: when the stick is released, leave the carrot where it is instead of recentering.
                // Skipping the write entirely freezes the last offset (bounded by LeashRadius, so it can't run away),
                // turning the leash into a place-and-hold lead point. With the flag off, zero input writes a zero
                // offset and the carrot recenters onto the player as before.
                var holdOnRelease = !hasInput && config.Flags.Has(AxisTransformFlags.HoldLastPosition);

                if (config.Flags.Has(AxisTransformFlags.Translate) && !holdOnRelease)
                {
                    // Place the carrot at the stick offset from its rest pose. LeashRadius caps how far in front it
                    // can sit. Zero input -> zero offset -> the carrot recenters onto the player (the body stops
                    // chasing). This is a pure kinematic write - no physics, no solver fight, no snap-back.
                    var worldOffset = inputVec * config.Range;
                    if (config.LeashRadius > 0f)
                    {
                        var len = math.length(worldOffset);
                        if (len > config.LeashRadius)
                            worldOffset *= config.LeashRadius / len;
                    }

                    var localOffset = parented ? math.rotate(math.inverse(parentRot), worldOffset) : worldOffset;

                    // ABSOLUTE local offset from the parent (rest = parent origin). Setting it absolutely each frame
                    // - never += a captured start - means it cannot accumulate/run away (the previous StartPosition
                    // approach compounded because the per-activation reset re-captured the already-drifted position),
                    // and it recenters onto the player when input returns to zero.
                    t.Position = localOffset;
                    writeTransform = true;
                }

                if (config.Flags.Has(AxisTransformFlags.FaceDirection) && hasInput)
                {
                    // Turn the carrot to FACE the stick direction (Smoothing = turn speed, 0 = snap). Written in the
                    // carrot's local frame; the body's own Angular PID can then LookAt this carrot.
                    var worldDesired = quaternion.LookRotationSafe(math.normalize(inputVec), planeNormal);
                    var desired = parented ? math.mul(math.inverse(parentRot), worldDesired) : worldDesired;
                    var lerpT = config.Smoothing <= 0.0001f ? 1f : 1f - math.exp(-config.Smoothing * DeltaTime);
                    t.Rotation = math.slerp(t.Rotation, desired, lerpT);
                    writeTransform = true;
                }

                if (writeTransform)
                    Transforms[carrot] = t;
            }

        }
    }
}