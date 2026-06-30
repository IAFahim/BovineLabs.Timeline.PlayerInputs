using System;
using System.ComponentModel;
using BovineLabs.Reaction.Authoring.Core;
using BovineLabs.Timeline.Authoring;
using UnityEngine.Timeline;

namespace BovineLabs.Timeline.PlayerInputs.Flow.Authoring
{
    [Serializable]
    [TrackClipType(typeof(SplineFlowInputClip))]
    [TrackColor(0.4f, 0.9f, 0.6f)]
    [TrackBindingType(typeof(TargetsAuthoring))]
    [DisplayName("BovineLabs/Player Inputs/Spline Flow Input (Fake Axis)")]
    public sealed class SplineFlowInputTrack : DOTSTrack
    {
    }
}
