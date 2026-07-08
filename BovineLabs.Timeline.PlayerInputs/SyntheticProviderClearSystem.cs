using BovineLabs.Timeline.Data;
using BovineLabs.Timeline.PlayerInputs.Data;
using BovineLabs.Timeline.PlayerInputs.Flow.Data;
using Unity.Entities;

namespace BovineLabs.Timeline.PlayerInputs.Flow
{
    /// <summary>
    /// Owns the once-per-frame clear of every synthetic input provider's <see cref="InputAxis"/> buffer, so any number
    /// of synthetic-input producers (the grid field flow, the spline flow, future ones) can simply accumulate.
    ///
    /// Runs first in the group across Local|Client|Server|Editor — every world any flow system runs in — so GridFlow no
    /// longer needs its own private clear loop (it now just accumulates and orders itself after this system). The clear
    /// is a plain per-frame reset of an already-empty-or-stale buffer; redundant with nothing since it is the single owner.
    /// </summary>
    [UpdateInGroup(typeof(TimelineComponentAnimationGroup), OrderFirst = true)]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation |
                       WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.Editor)]
    public partial struct SyntheticProviderClearSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            foreach (var axes in SystemAPI.Query<DynamicBuffer<InputAxis>>()
                         .WithAll<ProviderTag, SyntheticProviderTag>())
                axes.Clear();
        }
    }
}
