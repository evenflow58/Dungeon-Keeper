using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The one place game hotkeys ask whether the player is typing (#126): while a uGUI InputField has focus, digits,
/// Space, WASD and Esc belong to the field. A field gives its focus up on the Esc press itself, possibly before a
/// hotkey reader updates that frame, so a field focused at the end of the previous frame still counts: that Esc is the
/// field's (defocus), and only the next press reaches the game. Track() records the focus; StockPanel, the owner of the
/// only field, calls it every LateUpdate.
/// </summary>
public static class TextInputFocus
{
    private static int lastFocusedFrame = int.MinValue;

    /// <summary>True while the player is typing into a field, or was at the end of the previous frame.</summary>
    public static bool IsTyping => IsTypingAt(IsFocused(EventSystem.current), lastFocusedFrame, Time.frameCount);

    /// <summary>Whether the event system's selected object is an InputField with keyboard focus.</summary>
    public static bool IsFocused(EventSystem eventSystem)
    {
        GameObject selected = eventSystem != null ? eventSystem.currentSelectedGameObject : null;
        return selected != null && selected.TryGetComponent(out InputField field) && field.isFocused;
    }

    /// <summary>The typing rule on its own: focused now, or focused as of the frame before this one.</summary>
    public static bool IsTypingAt(bool focusedNow, int lastFocusedFrame, int frame) =>
        focusedNow || (lastFocusedFrame != int.MinValue && lastFocusedFrame >= frame - 1);

    /// <summary>Records this frame's focus. Called at the end of each frame (LateUpdate).</summary>
    public static void Track()
    {
        if (IsFocused(EventSystem.current)) lastFocusedFrame = Time.frameCount;
    }
}
