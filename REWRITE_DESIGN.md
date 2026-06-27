# Timeline.PlayerInputs v2 — Rewrite Design (hardened)

Status: HARDENED by a 48-agent senior/QA/edge-case panel (36 confirmed fixes folded in).
Goal: robust, Bridge-grade-DX, multiplayer input package for 200+ designers. Multiplayer kept.

## Non-negotiables (preserved backbone)

The byte-id + `BitArray256` backbone is load-bearing, not legacy:
- `CommandSequence` matching filters input history with bit-parallel ops over `BitArray256`.
- `ActiveBufferMask` / `BufferWindowConfig.AllowedActions` are `BitArray256` of action ids.
- Multiplayer: `InputRegistry.providerByPlayer[256]`, per-player providers, consumers linked via
  `EntityLinkResolver`, synthetic (flow-field AI) providers writing the same `InputState`/`InputAxis`.
- Timeline: carets (`AxisTransform`), `DirectionState`, `InputEvents`→`ConditionEvent`, control authority.

byte-id is a POSITIONAL index into `MultiInputSettings.inputActions[]`; per-player `InputActionAsset`
clones preserve action GUIDs, and `MultiInputSettings.TryGet` resolves GUID-first — so one byte-id
table maps correctly into every player's clone (verified: PlayerInputBridge resolves by `action.id`).

## What we adopt from com.bovinelabs.bridge.Input

1. `ButtonState { Down, Pressed, Up; Started/Cancelled/Reset }` — DONE (Bridge-parity + tests).
2. Source generator → strongly-typed input — but **reference-based + bake-resolved** (see below), NOT
   Bridge's single-global SetSingleton model.
3. `InputCommon` (cursor/ray/UI/focus) — adopted with the camera-optional fix.
4. Tests for every struct + system.

## CORRECTIONS from the panel (these overrule the first draft)

### C1 — Binding is by InputActionReference (GUID), never by field name. [blocker/major ×4]
Action names are unique only within a map, are not C#-identifier-shaped ("Light Attack", "236P"), and
`KSettingsBase` name→id hashmap THROWS on duplicate leaf names (kills all input). So:
- The generated `Settings` class declares one `InputActionReference` per field (field name = inspector
  label only) — exactly Bridge's model.
- field→byte-id is resolved via `MultiInputSettings.TryGet(reference)` (GUID-first), reusing the one
  registry. Names are DX sugar only.

### C2 — Resolution + validation at BAKE time, not compile time. [blocker ×3]
`MultiInputSettings` is a runtime ScriptableObject; a Roslyn generator cannot read it. Therefore:
- Generator emits ONLY structural diagnostics (missing `partial`, non-struct, unsupported field type,
  invalid Down/Up, invalid Delta) — what it can see in the Compilation.
- A BAKER resolves each field's `InputActionReference`→byte-id onto a baked id-table/config component,
  emitting a loud `Debug.LogError` per unresolved/ambiguous reference (mirrors existing
  `InputConsumerAuthoring` error pattern). Runtime is a last-resort `TryGet` + warn.

### C3 — Typed component lives on the BAKED CONSUMER, not the runtime-created provider. [blocker ×2]
Providers are created imperatively at runtime (`PlayerInputBridge.TryCreateProvider`,
`SyntheticProviderBuilder`) so a generated baker can never attach the typed component to them. The
consumer IS baked (`InputConsumerAuthoring.Baker`), so:
- The typed `T : IPlayerInput` + its resolved id-table are baked onto the consumer entity.
- The generated projection system, per consumer, resolves the consumer's provider via the existing
  registry/`EntityLinkResolver` link (same path `InputAccess` uses), reads that provider's
  `InputState`/`InputAxis` by the id-table, and fills the consumer's `T`. Zero runtime structural churn.
- Multiplayer-correct: each consumer reads its own player. (N consumers/player = N cheap projections;
  acceptable. Provider-placement-with-generated-EnsureSystem is a noted future optimization.)

### C4 — Projection ordering is explicit. [blocker + major ×2]
Writers: `ProviderSyncSystem` (InitializationSystemGroup) writes human axes early; `GridFlowInputSystem`
(TimelineComponentAnimationGroup) writes synthetic axes LATE. So the projection must run after ALL
writers and before ALL typed consumers:
- `[UpdateInGroup(typeof(TimelineComponentAnimationGroup))]`,
  `[UpdateAfter(typeof(GridFlowInputSystem))]` (transitively after ProviderSync),
  `[UpdateBefore(typeof(AxisTransformSystem))]`,
  `[WorldSystemFilter(LocalSimulation|ClientSimulation|ServerSimulation)]` (same triple as the backbone).
- Do NOT inherit Bridge's `UpdateInGroup(InputSystemGroup)` (Presentation child-default → wrong world).
- A dedicated empty `ProviderProjectionGroup` is the clean form; future synthetic writers declare
  `[UpdateBefore(ProviderProjectionGroup)]`.

### C5 — Delta time-scaling keys on CONTROL TYPE, not the attribute. [major ×3]
A pointer `Delta` control already reports per-frame physical displacement (Σ over a second = total
travel, fps-independent). Multiplying it by `dt` makes look speed framerate-dependent (the opposite of
a fix). A stick used as a look RATE does need `×dt`. Therefore:
- Pointer/mouse `Delta` → consumed RAW (`[InputAction] float2 Look`).
- Stick rate → `[InputActionDelta]` means `×dt`.
- The generator/classifier must NOT auto-apply `×dt` from detecting a `Delta` control; emit a bake
  diagnostic if `[InputActionDelta]` lands on a pointer-Delta-bound field.
- Correct the old prose: `×dt` is NOT "the answer to mouse jank"; RAW is, for a Delta control.

### C6 — Classify axis-vs-button by resolved control VALUE TYPE, not action.type. [major]
A 1D PassThrough (scroll/throttle) currently mis-routes to the button path and reads `float2.zero`
forever. Route to the axis path when control value type is float/Vector2 AND not a `ButtonControl`.
For v2, classify from the registry's declared field kind (`[InputAction] float2` vs `ButtonState` vs
`[InputActionDown] bool`) — single source of truth — and bake-error on declared-kind vs binding mismatch.

### C7 — Focus loss must NOT inject spurious Up edges into combo history. [major]
Routing force-release through `Release`→`pendingUp`→`InputState.Up` makes alt-tab fire release-combos
(charge-punch-on-release) and spurious `OnInputEnd` events. Correct protocol in `PlayerInputBridge`:
- Track `wasFocused`. On the focus→unfocused transition: `edges.Reset()` ONCE (clears pressed +
  pending, emits no Up), publish all-clear.
- While unfocused: skip the button reconcile and skip producing edges (freeze; OS delivers no input).
- On unfocused→focused transition: re-`Seed()` `pressed` from `action.IsPressed()` (sets pressed
  WITHOUT a Down edge), so a held key resumes silently with no spurious Down.
This kills stuck-on-alt-tab AND the spurious-edge class, using existing EdgeAccumulator primitives.

### C8 — Generator incremental pipeline must be cache-correct. [major]
Bridge's `CompilationProvider.Select(...).Combine(candidates)` + carrying `INamedTypeSymbol`/`Diagnostic`
roots the Compilation and re-runs on every keystroke (IDE death at 200 devs). v2 generator:
- `SyntaxProvider.ForAttributeWithMetadataName` keyed on the `[InputAction*]` attributes.
- Project to a fully value-equatable record: `string TypeName`, `string Namespace`,
  `EquatableArray<(string FieldName, string FieldType, AttributeKind Kind)>`, flags, and
  `(string FilePath, TextSpan Span)` for diagnostics. NEVER store ISymbol/Diagnostic/Compilation past
  the transform.
- Port ONLY field/attribute classification + the `Settings`/`Bake` shape. DROP Bridge's subscribe-based
  system body — v2 reads byte-id `InputState`/`InputAxis`, it does not subscribe.
- Pin `Microsoft.CodeAnalysis.CSharp` to Unity's version; vendor `CodeGenHelpers`; label `RoslynAnalyzer`,
  exclude from player; CI test the generator against a sample struct.

### C9 — Map enable/disable keyed by PlayerId, not Bridge's shared asset. [major]
Bridge's `InputAPI`/`InputActionMapSystem` operate on one shared `InputCommonSettings.Asset`; with
per-player clones that is a no-op. v2 gates through the existing per-player override/provider spine
(a per-provider gate via `InputRegistry.ProviderByPlayer`, with a broadcast-all option). Secondary:
fan out `Enable()/Disable()` across live `PlayerInputBridge` instances if OS-level stop is needed.

### C10 — PlayerId is a stable logical SEAT, not the transient playerIndex. [major]
`PlayerInput.playerIndex` is recycled on leave/rejoin → a newcomer silently inherits a consumer's
character/overrides. v2: registry key = a stable join-ticket / `InputUser.id` captured at join (not
recycled); authored `PlayerId` = logical seat resolved through a seat→identity map; wire a consumer
reaction to `PlayerJoined`/`PlayerLeft` (buffers already exist) to clear overrides on reassignment.
`PlayerIdOverride` stays the manual pin. (Phaseable; document `PlayerId == seat`.)

### C11 — Registry collision priority is deterministic; human beats synthetic. [major + minor]
On a shared slot the registry dedups by arbitrary `Entity.Index` (non-deterministic under CoreCLR
index reuse) and can let a synthetic steal a live human. Fix: tie-break on synthetic-vs-human
(human wins) then a stable monotonic provider sequence (not `Entity.Index`); name the collision kind
in the diagnostic ("human+synthetic on PlayerId N — synthetic ignored").

### C12 — Author-time validators (loud, directed). [major ×3 + minors]
`MultiInputSettings.OnValidate` (+ settings build hook): error on duplicate action.name, duplicate
action.id, null slots, and `inputActions.Length > 255`. Reserve byte 255 as the unresolved sentinel
and cap the usable registry at 255 (kills the sentinel-vs-legal-slot-255 collision); show an "N/255"
inspector counter. Qualify the Keys dropdown as `Map/Action` so duplicate display names can't collide.

### C13 — InputCommon is camera-OPTIONAL. [major]
Do NOT port Bridge's `RequireForUpdate(cameraQuery)`. Populate camera-independent fields every frame
(focus, AnyButtonPress, ScreenSize, CursorScreenPoint/ViewPoint, InputOverUI); compute
CameraRay/CursorCameraViewPoint only when a `CameraMain` exists, else default + one-time warn. Query
CameraMain, never `GetSingletonEntity` (throws on split-screen multiple cameras) — mirror
`AxisTransformSystem`'s `!IsEmpty` guard. Hoist the CursorPosition-null check out of any camera gate.

### C14 — Time-domain contract for typed reads. [major + minor]
The backbone ticks render-rate (TimelineSystemGroup ⊂ BeforeTransformSystemGroup). Typed reads are
live, render-rate, NON-consuming, and never see the command consume mask. Document this; if a fixed-step
consumer ever needs edges, it must accumulate (else double-fire/miss). If netcode/replay enters scope,
accumulate raw deltas into a fixed-step-consumed buffer.

## Supported typed field set (what the backbone can express)
`ButtonState` and `bool` (bit backbone), `float2` (axis), `float` (axis.x). `half`/`InputEvent`
(netcode) → "unsupported field" diagnostic unless/until netcode is in scope. Composite bindings
(2D WASD, 1D axis, modifier) resolve at the action level — locked as a tested invariant.

## Layers (assemblies)
- **.Data** (Burst): ButtonState ✓, attributes ✓, IPlayerInput ✓, InputCommon, configs, math, backbone.
- **.SourceGenerator~** (Roslyn incremental DLL): structural diagnostics + Settings/Bake + projection
  system emission per C1/C2/C4/C8.
- **(systems asm)**: providers, registry (C11), consumers, history, command sequences, axis transform,
  direction, input events, control authority, focus (C7), InputCommon (C13), map-gate (C9), projection.
- **.Authoring**: bakers (typed id-table resolution C2/C3), tracks/clips, MultiInputSettings validators (C12).
- **.Debug**: Quill overlays. **.Editor**: drawers, CLI tools. **.Tests**: Bridge-parity coverage.

## Frame contract (authoritative)
`ProviderSyncSystem` (Init) → human InputState/InputAxis written.
`InputRegistrySystem` (Init) → registry refreshed (C11 priority).
`GridFlowInputSystem` (TimelineComponentAnimationGroup) → synthetic axes written.
`ProviderProjection` (TimelineComponentAnimationGroup, after GridFlow, before AxisTransform) → typed T filled.
typed consumers / AxisTransform / CommandSequence / InputEvents → read.

## Staging (each step compiles + is tested)
1. .Data foundation (ButtonState/attrs/IPlayerInput) — DONE.
2. Focus-safe gating in PlayerInputBridge (C7) — self-contained, do next.
3. MultiInputSettings validators + 255-sentinel cap (C12) — self-contained.
4. InputCommon + camera-optional system (C13).
5. Source generator (C1/C2/C4/C8) + consumer-baked typed component (C3).
6. Classifier-by-kind (C6), delta-by-control-type (C5), map-gate-by-PlayerId (C9), registry priority (C11).
7. Seat identity (C10) — phaseable.
8. Tests per layer; remove dead old API once parity proven.

## Refuted by the panel (do NOT spend effort here)
Edge-model double-handling (SetSingleton-before-Reset is fine); hot-leave Up reaches consumers (retire
ordering is correct); CoreCLR settings re-seed (SettingsSingleton uses RuntimeInitializeOnLoad);
ProviderRetire ordering (already transitively correct); FixedString name-table desync (binding uses
GUID not the name table). Note: timeline `PlayerOverride` is currently display-only (does not suppress
live input) — that's a real feature gap but out of scope for the input rewrite; flag separately.
