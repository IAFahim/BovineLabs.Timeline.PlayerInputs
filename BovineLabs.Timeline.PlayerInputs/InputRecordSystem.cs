using System.Collections.Generic;
using BovineLabs.Core.Collections;
using BovineLabs.Timeline.PlayerInputs.Data;
using Unity.Entities;
using Unity.Mathematics;

namespace BovineLabs.Timeline.PlayerInputs
{
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [UpdateAfter(typeof(ProviderSyncSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation)]
    public partial class InputRecordSystem : SystemBase
    {
        /// <summary>How far back to look for an action's previous sample before assuming it changed.</summary>
        private const int Lookback = 64;

        private readonly Dictionary<byte, Entity> providerBySeat = new();

        protected override void OnUpdate()
        {
            this.providerBySeat.Clear();

            foreach (var (id, entity) in SystemAPI
                         .Query<RefRO<PlayerId>>()
                         .WithAll<ProviderTag, InputState>()
                         .WithNone<ProviderRetiring>()
                         .WithEntityAccess())
            {
                this.providerBySeat.TryAdd(id.ValueRO.Value, entity);
            }

            foreach (var (recording, edges, samples) in SystemAPI
                         .Query<RefRW<InputRecording>, DynamicBuffer<RecordedEdge>, DynamicBuffer<RecordedAxisSample>>()
                         .WithAll<InputRecordingActive>())
            {
                if (!this.providerBySeat.TryGetValue(recording.ValueRO.Seat, out var provider))
                {
                    continue;
                }

                var state = SystemAPI.GetComponent<InputState>(provider);
                var axes = SystemAPI.GetBuffer<InputAxis>(provider);
                var frame = recording.ValueRO.FrameCount;

                if (frame == 0)
                {
                    recording.ValueRW.InitialHeld = state.Held;
                }

                AppendEdges(state.Down, InputPhase.Down, frame, edges);
                AppendEdges(state.Up, InputPhase.Up, frame, edges);

                for (var i = 0; i < axes.Length; i++)
                {
                    if (Unchanged(samples, axes[i].ActionId, axes[i].Value))
                    {
                        continue;
                    }

                    samples.Add(new RecordedAxisSample { Frame = frame, ActionId = axes[i].ActionId, Value = axes[i].Value });
                }

                recording.ValueRW.FrameCount = frame + 1;
            }
        }

        /// <summary>
        /// Whether this action already ends on this exact value, so recording it again would say nothing.
        /// </summary>
        /// <remarks>
        /// Axis samples are CHANGES, not a per-frame stream, and replay holds the last value until the next one. A
        /// stick held at full tilt for five seconds is one sample, not three hundred.
        /// <para>
        /// Measured before this existed: one axis held motionless for 1760 frames wrote 1760 identical samples and a
        /// 155 KB asset — 88.6 bytes each, because the asset is YAML. Three live axes over a five-minute session came
        /// to 4.8 MB, per session, in a folder that keeps ten.
        /// </para>
        /// <para>
        /// The lookback is bounded rather than a per-action table so this needs no extra state on the recording
        /// entity — every existing creator of one keeps working untouched. Missing the window is safe: the sample is
        /// written, which is what the old code did unconditionally. With N live axes the previous sample for any of
        /// them is N entries back, so the window is only reached when an action has been silent for a long time,
        /// where one redundant sample every <see cref="Lookback"/> entries is the worst case.
        /// </para>
        /// <para>
        /// What is deliberately NOT tracked: an action that stops appearing in the provider's buffer entirely. Replay
        /// holds its last value rather than dropping it. A device unbinding mid-session is the only way to get there,
        /// and a stick that stays where it was is visible rather than silent.
        /// </para>
        /// </remarks>
        private static bool Unchanged(DynamicBuffer<RecordedAxisSample> samples, byte actionId, float2 value)
        {
            var floor = math.max(0, samples.Length - Lookback);

            for (var i = samples.Length - 1; i >= floor; i--)
            {
                if (samples[i].ActionId == actionId)
                {
                    return math.all(samples[i].Value == value);
                }
            }

            return false;
        }

        private static void AppendEdges(BitArray256 bits, InputPhase phase, uint frame, DynamicBuffer<RecordedEdge> edges)
        {
            Emit(bits.Data1, 0, phase, frame, edges);
            Emit(bits.Data2, 64, phase, frame, edges);
            Emit(bits.Data3, 128, phase, frame, edges);
            Emit(bits.Data4, 192, phase, frame, edges);
        }

        private static void Emit(ulong data, byte offset, InputPhase phase, uint frame, DynamicBuffer<RecordedEdge> edges)
        {
            while (data != 0)
            {
                var bit = math.tzcnt(data);
                data ^= 1ul << bit;
                edges.Add(new RecordedEdge
                {
                    Frame = frame,
                    ActionId = (byte)(offset + bit),
                    Phase = phase
                });
            }
        }
    }
}
