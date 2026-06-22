using BovineLabs.Core.Collections;

namespace BovineLabs.Timeline.PlayerInputs.Data
{
    public struct EdgeAccumulator
    {
        private BitArray256 pressed;
        private BitArray256 pendingDown;
        private BitArray256 pendingUp;

        public void Press(byte id)
        {
            pendingDown[id] = true;
            pressed[id] = true;
        }

        public void Release(byte id)
        {
            pendingUp[id] = true;
            pressed[id] = false;
        }

        public void Seed(byte id)
        {
            pressed[id] = true;
        }

        public readonly bool IsPressed(byte id)
        {
            return pressed[id];
        }

        public readonly void Prime(out BitArray256 held)
        {
            held = pressed;
        }

        public void Publish(out BitArray256 down, out BitArray256 up, out BitArray256 held)
        {
            down = pendingDown;
            up = pendingUp;
            held = pressed;
            pendingDown = default;
            pendingUp = default;
        }

        public void Reset()
        {
            pressed = default;
            pendingDown = default;
            pendingUp = default;
        }
    }
}