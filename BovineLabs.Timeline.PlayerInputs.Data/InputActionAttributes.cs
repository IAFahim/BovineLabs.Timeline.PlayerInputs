using System;

namespace BovineLabs.Timeline.PlayerInputs.Data
{
    // Mark a field on an IPlayerInput struct so the source generator projects an input action into it.
    // Binding is by InputActionReference on the generated <T>_Authoring (resolved at bake to a byte id) - there is
    // deliberately NO name-based binding: action names collide across maps and aren't stable identifiers.
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class InputActionAttribute : Attribute
    {
    }

    // Float/float2 axis multiplied by delta time (rate -> per-frame). Use plain [InputAction] for pointer Delta,
    // which is already per-frame travel.
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class InputActionDeltaAttribute : Attribute
    {
    }

    // bool that is true only on the frame the action started.
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class InputActionDownAttribute : Attribute
    {
    }

    // bool that is true only on the frame the action was released.
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class InputActionUpAttribute : Attribute
    {
    }
}
