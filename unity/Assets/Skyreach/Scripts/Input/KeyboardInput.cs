using UnityEngine;
#if ENABLE_INPUT_SYSTEM && SKYREACH_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Skyreach
{
    /// <summary>
    /// Keyboard controls for testing in the Unity Editor without a headset.
    ///  W/S or Up/Down pitch (W = nose down), A/D or Left/Right roll, Q/E rudder,
    ///  Shift/Ctrl (or R/F) throttle, Space afterburner, B brakes,
    ///  Enter restart, V chase camera, C comfort vignette, M mute.
    /// </summary>
    public sealed class KeyboardInput
    {
        public bool ReadInput(float dt, InputState s)
        {
#if ENABLE_INPUT_SYSTEM && SKYREACH_INPUT_SYSTEM
            var k = Keyboard.current;
            if (k == null) return false;
            s.Pitch = Axis(k.wKey.isPressed || k.upArrowKey.isPressed, k.sKey.isPressed || k.downArrowKey.isPressed);
            s.Roll = Axis(k.aKey.isPressed || k.leftArrowKey.isPressed, k.dKey.isPressed || k.rightArrowKey.isPressed);
            s.Yaw = Axis(k.qKey.isPressed, k.eKey.isPressed);
            float t = Axis(k.leftCtrlKey.isPressed || k.rightCtrlKey.isPressed || k.fKey.isPressed,
                           k.leftShiftKey.isPressed || k.rightShiftKey.isPressed || k.rKey.isPressed);
            s.Boost = k.spaceKey.isPressed;
            s.Brake = k.bKey.isPressed;
            if (k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame) s.Restart = true;
            if (k.vKey.wasPressedThisFrame) s.ToggleView = true;
            if (k.cKey.wasPressedThisFrame) s.CycleComfort = true;
            if (k.mKey.wasPressedThisFrame) s.ToggleMute = true;
#else
            s.Pitch = Axis(Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow), Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow));
            s.Roll = Axis(Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow), Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow));
            s.Yaw = Axis(Input.GetKey(KeyCode.Q), Input.GetKey(KeyCode.E));
            float t = Axis(Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) || Input.GetKey(KeyCode.F),
                           Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) || Input.GetKey(KeyCode.R));
            s.Boost = Input.GetKey(KeyCode.Space);
            s.Brake = Input.GetKey(KeyCode.B);
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) s.Restart = true;
            if (Input.GetKeyDown(KeyCode.V)) s.ToggleView = true;
            if (Input.GetKeyDown(KeyCode.C)) s.CycleComfort = true;
            if (Input.GetKeyDown(KeyCode.M)) s.ToggleMute = true;
#endif
            s.Throttle = Mathf.Clamp01(s.Throttle + t * 0.6f * dt);
            return true;
        }

        static float Axis(bool negative, bool positive) => (positive ? 1f : 0f) - (negative ? 1f : 0f);
    }
}
