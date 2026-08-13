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

        /// <summary>
        /// False (the default) replays onto the seat the recording was captured from, taking that seat over from its
        /// human. True replays onto <see cref="Seat"/> instead, which <em>adds</em> a player rather than possessing
        /// one — a recording becomes a second local player, or one of many.
        /// </summary>
        /// <remarks>
        /// Deliberately a flag rather than a sentinel value: <c>default(InputReplay)</c> has to keep meaning "replay
        /// where it was recorded", and seat 0 is a real seat, so any numeric sentinel would silently retarget every
        /// existing caller to seat 0.
        /// <para>
        /// Why a free seat needs no takeover: the registry indexes providers by seat and keeps Human and Synthetic
        /// slots apart, flagging a duplicate only when two providers of the <em>same kind</em> land on one seat. A
        /// replay provider carries no <c>SyntheticProviderTag</c>, so it registers as Human — which is exactly why
        /// replaying onto the recorded seat must first disable that seat's bridge. On a seat with no human there is
        /// nobody to displace, and both providers run side by side.
        /// </para>
        /// </remarks>
        public bool RetargetSeat;

        /// <summary>The seat to drive when <see cref="RetargetSeat"/> is set. Ignored otherwise.</summary>
        public byte Seat;

        /// <summary>Replay this recording onto <paramref name="seat"/> instead of the one it was captured from.</summary>
        public static InputReplay OnSeat(byte seat, bool loop = false)
        {
            return new InputReplay { RetargetSeat = true, Seat = seat, Loop = loop };
        }
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

        /// <summary>
        /// Applies this frame's axis changes, leaving every other axis at the value it already had.
        /// </summary>
        /// <remarks>
        /// Samples are CHANGES, not a per-frame stream — <see cref="InputRecordSystem"/> writes one only when an
        /// action's value differs from its last. So this must NOT clear: clearing would drop a stick that is being
        /// held to zero on every frame between the two samples that describe the hold, which is silent and looks
        /// like the recording is broken rather than like the format was misread.
        /// </remarks>
        public static void CollectAxes(DynamicBuffer<RecordedAxisSample> samples, uint frame, ref int cursor,
            DynamicBuffer<InputAxis> outAxes)
        {
            while (cursor < samples.Length && samples[cursor].Frame == frame)
            {
                var sample = samples[cursor];
                var axis = new InputAxis { ActionId = sample.ActionId, Value = sample.Value };
                var replaced = false;

                for (var i = 0; i < outAxes.Length; i++)
                {
                    if (outAxes[i].ActionId != sample.ActionId)
                    {
                        continue;
                    }

                    outAxes[i] = axis;
                    replaced = true;
                    break;
                }

                if (!replaced)
                {
                    outAxes.Add(axis);
                }

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
