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
    /// Runs first in the group, LocalSimulation-only to match SplineFlowInputSystem / AxisTransformSystem. In the
    /// Server/Client/Editor worlds (where SplineFlow does not run) GridFlowInputSystem owns the clear via its own
    /// internal loop, so this system is intentionally absent there — that is why GridFlow's clear must NOT be removed.
    /// In the Local world this runs OrderFirst; GridFlow's redundant re-clear of an already-empty buffer is harmless.
    /// </summary>
    [UpdateInGroup(typeof(TimelineComponentAnimationGroup), OrderFirst = true)]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation)]
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
