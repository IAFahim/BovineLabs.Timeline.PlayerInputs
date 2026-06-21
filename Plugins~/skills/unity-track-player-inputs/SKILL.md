---
name: unity-track-player-inputs
description: "Master of the BovineLabs.Timeline.PlayerInputs track family — CommandSequenceTrack/Clip (the input->ConditionEvent chain that turns a key/stick combo into a reaction trigger), InputEventsTrack (raw start/end edges of one action -> events), InputBufferTrack (window/clear: what gets recorded into the input history the sequences read), FlowInputTrack (a fake field-driven axis), and InputConsumerAuthoring (the bind target that reads a joined player). Owns the must-have-an-active-Buffer-Window-or-nothing-records trap, the live-probe-vs-buffered-transition rule, Repeatable-without-Consume retrigger, and the route/link resolution. Portable to any project containing the package; worked example from vex-ee. Use when a designer asks \"do X when the player presses/holds this\" or \"fire a fireball motion (236P)\"."
---

# PlayerInputs track-family specialist

## 1. SCOPE

You own the **`BovineLabs.Timeline.PlayerInputs`** timeline track family (package
`com.bovinelabs.timeline.playerinputs`), authoring types in ns
`BovineLabs.Timeline.PlayerInputs.Authoring` (asmdef
`BovineLabs.Timeline.PlayerInputs.Authoring`), runtime data in ns
`...PlayerInputs.Data`. This family is **canonical chain B (Input-Event)** of the
Arvex system: a player presses something → a clip recognizes it → it fires a
`ConditionEvent` at an Essence → Reaction → Action. You author the recognizer
half; the Reaction/Action half belongs to `unity-augment-architecture` (protocol
§6: report a missing prerequisite, never improvise it).

Five members, four tracks + one bind target:
- **`CommandSequenceTrack` / `CommandSequenceClip`** — the core: match an ordered
  combo of input transitions against the consumer's history, emit a ConditionEvent.
- **`InputBufferTrack`** — holds **`InputBufferWindowClip`** (what may be recorded)
  and **`InputBufferClearClip`** (wipe recorded history). The window is the gate the
  sequences depend on.
- **`InputEventsTrack` / `InputEventsClip`** — simplest: fire OnStart/OnEnd events on
  the rising/falling edge of ONE action's axis. No history, no combo.
- **`FlowInputTrack` / `FlowInputClip`** — synthesise a fake movement axis from a grid
  field (ns `...PlayerInputs.Flow.Authoring`; needs the Grid.Influence package).
- **`InputConsumerAuthoring`** — the MonoBehaviour you bind tracks to; it makes a
  scene object a "consumer" that reads one joined player and keeps an input history.

To actually exercise these at runtime you press inputs via **`unity-player-input`**
(the `player_input` tool / authored Input-System C#) — that skill drives the player;
this one authors what listens. Stat/Essence/Reaction setup is other specialists'.

Operate per **`unity-timeline-track-authoring`** (the SubScene open/save/restore
bracket, `PRE|` capture, undo-appendix structure, fresh-load verification — all of
it; this skill restates none of it). Behave per **`unity-agent-protocol`**; drive the
editor per **`unity-cli`**.

## 2. PORTABLE SEMANTICS

True in ANY project containing `BovineLabs.Timeline.PlayerInputs`. Provenance tags
say where a fact was PROVEN, not where it applies. (Verified vex-ee 2026-06 from
package source under `Packages/BovineLabs.Timeline.PlayerInputs/`; no play mode —
source-derived. Runtime effects exist only in play mode.)

### The mental model (read this first)

A **provider** is a joined player's device feed; a **consumer**
(`InputConsumerAuthoring`) subscribes to a provider by `PlayerId` and is what tracks
bind to. Every tick, `InputState` (Down/Held/Up bitmasks per action) is read from the
provider. **History** (`InputHistory` buffer on the consumer) is the append-only ring
of Down/Up *transitions* — **Held is NEVER recorded** (it is true every frame and
would flood the buffer). Command sequences read history; live-state probes read
`InputState`.

**The gate that surprises everyone:** what gets written into history is filtered by
`ActiveBufferMask`, and that mask is **rebuilt from scratch every tick** — it is the
OR of the `AllowedActions` of every *currently-active* `InputBufferWindowClip`
(`ConsumerBufferMaskSystem`). With **no buffer-window clip active, the mask is empty
→ `ConsumerHistorySystem` records nothing → every CommandSequenceClip matches
nothing.** History buffering is OFF by default; you must open a window. (Live-probe
steps, `CommandMode.None`, read `InputState` directly and so still work with no
window — but any Contains/Consume step needs a window open at press time.)

Actions are referenced by `InputActionReference` and resolved to a `byte ActionId`
at bake via the **`MultiInputSettings`** registry (an action not listed there → bake
`LogError`, ActionId 0 — a silent mis-match). All four tracks share this resolution.

### Type facts

| Type | Facts |
|---|---|
| `CommandSequenceTrack` | base `DOTSTrack`, **sealed**, `[TrackClipType(CommandSequenceClip)]`, **`[TrackBindingType(typeof(InputConsumerAuthoring))]`**, `DisplayName "BovineLabs/Player Inputs/Command Sequence Track"`. Bind to the consumer whose history/state the combo reads. |
| `CommandSequenceClip` | `DOTSClip`, `ITimelineClipAsset`, `clipCaps => ClipCaps.None`, **`duration => .5f`** (combos are recognized *while the clip is active*; the clip is a listening WINDOW, not an instant). Bake builds a `CommandBlob` + `CommandSequenceConfig{Blob,RouteEntity}` + empty `CommandSequenceState`. |
| `InputBufferTrack` | base `DOTSTrack`, sealed, TWO clip types `[TrackClipType(InputBufferWindowClip)]` + `[TrackClipType(InputBufferClearClip)]`, `[TrackBindingType(typeof(InputConsumerAuthoring))]`, DisplayName "BovineLabs/Player Inputs/Buffer Track". |
| `InputBufferWindowClip` | `DOTSClip`, caps None, `duration => 1`. Bake → `BufferWindowConfig{ BitArray256 AllowedActions }`. **Empty `AllowedActions` array = ALL 256 actions allowed** (window opens for everything); a non-empty list = ONLY those. |
| `InputBufferClearClip` | `DOTSClip`, caps None, `duration => 1`, **`BufferClearConfig` is `IEnableableComponent`**. Bake → mask from `ActionsToClear`. **Empty list = clear ALL history**; specifics clear only those. Fires on clip ENTER only (`[WithNone(ClipActivePrevious)]` — edge-triggered, once per activation). |
| `InputEventsTrack` | base `DOTSTrack`, sealed, `[TrackClipType(InputEventsClip)]`, **`[TrackBindingType(typeof(TargetsAuthoring))]`** (NOT the consumer — it binds the Targets holder and reaches the consumer through a LINK). DisplayName "BovineLabs/Player Inputs/Input Events Track". |
| `InputEventsClip` | `DOTSClip`, caps None, `duration => 1`. Watches ONE action's `InputAxis` magnitude; rising edge (`|axis|²>0.0001` & was inactive) fires `OnInputStart`, falling edge fires `OnInputEnd`. Edge-detected via `InputEventsState{WasInputActive}`. |
| `FlowInputTrack` | base `DOTSTrack`, sealed, `[TrackClipType(FlowInputClip)]`, `[TrackBindingType(typeof(TargetsAuthoring))]`, DisplayName "BovineLabs/Player Inputs/Flow Input (Fake Axis)". **ns `BovineLabs.Timeline.PlayerInputs.Flow.Authoring`** — different namespace. |
| `FlowInputClip` | `DOTSClip`, **`clipCaps => Blending\|Looping`** (only family clip that blends). Samples a `GridFieldSchemaObject` gradient to write a fake axis for `Action` on the linked consumer. **Requires the Grid.Influence package** (`BovineLabs.Timeline.Grid.Influence`); absent → it won't compile/exist. Treat as optional. |
| `InputConsumerAuthoring` | plain `MonoBehaviour` (NOT DOTSTrack). The bind target. Baker calls `InputConsumerBuilder.Build` → adds `PlayerId`, `ConsumerTag`, `ActiveBufferMask`, `InputHistory` buffer, `InputHistoryLimit`; optionally `Controllable`/`PlayerOverride`/`OverridePolicy`/`OverrideState` and `DirectionConfig`/`DirectionState`. |

### `InputConsumerAuthoring` fields

| Field | Type | Default | Meaning |
|---|---|---|---|
| `PlayerId` | byte | 0 | Which joined player this consumer reads. Match the id you join via `unity-player-input`. |
| `Controllable` | bool | false | If on, a timeline can take over this consumer so authored input overrides the live player. |
| `OverrideTrigger` | `OverrideTrigger` enum | `AnyInput` | (Controllable only) which edge hands control to the override: `Manual`(0) / `AnyInput`(1) / `Action`(2). |
| `ReleaseIdleSeconds` | float | 0.25 | (Controllable only) seconds of input idle before control returns to the player. |
| `HistoryLimit` | ushort `[Range 1,256]` | 64 | Max buffered transitions; oldest evicted first. Clamped to `[1,256]`. |
| `TrackDirection` | bool | false | Quantise a movement axis into an eight-way `Direction` (numpad notation, 5=Neutral) each tick — for motion inputs. |
| `DirectionAction` | InputActionReference | null | The axis quantised when `TrackDirection`. |
| `DirectionDeadZone` | float `[0,1]` | 0.3 | Axis magnitude below this = no direction. |
| `DirectionFacing` | sbyte | 1 | Sign only: ≥0 faces +X, <0 mirrors X (flips Back/Forward). |

### `CommandSequenceClip` fields

The clip holds **`CommandSequenceData[] Sequences`** (evaluated top-to-bottom; first
match fires and, if not Repeatable, completes the clip) and **`RouteTo`**
(`EntityLinkSchema`; the entity that RECEIVES fired events — defaults to the clip
target, i.e. the bound consumer entity, when unset).

Each `CommandSequenceData`: `CommandStepData[] Steps`, `ConditionEventObject
Condition` (the event fired), `int Value=1` (carried amount), `bool Repeatable=true`.

Each `CommandStepData`:

| Field | Type | Meaning |
|---|---|---|
| `Action` | InputActionReference | which action this step matches (resolved to ActionId via MultiInputSettings). |
| `Mode` | `CommandMode` | HOW to read: `None`=live-state probe; Contains/Consume families=match a buffered transition (see table below). |
| `Phase` | `InputPhase` | `Down`(press) / `Held`(sustained, live only) / `Up`(release). |
| `MaxGapTicks` | ushort | Max simulation ticks (frames) allowed between this matched step and the previous one. **0 = unbounded.** This is what makes motion inputs / frame links expressible (236P: each direction within N ticks). |

### `CommandMode` semantics (the recognizer)

`None`(0) probes live `InputState` (the only mode that works with no buffer window,
and the only one valid with `Phase.Held`). The rest scan recorded `InputHistory`:

| Mode | Meaning |
|---|---|
| `Contains`(1) | history holds a matching transition anywhere (does not remove it). |
| `Consume`(2) | as Contains, but marks the entry consumed so it can't re-match (removed from history on fire). |
| `FirstConsume`(3) / `LastConsume`(4) | must be the first / last unconsumed entry. |
| `OrderedContains`(16)/`OrderedConsume`(17)/`OrderedFirstConsume`(18)/`OrderedLastConsume`(19) | same, but each step must appear AFTER the previous matched step in history (true sequence order, via a shared searchIndex). Use the Ordered family for real combos. |
| `NotContains`(32)/`NotFirst`(33)/`NotLast`(34) | succeed when NO matching entry exists / the first / last entry is NOT this — negative lookahead (e.g. "dash without holding block"). |

On a full match: any consumed entries are removed from history, an `EventAmount(seq.Condition, seq.Value)` is routed to `RouteEntity`, and if `Repeat==0` the clip latches `IsCompleted` (fires once per activation); `Repeat==1` re-arms.

### `InputEventsClip` fields

| Field | Type | Default | Meaning |
|---|---|---|---|
| `ReadRootFrom` | `Target` | `Owner` | where to resolve the consumer-link root from on the bound Targets. |
| `ConsumerLink` | `EntityLinkSchema` | null (REQUIRED) | link to the input consumer whose action this watches. Missing → bake LogError, clip skipped. |
| `Action` | InputActionReference | null | the action whose axis edges fire events. |
| `EventRouteTo` | `Target` | `Self` | where the fired events go (`Self` = the bound Targets entity). |
| `EventRouteLink` | `EntityLinkSchema` | null | link used when `EventRouteTo` needs one. |
| `OnInputStart` / `OnInputEnd` | `ConditionEventObject` | null | events fired on rising / falling edge. Null = that edge fires nothing. |

### `FlowInputClip` fields (only if Grid.Influence present)

`Field`(GridFieldSchemaObject, REQUIRED — null → skip), `Bias`(`FlowBias` Descend/Ascend),
`ReadRootFrom`(Target, Owner), `ConsumerLink`(EntityLinkSchema, REQUIRED),
`Action`(the movement action whose axis is replaced — must match what the consumer's
AxisTransform reads), `Gain`(`[0,1]`, default 1), `LocalOffset`(Vector3 sample point).

### Edge cases & traps (each source-proven, vex-ee 2026-06)

- **A CommandSequence clip now AUTO-BUFFERS its own combo actions** (reworked 2026-06).
  At bake the clip records the union of action ids used by its *history-reading* modes
  (`Contains`/`Consume`/`Ordered*` families) into `CommandSequenceConfig.Actions`;
  `ConsumerBufferMaskSystem` ORs that into the consumer's `ActiveBufferMask` while the
  clip is active and not yet completed. **So buffered/combo modes work with NO separate
  `InputBufferWindowClip` track** — the old #1 silent dead-end is gone for a clip's own
  actions. Author an explicit `InputBufferWindowClip` only to record actions BEFORE the
  combo clip is active, or actions no step references (e.g. to make a `Not*` check see an
  action it doesn't itself read — `None`/`Held`/`Not*` actions are deliberately NOT
  auto-buffered, to avoid polluting the shared per-consumer history).
- **`CommandMode.None` is a pure LIVE-STATE probe for every phase** (`Down`=press frame,
  `Up`=release frame, `Held`=while sustained). It never touches the shared history, so it
  can't steal or contaminate another clip's edges — but it only matches when the clip is
  **ACTIVE on the edge frame**. For "fire on press/release", size the clip to span the
  input, or give it `ClipCaps.Looping` (now enabled) and loop it so it listens every
  frame. History-backed matching is only for the explicit combo modes.
- **DON'T put `Phase.Held` on a buffered mode — it can NEVER match** — history records
  Down/Up edges only, never Held; the baker emits a `LogError` AND now neutralises the
  step (fail-closed) so it doesn't uselessly self-buffer. A sustained hold must be a
  `CommandMode.None` live probe (`Phase.Held`).
- **DON'T leave `Repeatable=true` without a TRANSIENT trigger** — a Repeatable sequence
  fires once per frame ALL steps match, so it **re-fires every frame** unless one step's
  truth goes false again next frame: a `Consume`-family step (removes the matched history)
  or a `None` `Down`/`Up` edge (live bit set one frame only). A `None Held` probe or a
  non-consuming `Contains` stays true and spams the event — the baker warns. Pair
  Repeatable with a Consume step or a None edge, or set Repeatable off.
- **DO use the Ordered family for real combos** — plain `Contains`/`Consume` match
  anywhere in history regardless of order; `OrderedConsume`+`MaxGapTicks` is what
  expresses a true timed sequence (down, down-forward, forward + punch = 236P).
- **DO know which track binds what** — Command & Buffer tracks bind
  **`InputConsumerAuthoring`** directly; InputEvents & FlowInput bind
  **`TargetsAuthoring`** and reach the consumer via a `ConsumerLink` schema. Binding
  the wrong component type = the track won't accept the binding / resolves null.
- **DON'T forget the action must be in `MultiInputSettings`** — every clip resolves
  `InputActionReference`→ActionId through that registry; an unlisted action logs an
  error at bake and falls back to ActionId 0 (silently matches the wrong action).
- **DO match `PlayerId`** — the consumer reads exactly its `PlayerId`'s provider; if
  you join the player as id 1 via `unity-player-input` but the consumer is id 0, no
  state arrives and everything is silent (no error).
- **DO note InputBufferClear is edge-triggered & enableable** — fires once on clip
  enter (`[WithNone(ClipActivePrevious)]`), `BufferClearConfig` is an
  `IEnableableComponent`; use it to wipe stale inputs before a fresh combo window
  (e.g. on a parry/clash) so old presses don't satisfy the next sequence.
- **DON'T assume FlowInput exists** — it lives in a different namespace and depends on
  the optional Grid.Influence package. Probe for the type before authoring it; absent
  → report the missing prerequisite, don't substitute.
- **DO know the events route to an Essence** — both Command and InputEvents emit
  `ConditionEvent`s; for a Reaction to see them the `RouteTo`/`EventRouteTo`+link must
  resolve to the entity carrying the `ConditionEventObject`'s buffer (usually the
  player Essence via `Essence Link`). A mis-routed event is a silent no-op.

## 3. DISCOVERY RECIPES

Act only through `unity-cli exec` / `unity-cli console`; never the filesystem; never
play mode; unity-cli Safe Loop on every mutation. Names below are PARAMETERS — discover
them in THIS project; never assume the §5 worked example.

**3.1 Confirm the package + which optional members exist** (else report a missing
prerequisite — protocol §6):
```csharp
string Q(string n,string asm) => System.Type.GetType(n+", "+asm)?.AssemblyQualifiedName ?? "MISSING";
return "cmd="    + Q("BovineLabs.Timeline.PlayerInputs.Authoring.CommandSequenceTrack","BovineLabs.Timeline.PlayerInputs.Authoring")
     + "\nbuffer="+ Q("BovineLabs.Timeline.PlayerInputs.Authoring.InputBufferTrack","BovineLabs.Timeline.PlayerInputs.Authoring")
     + "\nevents="+ Q("BovineLabs.Timeline.PlayerInputs.Authoring.InputEventsTrack","BovineLabs.Timeline.PlayerInputs.Authoring")
     + "\nflow="  + Q("BovineLabs.Timeline.PlayerInputs.Flow.Authoring.FlowInputTrack","BovineLabs.Timeline.PlayerInputs.Authoring")
     + "\nconsumer="+Q("BovineLabs.Timeline.PlayerInputs.Authoring.InputConsumerAuthoring","BovineLabs.Timeline.PlayerInputs.Authoring");
```
`cmd==MISSING` → package absent, stop. `flow==MISSING` is normal (optional).

**3.2 Scene + SubScene(s):** run the unity-cli skill's First Command. Record
`parentScenePath` + each `subScenePath`.

**3.3 PlayableDirector(s) in the SubScene** (read-only additive open, restore parent):
`FindObjectsByType<UnityEngine.Playables.PlayableDirector>(...)` — per director print
hierarchy path, scene.path, playableAsset path. Selection rule (state it in the memory
card): the single director in the chosen SubScene, else the one carrying the project's
timeline-reference authoring; else ask. Zero → protocol §6.

**3.4 Find the InputConsumer (the bind target for Command/Buffer) by COMPONENT:**
```csharp
var cs = UnityEngine.Object.FindObjectsByType<BovineLabs.Timeline.PlayerInputs.Authoring.InputConsumerAuthoring>(
    UnityEngine.FindObjectsInactive.Include, UnityEngine.FindObjectsSortMode.None);
// per consumer: hierarchy path, scene.path, PlayerId, Controllable, HistoryLimit,
// and sibling TargetsAuthoring / EntityLinkSourceAuthoring (publishes a ConsumerLink?).
```
ZERO consumers → a missing prerequisite (a stage/setup specialist must add one — you
bind, you don't create). For InputEvents/FlowInput, find the `TargetsAuthoring` holder
and confirm a `ConsumerLink` schema is published by an `EntityLinkSourceAuthoring`.

**3.5 List the MultiInputSettings actions (action→id you'll author against):**
```csharp
var s = BovineLabs.Core.Authoring.Settings.AuthoringSettingsUtility
    .GetSettings<BovineLabs.Timeline.PlayerInputs.Data.MultiInputSettings>();
if (s == null) return "MISSING_PREREQUISITE|MultiInputSettings not found";
var sb = new System.Text.StringBuilder();
foreach (var nv in s.Keys) sb.AppendLine(nv.Value + " = " + nv.Name);
return sb.ToString();
```
The `InputActionReference` you assign in a clip MUST be one registered here (keys
drift between projects — never assume a remembered id). Also discover the
`ConditionEventObject` assets (`FindAssets("t:ConditionEventObject")`) and
`EntityLinkSchema` assets you'll route to — verify against THIS project.

**3.6 Capture the chosen director's pre-state** (`PRE|`): per
`unity-timeline-track-authoring` — `PRE|playableAsset=<path or null>` and one
`PRE|binding|<i>|<track>|<type>|<bound path+component>` line per `GetOutputTracks()`.

## 4. CANONICAL RECIPES

One logical change per exec block; print `PRE|` before mutating; save inside the
block; verify from a fresh load. Use the SubScene bracket + binding ceremony from
`unity-timeline-track-authoring` verbatim (open additive, SetActiveScene,
`CreateTrack`/`CreateClip`, set clip props via `SerializedObject`, `SetGenericBinding`
the COMPONENT, SaveScene, try/finally restore parent). Below: only the
family-specific authoring. Asset refs (`InputActionReference`, `ConditionEventObject`,
`EntityLinkSchema`, `GridFieldSchemaObject`) are object fields — assign via
`SerializedObject.objectReferenceValue` to the asset loaded by its discovered path.

**Pattern A — "do X when the player taps Z" (one-button → event), with its window.**
The two tracks must share the SAME bound consumer and OVERLAP in time:
```csharp
var bufTrack = timeline.CreateTrack<BovineLabs.Timeline.PlayerInputs.Authoring.InputBufferTrack>(null, "Buffer");
var win = bufTrack.CreateClip<BovineLabs.Timeline.PlayerInputs.Authoring.InputBufferWindowClip>();
win.start = 0; win.duration = 5;            // window OPEN for the whole listening span
// leave AllowedActions empty => all actions recorded; or set the property array to [Z] only

var cmdTrack = timeline.CreateTrack<BovineLabs.Timeline.PlayerInputs.Authoring.CommandSequenceTrack>(null, "Combo");
var clip = cmdTrack.CreateClip<BovineLabs.Timeline.PlayerInputs.Authoring.CommandSequenceClip>();
clip.start = 0; clip.duration = 5;          // listening clip spans the window
// SerializedObject on clip.asset: Sequences[0] = { Steps:[ {Action=Z, Mode=Consume, Phase=Down, MaxGapTicks=0} ],
//   Condition = OnX event asset, Value = 1, Repeatable = false }
//   RouteTo = Essence Link schema (so the player's Essence receives OnX)
director.SetGenericBinding(bufTrack, consumer);   // InputConsumerAuthoring COMPONENT
director.SetGenericBinding(cmdTrack, consumer);   // same consumer
```

**Pattern B — motion input (236P fireball).** One CommandSequence clip, Ordered+gaps,
over an open window. Steps (all `OrderedConsume`, `Phase.Down`, the direction actions
quantised by `TrackDirection` on the consumer, `MaxGapTicks` ~8–12 frames each so the
motion must be continuous): Down → DownForward → Forward → Punch; `Condition=OnFireball`,
`Repeatable=true` WITH the Consume modes (so re-doing the motion re-fires). Requires
`InputConsumerAuthoring.TrackDirection` set with the movement `DirectionAction`.

**Pattern C — raw start/end of one action (no combo).** `InputEventsTrack` bound to the
`TargetsAuthoring` holder; one `InputEventsClip` spanning the active window:
`ConsumerLink`=the consumer link schema, `ReadRootFrom=Owner`, `Action`=the action,
`EventRouteTo=Self` (or a link), `OnInputStart`/`OnInputEnd`=event assets. No buffer
window needed (it reads the live axis, not history).

**Pattern D — clear stale inputs before a fresh window.** `InputBufferClearClip` on an
`InputBufferTrack` at the moment a new combo phase begins (empty `ActionsToClear` =
wipe everything; or list specific actions). Fires once on clip enter; place it just
before a CommandSequence clip so leftover presses don't satisfy the new sequence.

Values are choices, not constants. Always: Command/Buffer bind the consumer;
InputEvents/Flow bind Targets; sequences need an overlapping open window unless every
step is `CommandMode.None`.

## 5. WORKED EXAMPLE (vex-ee) — example environment; rediscover, never assume

- Package present at `Packages/BovineLabs.Timeline.PlayerInputs/`
  (`com.bovinelabs.timeline.playerinputs` 1.0.0). FlowInput depends on the optional
  Grid.Influence package — probe before use.
- Provider/consumer model: `InputRegistry` singleton maps `PlayerId`(byte)→provider
  entity; players join/leave via `PlayerJoined`/`PlayerLeft` singleton buffers; you
  press inputs at runtime through the `player_input` tool / `unity-player-input`.
- Actions are registered in a `MultiInputSettings` asset (a `KSettingsBase`, group
  "Input"); list it via §3.5 — ids are project-specific.
- DOTS systems (for the verification mental model, all in
  `TimelineComponentAnimationGroup`): `ConsumerBufferMaskSystem` rebuilds
  `ActiveBufferMask` each tick → `ConsumerHistorySystem` records masked Down/Up into
  `InputHistory` → `CommandSequenceResetSystem` → `CommandSequenceSystem` matches &
  fires → `InputBufferClearSystem` wipes on clear-clip enter. `InputEventsSystem`
  (LocalSimulation only) handles the edge-event clip.
- Stage/consumer wiring is built by `unity-stage-foundations`/the project setup;
  this skill binds to it. Rediscover the actual scene paths, director, consumer
  GameObject, PlayerId, action ids, event assets, and link schemas in THIS project.

## 6. UNDO APPENDIX

Runtime effects exist only in play mode; the undo scope is the authoring artifacts.
Follow the artifact-inventory / restore-director-FIRST / UNDO-1..4 structure in
`unity-timeline-track-authoring` verbatim. Family specifics:
- Inventory for one §4 run: the created `.playable` (TimelineAsset + the
  Command/Buffer/Events tracks + their clip sub-assets — `DeleteAsset` removes all
  with the file); possibly-created folder(s); the mutated `director.playableAsset`;
  the generic-binding entries added for EACH new track (note: Pattern A adds TWO
  bindings — buffer track AND command track, both to the consumer — restore both).
- ORDER: restore the director (playableAsset + every captured binding) FIRST, THEN
  delete the asset, THEN any other captured scene values. An asset deleted while the
  director points at it leaves a dangling `{fileID:0}`.
- The recipe never edits the consumer, the stage, the MultiInputSettings, or any
  event/link asset — so there is nothing else to revert. If you (wrongly) had to
  touch the consumer's `PlayerId`/`Controllable`, that is a separate captured value.

## 7. VERIFICATION PROTOCOL

Per `unity-timeline-track-authoring` §verification, plus family checks:
1. **Fresh-load asset dump** (separate exec block): load the `.playable`, dump each
   track (type, name) and clip (start/duration, caps). Confirm a CommandSequence clip
   and an OVERLAPPING InputBufferWindow clip on the same-bound consumer exist together
   (the §2 gate). Dump `Sequences`/`Steps` (Action id, Mode, Phase, MaxGapTicks),
   `Condition`, `Repeatable`, `RouteTo`.
2. **Consistency checks**: no buffered-mode step with `Phase.Held`; not
   `Repeatable` with zero Consume-family steps; every `Action` resolves in
   `MultiInputSettings` (re-run §3.5 and cross-check ids); `Condition`/`RouteTo`
   non-null and resolving to an Essence buffer for the event to land.
3. **Binding from a RELOADED SubScene**: Command/Buffer tracks →
   `InputConsumerAuthoring`; InputEvents/Flow → `TargetsAuthoring`. All prior entries
   intact. Confirm the consumer's `PlayerId` matches the player you will drive.
4. **Parent-scene restore** (sceneCount=1, active, not dirty) and **console**
   (`unity-cli console --filter error` clean vs the project baseline). Silence is
   expected, not proof — the family is heavily silent-on-misconfig (§2 traps).
5. **(Optional, if asked to prove end-to-end)**: drive the input via
   `unity-player-input` in play mode and confirm the routed ConditionEvent fires /
   the Reaction triggers — but play-mode proof is that skill's job, not this one's.
