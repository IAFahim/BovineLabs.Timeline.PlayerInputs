using BovineLabs.Timeline.Grid.Influence.Data;
using BovineLabs.Timeline.PlayerInputs.Flow.Data;
using NUnit.Framework;
using Unity.Mathematics;
using Unity.Transforms;

namespace BovineLabs.Timeline.PlayerInputs.Tests
{
    public class GridProjectionTests
    {
        private static int2 Expected(GridBasis basis, float3 position, quaternion rotation, float3 localOffset,
            float cellSize)
        {
            var world = position + math.rotate(rotation, localOffset);
            var projected = basis.ToGridSpace(world);
            return new int2(
                (int)math.floor(projected.x / cellSize),
                (int)math.floor(projected.y / cellSize));
        }

        [Test]
        public void IdentityZeroOffset_FloorsAcrossPositiveAndNegative()
        {
            var basis = new GridBasis(math.up());

            var positive = LocalTransform.FromPosition(new float3(-3.4f, 9f, 7.6f));
            Assert.AreEqual(new int2(3, 7), GridProjection.ToCell(positive, float3.zero, basis, 1f));

            var negative = LocalTransform.FromPosition(new float3(3.4f, 9f, -7.6f));
            Assert.AreEqual(new int2(-4, -8), GridProjection.ToCell(negative, float3.zero, basis, 1f));
        }

        [Test]
        public void OnCellBoundary_FloorsDownToTheUpperCell()
        {
            var basis = new GridBasis(math.up());
            var transform = LocalTransform.FromPosition(new float3(-4f, 0f, 6f));

            Assert.AreEqual(new int2(2, 3), GridProjection.ToCell(transform, float3.zero, basis, 2f));
        }

        [Test]
        public void NonIdentityRotation_RotatesLocalOffsetBeforeProjection()
        {
            var basis = new GridBasis(math.up());
            var rotation = quaternion.Euler(0f, math.radians(90f), 0f);
            var transform = new LocalTransform { Position = float3.zero, Rotation = rotation, Scale = 1f };
            var localOffset = new float3(0f, 0f, 1f);

            Assert.AreEqual(
                Expected(basis, float3.zero, rotation, localOffset, 1f),
                GridProjection.ToCell(transform, localOffset, basis, 1f));
        }

        [Test]
        public void KnownPlaneNormalAndCellSize_MatchesHandComputedCell()
        {
            var basis = new GridBasis(new float3(0.3f, 0.8f, -0.5f));
            var rotation = quaternion.Euler(0.4f, 1.1f, -0.7f);
            var transform = new LocalTransform
            {
                Position = new float3(12.5f, 3f, -7.25f), Rotation = rotation, Scale = 1f,
            };
            var localOffset = new float3(0.5f, 0f, 1.25f);
            var cellSize = 0.25f;

            Assert.AreEqual(
                Expected(basis, transform.Position, rotation, localOffset, cellSize),
                GridProjection.ToCell(transform, localOffset, basis, cellSize));
        }
    }
}
