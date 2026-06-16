using System;
using System.ComponentModel;
using BovineLabs.Reaction.Authoring.Core;
using BovineLabs.Timeline.Authoring;
using UnityEngine.Timeline;

namespace BovineLabs.Timeline.PlayerInputs.Authoring
{
    [Serializable]
    [TrackClipType(typeof(InputBufferWindowClip))]
    [TrackClipType(typeof(InputBufferClearClip))]
    [TrackColor(0.90f, 0.75f, 0.20f)]
    [TrackBindingType(typeof(TargetsAuthoring))]
    [DisplayName("BovineLabs/Player Inputs/Buffer Track")]
    public sealed class InputBufferTrack : DOTSTrack
    {
    }
}