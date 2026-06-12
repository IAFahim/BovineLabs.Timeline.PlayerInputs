using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityCliConnector;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Users;

namespace BovineLabs.Timeline.PlayerInputs.Editor.CliTools
{
    /// <summary>
    /// Full-flexibility player-input driver for the new Input System, exposed to the CLI so the
    /// game can be played/tested without a human at the controls. Three axes of flexibility:
    ///
    ///   • PROVIDER  — which device feeds the input: a real keyboard/gamepad/mouse, OR a virtual
    ///                 device created on demand (`add_device`) so it works fully headless.
    ///   • PLAYER ID — which joined player (index) the input is delivered to.
    ///   • INPUT TYPE— buttons/axes (float) and sticks (Vector2) are both supported; you can drive
    ///                 a named input ACTION or an explicit CONTROL path for total control.
    ///
    /// Injection uses a queued <see cref="StateEvent"/> + WriteValueIntoEvent (correct for bitfield
    /// keyboard keys, and the only thing that works for Vector2 sticks), consumed by the play loop
    /// next frame so the ECS input consumer sees a clean edge. `tap` auto-releases after N frames.
    /// `editorInputBehaviorInPlayMode` is forced to AllDeviceInputAlwaysGoesToGameView so Game-View
    /// focus never gates injected input. Purely Input-System level — no ECS writes.
    /// </summary>
    [UnityCliTool(
        Name = "player_input",
        Group = "vex",
        Description = "Drive player input via the new Input System with full flexibility (provider/device, player id, button/axis/Vector2). ops: list, devices, add_device, remove_device, join, pair, leave, leave_all, press, release, tap.")]
    public static class PlayerInputTool
    {
        public class Parameters
        {
            [ToolParameter("Operation: list, devices, add_device, remove_device, join, pair, leave, leave_all, press, release, tap (default list).")]
            public string Op { get; set; }

            [ToolParameter("Player index into the joined-players list (default 0). Used by leave/pair/press/release/tap.")]
            public int Player { get; set; }

            [ToolParameter("Input action name to drive, e.g. Jump or Move (default Jump). Used by press/release/tap.")]
            public string Action { get; set; }

            [ToolParameter("Explicit control to drive instead of an action, e.g. buttonSouth, leftStick, <Keyboard>/space. Resolved against the player's paired devices.")]
            public string Control { get; set; }

            [ToolParameter("Scalar value for buttons/axes, 0..1 (default 1). Also used as X for a stick if X is omitted.")]
            public float Value { get; set; }

            [ToolParameter("X component for a Vector2 control (stick/Move/Look).")]
            public float X { get; set; }

            [ToolParameter("Y component for a Vector2 control (stick/Move/Look).")]
            public float Y { get; set; }

            [ToolParameter("tap only: frames to hold before auto-release (default 6).")]
            public int HoldFrames { get; set; }

            [ToolParameter("Provider/device: keyboard, gamepad, mouse, touch, or a device name. join/add_device/remove_device/pair use it to pick the device; press uses it to pick which paired device's control to drive.")]
            public string Provider { get; set; }

            [ToolParameter("join only: control scheme name to pair with (optional).")]
            public string Scheme { get; set; }
        }

        private class PendingRelease
        {
            public InputControl Control;
            public int ReleaseAtFrame;
        }

        private static readonly List<PendingRelease> Pending = new List<PendingRelease>();
        private static bool _hooked;

        public static object HandleCommand(JObject @params)
        {
            var p = new ToolParams(@params);
            var op = (p.Get("op", "list") ?? "list").Trim().ToLowerInvariant();

            switch (op)
            {
                case "list": return List();
                case "devices": return Devices();
                case "add_device": return AddDeviceOp(p.Get("provider", null));
                case "remove_device": return RemoveDeviceOp(p.Get("provider", null));
                case "join": return Join(p.Get("provider", null), p.Get("scheme", null));
                case "pair": return Pair(p.GetInt("player", 0) ?? 0, p.Get("provider", null));
                case "leave": return Leave(p.GetInt("player", 0) ?? 0);
                case "leave_all": return LeaveAll();
                case "press":
                case "release":
                case "tap": return Drive(op, p);
                default:
                    return new ErrorResponse(
                        $"Unknown op '{op}'. Use: list, devices, add_device, remove_device, join, pair, leave, leave_all, press, release, tap.");
            }
        }

        // ---- queries ------------------------------------------------------

        private static object List()
        {
            var mgr = PlayerInputManager.instance;
            var players = PlayerInput.all.Select((pi, i) => new
            {
                index = i,
                playerIndex = pi.playerIndex,
                name = pi.gameObject.name,
                devices = pi.devices.Select(d => d.displayName).ToArray(),
                currentMap = pi.currentActionMap != null ? pi.currentActionMap.name : null,
                actions = pi.actions != null
                    ? pi.actions.Where(a => a.actionMap == pi.currentActionMap).Select(a => a.name).ToArray()
                    : new string[0],
            }).ToArray();

            return new SuccessResponse(
                $"{players.Length} player(s) joined" + (mgr == null ? " (no PlayerInputManager in scene)" : ""),
                new
                {
                    isPlaying = Application.isPlaying,
                    manager = mgr != null
                        ? (object)new { joinBehavior = mgr.joinBehavior.ToString(), playerCount = mgr.playerCount, joiningEnabled = mgr.joiningEnabled }
                        : null,
                    playerCount = players.Length,
                    players,
                    pendingReleases = Pending.Count,
                });
        }

        private static object Devices()
        {
            var devices = InputSystem.devices.Select(d => new
            {
                name = d.name,
                displayName = d.displayName,
                layout = d.layout,
                deviceId = d.deviceId,
                added = d.added,
                kind = ProviderKind(d),
            }).ToArray();
            return new SuccessResponse($"{devices.Length} input device(s) present.", new { devices });
        }

        // ---- virtual devices ----------------------------------------------

        private static object AddDeviceOp(string provider)
        {
            if (string.IsNullOrEmpty(provider))
                return new ErrorResponse("add_device needs a provider: keyboard, gamepad, mouse, touch (or a layout name).");
            string layout = NormalizeLayout(provider);
            InputDevice device;
            try { device = InputSystem.AddDevice(layout); }
            catch (System.Exception e) { return new ErrorResponse($"Could not add device of layout '{layout}': {e.Message}"); }
            if (device == null) return new ErrorResponse($"AddDevice('{layout}') returned null.");
            return new SuccessResponse(
                $"Added virtual {device.displayName} (layout {device.layout}, id {device.deviceId}).",
                new { name = device.name, layout = device.layout, deviceId = device.deviceId });
        }

        private static object RemoveDeviceOp(string providerOrName)
        {
            var device = ResolveDevice(providerOrName);
            if (device == null) return new ErrorResponse($"No device matching '{providerOrName}'. See `devices`.");
            var label = device.displayName;
            InputSystem.RemoveDevice(device);
            return new SuccessResponse($"Removed device {label}.");
        }

        // ---- join / pair / leave ------------------------------------------

        private static object Join(string provider, string scheme)
        {
            if (!Application.isPlaying) return new ErrorResponse("join needs play mode (it instantiates the player prefab).");
            var mgr = PlayerInputManager.instance;
            if (mgr == null)
                return new ErrorResponse("No PlayerInputManager in the scene — cannot join. Add one, or instantiate the player prefab directly.");

            InputDevice device;
            if (!string.IsNullOrEmpty(provider))
            {
                device = ResolveDevice(provider);
                if (device == null)
                    return new ErrorResponse($"No '{provider}' device present. Plug one in or `add_device` a virtual one first. See `devices`.");
            }
            else
            {
                device = (InputDevice)Keyboard.current ?? (InputDevice)Gamepad.current ?? (InputDevice)Mouse.current;
            }

            var pi = mgr.JoinPlayer(-1, -1, scheme, device);
            if (pi == null)
                return new ErrorResponse("JoinPlayer returned null (joining disabled, max players reached, or no free device). Check `list`.");
            return new SuccessResponse(
                $"Joined player {pi.playerIndex} ('{pi.gameObject.name}') on {(device != null ? device.displayName : "no device")}.",
                new { playerIndex = pi.playerIndex, name = pi.gameObject.name, devices = pi.devices.Select(d => d.displayName).ToArray() });
        }

        private static object Pair(int index, string providerOrName)
        {
            if (!Application.isPlaying) return new ErrorResponse("pair needs play mode.");
            var pi = PlayerAt(index, out var err);
            if (pi == null) return err;
            var device = ResolveDevice(providerOrName);
            if (device == null) return new ErrorResponse($"No device matching '{providerOrName}'. See `devices`.");
            InputUser.PerformPairingWithDevice(device, pi.user, InputUserPairingOptions.None);
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
            if (!Application.isPlaying) return new ErrorResponse("leave_all needs play mode.");
            var count = PlayerInput.all.Count;
            foreach (var pi in PlayerInput.all.ToArray()) Object.Destroy(pi.gameObject);
            return new SuccessResponse($"Removed {count} player(s).");
        }

        // ---- input injection ----------------------------------------------

        private static object Drive(string op, ToolParams p)
        {
            if (!Application.isPlaying) return new ErrorResponse($"{op} needs play mode.");

            int playerIndex = p.GetInt("player", 0) ?? 0;
            var pi = PlayerAt(playerIndex, out var err);
            if (pi == null) return err;

            // Never let Game-View focus gate injected device input.
            InputSystem.settings.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;

            string provider = p.Get("provider", null);
            string controlPath = p.Get("control", null);
            string actionName = p.Get("action", "Jump");

            var control = ResolveControl(pi, controlPath, actionName, provider, out var why);
            if (control == null) return new ErrorResponse(why);

            float value = p.GetFloat("value", 1f) ?? 1f;
            bool released = op == "release";
            var floatControl = control as InputControl<float>;
            var vec2Control = control as InputControl<Vector2>;

            if (floatControl == null && vec2Control == null)
                return new ErrorResponse($"Control '{control.path}' is {control.valueType.Name}; only float (button/axis) and Vector2 (stick) are supported.");

            ApplyValue(control, released ? 0f : value, p, released);

            string label = Describe(actionName, controlPath);
            if (op == "release")
            {
                CancelPending(control);
                return new SuccessResponse($"Released '{label}' on player {playerIndex} ({control.path}).");
            }

            string what = vec2Control != null
                ? $"({p.GetFloat("x", value) ?? value},{p.GetFloat("y", 0f) ?? 0f})"
                : value.ToString();

            if (op == "press")
                return new SuccessResponse($"Pressed '{label}'={what} on player {playerIndex} ({control.path}). op=release to let go.");

            int holdFrames = System.Math.Max(1, p.GetInt("hold_frames", 6) ?? 6);
            CancelPending(control);
            Pending.Add(new PendingRelease { Control = control, ReleaseAtFrame = Time.frameCount + holdFrames });
            EnsureHooked();
            return new SuccessResponse($"Tapped '{label}'={what} on player {playerIndex} ({control.path}); auto-release in {holdFrames} frames.");
        }

        // Resolve the control to drive: explicit control path first, else the action's control
        // (preferring one on the requested provider's device).
        private static InputControl ResolveControl(PlayerInput pi, string controlPath, string actionName, string provider, out string why)
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
                    var c = InputControlPath.TryFindControl(d, controlPath) ?? InputControlPath.TryFindControl(d, "**/" + controlPath);
                    if (c != null) return c;
                }
                why = $"No control '{controlPath}' on device(s): {string.Join(", ", candidateDevices.Select(d => d.displayName))}.";
                return null;
            }

            var action = pi.actions != null ? pi.actions.FindAction(actionName, throwIfNotFound: false) : null;
            if (action == null) { why = $"Player has no action named '{actionName}'. See `list`."; return null; }
            if (!action.enabled) action.Enable();
            if (action.controls.Count == 0) { why = $"Action '{actionName}' resolves to no control (nothing bound / no paired device)."; return null; }

            // Prefer a control on a candidate device (honours provider filter).
            foreach (var c in action.controls)
                if (candidateDevices.Contains(c.device)) return c;
            return action.controls[0];
        }

        private static void ApplyValue(InputControl control, float scalar, ToolParams p, bool released)
        {
            if (control is InputControl<Vector2>)
            {
                float baseV = p.GetFloat("value", 1f) ?? 1f;
                var v = released ? Vector2.zero : new Vector2(p.GetFloat("x", baseV) ?? baseV, p.GetFloat("y", 0f) ?? 0f);
                WriteVector(control, v);
            }
            else
            {
                WriteFloat(control, scalar);
            }
        }

        // ---- low-level state-event writes ---------------------------------

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

        // ---- auto-release pump --------------------------------------------

        private static void EnsureHooked() { if (_hooked) return; EditorApplication.update += Pump; _hooked = true; }
        private static void CancelPending(InputControl control) => Pending.RemoveAll(r => r.Control == control);

        private static void Pump()
        {
            if (Pending.Count == 0) { EditorApplication.update -= Pump; _hooked = false; return; }
            if (!Application.isPlaying) { Pending.Clear(); return; }
            int now = Time.frameCount;
            for (int i = Pending.Count - 1; i >= 0; i--)
            {
                if (now < Pending[i].ReleaseAtFrame) continue;
                try
                {
                    var c = Pending[i].Control;
                    if (c is InputControl<Vector2>) WriteVector(c, Vector2.zero);
                    else WriteFloat(c, 0f);
                }
                catch { /* control may have gone with a destroyed player/device */ }
                Pending.RemoveAt(i);
            }
        }

        // ---- helpers ------------------------------------------------------

        private static PlayerInput PlayerAt(int index, out object error)
        {
            error = null;
            if (index < 0 || index >= PlayerInput.all.Count)
            {
                error = new ErrorResponse($"No player at index {index} (have {PlayerInput.all.Count}). Join one first, or see `list`.");
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

        private static InputDevice ResolveDevice(string providerOrName)
        {
            if (string.IsNullOrEmpty(providerOrName)) return null;
            var pr = providerOrName.Trim().ToLowerInvariant();
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
                default: return provider; // assume it's a real layout name
            }
        }

        private static string Describe(string action, string controlPath)
            => !string.IsNullOrEmpty(controlPath) ? controlPath : action;
    }
}
