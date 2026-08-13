using BovineLabs.Core.Collections;
using BovineLabs.Testing;
using BovineLabs.Timeline.PlayerInputs.Data;
using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;

namespace BovineLabs.Timeline.PlayerInputs.Tests
{
    public class InputRecordingTests : ECSTestsFixture
    {
        [Test]
        public void StepFrame_AppliesDownThenUpToHeld()
        {
            var edges = Edges((0, 5, InputPhase.Down), (1, 5, InputPhase.Up));
            var cursor = 0;
            var held = default(BitArray256);

            InputReplayMath.StepFrame(edges, 0, ref cursor, ref held, out var down, out var up);
            Assert.IsTrue(down[5]);
            Assert.IsTrue(held[5]);
            Assert.IsFalse(up[5]);
            Assert.AreEqual(1, cursor);

            InputReplayMath.StepFrame(edges, 1, ref cursor, ref held, out down, out up);
            Assert.IsFalse(down[5]);
            Assert.IsTrue(up[5]);
            Assert.IsFalse(held[5]);
            Assert.AreEqual(2, cursor);
        }

        [Test]
        public void StepFrame_SameFrameDownAndUp_BothEdges_HeldCleared()
        {
            var edges = Edges((0, 3, InputPhase.Down), (0, 3, InputPhase.Up));
            var cursor = 0;
            var held = default(BitArray256);

            InputReplayMath.StepFrame(edges, 0, ref cursor, ref held, out var down, out var up);
            Assert.IsTrue(down[3]);
            Assert.IsTrue(up[3]);
            Assert.IsFalse(held[3]);
            Assert.AreEqual(2, cursor);
        }

        [Test]
        public void StepFrame_OnlyConsumesCurrentFrame()
        {
            var edges = Edges((0, 1, InputPhase.Down), (2, 1, InputPhase.Up));
            var cursor = 0;
            var held = default(BitArray256);

            InputReplayMath.StepFrame(edges, 0, ref cursor, ref held, out _, out _);
            Assert.AreEqual(1, cursor);

            InputReplayMath.StepFrame(edges, 1, ref cursor, ref held, out var down, out var up);
            Assert.AreEqual(1, cursor);
            Assert.IsFalse(down[1]);
            Assert.IsFalse(up[1]);
            Assert.IsTrue(held[1]);

            InputReplayMath.StepFrame(edges, 2, ref cursor, ref held, out _, out up);
            Assert.AreEqual(2, cursor);
            Assert.IsTrue(up[1]);
            Assert.IsFalse(held[1]);
        }

        [Test]
        public void CollectAxes_RewritesTheAxisBuffer()
        {
            var samples = Samples(
                (1, 3, new float2(1f, 0f)),
                (1, 4, new float2(0f, 1f)),
                (2, 3, new float2(0.5f, 0f)));
            var outAxes = AxisBuffer();
            outAxes.Add(new InputAxis { ActionId = 9, Value = new float2(9f, 9f) });

            var cursor = 0;
            InputReplayMath.CollectAxes(samples, 1, ref cursor, outAxes);
            Assert.AreEqual(2, outAxes.Length);
            Assert.AreEqual(3, outAxes[0].ActionId);
            Assert.AreEqual(new float2(1f, 0f), outAxes[0].Value);
            Assert.AreEqual(4, outAxes[1].ActionId);
            Assert.AreEqual(2, cursor);

            InputReplayMath.CollectAxes(samples, 2, ref cursor, outAxes);
            Assert.AreEqual(1, outAxes.Length);
            Assert.AreEqual(3, outAxes[0].ActionId);
            Assert.AreEqual(new float2(0.5f, 0f), outAxes[0].Value);
            Assert.AreEqual(3, cursor);
        }

        [Test]
        public void Reset_RestoresInitialHeldAndCursors()
        {
            var initial = default(BitArray256);
            initial[7] = true;

            var replay = new InputReplay { Frame = 5, EdgeCursor = 3, AxisCursor = 2, Loop = true };
            InputReplayMath.Reset(ref replay, initial);

            Assert.AreEqual(0u, replay.Frame);
            Assert.AreEqual(0, replay.EdgeCursor);
            Assert.AreEqual(0, replay.AxisCursor);
            Assert.IsTrue(replay.Held[7]);
            Assert.IsTrue(replay.Loop);
        }

        [Test]
        public void ReplayDefaultsToTheSeatItWasRecordedOn()
        {
            var recording = this.RecordingOnSeat(3);
            Manager.AddComponentData(recording, new InputReplay());

            World.GetOrCreateSystemManaged<InputReplaySystem>().Update();

            var provider = Manager.GetComponentData<InputReplay>(recording).Provider;
            Assert.AreEqual(3, Manager.GetComponentData<PlayerId>(provider).Value,
                "an InputReplay with nothing set must keep replaying where it was captured");
        }

        [Test]
        public void RetargetingPutsTheReplayOnAnotherSeat()
        {
            var recording = this.RecordingOnSeat(3);
            Manager.AddComponentData(recording, InputReplay.OnSeat(7));

            World.GetOrCreateSystemManaged<InputReplaySystem>().Update();

            var provider = Manager.GetComponentData<InputReplay>(recording).Provider;
            Assert.AreEqual(7, Manager.GetComponentData<PlayerId>(provider).Value,
                "a recording captured on 3 must be able to drive seat 7 instead");
        }

        [Test]
        public void TwoRecordingsRetargetedToDifferentSeatsEachGetTheirOwnProvider()
        {
            var first = this.RecordingOnSeat(0);
            var second = this.RecordingOnSeat(0);
            Manager.AddComponentData(first, InputReplay.OnSeat(1));
            Manager.AddComponentData(second, InputReplay.OnSeat(2));

            World.GetOrCreateSystemManaged<InputReplaySystem>().Update();

            var a = Manager.GetComponentData<InputReplay>(first).Provider;
            var b = Manager.GetComponentData<InputReplay>(second).Provider;

            Assert.AreNotEqual(a, b, "one recording per seat means one provider per seat");
            Assert.AreEqual(1, Manager.GetComponentData<PlayerId>(a).Value);
            Assert.AreEqual(2, Manager.GetComponentData<PlayerId>(b).Value);
        }

        /// <summary>A one-frame recording captured on <paramref name="seat"/>, ready to replay.</summary>
        private Entity RecordingOnSeat(byte seat)
        {
            var entity = Manager.CreateEntity();
            Manager.AddComponentData(entity, new InputRecording { Seat = seat, FrameCount = 1 });
            Manager.AddBuffer<RecordedEdge>(entity);
            Manager.AddBuffer<RecordedAxisSample>(entity);
            return entity;
        }

        [Test]
        public void RecordThenReplay_ReproducesProviderStateEachFrame()
        {
            var provider = Manager.CreateEntity();
            Manager.AddComponent<ProviderTag>(provider);
            Manager.AddComponentData(provider, new PlayerId { Value = 3 });
            Manager.AddComponentData(provider, new InputState());
            Manager.AddBuffer<InputAxis>(provider);

            var recordingEntity = Manager.CreateEntity();
            Manager.AddComponentData(recordingEntity, new InputRecording { Seat = 3 });
            Manager.AddBuffer<RecordedEdge>(recordingEntity);
            Manager.AddBuffer<RecordedAxisSample>(recordingEntity);
            Manager.AddComponent<InputRecordingActive>(recordingEntity);

            var recordSystem = World.GetOrCreateSystemManaged<InputRecordSystem>();

            SetDown(provider, 5);
            recordSystem.Update();

            SetHeldWithAxis(provider, 5, 2, new float2(1f, 0f));
            recordSystem.Update();

            SetUp(provider, 5);
            recordSystem.Update();

            var recording = Manager.GetComponentData<InputRecording>(recordingEntity);
            Assert.AreEqual(3u, recording.FrameCount);
            Assert.IsTrue(recording.InitialHeld[5]);

            var recEdges = Manager.GetBuffer<RecordedEdge>(recordingEntity);
            Assert.AreEqual(2, recEdges.Length);
            Assert.AreEqual(0u, recEdges[0].Frame);
            Assert.AreEqual(5, recEdges[0].ActionId);
            Assert.AreEqual(InputPhase.Down, recEdges[0].Phase);
            Assert.AreEqual(2u, recEdges[1].Frame);
            Assert.AreEqual(5, recEdges[1].ActionId);
            Assert.AreEqual(InputPhase.Up, recEdges[1].Phase);

            var recSamples = Manager.GetBuffer<RecordedAxisSample>(recordingEntity);
            Assert.AreEqual(1, recSamples.Length);
            Assert.AreEqual(1u, recSamples[0].Frame);
            Assert.AreEqual(2, recSamples[0].ActionId);
            Assert.AreEqual(new float2(1f, 0f), recSamples[0].Value);

            Manager.SetComponentEnabled<InputRecordingActive>(recordingEntity, false);
            Manager.AddComponentData(recordingEntity, new InputReplay { Loop = false });

            var replaySystem = World.GetOrCreateSystemManaged<InputReplaySystem>();

            replaySystem.Update();
            var replayProvider = Manager.GetComponentData<InputReplay>(recordingEntity).Provider;
            Assert.AreNotEqual(Entity.Null, replayProvider);

            var s0 = Manager.GetComponentData<InputState>(replayProvider);
            Assert.IsTrue(s0.Down[5]);
            Assert.IsTrue(s0.Held[5]);
            Assert.IsFalse(s0.Up[5]);
            Assert.AreEqual(0, Manager.GetBuffer<InputAxis>(replayProvider).Length);

            replaySystem.Update();
            var s1 = Manager.GetComponentData<InputState>(replayProvider);
            Assert.IsFalse(s1.Down[5]);
            Assert.IsTrue(s1.Held[5]);
            Assert.IsFalse(s1.Up[5]);
            var ax1 = Manager.GetBuffer<InputAxis>(replayProvider);
            Assert.AreEqual(1, ax1.Length);
            Assert.AreEqual(2, ax1[0].ActionId);
            Assert.AreEqual(new float2(1f, 0f), ax1[0].Value);

            replaySystem.Update();
            var s2 = Manager.GetComponentData<InputState>(replayProvider);
            Assert.IsFalse(s2.Down[5]);
            Assert.IsFalse(s2.Held[5]);
            Assert.IsTrue(s2.Up[5]);
            Assert.AreEqual(0, Manager.GetBuffer<InputAxis>(replayProvider).Length);

            replaySystem.Update();
            Assert.IsTrue(Manager.HasComponent<ProviderRetiring>(replayProvider));
            Assert.IsFalse(Manager.HasComponent<InputReplay>(recordingEntity));
        }

        private DynamicBuffer<RecordedEdge> Edges(params (uint frame, byte action, InputPhase phase)[] entries)
        {
            var entity = Manager.CreateEntity(typeof(RecordedEdge));
            var buffer = Manager.GetBuffer<RecordedEdge>(entity);
            foreach (var (frame, action, phase) in entries)
            {
                buffer.Add(new RecordedEdge { Frame = frame, ActionId = action, Phase = phase });
            }

            return buffer;
        }

        private DynamicBuffer<RecordedAxisSample> Samples(params (uint frame, byte action, float2 value)[] entries)
        {
            var entity = Manager.CreateEntity(typeof(RecordedAxisSample));
            var buffer = Manager.GetBuffer<RecordedAxisSample>(entity);
            foreach (var (frame, action, value) in entries)
            {
                buffer.Add(new RecordedAxisSample { Frame = frame, ActionId = action, Value = value });
            }

            return buffer;
        }

        private DynamicBuffer<InputAxis> AxisBuffer()
        {
            var entity = Manager.CreateEntity(typeof(InputAxis));
            return Manager.GetBuffer<InputAxis>(entity);
        }

        private void SetDown(Entity provider, byte id)
        {
            var state = new InputState();
            state.Down[id] = true;
            state.Held[id] = true;
            Manager.SetComponentData(provider, state);
            Manager.GetBuffer<InputAxis>(provider).Clear();
        }

        private void SetHeldWithAxis(Entity provider, byte id, byte axisId, float2 axis)
        {
            var state = new InputState();
            state.Held[id] = true;
            Manager.SetComponentData(provider, state);

            var buffer = Manager.GetBuffer<InputAxis>(provider);
            buffer.Clear();
            buffer.Add(new InputAxis { ActionId = axisId, Value = axis });
        }

        private void SetUp(Entity provider, byte id)
        {
            var state = new InputState();
            state.Up[id] = true;
            Manager.SetComponentData(provider, state);
            Manager.GetBuffer<InputAxis>(provider).Clear();
        }
    }
}
