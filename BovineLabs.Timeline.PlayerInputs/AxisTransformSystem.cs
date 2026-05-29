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
using Unity.Physics;
using Unity.Transforms;

namespace BovineLabs.Timeline.PlayerInputs
{
    [UpdateInGroup(typeof(TimelineComponentAnimationGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation)]
    public partial struct AxisTransformSystem : ISystem
    {
        private ComponentLookup<Targets> _targetsLookup;
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

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<AxisTransformConfig>();
            state.RequireForUpdate<InputRegistry>();
            _targetsLookup = state.GetComponentLookup<Targets>(true);
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

            [ReadOnly] [NativeDisableContainerSafetyRestriction] public NativeArray<Entity> Registry;
            [ReadOnly] public BufferLookup<InputAxis> Axes;
            [ReadOnly] [NativeDisableContainerSafetyRestriction] public ComponentLookup<PlayerId> PlayerIds;

            [ReadOnly] public ComponentLookup<Parent> Parents;
            [ReadOnly] public ComponentLookup<LocalToWorld> Ltws;
            [ReadOnly] public ComponentLookup<PhysicsMass> Masses;

            [NativeDisableParallelForRestriction] public ComponentLookup<LocalTransform> Transforms;
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

                var lerpT = math.clamp(DeltaTime * (1.0f / math.max(0.001f, config.Smoothing)), 0f, 1f);
                var targetVel = inputVec * config.Range;

                switch (config.Mode)
                {
                    case AxisTransformMode.RigidbodyVelocity:
                        pv.Linear = math.lerp(pv.Linear, targetVel, lerpT);
                        break;
                    case AxisTransformMode.RigidbodyForce:
                        var mass = Masses.TryGetComponent(targetEntity, out var m) ? m.InverseMass : 1f;
                        if (mass > 0f)
                            pv.Linear += targetVel * (DeltaTime / mass);
                        break;
                    case AxisTransformMode.RigidbodyImpulse:
                        if (risingEdge)
                            pv.Linear += targetVel;
                        break;
                }

                Velocities[targetEntity] = pv;
            }

            private void ApplyCarrot(in AxisTransformConfig config, ref AxisTransformState state,
                Entity targetEntity, in Targets targets, LocalTransform transform, float3 inputVec)
            {
                if (!state.Initialized)
                {
                    state.AnchorOrigin = targets.Get(config.ReadRootFrom, targetEntity) != Entity.Null
                        ? Ltws[targets.Get(config.ReadRootFrom, targetEntity)].Position
                        : transform.Position;
                    state.DesiredOffset = float3.zero;
                    state.SmoothedOffset = float3.zero;
                    state.Initialized = true;
                }

                state.DesiredOffset = inputVec * config.LeashRadius;
                state.SmoothedOffset = math.lerp(state.SmoothedOffset, state.DesiredOffset,
                    math.clamp(DeltaTime * (1.0f / math.max(0.001f, config.Smoothing)), 0f, 1f));

                var currentAnchor = targets.Get(config.ReadRootFrom, targetEntity) != Entity.Null
                    ? Ltws[targets.Get(config.ReadRootFrom, targetEntity)].Position
                    : state.AnchorOrigin;

                transform.Position = currentAnchor + state.SmoothedOffset;
                Transforms[targetEntity] = transform;
            }

            private struct PlaneBasis
            {
                public float3 Forward;
                public float3 Right;
            }

            private static PlaneBasis ComputePlaneBasis(float3 planeNormal, AxisTransformFlags flags, quaternion cameraRotation)
            {
                float3 forward;
                float3 right;

                if (flags.Has(AxisTransformFlags.CameraRelative))
                {
                    var camForward = math.mul(cameraRotation, new float3(0, 0, 1));
                    var camRight = math.mul(cameraRotation, new float3(1, 0, 0));

                    forward = math.normalize(camForward - math.dot(camForward, planeNormal) * planeNormal);
                    right = math.normalize(camRight - math.dot(camRight, planeNormal) * planeNormal);
                }
                else
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

                return new PlaneBasis { Forward = forward, Right = right };
            }
        }
    }
}
