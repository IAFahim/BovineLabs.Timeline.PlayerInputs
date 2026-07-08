using System;
using System.ComponentModel;
using BovineLabs.Reaction.Authoring.Core;
using BovineLabs.Timeline.Authoring;
using UnityEngine.Timeline;

namespace BovineLabs.Timeline.PlayerInputs.Authoring
{
    [Serializable]
    [TrackClipType(typeof(ControlOverrideClip))]
    [TrackColor(0.60f, 0.30f, 0.80f)]
    [TrackBindingType(typeof(TargetsAuthoring))]
    [DisplayName("BovineLabs/Player Inputs/Control Override Track")]
    public sealed class ControlOverrideTrack : DOTSTrack
    {
    }
}
