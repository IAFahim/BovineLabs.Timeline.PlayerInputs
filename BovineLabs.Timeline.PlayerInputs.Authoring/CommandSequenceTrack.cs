using System;
using System.ComponentModel;
using BovineLabs.Timeline.Authoring;
using UnityEngine.Timeline;

namespace BovineLabs.Timeline.PlayerInputs.Authoring
{
    [Serializable]
    [TrackClipType(typeof(CommandSequenceClip))]
    [TrackColor(0.2f, 0.6f, 0.9f)]
    [TrackBindingType(typeof(InputConsumerAuthoring))]
    [DisplayName("BovineLabs/Player Inputs/Command Sequence Track")]
    public sealed class CommandSequenceTrack : DOTSTrack { }
}