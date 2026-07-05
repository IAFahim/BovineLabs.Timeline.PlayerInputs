#if UNITY_EDITOR || BL_DEBUG
using BovineLabs.Core;
using BovineLabs.Core.Collections;
using BovineLabs.Core.ConfigVars;
using BovineLabs.Core.Extensions;
using BovineLabs.Core.Iterators;
using BovineLabs.Quill;
using BovineLabs.Timeline.Core.Debug;
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
using UnityEngine;

namespace BovineLabs.Timeline.PlayerInputs.Debug
{
    [Configurable]
    public static class InputBufferDebugConfig
    {
        [ConfigVar("inputbuffer.draw-enabled", false, "Force-enable the input buffer window/clear debug drawer.")]
        public static readonly SharedStatic<bool> Enabled = SharedStatic<bool>.GetOrCreate<EnabledTag>();

        [ConfigVar("inputbuffer.scale", 1f, "World-space spacing/size multiplier for the drawer.")]
        public static readonly SharedStatic<float> Scale = SharedStatic<float>.GetOrCreate<ScaleTag>();

        [ConfigVar("inputbuffer.offset", 0f, 0f, 0f, 0f, "World anchor offset added to each bound entity.")]
        public static readonly SharedStatic<Vector4> Offset = SharedStatic<Vector4>.GetOrCreate<OffsetTag>();

        [ConfigVar("inputbuffer.window-color", 1f, 0.92f, 0.016f, 1f, "Accent for the buffer-window drawer.")]
        public static readonly SharedStatic<Color> WindowColor = SharedStatic<Color>.GetOrCreate<WindowColorTag>();

        [ConfigVar("inputbuffer.clear-color", 1f, 0.5f, 0.5f, 1f, "Accent for the buffer-clear drawer.")]
        public static readonly SharedStatic<Color> ClearColor = SharedStatic<Color>.GetOrCreate<ClearColorTag>();

        private struct EnabledTag
        {
        }

        private struct ScaleTag
        {
        }

        private struct OffsetTag
        {
        }

        private struct WindowColorTag
        {
        }

        private struct ClearColorTag
        {
        }
    }

    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ServerSimulation |
                       WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(DebugSystemGroup))]
    public partial struct DebugInputBufferSystem : ISystem
    {
        private ComponentLookup<TrackBinding> _bindings;
        private ComponentLookup<ClipActive> _active;
        private ComponentLookup<ActiveBufferMask> _masks;
        private BufferLookup<InputHistory> _histories;
        private ComponentLookup<LocalToWorld> _ltws;

        private UnsafeComponentLookup<Targets> _targets;
        private UnsafeComponentLookup<EntityLinkSource> _sources;
        private UnsafeBufferLookup<EntityLinkEntry> _entries;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<DrawSystem.Singleton>();
            state.RequireForUpdate<BufferWindowConfig>();

            _bindings = state.GetComponentLookup<TrackBinding>(true);
            _active = state.GetComponentLookup<ClipActive>(true);
            _masks = state.GetComponentLookup<ActiveBufferMask>(true);
            _histories = state.GetBufferLookup<InputHistory>(true);
            _ltws = state.GetComponentLookup<LocalToWorld>(true);

            _targets = state.GetUnsafeComponentLookup<Targets>(true);
            _sources = state.GetUnsafeComponentLookup<EntityLinkSource>(true);
            _entries = state.GetUnsafeBufferLookup<EntityLinkEntry>(true);
        }

        public void OnUpdate(ref SystemState state)
        {
            // inputbuffer.draw-enabled=true forces it on; otherwise toggle DebugInputBufferSystem in the draw filter.
            if (!TimelineDebugUtility.TryGetDrawer<DebugInputBufferSystem>(ref state,
                    InputBufferDebugConfig.Enabled.Data, out var renderer))
                return;

            _bindings.Update(ref state);
            _active.Update(ref state);
            _masks.Update(ref state);
            _histories.Update(ref state);
            _ltws.Update(ref state);
            _targets.Update(ref state);
            _sources.Update(ref state);
            _entries.Update(ref state);

            var tick = SystemAPI.HasSingleton<SimulationTick>()
                ? SystemAPI.GetSingleton<SimulationTick>().Value
                : 0u;

            var scale = InputBufferDebugConfig.Scale.Data;
            var offset = ((float4)InputBufferDebugConfig.Offset.Data).xyz;

            state.Dependency = new DrawWindowJob
            {
                Renderer = renderer,
                Ltws = _ltws,
                Active = _active,
                Masks = _masks,
                Histories = _histories,
                Targets = _targets,
                Sources = _sources,
                Entries = _entries,
                Tick = tick,
                Scale = scale,
                Offset = offset,
                Accent = InputBufferDebugConfig.WindowColor.Data
            }.Schedule(state.Dependency);

            state.Dependency = new DrawClearJob
            {
                Renderer = renderer,
                Ltws = _ltws,
                Active = _active,
                Targets = _targets,
                Sources = _sources,
                Entries = _entries,
                Scale = scale,
                Offset = offset,
                Accent = InputBufferDebugConfig.ClearColor.Data
            }.Schedule(state.Dependency);
        }

        [BurstCompile]
        private partial struct DrawWindowJob : IJobEntity
        {
            public Drawer Renderer;

            [ReadOnly] public ComponentLookup<LocalToWorld> Ltws;
            [ReadOnly] public ComponentLookup<ClipActive> Active;
            [ReadOnly] public ComponentLookup<ActiveBufferMask> Masks;
            [ReadOnly] public BufferLookup<InputHistory> Histories;

            [ReadOnly] public UnsafeComponentLookup<Targets> Targets;
            [ReadOnly] public UnsafeComponentLookup<EntityLinkSource> Sources;
            [ReadOnly] public UnsafeBufferLookup<EntityLinkEntry> Entries;

            public uint Tick;
            public float Scale;
            public float3 Offset;
            public Color Accent;

            private void Execute(Entity clip, in TrackBinding binding, in BufferWindowConfig config)
            {
                var bound = binding.Value;
                if (bound == Entity.Null || !Ltws.HasComponent(bound)) return;

                var isActive = Active.HasComponent(clip) && Active.IsComponentEnabled(clip);
                var accent = isActive ? Accent : new Color(0.5f, 0.5f, 0.5f, 0.5f);

                var pos = Ltws[bound].Position + Offset;
                var up = new float3(0f, 1f, 0f) * Scale;
                var right = new float3(1f, 0f, 0f) * Scale;

                Renderer.Point(pos, 0.15f * Scale, accent);

                var consumer = Entity.Null;
                var resolved = Targets.TryGetComponent(bound, out var targets)
                    && config.Consumer.TryResolve(bound, targets, Sources, Entries, out consumer);

                // Vertical stack above the entity, top-down: WIN / buf / hist. Even 0.4 spacing, no overlaps.
                // History ticks live at 0.8..~1.9; the stalk stops at 2.4; text rows start at 2.6.
                const float rowHead = 3.4f;
                const float rowBuf = 3.0f;
                const float rowHist = 2.6f;

                Renderer.Line(pos, pos + up * 2.4f, accent);

                var head = new FixedString64Bytes();
                head.Append(isActive ? "WIN " : "win ");
                head.Append(clip.Index);
                if (!resolved) head.Append(" ?");
                Renderer.Text64(pos + up * rowHead, head, accent, 12f * Scale);

                if (!resolved)
                {
                    DrawUnresolved(pos + up * rowBuf, right, up);
                    return;
                }

                var open = Masks.TryGetComponent(consumer, out var mask) && !mask.Value.AllFalse;
                var stateColor = open
                    ? new Color(0.3f, 1f, 0.6f)
                    : new Color(1f, 0.4f, 0.3f);

                var bits = new FixedString64Bytes();
                bits.Append("buf ");
                bits.Append(open ? mask.Value.CountBits() : 0);
                Renderer.Text64(pos + up * rowBuf, bits, stateColor, 11f * Scale);

                var histLen = Histories.TryGetBuffer(consumer, out var history) ? history.Length : 0;
                var hl = new FixedString64Bytes();
                hl.Append("hist ");
                hl.Append(histLen);
                Renderer.Text64(pos + up * rowHist, hl, new Color(1f, 1f, 1f, 0.8f), 11f * Scale);

                DrawHistoryTicks(pos, right, up, history, histLen);
            }

            private void DrawUnresolved(float3 center, float3 right, float3 up)
            {
                var red = new Color(1f, 0.3f, 0.3f);
                Renderer.Line(center - right * 0.25f, center + up * 0.4f + right * 0.25f, red);
                Renderer.Line(center + right * 0.25f, center + up * 0.4f - right * 0.25f, red);
            }

            private void DrawHistoryTicks(float3 pos, float3 right, float3 up,
                DynamicBuffer<InputHistory> history, int histLen)
            {
                if (histLen == 0) return;

                var cursor = pos + up * 0.8f;
                var n = math.min(history.Length, 8);
                for (var i = 0; i < n; i++)
                {
                    var entry = history[history.Length - 1 - i];
                    var age = (int)(Tick - entry.Tick);
                    var phaseColor = entry.Phase == InputPhase.Down
                        ? new Color(0.3f, 1f, 1f)
                        : entry.Phase == InputPhase.Up
                            ? new Color(1f, 0.3f, 0.3f)
                            : new Color(0.6f, 0.6f, 0.6f);
                    var len = age == 0 ? 0.5f : age < 5 ? 0.35f : 0.2f;
                    Renderer.Line(cursor, cursor + right * len, phaseColor);
                    cursor += up * 0.13f;
                }
            }
        }

        [BurstCompile]
        private partial struct DrawClearJob : IJobEntity
        {
            public Drawer Renderer;

            [ReadOnly] public ComponentLookup<LocalToWorld> Ltws;
            [ReadOnly] public ComponentLookup<ClipActive> Active;

            [ReadOnly] public UnsafeComponentLookup<Targets> Targets;
            [ReadOnly] public UnsafeComponentLookup<EntityLinkSource> Sources;
            [ReadOnly] public UnsafeBufferLookup<EntityLinkEntry> Entries;

            public float Scale;
            public float3 Offset;
            public Color Accent;

            private void Execute(Entity clip, in TrackBinding binding, in BufferClearConfig config)
            {
                var bound = binding.Value;
                if (bound == Entity.Null || !Ltws.HasComponent(bound)) return;

                var isActive = Active.HasComponent(clip) && Active.IsComponentEnabled(clip);
                var accent = isActive ? Accent : new Color(0.5f, 0.5f, 0.5f, 0.5f);

                var pos = Ltws[bound].Position + Offset;
                var up = new float3(0f, 1f, 0f) * Scale;
                var right = new float3(1f, 0f, 0f) * Scale;

                Renderer.Point(pos, 0.12f * Scale, accent);

                var consumer = Entity.Null;
                var resolved = Targets.TryGetComponent(bound, out var targets)
                    && config.Consumer.TryResolve(bound, targets, Sources, Entries, out consumer);

                // Sit in a column to the right of the window panel so the two never overlap on a shared consumer.
                var anchor = pos + right * 2.4f;
                Renderer.Line(pos + up * 2.4f, anchor + up * 2.4f, new Color(accent.r, accent.g, accent.b, 0.3f));

                var head = new FixedString64Bytes();
                head.Append(isActive ? "CLR " : "clr ");
                head.Append(clip.Index);
                if (!resolved) head.Append(" ?");
                Renderer.Text64(anchor + up * 3.4f, head, accent, 12f * Scale);

                if (!resolved)
                {
                    var red = new Color(1f, 0.3f, 0.3f);
                    var c = anchor + up * 3f;
                    Renderer.Line(c - right * 0.25f, c + up * 0.4f + right * 0.25f, red);
                    Renderer.Line(c + right * 0.25f, c + up * 0.4f - right * 0.25f, red);
                    return;
                }

                var mode = new FixedString64Bytes();
                mode.Append(config.ClearAll ? "ALL" : "sel ");
                if (!config.ClearAll)
                {
                    mode.Append(" ");
                    mode.Append(config.ActionMask.CountBits());
                }

                Renderer.Text64(anchor + up * 3f, mode, new Color(1f, 0.7f, 0.7f, 0.9f), 11f * Scale);
            }
        }
    }
}
#endif
