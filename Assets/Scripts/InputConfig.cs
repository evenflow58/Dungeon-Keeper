using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// The one place player-facing bindings are defined (ticket #30). Consumers (CameraController, TileHover,
/// DigDesignator, PlacementController) call CreateActions() for their own set — created per consumer so each
/// keeps its own enable/disable lifecycle — and never hardcode a binding path or read a device directly.
///
/// Overrides use the Input System's own format (SaveBindingOverridesAsJson / LoadBindingOverridesFromJson),
/// persisted in PlayerPrefs, so a future settings menu can rebind interactively against these actions. Every
/// binding has a stable id derived from its name: overrides saved in one session match the freshly built
/// actions in the next. Overrides apply to action sets created after they're set (component creation / scene
/// load) — there's no live rebinding of sets already in use.
/// </summary>
public static class InputConfig
{
    public const string DefaultPrefsKey = "DungeonKeeper.BindingOverrides";

    public const string Pan = "Pan";
    public const string PanDrag = "PanDrag";
    public const string Zoom = "Zoom";
    public const string PointerPosition = "PointerPosition";
    public const string Designate = "Designate";
    public const string ClearOrAbort = "ClearOrAbort";
    public const string Abort = "Abort";

    private const string MapName = "Player";

    /// <summary>Where overrides are stored. Tests point this at their own key.</summary>
    public static string PrefsKey { get; set; } = DefaultPrefsKey;

    /// <summary>One consumer's actions, built from the shared defaults plus the stored overrides.</summary>
    public sealed class Actions : IDisposable
    {
        public InputActionMap Map { get; }
        public InputAction Pan => Map[InputConfig.Pan];                         // Vector2: WASD + arrow keys
        public InputAction PanDrag => Map[InputConfig.PanDrag];                 // Button: middle mouse
        public InputAction Zoom => Map[InputConfig.Zoom];                       // Vector2: mouse wheel
        public InputAction PointerPosition => Map[InputConfig.PointerPosition]; // Vector2: mouse position
        public InputAction Designate => Map[InputConfig.Designate];             // Button: left mouse
        public InputAction ClearOrAbort => Map[InputConfig.ClearOrAbort];       // Button: right mouse
        public InputAction Abort => Map[InputConfig.Abort];                     // Button: Esc

        internal Actions(InputActionMap map) { Map = map; }

        public void Enable() => Map.Enable();
        public void Disable() => Map.Disable();
        public void Dispose() => Map.Dispose();
    }

    /// <summary>A fresh action set with the defaults and the stored overrides applied (not yet enabled).</summary>
    public static Actions CreateActions()
    {
        InputActionMap map = BuildDefaultMap();
        string json = PlayerPrefs.GetString(PrefsKey, "");
        if (!string.IsNullOrEmpty(json)) map.LoadBindingOverridesFromJson(json);
        return new Actions(map);
    }

    /// <summary>
    /// Rebinds one binding of an action and saves it. bindingIndex counts the action's bindings in order
    /// (for Pan: 0 is the WASD composite, 1–4 its Up/Down/Left/Right parts, 5 the arrow composite, 6–9 its parts).
    /// Applies to action sets created from now on.
    /// </summary>
    public static void SetOverride(string actionName, string newPath, int bindingIndex = 0)
    {
        using (Actions current = CreateActions())
        {
            InputAction action = current.Map.FindAction(actionName, throwIfNotFound: true);
            action.ApplyBindingOverride(bindingIndex, newPath);
            PlayerPrefs.SetString(PrefsKey, current.Map.SaveBindingOverridesAsJson());
            PlayerPrefs.Save();
        }
    }

    /// <summary>Forgets every override: action sets created from now on use the default table.</summary>
    public static void ResetToDefaults()
    {
        PlayerPrefs.DeleteKey(PrefsKey);
        PlayerPrefs.Save();
    }

    /// <summary>The paths an action actually binds to (overrides applied), skipping composite headers.</summary>
    public static List<string> EffectivePaths(InputAction action)
    {
        var paths = new List<string>();
        foreach (InputBinding b in action.bindings)
            if (!b.isComposite) paths.Add(b.effectivePath);
        return paths;
    }

    // ---- defaults: the only binding paths in the project ----

    private static InputActionMap BuildDefaultMap()
    {
        var map = new InputActionMap(MapName);

        InputAction pan = map.AddAction(Pan, InputActionType.Value, expectedControlLayout: "Vector2");
        AddComposite(pan, "WASD", "<Keyboard>/w", "<Keyboard>/s", "<Keyboard>/a", "<Keyboard>/d");
        AddComposite(pan, "Arrows", "<Keyboard>/upArrow", "<Keyboard>/downArrow", "<Keyboard>/leftArrow", "<Keyboard>/rightArrow");

        AddSingle(map.AddAction(PanDrag, InputActionType.Button), "<Mouse>/middleButton");
        AddSingle(map.AddAction(Zoom, InputActionType.Value, expectedControlLayout: "Vector2"), "<Mouse>/scroll");
        AddSingle(map.AddAction(PointerPosition, InputActionType.Value, expectedControlLayout: "Vector2"), "<Mouse>/position");
        AddSingle(map.AddAction(Designate, InputActionType.Button), "<Mouse>/leftButton");
        AddSingle(map.AddAction(ClearOrAbort, InputActionType.Button), "<Mouse>/rightButton");
        AddSingle(map.AddAction(Abort, InputActionType.Button), "<Keyboard>/escape");

        return map;
    }

    private static void AddSingle(InputAction action, string path)
    {
        action.AddBinding(new InputBinding
        {
            path = path,
            id = StableId(action.name),
        });
    }

    // A 2DVector composite (the default digital-normalized mode, as before) with stable part ids.
    private static void AddComposite(InputAction action, string name, string up, string down, string left, string right)
    {
        string key = action.name + "/" + name;
        action.AddBinding(new InputBinding { name = name, path = "2DVector", isComposite = true, id = StableId(key) });
        AddPart(action, key, "Up", up);
        AddPart(action, key, "Down", down);
        AddPart(action, key, "Left", left);
        AddPart(action, key, "Right", right);
    }

    private static void AddPart(InputAction action, string compositeKey, string part, string path)
    {
        action.AddBinding(new InputBinding
        {
            name = part,
            path = path,
            isPartOfComposite = true,
            id = StableId(compositeKey + "/" + part),
        });
    }

    // Deterministic per-binding id (a GUID from the binding's name), so saved overrides match across sessions.
    private static Guid StableId(string bindingKey)
    {
        using (MD5 md5 = MD5.Create())
        {
            return new Guid(md5.ComputeHash(Encoding.UTF8.GetBytes(MapName + "/" + bindingKey)));
        }
    }
}
