using System.Reflection;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

public static class InputHelper
{
    private static readonly PropertyInfo CurrentStateProperty =
        typeof(KeyControl).GetProperty(
            "isPressed",
            BindingFlags.Instance |
            BindingFlags.Public |
            BindingFlags.NonPublic);

    public static bool GetKey(Key key)
    {
        if (Keyboard.current == null)
            return false;

        KeyControl control = Keyboard.current[key];

        if (control == null)
            return false;

        if (CurrentStateProperty != null)
            return (bool)CurrentStateProperty.GetValue(control);

        return control.isPressed;
    }

    public static bool GetKeyDown(Key key)
    {
        if (Keyboard.current == null)
            return false;

        return Keyboard.current[key].wasPressedThisFrame;
    }
}
