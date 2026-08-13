#if UNITY_EDITOR || BL_DEBUG
using BovineLabs.Core;
using BovineLabs.Core.ConfigVars;
using BovineLabs.Quill;
using BovineLabs.Timeline.PlayerInputs.Data;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace BovineLabs.Timeline.PlayerInputs.Debug
{
    [Configurable]
    public static class PlayerInputDebugConfig
    {
        // The overlay draws at hardcoded world coordinates (origin-relative), which is fine for the showcase but useless
        // inside a real level where (0, 8, 0) may be underground or off-camera. This shifts the whole overlay so it can
        // be parked wherever the developer is looking. Mirrors inputbuffer.offset.
        [ConfigVar("playerinput.offset", 0f, 0f, 0f, 0f, "World anchor offset added to the player-input registry overlay.")]
        public static readonly SharedStatic<Vector4> Offset = SharedStatic<Vector4>.GetOrCreate<OffsetTag>();

        private struct OffsetTag
        {
        }
    }

    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ServerSimulation |
                       WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(DebugSystemGroup))]
    public partial struct DebugPlayerInputSystem : ISystem
    {
        private const int SlotCount = 256;

        private ComponentLookup<InputState> states;
        private BufferLookup<InputAxis> axes;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<DrawSystem.Singleton>();
            state.RequireForUpdate<InputRegistry>();
            states = state.GetComponentLookup<InputState>(true);
            axes = state.GetBufferLookup<InputAxis>(true);
        }

        public void OnUpdate(ref SystemState state)
        {
            states.Update(ref state);
            axes.Update(ref state);

            var registry = SystemAPI.GetSingleton<InputRegistry>();
            var renderer = SystemAPI.GetSingleton<DrawSystem.Singleton>().CreateDrawer();

            var consumerCounts = CollectionHelper.CreateNativeArray<int>(
                SlotCount, state.WorldUpdateAllocator);
            var overriddenCounts = CollectionHelper.CreateNativeArray<int>(
                SlotCount, state.WorldUpdateAllocator);
            var replayProgress = CollectionHelper.CreateNativeArray<int2>(
                SlotCount, state.WorldUpdateAllocator);

            state.Dependency = new CountConsumersJob { Counts = consumerCounts }.Schedule(state.Dependency);
            state.Dependency = new CountOverriddenJob { Counts = overriddenCounts }.Schedule(state.Dependency);
            state.Dependency = new CollectReplaysJob { Progress = replayProgress }.Schedule(state.Dependency);

            state.Dependency = new RenderJob
            {
                Renderer = renderer,
                Offset = ((float4)PlayerInputDebugConfig.Offset.Data).xyz,
                Slots = SystemAPI.GetSingletonBuffer<ProviderSlot>(true),
                Version = registry.Version,
                States = states,
                Axes = axes,
                ConsumerCounts = consumerCounts,
                OverriddenCounts = overriddenCounts,
                ReplayProgress = replayProgress,
                Joined = SystemAPI.GetSingletonBuffer<PlayerJoined>(true),
                Left = SystemAPI.GetSingletonBuffer<PlayerLeft>(true)
            }.Schedule(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(ConsumerTag))]
        private partial struct CountConsumersJob : IJobEntity
        {
            public NativeArray<int> Counts;

            private void Execute(in PlayerId id)
            {
                Counts[id.Value]++;
            }
        }

        [BurstCompile]
        [WithAll(typeof(ConsumerTag), typeof(Controllable))]
        private partial struct CountOverriddenJob : IJobEntity
        {
            public NativeArray<int> Counts;

            private void Execute(in PlayerId id, EnabledRefRO<PlayerOverride> driving)
            {
                if (driving.ValueRO) Counts[id.Value]++;
            }
        }

        /// <summary>
        /// Which seats a recording is currently driving, and how far through it is.
        /// </summary>
        /// <remarks>
        /// Keyed by the seat the replay DRIVES, which is not the seat it was captured on whenever RetargetSeat is set
        /// — that is the whole point of retargeting, and showing the recorded seat would put the bar on the wrong
        /// column exactly when a replay has been added as a second player.
        /// </remarks>
        [BurstCompile]
        private partial struct CollectReplaysJob : IJobEntity
        {
            public NativeArray<int2> Progress;

            private void Execute(in InputRecording recording, in InputReplay replay)
            {
                var seat = replay.RetargetSeat ? replay.Seat : recording.Seat;
                Progress[seat] = new int2((int)replay.Frame, (int)recording.FrameCount);
            }
        }

        private struct RenderJob : IJob
        {
            public Drawer Renderer;

            public float3 Offset;

            [ReadOnly] public DynamicBuffer<ProviderSlot> Slots;

            public uint Version;
            [ReadOnly] public ComponentLookup<InputState> States;
            [ReadOnly] public BufferLookup<InputAxis> Axes;
            [ReadOnly] public NativeArray<int> ConsumerCounts;
            [ReadOnly] public NativeArray<int> OverriddenCounts;
            [ReadOnly] public NativeArray<int2> ReplayProgress;
            [ReadOnly] public DynamicBuffer<PlayerJoined> Joined;
            [ReadOnly] public DynamicBuffer<PlayerLeft> Left;

            public void Execute()
            {
                RenderSummary();
                RenderEvents();

                var column = 0;
                for (var p = 0; p < SlotCount; p++)
                {
                    var slot = Slots[p];
                    if (slot.Human == Entity.Null && slot.Synthetic == Entity.Null) continue;

                    RenderPlayer((byte)p, slot, column);
                    column++;
                }
            }

            private void RenderSummary()
            {
                var occupied = 0;
                for (var p = 0; p < SlotCount; p++)
                    if (Slots[p].Human != Entity.Null || Slots[p].Synthetic != Entity.Null)
                        occupied++;

                var title = new FixedString128Bytes();
                title.Append("INPUT REGISTRY v");
                title.Append(Version);
                title.Append(" active ");
                title.Append(occupied);
                title.Append('/');
                title.Append(SlotCount);

                Renderer.Text128(Offset + new float3(0f, 8f, 0f), title, Color.white, 18f);
            }

            private void RenderEvents()
            {
                var origin = Offset + new float3(-9f, 8f, 0f);
                Renderer.Text32(origin, "EVENTS", new Color(1f, 1f, 1f, 0.8f), 12f);

                var cursor = origin + new float3(0f, -0.45f, 0f);

                for (var i = 0; i < Joined.Length; i++)
                {
                    var line = new FixedString64Bytes();
                    line.Append("JOIN  P");
                    line.Append(Joined[i].PlayerId);
                    line.Append(" -> #");
                    line.Append(Joined[i].Provider.Index);
                    Renderer.Text64(cursor, line, new Color(0.2f, 1f, 0.4f), 11f);
                    cursor.y -= 0.3f;
                }

                for (var i = 0; i < Left.Length; i++)
                {
                    var line = new FixedString64Bytes();
                    line.Append("LEFT  P");
                    line.Append(Left[i].PlayerId);
                    Renderer.Text64(cursor, line, new Color(1f, 0.3f, 0.3f), 11f);
                    cursor.y -= 0.3f;
                }
            }

            private void RenderPlayer(byte id, ProviderSlot slot, int column)
            {
                // Show the human provider when seated; fall back to the synthetic feed (and label it) otherwise.
                var provider = slot.Human != Entity.Null ? slot.Human : slot.Synthetic;

                var origin = Offset + new float3(column * 6f, 6f, 0f);

                // Show BOTH slots per seat: "P0 hum #12 / syn #34" (a dash where a slot is empty).
                var header = new FixedString64Bytes();
                header.Append("P");
                header.Append(id);
                header.Append(" hum ");
                if (slot.Human != Entity.Null)
                {
                    header.Append('#');
                    header.Append(slot.Human.Index);
                }
                else
                {
                    header.Append('-');
                }

                header.Append(" / syn ");
                if (slot.Synthetic != Entity.Null)
                {
                    header.Append('#');
                    header.Append(slot.Synthetic.Index);
                }
                else
                {
                    header.Append('-');
                }

                Renderer.Text64(origin, header, new Color(0.2f, 1f, 0.4f), 14f);

                var stats = new FixedString64Bytes();
                stats.Append("consumers ");
                stats.Append(ConsumerCounts[id]);
                stats.Append("   driven ");
                stats.Append(OverriddenCounts[id]);
                Renderer.Text64(origin + new float3(0f, -0.4f, 0f), stats, new Color(1f, 0.9f, 0.4f), 11f);

                // A replayed seat looks identical to a played one below this point — same provider, same held keys —
                // so the only place the difference can be shown is here, above the state it is producing.
                var cursor = origin + new float3(0f, -0.7f, 0f);
                if (ReplayProgress[id].y > 0)
                {
                    RenderReplay(cursor, ReplayProgress[id]);
                    cursor.y -= 0.5f;
                }

                Renderer.Line(cursor + new float3(-0.5f, 0f, 0f), cursor + new float3(4.5f, 0f, 0f),
                    new Color(1f, 1f, 1f, 0.3f));

                if (States.HasComponent(provider))
                    RenderState(cursor + new float3(0f, -0.4f, 0f), States[provider]);

                if (Axes.HasBuffer(provider))
                    RenderAxes(cursor + new float3(3f, -0.4f, 0f), Axes[provider]);
            }

            private void RenderReplay(float3 origin, int2 progress)
            {
                var tint = new Color(0.3f, 1f, 0.4f);

                var line = new FixedString64Bytes();
                line.Append("REPLAY ");
                line.Append(progress.x);
                line.Append('/');
                line.Append(progress.y);
                Renderer.Text64(origin, line, tint, 11f);

                var y = origin.y - 0.2f;
                var left = origin.x;
                var right = origin.x + 4f;
                var t = math.saturate(progress.x / (float)progress.y);

                Renderer.Line(new float3(left, y, origin.z), new float3(right, y, origin.z),
                    new Color(1f, 1f, 1f, 0.2f));
                Renderer.Line(new float3(left, y, origin.z), new float3(math.lerp(left, right, t), y, origin.z), tint);
            }

            private void RenderState(float3 origin, InputState state)
            {
                Renderer.Text32(origin, "STATE", new Color(0f, 1f, 1f, 0.9f), 11f);

                var cursor = origin + new float3(0f, -0.35f, 0f);
                var shown = 0;
                for (var i = 0; i < SlotCount && shown < 20; i++)
                {
                    FixedString32Bytes phase;
                    Color tint;

                    if (state.Down[i])
                    {
                        phase = "DOWN";
                        tint = new Color(0f, 1f, 1f);
                    }
                    else if (state.Held[i])
                    {
                        phase = "HELD";
                        tint = new Color(0f, 1f, 0.5f, 0.85f);
                    }
                    else if (state.Up[i])
                    {
                        phase = "UP";
                        tint = new Color(1f, 0.3f, 0.3f);
                    }
                    else
                    {
                        continue;
                    }

                    var line = new FixedString64Bytes();
                    line.Append('[');
                    line.Append(phase);
                    line.Append("] ");
                    line.Append(MultiInputSettings.KeyToName((byte)i));
                    Renderer.Text64(cursor, line, tint, 10f);
                    cursor.y -= 0.25f;
                    shown++;
                }

                if (shown == 0)
                    Renderer.Text32(cursor, "idle", new Color(1f, 1f, 1f, 0.3f), 10f);
            }

            private void RenderAxes(float3 origin, DynamicBuffer<InputAxis> buffer)
            {
                Renderer.Text32(origin, "AXES", new Color(0.4f, 0.8f, 1f, 0.9f), 11f);

                var cursor = origin + new float3(0f, -1f, 0f);
                for (var i = 0; i < buffer.Length; i++)
                {
                    var axis = buffer[i];
                    var vec = new float3(axis.Value.x, axis.Value.y, 0f) * 0.4f;

                    Renderer.Circle(cursor, new float3(0f, 0f, 1f) * 0.4f, new Color(1f, 1f, 1f, 0.1f));
                    Renderer.Line(cursor, cursor + vec, new Color(0.4f, 0.8f, 1f));
                    Renderer.Point(cursor + vec, 0.08f, new Color(0.4f, 0.8f, 1f));

                    var label = new FixedString64Bytes();
                    label.Append(MultiInputSettings.KeyToName(axis.ActionId));
                    Renderer.Text64(cursor + new float3(0f, 0.55f, 0f), label, new Color(1f, 1f, 1f, 0.7f), 10f);

                    cursor.y -= 1.4f;
                }
            }
        }
    }
}
#endif