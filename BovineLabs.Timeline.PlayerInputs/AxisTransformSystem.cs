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

        // NEW Dependencies
        private ComponentLookup<Parent> _parents;
        private ComponentLookup<LocalToWorld> _ltws;
        private ConditionEventWriter.Lookup _writers;

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

            [NativeDisableParallelForRestriction] public ComponentLookup<LocalTransform> Transforms;
            [NativeDisableParallelForRestriction] public ConditionEventWriter.Lookup Writers;

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

                // 1. Process Condition Events
                if (state.HasInput && !state.WasInputActive)
                {
                    if (config.OnInputStart != ConditionKey.Null &&
                        TryResolveTarget(config.EventRouteTo, config.EventRouteLinkKey, targetEntity, targets,
                            out var eventTarget))
                        if (Writers.TryGet(eventTarget, out var writer))
                            writer.Trigger(config.OnInputStart, 1);
                }
                else if (!state.HasInput && state.WasInputActive)
                {
                    if (config.OnInputEnd != ConditionKey.Null &&
                        TryResolveTarget(config.EventRouteTo, config.EventRouteLinkKey, targetEntity, targets,
                            out var eventTarget))
                        if (Writers.TryGet(eventTarget, out var writer))
                            writer.Trigger(config.OnInputEnd, 1);
                }

                state.WasInputActive = state.HasInput;

                // 2. Process ResetOnNoInput
                if (config.ResetOnNoInput && !state.HasInput)
                {
                    state.CurrentPosition = state.Origin;
                    state.Velocity = float3.zero;
                    transform.Position = state.CurrentPosition;
                    Transforms[targetEntity] = transform;
                    return;
                }

                // 3. Transform Logic
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
                else if (config.Mode.IgnoreParentRotation())
                    if (Parents.TryGetComponent(targetEntity, out var parent) &&
                        Ltws.TryGetComponent(parent.Value, out var parentLtw))
                    {
                        var parentRot = new quaternion(parentLtw.Value);
                        inputVec = math.rotate(math.inverse(parentRot), inputVec);
                    }

                var lerpT = config.Smoothing > 0f ? math.saturate(DeltaTime * config.Smoothing) : 1f;

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