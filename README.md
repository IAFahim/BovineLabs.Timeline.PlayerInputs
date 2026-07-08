# BovineLabs Timeline Player Inputs

A DOTS input package that turns a joined Unity `PlayerInput` into ECS entities and lets a Timeline
drive gameplay from a player's presses, holds, sticks, and motion inputs (fighting-game combos), or
synthesise input to steer a player along a spline / grid flow-field / navmesh path.

This document is the **designer contract**: the rules you must follow for a clip to actually do
something. Most "my clip silently does nothing" reports trace back to one of the traps below.

---

## Mental model

```
Unity PlayerInput ── PlayerInputBridge ──> provider entity (Human slot)   ┐
SyntheticProviderAuthoring ─────────────> provider entity (Synthetic slot)┤
                                                                          ├─ InputRegistry (seat -> {Human, Synthetic})
Flow clips (Spline/Grid/Nav) ───────────> write the Synthetic slot's axes ┘
                                                     │
   consumer entity (InputConsumerAuthoring, carries PlayerId)  ◄── clips resolve this via consumerLink
                                                     │
   Buffer Window clip opens a recording mask ─> InputHistory ring (tick-stamped)
                                                     │
   CommandSequence clip matches the history ─> ConditionEvent ─> your Reaction
   InputEvents clip watches one action's edges ─> ConditionEvents
   AxisTransform clip reads the seat's axis ─> moves/aims a bound "carrot" transform
```

Everything a clip touches is reached through the **consumer**, and the consumer is resolved through an
**EntityLink schema** (`consumerLink`) off the track-bound entity's `Targets`.

---

## The load-bearing rules (read these)

1. **A clip's `consumerLink` must point at the right EntityLink schema.**
   It is the single most fragile wire in the package. A wrong or unassigned schema resolves no consumer
   and the clip is permanently dead with **no error at runtime**. `AxisTransformClip` and
   `NavFlowInputClip` error at bake on a missing link; verify the link on every clip. When a link is
   missing, the debug drawers paint a red **"link miss"** cue at the bound entity (buffer window/clear,
   and now the AxisTransform carrot).

2. **Nothing is recorded unless a Buffer Window (or an active sequence's own mask) is open.**
   `CommandSequence` steps that read history (`Contains` / `Consume`) match against `InputHistory`, and
   `InputHistory` only records while an `InputBufferWindowClip` (or an active sequence clip's
   history-reading step) has that action's bit set in the consumer's `ActiveBufferMask`. **No open
   window ⇒ nothing records ⇒ the combo never fires.** This is the package's #1 trap.

3. **`None` steps live-probe; `Contains`/`Consume` steps read the buffer.**
   A `None` step checks the provider's *current* edge/held state this frame (no window needed). A
   `Contains`/`Consume` step reads the recorded history (window needed). Mixing them is how you author
   "hold X while tapping the 236 motion".

4. **`Repeatable` needs a transient trigger to re-fire.** A repeatable sequence that never releases its
   condition just holds; give it a `Consume` step or an edge so it can re-arm.

5. **Clear happens before match.** An `InputBufferClearClip` on its start frame wipes stale history
   *before* `CommandSequenceSystem` evaluates that frame, so a stale buffered press cannot sneak a combo
   through on the frame you open a fresh window.

6. **Axis-as-button edges use a deadzone.** Analog actions synthesise Down/Up edges for combo history
   only once their magnitude crosses the **Axis Edge Deadzone** on `MultiInputSettings` (default 0.125,
   with re-press hysteresis), so a resting/drifting stick never floods `InputHistory`. Axis *values*
   still stream from a much smaller threshold. Prefer an action-level deadzone processor where you can;
   this is the fallback floor.

---

## Timeline takeover (control authority)

`InputConsumerAuthoring.Controllable` + `ControlOverrideClip` let a timeline take over a seat:

- The registry keeps **two** provider slots per seat: **Human** and **Synthetic**. While a consumer's
  `PlayerOverride` bit is enabled (by a `ControlOverrideClip` or `OverrideTrigger.Manual`), authority-aware
  readers select the **Synthetic** slot; otherwise the **Human** slot.
- Flow clips (Spline / Grid / Nav) write the **Synthetic** slot directly — that is exactly what an
  overridden consumer reads, so a takeover clip + a flow clip together let the timeline "drive the player".
- **`ReleaseIdleSeconds = 0` means never auto-release** (`OverrideDecision` treats `<= 0` as hold-forever).
  Set a positive value to hand control back after that many idle seconds.

---

## Clip → system → requirement table

| Clip | Drives | Requires |
| --- | --- | --- |
| `CommandSequenceClip` | fires a `ConditionEvent` when an input recipe matches | consumer link; an open Buffer Window covering the actions its `Contains`/`Consume` steps read |
| `InputBufferWindowClip` | opens the recording mask (empty = all actions) | consumer link |
| `InputBufferClearClip` | wipes history (all or a selected mask) | consumer link |
| `InputEventsClip` | fires ConditionEvents on one action's start/end edges | consumer link; an event route |
| `AxisTransformClip` | moves/aims a bound carrot transform from the seat's axis (or cursor) | consumer link; a bound carrot with `LocalTransform` |
| `ControlOverrideClip` | enables `PlayerOverride` on the consumer while active | consumer link; `Controllable` on the consumer |
| `FlowInputClip` (grid) | writes a synthetic axis from a grid influence field gradient | consumer link; `InfluenceGridSettings` + field registry |
| `SplineFlowInputClip` | writes a synthetic axis from a spline tangent | consumer link; a `SplinePathAuthoring` registering the referenced spline |
| `NavFlowInputClip` | writes a synthetic axis chasing a hidden navmesh proxy | consumer link; a proxy link (Traverse agent) |

---

## World filters (multiplayer)

Presentation-coupled systems (`AxisTransformSystem`, `InputCommonSystem`, the flow steer systems) run
**LocalSimulation only** — they read a single camera/cursor and drive presentation. Simulation systems
(registry, provider sync, history, sequence, events) run the **Local | Client | Server** backbone.
`GridFlowInputSystem` additionally runs in the `Editor` world.

The bridge publishes input **once per render frame** and `ProviderSyncSystem` **drains** it once per
simulation tick (accumulate-and-drain), so fast taps between two sim ticks are neither lost nor
double-counted. Note the inherent **≥1 render-frame latency**: `InitializationSystemGroup` runs before
`MonoBehaviour.Update`, so the ECS consumes last frame's edges.

---

## Debug config vars

| ConfigVar | Effect |
| --- | --- |
| `inputbuffer.draw-enabled` | force-enable the buffer window/clear drawer |
| `inputbuffer.offset` / `inputbuffer.scale` | world anchor / size of the buffer drawer |
| `playerinput.offset` | world anchor offset for the registry overlay (park it where you're looking) |

---

## Package dependencies

Beyond the standard DOTS + Input System packages, this package's runtime hard-requires the sibling
BovineLabs packages: `com.bovinelabs.core`, `com.bovinelabs.reaction`, `com.bovinelabs.timeline`(+`.core`),
`com.bovinelabs.timeline.entitylinks`, `com.bovinelabs.timeline.physics`,
`com.bovinelabs.timeline.grid.influence`, `com.bovinelabs.traverse` (navmesh flow), and
`com.bovinelabs.bridge` (camera). See `package.json` for the version floors; in this project they resolve
from the shared monorepo forks.
