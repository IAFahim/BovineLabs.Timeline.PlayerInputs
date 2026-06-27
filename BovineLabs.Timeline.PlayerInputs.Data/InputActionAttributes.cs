using System;

namespace BovineLabs.Timeline.PlayerInputs.Data
{
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class InputActionAttribute : Attribute
    {
        public InputActionAttribute()
        {
        }

        public InputActionAttribute(string action)
        {
            Action = action;
        }

        public string Action { get; }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class InputActionDeltaAttribute : Attribute
    {
        public InputActionDeltaAttribute()
        {
        }

        public InputActionDeltaAttribute(string action)
        {
            Action = action;
        }

        public string Action { get; }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class InputActionDownAttribute : Attribute
    {
        public InputActionDownAttribute()
        {
        }

        public InputActionDownAttribute(string action)
        {
            Action = action;
        }

        public string Action { get; }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class InputActionUpAttribute : Attribute
    {
        public InputActionUpAttribute()
        {
        }

        public InputActionUpAttribute(string action)
        {
            Action = action;
        }

        public string Action { get; }
    }
}
