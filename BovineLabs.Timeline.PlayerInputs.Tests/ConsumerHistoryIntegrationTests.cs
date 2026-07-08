using BovineLabs.Core.Collections;
using BovineLabs.Testing;
using BovineLabs.Timeline.PlayerInputs.Data;
using NUnit.Framework;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;

namespace BovineLabs.Timeline.PlayerInputs.Tests
{
    // End-to-end coverage of the projection chain that had ZERO tests (the dormant-buffer incident lived here):
    // provider InputState -> ConsumerHistorySystem records a tick+millis-stamped InputHistory ring -> the matcher
    // matches a sequence over that recorded history. Also pins the same-tick Down/Up ordering fix.
    public class ConsumerHistoryIntegrationTests : ECSTestsFixture
    {
        private const byte Seat = 4;
        private const byte Action = 3;

        [Test]
        public void ProviderState_RecordsHistory_ThenSequenceMatches()
        {
            var provider = MakeProvider(down: Action, held: Action);
            MakeRegistry(Seat, provider);
            SetTick(7, elapsedSeconds: 0.5); // 500 ms

            var consumer = MakeConsumer(Seat, MaskFor(Action));

            RunHistory();

            var history = Manager.GetBuffer<InputHistory>(consumer);
            Assert.AreEqual(1, history.Length, "one Down transition recorded");
            Assert.AreEqual(Action, history[0].ActionId);
            Assert.AreEqual(InputPhase.Down, history[0].Phase);
            Assert.AreEqual(7u, history[0].Tick, "tick stamped from SimulationTick");
            Assert.AreEqual(500u, history[0].Millis, "millis stamped from ElapsedTime");

            // The sequence layer reads exactly this buffer: a one-step Consume(Action, Down) must match.
            var mask = default(BitArray256);
            var searchIndex = 0;
            var window = default(MatchWindow);
            var step = new CommandStep { Mode = CommandMode.Consume, ActionId = Action, Phase = InputPhase.Down };
            Assert.IsTrue(CommandMatcher.Evaluate(ref step, default, history, ref mask, ref searchIndex, ref window),
                "recorded history satisfies the sequence");
        }

        [Test]
        public void MaskedOutAction_IsNotRecorded()
        {
            var provider = MakeProvider(down: Action, held: Action);
            MakeRegistry(Seat, provider);
            SetTick(1, elapsedSeconds: 0.1);

            // Window only allows a DIFFERENT action, so the pressed Action must not enter history.
            var consumer = MakeConsumer(Seat, MaskFor(9));

            RunHistory();

            Assert.AreEqual(0, Manager.GetBuffer<InputHistory>(consumer).Length);
        }

        [Test]
        public void SameTick_DownAndUp_Held_OrdersUpThenDown()
        {
            // Both edges this frame AND still held => the press was last => Up recorded before Down.
            var provider = MakeProvider(down: Action, up: Action, held: Action);
            MakeRegistry(Seat, provider);
            SetTick(2, elapsedSeconds: 0.2);

            var consumer = MakeConsumer(Seat, MaskFor(Action));
            RunHistory();

            var history = Manager.GetBuffer<InputHistory>(consumer);
            Assert.AreEqual(2, history.Length);
            Assert.AreEqual(InputPhase.Up, history[0].Phase, "held => Up first");
            Assert.AreEqual(InputPhase.Down, history[1].Phase, "held => Down last");
        }

        [Test]
        public void SameTick_DownAndUp_NotHeld_OrdersDownThenUp()
        {
            // Both edges this frame and NOT held => press then release => Down recorded before Up.
            var provider = MakeProvider(down: Action, up: Action);
            MakeRegistry(Seat, provider);
            SetTick(2, elapsedSeconds: 0.2);

            var consumer = MakeConsumer(Seat, MaskFor(Action));
            RunHistory();

            var history = Manager.GetBuffer<InputHistory>(consumer);
            Assert.AreEqual(2, history.Length);
            Assert.AreEqual(InputPhase.Down, history[0].Phase, "not held => Down first");
            Assert.AreEqual(InputPhase.Up, history[1].Phase, "not held => Up last");
        }

        private void RunHistory()
        {
            World.GetOrCreateSystem<ConsumerHistorySystem>().Update(WorldUnmanaged);
            Manager.CompleteAllTrackedJobs();
        }

        private void SetTick(uint tick, double elapsedSeconds)
        {
            var e = Manager.CreateEntity();
            Manager.AddComponentData(e, new SimulationTick { Value = tick });
            World.SetTime(new TimeData(elapsedSeconds, 0.016f));
        }

        private static BitArray256 MaskFor(byte action)
        {
            var mask = default(BitArray256);
            mask[action] = true;
            return mask;
        }

        private Entity MakeProvider(byte down = 255, byte up = 255, byte held = 255)
        {
            var provider = Manager.CreateEntity();
            Manager.AddComponent<ProviderTag>(provider);
            var st = new InputState();
            if (down != 255) st.Down[down] = true;
            if (up != 255) st.Up[up] = true;
            if (held != 255) st.Held[held] = true;
            Manager.AddComponentData(provider, st);
            return provider;
        }

        private void MakeRegistry(byte seat, Entity human)
        {
            var reg = Manager.CreateEntity();
            Manager.AddComponent<InputRegistry>(reg);
            var slots = Manager.AddBuffer<ProviderSlot>(reg);
            slots.Resize(256, NativeArrayOptions.ClearMemory);
            slots[seat] = new ProviderSlot { Human = human, Synthetic = Entity.Null };
        }

        private Entity MakeConsumer(byte seat, BitArray256 mask)
        {
            var consumer = Manager.CreateEntity();
            Manager.AddComponent<ConsumerTag>(consumer);
            Manager.AddComponentData(consumer, new PlayerId { Value = seat });
            Manager.AddComponentData(consumer, new ActiveBufferMask { Value = mask });
            Manager.AddBuffer<InputHistory>(consumer);
            return consumer;
        }
    }
}
