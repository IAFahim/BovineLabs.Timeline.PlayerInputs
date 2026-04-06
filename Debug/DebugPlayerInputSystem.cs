using BovineLabs.Core;
using BovineLabs.Quill;
using PlayerInputs.Data;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace PlayerInputs.Debug
{
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(DebugSystemGroup))]
    public partial struct DebugPlayerInputSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var drawer = SystemAPI.GetSingleton<DrawSystem.Singleton>().CreateDrawer();

            state.Dependency = new DebugDrawInputJob
            {
                Drawer = drawer,
            }.Schedule(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(InputProviderTag))] // Changed to Provider so we can see raw inputs immediately!
        private partial struct DebugDrawInputJob : IJobEntity
        {
            public Drawer Drawer;

            private void Execute(
                in PlayerId id,
                in DynamicBuffer<InputAxisBuffer> axes,
                in DynamicBuffer<InputButtonDownBuffer> downs,
                in DynamicBuffer<InputButtonHeldBuffer> helds,
                in DynamicBuffer<InputButtonUpBuffer> ups)
            {
                if (axes.Length == 0 && downs.Length == 0 && helds.Length == 0 && ups.Length == 0) return;

                // Offset the text based on Player ID so they don't overlap if 2 players join
                float3 headPos = new float3(id.Value * 2f, 2.2f, 0);

                Drawer.Line(new float3(id.Value * 2f, 0, 0), headPos, new Color(1f, 1f, 1f, 0.2f));

                FixedString512Bytes text = new FixedString512Bytes();

                text.Append("[Player ");
                text.Append(id.Value);
                text.Append("]\n");

                if (axes.Length > 0)
                {
                    text.Append("Axes: ");
                    for (int i = 0; i < axes.Length; i++)
                    {
                        text.Append(InputKeys.KeyToName(axes[i].ActionId));
                        text.Append("(");
                        text.Append(((int)(axes[i].Value.x * 10)) / 10f);
                        text.Append(", ");
                        text.Append(((int)(axes[i].Value.y * 10)) / 10f);
                        text.Append(") ");
                    }
                    text.Append("\n");
                }

                if (downs.Length > 0)
                {
                    text.Append("Down: ");
                    for (int i = 0; i < downs.Length; i++) { text.Append(InputKeys.KeyToName(downs[i].ActionId)); text.Append(" "); }
                    text.Append("\n");
                }

                if (helds.Length > 0)
                {
                    text.Append("Held: ");
                    for (int i = 0; i < helds.Length; i++) { text.Append(InputKeys.KeyToName(helds[i].ActionId)); text.Append(" "); }
                    text.Append("\n");
                }

                if (ups.Length > 0)
                {
                    text.Append("Up: ");
                    for (int i = 0; i < ups.Length; i++) { text.Append(InputKeys.KeyToName(ups[i].ActionId)); text.Append(" "); }
                    text.Append("\n");
                }

                Drawer.Text512(headPos, text, Color.white, 14f);
            }
        }
    }
}