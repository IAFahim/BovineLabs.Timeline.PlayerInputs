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
                    samples.Add(new RecordedAxisSample { Frame = frame, ActionId = axes[i].ActionId, Value = axes[i].Value });
                }

                recording.ValueRW.FrameCount = frame + 1;
            }
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
