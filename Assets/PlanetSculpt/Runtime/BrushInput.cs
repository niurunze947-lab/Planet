using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Yongxian.PlanetSculpt
{
    // Use the project's existing Input System without changing player settings.
    public static class BrushInput
    {
        public static Vector3 mousePosition => Mouse.current != null ? (Vector3)Mouse.current.position.ReadValue() : Vector3.zero;
        public static Vector2 mouseScrollDelta => Mouse.current != null ? Mouse.current.scroll.ReadValue() / 120f : Vector2.zero;
        public static bool GetMouseButton(int button) => Mouse.current != null &&
            (button == 0 ? Mouse.current.leftButton.isPressed : Mouse.current.rightButton.isPressed);
        public static float GetAxis(string axis) => Mouse.current == null ? 0f :
            (axis == "Mouse X" ? Mouse.current.delta.ReadValue().x : Mouse.current.delta.ReadValue().y) * 0.1f;
        static KeyControl KeyFor(KeyCode key)
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return null;
            switch (key)
            {
                case KeyCode.Z: return keyboard.zKey;
                case KeyCode.E: return keyboard.eKey;
                case KeyCode.F8: return keyboard.f8Key;
                case KeyCode.LeftControl: return keyboard.leftCtrlKey;
                case KeyCode.RightControl: return keyboard.rightCtrlKey;
                case KeyCode.LeftShift: return keyboard.leftShiftKey;
                case KeyCode.RightShift: return keyboard.rightShiftKey;
                case KeyCode.LeftBracket: return keyboard.leftBracketKey;
                case KeyCode.RightBracket: return keyboard.rightBracketKey;
                default: return null;
            }
        }
        public static bool GetKey(KeyCode key) => KeyFor(key)?.isPressed ?? false;
        public static bool GetKeyDown(KeyCode key) => KeyFor(key)?.wasPressedThisFrame ?? false;
    }
}
