using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

// Minimal managed test doubles, not a Unity renderer. Production Plugin.cs owns
// all model-selection and build lifecycle decisions exercised by this runner.
namespace UnityEngine
{
    public class Object
    {
        internal static readonly List<Object> Objects = new List<Object>();
        internal bool Destroyed;
        public virtual string name { get; set; }
        public Object() { Objects.Add(this); }
        public static bool operator ==(Object left, Object right) =>
            (ReferenceEquals(left, null) || left.Destroyed) && (ReferenceEquals(right, null) || right.Destroyed)
            || ReferenceEquals(left, right);
        public static bool operator !=(Object left, Object right) => !(left == right);
        public override bool Equals(object other) => ReferenceEquals(this, other);
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
        public static void Destroy(Object value)
        {
            if (value == null) return;
            if (value is GameObject game)
            {
                foreach (Transform child in game.transform.Children.ToArray()) Destroy(child.gameObject);
                foreach (Component component in game.Components.ToArray()) Destroy(component);
                game.transform.SetParent(null, false);
            }
            else if (value is Component component)
            {
                TestRuntime.Invoke(component, "OnDestroy");
                component.gameObject.Components.Remove(component);
            }
            value.Destroyed = true;
        }
        public static void DontDestroyOnLoad(Object value) { }
    }

    public class Component : Object
    {
        public GameObject gameObject { get; internal set; }
        public Transform transform => gameObject.transform;
        public override string name { get => gameObject?.name; set { if (gameObject != null) gameObject.name = value; } }
        public T GetComponent<T>() where T : Component => gameObject.GetComponent<T>();
        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : Component => gameObject.GetComponentsInChildren<T>(includeInactive);
    }
    public class MonoBehaviour : Component
    {
        public bool enabled = true;
        public bool isActiveAndEnabled => enabled && gameObject.activeInHierarchy;
        public Coroutine StartCoroutine(IEnumerator routine) => TestRuntime.Start(routine);
        public void StopCoroutine(Coroutine coroutine) => TestRuntime.Stop(coroutine);
    }
    public sealed class Coroutine { internal IEnumerator Routine; internal bool Done; }
    public sealed class GameObject : Object
    {
        public static int CreatedCount;
        internal readonly List<Component> Components = new List<Component>();
        public readonly Transform transform;
        public bool activeSelf { get; private set; } = true;
        public bool activeInHierarchy => activeSelf && (transform.parent == null || transform.parent.gameObject.activeInHierarchy);
        public int layer;
        public GameObject(string objectName = "GameObject")
        {
            name = objectName; CreatedCount++;
            transform = new Transform { gameObject = this }; Components.Add(transform);
        }
        public T AddComponent<T>() where T : Component, new()
        {
            var result = new T { gameObject = this }; Components.Add(result);
            // Lifecycle setup is explicitly invoked in the fixture where needed.
            return result;
        }
        public T GetComponent<T>() where T : Component => Components.OfType<T>().FirstOrDefault(value => value != null);
        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : Component
        {
            var found = new List<T>();
            if (includeInactive || activeInHierarchy) found.AddRange(Components.OfType<T>().Where(value => value != null));
            foreach (Transform child in transform.Children) found.AddRange(child.gameObject.GetComponentsInChildren<T>(includeInactive));
            return found.ToArray();
        }
        public void SetActive(bool active)
        {
            var behaviors = GetComponentsInChildren<MonoBehaviour>(true);
            var previous = behaviors.Select(value => value.isActiveAndEnabled).ToArray();
            activeSelf = active;
            for (int index = 0; index < behaviors.Length; index++)
                if (previous[index] != behaviors[index].isActiveAndEnabled)
                    TestRuntime.Invoke(behaviors[index], behaviors[index].isActiveAndEnabled ? "OnEnable" : "OnDisable");
        }
    }
    public sealed class Transform : Component
    {
        internal readonly List<Transform> Children = new List<Transform>();
        public Transform parent { get; private set; }
        public Vector3 localPosition, localScale;
        public Quaternion localRotation = Quaternion.identity;
        public Quaternion rotation => parent == null ? localRotation : parent.rotation * localRotation;
        public void SetParent(Transform next, bool worldPositionStays)
        {
            parent?.Children.Remove(this); parent = next; parent?.Children.Add(this);
        }
        public bool IsChildOf(Transform other)
        {
            for (Transform item = this; item != null; item = item.parent) if (ReferenceEquals(item, other)) return true;
            return false;
        }
    }
    public readonly struct Vector3
    {
        public readonly float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    }
    public readonly struct Quaternion
    {
        private readonly System.Numerics.Quaternion _value;
        public Quaternion(float x, float y, float z, float w) { _value = new System.Numerics.Quaternion(x, y, z, w); }
        private Quaternion(System.Numerics.Quaternion value) { _value = value; }
        public static Quaternion identity => new Quaternion(0, 0, 0, 1);
        public static Quaternion Inverse(Quaternion value) => new Quaternion(System.Numerics.Quaternion.Inverse(value._value));
        public static Quaternion operator *(Quaternion left, Quaternion right) => new Quaternion(left._value * right._value);
    }
    public class Renderer : Component
    {
        public bool forceRenderingOff, enabled = true, receiveShadows;
        public Rendering.ShadowCastingMode shadowCastingMode;
        public Material[] sharedMaterials = Array.Empty<Material>();
        public void GetPropertyBlock(MaterialPropertyBlock block, int index = -1) { }
        public void SetPropertyBlock(MaterialPropertyBlock block, int index = -1) { }
    }
    public sealed class MeshRenderer : Renderer { }
    public sealed class SkinnedMeshRenderer : Renderer { }
    public sealed class MeshFilter : Component { public Mesh sharedMesh; }
    public sealed class Mesh : Object { }
    public sealed class Material : Object { public Shader shader; public int renderQueue; }
    public sealed class Shader : Object
    {
        public bool isSupported = true;
        public static Shader Find(string shaderName) => new Shader { name = shaderName };
    }
    public sealed class MaterialPropertyBlock { public bool isEmpty => true; }
    public static class Resources
    {
        public static T[] FindObjectsOfTypeAll<T>() where T : Object => Object.Objects.OfType<T>().Where(value => value != null).ToArray();
    }
    public static class TestRuntime
    {
        private static readonly List<Coroutine> Pending = new List<Coroutine>();
        public static int PendingCount => Pending.Count(value => !value.Done);
        internal static Coroutine Start(IEnumerator routine)
        {
            var coroutine = new Coroutine { Routine = routine }; Pending.Add(coroutine);
            Step(coroutine); return coroutine;
        }
        internal static void Stop(Coroutine coroutine) { if (coroutine != null) coroutine.Done = true; }
        private static void Step(Coroutine coroutine)
        {
            if (!coroutine.Done && !coroutine.Routine.MoveNext()) coroutine.Done = true;
        }
        public static void Tick()
        {
            foreach (Coroutine coroutine in Pending.ToArray()) Step(coroutine);
            Pending.RemoveAll(value => value.Done);
        }
        public static void Drain(int maximum = 20)
        {
            for (int index = 0; index < maximum && PendingCount > 0; index++) Tick();
            if (PendingCount != 0) throw new InvalidOperationException("Coroutine did not settle in bounded scheduler ticks.");
        }
        public static void Invoke(object target, string method) =>
            target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.Invoke(target, null);
        public static void Reset() { Pending.Clear(); Object.Objects.Clear(); GameObject.CreatedCount = 0; }
    }
}
namespace UnityEngine.Rendering { public enum ShadowCastingMode { Off, On, TwoSided, ShadowsOnly } }
namespace BepInEx.Logging
{
    public sealed class ManualLogSource
    {
        public readonly List<string> Errors = new List<string>();
        public void LogInfo(object message) { }
        public void LogWarning(object message) { }
        public void LogError(object message) => Errors.Add(message.ToString());
    }
}
namespace BepInEx.Configuration
{
    public sealed class ConfigEntry<T>
    {
        private T _value;
        public object Definition { get; } = new object();
        public event EventHandler SettingChanged;
        public ConfigEntry(T value) { _value = value; }
        public T Value { get => _value; set { _value = value; SettingChanged?.Invoke(this, EventArgs.Empty); } }
    }
    public sealed class ConfigDescription { public ConfigDescription(string description, object acceptable = null, params object[] tags) { } }
    public sealed class AcceptableValueList<T> { public AcceptableValueList(params T[] values) { } }
    public sealed class ConfigFile
    {
        public ConfigEntry<T> Bind<T>(string section, string key, T value, string description) => new ConfigEntry<T>(value);
        public ConfigEntry<T> Bind<T>(string section, string key, T value, ConfigDescription description) => new ConfigEntry<T>(value);
        public bool Remove(object definition) => true;
        public void Save() { }
    }
}
namespace BepInEx
{
    [AttributeUsage(AttributeTargets.Class)] public sealed class BepInPlugin : Attribute { public BepInPlugin(string id, string name, string version) { } }
    [AttributeUsage(AttributeTargets.Class)] public sealed class BepInDependency : Attribute { public BepInDependency(string id, string version) { } }
    public class BaseUnityPlugin : UnityEngine.MonoBehaviour
    {
        public readonly Configuration.ConfigFile Config = new Configuration.ConfigFile();
        public readonly Logging.ManualLogSource Logger = new Logging.ManualLogSource();
        public readonly PluginInfo Info = new PluginInfo();
    }
    public sealed class PluginInfo { public string Location = "synthetic/TscHh60Visual.dll"; }
}
namespace HarmonyLib
{
    public sealed class Harmony
    {
        public Harmony(string id) { }
        public void Patch(MethodInfo original, HarmonyMethod postfix) { }
        public void UnpatchSelf() { }
    }
    public sealed class HarmonyMethod { public HarmonyMethod(Type type, string name) { } }
    public static class AccessTools
    {
        public static Type TypeByName(string name) => null;
        public static MethodInfo Method(Type type, string name, Type[] parameters) =>
            type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static, null, parameters, null);
    }
}
namespace TscHh60Visual
{
    internal static class HelicopterDebugOverlay
    {
        internal static void Bind(BepInEx.Configuration.ConfigFile config) { }
        internal static void Attach(UnityEngine.GameObject aircraft) { }
        internal static void Shutdown() { }
    }

    internal sealed class SharedResources
    {
        internal static SharedResources Current;
        internal static Exception Failure;
        internal static int Requests;
        internal SceneData Scene;
        internal readonly Dictionary<string, UnityEngine.Mesh> Meshes = new Dictionary<string, UnityEngine.Mesh>();
        internal readonly Dictionary<string, UnityEngine.Material> Materials = new Dictionary<string, UnityEngine.Material>();
        internal static void Request(string path) { Requests++; }
        internal static void Shutdown() { Current = null; Failure = null; Requests = 0; }
    }
    internal sealed class SceneData
    {
        public NodeData[] nodes;
        public MaterialData[] materials;
        public RendererData[] renderers;
        public object[] crew = Array.Empty<object>();
    }
    internal sealed class NodeData { public string id, name, parent; public float[] position, rotation, scale; }
    internal sealed class MaterialData { public string id, name; }
    internal sealed class RendererData
    {
        public string node, mesh;
        public string[] materials;
        public bool enabled = true, receiveShadows = true;
        public int shadowCastingMode = 1;
    }
}
