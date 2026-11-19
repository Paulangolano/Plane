namespace UnityEngine.InputSystem
{
    public class ButtonControl { public bool isPressed => false; public bool wasPressedThisFrame => false; }
    public class Vector2Control { public Vector2 ReadValue() => default; }
    public class Keyboard
    {
        public static Keyboard current => null;
        public ButtonControl wKey, sKey, aKey, dKey, qKey, eKey, rKey, fKey, bKey, vKey, cKey, mKey,
            upArrowKey, downArrowKey, leftArrowKey, rightArrowKey, leftCtrlKey, rightCtrlKey,
            leftShiftKey, rightShiftKey, spaceKey, enterKey, numpadEnterKey;
    }
    public class Mouse { public static Mouse current => null; public ButtonControl rightButton; public Vector2Control delta; }
}
