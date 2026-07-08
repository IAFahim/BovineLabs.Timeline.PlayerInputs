using BovineLabs.Testing;
using BovineLabs.Timeline.PlayerInputs.Data;
using BovineLabs.Timeline.PlayerInputs.Flow.Data;
using NUnit.Framework;
using Unity.Entities;

namespace BovineLabs.Timeline.PlayerInputs.Tests
{
    public class InputRegistryTests : ECSTestsFixture
    {
        [Test]
        public void Registry_MapsPlayerIdToProvider()
        {
            var provider = Manager.CreateEntity();
            Manager.AddComponentData(provider, new PlayerId { Value = 7 });
            Manager.AddComponentData(provider, new ProviderTag());

            var sys = World.GetOrCreateSystem<InputRegistrySystem>();
            sys.Update(WorldUnmanaged);

            var registryEntity = Manager.CreateEntityQuery(typeof(InputRegistry)).GetSingletonEntity();
            var registry = Manager.GetComponentData<InputRegistry>(registryEntity);
            Assert.AreEqual(provider, Manager.GetBuffer<ProviderSlot>(registryEntity)[7].Human);
        }

        [Test]
        public void Registry_FiresJoinLeaveEvents()
        {
            var sys = World.GetOrCreateSystem<InputRegistrySystem>();

            var provider = Manager.CreateEntity();
            Manager.AddComponentData(provider, new PlayerId { Value = 7 });
            Manager.AddComponentData(provider, new ProviderTag());

            sys.Update(WorldUnmanaged);

            var joinedEntity = Manager.CreateEntityQuery(typeof(PlayerJoined)).GetSingletonEntity();
            var joined = Manager.GetBuffer<PlayerJoined>(joinedEntity);
            Assert.AreEqual(1, joined.Length);
            Assert.AreEqual(7, joined[0].PlayerId);
            Assert.AreEqual(provider, joined[0].Provider);

            sys.Update(WorldUnmanaged);
            joined = Manager.GetBuffer<PlayerJoined>(joinedEntity);
            Assert.AreEqual(0, joined.Length);

            Manager.RemoveComponent<ProviderTag>(provider);
            sys.Update(WorldUnmanaged);

            var leftEntity = Manager.CreateEntityQuery(typeof(PlayerLeft)).GetSingletonEntity();
            var left = Manager.GetBuffer<PlayerLeft>(leftEntity);
            Assert.AreEqual(1, left.Length);
            Assert.AreEqual(7, left[0].PlayerId);
        }

        [Test]
        public void Registry_RetiringProviderAlone_StillOccupiesSlot()
        {
            var retiring = Manager.CreateEntity();
            Manager.AddComponentData(retiring, new PlayerId { Value = 4 });
            Manager.AddComponent<ProviderTag>(retiring);
            Manager.AddComponent<ProviderRetiring>(retiring);

            var sys = World.GetOrCreateSystem<InputRegistrySystem>();
            sys.Update(WorldUnmanaged);

            var registryEntity = Manager.CreateEntityQuery(typeof(InputRegistry)).GetSingletonEntity();
            var registry = Manager.GetComponentData<InputRegistry>(registryEntity);
            Assert.AreEqual(retiring, Manager.GetBuffer<ProviderSlot>(registryEntity)[4].Human);
        }

        [Test]
        public void Registry_PrefersLiveProviderOverRetiringOnSameId()
        {
            var retiring = Manager.CreateEntity();
            Manager.AddComponentData(retiring, new PlayerId { Value = 3 });
            Manager.AddComponent<ProviderTag>(retiring);
            Manager.AddComponent<ProviderRetiring>(retiring);

            var live = Manager.CreateEntity();
            Manager.AddComponentData(live, new PlayerId { Value = 3 });
            Manager.AddComponent<ProviderTag>(live);

            var sys = World.GetOrCreateSystem<InputRegistrySystem>();
            sys.Update(WorldUnmanaged);

            var registryEntity = Manager.CreateEntityQuery(typeof(InputRegistry)).GetSingletonEntity();
            var registry = Manager.GetComponentData<InputRegistry>(registryEntity);
            Assert.AreEqual(live, Manager.GetBuffer<ProviderSlot>(registryEntity)[3].Human);
        }

        [Test]
        public void Registry_HumanAndSyntheticOnSameSeat_FillBothSlots_NoError()
        {
            var human = Manager.CreateEntity();
            Manager.AddComponentData(human, new PlayerId { Value = 5 });
            Manager.AddComponent<ProviderTag>(human);

            var syn = Manager.CreateEntity();
            Manager.AddComponentData(syn, new PlayerId { Value = 5 });
            Manager.AddComponent<ProviderTag>(syn);
            Manager.AddComponent<SyntheticProviderTag>(syn);

            var sys = World.GetOrCreateSystem<InputRegistrySystem>();
            // Human-vs-synthetic on the same seat is the takeover topology, not an error.
            sys.Update(WorldUnmanaged);

            var registryEntity = Manager.CreateEntityQuery(typeof(InputRegistry)).GetSingletonEntity();
            var slot = Manager.GetBuffer<ProviderSlot>(registryEntity)[5];
            Assert.AreEqual(human, slot.Human);
            Assert.AreEqual(syn, slot.Synthetic);
        }

        [Test]
        public void Registry_DuplicateHumans_LowestSeqWins()
        {
            var first = Manager.CreateEntity();
            Manager.AddComponentData(first, new PlayerId { Value = 2 });
            Manager.AddComponent<ProviderTag>(first);
            Manager.AddComponentData(first, new ProviderSeq { Value = 3 });

            var second = Manager.CreateEntity();
            Manager.AddComponentData(second, new PlayerId { Value = 2 });
            Manager.AddComponent<ProviderTag>(second);
            Manager.AddComponentData(second, new ProviderSeq { Value = 1 }); // lower seq -> should win

            var sys = World.GetOrCreateSystem<InputRegistrySystem>();
            // The duplicate emits a (Burst-discarded) error via managed Debug.LogError in edit-mode; tolerate it
            // without coupling the assertion to whether Burst is on.
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            sys.Update(WorldUnmanaged);

            var registryEntity = Manager.CreateEntityQuery(typeof(InputRegistry)).GetSingletonEntity();
            Assert.AreEqual(second, Manager.GetBuffer<ProviderSlot>(registryEntity)[2].Human);
        }

        [Test]
        public void Registry_ProviderReplacedWithoutRetiring_ResolvesToSurvivor_NoErrorSpam()
        {
            // Churn / world-teardown path: a provider is destroyed WITHOUT a ProviderRetiring stamp and a fresh
            // provider claims the same seat. The registry must deterministically resolve to the sole survivor
            // with no lingering pointer to the destroyed entity — and must not spam duplicate errors, since no
            // two live same-kind providers ever coexist on the seat. (LogAssert fails the test on any
            // unexpected logged error, so the absence of spam is pinned by not tolerating errors here.)
            var sys = World.GetOrCreateSystem<InputRegistrySystem>();

            var first = Manager.CreateEntity();
            Manager.AddComponentData(first, new PlayerId { Value = 6 });
            Manager.AddComponent<ProviderTag>(first);
            Manager.AddComponentData(first, new ProviderSeq { Value = 10 });
            sys.Update(WorldUnmanaged);

            var registryEntity = Manager.CreateEntityQuery(typeof(InputRegistry)).GetSingletonEntity();
            Assert.AreEqual(first, Manager.GetBuffer<ProviderSlot>(registryEntity)[6].Human);

            // Destroy the incumbent with no retirement, then a fresh provider (higher seq) takes the seat.
            Manager.DestroyEntity(first);
            var second = Manager.CreateEntity();
            Manager.AddComponentData(second, new PlayerId { Value = 6 });
            Manager.AddComponent<ProviderTag>(second);
            Manager.AddComponentData(second, new ProviderSeq { Value = 20 });
            sys.Update(WorldUnmanaged);

            var slot = Manager.GetBuffer<ProviderSlot>(registryEntity)[6];
            Assert.AreEqual(second, slot.Human, "sole survivor wins regardless of its higher seq");
            Assert.AreEqual(Entity.Null, slot.Synthetic, "no synthetic ever existed on the seat");

            // A few more stable frames must keep the deterministic winner and never start logging.
            sys.Update(WorldUnmanaged);
            sys.Update(WorldUnmanaged);
            Assert.AreEqual(second, Manager.GetBuffer<ProviderSlot>(registryEntity)[6].Human);
        }
    }
}