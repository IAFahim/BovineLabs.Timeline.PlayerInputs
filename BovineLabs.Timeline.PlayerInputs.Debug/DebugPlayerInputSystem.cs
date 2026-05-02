// #if UNITY_EDITOR || BL_DEBUG
// using BovineLabs.Core;
// using BovineLabs.Quill;
// using BovineLabs.Timeline.PlayerInputs.Data;
// using Unity.Collections;
// using Unity.Entities;
// using Unity.Mathematics;
// using UnityEngine;
//
// namespace BovineLabs.Timeline.PlayerInputs.Debug
// {
//     [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ServerSimulation |
//                        WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.Editor)]
//     [UpdateInGroup(typeof(DebugSystemGroup))]
//     public partial struct DebugPlayerInputSystem : ISystem
//     {
//         public void OnCreate(ref SystemState state)
//         {
//             state.RequireForUpdate<DrawSystem.Singleton>();
//         }
//
//         public void OnUpdate(ref SystemState state)
//         {
//             var renderer = SystemAPI.GetSingleton<DrawSystem.Singleton>().CreateDrawer();
//             var chronos = (uint)(SystemAPI.Time.ElapsedTime * 1000.0);
//             
//             state.Dependency = new RenderDiagnosticsJob
//             {
//                 Renderer = renderer,
//                 Chronos = chronos
//             }.Schedule(state.Dependency);
//         }
//
//         [WithAll(typeof(ConsumerTag))]
//         private partial struct RenderDiagnosticsJob : IJobEntity
//         {
//             public Drawer Renderer;
//             public uint Chronos;
//
//             private void Execute(Entity entity, in PlayerId id, in InputSource source, in InputState state, in DynamicBuffer<InputAxis> axes, in DynamicBuffer<InputHistory> history)
//             {
//                 var origin = new float3(id.Value * 8f, 4f, 0f);
//
//                 RenderHeader(origin, entity, id, source);
//                 
//                 var panelOrigin = origin + new float3(0, -1f, 0f);
//                 RenderState(panelOrigin + new float3(-2.5f, 0f, 0f), state);
//                 RenderAxes(panelOrigin + new float3(0f, 0f, 0f), axes);
//                 RenderHistory(panelOrigin + new float3(2.5f, 0f, 0f), history, Chronos);
//             }
//
//             private void RenderHeader(float3 position, Entity entity, PlayerId id, InputSource source)
//             {
//                 var title = new FixedString64Bytes();
//                 title.Append("CONSUMER [");
//                 title.Append(id.Value);
//                 title.Append("] ENTITY ");
//                 title.Append(entity.Index);
//
//                 Renderer.Text64(position, title, Color.white, 16f);
//                 
//                 var srcLabel = new FixedString64Bytes();
//                 srcLabel.Append("SOURCE: ");
//                 srcLabel.Append(source.Provider == Entity.Null ? "NULL" : source.Provider.Index.ToString());
//                 
//                 Renderer.Text64(position + new float3(0f, -0.4f, 0f), srcLabel, source.Provider == Entity.Null ? Color.red : Color.green, 10f);
//
//                 Renderer.Line(position + new float3(-3.5f, -0.7f, 0f), position + new float3(3.5f, -0.7f, 0f), new Color(1f, 1f, 1f, 0.3f));
//             }
//
//             private void RenderState(float3 position, InputState state)
//             {
//                 Renderer.Text32(position, "LIVE STATE", new Color(1f, 0.8f, 0.2f), 12f);
//                 Renderer.Line(position + new float3(-1f, -0.2f, 0f), position + new float3(1f, -0.2f, 0f), new Color(1f, 0.8f, 0.2f, 0.3f));
//
//                 var cursor = position + new float3(0f, -0.6f, 0f);
//
//                 for (byte i = 0; i < 255; i++)
//                 {
//                     if (state.Down[i])
//                     {
//                         RenderSignal(cursor, i, "DOWN", new Color(0f, 1f, 1f, 1f));
//                         cursor.y -= 0.25f;
//                     }
//                     else if (state.Held[i])
//                     {
//                         RenderSignal(cursor, i, "HELD", new Color(0f, 1f, 0.5f, 0.8f));
//                         cursor.y -= 0.25f;
//                     }
//                     else if (state.Up[i])
//                     {
//                         RenderSignal(cursor, i, "UP  ", new Color(1f, 0.2f, 0.2f, 1f));
//                         cursor.y -= 0.25f;
//                     }
//                 }
//             }
//
//             private void RenderSignal(float3 position, byte action, FixedString32Bytes phase, Color tint)
//             {
//                 var format = new FixedString64Bytes();
//                 format.Append("[");
//                 format.Append(phase);
//                 format.Append("] ");
//                 format.Append(MultiInputSettings.KeyToName(action));
//
//                 Renderer.Text64(position, format, tint, 11f);
//             }
//
//             private void RenderAxes(float3 position, DynamicBuffer<InputAxis> axes)
//             {
//                 Renderer.Text32(position, "KINETICS", new Color(0.2f, 0.8f, 1f), 12f);
//                 Renderer.Line(position + new float3(-1f, -0.2f, 0f), position + new float3(1f, -0.2f, 0f), new Color(0.2f, 0.8f, 1f, 0.3f));
//
//                 var cursor = position + new float3(0f, -1.2f, 0f);
//
//                 for (var i = 0; i < axes.Length; i++)
//                 {
//                     var axis = axes[i];
//                     var boundary = new float3(0f, 0f, 1f) * 0.4f;
//                     var vector = new float3(axis.Value.x, axis.Value.y, 0f) * 0.4f;
//
//                     Renderer.Circle(cursor, boundary, new Color(1f, 1f, 1f, 0.1f));
//                     Renderer.Line(cursor, cursor + vector, new Color(0f, 1f, 1f, 1f));
//                     Renderer.Point(cursor + vector, 0.08f, new Color(0f, 1f, 1f, 1f));
//
//                     var label = new FixedString64Bytes();
//                     label.Append(MultiInputSettings.KeyToName(axis.ActionId));
//
//                     Renderer.Text64(cursor + new float3(0f, 0.6f, 0f), label, new Color(1f, 1f, 1f, 0.7f), 10f);
//
//                     cursor.y -= 1.5f;
//                 }
//             }
//
//             private void RenderHistory(float3 position, DynamicBuffer<InputHistory> history, uint chronos)
//             {
//                 Renderer.Text32(position, "HISTORY", new Color(0.8f, 0.4f, 1f), 12f);
//                 Renderer.Line(position + new float3(-1f, -0.2f, 0f), position + new float3(1f, -0.2f, 0f), new Color(0.8f, 0.4f, 1f, 0.3f));
//
//                 var cursor = position + new float3(0f, -0.6f, 0f);
//
//                 for (var i = history.Length - 1; i >= 0; i--)
//                 {
//                     var record = history[i];
//                     var delta = chronos - record.Tick;
//
//                     if (delta > 2000) continue;
//
//                     var opacity = math.clamp(1f - delta / 2000f, 0.1f, 1f);
//                     var phaseStr = record.Phase == InputPhase.Down ? "+ " : "- ";
//                     var tint = record.Phase == InputPhase.Down 
//                         ? new Color(0f, 1f, 1f, opacity) 
//                         : new Color(1f, 0.2f, 0.2f, opacity);
//
//                     var format = new FixedString64Bytes();
//                     format.Append(phaseStr);
//                     format.Append(MultiInputSettings.KeyToName(record.ActionId));
//                     format.Append(" (");
//                     format.Append(delta);
//                     format.Append("ms)");
//
//                     Renderer.Text64(cursor, format, tint, 10f);
//                     cursor.y -= 0.2f;
//                 }
//             }
//         }
//     }
// }
// #endif