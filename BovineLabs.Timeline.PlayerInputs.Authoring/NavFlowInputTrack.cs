using System;
using System.ComponentModel;
using BovineLabs.Reaction.Authoring.Core;
using BovineLabs.Timeline.Authoring;
using UnityEngine.Timeline;

namespace BovineLabs.Timeline.PlayerInputs.Flow.Authoring
{
    [Serializable]
    [TrackClipType(typeof(NavFlowInputClip))]
    [TrackColor(0.4f, 0.7f, 0.95f)]
    [TrackBindingType(typeof(TargetsAuthoring))]
    [DisplayName("BovineLabs/Player Inputs/Nav Flow Input (Fake Axis)")]
    public sealed class NavFlowInputTrack : DOTSTrack
    {
    }
}
