using BovineLabs.Timeline.Data;
using BovineLabs.Timeline.PlayerInputs.Flow;
using Unity.Entities;

namespace BovineLabs.Timeline.PlayerInputs
{
    [UpdateInGroup(typeof(TimelineComponentAnimationGroup))]
    [UpdateAfter(typeof(GridFlowInputSystem))]
    [UpdateBefore(typeof(AxisTransformSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation |
                       WorldSystemFilterFlags.ServerSimulation)]
    public partial class PlayerInputProjectionGroup : ComponentSystemGroup
    {
    }
}
