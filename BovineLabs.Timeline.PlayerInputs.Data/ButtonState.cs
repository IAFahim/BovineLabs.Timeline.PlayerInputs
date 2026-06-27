namespace BovineLabs.Timeline.PlayerInputs.Data
{
    public struct ButtonState
    {
        public bool Down;
        public bool Pressed;
        public bool Up;

        public void Started()
        {
            Down = true;
            Pressed = true;
        }

        public void Cancelled()
        {
            Pressed = false;
            Up = true;
        }

        public void Reset()
        {
            Down = false;
            Up = false;
        }
    }
}
