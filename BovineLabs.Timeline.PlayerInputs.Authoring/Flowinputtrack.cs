using System;
using System.ComponentModel;
using BovineLabs.Reaction.Authoring.Core;
using BovineLabs.Timeline.Authoring;
using UnityEngine.Timeline;

namespace BovineLabs.Timeline.PlayerInputs.Flow.Authoring
{
    [Serializable]
    [TrackClipType(typeof(FlowInputClip))]
    [TrackColor(0.4f, 0.7f, 0.9f)]
    [TrackBindingType(typeof(TargetsAuthoring))]
    [DisplayName("BovineLabs/Player Inputs/Flow Input (Fake Axis)")]
    public sealed class FlowInputTrack : DOTSTrack
    {
    }
}