using BovineLabs.Bridge.Data.Camera;
using BovineLabs.Core.Collections;
using BovineLabs.Core.Extensions;
using BovineLabs.Core.Iterators;
using BovineLabs.Reaction.Conditions;
using BovineLabs.Reaction.Data.Conditions;
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
using Unity.Physics;
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
        private ComponentLookup<PhysicsVelocity> _velocities;
        private ComponentLookup<PhysicsMass> _masses;

        private EntityQuery _cameraQuery;

        private NativeParallelMultiHashMapFallback<Entity, EventAmount> _eventChanges;
        private NativeParallelHashSet<Entity> _uniqueKeySet;
        private NativeList<Entity> _uniqueKeys;
        private ConditionEventWriter.Lookup _writers;

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
            _velocities = state.GetComponentLookup<PhysicsVelocity>();
            _masses = state.GetComponentLookup<PhysicsMass>(true);

            _cameraQuery = SystemAPI.QueryBuilder().WithAll<CameraMain, LocalToWorld>().Build();

            _eventChanges = new NativeParallelMultiHashMapFallback<Entity, EventAmount>(64, Allocator.Persistent);
            _uniqueKeySet = new NativeParallelHashSet<Entity>(64, Allocator.Persistent);
            _uniqueKeys = new NativeList<Entity>(64, Allocator.Persistent);
            _writers.Create(ref state);
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
            _velocities.Update(ref state);
            _masses.Update(ref state);
            _writers.Update(ref state);

            _uniqueKeySet.Clear();

            var cameraRotation = quaternion.identity;
            if (!_cameraQuery.IsEmpty)
            {
                var cameraEntity = _cameraQuery.GetSingletonEntity();
                cameraRotation = SystemAPI.GetComponent<LocalToWorld>(cameraEntity).Rotation;
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
                Velocities = _velocities,
                Masses = _masses,
                DeltaTime = SystemAPI.Time.DeltaTime,
                CameraRotation = cameraRotation,
                EventChanges = _eventChanges.AsWriter(),
                UniqueKeys = _uniqueKeySet.AsParallelWriter()
            }.ScheduleParallel(state.Dependency);

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
        [WithAll(typeof(ClipActive))]
        [WithNone(typeof(ClipActivePrevious))]
        private partial struct InitJob : IJobEntity
        {
            private void Execute(ref AxisTransformState state)
            {
                // Reset per-activation edge/decay state too: a stale WasInputActive from a
                // previous activation suppresses the RigidbodyImpulse rising edge, and a stale
                // LastInput leaks last frame's offset/velocity into the fresh activation.
                state.Initialized = false;
                state.WasInputActive = false;
                state.LastInput = float2.zero;
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
            [ReadOnly] public ComponentLookup<PhysicsMass> Masses;

            [NativeDisableParallelForRestriction] public ComponentLookup<LocalTransform> Transforms;
            [NativeDisableParallelForRestriction] public ComponentLookup<PhysicsVelocity> Velocities;

            public float DeltaTime;
            public quaternion CameraRotation;

            public NativeParallelMultiHashMapFallback<Entity, EventAmount>.ParallelWriter EventChanges;
            public NativeParallelHashSet<Entity>.ParallelWriter UniqueKeys;

            private void Execute(in TrackBinding binding, in AxisTransformConfig config, ref AxisTransformState state)
            {
                var targetEntity = binding.Value;
                if (targetEntity == Entity.Null || !Transforms.HasComponent(targetEntity)) return;
                if (!TargetsLookup.TryGetComponent(targetEntity, out var targets)) return;

                if (!EntityLinkResolver.TryResolve(
                        targetEntity, targets, config.ReadRootFrom, config.ConsumerLinkKey,
                        Sources, Entries, out var consumer)) return;

                if (!PlayerIds.TryGetComponent(consumer, out var pid)) return;
                if (!InputAccess.TryGetAxes(Registry, Axes, pid.Value, out var axesBuf)) return;

                var axisValue = float2.zero;
                for (var i = 0; i < axesBuf.Length; i++)
                {
                    if (axesBuf[i].ActionId != config.ActionId) continue;
                    axisValue = axesBuf[i].Value;
                    break;
                }

                state.HasInput = math.lengthsq(axisValue) > 0.0001f;
                if (state.HasInput)
                {
                    state.LastInput = axisValue;
                }
                else if (!config.Flags.Has(AxisTransformFlags.KeepLastPosition))
                {
                    if (config.DecayRate > 0f)
                        state.LastInput *= math.exp(-config.DecayRate * DeltaTime);
                    else
                        state.LastInput = float2.zero;
                }

                var transform = Transforms[targetEntity];

                var planeNormal = math.lengthsq(config.Plane) > 1e-8f ? math.normalize(config.Plane) : math.up();
                var basis = ComputePlaneBasis(planeNormal, config.Flags, CameraRotation);
                var inputVec = basis.Right * state.LastInput.x + basis.Forward * state.LastInput.y;

                if (config.Flags.Has(AxisTransformFlags.LocalSpace))
                    inputVec = math.rotate(transform.Rotation, inputVec);
                else if (config.Flags.Has(AxisTransformFlags.IgnoreParentRotation) &&
                         Parents.TryGetComponent(targetEntity, out var parent) &&
                         Ltws.TryGetComponent(parent.Value, out var parentLtw))
                    inputVec = math.rotate(math.inverse(parentLtw.Rotation), inputVec);

                var risingEdge = state.HasInput && !state.WasInputActive;
                var fallingEdge = !state.HasInput && state.WasInputActive;
                state.WasInputActive = state.HasInput;

                if (risingEdge && config.OnAxisStart != ConditionKey.Null &&
                    InputRouting.TryResolveRoute(targetEntity, targets, config.EventRouteTo, config.EventRouteLinkKey,
                        Sources, Entries, out var startTarget))
                {
                    EventChanges.Add(startTarget, new EventAmount(config.OnAxisStart, 1));
                    UniqueKeys.Add(startTarget);
                }

                if (fallingEdge && config.OnAxisStop != ConditionKey.Null &&
                    InputRouting.TryResolveRoute(targetEntity, targets, config.EventRouteTo, config.EventRouteLinkKey,
                        Sources, Entries, out var stopTarget))
                {
                    EventChanges.Add(stopTarget, new EventAmount(config.OnAxisStop, 1));
                    UniqueKeys.Add(stopTarget);
                }

                if (config.Mode.IsRigidbody())
                {
                    ApplyRigidbody(config, ref state, targetEntity, inputVec, planeNormal, risingEdge);
                    return;
                }

                // Resolve accurate anchor
                var anchorEntity = targets.Get(config.ReadRootFrom, targetEntity);
                if (config.AnchorLinkKey != 0)
                    if (EntityLinkResolver.TryResolve(targetEntity, targets, config.ReadRootFrom, config.AnchorLinkKey,
                            Sources, Entries, out var linked))
                        anchorEntity = linked;

                if (anchorEntity == Entity.Null)
                    anchorEntity = targetEntity;

                ApplyCarrot(config, ref state, targetEntity, anchorEntity, transform, inputVec);
            }

            private void ApplyRigidbody(in AxisTransformConfig config, ref AxisTransformState state,
                Entity targetEntity, float3 inputVec, float3 planeNormal, bool risingEdge)
            {
                if (!Velocities.HasComponent(targetEntity)) return;
                var pv = Velocities[targetEntity];

                var targetVel = inputVec * config.Range;
                var hasEffectiveInput = math.lengthsq(inputVec) > 0.0001f;

                if (!hasEffectiveInput && config.DecayRate > 0f)
                {
                    // Isolated planar velocity so things can properly succumb to gravity.
                    var perp = math.dot(pv.Linear, planeNormal) * planeNormal;
                    var planar = pv.Linear - perp;
                    planar *= math.exp(-config.DecayRate * DeltaTime);
                    pv.Linear = perp + planar;
                    Velocities[targetEntity] = pv;
                    return;
                }

                switch (config.Mode)
                {
                    case AxisTransformMode.RigidbodyVelocity:
                        var lerpT = config.Smoothing <= 0.0001f ? 1f : 1f - math.exp(-config.Smoothing * DeltaTime);
                        var perpVel = math.dot(pv.Linear, planeNormal) * planeNormal;
                        var planarVel = pv.Linear - perpVel;
                        planarVel = math.lerp(planarVel, targetVel, lerpT);
                        pv.Linear = perpVel + planarVel;
                        break;

                    case AxisTransformMode.RigidbodyForce:
                        var invMass = Masses.TryGetComponent(targetEntity, out var m) ? m.InverseMass : 1f;
                        if (invMass > 0f)
                            pv.Linear += targetVel * (DeltaTime * invMass);

                        if (config.Drag > 0f)
                        {
                            var perpVelF = math.dot(pv.Linear, planeNormal) * planeNormal;
                            var planarVelF = pv.Linear - perpVelF;
                            planarVelF *= math.exp(-config.Drag * DeltaTime);
                            pv.Linear = perpVelF + planarVelF;
                        }

                        break;

                    case AxisTransformMode.RigidbodyImpulse:
                        if (risingEdge)
                        {
                            var invMass2 = Masses.TryGetComponent(targetEntity, out var ms) ? ms.InverseMass : 1f;
                            pv.Linear += targetVel * invMass2;
                        }

                        break;
                }

                Velocities[targetEntity] = pv;
            }

            private bool TryGetUpToDateWorldTransform(Entity entity, out float3 position, out quaternion rotation,
                out float scale)
            {
                // Unparented physics entities get updated directly in LocalTransform natively during physics.
                if (Transforms.TryGetComponent(entity, out var localTransform) && !Parents.HasComponent(entity))
                {
                    position = localTransform.Position;
                    rotation = localTransform.Rotation;
                    scale = localTransform.Scale;
                    return true;
                }

                // If not physics or it has a parent, fall back to LocalToWorld
                if (Ltws.TryGetComponent(entity, out var ltw))
                {
                    position = ltw.Position;
                    rotation = ltw.Rotation;
                    var s = math.length(ltw.Value.c0.xyz);
                    scale = s > 0 ? s : 1f;
                    return true;
                }

                position = float3.zero;
                rotation = quaternion.identity;
                scale = 1f;
                return false;
            }

            private void ApplyCarrot(in AxisTransformConfig config, ref AxisTransformState state,
                Entity targetEntity, Entity anchorEntity, LocalTransform transform, float3 inputVec)
            {
                if (!state.Initialized)
                {
                    TryGetUpToDateWorldTransform(anchorEntity, out var initPos, out _, out _);
                    state.AnchorOrigin = initPos;
                    state.DesiredOffset = float3.zero;
                    state.SmoothedOffset = float3.zero;
                    state.Initialized = true;
                }

                // Range scales the [-1,1] input to the offset (matches the rigidbody path and the tooltip);
                // LeashRadius then clamps the offset distance, with 0 meaning unlimited. Previously LeashRadius
                // was used as the scale, so Range did nothing and LeashRadius=0 ("unlimited") froze the carrot.
                // Position sets the target offset directly; Velocity integrates input into the offset over time
                // (input becomes a rate), so holding a direction keeps moving the carrot until released/clamped.
                float3 offset;
                if (config.Mode == AxisTransformMode.Velocity)
                    offset = state.DesiredOffset + (inputVec * config.Range * DeltaTime);
                else
                    offset = inputVec * config.Range;

                if (config.LeashRadius > 0f)
                {
                    var len = math.length(offset);
                    if (len > config.LeashRadius)
                        offset *= config.LeashRadius / len;
                }

                state.DesiredOffset = offset;

                // Frame-rate independent smoothing
                if (config.Smoothing <= 0.0001f)
                    state.SmoothedOffset = state.DesiredOffset;
                else
                    state.SmoothedOffset = math.lerp(state.SmoothedOffset, state.DesiredOffset,
                        1f - math.exp(-config.Smoothing * DeltaTime));

                float3 currentAnchorPos;
                if (anchorEntity == targetEntity)
                    currentAnchorPos = state.AnchorOrigin;
                else
                    TryGetUpToDateWorldTransform(anchorEntity, out currentAnchorPos, out _, out _);

                var targetWorldPos = currentAnchorPos + state.SmoothedOffset;

                // Ensure LocalTransform offset doesn't drift away by computing valid reverse-transform lookup
                if (Parents.TryGetComponent(targetEntity, out var parent))
                {
                    if (TryGetUpToDateWorldTransform(parent.Value, out var pPos, out var pRot, out var pScale))
                    {
                        var parentMatrix = float4x4.TRS(pPos, pRot, new float3(pScale));
                        var invParentMatrix = math.inverse(parentMatrix);
                        transform.Position = math.transform(invParentMatrix, targetWorldPos);
                    }
                }
                else
                {
                    transform.Position = targetWorldPos;
                }

                Transforms[targetEntity] = transform;
            }

            private struct PlaneBasis
            {
                public float3 Forward;
                public float3 Right;
            }

            private static PlaneBasis ComputePlaneBasis(float3 planeNormal, AxisTransformFlags flags,
                quaternion cameraRotation)
            {
                float3 forward;
                float3 right;

                if (flags.Has(AxisTransformFlags.CameraRelative))
                {
                    var camForward = math.mul(cameraRotation, new float3(0, 0, 1));
                    var camRight = math.mul(cameraRotation, new float3(1, 0, 0));

                    var projForward = camForward - math.dot(camForward, planeNormal) * planeNormal;
                    var projRight = camRight - math.dot(camRight, planeNormal) * planeNormal;

                    // A camera looking along the plane normal (e.g. top-down with an up-plane)
                    // collapses the projected forward to zero; derive the missing axis from the
                    // surviving one, falling back to a world basis only when both degenerate.
                    if (math.lengthsq(projForward) > 1e-6f)
                    {
                        forward = math.normalize(projForward);
                        right = math.lengthsq(projRight) > 1e-6f
                            ? math.normalize(projRight)
                            : math.normalize(math.cross(planeNormal, forward));
                    }
                    else if (math.lengthsq(projRight) > 1e-6f)
                    {
                        right = math.normalize(projRight);
                        forward = math.normalize(math.cross(right, planeNormal));
                    }
                    else
                    {
                        WorldBasis(planeNormal, out forward, out right);
                    }
                }
                else
                {
                    WorldBasis(planeNormal, out forward, out right);
                }

                return new PlaneBasis { Forward = forward, Right = right };
            }

            private static void WorldBasis(float3 planeNormal, out float3 forward, out float3 right)
            {
                if (math.abs(math.dot(planeNormal, new float3(0, 1, 0))) > 0.99f)
                {
                    forward = new float3(0, 0, 1);
                    right = new float3(1, 0, 0);
                }
                else
                {
                    right = math.normalize(math.cross(new float3(0, 1, 0), planeNormal));
                    forward = math.cross(planeNormal, right);
                }
            }
        }
    }
}