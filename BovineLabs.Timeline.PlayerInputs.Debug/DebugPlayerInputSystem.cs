#if UNITY_EDITOR || BL_DEBUG
using BovineLabs.Core;
using BovineLabs.Quill;
using BovineLabs.Timeline.PlayerInputs.Data;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace BovineLabs.Timeline.PlayerInputs.Debug
{
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ServerSimulation |
                       WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(DebugSystemGroup))]
    public partial struct DebugPlayerInputSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<DrawSystem.Singleton>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var renderer = SystemAPI.GetSingleton<DrawSystem.Singleton>().CreateDrawer();
            var chronos = (uint)(SystemAPI.Time.ElapsedTime * 1000.0);

            state.Dependency = new RenderInputDiagnostics
            {
                Renderer = renderer,
                Chronos = chronos
            }.Schedule(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(InputProviderTag))]
        private partial struct RenderInputDiagnostics : IJobEntity
        {
            public Drawer Renderer;
            public uint Chronos;

            private void Execute(
                in PlayerId id,
                in InputState state,
                in DynamicBuffer<InputAxisBuffer> axes,
                in DynamicBuffer<InputHistory> history)
            {
                var origin = new float3(id.Value * 4f, 2f, 0f);

                RenderHeader(origin, id);
                RenderTelemetry(origin + new float3(-1.5f, -0.5f, 0f), state);
                RenderKinetics(origin + new float3(0f, -1.0f, 0f), axes);
                RenderChronology(origin + new float3(1.5f, -0.5f, 0f), history, Chronos);
            }

            private void RenderHeader(float3 position, PlayerId id)
            {
                var label = new FixedString32Bytes();
                label.Append("INTERFACE 0");
                label.Append(id.Value);

                Renderer.Text32(position, label, Color.cyan);
                Renderer.Line(position + new float3(-2f, -0.2f, 0f), position + new float3(2f, -0.2f, 0f),
                    new Color(0f, 1f, 1f, 0.4f));
            }

            private void RenderTelemetry(float3 position, InputState state)
            {
                var cursor = position;

                for (byte i = 0; i < 255; i++)
                    if (state.Down[i])
                    {
                        RenderSignal(cursor, i, "D", new Color(0f, 1f, 1f, 1f));
                        cursor.y -= 0.15f;
                    }
                    else if (state.Held[i])
                    {
                        RenderSignal(cursor, i, "H", new Color(0f, 1f, 0.5f, 1f));
                        cursor.y -= 0.15f;
                    }
                    else if (state.Up[i])
                    {
                        RenderSignal(cursor, i, "U", new Color(1f, 0.2f, 0.2f, 1f));
                        cursor.y -= 0.15f;
                    }
            }

            private void RenderSignal(float3 position, byte action, FixedString32Bytes phase, Color tint)
            {
                var format = new FixedString64Bytes();
                format.Append("[");
                format.Append(phase);
                format.Append("] ");
                format.Append(MuliInputSettings.KeyToName(action));

                Renderer.Text64(position, format, tint, 12f);
            }

            private void RenderKinetics(float3 position, DynamicBuffer<InputAxisBuffer> axes)
            {
                var cursor = position;

                for (var i = 0; i < axes.Length; i++)
                {
                    var axis = axes[i];
                    var boundary = new float3(0f, 0f, 1f) * 0.25f;
                    var vector = new float3(axis.Value.x, axis.Value.y, 0f) * 0.25f;

                    Renderer.Circle(cursor, boundary, new Color(1f, 1f, 1f, 0.1f));
                    Renderer.Line(cursor, cursor + vector, new Color(0f, 1f, 1f, 1f));
                    Renderer.Point(cursor + vector, 0.05f, new Color(0f, 1f, 1f, 1f));

                    var label = new FixedString64Bytes();
                    label.Append(MuliInputSettings.KeyToName(axis.ActionId));

                    Renderer.Text64(cursor + new float3(0f, 0.35f, 0f), label, new Color(1f, 1f, 1f, 0.5f), 10f);

                    cursor.y -= 0.8f;
                }
            }

            private void RenderChronology(float3 position, DynamicBuffer<InputHistory> history, uint chronos)
            {
                var cursor = position;
                var limit = math.max(0, history.Length - 16);

                for (var i = history.Length - 1; i >= limit; i--)
                {
                    var record = history[i];
                    var delta = chronos - record.Tick;

                    if (delta > 2000) continue;

                    var opacity = math.clamp(1f - delta / 2000f, 0f, 1f);
                    var tint = new Color(1f, 1f, 1f, opacity);

                    var format = new FixedString64Bytes();
                    format.Append("-");
                    format.Append(delta);
                    format.Append("ms ");
                    format.Append(MuliInputSettings.KeyToName(record.ActionId));

                    Renderer.Text64(cursor, format, tint, 10f);
                    cursor.y -= 0.15f;
                }
            }
        }
    }
}
#endif