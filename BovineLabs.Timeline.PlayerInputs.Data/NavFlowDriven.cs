using Unity.Entities;

namespace BovineLabs.Timeline.PlayerInputs.Flow.Data
{
    /// <summary>
    /// Teardown marker added by <c>NavFlowInputSystem</c> to a Traverse proxy the first frame a NavFlow clip drives it,
    /// and removed by the same system's sweep the frame the proxy stops being driven. Its whole job is to close the
    /// lifecycle hole where a clip is destroyed WHILE ACTIVE (director scene-unloaded, timeline killed by gameplay): the
    /// clean deactivate edge never runs, so without this the hidden proxy would keep pathfinding toward its last
    /// destination forever (invisible CPU drain + a proxy that may re-trigger downstream logic on "arrival").
    ///
    /// The sweep disables <c>IsPathfinding</c> and removes this marker for any marked proxy not driven this frame, which
    /// covers BOTH a clean clip end and a hard mid-clip teardown with one code path.
    /// </summary>
    public struct NavFlowDriven : IComponentData
    {
    }
}
