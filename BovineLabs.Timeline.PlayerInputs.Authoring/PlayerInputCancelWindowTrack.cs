using System;
using System.ComponentModel;
using BovineLabs.Timeline.Authoring;
using UnityEngine.Timeline;

namespace BovineLabs.Timeline.PlayerInputs.Authoring
{
    [Serializable]
    [TrackClipType(typeof(PlayerInputCancelWindowClip))]
    [TrackColor(0.9f, 0.6f, 0.2f)]
    [TrackBindingType(typeof(InputConsumerAuthoring))]
    [DisplayName("BovineLabs/Player Inputs/Cancel Window Track")]
    public sealed class PlayerInputCancelWindowTrack : DOTSTrack
    {
    }
}