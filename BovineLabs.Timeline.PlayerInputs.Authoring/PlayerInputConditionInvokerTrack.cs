using System;
using System.ComponentModel;
using BovineLabs.Timeline.Authoring;
using UnityEngine.Timeline;

namespace BovineLabs.Timeline.PlayerInputs.Authoring
{
    [Serializable]
    [TrackClipType(typeof(PlayerInputConditionInvokerClip))]
    [TrackColor(0.2f, 0.6f, 0.9f)]
    [TrackBindingType(typeof(InputConsumerAuthoring))]
    [DisplayName("BovineLabs/Timeline/Player Inputs/Condition Invoker Track")]
    public sealed class PlayerInputConditionInvokerTrack : DOTSTrack
    {
    }
}