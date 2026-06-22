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
                var color = isActive ? new Color(1f, 0.55f, 0.05f) : new Color(0.45f, 0.45f, 0.45f, 0.6f);

                var carrotPos = Ltws[carrot].Position;

                var anchorPos = carrotPos;
                if (Parents.HasComponent(carrot) && Ltws.HasComponent(Parents[carrot].Value))
                {
                    anchorPos = Ltws[Parents[carrot].Value].Position;
                    Renderer.Line(anchorPos, carrotPos, color);
                }

                Renderer.Point(carrotPos, 0.35f, color);

                var label = new FixedString64Bytes();
                label.Append("carrot #");
                label.Append(carrot.Index);
                if (isActive)
                {
                    label.Append(" d=");
                    label.Append((int)math.round(math.distance(anchorPos, carrotPos) * 100f));
                    label.Append("cm");
                }

                Renderer.Text64(carrotPos + new float3(0f, 0.55f, 0f), label, color, 11f);
            }
        }
    }
}
#endif