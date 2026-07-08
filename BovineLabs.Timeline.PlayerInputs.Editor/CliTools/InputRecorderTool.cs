using System.Collections.Generic;
using BovineLabs.Timeline.PlayerInputs.Data;
using Newtonsoft.Json.Linq;
using Unity.Collections;
using Unity.Entities;
using UnityCliConnector;
using UnityEditor;
using UnityEngine;

namespace BovineLabs.Timeline.PlayerInputs.Editor.CliTools
{
    [InitializeOnLoad]
    [UnityCliTool(
        Name = "input_recorder",
        Group = "vex",
        Description =
            "Record a seat's provider input each frame and replay it through the whole input pipeline. ops: status, record_start, record_stop, play, play_stop, save, load.")]
    public static class InputRecorderTool
    {
        static InputRecorderTool()
        {
        }

        public static object HandleCommand(JObject @params)
        {
            var p = new ToolParams(@params);
            var op = (p.Get("op", "status") ?? "status").Trim().ToLowerInvariant();

            if (!TryWorld(out var em, out var worldError))
            {
                return worldError;
            }

            var seat = (byte)(p.GetInt("seat", 0) ?? 0);

            switch (op)
            {
                case "status": return Status(em);
                case "record_start": return RecordStart(em, seat);
                case "record_stop": return RecordStop(em, seat);
                case "play": return Play(em, seat, p.GetBool("loop", false));
                case "play_stop": return PlayStop(em, seat);
                case "save": return Save(em, seat, p.Get("path"));
                case "load": return Load(em, p.Get("path"));
                default:
                    return new ErrorResponse(
                        $"Unknown op '{op}'. Use: status, record_start, record_stop, play, play_stop, save, load.");
            }
        }

        private static object Status(EntityManager em)
        {
            using var query = em.CreateEntityQuery(ComponentType.ReadOnly<InputRecording>());
            using var entities = query.ToEntityArray(Allocator.Temp);

            var recordings = new List<object>();
            foreach (var entity in entities)
            {
                var recording = em.GetComponentData<InputRecording>(entity);
                var replaying = em.HasComponent<InputReplay>(entity);
                object replay = null;
                if (replaying)
                {
                    var r = em.GetComponentData<InputReplay>(entity);
                    replay = new { frame = (int)r.Frame, loop = r.Loop };
                }

                recordings.Add(new
                {
                    seat = (int)recording.Seat,
                    frames = (int)recording.FrameCount,
                    recording = em.IsComponentEnabled<InputRecordingActive>(entity),
                    edges = em.GetBuffer<RecordedEdge>(entity).Length,
                    axes = em.GetBuffer<RecordedAxisSample>(entity).Length,
                    replay
                });
            }

            return new SuccessResponse($"{recordings.Count} recording(s).",
                new { Application.isPlaying, recordings });
        }

        private static object RecordStart(EntityManager em, byte seat)
        {
            var recording = FindRecording(em, seat);
            if (recording == Entity.Null)
            {
                recording = em.CreateEntity();
                em.AddComponentData(recording, new InputRecording { Seat = seat });
                em.AddBuffer<RecordedEdge>(recording);
                em.AddBuffer<RecordedAxisSample>(recording);
                em.AddComponent<InputRecordingActive>(recording);
                return new SuccessResponse($"Recording started for seat {seat}.");
            }

            em.GetBuffer<RecordedEdge>(recording).Clear();
            em.GetBuffer<RecordedAxisSample>(recording).Clear();
            em.SetComponentData(recording, new InputRecording { Seat = seat });
            em.SetComponentEnabled<InputRecordingActive>(recording, true);
            return new SuccessResponse($"Recording restarted for seat {seat}.");
        }

        private static object RecordStop(EntityManager em, byte seat)
        {
            var recording = FindRecording(em, seat);
            if (recording == Entity.Null)
            {
                return new ErrorResponse($"No recording for seat {seat}.");
            }

            em.SetComponentEnabled<InputRecordingActive>(recording, false);
            var frames = (int)em.GetComponentData<InputRecording>(recording).FrameCount;
            return new SuccessResponse($"Recording stopped for seat {seat} ({frames} frames).");
        }

        private static object Play(EntityManager em, byte seat, bool loop)
        {
            var recording = FindRecording(em, seat);
            if (recording == Entity.Null)
            {
                return new ErrorResponse($"No recording for seat {seat}.");
            }

            if (em.HasComponent<InputReplay>(recording))
            {
                return new ErrorResponse($"Seat {seat} is already replaying. Use play_stop first.");
            }

            if (em.IsComponentEnabled<InputRecordingActive>(recording))
            {
                return new ErrorResponse($"Stop recording seat {seat} before replaying.");
            }

            var data = em.GetComponentData<InputRecording>(recording);
            if (data.FrameCount == 0)
            {
                return new ErrorResponse($"Recording for seat {seat} has no frames.");
            }

            em.AddComponentData(recording, new InputReplay { Loop = loop });
            return new SuccessResponse($"Replaying seat {seat} ({data.FrameCount} frames, loop={loop}).");
        }

        private static object PlayStop(EntityManager em, byte seat)
        {
            var recording = FindRecording(em, seat);
            if (recording == Entity.Null || !em.HasComponent<InputReplay>(recording))
            {
                return new ErrorResponse($"No live replay for seat {seat}.");
            }

            var replay = em.GetComponentData<InputReplay>(recording);
            replay.Frame = em.GetComponentData<InputRecording>(recording).FrameCount;
            replay.Loop = false;
            em.SetComponentData(recording, replay);
            return new SuccessResponse($"Replay for seat {seat} will stop on the next update.");
        }

        private static object Save(EntityManager em, byte seat, string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return new ErrorResponse("save needs a path, e.g. Assets/Recordings/Seat0.asset.");
            }

            var recording = FindRecording(em, seat);
            if (recording == Entity.Null)
            {
                return new ErrorResponse($"No recording for seat {seat}.");
            }

            var asset = AssetDatabase.LoadAssetAtPath<InputRecordingAsset>(path);
            var created = asset == null;
            if (created)
            {
                asset = ScriptableObject.CreateInstance<InputRecordingAsset>();
            }

            InputRecordingTransfer.Save(em, recording, asset);

            if (created)
            {
                AssetDatabase.CreateAsset(asset, path);
            }
            else
            {
                EditorUtility.SetDirty(asset);
            }

            AssetDatabase.SaveAssets();
            return new SuccessResponse($"Saved seat {seat} recording ({asset.FrameCount} frames) to {path}.");
        }

        private static object Load(EntityManager em, string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return new ErrorResponse("load needs a path to an InputRecordingAsset.");
            }

            var asset = AssetDatabase.LoadAssetAtPath<InputRecordingAsset>(path);
            if (asset == null)
            {
                return new ErrorResponse($"No InputRecordingAsset at {path}.");
            }

            var existing = FindRecording(em, asset.Seat);
            if (existing != Entity.Null)
            {
                em.DestroyEntity(existing);
            }

            InputRecordingTransfer.Load(em, asset);
            return new SuccessResponse($"Loaded {path} into seat {asset.Seat} ({asset.FrameCount} frames).");
        }

        private static Entity FindRecording(EntityManager em, byte seat)
        {
            using var query = em.CreateEntityQuery(ComponentType.ReadOnly<InputRecording>());
            using var entities = query.ToEntityArray(Allocator.Temp);
            foreach (var entity in entities)
            {
                if (em.GetComponentData<InputRecording>(entity).Seat == seat)
                {
                    return entity;
                }
            }

            return Entity.Null;
        }

        private static bool TryWorld(out EntityManager em, out object error)
        {
            error = null;
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                em = default;
                error = new ErrorResponse("No default world available (enter play mode first).");
                return false;
            }

            em = world.EntityManager;
            return true;
        }

        public class Parameters
        {
            [ToolParameter("Operation: status, record_start, record_stop, play, play_stop, save, load (default status).")]
            public string Op { get; set; }

            [ToolParameter("Seat (player id) to record/replay/save (default 0).")]
            public int Seat { get; set; }

            [ToolParameter("play only: loop the replay when it reaches the end (default false).")]
            public bool Loop { get; set; }

            [ToolParameter("save/load only: asset path, e.g. Assets/Recordings/Seat0.asset.")]
            public string Path { get; set; }
        }
    }
}
