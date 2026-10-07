using System;
using BepInEx.Configuration;
using UnityEngine;

namespace TscHh60Visual
{
    internal static class EscortManualTriggerInput
    {
        // KeyboardShortcut.IsPressed also rejects unrelated held keyboard keys.
        // A gameplay trigger must allow movement while requiring its own chord.
        // Input focus, menus, pause and weapon permission remain caller-owned.
        internal static bool IsHeld(KeyboardShortcut shortcut, Func<KeyCode, bool> keyHeld)
        {
            if (keyHeld == null || shortcut.MainKey == KeyCode.None || !keyHeld(shortcut.MainKey)) return false;
            foreach (KeyCode modifier in shortcut.Modifiers)
                if (modifier == KeyCode.None || !keyHeld(modifier)) return false;
            return true;
        }
    }
}
