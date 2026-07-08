using BovineLabs.Core.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace BovineLabs.Timeline.PlayerInputs.Data
{
    public struct InputRecording : IComponentData
    {
        public byte Seat;
        public uint FrameCount;
        public BitArray256 InitialHeld;
    }

    public struct InputRecordingActive : IComponentData, IEnableableComponent
    {
    }

    [InternalBufferCapacity(0)]
    public struct RecordedEdge : IBufferElementData
    {
        public uint Frame;
        public byte ActionId;
        public InputPhase Phase;
    }

    [InternalBufferCapacity(0)]
    public struct RecordedAxisSample : IBufferElementData
    {
        public uint Frame;
        public byte ActionId;
        public float2 Value;
    }

    public struct InputReplay : IComponentData
    {
        public uint Frame;
        public Entity Provider;
        public int EdgeCursor;
        public int AxisCursor;
        public BitArray256 Held;
        public bool Loop;
    }

    public static class InputReplayMath
    {
        public static void StepFrame(DynamicBuffer<RecordedEdge> edges, uint frame, ref int cursor,
            ref BitArray256 held, out BitArray256 down, out BitArray256 up)
        {
            down = default;
            up = default;

            while (cursor < edges.Length && edges[cursor].Frame == frame)
            {
                var edge = edges[cursor];
                if (edge.Phase == InputPhase.Up)
                {
                    up[edge.ActionId] = true;
                    held[edge.ActionId] = false;
                }
                else
                {
                    down[edge.ActionId] = true;
                    held[edge.ActionId] = true;
                }

                cursor++;
            }
        }

        public static void CollectAxes(DynamicBuffer<RecordedAxisSample> samples, uint frame, ref int cursor,
            DynamicBuffer<InputAxis> outAxes)
        {
            outAxes.Clear();

            while (cursor < samples.Length && samples[cursor].Frame == frame)
            {
                var sample = samples[cursor];
                outAxes.Add(new InputAxis { ActionId = sample.ActionId, Value = sample.Value });
                cursor++;
            }
        }

        public static void Reset(ref InputReplay replay, in BitArray256 initialHeld)
        {
            replay.Frame = 0;
            replay.EdgeCursor = 0;
            replay.AxisCursor = 0;
            replay.Held = initialHeld;
        }
    }
}
