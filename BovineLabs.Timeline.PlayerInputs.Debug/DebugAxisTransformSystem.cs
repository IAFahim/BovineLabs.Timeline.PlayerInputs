#if UNITY_EDITOR || BL_DEBUG
using BovineLabs.Core;
using BovineLabs.Quill;
using BovineLabs.Timeline.Data;
using BovineLabs.Timeline.PlayerInputs.Data;
using Unity.Burst;
using Unity.Collections;
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

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<DrawSystem.Singleton>();
            state.RequireForUpdate<AxisTransformConfig>();
            ltws = state.GetComponentLookup<LocalToWorld>(true);
            parents = state.GetComponentLookup<Parent>(true);
            active = state.GetComponentLookup<ClipActive>(true);
        }

        public void OnUpdate(ref SystemState state)
        {
            ltws.Update(ref state);
            parents.Update(ref state);
            active.Update(ref state);

            var renderer = SystemAPI.GetSingleton<DrawSystem.Singleton>().CreateDrawer();

            state.Dependency = new DrawJob
            {
                Renderer = renderer,
                Ltws = ltws,
                Parents = parents,
                Active = active
            }.Schedule(state.Dependency);
        }

        [BurstCompile]
        private partial struct DrawJob : IJobEntity
        {
            public Drawer Renderer;

            [ReadOnly] public ComponentLookup<LocalToWorld> Ltws;
            [ReadOnly] public ComponentLookup<Parent> Parents;
            [ReadOnly] public ComponentLookup<ClipActive> Active;

            private void Execute(Entity clip, in TrackBinding binding)
            {
                var carrot = binding.Value;
                if (carrot == Entity.Null || !Ltws.HasComponent(carrot)) return;

                var isActive = Active.HasComponent(clip) && Active.IsComponentEnabled(clip);
                var color = isActive ? Color.yellow : new Color(0.5f, 0.5f, 0.5f, 0.5f);

                var ltw = Ltws[carrot];
                var pos = ltw.Position;

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