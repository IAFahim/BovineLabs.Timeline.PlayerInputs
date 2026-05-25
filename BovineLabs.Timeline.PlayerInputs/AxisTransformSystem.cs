using BovineLabs.Bridge.Data.Camera;
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
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

namespace BovineLabs.Timeline.PlayerInputs
{
    [UpdateInGroup(typeof(TimelineComponentAnimationGroup))]
    [UpdateAfter(typeof(ConsumerSyncSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation)]
    public partial struct AxisTransformSystem : ISystem
    {
        private ComponentLookup<Targets> _targetsLookup;
        private UnsafeComponentLookup<EntityLinkSource> _sources;
        private UnsafeBufferLookup<EntityLinkEntry> _entries;
        private BufferLookup<InputAxis> _axes;
        private ComponentLookup<LocalTransform> _transforms;
        private ComponentLookup<Parent> _parents;
        private ComponentLookup<LocalToWorld> _ltws;
        private ConditionEventWriter.Lookup _writers;
        private ComponentLookup<PhysicsVelocity> _velocities;
        private ComponentLookup<PhysicsMass> _masses;

        private EntityQuery _cameraQuery;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<AxisTransformConfig>();
            _targetsLookup = state.GetComponentLookup<Targets>(true);
            _sources = state.GetUnsafeComponentLookup<EntityLinkSource>(true);
            _entries = state.GetUnsafeBufferLookup<EntityLinkEntry>(true);
            _axes = state.GetBufferLookup<InputAxis>(true);
            _transforms = state.GetComponentLookup<LocalTransform>();
            _parents = state.GetComponentLookup<Parent>(true);
            _ltws = state.GetComponentLookup<LocalToWorld>(true);
            _writers.Create(ref state);
            _velocities = state.GetComponentLookup<PhysicsVelocity>();
            _masses = state.GetComponentLookup<PhysicsMass>(true);

            _cameraQuery = SystemAPI.QueryBuilder().WithAll<CameraMain, LocalToWorld>().Build();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            _targetsLookup.Update(ref state);
            _sources.Update(ref state);
            _entries.Update(ref state);
            _axes.Update(ref state);
            _transforms.Update(ref state);
            _parents.Update(ref state);
            _ltws.Update(ref state);
            _writers.Update(ref state);
            _velocities.Update(ref state);
            _masses.Update(ref state);

            var cameraRotation = quaternion.identity;
            if (!_cameraQuery.IsEmpty)
            {
                var cameraEntity = _cameraQuery.GetSingletonEntity();
                cameraRotation = SystemAPI.GetComponent<LocalToWorld>(cameraEntity).Rotation;
            }

            state.Dependency = new InitJob().ScheduleParallel(state.Dependency);

            state.Dependency = new ApplyJob
            {
                TargetsLookup = _targetsLookup,
                Sources = _sources,
                Entries = _entries,
                Axes = _axes,
                Transforms = _transforms,
                Parents = _parents,
                Ltws = _ltws,
                Writers = _writers,
                Velocities = _velocities,
                Masses = _masses,
                DeltaTime = SystemAPI.Time.DeltaTime,
                CameraRotation = cameraRotation
            }.ScheduleParallel(state.Dependency);
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
            [ReadOnly] public ComponentLookup<Targets> TargetsLookup;
            [ReadOnly] public UnsafeComponentLookup<EntityLinkSource> Sources;
            [ReadOnly] public UnsafeBufferLookup<EntityLinkEntry> Entries;
            [ReadOnly] public BufferLookup<InputAxis> Axes;
            [ReadOnly] public ComponentLookup<Parent> Parents;
            [ReadOnly] public ComponentLookup<LocalToWorld> Ltws;
            [ReadOnly] public ComponentLookup<PhysicsMass> Masses;

            [NativeDisableParallelForRestriction] public ComponentLookup<LocalTransform> Transforms;
            [NativeDisableParallelForRestriction] public ConditionEventWriter.Lookup Writers;
            [NativeDisableParallelForRestriction] public ComponentLookup<PhysicsVelocity> Velocities;

            public float DeltaTime;
            public quaternion CameraRotation;

            private void Execute(in TrackBinding binding, in AxisTransformConfig config, ref AxisTransformState state)
            {
                var targetEntity = binding.Value;
                if (targetEntity == Entity.Null || !Transforms.HasComponent(targetEntity)) return;
                if (!TargetsLookup.TryGetComponent(targetEntity, out var targets)) return;

                if (!EntityLinkResolver.TryResolve(
                        targetEntity, targets, config.ReadRootFrom, config.ConsumerLinkKey,
                        Sources, Entries, out var consumer)) return;

                if (!Axes.TryGetBuffer(consumer, out var axesBuf)) return;

                var axisValue = float2.zero;
                for (var i = 0; i < axesBuf.Length; i++)
                {
                    if (axesBuf[i].ActionId != config.ActionId) continue;
                    axisValue = axesBuf[i].Value;
                    break;
                }

                state.HasInput = math.lengthsq(axisValue) > 0.0001f;
                if (state.HasInput)
                    state.LastInput = axisValue;
                else if (!config.Flags.Has(AxisTransformFlags.KeepLastPosition))
                    state.LastInput = float2.zero;

                var transform = Transforms[targetEntity];

                var planeNormal = math.normalize(config.Plane);
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

                if (risingEdge && config.OnInputStart != ConditionKey.Null &&
                    TryResolveTarget(config.EventRouteTo, config.EventRouteLinkKey, targetEntity, targets,
                        out var startTarget))
                    if (Writers.TryGet(startTarget, out var w)) w.Trigger(config.OnInputStart, 1);

                if (fallingEdge && config.OnInputEnd != ConditionKey.Null &&
                    TryResolveTarget(config.EventRouteTo, config.EventRouteLinkKey, targetEntity, targets,
                        out var endTarget))
                    if (Writers.TryGet(endTarget, out var w)) w.Trigger(config.OnInputEnd, 1);

                state.WasInputActive = state.HasInput;

                if (config.Mode.IsRigidbody())
                {
                    ApplyRigidbody(config, ref state, targetEntity, inputVec, planeNormal, risingEdge);
                    return;
                }

                ApplyCarrot(config, ref state, targetEntity, targets, transform, inputVec);
            }

            private void ApplyRigidbody(in AxisTransformConfig config, ref AxisTransformState state,
                Entity targetEntity, float3 inputVec, float3 planeNormal, bool risingEdge)
            {
                if (!Velocities.HasComponent(targetEntity)) return;
                var pv = Velocities[targetEntity];

                if (!state.HasInput && config.DecayRate > 0f)
                {
                    var perp = math.dot(pv.Linear, planeNormal) * planeNormal;
                    var planar = pv.Linear - perp;
                    planar *= math.max(0f, 1f - config.DecayRate * DeltaTime);
                    pv.Linear = perp + planar;
                    Velocities[targetEntity] = pv;
                    return;
                }

                var lerpT = config.Smoothing > 0f ? math.saturate(DeltaTime * config.Smoothing) : 1f;

                switch (config.Mode)
                {
                    case AxisTransformMode.RigidbodyVelocity:
                    {
                        var perp = math.dot(pv.Linear, planeNormal) * planeNormal;
                        var planarNow = pv.Linear - perp;
                        var planarTarget = state.HasInput ? inputVec * config.Range : float3.zero;
                        pv.Linear = perp + math.lerp(planarNow, planarTarget, lerpT);
                        break;
                    }
                    case AxisTransformMode.RigidbodyForce:
                    {
                        var inverseMass = Masses.TryGetComponent(targetEntity, out var mass) ? mass.InverseMass : 1f;
                        if (state.HasInput)
                            pv.Linear += inputVec * config.Range * inverseMass * DeltaTime;
                        if (config.Drag > 0f)
                            pv.Linear *= math.max(0f, 1f - config.Drag * DeltaTime);
                        break;
                    }
                    case AxisTransformMode.RigidbodyImpulse:
                    {
                        if (risingEdge)
                        {
                            var inverseMass = Masses.TryGetComponent(targetEntity, out var mass)
                                ? mass.InverseMass
                                : 1f;
                            pv.Linear += inputVec * config.Range * inverseMass;
                        }

                        break;
                    }
                }

                if (config.LeashRadius > 0f)
                {
                    var perp = math.dot(pv.Linear, planeNormal) * planeNormal;
                    var planar = pv.Linear - perp;
                    var speedSq = math.lengthsq(planar);
                    if (speedSq > config.LeashRadius * config.LeashRadius)
                        planar = planar / math.sqrt(speedSq) * config.LeashRadius;
                    pv.Linear = perp + planar;
                }

                Velocities[targetEntity] = pv;
            }

            private void ApplyCarrot(in AxisTransformConfig config, ref AxisTransformState state,
                Entity targetEntity, in Targets targets, LocalTransform transform, float3 inputVec)
            {
                var anchorPos = ResolveAnchorPosition(config, targetEntity, targets, transform);

                if (!state.Initialized)
                {
                    state.AnchorOrigin = anchorPos;
                    state.DesiredOffset = transform.Position - anchorPos;
                    state.SmoothedOffset = state.DesiredOffset;
                    state.Initialized = true;
                }

                if (config.AnchorLinkKey != 0)
                    state.AnchorOrigin = anchorPos;

                switch (config.Mode)
                {
                    case AxisTransformMode.Position:
                        state.DesiredOffset = inputVec * config.Range;
                        break;
                    case AxisTransformMode.Velocity:
                        state.DesiredOffset += inputVec * config.Range * DeltaTime;
                        break;
                }

                if (!state.HasInput && config.DecayRate > 0f)
                {
                    var decay = math.saturate(config.DecayRate * DeltaTime);
                    state.DesiredOffset = math.lerp(state.DesiredOffset, float3.zero, decay);
                }

                if (config.LeashRadius > 0f)
                {
                    var distSq = math.lengthsq(state.DesiredOffset);
                    if (distSq > config.LeashRadius * config.LeashRadius)
                        state.DesiredOffset = state.DesiredOffset / math.sqrt(distSq) * config.LeashRadius;
                }

                var lerpT = config.Smoothing > 0f ? math.saturate(DeltaTime * config.Smoothing) : 1f;
                state.SmoothedOffset = math.lerp(state.SmoothedOffset, state.DesiredOffset, lerpT);

                transform.Position = anchorPos + state.SmoothedOffset;
                Transforms[targetEntity] = transform;
            }

            private float3 ResolveAnchorPosition(in AxisTransformConfig config, Entity targetEntity,
                in Targets targets, in LocalTransform carrotTransform)
            {
                if (config.AnchorLinkKey == 0)
                    return carrotTransform.Position;

                if (!EntityLinkResolver.TryResolve(
                        targetEntity, targets, config.ReadRootFrom, config.AnchorLinkKey,
                        Sources, Entries, out var anchorEntity))
                    return carrotTransform.Position;

                if (!Ltws.TryGetComponent(anchorEntity, out var anchorLtw))
                    return carrotTransform.Position;

                var anchorWorldPos = anchorLtw.Position;

                if (Parents.TryGetComponent(targetEntity, out var parent) &&
                    Ltws.TryGetComponent(parent.Value, out var parentLtw))
                {
                    var invParentRot = math.inverse(parentLtw.Rotation);
                    return math.rotate(invParentRot, anchorWorldPos - parentLtw.Position);
                }

                return anchorWorldPos;
            }

            private static PlaneBasis ComputePlaneBasis(float3 planeNormal, AxisTransformFlags flags,
                quaternion cameraRotation)
            {
                if (flags.Has(AxisTransformFlags.CameraRelative))
                {
                    var camForward = math.rotate(cameraRotation, math.forward());
                    var projForward = camForward - math.dot(camForward, planeNormal) * planeNormal;

                    if (math.lengthsq(projForward) > 0.0001f)
                    {
                        var forward = math.normalize(projForward);
                        return new PlaneBasis
                        {
                            Right = math.normalize(math.cross(planeNormal, forward)),
                            Forward = forward
                        };
                    }

                    var camUp = math.rotate(cameraRotation, math.up());
                    var projUp = camUp - math.dot(camUp, planeNormal) * planeNormal;
                    var fallbackForward = math.normalize(projUp);
                    return new PlaneBasis
                    {
                        Right = math.normalize(math.cross(planeNormal, fallbackForward)),
                        Forward = fallbackForward
                    };
                }

                if (math.abs(planeNormal.y) > 0.99f)
                {
                    var sign = math.sign(planeNormal.y);
                    return new PlaneBasis { Right = math.right() * sign, Forward = math.forward() * sign };
                }

                if (math.abs(planeNormal.z) > 0.99f)
                {
                    var sign = math.sign(planeNormal.z);
                    return new PlaneBasis { Right = math.right() * sign, Forward = math.up() * sign };
                }

                {
                    var right = math.normalize(math.cross(math.up(), planeNormal));
                    return new PlaneBasis
                    {
                        Right = right,
                        Forward = math.normalize(math.cross(planeNormal, right))
                    };
                }
            }

            private bool TryResolveTarget(Target targetMode, ushort linkKey, Entity self, in Targets targets,
                out Entity resolved)
            {
                resolved = Entity.Null;
                var t = targets.Get(targetMode, self);
                if (t == Entity.Null) return false;

                if (linkKey == 0)
                {
                    resolved = t;
                    return true;
                }

                if (EntityLinkResolver.TryResolve(t, linkKey, Sources, Entries, out var linked))
                {
                    resolved = linked;
                    return true;
                }

                resolved = t;
                return true;
            }

            private struct PlaneBasis
            {
                public float3 Right;
                public float3 Forward;
            }
        }
    }
}