using System;
using UnityEngine;

// Only the data contract consumed by production EscortManualTriggerInput.
// Native parser and IsPressed/IsDown behavior are exercised by NativeInput=true.
namespace UnityEngine
{
    public enum KeyCode { None, RightAlt, W, A, S, D, LeftShift, Space, PageDown, RightControl, LeftControl, F7, Mouse3, Mouse4 }
}
namespace BepInEx.Configuration
{
    public readonly struct KeyboardShortcut
    {
        public readonly KeyCode MainKey;
        private readonly KeyCode[] _modifiers;
        public KeyCode[] Modifiers => _modifiers ?? Array.Empty<KeyCode>();
        public KeyboardShortcut(KeyCode key, params KeyCode[] modifiers) { MainKey = key; _modifiers = modifiers; }
        public static KeyboardShortcut Empty => default;
    }
}
