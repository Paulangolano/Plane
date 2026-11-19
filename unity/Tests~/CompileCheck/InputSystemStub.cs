// Minimal stand-in for the Input System package (only what Skyreach uses), for compile checking.
namespace UnityEngine.InputSystem.Controls
{
    public class KeyControl { public bool isPressed => false; public bool wasPressedThisFrame => false; }
    public class ButtonControl { public bool isPressed => false; public bool wasPressedThisFrame => false; }
    public class Vector2Control { public Vector2 ReadValue() => default; }
}

namespace UnityEngine.InputSystem
{
    using UnityEngine.InputSystem.Controls;

    public enum Key { None, Space, Enter, Comma, Period, LeftBracket, RightBracket, Minus, Equals, A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
        Digit1, Digit2, Digit3, Digit4, Digit5, Digit6, Digit7, Digit8, Digit9, Digit0, LeftShift, RightShift, LeftCtrl, RightCtrl,
        LeftArrow, RightArrow, UpArrow, DownArrow, PageDown, PageUp, NumpadEnter }

    public class Keyboard
    {
        public static Keyboard current => null;
        public KeyControl this[Key key] => null;
    }

    public class Mouse { public static Mouse current => null; public ButtonControl rightButton; public Vector2Control delta; }
}
