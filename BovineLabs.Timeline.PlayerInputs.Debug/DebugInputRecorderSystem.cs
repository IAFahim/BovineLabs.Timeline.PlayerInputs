#if UNITY_EDITOR || BL_DEBUG
using BovineLabs.Core;
using BovineLabs.Core.ConfigVars;
using BovineLabs.Quill;
using BovineLabs.Timeline.PlayerInputs.Data;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BovineLabs.Timeline.PlayerInputs.Debug
{
    [Configurable]
    public static class InputRecorderDebugConfig
    {
        [ConfigVar("inputrecord.hotkeys", true, "Enable F9 record / F10 replay hotkeys.")]
        public static readonly SharedStatic<bool> Hotkeys = SharedStatic<bool>.GetOrCreate<HotkeysTag>();

        [ConfigVar("inputrecord.seat", 0, "Seat the record/replay hotkeys operate on.")]
        public static readonly SharedStatic<int> Seat = SharedStatic<int>.GetOrCreate<SeatTag>();

        [ConfigVar("inputrecord.loop", false, "Loop the replay when it reaches the end.")]
        public static readonly SharedStatic<bool> Loop = SharedStatic<bool>.GetOrCreate<LoopTag>();

        private struct HotkeysTag
        {
        }

        private struct SeatTag
        {
        }

        private struct LoopTag
        {
        }
    }

    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ServerSimulation |
                       WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(DebugSystemGroup))]
    public partial class DebugInputRecorderSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            var em = this.EntityManager;
            var seat = (byte)math.clamp(InputRecorderDebugConfig.Seat.Data, 0, 255);

            if (InputRecorderDebugConfig.Hotkeys.Data && Keyboard.current != null)
            {
                if (Keyboard.current.f9Key.wasPressedThisFrame)
                {
                    this.ToggleRecord(em, seat);
                }

                if (Keyboard.current.f10Key.wasPressedThisFrame)
                {
                    this.ToggleReplay(em, seat);
                }
            }

            this.DrawOverlay(em);
        }

        private void ToggleRecord(EntityManager em, byte seat)
        {
            var recording = this.FindRecording(seat);

            if (recording == Entity.Null)
            {
                var entity = em.CreateEntity();
                em.AddComponentData(entity, new InputRecording { Seat = seat });
                em.AddBuffer<RecordedEdge>(entity);
                em.AddBuffer<RecordedAxisSample>(entity);
                em.AddComponent<InputRecordingActive>(entity);
                return;
            }

            if (em.IsComponentEnabled<InputRecordingActive>(recording))
            {
                em.SetComponentEnabled<InputRecordingActive>(recording, false);
                return;
            }

            em.GetBuffer<RecordedEdge>(recording).Clear();
            em.GetBuffer<RecordedAxisSample>(recording).Clear();
            em.SetComponentData(recording, new InputRecording { Seat = seat });
            em.SetComponentEnabled<InputRecordingActive>(recording, true);
        }

        private void ToggleReplay(EntityManager em, byte seat)
        {
            var recording = this.FindRecording(seat);
            if (recording == Entity.Null)
            {
                return;
            }

            if (em.HasComponent<InputReplay>(recording))
            {
                var live = em.GetComponentData<InputReplay>(recording);
                live.Frame = em.GetComponentData<InputRecording>(recording).FrameCount;
                live.Loop = false;
                em.SetComponentData(recording, live);
                return;
            }

            if (em.IsComponentEnabled<InputRecordingActive>(recording))
            {
                return;
            }

            if (em.GetComponentData<InputRecording>(recording).FrameCount == 0)
            {
                return;
            }

            em.AddComponentData(recording, new InputReplay { Loop = InputRecorderDebugConfig.Loop.Data });
        }

        private Entity FindRecording(byte seat)
        {
            foreach (var (recording, entity) in SystemAPI
                         .Query<RefRO<InputRecording>>()
                         .WithEntityAccess())
            {
                if (recording.ValueRO.Seat == seat)
                {
                    return entity;
                }
            }

            return Entity.Null;
        }

        private void DrawOverlay(EntityManager em)
        {
            if (!SystemAPI.HasSingleton<DrawSystem.Singleton>())
            {
                return;
            }

            var y = 9.5f;
            foreach (var (recordingRef, entity) in SystemAPI
                         .Query<RefRO<InputRecording>>()
                         .WithEntityAccess())
            {
                var recording = recordingRef.ValueRO;
                var origin = new float3(0f, y, 0f);
                y -= 0.5f;

                var line = new FixedString128Bytes();
                Color color;

                if (em.IsComponentEnabled<InputRecordingActive>(entity))
                {
                    line.Append((FixedString32Bytes)"REC seat");
                    line.Append((int)recording.Seat);
                    line.Append((FixedString32Bytes)"  f=");
                    line.Append((int)recording.FrameCount);
                    line.Append((FixedString32Bytes)"  edges=");
                    line.Append(em.GetBuffer<RecordedEdge>(entity).Length);
                    color = new Color(1f, 0.3f, 0.3f);
                }
                else if (em.HasComponent<InputReplay>(entity))
                {
                    var replay = em.GetComponentData<InputReplay>(entity);
                    line.Append((FixedString32Bytes)"PLAY seat");
                    line.Append((int)recording.Seat);
                    line.Append((FixedString32Bytes)"  f=");
                    line.Append((int)replay.Frame);
                    line.Append('/');
                    line.Append((int)recording.FrameCount);
                    color = new Color(0.3f, 1f, 0.4f);
                }
                else
                {
                    line.Append((FixedString32Bytes)"rec ready seat");
                    line.Append((int)recording.Seat);
                    line.Append((FixedString32Bytes)" (");
                    line.Append((int)recording.FrameCount);
                    line.Append((FixedString32Bytes)"f) [F10]");
                    color = new Color(0.7f, 0.7f, 0.75f);
                }

                GlobalDraw.Text128(origin, line, color, 16f);
            }
        }
    }
}
#endif
