# TODO.md

> Full-library audit of `com.bovinelabs.timeline.playerinputs` (all 6 assemblies + SourceGenerator~ + Sample~), 2026-07-07.
> Verified against source on disk, not just a dump. Cross-checked against `REWRITE_DESIGN.md` (the hardened v2 spec) and the
> prior 48/51/23-agent panel campaigns so that **already-refuted findings are not re-reported** (edge-model double-handling,
> hot-leave Up-edge loss, ProviderRetire ordering, CoreCLR settings re-seed, per-frame QueryBuilder, AddComponent-then-query,
> byte-255 reads, FixedString name desync — all previously refuted; left alone).

## Executive Summary

The package is in unusually good shape for its size: the byte-id + `BitArray256` backbone is sound, the pure math is
extracted and well-tested (~140 unit tests), bake-time validation is loud, the focus-loss/refocus edge protocol is correct,
and the debug drawers are genuinely useful. The remaining risk concentrates in four places:

1. **The control-authority feature is inert.** `Controllable`/`PlayerOverride`/`OverridePolicy` are computed every frame by
   `ControlAuthoritySystem`, but **no system reads `PlayerOverride`**. Live player input is never suppressed while a
   timeline drives, and the registry's "human beats synthetic" collision rule actively *prevents* a synthetic provider from
   taking over a human seat. The package's headline "timeline can take over the player" promise does not function end-to-end.
2. **Combo timing is framerate-dependent.** `SimulationTick` increments once per rendered frame, so `MaxGapTicks` — the
   fighting-game motion-input window — means 166 ms at 60 fps, 41 ms at 240 fps, 333 ms at 30 fps. Every authored motion
   input (236P etc.) changes difficulty with the player's framerate.
3. **Ordering traps around the input buffer.** Buffer-clear runs *after* sequence matching (stale inputs can fire a combo on
   the very frame a clear clip opens), and cross-clip sequence priority is entity-index order, not authoring order.
4. **Silent-failure paths designers have already been burned by** (the `consumerLink`-points-at-wrong-schema incident):
   several clips bake a null/wrong `consumerLink` without any error, producing a permanently-dead clip.

Everything else is medium/low: world-filter matrix inconsistencies, one suppressed-safety-system race window on the registry
array, log-spam/Burst-logging convention violations, dead API surface, asmdef hygiene, and a healthy list of missing
integration tests.

## System Inventory

**Data assembly** (`BovineLabs.Timeline.PlayerInputs.Data`) — components + pure logic:
- `InputData.cs` — the core vocabulary: `InputState` (Down/Held/Up `BitArray256`), `InputAxis`, `InputHistory`,
  `PlayerId`, `ProviderTag`/`ConsumerTag`/`PointerProviderTag`/`ProviderRetiring`, `CommandBlob`/`CommandStep`/`CommandMode`,
  `BufferWindowConfig`/`BufferClearConfig`, `AxisTransformConfig/State`, `InputEventsConfig/State`, `SimulationTick`,
  `Direction*`, `HistoryMath`, `DirectionMath`. Also two **dead** components: `PlayerMoveInput`, `InputSource`.
- `InputIdentity.cs` — `InputRegistry` (raw `NativeArray<Entity>[256]`), `PlayerJoined`/`PlayerLeft`, `Controllable`,
  `PlayerOverride`, `OverridePolicy`, `OverrideState`.
- Pure math: `AxisAim`, `AxisBasis`, `AxisLead`, `AxisParentWorld`, `GridProjection`, `HistoryCompaction`,
  `NavFlowInputMath`, `SplineFlowInputMath`, `OverrideDecision`, `EdgeAccumulator`, `ButtonState`.
- `PlayerInputBridge` (MonoBehaviour) — the human-input acquisition layer: InputSystem → `EdgeAccumulator` → provider entity.
- `MultiInputSettings` (+ `.Validation`) — the byte-id action registry (KSettings), GUID-first `TryGet`, editor validation.
- Builders: `InputConsumerBuilder`, `SyntheticProviderBuilder`, `FlowInputBuilder`, `SplineFlowInputBuilder`, `NavFlowInputBuilder`.
- Source-gen support: `IPlayerInput`, `[InputAction*]` attributes, `InputCommon`/`CameraRay`.

**Runtime assembly** (`BovineLabs.Timeline.PlayerInputs`) — systems:
- Frame front-end (InitializationSystemGroup): `SimulationTickSystem` → `InputRegistrySystem`, `ProviderSyncSystem`,
  `ControlAuthoritySystem`, `InputCommonSystem`.
- Timeline group (TimelineComponentAnimationGroup): `SyntheticProviderClearSystem` (OrderFirst) → `GridFlowInputSystem` →
  `SplineFlowInputSystem` → `NavFlowInputSystem` → `PlayerInputProjectionGroup` (generated typed projections) →
  `AxisTransformSystem`; and `ConsumerBufferMaskSystem` → `ConsumerHistorySystem` → `CommandSequenceResetSystem` →
  `CommandSequenceSystem` → `InputBufferClearSystem`/`ProviderRetireSystem`; plus `DirectionInputSystem`, `InputEventsSystem`.
- `CommandMatcher` (pure), `InputAccess` (registry access), `InputEventDispatch` (`EventAmount`, merge/trigger jobs, `InputRouting`).

**Authoring assembly** — one clip+track pair per feature: `AxisTransformClip/Track`, `CommandSequenceClip/Track`,
`InputBufferWindowClip`/`InputBufferClearClip`/`InputBufferTrack`, `InputEventsClip/Track`, `FlowInputClip/Track`,
`SplineFlowInputClip/Track`, `NavFlowInputClip/Track`, `InputConsumerAuthoring`, `SyntheticProviderAuthoring`,
`MultiInputSettingsAuthoringUtility`.

**Debug assembly** — `DebugPlayerInputSystem` (registry/state/axes overlay), `DebugInputBufferSystem` (window/clear/history
drawers, config-vars), `DebugAxisTransformSystem` (carrot gizmos).

**Editor assembly** — `AxisTransformClipEditor` (+ `AxisConceptSvg`/`SvgIconRaster` hover concept art), `ConditionKeyDrawer`,
`PlayerInputTool` (CLI: join/pair/press/tap with SessionState-ledger self-cleaning).

**SourceGenerator~** — `InputProjectionGenerator`: `[InputAction*]` fields on `IPlayerInput` structs → `Bindings` +
`<T>_Map` + `<T>_Authoring/Baker` + `<T>_Projection` system; BLI001–BLI010 diagnostics. Already QA-panel-hardened.

**Sample~** — `PlayerInputsShowcaseBuilder`: 5-column showcase grid scene builder.

**Tests** — 17 test files; excellent pure-math coverage (matcher, history math/compaction, edges, aim/lead/basis/parent,
direction, override decision, registry, seat routing, event accumulation). Near-zero coverage of the *timeline-driven*
systems themselves (mask accumulation, history recording, sequence system end-to-end, axis system, buffer clear ordering).

## Dependency & Flow Map

```
PlayerInput (Unity) ──callbacks/poll──> PlayerInputBridge (MonoBehaviour.Update)
                                            │ publishes Down/Held/Up + axes
                                            ▼
                    provider entity (runtime-created; ProviderTag + PlayerId + InputState + InputAxis)
   SyntheticProviderAuthoring ──bake──> synthetic provider (same shape + SyntheticProviderTag)
                                            │
   InitializationSystemGroup:  SimulationTick++ → InputRegistrySystem (PlayerId→provider[256], join/leave events)
                               ProviderSyncSystem (bridge → InputState/InputAxis)   ControlAuthoritySystem (→ PlayerOverride, UNREAD)
                               InputCommonSystem (cursor ray singleton)
                                            │
   TimelineComponentAnimationGroup:
     SyntheticProviderClearSystem → Grid/Spline/NavFlow (accumulate synthetic axes) → typed projections → AxisTransformSystem
     ConsumerBufferMaskSystem (windows+sequence configs → ActiveBufferMask)
       → ConsumerHistorySystem (provider InputState edges ∧ mask → InputHistory ring, tick-stamped)
       → CommandSequenceResetSystem → CommandSequenceSystem (matcher → consume → ConditionEvent via InputEventDispatch)
       → InputBufferClearSystem (enter-edge clear)   ProviderRetireSystem (destroy retiring)
     DirectionInputSystem (axis → 8-way DirectionState)   InputEventsSystem (action edges → ConditionEvents)
```

Key couplings:
- Everything resolves the consumer through **EntityLink schemas** (`consumerLink`) from the track-bound entity's `Targets` —
  the single most fragile designer-facing wire (a wrong schema = silent dead clip; historical incident).
- `InputRegistry.ProviderByPlayer` is a **raw NativeArray captured by value into jobs** across five systems, always with
  `[NativeDisableContainerSafetyRestriction]` — it escapes the ECS dependency graph entirely.
- History recording is gated by `ActiveBufferMask`, which is rebuilt from scratch every frame from active window clips AND
  active (not-completed) sequence clips' history-reading steps. No window/sequence active ⇒ nothing records — by design,
  and the package's #1 designer trap.
- `CommandSequenceSystem` and `InputEventsSystem` each own a private copy of the same event-dispatch plumbing
  (`NativeParallelMultiHashMapFallback` + unique-key set + `CollectEventKeysJob` + `TriggerEventsJob`).

---

## Critical TODOs

### TODO: Make control authority actually arbitrate input (PlayerOverride has zero readers)

**Priority:** Critical
**Certainty:** Confirmed
**Lens:** Architecture / State
**Files/Systems Involved:** `ControlAuthoritySystem.cs`, `InputIdentity.cs` (`Controllable`, `PlayerOverride`, `OverridePolicy`, `OverrideState`), `InputRegistrySystem.cs`, `InputConsumerAuthoring.cs`, `InputAccess.cs`
**Problem:** `ControlAuthoritySystem` faithfully computes engage/release and toggles the `PlayerOverride` enabled bit — and
nothing consumes it. `grep` over the runtime confirms the only references are the builder (adds it) and the authority system
(writes it). No system suppresses live provider state for overridden consumers, no system swaps in a synthetic provider, and
`AxisTransformSystem`/`CommandSequenceSystem`/`InputEventsSystem` read the registry provider unconditionally. Worse, the
takeover path is *structurally blocked*: `InputRegistrySystem` resolves a human-vs-synthetic collision on the same
`PlayerId` by keeping the human and logging an error — so a synthetic provider cannot even claim the seat to drive it.
**Evidence:** `InputConsumerBuilder.Build` adds `PlayerOverride` (disabled); `ControlAuthoritySystem.AuthorityJob` writes
`driving.ValueRW`; zero read sites. `InputRegistrySystem.cs:64-72` ("keeping the human, ignoring the synthetic"). Prior
session memory records this as a known gap ("timeline PlayerOverride is display-only").
**Why It Matters:** The `Controllable`/`OverrideTrigger`/`ReleaseIdleSeconds` inspector surface promises designers a
takeover/handoff feature that silently does nothing. Designers will author against it, test in isolation ("the debug overlay
shows driven=1!"), and ship broken behavior. This is the package's largest functional hole.
**Suggested Change:** Introduce a real arbitration point. Recommended shape:
1. Registry stores **two** slots per seat: `Human` and `Synthetic` (ends the collision error for the takeover case; keep the
   error only for human-vs-human / synthetic-vs-synthetic duplicates).
2. Add `InputAccess.TryGetState/TryGetAxes` overloads that take the consumer entity + a `PlayerOverride` lookup and select
   the synthetic slot when the override is enabled (fall back to human when no synthetic exists).
3. Route all consumer-side readers (`CommandSequenceSystem`, `InputEventsSystem`, `AxisTransformSystem`,
   `DirectionInputSystem`, `ConsumerHistorySystem`, generated projections) through the authority-aware accessor.
**Implementation Path:**
1. Change `InputRegistry` to `struct ProviderSlots { Entity Human; Entity Synthetic; }` (or two arrays). Update
   `InputRegistrySystem` classification (it already computes `synthetic.HasComponent`).
2. Add the authority-aware accessor to `InputAccess`; thread a `ComponentLookup<PlayerOverride>` + the consumer entity into
   each reader job (they all already have the consumer entity in hand after link resolution).
3. Decide and document semantics per reader: history/sequences should read the *authoritative* provider; `AxisTransform`
   likewise; debug drawers should show both.
4. Keep `OverrideTrigger.Manual` as the external-toggle path; add a timeline clip (e.g. `ControlOverrideClip`) that enables
   `PlayerOverride` while active — that is the missing "timeline takes over" affordance.
**Snippet/Pseudocode:**
```csharp
public static bool TryGetState(in InputRegistry reg, ComponentLookup<InputState> states,
    ComponentLookup<PlayerOverride> overrides, Entity consumer, byte playerId, out InputState state)
{
    var slots = reg.ProviderByPlayer[playerId];
    var overridden = overrides.HasComponent(consumer) && overrides.IsComponentEnabled(consumer);
    var provider = overridden && slots.Synthetic != Entity.Null ? slots.Synthetic : slots.Human;
    ...
}
```
**How to Verify:** New ECSTestsFixture test: consumer with `Controllable` + human provider (pressing A) + synthetic provider
(pressing B), toggle `PlayerOverride`, assert `ConsumerHistorySystem` records B not A while enabled, A after release.
Live: showcase cell with an override clip; use `player_input` CLI to hold a key and confirm the seat ignores it while driven.
**Tradeoffs:** Registry shape change touches every reader (mechanical, ~6 call sites). Alternative (suppress human provider's
`InputState` in `ProviderSyncSystem` when overridden) is smaller but wrong for multi-consumer seats (override is
per-consumer, not per-provider) — reject it.
**Confidence:** High

---

### TODO: Make combo/sequence timing windows framerate-independent

**Priority:** Critical
**Certainty:** Confirmed
**Lens:** Timing
**Files/Systems Involved:** `SimulationTickSystem.cs`, `ConsumerHistorySystem.cs`, `CommandMatcher.WithinWindow`, `InputHistory.Tick`, `CommandStepData.MaxGapTicks`, `DirectionState.ChangedTick`
**Problem:** `SimulationTickSystem` increments once per `InitializationSystemGroup` update — i.e. once per rendered frame.
`InputHistory.Tick` and `MaxGapTicks` are therefore *frame* counts, not simulation ticks, despite the tooltip ("Max
simulation ticks allowed between this step and the previous"). A 236P authored with `MaxGapTicks = 10` gives the player
333 ms at 30 fps, 166 ms at 60 fps, 41 ms at 240 fps. High-refresh players physically cannot perform authored motion inputs;
low-fps players get free lenience. Additionally, at low fps multiple physical transitions collapse into one tick, and
`ConsumerHistorySystem.Emit` writes all Downs before all Ups within a tick, so a physical release→press inside one frame is
recorded as Down,Up (wrong order) — sequences keyed on Up-then-Down cannot match and Down-then-Up can false-match.
**Evidence:** `SimulationTickSystem.cs:22-26` (`tick.Value++` per update, InitializationSystemGroup);
`ConsumerHistorySystem.RecordHistoryJob` stamps `Tick`; `CommandMatcher.WithinWindow` compares raw tick deltas;
`ConsumerHistorySystem` emit order Down words then Up words.
**Why It Matters:** This is a combat-input package (motion inputs, cancel windows, buffers). Framerate-dependent windows are
the classic fighting-game bug class; it will surface as "combos feel impossible on my 144 Hz monitor" and be miserable to
diagnose because the numbers look right in the inspector.
**Suggested Change:** Stamp history entries with *time*, author windows in *seconds* (or milliseconds), and keep the tick
only as a monotonic sequence number:
- `InputHistory { byte ActionId; InputPhase Phase; uint Tick; float Time; }` (or `uint Millis` to stay blittable-compact).
- `CommandStepData.MaxGapSeconds` (float, tooltip in frames-at-60 for designers), baked to millis in `CommandStep`.
- `WithinWindow` compares `matchTime - lastMatchTime > maxGapMillis`.
- Keep `Tick` for the monotonic-order check (`matchTick < lastMatchTick`).
**Implementation Path:**
1. Add the time field to `InputHistory`; `ConsumerHistorySystem` gets `ElapsedTime` from `SystemAPI.Time` (already available).
2. Add `MaxGapSeconds` to `CommandStepData` with `FormerlySerializedAs("MaxGapTicks")` migration: interpret legacy ushort
   values as frames-at-60 (`seconds = ticks / 60f`) in a one-time upgrade path, or keep both fields and prefer seconds.
3. Update `CommandMatcher.WithinWindow` signature; fix the tests (they construct ticks directly — translate to millis).
4. Update `DebugInputBufferSystem` age display (`t-N`) to millis.
**Snippet/Pseudocode:**
```csharp
public static bool WithinWindow(uint matchTick, uint matchMillis, ushort maxGapMillis,
    ref uint lastMatchTick, ref uint lastMatchMillis)
{
    if (lastMatchTick != NoPriorMatch)
    {
        if (matchTick < lastMatchTick) return false;                       // order: still tick-based
        if (maxGapMillis != 0 && matchMillis - lastMatchMillis > maxGapMillis) return false; // window: time-based
    }
    lastMatchTick = matchTick; lastMatchMillis = matchMillis;
    return true;
}
```
**How to Verify:** Unit test: same physical input timings expressed at simulated 30/60/240 fps tick streams produce identical
match results. Manual: `Application.targetFrameRate = 30` vs `240`, perform the showcase 236P cell, confirm identical feel.
**Tradeoffs:** +4 bytes per history entry (256 max entries ⇒ negligible). Data migration for already-authored clips — the
showcase and Mechanics timelines need a sweep. Alternative (drive `SimulationTick` from the fixed step) fixes rate-dependence
but couples combo windows to physics rate and still collapses at render-frame granularity for input *sampling*; time-based is
strictly better here.
**Confidence:** High

---

## High Priority TODOs

### TODO: Buffer-clear must take effect before sequence matching on its start frame

**Priority:** High
**Certainty:** Confirmed
**Lens:** Event / Timing
**Files/Systems Involved:** `InputBufferClearSystem.cs` (`[UpdateAfter(typeof(CommandSequenceSystem))]`), `CommandSequenceSystem.cs`, `ConsumerHistorySystem.cs`
**Problem:** The clear clip's documented intent (showcase: "Clear then Window — wipe stale history, then open a fresh
window") cannot hold on the clear's enter frame: order is mask → record → **match** → clear. Stale buffered inputs survive
into that frame's `CommandSequenceSystem` evaluation and can fire a sequence the designer explicitly tried to guard against.
**Evidence:** `InputBufferClearSystem` is `[UpdateAfter(typeof(CommandSequenceSystem))]`; the clear job runs on the
`ClipActive && !ClipActivePrevious` edge only, i.e. exactly the frame that also evaluates sequences against uncleared history.
**Why It Matters:** "Attack buffered during the previous move fires instantly even though I put a Clear at the start of this
window" is precisely the class of one-frame race that already cost a long debugging session (input-buffer-dormant incident).
It defeats the only tool designers have to fence off stale inputs.
**Suggested Change:** Move the clear before matching: `[UpdateAfter(typeof(ConsumerHistorySystem))]`,
`[UpdateBefore(typeof(CommandSequenceResetSystem))]`. Decide explicitly whether a clear should also wipe *same-frame* fresh
entries (current position after history-record ⇒ yes, it clears this frame's inputs too; if "stale only" is wanted, run it
after `ConsumerBufferMaskSystem` and before `ConsumerHistorySystem` instead — recommended, since a press on the clear frame
is arguably fresh).
**Implementation Path:** Change the two ordering attributes; add an integration test; update the showcase caption if the
same-frame semantics change.
**How to Verify:** ECS test: seed history with a stale Down; activate clear clip + sequence clip on the same frame; assert
the sequence does not fire. Before the fix it fires.
**Tradeoffs:** If any existing content relies on "sequence eats the buffered input, then clear" (unlikely — the showcase
prose says otherwise), its behavior changes. One-line diff otherwise.
**Confidence:** High

### TODO: Give cross-clip CommandSequence evaluation a deterministic, author-controlled priority

**Priority:** High
**Certainty:** Confirmed
**Lens:** State / Designer Safety
**Files/Systems Involved:** `CommandSequenceSystem.GatherJob` (`Clips.Sort()`), `CommandSequenceClip`
**Problem:** Within one clip, sequences evaluate top-to-bottom (documented). Across *clips* sharing a consumer, evaluation
order is `Entity` sort order — effectively creation order, which shifts with scene layout, subscene load order, and entity
recycling (CoreCLR reuses indices). Two clips with `Consume` steps compete for the same history entries in an order the
designer never chose; the "wrong" clip can eat the input.
**Evidence:** `CommandSequenceSystem.cs:171` `Clips.Sort();` then serial evaluation. Sorting exists to make the race
deterministic *per run*, but the key is meaningless to authors.
**Why It Matters:** Combos are routinely split across timelines (Mechanics/<Name>/Timeline pattern). "Dash steals the attack
input, but only in the build, not in the editor" is a production-grade heisenbug.
**Suggested Change:** Bake an explicit priority: add `int Priority` to `CommandSequenceClip` (default 0) into
`CommandSequenceConfig`; `GatherJob` sorts by `(Priority, Entity)` — stable tiebreak retained. Document: lower value wins
first crack at consuming.
**Implementation Path:** Field + bake + a `struct ClipSortKey : IComparable` gather (collect `(priority, entity)` pairs
instead of raw entities, sort, iterate).
**How to Verify:** Test: two clips, both `Consume` on the same action, priorities inverted between runs — the prioritized
one always fires; without priority, assert current entity-order behavior as the documented fallback.
**Tradeoffs:** One more knob; default keeps today's behavior.
**Confidence:** High

### TODO: Error at bake when a clip's consumerLink is missing (silent dead-clip class)

**Priority:** High
**Certainty:** Confirmed
**Lens:** Designer Safety / Validation
**Files/Systems Involved:** `CommandSequenceClip.Bake`, `InputBufferWindowClip.Bake`, `InputBufferClearClip.Bake`, `InputEventsClip.Bake`, `FlowInputClip.Bake`, `SplineFlowInputClip.Bake`
**Problem:** `AxisTransformClip` and `NavFlowInputClip` hard-error when the consumer link schema is unassigned; the six
clips above silently call `EntityLinkAuthoringUtility.BakeRef(baker, null, ReadRootFrom)` and bake a `LinkKey = 0` ref.
At runtime the resolve degrades (resolves the root or nothing), the clip does nothing, and no message ever appears. This is
the exact failure shape of the recorded `InputBufferWindowClip.consumerLink → wrong schema` incident: buffer never recorded,
combo dead, multi-hour debug.
**Evidence:** `CommandSequenceClip.Bake` has no null/`TryGetKey` check on `consumerLink` (contrast `AxisTransformClip.Bake`
lines 1-10 of the method). Same for the buffer/events/flow clips.
**Why It Matters:** The consumer link is the load-bearing wire of every clip in the package; the package's own trap list
calls it out. A missing link must be loud at bake, not silent at runtime.
**Suggested Change:** Shared validation helper in `MultiInputSettingsAuthoringUtility` (or a new `PlayerInputsBakeUtility`):
`RequireConsumerLink(schema, clipName, context)` that logs `Debug.LogError(..., this)` and returns false; every clip baker
calls it and skips baking on failure (mirroring the AxisTransform pattern). Additionally, where feasible, validate that the
schema is actually registered (`EntityLinkAuthoringUtility.TryGetKey`) rather than only non-null.
**Implementation Path:** One static helper; 6 call sites; align the log wording ("Clip will be skipped.") across all clips.
**How to Verify:** Open each showcase timeline, clear a `consumerLink`, re-bake ⇒ one clear error per offending clip
pinging the asset.
**Tradeoffs:** None; strictly additive diagnostics.
**Confidence:** High

### TODO: Stabilize seat identity — playerIndex recycling and the (byte)(-1) wrap (spec C10)

**Priority:** High
**Certainty:** Strongly Likely
**Lens:** State / Edge Case
**Files/Systems Involved:** `PlayerInputBridge.GetPlayerId`, `InputRegistrySystem` duplicate tie-break, `REWRITE_DESIGN.md` §C10/§C11
**Problem:** Two related identity hazards. (a) `GetPlayerId` falls back to `PlayerInput.playerIndex`, which Unity recycles on
leave/rejoin — a newcomer silently inherits the departed player's consumers, overrides, and buffered history. `playerIndex`
can also be `-1` before assignment, and `(byte)(-1)` = 255, colliding with the reserved-sentinel-adjacent top slot. (b) The
same-kind duplicate tie-break in `InputRegistrySystem` is `existing.Index <= entity.Index` — under CoreCLR entity-index
reuse, "first" is not stable across runs (spec C11 explicitly calls for a stable join sequence).
**Evidence:** `PlayerInputBridge.cs:307-312`; `InputRegistrySystem.cs:75`. C10/C11 are marked "Phaseable"/partially done in
the design doc — human-beats-synthetic landed, seat stability did not.
**Why It Matters:** Local coop is a stated hard requirement. Drop-in/drop-out sessions will hand player 2's character (and
any latched input history) to whoever joins next.
**Suggested Change:** Minimum now: clamp/guard `GetPlayerId` (`playerIndex < 0 → warn + 0`; `> 254 → warn + clamp`), and
tie-break duplicates on a monotonically increasing join sequence number stamped on the provider at creation
(`ProviderSeq : IComponentData { uint Value; }` from a static counter reset via `[OnCodeUnloading]`). Full C10 (join-ticket /
`InputUser.id` seat map + clear-overrides-on-`PlayerLeft`) stays a design task — write it against the existing
`PlayerJoined/PlayerLeft` buffers, which are already published but have no consumer reactions.
**Implementation Path:** 1) guard + warn in `GetPlayerId`; 2) `ProviderSeq` on create (bridge + `SyntheticProviderBuilder`);
3) registry tie-break by seq; 4) (bigger) seat-map design per C10.
**How to Verify:** Play-mode test with `PlayerInputManager`: join P0,P1 → leave P0 → join P2; assert P2 does not inherit
P0's consumer state. Unit-test tie-break stability with recycled indices.
**Tradeoffs:** Full C10 is a real design task; the guard+seq portion is an afternoon and removes the sharpest edges.
**Confidence:** High for the hazards; Medium for choosing the full seat-map now.

### TODO: Take InputRegistry's provider array back under dependency tracking

**Priority:** High
**Certainty:** Strongly Likely
**Lens:** Event / Performance (race)
**Files/Systems Involved:** `InputIdentity.cs` (`InputRegistry`), `InputRegistrySystem.cs:99` (`next.CopyTo(current)`), every job that declares `[ReadOnly][NativeDisableContainerSafetyRestriction] NativeArray<Entity> Registry` (`AxisTransformSystem`, `CommandSequenceSystem`, `ConsumerHistorySystem`, `ControlAuthoritySystem`, `DirectionInputSystem`, `InputEventsSystem`, `DebugPlayerInputSystem`)
**Problem:** `ProviderByPlayer` is a persistent `NativeArray` stored *inside* a component and captured by value into jobs.
The ECS dependency graph tracks the `InputRegistry` component, not the array, so the safety system would flag every capture —
which is why all seven sites carry `[NativeDisableContainerSafetyRestriction]`. That attribute doesn't fix the hazard, it
silences the detector: if any reader job from frame N is still in flight when `InputRegistrySystem` runs early in frame N+1
and does `next.CopyTo(current)` on the main thread, the reader observes a torn/mid-update table. Today it "works" because the
timeline-group jobs happen to complete within the frame, but nothing *enforces* that, and one long frame or an added
end-of-frame job breaks the invariant invisibly.
**Evidence:** The attribute appears on every capture site; `CopyTo` runs unfenced on the main thread; no
`state.CompleteDependency()`/component-handle coupling protects the array itself.
**Why It Matters:** Torn registry reads manifest as one-frame wrong-player input — the least reproducible bug class
imaginable in a coop input system.
**Suggested Change:** Replace the embedded array with a `DynamicBuffer<ProviderSlot>(256)` on the registry singleton entity.
Buffers are dependency-tracked; every reader takes a `BufferLookup<ProviderSlot>`/singleton-buffer handle and the seven
`[NativeDisableContainerSafetyRestriction]` annotations get deleted. This also merges cleanly with the dual-slot change from
the authority TODO.
**Implementation Path:** 1) `ProviderSlot : IBufferElementData`; 2) create with `ResizeUninitialized(256)` in OnCreate;
3) mechanical migration of `InputAccess` + 7 systems; 4) delete `OnDestroy` disposal (buffer owns lifetime).
**How to Verify:** Compile with the attribute removals — the safety system itself becomes the verifier (any remaining
untracked access now throws in the editor). Race test is impractical; rely on the safety system.
**Tradeoffs:** Mechanical churn across 7 systems (do it together with the authority/registry reshape to pay the cost once).
Singleton-buffer lookups are equally fast.
**Confidence:** High that the hazard is real; Medium that it has ever fired.

### TODO: Reconcile the WorldSystemFilter matrix across the family

**Priority:** High
**Certainty:** Confirmed
**Lens:** Architecture / Edge Case
**Files/Systems Involved:** `InputEventsSystem` (Local only), `AxisTransformSystem` (Local only — probably right),
`SyntheticProviderClearSystem` (Local only), `GridFlowInputSystem` (Local|Server|Client|**Editor**),
`SplineFlowInputSystem`/`NavFlowInputSystem` (Local only), everything else (Local|Client|Server)
**Problem:** The filters disagree in ways that produce asymmetric behavior:
(a) `CommandSequenceSystem` fires condition events in Client/Server worlds but `InputEventsSystem` — its sibling that fires
start/end edges — is Local-only, so half the event surface disappears in netcode worlds.
(b) `SyntheticProviderClearSystem` is Local-only while `GridFlowInputSystem` runs in Server/Client/Editor: in those worlds
synthetic axis buffers are cleared only by GridFlow's own redundant clear, which runs only when `FlowInputConfig` +
grid singletons exist — otherwise stale synthetic axes persist forever.
(c) GridFlow alone has the `Editor` flag.
**Evidence:** grep of `WorldSystemFilter` across the runtime assembly (output above).
**Why It Matters:** The design doc names Local|Client|Server as "the backbone triple". Divergence means multiplayer behavior
silently loses features and stale-input bugs appear only in non-local worlds — exactly where they're hardest to debug.
**Suggested Change:** Write the intended matrix as a table in `REWRITE_DESIGN.md` (which systems are presentation-coupled
⇒ Local-only: `AxisTransformSystem`, `InputCommonSystem`; which are simulation ⇒ triple: everything else including
`InputEventsSystem` and `SyntheticProviderClearSystem`), then align the attributes. Delete GridFlow's private clear loop
once the clear system covers its worlds (it exists only to patch this hole), and give GridFlow an explicit
`[UpdateAfter(typeof(SyntheticProviderClearSystem))]`.
**Implementation Path:** Attribute edits + delete `GridFlowInputSystem.cs:75-77` + add the ordering attribute.
**How to Verify:** Editor world inspector (Systems window) per world type; a client-world ECS test asserting
`InputEventsSystem` updates.
**Tradeoffs:** `InputEventsSystem` in a server world needs a provider — harmless (query-gated). None otherwise.
**Confidence:** High

### TODO: Defensively dedupe MultiInputSettings.Keys and validate at build time

**Priority:** High
**Certainty:** Strongly Likely
**Lens:** Designer Safety / Validation
**Files/Systems Involved:** `MultiInputSettings.Keys`, `MultiInputSettings.Validation.cs`, KSettings init
**Problem:** `Keys` yields raw action names. Per the package's own C1 analysis, the `KSettingsBase` name→id hashmap
**throws on duplicate leaf names — killing all input at startup**. `OnValidate` errors on duplicates, but an error log does
not stop a designer from saving, and nothing re-checks at build time. One duplicate action name (trivially easy across two
action maps: both have "Move") away from a black-screen-equivalent input outage.
**Evidence:** `MultiInputSettings.cs` `Keys` getter (no dedupe); `MultiInputSettings.Validation.cs` (editor-only, log-only);
REWRITE_DESIGN C1 ("KSettings name→id hashmap THROWS on dup leaf name (kills all input)").
**Why It Matters:** Single-asset misconfiguration with total-outage blast radius and a startup-exception failure mode that
does not point at the asset.
**Suggested Change:** (1) Make `Keys` collision-proof: suffix duplicates deterministically (`"Move (2)"`) so init never
throws — names are DX sugar only, per C1. (2) Add an `IProcessSceneWithReport`/build-preprocess check that fails the build
with the asset path when duplicates/unassigned slots exist. Keep `OnValidate` as the fast feedback.
**Implementation Path:** HashSet in the `Keys` iterator; small `BuildFailedException` preprocessor in the Editor assembly.
**Snippet/Pseudocode:**
```csharp
var seen = new HashSet<string>();
for (var i = 0; i < count; i++)
{
    var name = ResolveName(i);
    while (!seen.Add(name)) name += "'";   // never throw in KSettings init
    yield return new NameValue<byte>(name, (byte)i);
}
```
**How to Verify:** Duplicate an action name in the asset: editor still boots with input working + OnValidate error;
`Ctrl+B` build fails with a pointing message.
**Tradeoffs:** Suffixed display names are mildly ugly in dropdowns — acceptable vs. total outage.
**Confidence:** High

### TODO: Fix registry error logging — per-frame spam, Burst-discarded in players, convention violation

**Priority:** High
**Certainty:** Confirmed
**Lens:** Debugging / Production Readiness
**Files/Systems Involved:** `InputRegistrySystem.ReportDuplicate/ReportSyntheticCollision`, `SplineFlowInputSystem` missing-spline warn
**Problem:** Three stacked issues. (1) The duplicate/collision errors fire **every frame** while the condition holds — a
duplicate seat floods the console at 60 errors/sec, drowning everything else. (2) They're `[BurstDiscard]` `Debug.LogError`,
so in a Burst-compiled player build the diagnostic vanishes entirely — the exact configuration where you need it. (3) The
repo has an explicit Burst logging convention (BLLogger + `LogError512`, never `UnityEngine.Debug` in Burst paths) that this
violates. `SplineFlowInputSystem`'s missing-spline warning already does rate-limiting (3 s) but is `#if UNITY_EDITOR`-only —
players get silence there too.
**Evidence:** `InputRegistrySystem.cs:103-115`; called inside the per-provider loop with no latch;
`SplineFlowInputSystem` warn block.
**Why It Matters:** Misconfiguration diagnostics that spam in dev and disappear in prod are worse than none: they train
designers to ignore the console and leave QA blind in builds.
**Suggested Change:** Thread the BL logger (`SystemAPI.GetSingleton<BLLogger>()`-pattern used across the other Timeline
packages) with `LogError512`, and latch per-slot: keep a `BitArray256` of already-reported slots in the system, cleared when
the slot's collision resolves.
**Implementation Path:** Add `BitArray256 reportedDup/reportedSynth` fields to the system; report only on transition;
replace `Debug.LogError` per the shattered-debug-logging convention.
**How to Verify:** Two providers on one PlayerId: exactly one error on occurrence, one more if it recurs after resolution;
visible in a development player build.
**Tradeoffs:** None.
**Confidence:** High

### TODO: Document and de-risk the bridge's per-render-frame edge lifetime (fixed-step / multi-world consumers)

**Priority:** High
**Certainty:** Strongly Likely (known-deferred item, re-confirmed)
**Lens:** Timing
**Files/Systems Involved:** `PlayerInputBridge.Update`, `ProviderSyncSystem`, any fixed-step or non-default-world consumer
**Problem:** The bridge publishes Down/Up edges once per MonoBehaviour `Update` and `ProviderSyncSystem` copies them once per
world update. That contract is 1:1 only for the default world at render rate. Under NetCode/fixed-step consumption or
multiple worlds, edges are lost (sim steps 0 times in a frame ⇒ the next publish overwrites) or double-observed (sim steps
twice ⇒ same Down seen twice). Also note the inherent one-frame latency: `InitializationSystemGroup` runs before
MonoBehaviour `Update`, so the ECS always consumes *last* frame's edges.
**Evidence:** `PlayerInputBridge.Update` unconditionally `edges.Publish(...)` (consume-on-publish);
`ProviderSyncSystem` copies `bridge.CurrentDown` verbatim. Deferred in the 2026-06 fix pass for exactly this reason.
**Why It Matters:** The package advertises Server/Client world support (filters, spec); the moment someone consumes input in
a fixed-step group, presses start vanishing at high fps ("pressed twice, fired once").
**Suggested Change:** Accumulate-and-drain: bridge accumulates edges into pending sets; `ProviderSyncSystem` (the single
ECS-side consumer) drains them (`bridge.Drain(out down, out up, out held)`), clearing pending only on consumption. Held stays
level-based. Multi-consumer worlds then need one drain owner that fans out — keep `ProviderSyncSystem` as that owner.
Until built, add a loud doc note ("edges are render-frame events; do not consume from fixed-step groups").
**Implementation Path:** 1) Move `Publish` semantics into a `Drain` called by `ProviderSyncSystem` via
`PlayerInputBridgeComponent`; `Update` only accumulates + reconciles. 2) Keep `CurrentDown/Up/Held` as debug mirrors.
3) Test: two `ProviderSyncSystem` updates between bridge updates see the Down exactly once.
**How to Verify:** Harness test driving `Update()`/drain at mismatched cadences (bridge is plain C#; testable without
play mode for the accumulator half).
**Tradeoffs:** Slightly more coupling (system pulls from bridge instead of bridge pushing); that direction is what makes
the cadence correct.
**Confidence:** High on the defect, Medium on urgency (pure render-rate local play never hits it).

---

## Medium Priority TODOs

### TODO: Resolve the CommandMatcher unordered-monotonicity and OrderedLastConsume semantics with the maintainer

**Priority:** Medium
**Certainty:** Risk (intent ambiguity, code behavior Confirmed)
**Lens:** State
**Files/Systems Involved:** `CommandMatcher.WithinWindow` (`matchTick < lastMatchTick` applies to *unordered* modes), `EvaluateOrderedLastConsume`
**Problem:** Two long-deferred semantic questions. (1) `WithinWindow` enforces forward tick progression even for unordered
`Contains`/`Consume` steps — an "unordered" recipe (`[A, B] in any order`) actually requires B's entry to be no older than
A's matched entry. Documented in-file as intended, but it makes `Contains` misleadingly named. (2) `OrderedLastConsume`
scans backward from the end but then sets `searchIndex = i + 1`, which constrains *subsequent* ordered steps to entries
**after the last-matched one** — combined with backward scanning this produces hard-to-predict interactions and has zero
dedicated tests beyond none.
**Evidence:** `CommandMatcher.cs:193-203`, `140-155`; deferred in the 2026-06 pass ("confirm with maintainer before touching").
**Why It Matters:** Designers authoring "any order" steps get order-ish behavior; nobody can currently explain
`OrderedLastConsume` in one sentence, which means nobody should ship content on it.
**Suggested Change:** Decide and write down: either rename modes to match behavior (e.g. `Contains` → `ContainsForward`) or
relax monotonicity for the unordered family. Add a truth-table doc comment per mode + a test per row. If
`OrderedLastConsume` has no shipped use, consider deleting it (grep content first).
**Implementation Path:** Maintainer decision → doc table → tests (`CommandMatcherTests` already has the harness) → optional
rename with `FormerlySerializedAs`-equivalent for the enum (enum values are serialized by number — safe to rename symbols).
**How to Verify:** New tests encode the decided table; existing 14 matcher tests keep passing.
**Tradeoffs:** Renames touch authored assets only via inspector labels (values are numeric) — cheap.
**Confidence:** High that the ambiguity exists; decision needed.

### TODO: Guard axis-as-button edges against stick drift feeding combo history

**Priority:** Medium
**Certainty:** Strongly Likely
**Lens:** Edge Case / Designer Safety
**Files/Systems Involved:** `PlayerInputBridge` (`AxisPublishThresholdSq = 0.0001f`), `ConsumerHistorySystem`
**Problem:** Axis actions synthesize Down/Up edges at |v| > 0.01 — far inside typical stick drift (1–5%). Unless every axis
action has a deadzone processor configured, a drifting pad emits Down/Up chatter that (with an open buffer window on that
action) floods `InputHistory`, evicting real entries (`HistoryLimit` default 64) and false-matching direction steps.
**Evidence:** `PlayerInputBridge.cs:14` threshold; `Update` reconcile; history recording is mask ∧ edge with no magnitude
filter of its own.
**Why It Matters:** "Combo randomly whiffs only on Dave's controller" — eviction-by-chatter is invisible without the debug
drawer.
**Suggested Change:** Raise the synthetic-edge threshold to a real deadzone (configurable on `MultiInputSettings`, default
~0.125 magnitude ⇒ `0.015625f` lengthsq) while keeping the *publish* threshold small (axes should still stream fine values);
i.e. split "actuated for edge purposes" from "worth publishing". Document that action-level deadzone processors are still
the preferred fix.
**Implementation Path:** Second constant + config field; only the Press/Release reconcile uses the deadzone.
**How to Verify:** Unit-style: feed 0.05-magnitude values through the reconcile logic (extract to a testable helper) —
no edges; 0.2 ⇒ edges.
**Tradeoffs:** A deliberately-feather-touch action would need the config lowered; default matches platform conventions.
**Confidence:** Medium-High (depends on projects' processor hygiene).

### TODO: Decide InputEvents level-vs-edge semantics on clip activation (and the Held fallback)

**Priority:** Medium
**Certainty:** Confirmed behavior, Risk on intent
**Lens:** Event / Designer Safety
**Files/Systems Involved:** `InputEventsSystem.GatherJob`, `InputEventsClip`
**Problem:** `InputEventsState.WasInputActive` resets to false on clip enter, and `hasInput` is level-based (axis magnitude
or `Held` bit). Consequence: if the button is already held when the clip activates, `OnInputStart` fires immediately on
frame 1 — a level-trigger masquerading as an edge event. For a charge attack ("fires on press") a player holding the button
through a window boundary gets a free trigger.
**Evidence:** `InitJob` (`WasInputActive = false` on enter) + `risingEdge = hasInput && !state.WasInputActive`.
**Why It Matters:** Subtle content bug: designers reason in press/release edges; the clip delivers "held at window start"
as a press.
**Suggested Change:** Add a clip toggle `TriggerIfAlreadyHeld` (default true = current behavior, documented). When false,
seed `WasInputActive = hasInput` on the enter frame instead of false (one extra branch in `GatherJob` on
`!ClipActivePrevious`, or move the seeding into `InitJob` with registry access).
**Implementation Path:** Config bit in `InputEventsConfig`; seed-on-enter path; tooltip explaining both modes.
**How to Verify:** Test: Held bit set before activation → with the flag off, no `OnInputStart` until a fresh press.
**Tradeoffs:** None; default preserves behavior.
**Confidence:** High

### TODO: Extract the duplicated event-dispatch plumbing shared by CommandSequenceSystem and InputEventsSystem

**Priority:** Medium
**Certainty:** Confirmed
**Lens:** Architecture / Maintainability
**Files/Systems Involved:** `CommandSequenceSystem`, `InputEventsSystem`, `InputEventDispatch.cs`
**Problem:** Both systems hand-roll the identical pipeline: persistent `NativeParallelMultiHashMapFallback<Entity,EventAmount>`
+ `NativeList<Entity>` + `ConditionEventWriter.Lookup/SingletonData` + per-frame unique-key hash set + `CollectEventKeysJob`
+ `TriggerEventsJob` + `Apply/Clear` chaining + identical `OnDestroy` disposal. ~80 duplicated lines that must be kept in
lockstep (the fixed-64-overflow + Clear-race fix documented in `CommandSequenceSystem` comments has to be remembered twice).
**Evidence:** Side-by-side `OnCreate/OnUpdate/OnDestroy` of both systems.
**Suggested Change:** A small `struct ConditionEventDispatch` in `InputEventDispatch.cs` owning the containers and exposing
`Create(ref SystemState)`, `Dispose()`, `Writer AsWriter()`, `JobHandle Flush(ref SystemState, NativeParallelHashSet<Entity> keys, JobHandle dep)`.
Both systems shrink to gather-job + one Flush call.
**Implementation Path:** Mechanical extraction; no behavior change; keep both systems' schedules identical to today.
**How to Verify:** Existing behavior; showcase events still fire; diff of scheduled job graph unchanged.
**Tradeoffs:** None beyond review time.
**Confidence:** High

### TODO: Remove dead API surface and stale assembly plumbing

**Priority:** Medium
**Certainty:** Confirmed
**Lens:** Architecture / Maintainability
**Files/Systems Involved:** `InputData.cs` (`PlayerMoveInput`, `InputSource`), `BufferClearConfig : IEnableableComponent`, `HistoryMath.Plan`, `BovineLabs.Timeline.PlayerInputs.Data/AssemblyInfo.cs`, all 5 asmdefs
**Problem:** Accumulated cruft that misleads readers:
- `PlayerMoveInput` and `InputSource` are declared and referenced nowhere (grep-verified). Dead vocabulary invites misuse.
- `BufferClearConfig` implements `IEnableableComponent` but nothing ever toggles the bit — readers will assume a
  disable-path exists.
- `HistoryMath.Plan` is used only by its own tests; the runtime calls `ClampLimit/EvictCount/OverflowCount` directly.
- `Data/AssemblyInfo.cs` grants `InternalsVisibleTo` to five assemblies that no longer exist (`"PlayerInputs"`,
  `"PlayerInputs.Authoring"`, …) — the real assemblies get no internals access, so the grants are pure noise.
- Four asmdefs reference a nonexistent assembly `"PlayerInputs.Data"` (silently ignored by Unity) and carry unused heavy
  references: `Unity.AppUI(.MVVM/.Navigation)`, `BovineLabs.Anchor`, `Unity.Physics`, `Unity.Entities.Graphics` are not
  used by any source file in the runtime or data assemblies (grep-verified — only `AnchorLinkKey` false-positives).
**Evidence:** greps above; asmdef contents.
**Why It Matters:** Unused asmdef refs lengthen compile graphs and can drag modules into player builds; phantom
InternalsVisibleTo/references break the next person's mental model; dead components get baked into someone's authoring
"because it looked official".
**Suggested Change:** Delete `PlayerMoveInput`/`InputSource` (or move to the generator sample if they were exemplars);
drop `IEnableableComponent` from `BufferClearConfig` **or** use it (a clear clip that re-fires by re-enabling would actually
be useful — decide); fix `AssemblyInfo` names to the `BovineLabs.Timeline.PlayerInputs.*` set; prune asmdef refs; remove
`HistoryMath.Plan` or make `ConsumerHistorySystem` call it (single source of truth — mild preference for the latter since
tests already pin its invariants).
**Implementation Path:** Grep-verify each deletion against the whole superproject (other packages/Assets may reference);
compile per-assembly with the documented `dotnet build` recipe.
**How to Verify:** Full editor compile + package tests green.
**Tradeoffs:** If any downstream project consumed `PlayerMoveInput`, deletion is breaking — verify first (submodule is a
shared fork).
**Confidence:** High

### TODO: Main-thread sync points in the flow-input systems — contain and document

**Priority:** Medium
**Certainty:** Confirmed
**Lens:** Performance
**Files/Systems Involved:** `GridFlowInputSystem`, `SplineFlowInputSystem`, `NavFlowInputSystem` (`state.CompleteDependency()` + main-thread foreach), `GridFlowInputSystem` per-slot `WriterDependency.Complete()`
**Problem:** All three synthetic-axis writers start with `CompleteDependency()` and iterate on the main thread; GridFlow
additionally completes every field-registry writer handle. Each is an unconditional sync point in the hot timeline group,
every frame those clips are active. Fine at showcase scale; a wall as clip counts grow, and it partially defeats the
carefully-jobified systems around them.
**Evidence:** `GridFlowInputSystem.OnUpdate:48-69`; same pattern in the siblings.
**Why It Matters:** Performance ceiling + it sets the pattern future flow systems will copy.
**Suggested Change:** Near-term: accept but document *why* (field registry's front/back buffers + `DynamicBuffer` writes to
a shared provider entity make jobifying nontrivial), and gate the per-slot Complete loop to only the fields actually
referenced by active clips. Long-term: convert to `IJobEntity` accumulating into a per-action `NativeParallelHashMap`
keyed by (provider, actionId), applied in one follow-up job — removes both the sync and the O(n) linear `Accumulate` scans.
**Implementation Path:** Documentation now; jobification when a profile shows it (add a profiler marker per system so the
cost is visible — they currently have none).
**How to Verify:** Profiler capture with 10+ active flow clips before/after.
**Tradeoffs:** Jobifying the shared-buffer accumulation needs care (three writers, one buffer) — that's exactly why it
should wait for a profile.
**Confidence:** High on the facts, Medium on urgency.

### TODO: NavFlow proxy pathfinding leaks on hard clip teardown

**Priority:** Medium
**Certainty:** Strongly Likely
**Lens:** Full System Flow / Edge Case
**Files/Systems Involved:** `NavFlowInputSystem` exit path (`WithDisabled<ClipActive>` + `WithAll<ClipActivePrevious>`)
**Problem:** The proxy's `IsPathfinding` is disabled only on the clean deactivate *edge*, which requires the clip entity to
still exist for one frame with `ClipActivePrevious`. If the timeline/director entity is destroyed while the clip is active
(scene unload of the director but not the proxy, timeline killed by gameplay), the hidden Traverse proxy keeps pathfinding
toward its last destination forever.
**Evidence:** Exit query shape; no `ICleanupComponent`/teardown path exists for the proxy link.
**Why It Matters:** Invisible CPU drain + a proxy that "arrives" somewhere and may re-trigger downstream logic; classic
lifecycle-hole per the package family's own Physics-track lessons (Active*-enable vs disable-at-end root cause).
**Suggested Change:** Same medicine the Physics package took: a `DisableAbsentJob`-style sweep — track proxies driven this
frame (or add a `NavFlowDriven : ICleanupComponentData` on the proxy) and disable `IsPathfinding` for any proxy whose
driving clip vanished.
**Implementation Path:** Cleanup component on first drive; a second query (`NavFlowDriven` && no live driving clip → disable
+ remove). Alternatively piggyback on `LifeCycle` teardown if the proxy is owned by the same subscene.
**How to Verify:** Play-mode: destroy the director mid-clip; assert proxy `IsPathfinding` disabled next frame.
**Tradeoffs:** Cleanup components add a structural change on teardown — negligible at these counts.
**Confidence:** Medium-High (destruction-order dependent; verify in editor first).

### TODO: History eviction can starve multi-step combos when a window records everything

**Priority:** Medium
**Certainty:** Strongly Likely
**Lens:** Designer Safety / Edge Case
**Files/Systems Involved:** `InputBufferWindowClip` (empty = ALL 256 actions), `InputConsumerAuthoring.HistoryLimit` (default 64), `ConsumerHistorySystem` eviction
**Problem:** The convenient default — empty `AllowedActions` = record *all* actions — combined with a modest `HistoryLimit`
means mashy periods (movement axis chatter counts: axis actuation edges are recorded too) evict the earlier steps of a
motion input before the final press arrives. The combo "sometimes" fails under exactly the conditions players mash hardest.
**Evidence:** `InputBufferWindowClip.Bake` all-bits mask; `RecordHistoryJob` evicts oldest on overflow; default limit 64.
**Why It Matters:** Nondeterministic-feeling combo drops with no error anywhere; the debug drawer shows it (`hist 64/64`)
but only if you know to look.
**Suggested Change:** (1) Bake-time warning on `InputBufferWindowClip` when `AllowedActions` is empty *and* any
`CommandSequenceClip` on the same timeline uses multi-step sequences: suggest restricting the window to the actions the
sequences read. (2) Runtime: `DebugInputBufferSystem` already shows `hist n/limit` — color it red at the cap. (3) Docs: the
trap list entry.
**Implementation Path:** The bake warning needs only the clip's own data (warn when empty mask, phrased as guidance);
drawer color tweak is two lines.
**How to Verify:** Showcase: open an ALL window, wiggle the stick for 3 s, attempt 236P — observe eviction in the drawer.
**Tradeoffs:** Warning may be noisy for legitimately-broad windows — make it `LogWarning`, not error.
**Confidence:** High on mechanics, Medium on real-world frequency.

### TODO: Unify clip-baker skip semantics and messages

**Priority:** Medium
**Certainty:** Confirmed
**Lens:** Designer Safety / Maintainability
**Files/Systems Involved:** all clip `Bake` methods in the Authoring assembly
**Problem:** Three inconsistent conventions for the same failure: (a) hard-skip with "Clip will be skipped." (`FlowInputClip`,
`SplineFlowInputClip`, `NavFlowInputClip`); (b) hard-skip *without* saying so (`AxisTransformClip` missing ConsumerLink);
(c) bake-anyway-with-sentinel (`AxisTransformClip` missing Action, `CommandSequenceClip` unresolved steps, `InputEventsClip`).
Sentinel-baked clips still consume a track slot and evaluate every frame doing nothing. Also `InputEventsClip.Bake` calls
`context.Baker.DependsOn(OnInputStart)` with a possibly-null argument — verify `IBaker.DependsOn(null)` is a no-op in the
current Entities version (it historically is, but it's an undocumented reliance).
**Evidence:** Compare the six `Bake` methods.
**Suggested Change:** Pick the rule: *link missing ⇒ skip clip loudly; action unresolved ⇒ bake with sentinel + error*
(current majority), write it in a comment on a shared helper, and align messages ("… Clip will be skipped." vs "… the clip
does nothing."). Null-guard the `DependsOn` calls.
**Implementation Path:** Shared helper from the consumerLink TODO covers most of it.
**How to Verify:** Bake each clip with each field nulled; every failure prints one clear, consistent message.
**Tradeoffs:** None.
**Confidence:** High

### TODO: Same-tick Down/Up ordering artifact in history (frame-collapse)

**Priority:** Medium
**Certainty:** Confirmed
**Lens:** Timing / Low FPS
**Files/Systems Involved:** `ConsumerHistorySystem.RecordHistoryJob` (emits all Downs, then all Ups), `EdgeAccumulator`
**Problem:** Within one tick, history always records Down entries before Up entries regardless of physical order. A
release→press inside one frame (fast double-tap at low fps, or lag spike) is recorded as Down,Up — an Up-then-Down sequence
can never match across a frame collapse and a Down-then-Up sequence can false-match. `EdgeAccumulator` cannot represent
multiple transitions per frame at all (press→release→press = one Down + one Up + held).
**Evidence:** `RecordHistoryJob.Execute` emit order; `EdgeAccumulator` single pending bit per direction.
**Why It Matters:** Only bites under frame collapse — which is exactly the low-FPS lens this package claims to care about
(buffered inputs exist *for* laggy frames).
**Suggested Change:** Cheap improvement: when both Down and Up are pending for an action in the same tick, order them by
the *current held state* (held ⇒ the press came last ⇒ emit Up,Down; not held ⇒ Down,Up). Full fix (per-transition
timestamped queue in the bridge) belongs with the accumulate-and-drain TODO.
**Implementation Path:** In `RecordHistoryJob`, compute `both = downFiltered & upFiltered`; emit the both-set specially
using `state.Held`.
**How to Verify:** Unit test on the emit helper with synthetic InputState (down∧up, held vs not).
**Tradeoffs:** Still can't represent 3+ transitions/frame — acceptable; document.
**Confidence:** High on mechanics, Medium on player-visible impact.

---

## Low Priority TODOs

### TODO: Naming and file-hygiene sweep

**Priority:** Low  **Certainty:** Confirmed  **Lens:** Maintainability
**Files/Systems Involved:** `Flowinputtrack.cs`, `Splineflowinputtrack.cs`, `Flowinputbuilder.cs`, `Splineflowinputbuilder.cs`, `Flowinputconfig.cs`, `Splineflowinputconfig.cs` (lowercase filenames); `AxisTransformClip.ConsumerLink`/`AnchorLink` (PascalCase) vs every other clip's `consumerLink`/`eventRouteLink` (camelCase w/ FormerlySerializedAs); `CommandSequenceClip.duration => .5f` vs `1` elsewhere; `SyntheticProviderTag` declared inside `Flowinputconfig.cs` rather than `InputData.cs`.
**Problem/Why:** Inconsistent casing breaks muscle memory and greps; the serialized-name split means future
`FormerlySerializedAs` churn; misplaced type declarations slow discovery.
**Suggested Change:** Rename files to PascalCase (git mv, meta GUIDs preserved); pick camelCase+FSA for all clip link fields
(AxisTransform is the odd one out — add FSA when renaming); move `SyntheticProviderTag` next to `ProviderTag`.
**How to Verify:** Compile + showcase timelines still bind (serialization preserved via FormerlySerializedAs).
**Confidence:** High

### TODO: WithinWindow uint sentinel micro-hole

**Priority:** Low  **Certainty:** Confirmed  **Lens:** Edge Case
**Files/Systems Involved:** `CommandMatcher.WithinWindow` (`matchTick == NoPriorMatch ? NoPriorMatch - 1 : matchTick`)
**Problem:** An entry recorded at tick `uint.MaxValue` (828 days at 60 fps, or a future tick-source change) aliases the
no-prior-match sentinel. The time-based-window TODO removes the need for this dance; otherwise switch the sentinel to a
separate bool.
**Suggested Change:** Fold into the Critical timing rework: `bool hasPrior` instead of a magic tick.
**Confidence:** High (impact ~zero today)

### TODO: Debug drawer gaps

**Priority:** Low  **Certainty:** Confirmed  **Lens:** Debugging
**Files/Systems Involved:** `DebugInputBufferSystem` (`RequireForUpdate<BufferWindowConfig>`), `DebugPlayerInputSystem` (managed `RenderJob` reads registry raw)
**Problem:** A scene containing only clear clips draws nothing (the require gate is window-only); the player-input overlay
renders at hardcoded world-origin coordinates (fine for the showcase, useless inside a real level); `RenderJob` shares the
untracked-registry capture from the High registry TODO.
**Suggested Change:** `RequireForUpdate<Any(BufferWindowConfig, BufferClearConfig)>` (two-query `RequireAnyForUpdate`);
add the existing `Offset` config-var pattern to `DebugPlayerInputSystem`; registry fix arrives with the buffer migration.
**Confidence:** High

### TODO: package.json dependency floors are stale

**Priority:** Low  **Certainty:** Confirmed  **Lens:** Production Readiness
**Files/Systems Involved:** `package.json` (`com.unity.entities 1.3.0`, `unity 6000.3`, missing deps)
**Problem:** The project runs Unity 6000.7-era Entities; the manifest also omits packages the asmdefs hard-require
(`com.unity.timeline`, movement/grid/physics siblings, `com.bovinelabs.bridge`), so a standalone consumer gets compile
errors instead of a resolver message. Keyword list says "physics" but not "input".
**Suggested Change:** Align floors with the actually-tested versions; add the missing dependencies (or document the
sibling-package requirement explicitly in a README); fix keywords.
**Confidence:** High

### TODO: ConditionKeyDrawer cache thrash

**Priority:** Low  **Certainty:** Confirmed  **Lens:** Editor Performance
**Files/Systems Involved:** `ConditionKeyDrawer.AssetPostprocessor`
**Problem:** Any imported `.asset` nukes the cache; the next drawer repaint re-runs `AssetDatabase.FindAssets` over all
ConditionEventObjects. Noticeable in big projects during import storms.
**Suggested Change:** Only invalidate when an imported/deleted path actually resolves to a `ConditionEventObject`
(cheap `AssetDatabase.GetMainAssetTypeAtPath` check), and only remove/add the touched entries.
**Confidence:** High

### TODO: Exclude reserved id 255 from the all-actions window mask

**Priority:** Low  **Certainty:** Confirmed  **Lens:** Validation
**Files/Systems Involved:** `InputBufferWindowClip.Bake` (`for i in 0..255 mask[i]=true`)
**Problem:** Bit 255 is the unresolved-action sentinel; setting it in the ALL mask is harmless today (nothing emits 255) but
contradicts the "byte 255 is reserved" invariant the validators enforce elsewhere.
**Suggested Change:** Loop to `MultiInputSettings.MaxActions` (255 exclusive). One character of intent.
**Confidence:** High

### TODO: Package README for designers

**Priority:** Low  **Certainty:** Confirmed  **Lens:** Production Readiness
**Problem:** The package has a hardened internal design doc and a showcase, but no README stating the designer contract:
the must-have-a-Buffer-Window rule, live-probe (`None`) vs buffered (`Contains/Consume`) semantics, Repeatable-needs-a-
transient-trigger, the consumerLink schema requirement, the clear-vs-match ordering, `OverrideTrigger` semantics
(including `ReleaseIdleSeconds = 0` = never release — currently undocumented), and the world-filter matrix.
**Suggested Change:** One `README.md` distilling the trap list + a table of clips → systems → requirements. Much of the
text already exists in tooltips and the showcase captions — consolidate.
**Confidence:** High

---

## Designer Safety TODOs

(Aggregated view — items detailed above are referenced, new small items listed in full.)

1. **consumerLink bake validation everywhere** — see High TODO. The single highest-leverage designer guard in the package.
2. **Duplicate action names cannot be allowed to reach KSettings init** — see High TODO (dedupe + build gate).
3. **Empty-window + multi-step-sequence eviction warning** — see Medium TODO.
4. **`ReleaseIdleSeconds` tooltip must state `0 = never release`** (`InputConsumerAuthoring`; `OverrideDecision.Step` treats
   `<= 0` as hold-forever). One tooltip edit. *Certainty: Confirmed.*
5. **`OverrideTrigger.Manual` needs a pointer to how it's driven** — nothing in the package toggles `PlayerOverride`
   manually today (see Critical authority TODO); until the clip exists the tooltip promises a mechanism with no handle.
6. **`InputConsumerAuthoring.PlayerId` has no hint of the seat contract** — add tooltip: "must match the joined player's
   index / PlayerIdOverride of the bridge" and (post-C10) the seat semantics.
7. **`MaxGapTicks` tooltip is wrong** (says simulation ticks; means frames) — fix wording *now* even before the timing
   rework, because designers are authoring against it today.
8. **`CommandSequenceClip` Held+buffered-mode error already exists — good** — extend the same bake-error pattern to
   `Steps.Length == 0` sequences (currently baked and skipped silently at runtime: `if (seq.Steps.Length == 0) continue;`).
9. **`InputEventsClip` with both events null** — bakes a config that can never do anything; warn.
10. **Showcase**: the builder silently no-ops several cells when schemas/events are missing (`LoadSchema` returns null →
    NREs later in `MakeConsumer` when `consumerLink` null is assigned to arrays — actually tolerated — but
    `EnsureFolders`/binding failures surface as broken cells). Add a preflight assert listing missing assets before building.

## Validation & Guard TODOs

- **Bake-time:** consumerLink/eventRouteLink null checks (High TODO); empty-sequence error; both-events-null warning;
  window-ALL advisory; unify skip messages (Medium TODO).
- **Build-time:** MultiInputSettings duplicate/unassigned gate (High TODO); optionally scan timelines for
  `CommandSequenceClip`s whose timeline has no `InputBufferTrack` window covering their history-reading steps — the #1 trap,
  checkable statically per-timeline (steps read history ⇒ some window/sequence mask must open; sequence configs self-open
  via `config.Actions`, so the real check is: history-reading steps exist AND `HistoryLimit == 0`-style misconfig — start
  with a per-timeline advisory, refine with usage).
- **Runtime:** registry duplicate-seat latched errors via BLLogger (High TODO); `GetPlayerId` range guard (High TODO);
  authority-aware access guards once arbitration lands (Critical TODO); keep the existing silent-return style for per-frame
  link misses (correct — the debug drawer surfaces them) but ensure *every* drawer shows the link-miss state the way
  `DebugInputBufferSystem` does (`DebugAxisTransformSystem` currently draws nothing on miss — add the red "link miss" cue).
- **Editor:** MultiInputSettings.Keys defensive dedupe (High TODO); OnValidate already solid.

## Timing / Physics / Animation TODOs

1. **Framerate-independent windows** — Critical TODO #2 (the big one).
2. **Buffer-clear ordering** — High TODO.
3. **Same-tick Down/Up collapse ordering** — Medium TODO.
4. **Bridge cadence vs fixed-step consumers + inherent 1-frame latency** — High TODO. Note the latency explicitly in docs:
   `InitializationSystemGroup` reads *last* frame's bridge publish; input→carrot latency is ≥1 render frame + timeline-group
   position in the frame. For a combat game this is fine but must be a *known* number, not a discovered one.
5. **Pause/timescale audit:** `ControlAuthoritySystem` accumulates `SystemAPI.Time.DeltaTime` for release-idle — under
   world-pause (BovineLabs `PauseGame`) or `WorldTimeScale = 0`, idle never accumulates and an engaged override persists
   through pause (probably desired) — document; under slow-mo, release takes longer in real time (probably undesired for an
   input-feel parameter) — consider unscaled time. *Certainty: Risk (depends on which DeltaTime the group sees).*
6. **`AxisTransformSystem` smoothing** uses `1 - exp(-smoothing * dt)` — correctly framerate-independent; tests pin it. ✔ no action.
7. **History ring under lag spikes:** a 1-frame stall then burst of edges is handled (eviction math has an invariant test) ✔.

## Architecture TODOs

1. **Authority arbitration + dual-slot registry** — Critical TODO #1. This is the one structural change; everything else
   composes around it.
2. **Registry as tracked buffer** — High TODO (do together with #1: one migration).
3. **Seat identity (C10)** — High TODO.
4. **World-filter matrix as an explicit table** — High TODO.
5. **Event-dispatch extraction** — Medium TODO.
6. **Sequence priority baking** — High TODO.
7. **Consider a `PlayerInputsSystemGroup`**: the package currently strings 12 systems through
   `TimelineComponentAnimationGroup` with pairwise `UpdateAfter/Before` attributes; a dedicated child group
   (mask → history → direction/reset → sequence → clear → retire) would make the pipeline order visible in one attribute set
   and give external systems a single anchor (the generated-projection group already exists as precedent). Low urgency,
   high readability. *Certainty: Confirmed structure; suggestion.*

## Debugging / Tooling TODOs

1. **Sequence-match explainer** (the missing tool): when a combo doesn't fire, nothing says *which step* failed. Add to
   `DebugInputBufferSystem` (or a new `DebugCommandSequenceSystem`): per active sequence clip, draw per-step status —
   matched-at-tick / waiting / failed-window — by re-running the matcher with a recording shim (the matcher is pure; run it
   with a step-index-out parameter). This converts the package's hardest support question into a glance.
2. **Authority overlay**: once arbitration lands, extend `DebugPlayerInputSystem` to show per-consumer
   `driving/idle-seconds/trigger` (counts exist; the *why* doesn't).
3. **Event fire trace**: a config-var-gated log line in `TriggerEventsJob` (entity, condition key, amount) — condition
   events are currently observable only by their downstream reactions.
4. **`player_input` CLI**: already strong. Add `history` op dumping a consumer's `InputHistory` (+tick ages) — pairs with
   the sequence explainer for headless repro scripts.
5. **Anchor `DebugAxisTransformSystem` link-miss cue** — see Validation section.

## Testing TODOs

Each test names the specific risk it pins:

1. **End-to-end sequence integration** (`ECSTestsFixture`): provider + consumer + window mask + history + sequence clip →
   condition event recorded. *Proves the mask→record→match→dispatch chain — currently zero coverage; every regression in
   this file chain (the dormant-buffer incident) would have been caught here.*
2. **Buffer-clear ordering test** — clear + stale entry + sequence, same frame (asserts the High fix).
3. **Cross-clip priority test** — two consuming clips, priority respected (asserts the High fix).
4. **Authority arbitration tests** — override on/off provider selection (asserts the Critical fix).
5. **Framerate-window equivalence test** — same physical timings at 30/60/240 fps tick streams (asserts Critical #2).
6. **`OrderedLastConsume` truth-table tests** — currently zero direct tests for the least-explainable mode.
7. **Move lateral-offset unit test** — known-owed from the last panel (Aim lateral is tested; Move is not).
8. **`ConsumerHistorySystem` eviction under mask** — masked actions excluded, limit respected, same-tick Down-before-Up
   pinned (or the improved ordering once fixed).
9. **`ControlAuthoritySystem` engage/release integration** — `OverrideDecisionTests` cover the math; nothing covers the
   system wiring (policy component → enabled bit) with a live registry.
10. **Bridge drain-cadence tests** — once accumulate-and-drain lands: 0-consume and 2-consume cadences.
11. **`InputEventsSystem` deactivate-edge test** — `DeactivateJob` fires `OnInputEnd` exactly once when the clip ends while
    held (subtle one-shot logic; untested).
12. **Registry churn test** — provider destroyed without retirement (world teardown path) → slot clears next frame,
    `PlayerLeft` fires once.

## Suggested Architecture Direction

**Current weakness:** the package has a clean *data* pipeline but three cross-cutting concerns leak through it: authority
(computed, never applied), identity (seat = transient index), and scheduling (12 systems threaded through a host group with
pairwise attributes + one untracked shared array). Individually small; together they make the multiplayer story — the
package's stated differentiator — unreliable.

**Target shape (ownership boundaries):**
- **Acquisition** (`PlayerInputBridge`, `SyntheticProvider*`, flow systems): owns *devices → provider entities*. Providers
  are dumb state carriers; the bridge drains on demand (accumulate-and-drain), synthetic writers only accumulate.
- **Identity & registry** (`InputRegistrySystem`): owns *seat → {human, synthetic} provider slots* as a **tracked
  DynamicBuffer** on the registry singleton; seats are stable join-tickets (C10); join/leave buffers are the only public
  events. Collisions latch-log once via BLLogger.
- **Authority** (`ControlAuthoritySystem` + a new `ControlOverrideClip`): owns the per-consumer `PlayerOverride` bit.
  Nothing else writes it; *everything* downstream reads providers exclusively through authority-aware `InputAccess`.
- **Consumer projection** (`ConsumerBufferMaskSystem` → `ConsumerHistorySystem` → `DirectionInputSystem` → generated
  `<T>_Projection`s): owns per-consumer derived state. History entries carry `(tick, millis)`; windows in millis.
- **Timeline semantics** (sequence/reset/clear/events/axis clips): pure consumers of the projection; deterministic
  priority; clear-before-match; all inside one `PlayerInputsTimelineGroup` whose child order *is* the documentation.
- **Dispatch** (`ConditionEventDispatch`): one shared implementation.

**Data flow:** devices → providers → registry slots → (authority pick) → consumer history/state → matcher → condition
events. **Event flow:** join/leave from the registry; condition events from dispatch; nothing else crosses layers.
**Validation flow:** OnValidate (asset shape) → bake (links/actions, loud + skip) → build gate (settings duplicates) →
runtime drawers (link misses, history pressure) — each failure caught at the earliest layer that can see it.
**Debugging flow:** registry overlay (who owns the seat) → buffer drawer (what was recorded) → sequence explainer (why it
didn't match) → event trace (what fired) — a designer can walk the exact pipeline order when something is dead.

**Migration steps:** (1) registry buffer + dual slots + authority-aware `InputAccess` (one PR — the Critical+High registry
items share a migration); (2) clear-order + priority + bake validation (small PRs, immediate designer wins); (3) time-based
windows (data migration PR with the legacy-ticks conversion); (4) seat identity C10; (5) drain-cadence bridge; (6) hygiene
sweep. **Risks:** (1) and (3) touch serialized/authored data — sweep `Assets/Settings` + Mechanics timelines in the
superproject after each. **Verify:** package tests + the new integration suite + one manual pass of every showcase cell.

## Implementation Snippets

**Authority-aware provider pick (core of Critical #1):**
```csharp
public struct ProviderSlot : IBufferElementData { public Entity Human; public Entity Synthetic; }

public static bool TryGetProvider(in DynamicBuffer<ProviderSlot> registry,
    ComponentLookup<PlayerOverride> overrides, Entity consumer, byte playerId, out Entity provider)
{
    var slot = registry[playerId];
    var overridden = overrides.HasComponent(consumer) && overrides.IsComponentEnabled(consumer);
    provider = overridden && slot.Synthetic != Entity.Null ? slot.Synthetic : slot.Human;
    return provider != Entity.Null;
}
```

**Latched Burst-safe collision report (High logging fix):**
```csharp
if (!reportedDup[slot])
{
    reportedDup[slot] = true;
    Logger.LogError512($"Duplicate provider for PlayerId {slot}; keeping first (seq {existingSeq}).");
}
// on clean resolution: reportedDup[slot] = false;
```

**Shared bake guard (High consumerLink fix):**
```csharp
public static bool RequireLink(EntityLinkSchema schema, UnityEngine.Object context, string clipName, string field)
{
    if (schema != null && EntityLinkAuthoringUtility.TryGetKey(schema, out _)) return true;
    Debug.LogError($"{clipName}: '{field}' is unassigned or unregistered; the clip resolves no consumer. Clip will be skipped.", context);
    return false;
}
```

**Time-based window record (Critical #2):**
```csharp
// ConsumerHistorySystem
var millis = (uint)(SystemAPI.Time.ElapsedTime * 1000.0);
history.Add(new InputHistory { ActionId = id, Phase = phase, Tick = Tick, Millis = millis });
// bake
step.MaxGapMillis = (ushort)math.min(ushort.MaxValue, stepData.MaxGapSeconds * 1000f);
```

**Clear-before-match ordering (High #1):**
```csharp
[UpdateInGroup(typeof(TimelineComponentAnimationGroup))]
[UpdateAfter(typeof(ConsumerBufferMaskSystem))]      // stale-only semantics: clear before this frame records
[UpdateBefore(typeof(ConsumerHistorySystem))]
public partial struct InputBufferClearSystem : ISystem { ... }
```

**Sequence-priority gather (High #2):**
```csharp
struct ClipKey : IComparable<ClipKey>
{
    public int Priority; public Entity Entity;
    public int CompareTo(ClipKey o) => Priority != o.Priority ? Priority.CompareTo(o.Priority) : Entity.CompareTo(o.Entity);
}
```

## Final Ranked TODO List

1. **[Critical]** Wire control authority end-to-end (`PlayerOverride` readers + dual-slot registry; unblocks the package's takeover promise).
2. **[Critical]** Framerate-independent sequence windows (time-stamped history, `MaxGapSeconds`; fix the `MaxGapTicks` tooltip immediately).
3. **[High]** Bake-error on missing `consumerLink` in all six silent clips (the proven silent-dead-clip class).
4. **[High]** Run `InputBufferClearSystem` before sequence matching (stale-input race on the clear frame).
5. **[High]** Deterministic authored priority for cross-clip `CommandSequence` consumption.
6. **[High]** MultiInputSettings: collision-proof `Keys` + build-time duplicate/unassigned gate (total-outage guard).
7. **[High]** Registry array → tracked `DynamicBuffer` (delete all seven `NativeDisableContainerSafetyRestriction` captures).
8. **[High]** Seat identity guards now (`GetPlayerId` clamp, stable-seq tie-break); schedule full C10 seat map.
9. **[High]** Reconcile `WorldSystemFilter` matrix (`InputEventsSystem`, `SyntheticProviderClearSystem`; delete GridFlow's patch-clear).
10. **[High]** Latched, Burst-safe, player-visible registry diagnostics (BLLogger convention).
11. **[High]** Bridge accumulate-and-drain design for fixed-step/multi-world cadence (+ document the 1-frame latency).
12. **[Medium]** Maintainer decision + tests on unordered-monotonicity and `OrderedLastConsume` (or delete the mode).
13. **[Medium]** Deadzone for axis-as-button edge synthesis (stick-drift history pollution).
14. **[Medium]** `TriggerIfAlreadyHeld` toggle on `InputEventsClip` (level-vs-edge on activation).
15. **[Medium]** NavFlow proxy pathfinding cleanup on hard clip teardown.
16. **[Medium]** Empty-window eviction advisory + red-at-cap history drawer.
17. **[Medium]** Same-tick Down/Up ordering by held-state.
18. **[Medium]** Extract shared `ConditionEventDispatch`.
19. **[Medium]** Unify clip-baker skip semantics; null-guard `DependsOn`.
20. **[Medium]** Dead surface + assembly hygiene (`PlayerMoveInput`, `InputSource`, enableable `BufferClearConfig`, stale `InternalsVisibleTo`, phantom `PlayerInputs.Data` refs, unused AppUI/Anchor/Physics/Graphics deps).
21. **[Medium]** Flow-system sync-point containment (profiler markers now; jobify on evidence).
22. **[Low]** Designer README (trap list, clip→system table, override semantics, world matrix).
23. **[Low]** Sequence-match explainer drawer + `player_input history` op + event fire trace.
24. **[Low]** Tooltip fixes (`ReleaseIdleSeconds` 0=never, `PlayerId` contract, Manual trigger pointer).
25. **[Low]** Naming/file-case sweep; `SyntheticProviderTag` relocation; clip `duration` consistency.
26. **[Low]** `package.json` dependency floors + missing deps + keywords.
27. **[Low]** Reserved-id-255 exclusion in the ALL window mask; `WithinWindow` sentinel removal (folds into #2).
28. **[Low]** `ConditionKeyDrawer` targeted cache invalidation; `DebugInputBufferSystem` clear-only-scene gate; `DebugPlayerInputSystem` offset config-var.
29. **[Testing, alongside each fix]** The 12-test list above — items 1–5 of it land with ranked items 1–5 respectively.
