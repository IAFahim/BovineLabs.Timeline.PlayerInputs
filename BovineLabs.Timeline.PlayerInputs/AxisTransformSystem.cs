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

                // ── Input sampling ────────────────────────────────────────────────
                var axisValue = float2.zero;
                for (var i = 0; i < axesBuf.Length; i++)
                {
                    if (axesBuf[i].ActionId != config.ActionId) continue;
                    axisValue = axesBuf[i].Value;
                    break;
                }

                if (math.lengthsq(axisValue) > 0.0001f)
                {
                    state.HasInput = true;
                    state.LastInput = axisValue;
                }
                else
                {
                    state.HasInput = false;
                    if (!config.Mode.KeepLast()) state.LastInput = float2.zero;
                }

                var transform = Transforms[targetEntity];

                if (!state.Initialized)
                {
                    state.Origin = transform.Position;
                    state.CurrentPosition = transform.Position;
                    state.Velocity = float3.zero;
                    state.WasInputActive = false;
                    state.Initialized = true;
                }

                // ── Edge detection (before WasInputActive update) ────────────────
                var risingEdge  = state.HasInput && !state.WasInputActive;
                var fallingEdge = !state.HasInput && state.WasInputActive;

                // ── Condition events ──────────────────────────────────────────────
                if (risingEdge && config.OnInputStart != ConditionKey.Null &&
                    TryResolveTarget(config.EventRouteTo, config.EventRouteLinkKey, targetEntity, targets,
                        out var startTarget))
                    if (Writers.TryGet(startTarget, out var w)) w.Trigger(config.OnInputStart, 1);

                if (fallingEdge && config.OnInputEnd != ConditionKey.Null &&
                    TryResolveTarget(config.EventRouteTo, config.EventRouteLinkKey, targetEntity, targets,
                        out var endTarget))
                    if (Writers.TryGet(endTarget, out var w)) w.Trigger(config.OnInputEnd, 1);

                state.WasInputActive = state.HasInput;

                // ── Basis vectors (shared by all modes) ───────────────────────────
                var planeNormal = math.normalize(config.Plane);
                float3 right, forward;

                if (config.Mode.IsCameraRelative())
                {
                    var camForward = math.rotate(CameraRotation, math.forward());
                    var projForward = camForward - math.dot(camForward, planeNormal) * planeNormal;

                    if (math.lengthsq(projForward) > 0.0001f)
                    {
                        forward = math.normalize(projForward);
                        right = math.normalize(math.cross(planeNormal, forward));
                    }
                    else
                    {
                        var camUp = math.rotate(CameraRotation, math.up());
                        var projUp = camUp - math.dot(camUp, planeNormal) * planeNormal;
                        forward = math.normalize(projUp);
                        right = math.normalize(math.cross(planeNormal, forward));
                    }
                }
                else
                {
                    if (math.abs(planeNormal.y) > 0.99f)
                    {
                        right = math.right() * math.sign(planeNormal.y);
                        forward = math.forward() * math.sign(planeNormal.y);
                    }
                    else if (math.abs(planeNormal.z) > 0.99f)
                    {
                        right = math.right() * math.sign(planeNormal.z);
                        forward = math.up() * math.sign(planeNormal.z);
                    }
                    else
                    {
                        right = math.normalize(math.cross(math.up(), planeNormal));
                        forward = math.normalize(math.cross(planeNormal, right));
                    }
                }

                var inputVec = right * state.LastInput.x + forward * state.LastInput.y;

                if (config.Mode.IsLocal())
                    inputVec = math.rotate(transform.Rotation, inputVec);
                else if (config.Mode.IgnoreParentRotation() &&
                         Parents.TryGetComponent(targetEntity, out var parent) &&
                         Ltws.TryGetComponent(parent.Value, out var parentLtw))
                {
                    var parentRot = new quaternion(parentLtw.Value);
                    inputVec = math.rotate(math.inverse(parentRot), inputVec);
                }

                var lerpT = config.Smoothing > 0f ? math.saturate(DeltaTime * config.Smoothing) : 1f;

                // ── Rigidbody path ────────────────────────────────────────────────
                if (config.Mode.IsRigidbody())
                {
                    if (!Velocities.HasComponent(targetEntity)) return;
                    var pv = Velocities[targetEntity];

                    if (config.ResetOnNoInput && !state.HasInput)
                    {
                        // Zero only the planar component; preserve perpendicular (e.g. gravity).
                        var perp = math.dot(pv.Linear, planeNormal) * planeNormal;
                        pv.Linear = perp;
                        Velocities[targetEntity] = pv;
                        return;
                    }

                    if (config.Mode.IsRigidbodyVelocity())
                    {
                        // Directly lerp toward target planar velocity; perpendicular is untouched.
                        var perp        = math.dot(pv.Linear, planeNormal) * planeNormal;
                        var planarNow   = pv.Linear - perp;
                        var planarTarget = state.HasInput ? inputVec * config.Range : float3.zero;
                        pv.Linear = perp + math.lerp(planarNow, planarTarget, lerpT);
                    }
                    else if (config.Mode.IsRigidbodyForce())
                    {
                        var inverseMass = Masses.TryGetComponent(targetEntity, out var mass) ? mass.InverseMass : 1f;
                        if (state.HasInput)
                            pv.Linear += inputVec * config.Range * inverseMass * DeltaTime;
                        if (config.Drag > 0f)
                            pv.Linear *= math.max(0f, 1f - config.Drag * DeltaTime);
                    }
                    else if (config.Mode.IsRigidbodyImpulse() && risingEdge)
                    {
                        var inverseMass = Masses.TryGetComponent(targetEntity, out var mass) ? mass.InverseMass : 1f;
                        pv.Linear += inputVec * config.Range * inverseMass;
                    }

                    // Clamp planar speed; perpendicular (gravity) is unaffected.
                    if (config.ClampRadius > 0f)
                    {
                        var perp   = math.dot(pv.Linear, planeNormal) * planeNormal;
                        var planar = pv.Linear - perp;
                        var speedSq = math.lengthsq(planar);
                        if (speedSq > config.ClampRadius * config.ClampRadius)
                            planar = planar / math.sqrt(speedSq) * config.ClampRadius;
                        pv.Linear = perp + planar;
                    }

                    Velocities[targetEntity] = pv;
                    return;
                }

                // ── LocalTransform path ───────────────────────────────────────────
                if (config.ResetOnNoInput && !state.HasInput)
                {
                    state.CurrentPosition = state.Origin;
                    state.Velocity = float3.zero;
                    transform.Position = state.CurrentPosition;
                    Transforms[targetEntity] = transform;
                    return;
                }

                if (config.Mode.IsVelocity())
                {
                    var targetVel = inputVec * config.Range;
                    state.Velocity = math.lerp(state.Velocity, targetVel, lerpT);
                    state.CurrentPosition += state.Velocity * DeltaTime;
                }
                else
                {
                    var targetPos = state.Origin + inputVec * config.Range;
                    state.CurrentPosition = math.lerp(state.CurrentPosition, targetPos, lerpT);
                }

                if (config.ClampRadius > 0f)
                {
                    var offset = state.CurrentPosition - state.Origin;
                    var distSq = math.lengthsq(offset);
                    if (distSq > config.ClampRadius * config.ClampRadius)
                        state.CurrentPosition = state.Origin + offset / math.sqrt(distSq) * config.ClampRadius;
                }

                transform.Position = state.CurrentPosition;
                Transforms[targetEntity] = transform;
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
        }
    }
}