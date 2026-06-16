using System;
using System.ComponentModel;
using BovineLabs.Reaction.Authoring.Core;
using BovineLabs.Timeline.Authoring;
using UnityEngine.Timeline;

namespace BovineLabs.Timeline.PlayerInputs.Authoring
{
    [Serializable]
    [TrackClipType(typeof(CommandSequenceClip))]
    [TrackColor(0.20f, 0.55f, 0.90f)]
    [TrackBindingType(typeof(TargetsAuthoring))]
    [DisplayName("BovineLabs/Player Inputs/Command Sequence Track")]
    public sealed class CommandSequenceTrack : DOTSTrack
    {
    }
}