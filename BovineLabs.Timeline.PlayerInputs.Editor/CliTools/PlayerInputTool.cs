using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityCliConnector;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Users;
using Object = UnityEngine.Object;

namespace BovineLabs.Timeline.PlayerInputs.Editor.CliTools
{
    [InitializeOnLoad]
    [UnityCliTool(
        Name = "player_input",
        Group = "vex",
        Description =
            "Drive player input via the new Input System with full flexibility (provider/device, player id, button/axis/Vector2). ops: list, devices, add_device, remove_device, join, pair, leave, leave_all, press, release, tap, reset.")]
    public static class PlayerInputTool
    {
        private const string VirtualUsage = "VexCliVirtual";
        private const string KeyCaptured = "vex.player_input.captured";
        private const string KeyOrigBackground = "vex.player_input.origBackground";
        private const string KeyOrigEditorBehavior = "vex.player_input.origEditorBehavior";
        private const string KeyOrigRunInBackground = "vex.player_input.origRunInBackground";
        private const string KeyHeld = "vex.player_input.held";
        private const string KeyDevices = "vex.player_input.devices";

        private static readonly List<PendingRelease> Pending = new();
        private static bool _hooked;
        private static bool _lifecycleArmed;

        static PlayerInputTool()
        {
            Bootstrap();
        }

        [InitializeOnLoadMethod]
        private static void Bootstrap()
        {
            ArmLifecycle();
            // Sweep leftovers only in edit mode: a mid-play domain reload (recompile-and-continue) must NOT destroy
            // the live player / yank devices / revert routing. Play-exit is handled by OnPlayModeStateChanged.
            if (!Application.isPlaying)
                Cleanup();
        }

        private static void ArmLifecycle()
        {
            if (_lifecycleArmed) return;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            _lifecycleArmed = true;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingPlayMode || change == PlayModeStateChange.EnteredEditMode)
                Cleanup();
        }

        public static object HandleCommand(JObject @params)
        {
            ArmLifecycle();
            var p = new ToolParams(@params);
            var op = (p.Get("op", "list") ?? "list").Trim().ToLowerInvariant();

            switch (op)
            {
                case "list": return List();
                case "devices": return Devices();
                case "add_device": return AddDeviceOp(p.Get("provider"));
                case "remove_device": return RemoveDeviceOp(p.Get("provider"));
                case "join":
                    CaptureAndApplySettings();
                    return Join(p.Get("provider"), p.Get("scheme"));
                case "pair":
                    CaptureAndApplySettings();
                    return Pair(p.GetInt("player", 0) ?? 0, p.Get("provider"));
                case "leave": return Leave(p.GetInt("player", 0) ?? 0);
                case "leave_all": return LeaveAll();
                case "reset":
                case "cleanup": return ResetOp();
                case "press":
                case "release":
                case "tap":
                    CaptureAndApplySettings();
                    return Drive(op, p);
                default:
                    return new ErrorResponse(
                        $"Unknown op '{op}'. Use: list, devices, add_device, remove_device, join, pair, leave, leave_all, press, release, tap, reset.");
            }
        }

        private static object List()
        {
            var mgr = PlayerInputManager.instance;
            var players = PlayerInput.all.Select((pi, i) => new
            {
                index = i,
                pi.playerIndex,
                pi.gameObject.name,
                devices = pi.devices.Select(d => d.displayName).ToArray(),
                currentMap = pi.currentActionMap != null ? pi.currentActionMap.name : null,
                actions = pi.actions != null
                    ? pi.actions.Where(a => a.actionMap == pi.currentActionMap).Select(a => a.name).ToArray()
                    : new string[0]
            }).ToArray();

            return new SuccessResponse(
                $"{players.Length} player(s) joined" + (mgr == null ? " (no PlayerInputManager in scene)" : ""),
                new
                {
                    Application.isPlaying,
                    manager = mgr != null
                        ? (object)new
                            { joinBehavior = mgr.joinBehavior.ToString(), mgr.playerCount, mgr.joiningEnabled }
                        : null,
                    playerCount = players.Length,
                    players,
                    pendingReleases = Pending.Count,
                    heldControls = ReadList(KeyHeld).Count,
                    virtualDevices = ReadList(KeyDevices).Count,
                    settingsCaptured = SessionState.GetBool(KeyCaptured, false)
                });
        }

        private static object Devices()
        {
            var devices = InputSystem.devices.Select(d => new
            {
                d.name,
                d.displayName,
                d.layout,
                d.deviceId,
                d.added,
                kind = ProviderKind(d),
                isVirtual = d.usages.Any(u => u == VirtualUsage)
            }).ToArray();
            return new SuccessResponse($"{devices.Length} input device(s) present.", new { devices });
        }

        private static object AddDeviceOp(string provider)
        {
            if (string.IsNullOrEmpty(provider))
                return new ErrorResponse(
                    "add_device needs a provider: keyboard, gamepad, mouse, touch (or a layout name).");
            var layout = NormalizeLayout(provider);
            InputDevice device;
            try
            {
                device = InputSystem.AddDevice(layout);
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Could not add device of layout '{layout}': {e.Message}");
            }

            if (device == null) return new ErrorResponse($"AddDevice('{layout}') returned null.");

            try
            {
                InputSystem.AddDeviceUsage(device, VirtualUsage);
            }
            catch
            {
            }

            var ids = ReadList(KeyDevices);
            var idStr = device.deviceId.ToString();
            if (!ids.Contains(idStr)) ids.Add(idStr);
            WriteList(KeyDevices, ids);

            return new SuccessResponse(
                $"Added virtual {device.displayName} (layout {device.layout}, id {device.deviceId}). Tracked for auto-cleanup.",
                new { device.name, device.layout, device.deviceId });
        }

        private static object RemoveDeviceOp(string providerOrName)
        {
            var device = ResolveDevice(providerOrName, true);
            if (device == null) return new ErrorResponse($"No device matching '{providerOrName}'. See `devices`.");
            var label = device.displayName;
            UntrackDevice(device.deviceId);
            InputSystem.RemoveDevice(device);
            return new SuccessResponse($"Removed device {label}.");
        }

        private static object Join(string provider, string scheme)
        {
            if (!Application.isPlaying)
                return new ErrorResponse("join needs play mode (it instantiates the player prefab).");
            var mgr = PlayerInputManager.instance;
            if (mgr == null)
                return new ErrorResponse(
                    "No PlayerInputManager in the scene — cannot join. Add one, or instantiate the player prefab directly.");

            InputDevice device;
            if (!string.IsNullOrEmpty(provider))
            {
                device = ResolveDevice(provider);
                if (device == null)
                    return new ErrorResponse(
                        $"No '{provider}' device present. Plug one in or `add_device` a virtual one first. See `devices`.");
            }
            else
            {
                device = (InputDevice)Keyboard.current ?? (InputDevice)Gamepad.current ?? Mouse.current;
            }

            var pi = mgr.JoinPlayer(-1, -1, scheme, device);
            if (pi == null)
                return new ErrorResponse(
                    "JoinPlayer returned null (joining disabled, max players reached, or no free device). Check `list`.");
            return new SuccessResponse(
                $"Joined player {pi.playerIndex} ('{pi.gameObject.name}') on {(device != null ? device.displayName : "no device")}.",
                new { pi.playerIndex, pi.gameObject.name, devices = pi.devices.Select(d => d.displayName).ToArray() });
        }

        private static object Pair(int index, string providerOrName)
        {
            if (!Application.isPlaying) return new ErrorResponse("pair needs play mode.");
            var pi = PlayerAt(index, out var err);
            if (pi == null) return err;
            var device = ResolveDevice(providerOrName);
            if (device == null) return new ErrorResponse($"No device matching '{providerOrName}'. See `devices`.");
            InputUser.PerformPairingWithDevice(device, pi.user);
            return new SuccessResponse(
                $"Paired {device.displayName} to player {index}.",
                new { devices = pi.devices.Select(d => d.displayName).ToArray() });
        }

        private static object Leave(int index)
        {
            if (!Application.isPlaying) return new ErrorResponse("leave needs play mode.");
            var pi = PlayerAt(index, out var err);
            if (pi == null) return err;
            var name = pi.gameObject.name;
            Object.Destroy(pi.gameObject);
            return new SuccessResponse($"Removed player {index} ('{name}').");
        }

        private static object LeaveAll()
        {
            var count = PlayerInput.all.Count;
            Cleanup();
            return new SuccessResponse($"Removed {count} player(s) and cleaned up tool state.");
        }

        private static object ResetOp()
        {
            Cleanup();
            return new SuccessResponse(
                "Reset: released held controls, removed virtual devices, left players, restored input settings.");
        }

        private static object Drive(string op, ToolParams p)
        {
            if (!Application.isPlaying) return new ErrorResponse($"{op} needs play mode.");

            var playerIndex = p.GetInt("player", 0) ?? 0;
            var pi = PlayerAt(playerIndex, out var err);
            if (pi == null) return err;

            var provider = p.Get("provider");
            var controlPath = p.Get("control");
            var actionName = p.Get("action", "Jump");
            var value = p.GetFloat("value", 1f) ?? 1f;
            var x = p.GetFloat("x", value) ?? value;
            var y = p.GetFloat("y", 0f) ?? 0f;
            var released = op == "release";

            var devices = FilterDevices(pi.devices, provider);
            if (devices.Count == 0)
                return new ErrorResponse("Player has no paired device" +
                                         (string.IsNullOrEmpty(provider)
                                             ? "."
                                             : $" matching provider '{provider}'. See `list`."));

            var compositeResult = TryDriveButtonComposite(pi, devices, controlPath, actionName, playerIndex, x, y,
                released, op, p);
            if (compositeResult != null) return compositeResult;

            var control = ResolveControl(pi, controlPath, actionName, provider, out var why);
            if (control == null) return new ErrorResponse(why);

            var vec2Control = control as InputControl<Vector2>;
            if (control is not InputControl<float> && vec2Control == null)
                return new ErrorResponse(
                    $"Control '{control.path}' is {control.valueType.Name}; only float (button/axis) and Vector2 (stick) are supported.");

            ApplyValue(control, released ? 0f : value, p, released);

            var label = Describe(actionName, controlPath);
            if (op == "release")
            {
                CancelPending(control);
                UntrackHeld(control);
                return new SuccessResponse($"Released '{label}' on player {playerIndex} ({control.path}).");
            }

            TrackHeld(control);

            var what = vec2Control != null ? $"({x},{y})" : value.ToString();

            if (op == "press")
                return new SuccessResponse(
                    $"Pressed '{label}'={what} on player {playerIndex} ({control.path}). op=release to let go.");

            var holdFrames = Math.Max(1, p.GetInt("hold_frames", 6) ?? 6);
            CancelPending(control);
            var releaseControl = control;
            Pending.Add(new PendingRelease
            {
                Key = releaseControl,
                Release = () =>
                {
                    if (releaseControl is InputControl<Vector2>) WriteVector(releaseControl, Vector2.zero);
                    else WriteFloat(releaseControl, 0f);
                    UntrackHeld(releaseControl);
                },
                ReleaseAtFrame = Time.frameCount + holdFrames
            });
            EnsureHooked();
            return new SuccessResponse(
                $"Tapped '{label}'={what} on player {playerIndex} ({control.path}); auto-release in {holdFrames} frames.");
        }

        private static object TryDriveButtonComposite(PlayerInput pi, List<InputDevice> devices,
            string controlPath, string actionName, int playerIndex, float x, float y, bool released, string op,
            ToolParams p)
        {
            if (!string.IsNullOrEmpty(controlPath)) return null;

            var act = pi.actions != null ? pi.actions.FindAction(actionName) : null;
            if (act == null) return new ErrorResponse($"Player has no action named '{actionName}'. See `list`.");
            if (!act.enabled) act.Enable();
            if (!IsButtonComposite(act, devices)) return null;

            var devs = devices.ToList();
            var n = DriveComposite(act, devs, x, y, released);
            if (n == 0)
                return new ErrorResponse(
                    $"Could not resolve composite parts for '{actionName}' on the player's device(s).");

            CancelPending(act);
            TrackHeldComposite(act, devs, released);

            if (op == "tap")
            {
                var holdFrames = Math.Max(1, p.GetInt("hold_frames", 6) ?? 6);
                Pending.Add(new PendingRelease
                {
                    Key = act,
                    Release = () =>
                    {
                        DriveComposite(act, devs, 0f, 0f, true);
                        TrackHeldComposite(act, devs, true);
                    },
                    ReleaseAtFrame = Time.frameCount + holdFrames
                });
                EnsureHooked();
                return new SuccessResponse(
                    $"Tapped '{actionName}' = ({x},{y}) via {n} key(s) on player {playerIndex}; auto-release in {holdFrames} frames.");
            }

            var vlabel = released ? "(0,0)" : $"({x},{y})";
            return new SuccessResponse(
                $"{(released ? "Released" : "Set")} '{actionName}' = {vlabel} via {n} key(s) on player {playerIndex}.");
        }

        private static void CaptureAndApplySettings()
        {
            var s = InputSystem.settings;
            if (!SessionState.GetBool(KeyCaptured, false))
            {
                SessionState.SetInt(KeyOrigBackground, (int)s.backgroundBehavior);
                SessionState.SetInt(KeyOrigEditorBehavior, (int)s.editorInputBehaviorInPlayMode);
                SessionState.SetBool(KeyOrigRunInBackground, Application.runInBackground);
                SessionState.SetBool(KeyCaptured, true);
            }

            if (Application.isPlaying)
                Application.runInBackground = true;
            if (s.backgroundBehavior != InputSettings.BackgroundBehavior.IgnoreFocus)
                s.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            if (s.editorInputBehaviorInPlayMode !=
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView)
                s.editorInputBehaviorInPlayMode =
                    InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        }

        private static void RestoreSettings()
        {
            if (!SessionState.GetBool(KeyCaptured, false)) return;
            try
            {
                var s = InputSystem.settings;
                s.backgroundBehavior =
                    (InputSettings.BackgroundBehavior)SessionState.GetInt(KeyOrigBackground,
                        (int)InputSettings.BackgroundBehavior.ResetAndDisableNonBackgroundDevices);
                s.editorInputBehaviorInPlayMode =
                    (InputSettings.EditorInputBehaviorInPlayMode)SessionState.GetInt(KeyOrigEditorBehavior, 0);
                Application.runInBackground = SessionState.GetBool(KeyOrigRunInBackground, false);

                // Erase only after a successful restore, so a thrown restore retries on the next op/reload.
                SessionState.EraseBool(KeyCaptured);
                SessionState.EraseInt(KeyOrigBackground);
                SessionState.EraseInt(KeyOrigEditorBehavior);
                SessionState.EraseBool(KeyOrigRunInBackground);
            }
            catch
            {
            }
        }

        private static void Cleanup()
        {
            try
            {
                foreach (var r in Pending.ToArray())
                    try
                    {
                        r.Release();
                    }
                    catch
                    {
                    }

                Pending.Clear();
            }
            catch
            {
            }

            ZeroHeldFromLedger();
            RemoveTrackedDevices();

            if (Application.isPlaying)
                try
                {
                    foreach (var pi in PlayerInput.all.ToArray()) Object.Destroy(pi.gameObject);
                }
                catch
                {
                }

            RestoreSettings();
        }

        private static void ZeroHeldFromLedger()
        {
            var held = ReadList(KeyHeld);
            if (held.Count == 0) return;
            var touched = new HashSet<InputDevice>();
            foreach (var entry in held)
            {
                var sep = entry.IndexOf('|');
                if (sep <= 0) continue;
                if (!int.TryParse(entry.Substring(0, sep), out var devId)) continue;
                var path = entry.Substring(sep + 1);
                var device = InputSystem.devices.FirstOrDefault(d => d.deviceId == devId);
                if (device == null) continue;
                var control = InputControlPath.TryFindControl(device, path);
                if (control == null) continue;
                try
                {
                    if (control is InputControl<Vector2>) WriteVector(control, Vector2.zero);
                    else WriteFloat(control, 0f);
                }
                catch
                {
                }

                touched.Add(device);
            }

            foreach (var d in touched)
                try
                {
                    InputSystem.ResetDevice(d);
                }
                catch
                {
                }

            try
            {
                InputSystem.Update();
            }
            catch
            {
            }

            SessionState.EraseString(KeyHeld);
        }

        private static void RemoveTrackedDevices()
        {
            var ids = new HashSet<string>(ReadList(KeyDevices));
            foreach (var d in InputSystem.devices.ToArray())
            {
                var match = ids.Contains(d.deviceId.ToString()) || d.usages.Any(u => u == VirtualUsage);
                if (!match) continue;
                try
                {
                    InputSystem.RemoveDevice(d);
                }
                catch
                {
                }
            }

            SessionState.EraseString(KeyDevices);
        }

        private static void TrackHeld(InputControl control)
        {
            var entry = $"{control.device.deviceId}|{control.path}";
            var held = ReadList(KeyHeld);
            if (!held.Contains(entry)) held.Add(entry);
            WriteList(KeyHeld, held);
        }

        private static void UntrackHeld(InputControl control)
        {
            var entry = $"{control.device.deviceId}|{control.path}";
            var held = ReadList(KeyHeld);
            if (held.Remove(entry)) WriteList(KeyHeld, held);
        }

        private static void TrackHeldComposite(InputAction action, List<InputDevice> devices, bool released)
        {
            var held = ReadList(KeyHeld);
            var changed = false;
            foreach (var b in action.bindings)
            {
                if (!b.isPartOfComposite) continue;
                foreach (var d in devices)
                {
                    if (!(InputControlPath.TryFindControl(d, b.effectivePath) is InputControl<float> fc)) continue;
                    var entry = $"{fc.device.deviceId}|{fc.path}";
                    if (released)
                    {
                        if (held.Remove(entry)) changed = true;
                    }
                    else if (!held.Contains(entry))
                    {
                        held.Add(entry);
                        changed = true;
                    }

                    break;
                }
            }

            if (changed) WriteList(KeyHeld, held);
        }

        private static List<string> ReadList(string key)
        {
            var raw = SessionState.GetString(key, "");
            if (string.IsNullOrEmpty(raw)) return new List<string>();
            return raw.Split('\n').Where(s => s.Length > 0).ToList();
        }

        private static void WriteList(string key, List<string> list)
        {
            SessionState.SetString(key, string.Join("\n", list));
        }

        private static void UntrackDevice(int deviceId)
        {
            var ids = ReadList(KeyDevices);
            if (ids.Remove(deviceId.ToString())) WriteList(KeyDevices, ids);
        }

        private static bool IsButtonComposite(InputAction action, List<InputDevice> devices)
        {
            foreach (var c in action.controls)
                if (c is InputControl<Vector2> && devices.Contains(c.device))
                    return false;
            foreach (var b in action.bindings)
            {
                if (!b.isPartOfComposite) continue;
                foreach (var d in devices)
                    if (InputControlPath.TryFindControl(d, b.effectivePath) is InputControl<float>)
                        return true;
            }

            return false;
        }

        private static int DriveComposite(InputAction action, List<InputDevice> devices, float x, float y,
            bool released)
        {
            var want = new Dictionary<string, float> { { "up", 0f }, { "down", 0f }, { "left", 0f }, { "right", 0f } };
            if (!released)
            {
                if (y > 0f) want["up"] = Mathf.Abs(y);
                else if (y < 0f) want["down"] = Mathf.Abs(y);
                if (x > 0f) want["right"] = Mathf.Abs(x);
                else if (x < 0f) want["left"] = Mathf.Abs(x);
            }

            var byDevice = new Dictionary<InputDevice, List<KeyValuePair<InputControl<float>, float>>>();
            foreach (var b in action.bindings)
            {
                if (!b.isPartOfComposite) continue;
                var nm = (b.name ?? "").ToLowerInvariant();
                if (!want.ContainsKey(nm)) continue;
                foreach (var d in devices)
                {
                    if (!(InputControlPath.TryFindControl(d, b.effectivePath) is InputControl<float> fc)) continue;
                    if (!byDevice.TryGetValue(d, out var list))
                    {
                        list = new List<KeyValuePair<InputControl<float>, float>>();
                        byDevice[d] = list;
                    }

                    list.Add(new KeyValuePair<InputControl<float>, float>(fc, want[nm]));
                    break;
                }
            }

            var driven = 0;
            foreach (var kv in byDevice)
                using (StateEvent.From(kv.Key, out var eventPtr))
                {
                    foreach (var cv in kv.Value)
                    {
                        cv.Key.WriteValueIntoEvent(cv.Value, eventPtr);
                        driven++;
                    }

                    InputSystem.QueueEvent(eventPtr);
                }

            return driven;
        }

        private static InputControl ResolveControl(PlayerInput pi, string controlPath, string actionName,
            string provider, out string why)
        {
            why = null;
            var candidateDevices = FilterDevices(pi.devices, provider);
            if (candidateDevices.Count == 0)
            {
                why = "Player has no paired device" + (string.IsNullOrEmpty(provider)
                    ? "."
                    : $" matching provider '{provider}'. Devices: {string.Join(", ", pi.devices.Select(d => d.displayName))}");
                return null;
            }

            if (!string.IsNullOrEmpty(controlPath))
            {
                foreach (var d in candidateDevices)
                {
                    var c = InputControlPath.TryFindControl(d, controlPath) ??
                            InputControlPath.TryFindControl(d, "**/" + controlPath);
                    if (c != null) return c;
                }

                why =
                    $"No control '{controlPath}' on device(s): {string.Join(", ", candidateDevices.Select(d => d.displayName))}.";
                return null;
            }

            var action = pi.actions != null ? pi.actions.FindAction(actionName) : null;
            if (action == null)
            {
                why = $"Player has no action named '{actionName}'. See `list`.";
                return null;
            }

            if (!action.enabled) action.Enable();
            if (action.controls.Count == 0)
            {
                why = $"Action '{actionName}' resolves to no control (nothing bound / no paired device).";
                return null;
            }

            foreach (var c in action.controls)
                if (candidateDevices.Contains(c.device))
                    return c;
            return action.controls[0];
        }

        private static void ApplyValue(InputControl control, float scalar, ToolParams p, bool released)
        {
            if (control is InputControl<Vector2>)
            {
                var baseV = p.GetFloat("value", 1f) ?? 1f;
                var v = released
                    ? Vector2.zero
                    : new Vector2(p.GetFloat("x", baseV) ?? baseV, p.GetFloat("y", 0f) ?? 0f);
                WriteVector(control, v);
            }
            else
            {
                WriteFloat(control, scalar);
            }
        }

        private static void WriteFloat(InputControl control, float value)
        {
            if (!(control is InputControl<float> fc)) return;
            using (StateEvent.From(control.device, out var eventPtr))
            {
                fc.WriteValueIntoEvent(value, eventPtr);
                InputSystem.QueueEvent(eventPtr);
            }
        }

        private static void WriteVector(InputControl control, Vector2 value)
        {
            if (!(control is InputControl<Vector2> vc)) return;
            using (StateEvent.From(control.device, out var eventPtr))
            {
                vc.WriteValueIntoEvent(value, eventPtr);
                InputSystem.QueueEvent(eventPtr);
            }
        }

        private static void EnsureHooked()
        {
            if (_hooked) return;
            EditorApplication.update += Pump;
            _hooked = true;
        }

        private static void CancelPending(object key)
        {
            Pending.RemoveAll(r => Equals(r.Key, key));
        }

        private static void Pump()
        {
            if (Pending.Count == 0)
            {
                EditorApplication.update -= Pump;
                _hooked = false;
                return;
            }

            if (!Application.isPlaying)
            {
                foreach (var r in Pending.ToArray())
                    try
                    {
                        r.Release();
                    }
                    catch
                    {
                    }

                Pending.Clear();
                SessionState.EraseString(KeyHeld);
                return;
            }

            var now = Time.frameCount;
            for (var i = Pending.Count - 1; i >= 0; i--)
            {
                if (now < Pending[i].ReleaseAtFrame) continue;
                try
                {
                    Pending[i].Release();
                }
                catch
                {
                }

                Pending.RemoveAt(i);
            }
        }

        private static PlayerInput PlayerAt(int index, out object error)
        {
            error = null;
            if (index < 0 || index >= PlayerInput.all.Count)
            {
                error = new ErrorResponse(
                    $"No player at index {index} (have {PlayerInput.all.Count}). Join one first, or see `list`.");
                return null;
            }

            return PlayerInput.all[index];
        }

        private static List<InputDevice> FilterDevices(IEnumerable<InputDevice> devices, string provider)
        {
            if (string.IsNullOrEmpty(provider)) return devices.ToList();
            return devices.Where(d => MatchesProvider(d, provider)).ToList();
        }

        private static bool MatchesProvider(InputDevice d, string provider)
        {
            var kind = ProviderKind(d);
            var pr = provider.Trim().ToLowerInvariant();
            if (kind == pr) return true;
            return d.name.ToLowerInvariant().Contains(pr)
                   || d.displayName.ToLowerInvariant().Contains(pr)
                   || d.layout.ToLowerInvariant().Contains(pr);
        }

        private static string ProviderKind(InputDevice d)
        {
            switch (d)
            {
                case Gamepad _: return "gamepad";
                case Keyboard _: return "keyboard";
                case Mouse _: return "mouse";
                case Touchscreen _: return "touch";
                default: return d.layout.ToLowerInvariant();
            }
        }

        private static InputDevice ResolveDevice(string providerOrName, bool preferVirtual = false)
        {
            if (string.IsNullOrEmpty(providerOrName)) return null;
            var pr = providerOrName.Trim().ToLowerInvariant();

            if (preferVirtual)
            {
                var virt = InputSystem.devices.FirstOrDefault(d =>
                    d.usages.Any(u => u == VirtualUsage) && MatchesProvider(d, providerOrName));
                if (virt != null) return virt;
            }

            switch (pr)
            {
                case "keyboard": return Keyboard.current ?? InputSystem.devices.FirstOrDefault(d => d is Keyboard);
                case "gamepad": return Gamepad.current ?? InputSystem.devices.FirstOrDefault(d => d is Gamepad);
                case "mouse": return Mouse.current ?? InputSystem.devices.FirstOrDefault(d => d is Mouse);
                case "touch": return Touchscreen.current ?? InputSystem.devices.FirstOrDefault(d => d is Touchscreen);
            }

            return InputSystem.devices.FirstOrDefault(d => MatchesProvider(d, providerOrName));
        }

        private static string NormalizeLayout(string provider)
        {
            switch (provider.Trim().ToLowerInvariant())
            {
                case "keyboard": return "Keyboard";
                case "gamepad": return "Gamepad";
                case "mouse": return "Mouse";
                case "touch": return "Touchscreen";
                default: return provider;
            }
        }

        private static string Describe(string action, string controlPath)
        {
            return !string.IsNullOrEmpty(controlPath) ? controlPath : action;
        }

        public class Parameters
        {
            [ToolParameter(
                "Operation: list, devices, add_device, remove_device, join, pair, leave, leave_all, press, release, tap, reset (default list).")]
            public string Op { get; set; }

            [ToolParameter(
                "Player index into the joined-players list (default 0). Used by leave/pair/press/release/tap.")]
            public int Player { get; set; }

            [ToolParameter("Input action name to drive, e.g. Jump or Move (default Jump). Used by press/release/tap.")]
            public string Action { get; set; }

            [ToolParameter(
                "Explicit control to drive instead of an action, e.g. buttonSouth, leftStick, <Keyboard>/space. Resolved against the player's paired devices.")]
            public string Control { get; set; }

            [ToolParameter(
                "Scalar value for buttons/axes, 0..1 (default 1). Also used as X for a stick if X is omitted.")]
            public float Value { get; set; }

            [ToolParameter("X component for a Vector2 control (stick/Move/Look).")]
            public float X { get; set; }

            [ToolParameter("Y component for a Vector2 control (stick/Move/Look).")]
            public float Y { get; set; }

            [ToolParameter("tap only: frames to hold before auto-release (default 6).")]
            public int HoldFrames { get; set; }

            [ToolParameter(
                "Provider/device: keyboard, gamepad, mouse, touch, or a device name. join/add_device/remove_device/pair use it to pick the device; press uses it to pick which paired device's control to drive.")]
            public string Provider { get; set; }

            [ToolParameter("join only: control scheme name to pair with (optional).")]
            public string Scheme { get; set; }
        }

        private class PendingRelease
        {
            public object Key;
            public Action Release;
            public int ReleaseAtFrame;
        }
    }
}