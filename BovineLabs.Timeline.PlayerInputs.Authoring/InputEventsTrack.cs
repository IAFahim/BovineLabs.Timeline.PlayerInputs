using System;
using System.ComponentModel;
using BovineLabs.Reaction.Authoring.Core;
using BovineLabs.Timeline.Authoring;
using UnityEngine.Timeline;

namespace BovineLabs.Timeline.PlayerInputs.Authoring
{
    [Serializable]
    [TrackClipType(typeof(InputEventsClip))]
    [TrackColor(0.90f, 0.85f, 0.20f)]
    [TrackBindingType(typeof(TargetsAuthoring))]
    [DisplayName("BovineLabs/Player Inputs/Input Events Track")]
    public sealed class InputEventsTrack : DOTSTrack
    {
    }
}