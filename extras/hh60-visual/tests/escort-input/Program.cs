using System;
using System.Collections.Generic;
#if NATIVE_INPUT
using System.Reflection;
using BepInEx;
#endif
using BepInEx.Configuration;
using TscHh60Visual;
using UnityEngine;

internal static class Program
{
    private static int _passed;
    private static void Check(bool value, string description)
    {
        if (!value) throw new InvalidOperationException(description);
        _passed++;
    }

    private static void Main()
    {
        // Default CI exercises production chord handling with synthetic key state.
        // NativeInput additionally checks the installed parser and shortcut API.
        var input = new ButtonState();
#if NATIVE_INPUT
        FieldInfo current = typeof(UnityInput).GetField("_current", BindingFlags.Static | BindingFlags.NonPublic);
        if (current == null) throw new MissingFieldException("Installed BepInEx input bridge changed.");
        object previous = current.GetValue(null);
        current.SetValue(null, input);
        try
#endif
        {
#if NATIVE_INPUT
            var rightAlt = KeyboardShortcut.Deserialize("RightAlt");
            Check(rightAlt.MainKey == KeyCode.RightAlt, "real parser preserves a modifier-only main key");
#else
            var rightAlt = new KeyboardShortcut(KeyCode.RightAlt);
#endif
            input.Set(KeyCode.RightAlt);
#if NATIVE_INPUT
            Check(rightAlt.IsPressed(), "native shortcut accepts RightAlt alone");
#endif
            Check(EscortManualTriggerInput.IsHeld(rightAlt, input.GetKey), "helper accepts RightAlt alone");
            foreach (KeyCode movement in new[] { KeyCode.W, KeyCode.A, KeyCode.S, KeyCode.D, KeyCode.LeftShift, KeyCode.Space })
            {
                input.Set(KeyCode.RightAlt, movement);
#if NATIVE_INPUT
                Check(!rightAlt.IsPressed(), "reproduce native shortcut rejection with " + movement);
                Check(!rightAlt.IsDown(), "native press edge is also rejected with " + movement);
#endif
                Check(EscortManualTriggerInput.IsHeld(rightAlt, input.GetKey), "held trigger permits unrelated " + movement);
            }
            input.Set(KeyCode.RightAlt, KeyCode.W, KeyCode.LeftShift);
            Check(EscortManualTriggerInput.IsHeld(rightAlt, input.GetKey), "modifier-only trigger works during freecam sprint movement");
            input.Set(KeyCode.W, KeyCode.LeftShift);
            Check(!EscortManualTriggerInput.IsHeld(rightAlt, input.GetKey), "releasing the main trigger stops fire while movement remains held");

            var defaultChord = new KeyboardShortcut(KeyCode.PageDown, KeyCode.RightControl);
            input.Set(KeyCode.PageDown, KeyCode.RightControl, KeyCode.W, KeyCode.LeftShift);
#if NATIVE_INPUT
            Check(!defaultChord.IsPressed(), "reproduce native multi-key shortcut conflict with movement");
#endif
            Check(EscortManualTriggerInput.IsHeld(defaultChord, input.GetKey), "configured multi-key trigger allows movement");
            input.Set(KeyCode.PageDown, KeyCode.W);
            Check(!EscortManualTriggerInput.IsHeld(defaultChord, input.GetKey), "missing required modifier cannot fire");
            input.Set(KeyCode.RightControl, KeyCode.W);
            Check(!EscortManualTriggerInput.IsHeld(defaultChord, input.GetKey), "required modifier alone cannot fire");
            input.Set(KeyCode.PageDown, KeyCode.LeftControl);
            Check(!EscortManualTriggerInput.IsHeld(defaultChord, input.GetKey), "left modifier does not substitute for configured right modifier");

            var custom = new KeyboardShortcut(KeyCode.F7, KeyCode.LeftShift, KeyCode.RightAlt);
            input.Set(KeyCode.F7, KeyCode.LeftShift, KeyCode.RightAlt, KeyCode.A);
            Check(EscortManualTriggerInput.IsHeld(custom, input.GetKey), "all configured modifiers are honored with unrelated movement");
            input.Set(KeyCode.F7, KeyCode.RightAlt, KeyCode.A);
            Check(!EscortManualTriggerInput.IsHeld(custom, input.GetKey), "losing any required modifier stops fire");
            var mouse = new KeyboardShortcut(KeyCode.Mouse4);
            input.Set(KeyCode.Mouse4, KeyCode.D, KeyCode.LeftShift);
            Check(EscortManualTriggerInput.IsHeld(mouse, input.GetKey), "custom mouse trigger works while moving");
            input.Set(KeyCode.D, KeyCode.LeftShift);
            Check(!EscortManualTriggerInput.IsHeld(mouse, input.GetKey), "mouse release stops fire");
            var mouseChord = new KeyboardShortcut(KeyCode.Mouse3, KeyCode.RightControl);
            input.Set(KeyCode.Mouse3, KeyCode.RightControl, KeyCode.W);
            Check(EscortManualTriggerInput.IsHeld(mouseChord, input.GetKey), "mouse plus modifier shortcut works while moving");
            input.Set(KeyCode.Mouse3, KeyCode.W);
            Check(!EscortManualTriggerInput.IsHeld(mouseChord, input.GetKey), "mouse shortcut still requires its configured modifier");
            Check(!EscortManualTriggerInput.IsHeld(KeyboardShortcut.Empty, _ => true), "empty binding stays disabled even if the input provider reports every key held");
            Check(!EscortManualTriggerInput.IsHeld(new KeyboardShortcut(KeyCode.None), _ => true), "explicit None binding cannot fire");
            Check(!EscortManualTriggerInput.IsHeld(rightAlt, null), "missing input provider fails closed");
            Check(!EscortManualTriggerInput.IsHeld(default, input.GetKey), "default shortcut is safe and disabled");
#if NATIVE_INPUT
            Console.WriteLine(_passed + " manual trigger input checks passed using installed BepInEx and Unity key types.");
#else
            Console.WriteLine(_passed + " production manual trigger input checks passed (synthetic key and shortcut types).");
#endif
        }
#if NATIVE_INPUT
        finally { current.SetValue(null, previous); }
#endif
    }

    private sealed class ButtonState
#if NATIVE_INPUT
        : IInputSystem
#endif
    {
        private readonly HashSet<KeyCode> _held = new HashSet<KeyCode>();
        internal void Set(params KeyCode[] keys) { _held.Clear(); foreach (var key in keys) _held.Add(key); }
        public bool GetKey(KeyCode key) => _held.Contains(key);
#if NATIVE_INPUT
        public bool GetKey(string key) => GetKey((KeyCode)Enum.Parse(typeof(KeyCode), key));
        public bool GetKeyDown(KeyCode key) => GetKey(key);
        public bool GetKeyDown(string key) => GetKey(key);
        public bool GetKeyUp(KeyCode key) => !GetKey(key);
        public bool GetKeyUp(string key) => !GetKey(key);
        public bool GetMouseButton(int button) => GetKey((KeyCode)((int)KeyCode.Mouse0 + button));
        public bool GetMouseButtonDown(int button) => GetMouseButton(button);
        public bool GetMouseButtonUp(int button) => !GetMouseButton(button);
        public void ResetInputAxes() { }
        public Vector3 mousePosition => default;
        public Vector2 mouseScrollDelta => default;
        public bool mousePresent => true;
        public bool anyKey => _held.Count != 0;
        public bool anyKeyDown => anyKey;
        public IEnumerable<KeyCode> SupportedKeyCodes => (KeyCode[])Enum.GetValues(typeof(KeyCode));
#endif
    }
}
