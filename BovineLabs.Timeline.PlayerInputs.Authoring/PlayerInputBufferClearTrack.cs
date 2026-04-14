using System;
using System.ComponentModel;
using BovineLabs.Timeline.Authoring;
using UnityEngine.Timeline;

namespace BovineLabs.Timeline.PlayerInputs.Authoring
{
    [Serializable]
    [TrackClipType(typeof(PlayerInputBufferClearClip))]
    [TrackColor(0.9f, 0.2f, 0.2f)]
    [TrackBindingType(typeof(InputConsumerAuthoring))]
    [DisplayName("BovineLabs/Timeline/Player Inputs/Buffer Clear Track")]
    public sealed class PlayerInputBufferClearTrack : DOTSTrack
    {
    }
}