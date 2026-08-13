using BovineLabs.Timeline.Data;
using BovineLabs.Timeline.PlayerInputs.Flow;
using Unity.Entities;

namespace BovineLabs.Timeline.PlayerInputs
{
    // The "run before AxisTransformSystem" half of the ordering lives on AxisTransformSystem instead, because that
    // system is LocalSimulation-only while this group exists in client and server worlds too. Declared from this
    // side, the target is simply absent in those worlds and Unity discards the attribute with a warning on every
    // world creation. An ordering attribute has to be declared by whichever system has the NARROWER world filter,
    // so its target is guaranteed to exist wherever the attribute does. GridFlowInputSystem is wider than this
    // group, so the UpdateAfter below is always satisfiable and stays here.
    [UpdateInGroup(typeof(TimelineComponentAnimationGroup))]
    [UpdateAfter(typeof(GridFlowInputSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation |
                       WorldSystemFilterFlags.ServerSimulation)]
    public partial class PlayerInputProjectionGroup : ComponentSystemGroup
    {
    }
}
