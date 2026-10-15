using UnityEditor;
using UnityEngine;

/// <summary>
/// #137: when scripts recompile while Play is running (the editor preference "Script Changes While Playing" left at
/// Recompile And Continue Playing), restart the run instead of continuing it. A domain reload can't keep the
/// simulation's non-serialized state (board tiles, placements, stock), so continuing presents a silently reset run under
/// a still-rendered world. This exits Play once the reload is done and enters it again, fresh.
///
/// Unity has no project-level setting for this (the preference is per user, in EditorPrefs) and its options don't
/// include a restart, hence this hook. With the preference at Stop Playing And Recompile or Recompile After Finished
/// Playing, no reload happens during Play and this does nothing. On by default; a developer who wants to keep playing
/// through a reload turns it off locally (Tools > Dungeon Keeper menu, stored per user): the controls survive the
/// reload (#137's OnEnable rewiring), the simulation's state doesn't.
/// </summary>
[InitializeOnLoad]
internal static class RestartPlayOnScriptReload
{
    private const string PendingKey = "DungeonKeeper.RestartPlayAfterReload"; // SessionState: survives the reload
    private const string EnabledKey = "DungeonKeeper.RestartPlayOnScriptReload"; // EditorPrefs: the local opt-out
    private const string MenuPath = "Tools/Dungeon Keeper/Restart Play On Script Reload";

    private static bool Enabled
    {
        get => EditorPrefs.GetBool(EnabledKey, true);
        set => EditorPrefs.SetBool(EnabledKey, value);
    }

    [MenuItem(MenuPath)]
    private static void Toggle() => Enabled = !Enabled;

    [MenuItem(MenuPath, true)]
    private static bool ToggleValidate()
    {
        Menu.SetChecked(MenuPath, Enabled);
        return true;
    }

    static RestartPlayOnScriptReload()
    {
        AssemblyReloadEvents.beforeAssemblyReload += OnBeforeReload;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;

        if (!SessionState.GetBool(PendingKey, false)) return;
        if (!EditorApplication.isPlaying)
        {
            SessionState.EraseBool(PendingKey); // Play ended during the compile: nothing to restart
            return;
        }

        Debug.Log("Scripts recompiled during Play: restarting the run (#137).");
        EditorApplication.delayCall += EditorApplication.ExitPlaymode;
    }

    // Only a reload while Play is live counts; the flag outlives the reload in SessionState.
    private static void OnBeforeReload()
    {
        if (Enabled && EditorApplication.isPlaying) SessionState.SetBool(PendingKey, true);
    }

    private static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (change != PlayModeStateChange.EnteredEditMode || !SessionState.GetBool(PendingKey, false)) return;
        SessionState.EraseBool(PendingKey);
        EditorApplication.EnterPlaymode();
    }
}
