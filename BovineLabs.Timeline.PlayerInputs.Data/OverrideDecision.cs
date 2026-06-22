namespace BovineLabs.Timeline.PlayerInputs.Data
{
    public static class OverrideDecision
    {
        public static bool IsActive(OverrideTrigger trigger, bool anyDown, bool anyHeld, bool actionDown, bool actionHeld)
        {
            return trigger switch
            {
                OverrideTrigger.AnyInput => anyDown || anyHeld,
                OverrideTrigger.Action => actionDown || actionHeld,
                _ => false
            };
        }

        public static void Step(bool active, bool currentlyDriving, float idleSeconds, float releaseIdleSeconds,
            float dt, out bool nextDriving, out float nextIdle)
        {
            if (active)
            {
                nextDriving = true;
                nextIdle = 0f;
                return;
            }

            if (!currentlyDriving || releaseIdleSeconds <= 0f)
            {
                nextDriving = currentlyDriving;
                nextIdle = idleSeconds;
                return;
            }

            var accumulated = idleSeconds + dt;
            if (accumulated >= releaseIdleSeconds)
            {
                nextDriving = false;
                nextIdle = 0f;
                return;
            }

            nextDriving = currentlyDriving;
            nextIdle = accumulated;
        }
    }
}
