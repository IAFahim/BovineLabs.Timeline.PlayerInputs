// <copyright file="InputConsumerBuilder.cs" company="BovineLabs">

using BovineLabs.Core.EntityCommands;
using Unity.Entities;

namespace BovineLabs.Timeline.PlayerInputs.Data.Builders
{
    public struct InputConsumerBuilder
    {
        public byte PlayerId;

        public InputConsumerBuilder WithPlayerId(byte playerId)
        {
            PlayerId = playerId;
            return this;
        }

        public void ApplyTo<T>(ref T builder)
            where T : struct, IEntityCommands
        {
            builder.AddComponent(new PlayerId { Value = PlayerId });
            builder.AddComponent<InputConsumerTag>();
            builder.AddComponent(new InputSource { Provider = Entity.Null });
            builder.AddComponent(new PlayerMoveInput());
        }
    }
}