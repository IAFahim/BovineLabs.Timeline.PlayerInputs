using System;
using System.ComponentModel;
using BovineLabs.Timeline.Authoring;
using UnityEngine.Timeline;

namespace BovineLabs.Timeline.PlayerInputs.Authoring
{
    [Serializable, TrackClipType(typeof(InputTransducerClip)), TrackColor(0.2f, 0.6f, 0.9f), TrackBindingType(typeof(InputConsumerAuthoring)), DisplayName("BovineLabs/Player Inputs/Transducer Track")]
    public sealed class InputTransducerTrack : DOTSTrack { }
}