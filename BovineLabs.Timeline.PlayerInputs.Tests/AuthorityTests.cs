using BovineLabs.Testing;
using BovineLabs.Timeline.PlayerInputs.Data;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;

namespace BovineLabs.Timeline.PlayerInputs.Tests
{
    public class AuthorityTests : ECSTestsFixture
    {
        // (a) InputAccess slot-selection truth table: preferSynthetic (== override enabled) x slots filled/empty.
        [Test]
        public void InputAccess_Provider_SlotSelectionTruthTable()
        {
            var human = Manager.CreateEntity();
            var synthetic = Manager.CreateEntity();

            var holder = Manager.CreateEntity();
            var slots = Manager.AddBuffer<ProviderSlot>(holder);
            slots.Resize(1, NativeArrayOptions.ClearMemory);

            // Both slots filled.
            slots[0] = new ProviderSlot { Human = human, Synthetic = synthetic };
            Assert.AreEqual(human, InputAccess.Provider(slots, 0, false), "no override -> human");
            Assert.AreEqual(synthetic, InputAccess.Provider(slots, 0, true), "override -> synthetic");

            // Only human filled: override still resolves to human (fallback).
            slots[0] = new ProviderSlot { Human = human, Synthetic = Entity.Null };
            Assert.AreEqual(human, InputAccess.Provider(slots, 0, false));
            Assert.AreEqual(human, InputAccess.Provider(slots, 0, true), "override falls back to human when no synthetic");

            // Only synthetic filled: no-override still resolves to synthetic (fallback).
            slots[0] = new ProviderSlot { Human = Entity.Null, Synthetic = synthetic };
            Assert.AreEqual(synthetic, InputAccess.Provider(slots, 0, true));
            Assert.AreEqual(synthetic, InputAccess.Provider(slots, 0, false), "human read falls back to synthetic when no human");

            // Both empty.
            slots[0] = default;
            Assert.AreEqual(Entity.Null, InputAccess.Provider(slots, 0, false));
            Assert.AreEqual(Entity.Null, InputAccess.Provider(slots, 0, true));
        }

        // (b) ControlAuthoritySystem: human Down under AnyInput policy engages PlayerOverride.
        [Test]
        public void Authority_EngagesOnHumanInput()
        {
            var human = MakeHumanProvider(id: 1, down: true);
            var consumer = MakeControllableConsumer(id: 1, releaseIdle: 0.1f);
            MakeRegistry(seat: 1, human: human, synthetic: Entity.Null);

            World.SetTime(new Unity.Core.TimeData(0.0, 0.016f));
            RunAuthority();

            Assert.IsTrue(Manager.IsComponentEnabled<PlayerOverride>(consumer),
                "human input under AnyInput policy engages the override");
        }

        // (b) Idle past ReleaseIdleSeconds disengages.
        [Test]
        public void Authority_ReleasesAfterIdle()
        {
            var human = MakeHumanProvider(id: 2, down: true);
            var consumer = MakeControllableConsumer(id: 2, releaseIdle: 0.1f);
            MakeRegistry(seat: 2, human: human, synthetic: Entity.Null);

            World.SetTime(new Unity.Core.TimeData(0.0, 0.016f));
            RunAuthority();
            Assert.IsTrue(Manager.IsComponentEnabled<PlayerOverride>(consumer), "engaged first");

            // Human goes idle, a big delta blows past the release window.
            Manager.SetComponentData(human, default(InputState));
            World.SetTime(new Unity.Core.TimeData(1.0, 1.0f));
            RunAuthority();

            Assert.IsFalse(Manager.IsComponentEnabled<PlayerOverride>(consumer),
                "idle past ReleaseIdleSeconds disengages the override");
        }

        // (b) TimelineOverride enabled -> authority system leaves PlayerOverride untouched (the clip owns it).
        [Test]
        public void Authority_TimelineOverride_LeavesBitAlone()
        {
            var human = MakeHumanProvider(id: 3, down: false); // idle: would normally release
            var consumer = MakeControllableConsumer(id: 3, releaseIdle: 0.1f);
            MakeRegistry(seat: 3, human: human, synthetic: Entity.Null);

            // The clip owns the bits: PlayerOverride ON, TimelineOverride ON.
            Manager.SetComponentEnabled<PlayerOverride>(consumer, true);
            Manager.SetComponentEnabled<TimelineOverride>(consumer, true);

            World.SetTime(new Unity.Core.TimeData(1.0, 1.0f));
            RunAuthority();

            Assert.IsTrue(Manager.IsComponentEnabled<PlayerOverride>(consumer),
                "authority must not clear PlayerOverride while TimelineOverride drives it");
        }

        private void RunAuthority()
        {
            World.GetOrCreateSystem<ControlAuthoritySystem>().Update(WorldUnmanaged);
            Manager.CompleteAllTrackedJobs();
        }

        private Entity MakeHumanProvider(byte id, bool down)
        {
            var provider = Manager.CreateEntity();
            Manager.AddComponentData(provider, new PlayerId { Value = id });
            Manager.AddComponent<ProviderTag>(provider);

            var st = new InputState();
            st.Down[0] = down;
            st.Held[0] = down;
            Manager.AddComponentData(provider, st);
            return provider;
        }

        private Entity MakeControllableConsumer(byte id, float releaseIdle)
        {
            var consumer = Manager.CreateEntity();
            Manager.AddComponentData(consumer, new PlayerId { Value = id });
            Manager.AddComponent<Controllable>(consumer);
            Manager.AddComponent<PlayerOverride>(consumer);
            Manager.SetComponentEnabled<PlayerOverride>(consumer, false);
            Manager.AddComponent<TimelineOverride>(consumer);
            Manager.SetComponentEnabled<TimelineOverride>(consumer, false);
            Manager.AddComponentData(consumer, new OverridePolicy
            {
                Trigger = OverrideTrigger.AnyInput,
                TriggerActionId = 0,
                ReleaseIdleSeconds = releaseIdle
            });
            Manager.AddComponentData(consumer, new OverrideState());
            return consumer;
        }

        private void MakeRegistry(byte seat, Entity human, Entity synthetic)
        {
            var reg = Manager.CreateEntity();
            Manager.AddComponent<InputRegistry>(reg);
            var slots = Manager.AddBuffer<ProviderSlot>(reg);
            slots.Resize(256, NativeArrayOptions.ClearMemory);
            slots[seat] = new ProviderSlot { Human = human, Synthetic = synthetic };
        }
    }
}
