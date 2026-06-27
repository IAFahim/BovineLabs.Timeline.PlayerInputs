using System.Collections.Generic;
using TMPro;
using Unity.Scenes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using TargetsAuthoring = BovineLabs.Reaction.Authoring.Core.TargetsAuthoring;
using TargetSlot = BovineLabs.Reaction.Data.Core.Target;
using ConditionEventObject = BovineLabs.Reaction.Authoring.Conditions.ConditionEventObject;
using EntityLinkSchema = BovineLabs.Timeline.EntityLinks.Authoring.EntityLinkSchema;
using EntityLinkRootAuthoring = BovineLabs.Timeline.EntityLinks.Authoring.EntityLinkRootAuthoring;
using EntityLinkSourceAuthoring = BovineLabs.Timeline.EntityLinks.Authoring.EntityLinkSourceAuthoring;
using TimelineBeginAuthoring = BovineLabs.Timeline.Core.Authoring.TimelineBeginAuthoring;
using TimelineBeginMode = BovineLabs.Timeline.Core.Authoring.TimelineBeginMode;
using InputConsumerAuthoring = BovineLabs.Timeline.PlayerInputs.Authoring.InputConsumerAuthoring;
using MultiInputSettings = BovineLabs.Timeline.PlayerInputs.Data.MultiInputSettings;
using SyntheticProviderAuthoring = BovineLabs.Timeline.PlayerInputs.Flow.Authoring.SyntheticProviderAuthoring;
using CommandTrack = BovineLabs.Timeline.PlayerInputs.Authoring.CommandSequenceTrack;
using CommandClip = BovineLabs.Timeline.PlayerInputs.Authoring.CommandSequenceClip;
using CommandSequenceData = BovineLabs.Timeline.PlayerInputs.Authoring.CommandSequenceData;
using CommandStepData = BovineLabs.Timeline.PlayerInputs.Authoring.CommandStepData;
using CommandMode = BovineLabs.Timeline.PlayerInputs.Data.CommandMode;
using InputPhase = BovineLabs.Timeline.PlayerInputs.Data.InputPhase;
using BufferTrack = BovineLabs.Timeline.PlayerInputs.Authoring.InputBufferTrack;
using BufferWindowClip = BovineLabs.Timeline.PlayerInputs.Authoring.InputBufferWindowClip;
using BufferClearClip = BovineLabs.Timeline.PlayerInputs.Authoring.InputBufferClearClip;
using EventsTrack = BovineLabs.Timeline.PlayerInputs.Authoring.InputEventsTrack;
using EventsClip = BovineLabs.Timeline.PlayerInputs.Authoring.InputEventsClip;
using AxisTrack = BovineLabs.Timeline.PlayerInputs.Authoring.AxisTransformTrack;
using AxisClip = BovineLabs.Timeline.PlayerInputs.Authoring.AxisTransformClip;
using AxisMode = BovineLabs.Timeline.PlayerInputs.Data.AxisTransformMode;
using FlowTrack = BovineLabs.Timeline.PlayerInputs.Flow.Authoring.FlowInputTrack;
using FlowClip = BovineLabs.Timeline.PlayerInputs.Flow.Authoring.FlowInputClip;
using GridFieldSchemaObject = BovineLabs.Timeline.Grid.Influence.Authoring.GridFieldSchemaObject;

public static class PlayerInputsShowcaseBuilder
{
    private const string SampleFolder = "Assets/Samples/PlayerInputsShowcase";
    private const string TimelineFolder = SampleFolder + "/Timelines";
    private const string ParentPath = SampleFolder + "/PlayerInputsShowcase.unity";
    private const string SubPath = SampleFolder + "/PlayerInputsShowcase_Sub.unity";

    private const string ConsumerLinkName = "Input Consumer Link";
    private const string EssenceLinkName = "Essence Link";
    private const string GridFieldName = "GridField";

    private static readonly Color CmdColor = new Color(0.20f, 0.55f, 0.90f);
    private static readonly Color BufColor = new Color(0.90f, 0.75f, 0.20f);
    private static readonly Color EvtColor = new Color(0.90f, 0.85f, 0.20f);
    private static readonly Color AxisColor = new Color(0.20f, 0.90f, 0.40f);
    private static readonly Color FlowColor = new Color(0.40f, 0.70f, 0.90f);
    private static readonly Color ConsumerColor = new Color(0.55f, 0.57f, 0.62f);
    private static readonly Color AnchorColor = new Color(0.90f, 0.25f, 0.25f);
    private static readonly Color PadColor = new Color(0.22f, 0.24f, 0.29f);
    private static readonly Color BannerColor = new Color(0.06f, 0.08f, 0.12f);

    private const float ColStep = 11f;
    private const float RowStep = 6f;
    private const float ActorY = 1.0f;

    private const float CmdX = -22f;
    private const float BufX = -11f;
    private const float EvtX = 0f;
    private const float AxisX = 11f;
    private const float FlowX = 22f;

    private static readonly Vector3 CameraPos = new Vector3(0f, 16f, -30f);

    private static Scene activeSub;

    private static EntityLinkSchema consumerLink;
    private static EntityLinkSchema essenceLink;
    private static GridFieldSchemaObject gridField;
    private static MultiInputSettings settings;

    private static InputActionReference moveAction;
    private static InputActionReference attackAction;
    private static readonly List<InputActionReference> registered = new List<InputActionReference>();

    private static ConditionEventObject evLightHit;
    private static ConditionEventObject evFireball;
    private static ConditionEventObject evPressBegin;
    private static ConditionEventObject evPressEnd;

    private enum BindKind { Consumer, Targets }

    private sealed class TrackBind
    {
        public string TrackName;
        public string BindActorName;
        public BindKind Kind;
    }

    private sealed class CellWire
    {
        public string DirectorName;
        public string TimelinePath;
        public List<TrackBind> Binds;
    }

    private static readonly List<CellWire> Wires = new List<CellWire>();

    private sealed class CaptionData
    {
        public string Title;
        public string Usage;
        public Vector3 CellPos;
        public Color Color;
    }

    private static readonly List<CaptionData> Captions = new List<CaptionData>();

    [MenuItem("Showcase/Build PlayerInputs")]
    public static void Build()
    {
        Wires.Clear();
        Captions.Clear();
        ResolveAssets();
        EnsureFolders();
        ResetAssets();

        var parent = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.SaveScene(parent, ParentPath);
        var sub = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        EditorSceneManager.SetActiveScene(sub);
        activeSub = sub;

        BuildPads();
        BuildSharedProvider();
        BuildCommandColumn();
        BuildBufferColumn();
        BuildEventsColumn();
        BuildAxisColumn();
        BuildFlowColumn();

        EditorSceneManager.SaveScene(sub, SubPath);
        EditorSceneManager.SetActiveScene(parent);
        EditorSceneManager.CloseScene(sub, true);

        sub = EditorSceneManager.OpenScene(SubPath, OpenSceneMode.Additive);
        EditorSceneManager.SetActiveScene(sub);
        activeSub = sub;

        foreach (var w in Wires)
        {
            WireCell(w);
        }

        EditorSceneManager.MarkSceneDirty(sub);
        EditorSceneManager.SaveScene(sub);

        EditorSceneManager.SetActiveScene(parent);
        BuildParent();
        EditorSceneManager.SaveScene(parent);

        EditorSceneManager.CloseScene(sub, true);
        EditorSceneManager.OpenScene(ParentPath, OpenSceneMode.Single);

        Debug.Log("PlayerInputsShowcase: built grid at " + ParentPath +
                  " | registeredActions=" + registered.Count +
                  " move=" + (moveAction != null) + " attack=" + (attackAction != null) +
                  " gridField=" + (gridField != null));
    }

    // ---------------- asset resolution ----------------

    private static void ResolveAssets()
    {
        consumerLink = LoadSchema(ConsumerLinkName);
        essenceLink = LoadSchema(EssenceLinkName);
        gridField = AssetDatabase.LoadAssetAtPath<GridFieldSchemaObject>(
            "Assets/Settings/Schemas/GridFields/GridField.asset");

        settings = AssetDatabase.LoadAssetAtPath<MultiInputSettings>(
            "Assets/Settings/Settings/K/MultiInputSettings.asset");

        registered.Clear();
        moveAction = null;
        attackAction = null;
        if (settings != null)
        {
            foreach (var r in settings.InputActions)
            {
                if (r == null || r.action == null) continue;
                registered.Add(r);
                var n = r.action.name;
                if (moveAction == null && n == "Move") moveAction = r;
                if (attackAction == null && n == "Attack") attackAction = r;
            }
        }

        if (moveAction == null && registered.Count > 0) moveAction = registered[0];
        if (attackAction == null && registered.Count > 0) attackAction = registered[registered.Count - 1];

        evLightHit = LoadEvent("OnComboIncrement");
        evFireball = LoadEvent("OnChargeAttackReleased");
        evPressBegin = LoadEvent("OnChargeAttackInitiated");
        evPressEnd = LoadEvent("OnChargeAttackReleased");
    }

    private static EntityLinkSchema LoadSchema(string name)
    {
        var path = "Assets/Settings/Schemas/EntityLinks/" + name + ".asset";
        return AssetDatabase.LoadAssetAtPath<EntityLinkSchema>(path);
    }

    private static ConditionEventObject LoadEvent(string name)
    {
        var path = "Assets/Settings/Schemas/Events/" + name + ".asset";
        return AssetDatabase.LoadAssetAtPath<ConditionEventObject>(path);
    }

    // ---------------- shared provider ----------------

    private static void BuildSharedProvider()
    {
        var go = new GameObject("PI_SyntheticProvider");
        SceneManager.MoveGameObjectToScene(go, activeSub);
        var sp = go.AddComponent<SyntheticProviderAuthoring>();
        sp.PlayerId = 0;
    }

    // ---------------- a consumer (root + source + InputConsumer) ----------------

    private static GameObject MakeConsumer(string name, Vector3 pos, Color color, bool trackDirection)
    {
        var go = MakeCube(name, pos, new Vector3(1.1f, 1.1f, 1.1f), color);

        var consumer = go.AddComponent<InputConsumerAuthoring>();
        consumer.PlayerId = 0;
        consumer.HistoryLimit = 64;
        consumer.TrackDirection = trackDirection;
        consumer.DirectionAction = trackDirection ? moveAction : null;
        consumer.DirectionDeadZone = 0.3f;
        consumer.DirectionFacing = 1;

        var root = go.AddComponent<EntityLinkRootAuthoring>();
        var source = go.AddComponent<EntityLinkSourceAuthoring>();
        source.Root = root;
        source.Schemas = new[] { consumerLink, essenceLink };
        root.Links = new[] { source };

        return go;
    }

    // ---------------- an actor that LINKS to a consumer (for Targets-bound tracks) ----------------

    private static GameObject MakeLinkedActor(string name, Vector3 pos, Vector3 scale, Color color, GameObject consumer)
    {
        var go = MakeCube(name, pos, scale, color);

        var targets = go.AddComponent<TargetsAuthoring>();
        targets.Owner = go;
        targets.Target = go;

        var root = consumer.GetComponent<EntityLinkRootAuthoring>();
        var source = go.AddComponent<EntityLinkSourceAuthoring>();
        source.Root = root;
        source.Schemas = new[] { consumerLink, essenceLink };

        var existing = new List<EntityLinkSourceAuthoring>(root.Links);
        if (!existing.Contains(source)) existing.Add(source);
        root.Links = existing.ToArray();

        return go;
    }

    // ---------------- COMMAND SEQUENCE ----------------

    private static void BuildCommandColumn()
    {
        // Row 0 — DEMO A: one-button Attack -> OnLightHit (Consume, Down, Repeatable=false),
        // paired with an OVERLAPPING InputBufferWindow on the SAME consumer (the §4 gate).
        {
            var z = 0 * RowStep;
            var consumer = MakeConsumer("Cmd0_Consumer", new Vector3(CmdX, ActorY, z), CmdColor, true);

            var timeline = NewTimeline(TimelineFolder + "/Cmd0_OneButton.playable");

            var bt = timeline.CreateTrack<BufferTrack>(null, "Buffer");
            var win = AddClip<BufferWindowClip>(bt, 0.0, 5.0, "window: ALL");
            ((BufferWindowClip)win.asset).AllowedActions = System.Array.Empty<InputActionReference>();
            ((BufferWindowClip)win.asset).ConsumerLink = consumerLink;
            Dirty(win.asset);

            var ct = timeline.CreateTrack<CommandTrack>(null, "Command");
            var clip = AddClip<CommandClip>(ct, 0.0, 5.0, "Attack -> OnLightHit");
            var ca = (CommandClip)clip.asset;
            ca.ReadRootFrom = TargetSlot.Owner;
            ca.ConsumerLink = consumerLink;
            ca.EventRouteTo = TargetSlot.Owner;
            ca.EventRouteLink = essenceLink;
            ca.Sequences = new[]
            {
                new CommandSequenceData
                {
                    Steps = new[]
                    {
                        new CommandStepData { Action = attackAction, Mode = CommandMode.Consume, Phase = InputPhase.Down, MaxGapTicks = 0 },
                    },
                    Condition = evLightHit,
                    Value = 1,
                    Repeatable = false,
                },
            };
            Dirty(clip.asset);

            var wire = NewWire(timeline, "Cmd0_Director");
            wire.Binds.Add(new TrackBind { TrackName = "Buffer", BindActorName = "Cmd0_Consumer", Kind = BindKind.Consumer });
            wire.Binds.Add(new TrackBind { TrackName = "Command", BindActorName = "Cmd0_Consumer", Kind = BindKind.Consumer });
            FinishWire(timeline, wire, CmdX, z,
                "One-button combo",
                "CommandSequenceClip Attack->OnLightHit (Consume,Down) + overlapping InputBufferWindow on the SAME consumer (the gate). Needs live Attack press.",
                CmdColor);
        }

        // Row 1 — DEMO B: 236P motion (Down -> DownForward -> Forward -> Attack), OrderedConsume.
        {
            var z = 1 * RowStep;
            var consumer = MakeConsumer("Cmd1_Consumer", new Vector3(CmdX, ActorY, z), CmdColor, true);

            var timeline = NewTimeline(TimelineFolder + "/Cmd1_Motion.playable");

            var bt = timeline.CreateTrack<BufferTrack>(null, "Buffer");
            var win = AddClip<BufferWindowClip>(bt, 0.0, 5.0, "window: ALL");
            ((BufferWindowClip)win.asset).AllowedActions = System.Array.Empty<InputActionReference>();
            ((BufferWindowClip)win.asset).ConsumerLink = consumerLink;
            Dirty(win.asset);

            var ct = timeline.CreateTrack<CommandTrack>(null, "Command");
            var clip = AddClip<CommandClip>(ct, 0.0, 5.0, "236P -> OnFireball");
            var ca = (CommandClip)clip.asset;
            ca.ReadRootFrom = TargetSlot.Owner;
            ca.ConsumerLink = consumerLink;
            ca.EventRouteTo = TargetSlot.Owner;
            ca.EventRouteLink = essenceLink;
            // Only registered actions resolve; unregistered direction actions fail-closed at bake.
            // We express the motion with the registered Move axis (Down/DownForward/Forward share Move)
            // followed by Attack, using OrderedConsume + MaxGapTicks for a true timed link.
            ca.Sequences = new[]
            {
                new CommandSequenceData
                {
                    Steps = new[]
                    {
                        new CommandStepData { Action = moveAction, Mode = CommandMode.OrderedConsume, Phase = InputPhase.Down, MaxGapTicks = 10 },
                        new CommandStepData { Action = moveAction, Mode = CommandMode.OrderedConsume, Phase = InputPhase.Down, MaxGapTicks = 10 },
                        new CommandStepData { Action = attackAction, Mode = CommandMode.OrderedConsume, Phase = InputPhase.Down, MaxGapTicks = 10 },
                    },
                    Condition = evFireball,
                    Value = 1,
                    Repeatable = true,
                },
            };
            Dirty(clip.asset);

            var wire = NewWire(timeline, "Cmd1_Director");
            wire.Binds.Add(new TrackBind { TrackName = "Buffer", BindActorName = "Cmd1_Consumer", Kind = BindKind.Consumer });
            wire.Binds.Add(new TrackBind { TrackName = "Command", BindActorName = "Cmd1_Consumer", Kind = BindKind.Consumer });
            FinishWire(timeline, wire, CmdX, z,
                "Motion combo (236P)",
                "OrderedConsume timed link (MaxGapTicks 10) -> OnFireball, Repeatable. Needs live directional+Attack input.",
                CmdColor);
        }
    }

    // ---------------- INPUT BUFFER (standalone) ----------------

    private static void BuildBufferColumn()
    {
        // Row 0 — Window only (record everything).
        {
            var z = 0 * RowStep;
            MakeConsumer("Buf0_Consumer", new Vector3(BufX, ActorY, z), BufColor, false);

            var timeline = NewTimeline(TimelineFolder + "/Buf0_Window.playable");
            var bt = timeline.CreateTrack<BufferTrack>(null, "Buffer");
            var win = AddClip<BufferWindowClip>(bt, 0.0, 5.0, "window: ALL");
            ((BufferWindowClip)win.asset).AllowedActions = System.Array.Empty<InputActionReference>();
            ((BufferWindowClip)win.asset).ConsumerLink = consumerLink;
            Dirty(win.asset);

            var wire = NewWire(timeline, "Buf0_Director");
            wire.Binds.Add(new TrackBind { TrackName = "Buffer", BindActorName = "Buf0_Consumer", Kind = BindKind.Consumer });
            FinishWire(timeline, wire, BufX, z,
                "Window (record all)",
                "InputBufferWindowClip empty array = buffer ALL 256 actions. Without it history records nothing.",
                BufColor);
        }

        // Row 1 — Clear then Window (wipe-then-window).
        {
            var z = 1 * RowStep;
            MakeConsumer("Buf1_Consumer", new Vector3(BufX, ActorY, z), BufColor, false);

            var timeline = NewTimeline(TimelineFolder + "/Buf1_ClearWindow.playable");
            var bt = timeline.CreateTrack<BufferTrack>(null, "Buffer");
            var clr = AddClip<BufferClearClip>(bt, 0.0, 1.0, "clear ALL");
            ((BufferClearClip)clr.asset).ActionsToClear = System.Array.Empty<InputActionReference>();
            ((BufferClearClip)clr.asset).ConsumerLink = consumerLink;
            Dirty(clr.asset);
            var win = AddClip<BufferWindowClip>(bt, 1.0, 4.0, "window: ALL");
            ((BufferWindowClip)win.asset).AllowedActions = System.Array.Empty<InputActionReference>();
            ((BufferWindowClip)win.asset).ConsumerLink = consumerLink;
            Dirty(win.asset);

            var wire = NewWire(timeline, "Buf1_Director");
            wire.Binds.Add(new TrackBind { TrackName = "Buffer", BindActorName = "Buf1_Consumer", Kind = BindKind.Consumer });
            FinishWire(timeline, wire, BufX, z,
                "Clear then Window",
                "InputBufferClearClip (empty=wipe ALL history, edge on enter) then InputBufferWindowClip opens a fresh window.",
                BufColor);
        }
    }

    // ---------------- INPUT EVENTS ----------------

    private static void BuildEventsColumn()
    {
        var z = 0 * RowStep;
        var consumer = MakeConsumer("Evt0_Consumer", new Vector3(EvtX - 1.6f, ActorY, z), ConsumerColor, false);
        var actor = MakeLinkedActor("Evt0_Actor", new Vector3(EvtX + 1.6f, ActorY, z), new Vector3(1.0f, 1.0f, 1.0f), EvtColor, consumer);

        var timeline = NewTimeline(TimelineFolder + "/Evt0_Edges.playable");
        var et = timeline.CreateTrack<EventsTrack>(null, "Events");
        var clip = AddClip<EventsClip>(et, 0.0, 5.0, "Attack start/end edges");
        var ca = (EventsClip)clip.asset;
        ca.ReadRootFrom = TargetSlot.Owner;
        ca.ConsumerLink = consumerLink;
        ca.Action = attackAction;
        ca.EventRouteTo = TargetSlot.Self;
        ca.OnInputStart = evPressBegin;
        ca.OnInputEnd = evPressEnd;
        Dirty(clip.asset);

        var wire = NewWire(timeline, "Evt0_Director");
        wire.Binds.Add(new TrackBind { TrackName = "Events", BindActorName = "Evt0_Actor", Kind = BindKind.Targets });
        FinishWire(timeline, wire, EvtX, z,
            "Press start/end edges",
            "InputEventsClip watches Attack via ConsumerLink; fires OnInputStart on press, OnInputEnd on release. Needs live Attack.",
            EvtColor);
    }

    // ---------------- AXIS TRANSFORM (the play-verifiable column) ----------------

    private static void BuildAxisColumn()
    {
        // Row 0 — Move with a leash, keep lead: Move axis drives the carrot on XZ, clamped within 4 units; on
        // release the lead stays put so the body travels there and stops.
        AxisCell(0, "Move (leash, keep lead)",
            "AxisTransformClip Move: the Move axis pushes the carrot on the XZ plane, clamped within a 4-unit lead. On release the lead is kept (KeepLead) so the body travels to it and stops.",
            AxisMode.Move, false, 4f, 4f, false);

        // Row 1 — Move, no leash, keep lead: travels freely while held, holds the lead where released.
        AxisCell(1, "Move (free, keep lead)",
            "AxisTransformClip Move, no leash: the carrot leads while Move is held and holds where released (KeepLead, no snap-back).",
            AxisMode.Move, false, 6f, 0f, false);

        // Row 2 — Aim: carrot turns to face the Move axis; on release it holds the last direction.
        AxisCell(2, "Aim",
            "AxisTransformClip Aim: the carrot turns to FACE the Move axis direction and HOLDS the last direction on release.",
            AxisMode.Aim, true, 0f, 0f, false);
    }

    private static void AxisCell(int row, string label, string usage, AxisMode mode, bool snapBackOnRelease, float range, float leash, bool cameraRelative)
    {
        var z = row * RowStep;
        var nm = "Axis" + row;
        var consumer = MakeConsumer(nm + "_Consumer", new Vector3(AxisX - 1.8f, ActorY, z), ConsumerColor, false);
        var actor = MakeLinkedActor(nm + "_Actor", new Vector3(AxisX + 1.4f, ActorY, z), new Vector3(1.0f, 1.0f, 1.0f), AxisColor, consumer);

        var timeline = NewTimeline(TimelineFolder + "/" + nm + ".playable");
        var at = timeline.CreateTrack<AxisTrack>(null, "Axis");
        var clip = AddClip<AxisClip>(at, 0.0, 10.0, label);
        var ca = (AxisClip)clip.asset;
        ca.ReadRootFrom = TargetSlot.Owner;
        ca.ConsumerLink = consumerLink;
        ca.Action = moveAction;
        ca.Range = range;
        ca.Plane = new Vector3(0f, 1f, 0f);
        ca.Mode = mode;
        ca.SnapBackOnRelease = snapBackOnRelease;
        ca.Smoothing = mode == AxisMode.Aim ? 8f : 0f;
        ca.LeashRadius = leash;
        ca.CameraRelative = cameraRelative;
        Dirty(clip.asset);

        var wire = NewWire(timeline, nm + "_Director");
        wire.Binds.Add(new TrackBind { TrackName = "Axis", BindActorName = nm + "_Actor", Kind = BindKind.Targets });
        FinishWire(timeline, wire, AxisX, z, label, usage, AxisColor);
    }

    // ---------------- FLOW INPUT (fake axis from grid field) ----------------

    private static void BuildFlowColumn()
    {
        if (gridField == null)
        {
            Debug.LogWarning("PlayerInputsShowcase: GridField schema missing — Flow column omitted.");
            return;
        }

        var z = 0 * RowStep;
        var consumer = MakeConsumer("Flow0_Consumer", new Vector3(FlowX - 1.8f, ActorY, z), ConsumerColor, false);
        var actor = MakeLinkedActor("Flow0_Actor", new Vector3(FlowX + 1.4f, ActorY, z), new Vector3(1.0f, 1.0f, 1.0f), FlowColor, consumer);

        // Flow synthesises a fake Move axis from a grid field; the consumer's AxisTransform reads it.
        var timeline = NewTimeline(TimelineFolder + "/Flow0.playable");
        var ft = timeline.CreateTrack<FlowTrack>(null, "Flow");
        var clip = AddClip<FlowClip>(ft, 0.0, 5.0, "fake Move from field");
        var ca = (FlowClip)clip.asset;
        ca.Field = gridField;
        ca.ReadRootFrom = TargetSlot.Owner;
        ca.ConsumerLink = consumerLink;
        ca.Action = moveAction;
        ca.Gain = 1f;
        Dirty(clip.asset);

        // Companion AxisTransform so the synthesised Move axis becomes visible motion.
        var at = timeline.CreateTrack<AxisTrack>(null, "Axis");
        var axisClip = AddClip<AxisClip>(at, 0.0, 5.0, "Move drives actor");
        var aa = (AxisClip)axisClip.asset;
        aa.ReadRootFrom = TargetSlot.Owner;
        aa.ConsumerLink = consumerLink;
        aa.Action = moveAction;
        aa.Range = 4f;
        aa.Plane = new Vector3(0f, 1f, 0f);
        aa.Mode = AxisMode.Move;
        aa.LeashRadius = 4f;
        Dirty(axisClip.asset);

        var wire = NewWire(timeline, "Flow0_Director");
        wire.Binds.Add(new TrackBind { TrackName = "Flow", BindActorName = "Flow0_Actor", Kind = BindKind.Targets });
        wire.Binds.Add(new TrackBind { TrackName = "Axis", BindActorName = "Flow0_Actor", Kind = BindKind.Targets });
        FinishWire(timeline, wire, FlowX, z,
            "Flow fake axis (field)",
            "FlowInputClip (Blending|Looping) samples GridField -> synthesises a Move axis; companion AxisTransform turns it into motion (needs a populated influence field).",
            FlowColor);
    }

    // ---------------- wire / caption plumbing ----------------

    private static CellWire NewWire(TimelineAsset timeline, string directorName)
    {
        return new CellWire
        {
            DirectorName = directorName,
            TimelinePath = AssetDatabase.GetAssetPath(timeline),
            Binds = new List<TrackBind>(),
        };
    }

    private static void FinishWire(TimelineAsset timeline, CellWire wire, float x, float z, string label, string usage, Color color)
    {
        FixDuration(timeline);
        Dirty(timeline);
        foreach (var tr in timeline.GetOutputTracks()) Dirty(tr);
        AssetDatabase.SaveAssets();
        MakeDirector(wire.DirectorName);
        Wires.Add(wire);
        Captions.Add(new CaptionData { Title = label, Usage = usage, CellPos = new Vector3(x, 3.6f, z), Color = color });
    }

    private static void WireCell(CellWire w)
    {
        var director = GameObject.Find(w.DirectorName).GetComponent<PlayableDirector>();
        var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(w.TimelinePath);
        director.playableAsset = timeline;

        foreach (var track in timeline.GetOutputTracks())
        {
            var bind = FindBind(w, track.name);
            if (bind == null) continue;
            var actor = GameObject.Find(bind.BindActorName);
            Object value = bind.Kind == BindKind.Consumer
                ? (Object)actor.GetComponent<InputConsumerAuthoring>()
                : actor.GetComponent<TargetsAuthoring>();
            director.SetGenericBinding(track, value);
        }

        EditorUtility.SetDirty(director);
    }

    private static TrackBind FindBind(CellWire w, string trackName)
    {
        foreach (var b in w.Binds)
            if (b.TrackName == trackName)
                return b;
        return null;
    }

    private static PlayableDirector MakeDirector(string name)
    {
        var go = new GameObject(name);
        SceneManager.MoveGameObjectToScene(go, activeSub);
        var director = go.AddComponent<PlayableDirector>();
        director.playOnAwake = true;
        director.extrapolationMode = DirectorWrapMode.Loop;
        var begin = go.AddComponent<TimelineBeginAuthoring>();
        begin.Mode = TimelineBeginMode.OnLoad;
        return director;
    }

    private static TimelineAsset NewTimeline(string path)
    {
        var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
        AssetDatabase.CreateAsset(timeline, path);
        return timeline;
    }

    private static TimelineClip AddClip<T>(TrackAsset track, double start, double duration, string name) where T : PlayableAsset
    {
        var clip = track.CreateClip<T>();
        clip.start = start;
        clip.duration = duration;
        clip.displayName = name;
        return clip;
    }

    private static void FixDuration(TimelineAsset timeline)
    {
        var end = 0.0;
        foreach (var track in timeline.GetOutputTracks())
            foreach (var clip in track.GetClips())
            {
                var clipEnd = clip.start + clip.duration;
                if (clipEnd > end) end = clipEnd;
            }

        timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
        timeline.fixedDuration = end;
    }

    // ---------------- primitives ----------------

    private static GameObject MakeCube(string name, Vector3 pos, Vector3 scale, Color color)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.position = pos;
        go.transform.localScale = scale;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(name, color);
        SceneManager.MoveGameObjectToScene(go, activeSub);
        return go;
    }

    private static GameObject MakePad(string name, Vector3 pos, Vector3 size)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.position = pos;
        go.transform.localScale = size;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(name, PadColor);
        SceneManager.MoveGameObjectToScene(go, activeSub);
        return go;
    }

    private static void BuildPads()
    {
        float[] xs = { CmdX, BufX, EvtX, AxisX, FlowX };
        string[] names = { "Cmd", "Buf", "Evt", "Axis", "Flow" };
        for (var i = 0; i < xs.Length; i++)
            MakePad(names[i] + "_Pad", new Vector3(xs[i], 0.05f, 4f), new Vector3(8.0f, 0.12f, 20f));
    }

    private static Material MakeMaterial(string name, Color color)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        var mat = new Material(shader) { name = name + "_Mat" };
        mat.color = color;
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        return mat;
    }

    // ---------------- parent: camera, labels, subscene ----------------

    private static void BuildParent()
    {
        FrameCamera();
        RenderSettings.fog = false;

        MakeBanner("Title_Banner", new Vector3(0f, 14.0f, 0f), new Vector3(46f, 3.4f, 0.1f));
        MakeWorldLabel("Title", "PLAYER INPUTS TIMELINE GRID", new Vector3(0f, 14.4f, -0.4f), 46f, Color.white, 5.0f, TextAlignmentOptions.Center);
        MakeWorldLabel("Subtitle", "5 tracks · 6 clip types on ECS-pure consumers   ·   com.bovinelabs.timeline.playerinputs", new Vector3(0f, 13.1f, -0.4f), 46f, new Color(0.85f, 0.9f, 1f), 1.9f, TextAlignmentOptions.Center);

        MakeColumnHeader("Cmd_Header", "COMMAND SEQ", CmdX, CmdColor);
        MakeColumnHeader("Buf_Header", "INPUT BUFFER", BufX, BufColor);
        MakeColumnHeader("Evt_Header", "INPUT EVENTS", EvtX, EvtColor);
        MakeColumnHeader("Axis_Header", "AXIS TRANSFORM", AxisX, AxisColor);
        MakeColumnHeader("Flow_Header", "FLOW INPUT", FlowX, FlowColor);

        foreach (var cap in Captions)
            MakeCaption(cap.Title, cap.Usage, cap.CellPos, cap.Color);

        MakeBanner("Usage_Banner", new Vector3(0f, 0.7f, -7.5f), new Vector3(50f, 2.0f, 0.1f));
        MakeWorldLabel("Usage",
            "Grey cubes = InputConsumer (PlayerId 0, fed by one synthetic provider). Coloured cubes = Targets-bound actors linked to a consumer. Axis column animates from injected Move; combo/buffer/events columns need live device input (see captions).",
            new Vector3(0f, 0.7f, -7.8f), 48f, new Color(0.96f, 0.97f, 1f), 1.5f, TextAlignmentOptions.Center);

        var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(SubPath);
        if (sceneAsset == null)
        {
            Debug.LogError("PlayerInputsShowcase: sub-scene asset missing at " + SubPath);
            return;
        }

        var subSceneGo = new GameObject("Showcase SubScene");
        var subScene = subSceneGo.AddComponent<SubScene>();
        subScene.SceneAsset = sceneAsset;
        subScene.AutoLoadScene = true;
        EditorUtility.SetDirty(subScene);
    }

    private static void MakeColumnHeader(string name, string text, float x, Color color)
    {
        var pos = new Vector3(x, 4.2f, -4.5f);
        MakeBanner(name + "_Banner", pos + new Vector3(0f, 0f, 0.08f), new Vector3(7.4f, 1.3f, 0.1f));
        MakeWorldLabel(name, "<b>" + text + "</b>", pos, 7.2f, color, 2.6f, TextAlignmentOptions.Center);
    }

    private static float CaptionY(float z)
    {
        return 3.8f + z * 0.18f;
    }

    private static void MakeCaption(string title, string usage, Vector3 cellPos, Color color)
    {
        var z = cellPos.z;
        var y = CaptionY(z);
        MakeBanner("CapBanner_" + title + "_" + z, new Vector3(cellPos.x, y, z + 0.06f), new Vector3(7.6f, 2.0f, 0.05f));
        MakeWorldLabel("Cap_" + title + "_" + z, "<b>" + title + "</b>", new Vector3(cellPos.x, y + 0.5f, z), 7.4f, color, 2.4f, TextAlignmentOptions.Center);
        MakeWorldLabel("Use_" + title + "_" + z, usage, new Vector3(cellPos.x, y - 0.42f, z), 7.4f, new Color(0.95f, 0.96f, 1f), 1.25f, TextAlignmentOptions.Center);
    }

    private static void FrameCamera()
    {
        var required = GameObject.Find("Required In Scene");
        if (required == null) return;
        var camTransform = required.transform.Find("Main Camera");
        if (camTransform == null) return;
        camTransform.position = CameraPos;
        camTransform.rotation = Quaternion.Euler(20f, 0f, 0f);
        var cam = camTransform.GetComponent<Camera>();
        if (cam != null)
        {
            cam.fieldOfView = 60f;
            cam.farClipPlane = 400f;
            EditorUtility.SetDirty(cam);
        }

        EditorUtility.SetDirty(camTransform);
    }

    private static void MakeBanner(string name, Vector3 pos, Vector3 size)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.position = pos;
        go.transform.rotation = Quaternion.LookRotation(pos - CameraPos, Vector3.up);
        go.transform.localScale = size;
        go.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(name, BannerColor);
    }

    private static void MakeWorldLabel(string name, string text, Vector3 pos, float width, Color color, float fontSize, TextAlignmentOptions alignment)
    {
        var holder = new GameObject(name);
        holder.transform.position = pos;
        holder.transform.rotation = Quaternion.LookRotation(pos - CameraPos, Vector3.up);

        var go = new GameObject("Text");
        go.transform.SetParent(holder.transform, false);
        var tmp = go.AddComponent<TextMeshPro>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.color = color;
        tmp.alignment = alignment;
        tmp.rectTransform.sizeDelta = new Vector2(width, 4f);
        tmp.rectTransform.localPosition = Vector3.zero;
        tmp.fontStyle = FontStyles.Bold;
    }

    private static void EnsureFolders()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Samples"))
            AssetDatabase.CreateFolder("Assets", "Samples");
        if (!AssetDatabase.IsValidFolder(SampleFolder))
            AssetDatabase.CreateFolder("Assets/Samples", "PlayerInputsShowcase");
        if (!AssetDatabase.IsValidFolder(TimelineFolder))
            AssetDatabase.CreateFolder(SampleFolder, "Timelines");
    }

    private static void ResetAssets()
    {
        if (AssetDatabase.LoadAssetAtPath<Object>(TimelineFolder) != null)
            foreach (var guid in AssetDatabase.FindAssets("t:TimelineAsset", new[] { TimelineFolder }))
                AssetDatabase.DeleteAsset(AssetDatabase.GUIDToAssetPath(guid));

        foreach (var p in new[] { ParentPath, SubPath })
            if (AssetDatabase.LoadAssetAtPath<Object>(p) != null)
                AssetDatabase.DeleteAsset(p);
    }

    private static void Dirty(params Object[] objects)
    {
        foreach (var o in objects)
            EditorUtility.SetDirty(o);
    }
}
