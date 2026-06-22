using BovineLabs.Timeline.Grid.Influence.Data;
using Unity.Mathematics;
using Unity.Transforms;

namespace BovineLabs.Timeline.PlayerInputs.Flow.Data
{
    public static class GridProjection
    {
        public static int2 ToCell(in LocalTransform transform, float3 localOffset, in GridBasis basis, float cellSize)
        {
            var world = transform.Position + math.rotate(transform.Rotation, localOffset);
            var projected = basis.ToGridSpace(world);
            return new int2(
                (int)math.floor(projected.x / cellSize),
                (int)math.floor(projected.y / cellSize));
        }
    }
}
