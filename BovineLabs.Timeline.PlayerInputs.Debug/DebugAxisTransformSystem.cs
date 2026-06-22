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
    // Quill overlay for AxisTransform "carrots": draws a marker at each clip-bound carrier and a line from its parent
    // (the body it offsets from) to it, so you can SEE which carrot a clip actually drives. A clip-bound carrot's
    // marker moves with input; a DUPLICATE rig's carriers (no clip bound) draw nothing and stay frozen - which is how
    // you spot that you're watching an un-driven copy. ACTIVE clips draw bright orange, inactive dim.
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
            this.ltws = state.GetComponentLookup<LocalToWorld>(true);
            this.parents = state.GetComponentLookup<Parent>(true);
            this.active = state.GetComponentLookup<ClipActive>(true);
        }

        public void OnUpdate(ref SystemState state)
        {
            this.ltws.Update(ref state);
            this.parents.Update(ref state);
            this.active.Update(ref state);

            var renderer = SystemAPI.GetSingleton<DrawSystem.Singleton>().CreateDrawer();

            state.Dependency = new DrawJob
            {
                Renderer = renderer,
                Ltws = this.ltws,
                Parents = this.parents,
                Active = this.active,
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
                if (carrot == Entity.Null || !this.Ltws.HasComponent(carrot))
                {
                    return;
                }

                var isActive = this.Active.HasComponent(clip) && this.Active.IsComponentEnabled(clip);
                var color = isActive ? new Color(1f, 0.55f, 0.05f) : new Color(0.45f, 0.45f, 0.45f, 0.6f);

                var carrotPos = this.Ltws[carrot].Position;

                // Line from the anchor (parent body) to the carrot, so the offset is unmistakable.
                var anchorPos = carrotPos;
                if (this.Parents.HasComponent(carrot) && this.Ltws.HasComponent(this.Parents[carrot].Value))
                {
                    anchorPos = this.Ltws[this.Parents[carrot].Value].Position;
                    this.Renderer.Line(anchorPos, carrotPos, color);
                }

                this.Renderer.Point(carrotPos, 0.35f, color);

                var label = new FixedString64Bytes();
                label.Append("carrot #");
                label.Append(carrot.Index);
                if (isActive)
                {
                    label.Append(" d=");
                    label.Append((int)math.round(math.distance(anchorPos, carrotPos) * 100f));
                    label.Append("cm");
                }

                this.Renderer.Text64(carrotPos + new float3(0f, 0.55f, 0f), label, color, 11f);
            }
        }
    }
}
#endif
