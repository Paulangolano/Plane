using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM && SKYREACH_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
#endif

namespace Skyreach
{
    /// <summary>
    /// Keyboard controls for testing in the Unity Editor (shared by both aircraft):
    ///  W/S pitch (W = nose down), A/D roll, Q/E rudder, Shift/Ctrl (or R/F) throttle,
    ///  Space afterburner, B brakes, , / . left / right brake, T/G trim,
    ///  Enter restart, V chase camera, C comfort vignette, M mute, 1/2 pick aircraft.
    /// Aircraft-specific keys come from the aircraft's <see cref="KeyMap"/>.
    /// </summary>
    public sealed class KeyboardInput
    {
        KeyMap map = new KeyMap();
        readonly Dictionary<string, float> holdTimes = new Dictionary<string, float>();

        public void SetKeyMap(KeyMap keyMap)
        {
            map = keyMap ?? new KeyMap();
            holdTimes.Clear();
        }

        public bool ReadInput(float dt, InputState s)
        {
#if ENABLE_INPUT_SYSTEM && SKYREACH_INPUT_SYSTEM
            if (Keyboard.current == null) return false;
#endif
            s.Pitch = Axis(Held("W") || Held("UpArrow"), Held("S") || Held("DownArrow"));
            s.Roll = Axis(Held("A") || Held("LeftArrow"), Held("D") || Held("RightArrow"));
            s.Yaw = Axis(Held("Q"), Held("E"));
            float t = Axis(Held("LeftCtrl") || Held("RightCtrl") || Held("F"), Held("LeftShift") || Held("RightShift") || Held("R"));
            s.Throttle = Mathf.Clamp01(s.Throttle + t * 0.6f * dt);
            s.Boost = Held("Space");
            s.Brake = Held("B");
            s.BrakeLeft = Held("Comma") ? 1 : 0;
            s.BrakeRight = Held("Period") ? 1 : 0;
            s.TrimRate = Axis(Held("T"), Held("G"));
            if (Down("Enter") || Down("NumpadEnter")) s.Fire("restart");
            if (Down("V")) s.Fire("toggleView");
            if (Down("C")) s.Fire("cycleComfort");
            if (Down("M")) s.Fire("toggleMute");
            if (Down("Digit1")) s.Fire("selectJet");
            if (Down("Digit2")) s.Fire("selectSr22");
            foreach (var kv in map.Keys) if (Down(kv.Key)) s.Fire(kv.Value);
            foreach (var kv in map.HoldKeys)
            {
                if (Held(kv.Key))
                {
                    holdTimes.TryGetValue(kv.Key, out float before);
                    holdTimes[kv.Key] = before + dt;
                    if (before < kv.Value.seconds && holdTimes[kv.Key] >= kv.Value.seconds) s.Fire(kv.Value.action);
                }
                else holdTimes[kv.Key] = 0;
            }
            return true;
        }

        static float Axis(bool negative, bool positive) => (positive ? 1f : 0f) - (negative ? 1f : 0f);

#if ENABLE_INPUT_SYSTEM && SKYREACH_INPUT_SYSTEM
        static KeyControl Key(string name)
        {
            if (!System.Enum.TryParse(name, out UnityEngine.InputSystem.Key k)) return null;
            return Keyboard.current?[k];
        }
        static bool Held(string name) => Key(name)?.isPressed ?? false;
        static bool Down(string name) => Key(name)?.wasPressedThisFrame ?? false;
#else
        static KeyCode Code(string name)
        {
            switch (name)
            {
                case "Enter": return KeyCode.Return;
                case "LeftCtrl": return KeyCode.LeftControl;
                case "RightCtrl": return KeyCode.RightControl;
                case "Equals": return KeyCode.Equals;
                case "NumpadEnter": return KeyCode.KeypadEnter;
            }
            if (name.StartsWith("Digit")) name = "Alpha" + name.Substring(5);
            return System.Enum.TryParse(name, out KeyCode c) ? c : KeyCode.None;
        }
        static bool Held(string name) => Input.GetKey(Code(name));
        static bool Down(string name) => Input.GetKeyDown(Code(name));
#endif
    }
}
