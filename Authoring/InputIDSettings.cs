using System;
using BovineLabs.Core.Authoring.Settings;
using BovineLabs.Core.Settings;
using BovineLabs.Reaction.Authoring.Conditions;
using BovineLabs.Timeline.Tracks.Data.PlayerInputs;
using Unity.Entities;
using UnityEngine;

namespace PlayerInputs.PlayerInputs.Authoring
{
    [SettingsGroup("InputID")]
    [SettingsWorld("Server")]
    public class InputIDSettings : SettingsBase
    {
        [SerializeField] public InputIdData[] inputEvents = Array.Empty<InputIdData>();


        public override void Bake(Baker<SettingsAuthoring> baker)
        {
        }
    }

    [Serializable]
    public struct InputIdData
    {
        public PlayerInputType id;
        public ConditionEventObject conditionEventObject;
    }
}
