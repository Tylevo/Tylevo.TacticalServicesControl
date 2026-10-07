using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;

// Bounded orchestration doubles. This is not a Unity engine, Harmony patch,
// target-geometry, native ballistics or visual/audio integration test.
namespace UnityEngine
{
    public class Object
    {
        internal bool Destroyed;
        public static bool operator ==(Object a, Object b) =>
            (ReferenceEquals(a, null) || a.Destroyed) && (ReferenceEquals(b, null) || b.Destroyed) || ReferenceEquals(a, b);
        public static bool operator !=(Object a, Object b) => !(a == b);
        public override bool Equals(object other) => ReferenceEquals(this, other);
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
        public static void Destroy(Object value)
        {
            if (value == null) return;
            if (value is GameObject game)
            {
                foreach (var child in game.transform.Children.ToArray()) Destroy(child.gameObject);
                game.transform.SetParent(null, false);
                foreach (var component in game.Components) component.Destroyed = true;
            }
            value.Destroyed = true;
        }
    }
    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform => gameObject.transform;
        public string name => gameObject.name;
        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : Component => gameObject.GetComponentsInChildren<T>(includeInactive);
    }
    public class GameObject : Object
    {
        public readonly Transform transform;
        internal readonly List<Component> Components = new List<Component>();
        public string name;
        public bool activeSelf = true;
        public bool activeInHierarchy => !Destroyed && activeSelf && (transform.parent == null || transform.parent.gameObject.activeInHierarchy);
        public GameObject(string name) { this.name = name; transform = new Transform { gameObject = this }; Components.Add(transform); }
        public void SetActive(bool value) => activeSelf = value;
        public T AddComponent<T>() where T : Component, new() { var item = new T { gameObject = this }; Components.Add(item); return item; }
        public T[] GetComponentsInChildren<T>(bool inactive) where T : Component =>
            Components.OfType<T>().Concat(transform.Children.SelectMany(c => c.gameObject.GetComponentsInChildren<T>(inactive))).ToArray();
    }
    public sealed class Transform : Component
    {
        public Transform parent;
        internal readonly List<Transform> Children = new List<Transform>();
        public void SetParent(Transform value, bool worldPositionStays) { parent?.Children.Remove(this); parent = value; parent?.Children.Add(this); }
    }
    public class MonoBehaviour : Component
    {
        public Coroutine StartCoroutine(IEnumerator routine) { var c = new Coroutine { Routine = routine }; c.Running = routine.MoveNext(); return c; }
        public void StopCoroutine(Coroutine coroutine) { coroutine.Running = false; }
    }
    public sealed class Coroutine { internal IEnumerator Routine; internal bool Running; }
    public struct Vector3 { }
    public static class Mathf { public static float Max(float first, float second) => Math.Max(first, second); }
    public enum KeyCode { None, PageDown, PageUp, RightControl, W }
    public enum CursorLockMode { None, Locked }
    public static class Time { public static float time, deltaTime = .02f, timeScale = 1f; }
    public static class Cursor { public static bool visible; public static CursorLockMode lockState = CursorLockMode.Locked; }
    public static class Application { public static bool isFocused = true; }
    public static class Input
    {
        public static readonly HashSet<KeyCode> Held = new HashSet<KeyCode>();
        public static bool GetKey(KeyCode key) => Held.Contains(key);
    }
}
namespace BepInEx.Configuration
{
    public sealed class ConfigEntry<T> { public T Value; public ConfigEntry(T value) { Value = value; } }
    public sealed class ConfigFile
    {
        public ConfigEntry<T> Bind<T>(string section, string key, T initial, string description) => new ConfigEntry<T>(initial);
    }
    public struct KeyboardShortcut
    {
        public UnityEngine.KeyCode MainKey;
        public UnityEngine.KeyCode[] Modifiers;
        public KeyboardShortcut(UnityEngine.KeyCode key, params UnityEngine.KeyCode[] modifiers) { MainKey = key; Modifiers = modifiers; }
        public override string ToString() => string.Join("+", Modifiers) + "+" + MainKey;
    }
}
namespace HarmonyLib
{
    public sealed class Harmony { public void Patch(MethodInfo original, HarmonyMethod prefix = null, HarmonyMethod postfix = null) { } }
    public sealed class HarmonyMethod { public HarmonyMethod(Type type, string method) { } }
    public static class AccessTools
    {
        public static MethodInfo Method(Type type, string name, Type[] parameters = null) =>
            type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        public static FieldInfo Field(Type type, string name) => type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        public static PropertyInfo Property(Type type, string name) => type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    }
}
namespace EFT { public sealed class GameWorld { } public sealed class Player { } }
namespace TscHh60Visual
{
    using UnityEngine;
    using BepInEx.Configuration;
    using EFT;

    public enum HelicopterModel { UH60, HH60 }
    internal enum FakeSupportType { Extract, PriorityExfil }
    internal sealed class FakeTimingSnapshot { public float SpeedMultiplier { get; set; } = 1f; }
    internal sealed class NativeAircraft : Component
    {
        internal FakeSupportType _requestSupportType = FakeSupportType.Extract;
        internal bool _allowLocalServicePoint = true;
        internal CancellationToken _cancellationToken = default;
        internal FakeTimingSnapshot _timingSnapshot = new FakeTimingSnapshot();
        private void ProcessRequest() { }
        private void CancelRequestLifetime() { }
        private GameObject CreateExtractionPoint() => null;
        private void DestroyLandingPoint(GameObject point) { }
    }
    internal sealed class TestLog
    {
        internal readonly List<string> Errors = new List<string>();
        public void LogInfo(object value) { }
        public void LogWarning(object value) { }
        public void LogError(object value) => Errors.Add(value.ToString());
    }
    public sealed partial class Plugin : MonoBehaviour
    {
        internal const string MarkerName = "TSC_HH60_VISUAL";
        internal static TestLog Log = new TestLog();
        internal static ConfigEntry<HelicopterModel> Model = new ConfigEntry<HelicopterModel>(HelicopterModel.UH60);
        private static Plugin _host;
        private readonly HarmonyLib.Harmony _harmony = new HarmonyLib.Harmony();
        private readonly ConfigFile Config = new ConfigFile();
        private readonly ConfigEntry<bool> _benchLiveShots = new ConfigEntry<bool>(false);
        private readonly ConfigEntry<KeyboardShortcut> _benchShotKey = new ConfigEntry<KeyboardShortcut>(new KeyboardShortcut(KeyCode.PageDown, KeyCode.RightControl));
        private readonly ConfigEntry<KeyboardShortcut> _benchGunKey = new ConfigEntry<KeyboardShortcut>(new KeyboardShortcut(KeyCode.PageUp, KeyCode.RightControl));
        private static readonly Func<KeyCode, bool> BenchKeyHeld = Input.GetKey;
        internal GameWorld Raid = new GameWorld();
        internal Player Caller = new Player();
        internal bool RaidValid = true;
        internal bool Enabled { get => _extractionCoverEnabled.Value; set => _extractionCoverEnabled.Value = value; }
        internal bool Live { get => _benchLiveShots.Value; set => _benchLiveShots.Value = value; }
        internal bool OwnsTrigger => ExtractionOwnsManualTrigger;
        internal bool Failed => _coverFailed;
        internal bool HasRuntime => _coverTargeting != null || _coverGuns != null || _coverBallistics != null || _coverPresentation != null;
        internal EscortBenchTargeting Targeting => _coverTargeting;
        internal EscortBenchBallistics Ballistics => _coverBallistics;
        internal EscortBenchAircraft Presentation => _coverPresentation;
        internal ExtractionGunVisuals Guns => _coverGuns;
        internal GameObject Point => _coverPoint;
        internal void Initialize() { _host = this; BindExtractionCover(); HookExtractionCover(typeof(NativeAircraft)); }
        internal void Inbound(Component aircraft, bool visualOnly = false) => BeginInboundExtractionCover(aircraft, visualOnly);
        internal void Cancel(Component aircraft) => CancelExtractionCoverRequest(aircraft);
        internal void Begin(Component aircraft, GameObject point, CancellationToken cancellation = default) => BeginExtractionCoverWindow(aircraft, point, cancellation);
        internal void End(GameObject point) => EndExtractionCoverWindow(point);
        internal void Tick(float dt = .02f) { Time.deltaTime = dt; Time.time += dt; TickExtractionCover(); }
        internal void Shutdown() => ShutdownExtractionCover();
        private bool TryGetBenchRaid(out GameWorld world, out Player player, out string reason)
        { world = Raid; player = Caller; reason = RaidValid ? null : "raid unavailable"; return RaidValid; }
        internal static Transform FindDescendant(Transform root, string name) => root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);
        partial void BindExtractionCover();
        partial void HookExtractionCover(Type aircraft);
        partial void TickExtractionCover();
        partial void ShutdownExtractionCover();
    }

    internal sealed class ExtractionGunVisuals : IDisposable
    {
        internal const string MarkerName = "TSC_EXTRACTION_GUNS";
        internal static bool Ready = true;
        internal static int Created;
        internal bool Disposed;
        private readonly Transform _anchor;
        private GameObject _owned;
        internal ExtractionGunVisuals(Transform anchor) { _anchor = anchor; Created++; }
        internal bool TryPrepare(out string reason)
        {
            reason = Ready ? "ready" : "waiting";
            if (!Ready) return false;
            if (_owned == null) { _owned = new GameObject(MarkerName); _owned.transform.SetParent(_anchor, false); }
            return true;
        }
        public void Dispose() { Disposed = true; Object.Destroy(_owned); }
    }
    internal sealed class EscortBenchTargeting : IDisposable
    {
        internal static bool ThrowOnTick, AllowShot = true;
        internal static int Created;
        internal bool Disposed;
        internal int Ticks, Suspends, Cycles, ShotRequests;
        internal int SelectedGunIndex;
        internal Transform SelectedBarrel;
        internal string Marker;
        internal float HullMotionSpeed;
        internal EscortBenchTargeting(GameObject main, Player caller, EscortBenchBallistics ballistics, bool moving, int fixedGun, string marker)
        {
            if (!moving || fixedGun != -1) throw new Exception("manual extraction targeting mode changed");
            Created++; SelectedBarrel = main.transform; Marker = marker;
        }
        internal void Tick(float dt) { Ticks++; if (ThrowOnTick) throw new Exception("injected targeting fault"); }
        internal void SetHullMotionSpeed(float speed) { HullMotionSpeed = speed; }
        internal void CycleGun() { Cycles++; SelectedGunIndex = 1 - SelectedGunIndex; }
        internal void Suspend(string reason) { Suspends++; }
        internal bool TryGetFreeShot(out Vector3 origin, out Vector3 direction, out object ammo, out string reason)
        { ShotRequests++; origin = default; direction = default; ammo = new object(); reason = AllowShot ? null : "blocked"; return AllowShot; }
        public void Dispose() { Disposed = true; }
    }
    internal sealed class EscortBenchBallistics : IDisposable
    {
        internal static int TotalRounds;
        internal static bool AllowFire = true;
        internal bool Disposed;
        internal readonly Player Owner;
        internal int Rounds;
        internal EscortBenchBallistics(Player caller) { Owner = caller; }
        internal bool Fire(Vector3 origin, Vector3 direction, string target, object ammo)
        {
            if (Disposed) throw new Exception("disposed ballistics used");
            if (target != null) throw new Exception("manual free shot must retain optional target");
            if (!AllowFire) return false;
            Rounds++; TotalRounds++; return true;
        }
        public void Dispose() { Disposed = true; }
    }
    internal sealed class EscortBenchAircraft : IDisposable
    {
        internal static int ActiveLoads;
        internal bool Disposed, WeaponsOnly;
        internal int Ticks, Flashes, Sounds;
        internal readonly GameObject NativeAircraft;
        internal readonly string Marker;
        internal EscortBenchAircraft(GameObject aircraft, bool weaponsOnly, string gunMarkerName)
        { NativeAircraft = aircraft; WeaponsOnly = weaponsOnly; Marker = gunMarkerName; }
        internal void Tick(float dt) { Ticks++; }
        internal IEnumerator LoadGunshotAudio(Func<bool> current) => Load(current);
        internal IEnumerator LoadMuzzleFlash(Func<bool> current) => Load(current);
        private IEnumerator Load(Func<bool> current)
        {
            ActiveLoads++;
            try { while (current()) yield return null; }
            finally { ActiveLoads--; }
        }
        internal void PlayMuzzleFlash(Transform barrel, Vector3 origin, Vector3 direction) { Flashes++; }
        internal void PlayGunshot(Vector3 origin) { Sounds++; }
        public void Dispose() { Disposed = true; }
    }
}
