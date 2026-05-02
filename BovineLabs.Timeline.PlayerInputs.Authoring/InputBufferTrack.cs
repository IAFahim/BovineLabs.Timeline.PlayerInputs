using System;
using System.ComponentModel;
using BovineLabs.Timeline.Authoring;
using UnityEngine.Timeline;

namespace BovineLabs.Timeline.PlayerInputs.Authoring
{
    [Serializable, TrackClipType(typeof(InputBufferWindowClip)), TrackClipType(typeof(InputBufferClearClip)),
     TrackColor(0.9f, 0.2f, 0.2f), TrackBindingType(typeof(InputConsumerAuthoring)),
     DisplayName("BovineLabs/Player Inputs/Buffer Track")]
    public sealed class InputBufferTrack : DOTSTrack
    {
    }
}