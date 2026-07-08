using System;
using BovineLabs.Core.Collections;
using BovineLabs.Timeline.PlayerInputs.Data;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace BovineLabs.Timeline.PlayerInputs.Editor
{
    [CreateAssetMenu(menuName = "BovineLabs/Player Inputs/Input Recording")]
    public sealed class InputRecordingAsset : ScriptableObject
    {
        public byte Seat;
        public uint FrameCount;
        public ulong[] InitialHeld = new ulong[4];
        public Edge[] Edges = Array.Empty<Edge>();
        public AxisSample[] Axes = Array.Empty<AxisSample>();

        [Serializable]
        public struct Edge
        {
            public uint Frame;
            public byte ActionId;
            public byte Phase;
        }

        [Serializable]
        public struct AxisSample
        {
            public uint Frame;
            public byte ActionId;
            public Vector2 Value;
        }
    }

    public static class InputRecordingTransfer
    {
        public static void Save(EntityManager em, Entity recordingEntity, InputRecordingAsset asset)
        {
            var recording = em.GetComponentData<InputRecording>(recordingEntity);
            asset.Seat = recording.Seat;
            asset.FrameCount = recording.FrameCount;
            asset.InitialHeld = new[]
            {
                recording.InitialHeld.Data1,
                recording.InitialHeld.Data2,
                recording.InitialHeld.Data3,
                recording.InitialHeld.Data4
            };

            var edges = em.GetBuffer<RecordedEdge>(recordingEntity);
            asset.Edges = new InputRecordingAsset.Edge[edges.Length];
            for (var i = 0; i < edges.Length; i++)
            {
                asset.Edges[i] = new InputRecordingAsset.Edge
                {
                    Frame = edges[i].Frame,
                    ActionId = edges[i].ActionId,
                    Phase = (byte)edges[i].Phase
                };
            }

            var samples = em.GetBuffer<RecordedAxisSample>(recordingEntity);
            asset.Axes = new InputRecordingAsset.AxisSample[samples.Length];
            for (var i = 0; i < samples.Length; i++)
            {
                asset.Axes[i] = new InputRecordingAsset.AxisSample
                {
                    Frame = samples[i].Frame,
                    ActionId = samples[i].ActionId,
                    Value = new Vector2(samples[i].Value.x, samples[i].Value.y)
                };
            }
        }

        public static Entity Load(EntityManager em, InputRecordingAsset asset)
        {
            var entity = em.CreateEntity();

            var held = asset.InitialHeld != null && asset.InitialHeld.Length >= 4
                ? new BitArray256(asset.InitialHeld[0], asset.InitialHeld[1], asset.InitialHeld[2], asset.InitialHeld[3])
                : default;

            em.AddComponentData(entity, new InputRecording
            {
                Seat = asset.Seat,
                FrameCount = asset.FrameCount,
                InitialHeld = held
            });

            var edges = em.AddBuffer<RecordedEdge>(entity);
            if (asset.Edges != null)
            {
                for (var i = 0; i < asset.Edges.Length; i++)
                {
                    edges.Add(new RecordedEdge
                    {
                        Frame = asset.Edges[i].Frame,
                        ActionId = asset.Edges[i].ActionId,
                        Phase = (InputPhase)asset.Edges[i].Phase
                    });
                }
            }

            var samples = em.AddBuffer<RecordedAxisSample>(entity);
            if (asset.Axes != null)
            {
                for (var i = 0; i < asset.Axes.Length; i++)
                {
                    samples.Add(new RecordedAxisSample
                    {
                        Frame = asset.Axes[i].Frame,
                        ActionId = asset.Axes[i].ActionId,
                        Value = new float2(asset.Axes[i].Value.x, asset.Axes[i].Value.y)
                    });
                }
            }

            em.AddComponent<InputRecordingActive>(entity);
            em.SetComponentEnabled<InputRecordingActive>(entity, false);

            return entity;
        }
    }
}
