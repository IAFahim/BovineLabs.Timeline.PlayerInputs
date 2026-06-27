using BovineLabs.Testing;
using BovineLabs.Timeline.PlayerInputs.Data;
using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;

namespace BovineLabs.Timeline.PlayerInputs.Tests
{
    // Typed-input declaration the source generator turns into SeatTestInput_Map + SeatTestInput_Projection.
    // Declared here so the generated projection compiles into the Tests assembly and can be driven directly.
    public partial struct SeatTestInput : IPlayerInput
    {
        [InputAction]
        public ButtonState Jump;

        [InputAction]
        public float2 Move;
    }

    // Acceptance gate for local coop: each provider (seat) must receive ITS OWN input, never another seat's.
    public class SeatRoutingTests : ECSTestsFixture
    {
        [Test]
        public void Projection_RoutesEachSeatToItsOwnInput()
        {
            // Map: Jump=action 0, Move=action 1. (Bake normally produces this; here we set it by hand.)
            var mapEntity = Manager.CreateEntity();
            Manager.AddComponentData(mapEntity, new SeatTestInput_Map { Jump = 0, Move = 1 });

            // Projection requires the registry singleton to run.
            var registry = Manager.CreateEntity();
            Manager.AddComponent<InputRegistry>(registry);

            var seatA = MakeSeat(jumpDown: true, move: new float2(1f, 0f));
            var seatB = MakeSeat(jumpDown: false, move: new float2(0f, 1f));

            World.GetOrCreateSystem<SeatTestInput_Projection>().Update(WorldUnmanaged);

            var a = Manager.GetComponentData<SeatTestInput>(seatA);
            var b = Manager.GetComponentData<SeatTestInput>(seatB);

            Assert.IsTrue(a.Jump.Down, "Seat A pressed Jump");
            Assert.AreEqual(new float2(1f, 0f), a.Move, "Seat A move");

            Assert.IsFalse(b.Jump.Down, "Seat B did not press Jump (no cross-talk from A)");
            Assert.AreEqual(new float2(0f, 1f), b.Move, "Seat B move");
        }

        private Entity MakeSeat(bool jumpDown, float2 move)
        {
            var seat = Manager.CreateEntity();
            Manager.AddComponent<ProviderTag>(seat);

            var state = new InputState();
            state.Down[0] = jumpDown;
            state.Held[0] = jumpDown;
            Manager.AddComponentData(seat, state);

            var axes = Manager.AddBuffer<InputAxis>(seat);
            axes.Add(new InputAxis { ActionId = 1, Value = move });

            return seat;
        }
    }
}
