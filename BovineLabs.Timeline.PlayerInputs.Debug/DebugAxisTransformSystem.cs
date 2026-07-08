#if UNITY_EDITOR || BL_DEBUG
using BovineLabs.Core.Extensions;
using BovineLabs.Core.Iterators;
using BovineLabs.Core;
using BovineLabs.Quill;
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
using UnityEngine;

namespace BovineLabs.Timeline.PlayerInputs.Debug
{
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ServerSimulation |
                       WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(DebugSystemGroup))]
    public partial struct DebugAxisTransformSystem : ISystem
    {
        private ComponentLookup<LocalToWorld> ltws;
        private ComponentLookup<Parent> parents;
        private ComponentLookup<ClipActive> active;

        private UnsafeComponentLookup<Targets> targets;
        private UnsafeComponentLookup<EntityLinkSource> sources;
        private UnsafeBufferLookup<EntityLinkEntry> entries;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<DrawSystem.Singleton>();
            state.RequireForUpdate<AxisTransformConfig>();
            ltws = state.GetComponentLookup<LocalToWorld>(true);
            parents = state.GetComponentLookup<Parent>(true);
            active = state.GetComponentLookup<ClipActive>(true);

            targets = state.GetUnsafeComponentLookup<Targets>(true);
            sources = state.GetUnsafeComponentLookup<EntityLinkSource>(true);
            entries = state.GetUnsafeBufferLookup<EntityLinkEntry>(true);
        }

        public void OnUpdate(ref SystemState state)
        {
            ltws.Update(ref state);
            parents.Update(ref state);
            active.Update(ref state);
            targets.Update(ref state);
            sources.Update(ref state);
            entries.Update(ref state);

            var renderer = SystemAPI.GetSingleton<DrawSystem.Singleton>().CreateDrawer();

            state.Dependency = new DrawJob
            {
                Renderer = renderer,
                Ltws = ltws,
                Parents = parents,
                Active = active,
                Targets = targets,
                Sources = sources,
                Entries = entries
            }.Schedule(state.Dependency);
        }

        [BurstCompile]
        private partial struct DrawJob : IJobEntity
        {
            public Drawer Renderer;

            [ReadOnly] public ComponentLookup<LocalToWorld> Ltws;
            [ReadOnly] public ComponentLookup<Parent> Parents;
            [ReadOnly] public ComponentLookup<ClipActive> Active;

            [ReadOnly] public UnsafeComponentLookup<Targets> Targets;
            [ReadOnly] public UnsafeComponentLookup<EntityLinkSource> Sources;
            [ReadOnly] public UnsafeBufferLookup<EntityLinkEntry> Entries;

            private void Execute(Entity clip, in TrackBinding binding, in AxisTransformConfig config)
            {
                var carrot = binding.Value;
                if (carrot == Entity.Null || !Ltws.HasComponent(carrot)) return;

                var ltw = Ltws[carrot];
                var pos = ltw.Position;

                // Surface the silent dead-clip case the buffer drawer already shows: the AxisTransform reads the seat
                // through config.ConsumerLinkKey, and a wrong/unassigned schema resolves nothing -> the carrot never
                // moves with no message anywhere. Paint a red cross + label at the carrot so a miss is a glance, not a
                // multi-hour hunt (mirrors DebugInputBufferSystem's "link miss" cue).
                var linked = Targets.TryGetComponent(carrot, out var t)
                    && EntityLinkResolver.TryResolve(carrot, t, config.ReadRootFrom, config.ConsumerLinkKey,
                        Sources, Entries, out _);

                if (!linked)
                {
                    var red = new Color(1f, 0.3f, 0.3f);
                    var up = new float3(0f, 1f, 0f);
                    var right = new float3(1f, 0f, 0f);

                    Renderer.Point(pos, 0.14f, red);
                    Renderer.Line(pos - right * 0.15f + up * 0.55f, pos + right * 0.15f + up * 0.9f, red);
                    Renderer.Line(pos + right * 0.15f + up * 0.55f, pos - right * 0.15f + up * 0.9f, red);

                    var label = new FixedString64Bytes();
                    label.Append((FixedString32Bytes)"AXIS link miss");
                    Renderer.Text64(pos + up * 1.05f, label, red, 12f);
                    return;
                }

                var isActive = Active.HasComponent(clip) && Active.IsComponentEnabled(clip);
                var color = isActive ? Color.yellow : new Color(0.5f, 0.5f, 0.5f, 0.5f);

                if (Parents.HasComponent(carrot) && Ltws.HasComponent(Parents[carrot].Value))
                {
                    var pLtw = Ltws[Parents[carrot].Value];
                    Renderer.Line(pLtw.Position, pos, color);
                    Renderer.Line(pLtw.Position, pLtw.Position + pLtw.Forward, Color.red);
                }

                Renderer.Line(pos, pos + ltw.Forward, Color.cyan);
                Renderer.Point(pos, 0.1f, color);
            }
        }
    }
}
#endif