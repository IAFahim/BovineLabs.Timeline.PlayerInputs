using BovineLabs.Testing;
using BovineLabs.Timeline.PlayerInputs;
using BovineLabs.Timeline.PlayerInputs.Data;
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
            Assert.AreEqual(provider, registry.ProviderByPlayer[7]);
        }

        [Test]
        public void Registry_FiresJoinLeaveEvents()
        {
            var sys = World.GetOrCreateSystem<InputRegistrySystem>();
            
            // First frame: Player 7 joins
            var provider = Manager.CreateEntity();
            Manager.AddComponentData(provider, new PlayerId { Value = 7 });
            Manager.AddComponentData(provider, new ProviderTag());

            sys.Update(WorldUnmanaged);

            var joinedEntity = Manager.CreateEntityQuery(typeof(PlayerJoined)).GetSingletonEntity();
            var joined = Manager.GetBuffer<PlayerJoined>(joinedEntity);
            Assert.AreEqual(1, joined.Length);
            Assert.AreEqual(7, joined[0].PlayerId);
            Assert.AreEqual(provider, joined[0].Provider);

            // Second frame: No changes
            sys.Update(WorldUnmanaged);
            joined = Manager.GetBuffer<PlayerJoined>(joinedEntity);
            Assert.AreEqual(0, joined.Length);

            // Third frame: Player 7 leaves
            Manager.RemoveComponent<ProviderTag>(provider);
            sys.Update(WorldUnmanaged);

            var leftEntity = Manager.CreateEntityQuery(typeof(PlayerLeft)).GetSingletonEntity();
            var left = Manager.GetBuffer<PlayerLeft>(leftEntity);
            Assert.AreEqual(1, left.Length);
            Assert.AreEqual(7, left[0].PlayerId);
        }
    }
}
