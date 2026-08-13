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
        private ComponentLookup<InputHistoryLimit> _limits;
        private ComponentLookup<LocalToWorld> _ltws;

        private ComponentLookup<Targets> _targets;
        private ComponentLookup<EntityLinkSource> _sources;
        private BufferLookup<EntityLinkEntry> _entries;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<DrawSystem.Singleton>();
            state.RequireForUpdate<BufferWindowConfig>();

            _bindings = state.GetComponentLookup<TrackBinding>(true);
            _active = state.GetComponentLookup<ClipActive>(true);
            _masks = state.GetComponentLookup<ActiveBufferMask>(true);
            _histories = state.GetBufferLookup<InputHistory>(true);
            _limits = state.GetComponentLookup<InputHistoryLimit>(true);
            _ltws = state.GetComponentLookup<LocalToWorld>(true);

            _targets = state.GetComponentLookup<Targets>(true);
            _sources = state.GetComponentLookup<EntityLinkSource>(true);
            _entries = state.GetBufferLookup<EntityLinkEntry>(true);
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
            _limits.Update(ref state);
            _ltws.Update(ref state);
            _targets.Update(ref state);
            _sources.Update(ref state);
            _entries.Update(ref state);

            var tick = SystemAPI.HasSingleton<SimulationTick>()
                ? SystemAPI.GetSingleton<SimulationTick>().Value
                : 0u;
            var millis = (uint)(SystemAPI.Time.ElapsedTime * 1000.0);

            var scale = InputBufferDebugConfig.Scale.Data;
            var offset = ((float4)InputBufferDebugConfig.Offset.Data).xyz;

            state.Dependency = new DrawWindowJob
            {
                Renderer = renderer,
                Ltws = _ltws,
                Active = _active,
                Masks = _masks,
                Histories = _histories,
                Limits = _limits,
                Targets = _targets,
                Sources = _sources,
                Entries = _entries,
                Tick = tick,
                Millis = millis,
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
            [ReadOnly] public ComponentLookup<InputHistoryLimit> Limits;

            [ReadOnly] public ComponentLookup<Targets> Targets;
            [ReadOnly] public ComponentLookup<EntityLinkSource> Sources;
            [ReadOnly] public BufferLookup<EntityLinkEntry> Entries;

            public uint Tick;
            public uint Millis;
            public float Scale;
            public float3 Offset;
            public Color Accent;

            // Layout (all ABOVE the character, one tidy column so it never fights the mesh/other drawers):
            //   up*5.0  header   "BUF OPEN x3" / "BUF ----"   (state = the CONSUMER's live mask, not per-clip)
            //   up*4.6  "hist 16 / 64"                        (recorded count / limit)
            //   up*4.42 divider
            //   up*4.1  -> down   readable history log, NEWEST FIRST, 6 rows, colour by phase
            // The per-clip index is intentionally GONE: many window clips share one consumer, so stamping
            // clip.Index just drew "win 2721" over "win 2724". Every clip now paints the SAME consumer panel
            // (identical overdraw = invisible), and the state comes from the consumer's ActiveBufferMask.
            private const float RowHead = 5.0f;
            private const float RowHist = 4.6f;
            private const float LogTop = 4.1f;
            private const float RowStep = 0.34f;
            private const int MaxRows = 6;

            private void Execute(Entity clip, in TrackBinding binding, in BufferWindowConfig config)
            {
                var bound = binding.Value;
                if (bound == Entity.Null || !Ltws.HasComponent(bound)) return;

                var pos = Ltws[bound].Position + Offset;
                var up = new float3(0f, 1f, 0f) * Scale;
                var right = new float3(1f, 0f, 0f) * Scale;

                var consumer = Entity.Null;
                var resolved = Targets.TryGetComponent(bound, out var targets)
                    && config.Consumer.TryResolve(bound, targets, Sources, Entries, out consumer);

                Renderer.Point(pos, 0.12f * Scale, resolved ? Accent : new Color(1f, 0.3f, 0.3f));

                if (!resolved)
                {
                    Renderer.Line(pos, pos + up * RowHead, new Color(1f, 0.3f, 0.3f, 0.4f));
                    var miss = new FixedString64Bytes();
                    miss.Append((FixedString32Bytes)"BUFFER ? link miss");
                    Renderer.Text64(pos + up * RowHead, miss, new Color(1f, 0.35f, 0.35f), 12f * Scale);
                    return;
                }

                var open = Masks.TryGetComponent(consumer, out var mask) && !mask.Value.AllFalse;
                var bits = open ? mask.Value.CountBits() : 0;
                var histLen = Histories.TryGetBuffer(consumer, out var history) ? history.Length : 0;
                var limit = Limits.TryGetComponent(consumer, out var lim) ? lim.Value : (ushort)0;

                var panel = open ? new Color(0.35f, 1f, 0.6f) : new Color(0.72f, 0.72f, 0.78f);

                // stalk from the character up to the bottom of the log
                Renderer.Line(pos, pos + up * (LogTop - (MaxRows - 1) * RowStep),
                    new Color(panel.r, panel.g, panel.b, 0.3f));

                var head = new FixedString64Bytes();
                head.Append((FixedString32Bytes)"BUF ");
                if (open)
                {
                    head.Append((FixedString32Bytes)"OPEN x");
                    head.Append(bits);
                }
                else
                {
                    head.Append((FixedString32Bytes)"----");
                }

                Renderer.Text64(pos + up * RowHead, head, panel, 12f * Scale);

                // Ring pressure: at the cap the oldest entries are evicted every record, which silently starves
                // multi-step combos. Red = full (evicting), amber = >=75% (about to evict), else white.
                var atCap = limit > 0 && histLen >= limit;
                var nearCap = limit > 0 && histLen >= (limit * 3) / 4;
                var histColor = atCap ? new Color(1f, 0.3f, 0.3f)
                    : nearCap ? new Color(1f, 0.8f, 0.3f)
                    : new Color(1f, 1f, 1f, 0.85f);

                var hl = new FixedString64Bytes();
                hl.Append((FixedString32Bytes)"hist ");
                hl.Append(histLen);
                if (limit > 0)
                {
                    hl.Append((FixedString32Bytes)" / ");
                    hl.Append((int)limit);
                }

                if (atCap) hl.Append((FixedString32Bytes)" FULL");

                Renderer.Text64(pos + up * RowHist, hl, histColor, 11f * Scale);

                if (atCap)
                {
                    var advise = new FixedString64Bytes();
                    advise.Append((FixedString32Bytes)"evicting oldest - restrict window");
                    Renderer.Text64(pos + up * (RowHist - 0.28f), advise, new Color(1f, 0.45f, 0.45f), 9f * Scale);
                }

                Renderer.Line(pos + up * 4.42f - right * 0.05f, pos + up * 4.42f + right * 1.7f,
                    new Color(1f, 1f, 1f, 0.22f));

                DrawHistoryLog(pos, up, history, histLen);
            }

            // Newest press at the top, oldest at the bottom. Each row: phase glyph + action id + age in millis.
            //   v = Down (press)   ^ = Up (release)   = = Held      yellow row = happened THIS frame.
            // ActionId is the MultiInputSettings slot index (A0 = first configured action, etc.).
            private void DrawHistoryLog(float3 pos, float3 up, DynamicBuffer<InputHistory> history, int histLen)
            {
                if (histLen == 0)
                {
                    var empty = new FixedString64Bytes();
                    empty.Append((FixedString32Bytes)"(empty)");
                    Renderer.Text64(pos + up * LogTop, empty, new Color(0.6f, 0.6f, 0.65f), 10f * Scale);
                    return;
                }

                var n = math.min(histLen, MaxRows);
                for (var i = 0; i < n; i++)
                {
                    var entry = history[histLen - 1 - i]; // newest first
                    var tickAge = (int)(Tick - entry.Tick);
                    var age = (int)(Millis - entry.Millis); // wall-clock age drives the sequence gap windows

                    var row = new FixedString64Bytes();
                    Color color;
                    switch (entry.Phase)
                    {
                        case InputPhase.Down:
                            row.Append((FixedString32Bytes)"v A");
                            color = new Color(0.35f, 1f, 1f);
                            break;
                        case InputPhase.Up:
                            row.Append((FixedString32Bytes)"^ A");
                            color = new Color(1f, 0.5f, 0.4f);
                            break;
                        default:
                            row.Append((FixedString32Bytes)"= A");
                            color = new Color(0.66f, 0.66f, 0.72f);
                            break;
                    }

                    row.Append((int)entry.ActionId);
                    row.Append((FixedString32Bytes)"  ");
                    row.Append(age);
                    row.Append((FixedString32Bytes)"ms");
                    if (tickAge == 0) color = new Color(1f, 1f, 0.4f); // fired this frame

                    Renderer.Text64(pos + up * (LogTop - i * RowStep), row, color, 10f * Scale);
                }

                if (histLen > n)
                {
                    var more = new FixedString64Bytes();
                    more.Append((FixedString32Bytes)"+");
                    more.Append(histLen - n);
                    more.Append((FixedString32Bytes)" older");
                    Renderer.Text64(pos + up * (LogTop - n * RowStep), more,
                        new Color(0.6f, 0.6f, 0.65f), 9f * Scale);
                }
            }
        }

        [BurstCompile]
        private partial struct DrawClearJob : IJobEntity
        {
            public Drawer Renderer;

            [ReadOnly] public ComponentLookup<LocalToWorld> Ltws;
            [ReadOnly] public ComponentLookup<ClipActive> Active;

            [ReadOnly] public ComponentLookup<Targets> Targets;
            [ReadOnly] public ComponentLookup<EntityLinkSource> Sources;
            [ReadOnly] public BufferLookup<EntityLinkEntry> Entries;

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
                head.Append(isActive ? (FixedString32Bytes)"CLR " : (FixedString32Bytes)"clr ");
                head.Append(clip.Index);
                if (!resolved) head.Append((FixedString32Bytes)" ?");
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
                mode.Append(config.ClearAll ? (FixedString32Bytes)"ALL" : (FixedString32Bytes)"sel ");
                if (!config.ClearAll)
                {
                    mode.Append((FixedString32Bytes)" ");
                    mode.Append(config.ActionMask.CountBits());
                }

                Renderer.Text64(anchor + up * 3f, mode, new Color(1f, 0.7f, 0.7f, 0.9f), 11f * Scale);
            }
        }
    }
}
#endif
