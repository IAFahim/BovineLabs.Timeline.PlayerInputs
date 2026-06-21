using BovineLabs.Core.Collections;

namespace BovineLabs.Timeline.PlayerInputs.Data
{
    // The pure edge state-machine behind PlayerInputBridge, extracted so the "rebuild Down/Up each frame but
    // keep the latched Held" invariant can be unit-tested without a live Input System. Modelled on Bridge's
    // ButtonState (Down/Pressed/Up) but vectorised over all 256 action ids.
    //
    // Press/Release are driven by the Input System's started/canceled callbacks (which fire for Value/axis
    // actions too, not just buttons). Publish is called once per frame: it emits the edges accumulated since
    // the last call and the latched hold, then consumes the edges. Held is level state and survives across
    // frames until Release; Down/Up are one-frame transients.
    public struct EdgeAccumulator
    {
        private BitArray256 pressed;
        private BitArray256 pendingDown;
        private BitArray256 pendingUp;

        // started callback: a press edge plus latch the hold.
        public void Press(byte id)
        {
            this.pendingDown[id] = true;
            this.pressed[id] = true;
        }

        // canceled callback: a release edge and drop the hold. Fired by the Input System on actual release,
        // and also on focus loss / device removal / action disable - the stuck-key safety polling lacked.
        public void Release(byte id)
        {
            this.pendingUp[id] = true;
            this.pressed[id] = false;
        }

        // Cold start: an action already actuated when we subscribe will not raise started, so latch the hold
        // without emitting a spurious Down edge.
        public void Seed(byte id)
        {
            this.pressed[id] = true;
        }

        // The latched hold for one id. Axis actions reconcile their edges against this each frame (actuated and
        // not pressed -> Press; not actuated and pressed -> Release), so the magnitude crossing IS the edge -
        // reliable for Value and PassThrough alike, where started/canceled phase edges are not.
        public readonly bool IsPressed(byte id)
        {
            return this.pressed[id];
        }

        // Snapshot the latched hold WITHOUT consuming the pending edges. Used at enable to prime CurrentHeld so
        // a provider read before the first Publish sees a coherent hold (no one-frame stale, no orphan Up).
        public readonly void Prime(out BitArray256 held)
        {
            held = this.pressed;
        }

        // Publish this frame's state and consume the edges. A press and release in the same frame both surface
        // (a one-frame tap), and an edge published here lives for the whole consuming frame.
        public void Publish(out BitArray256 down, out BitArray256 up, out BitArray256 held)
        {
            down = this.pendingDown;
            up = this.pendingUp;
            held = this.pressed;
            this.pendingDown = default;
            this.pendingUp = default;
        }

        public void Reset()
        {
            this.pressed = default;
            this.pendingDown = default;
            this.pendingUp = default;
        }
    }
}
