using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

// Synthetic hierarchy/resource doubles. The production flash loader, gun lookup,
// matching-barrel pulse and cleanup run unchanged; no Unity renderer is claimed.
namespace UnityEngine
{
    public class Object
    {
        internal bool Destroyed;
        public virtual string name { get; set; }
        public static bool operator ==(Object a, Object b) =>
            (ReferenceEquals(a, null) || a.Destroyed) && (ReferenceEquals(b, null) || b.Destroyed) || ReferenceEquals(a, b);
        public static bool operator !=(Object a, Object b) => !(a == b);
        public override bool Equals(object value) => ReferenceEquals(this, value);
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
        public static void Destroy(Object value)
        {
            if (value == null) return;
            if (value is GameObject game)
            {
                foreach (var child in game.transform.Children.ToArray()) Destroy(child.gameObject);
                foreach (var component in game.Components) component.Destroyed = true;
                game.transform.SetParent(null, false);
            }
            value.Destroyed = true;
        }
    }
    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform => gameObject.transform;
        public override string name { get => gameObject.name; set => gameObject.name = value; }
        public T GetComponent<T>() where T : Component => gameObject.GetComponent<T>();
    }
    public sealed class GameObject : Object
    {
        internal readonly List<Component> Components = new List<Component>();
        public readonly Transform transform;
        public bool activeSelf = true;
        public bool activeInHierarchy => activeSelf && (transform.parent == null || transform.parent.gameObject.activeInHierarchy);
        public GameObject(string name) { this.name = name; transform = new Transform { gameObject = this }; Components.Add(transform); }
        public void SetActive(bool value) { activeSelf = value; }
        public T AddComponent<T>() where T : Component, new() { var result = new T { gameObject = this }; Components.Add(result); return result; }
        public T GetComponent<T>() where T : Component => Components.OfType<T>().FirstOrDefault(value => value != null);
        public T GetComponentInChildren<T>(bool includeInactive) where T : Component => GetComponentsInChildren<T>(includeInactive).FirstOrDefault();
        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : Component
        {
            var result = new List<T>();
            if (includeInactive || activeInHierarchy) result.AddRange(Components.OfType<T>().Where(value => value != null));
            foreach (var child in transform.Children) result.AddRange(child.gameObject.GetComponentsInChildren<T>(includeInactive));
            return result.ToArray();
        }
    }
    public sealed class Transform : Component
    {
        internal readonly List<Transform> Children = new List<Transform>();
        public Transform parent { get; private set; }
        public Vector3 localPosition, localScale = Vector3.one;
        public Quaternion localRotation = Quaternion.identity;
        public Quaternion rotation => parent == null ? localRotation : parent.rotation * localRotation;
        public Vector3 position => parent == null ? localPosition : parent.TransformPoint(localPosition);
        public void SetParent(Transform next, bool worldPositionStays) { parent?.Children.Remove(this); parent = next; parent?.Children.Add(this); }
        public Transform Find(string name) => Children.FirstOrDefault(child => child.name == name);
        public Vector3 TransformPoint(Vector3 value) => position + rotation * value;
        public Vector3 InverseTransformPoint(Vector3 value) => Quaternion.Inverse(rotation) * (value - position);
        public Vector3 InverseTransformDirection(Vector3 value) => Quaternion.Inverse(rotation) * value;
    }
    public readonly struct Vector2
    {
        public readonly float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public float magnitude => MathF.Sqrt(x * x + y * y);
    }
    public readonly struct Vector3
    {
        public readonly float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => new Vector3(0, 0, 0);
        public static Vector3 one => new Vector3(1, 1, 1);
        public static Vector3 up => new Vector3(0, 1, 0);
        public static Vector3 down => new Vector3(0, -1, 0);
        public float sqrMagnitude => x * x + y * y + z * z;
        public Vector3 normalized => this * (1f / MathF.Sqrt(sqrMagnitude));
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator *(Vector3 a, float b) => new Vector3(a.x * b, a.y * b, a.z * b);
    }
    public readonly struct Quaternion
    {
        private readonly System.Numerics.Quaternion _value;
        private Quaternion(System.Numerics.Quaternion value) { _value = value; }
        public static Quaternion identity => new Quaternion(System.Numerics.Quaternion.Identity);
        public static Quaternion Inverse(Quaternion value) => new Quaternion(System.Numerics.Quaternion.Inverse(value._value));
        public static Quaternion operator *(Quaternion a, Quaternion b) => new Quaternion(a._value * b._value);
        public static Vector3 operator *(Quaternion rotation, Vector3 vector)
        {
            var v = System.Numerics.Vector3.Transform(new System.Numerics.Vector3(vector.x, vector.y, vector.z), rotation._value);
            return new Vector3(v.X, v.Y, v.Z);
        }
        public static Quaternion FromToRotation(Vector3 from, Vector3 to)
        {
            var a = System.Numerics.Vector3.Normalize(new System.Numerics.Vector3(from.x, from.y, from.z));
            var b = System.Numerics.Vector3.Normalize(new System.Numerics.Vector3(to.x, to.y, to.z));
            float dot = System.Numerics.Vector3.Dot(a, b);
            if (dot < -.99999f) return new Quaternion(System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitX, MathF.PI));
            return new Quaternion(System.Numerics.Quaternion.Normalize(new System.Numerics.Quaternion(System.Numerics.Vector3.Cross(a, b), 1 + dot)));
        }
    }
    public readonly struct Vector4 { public Vector4(float x, float y, float z, float w) { } }
    public readonly struct Color { public Color(float r, float g, float b) { } }
    public readonly struct Bounds { public Bounds(Vector3 center, Vector3 size) { } }
    public sealed class Mesh : Object { public int vertexCount; public Bounds bounds; }
    public sealed class MeshFilter : Component { public Mesh sharedMesh; }
    public sealed class MeshRenderer : Component { public bool enabled, receiveShadows; public Rendering.ShadowCastingMode shadowCastingMode; }
    public sealed class Texture : Object { }
    public sealed class Shader : Object { public static int PropertyToID(string name) => 1; }
    public sealed class Material : Object
    {
        public Shader shader;
        public Texture mainTexture;
        public Material() { }
        public Material(Material source) { shader = source.shader; mainTexture = source.mainTexture; }
        public void SetVector(int property, Vector4 vector) { }
    }
    public enum LightType { Point }
    public enum LightShadows { None }
    public sealed class Light : Component { public LightType type; public Color color; public LightShadows shadows; public float range, intensity; public bool enabled; }
    public readonly struct Keyframe { public Keyframe(float time, float value, float incoming, float outgoing) { } }
    public sealed class AnimationCurve { public AnimationCurve(params Keyframe[] keys) { } public float Evaluate(float time) => time; }
    public static class Mathf
    {
        public static float Abs(float a) => MathF.Abs(a);
        public static float Min(float a, float b) => MathF.Min(a, b);
        public static float Max(float a, float b) => MathF.Max(a, b);
        public static float Clamp01(float a) => Math.Clamp(a, 0, 1);
    }
    public static class Random { public static int state = 42; public static float Range(float a, float b) { state++; return (a + b) / 2; } }
    public static class Time { public static float realtimeSinceStartup; }
}
namespace UnityEngine.Rendering { public enum ShadowCastingMode { Off } }
namespace Comfort.Common { public static class Singleton<T> { public static T Instance; } }
namespace Diz.DependencyManager
{
    public sealed class DependencyGraph<T> { public sealed class Token { public int Releases; public void Release() { Releases++; } } }
}
namespace Diz.Resources
{
    public interface IEasyBundle { }
    public interface IEasyAssets
    {
        Diz.DependencyManager.DependencyGraph<IEasyBundle>.Token Retain(string[] keys);
        T GetAsset<T>(string bundle, string name) where T : UnityEngine.Object;
    }
    public static class EasyAssetsExtensions { public static Task LoadBundles(Diz.DependencyManager.DependencyGraph<IEasyBundle>.Token token) => Task.CompletedTask; }
}
namespace EFT
{
    public sealed class MuzzleJet : UnityEngine.Component
    {
        public UnityEngine.Vector3 JetBounds;
        public float Chance;
        public Particle[] Particles;
        public sealed class Particle { public float Position, Size, AxisShift; public UnityEngine.Vector2 RandomShift; }
        public static void UpdateOrCreateMesh(MuzzleJet[] jets, UnityEngine.Transform parent, UnityEngine.Material material, UnityEngine.Vector2 atlas)
        {
            var child = new UnityEngine.GameObject("MuzzleJetCombinedMesh"); child.transform.SetParent(parent, false);
            child.AddComponent<UnityEngine.MeshFilter>().sharedMesh = new UnityEngine.Mesh { vertexCount = jets.Sum(jet => jet.Particles.Length * 4) };
            child.AddComponent<UnityEngine.MeshRenderer>();
        }
        public static void RandomizeMaterial(UnityEngine.Material material, UnityEngine.Vector2 atlas) { UnityEngine.Random.state++; }
    }
}
namespace TscHh60Visual
{
    internal static class Plugin
    {
        internal const string MarkerName = "TSC_HH60_VISUAL";
        internal static readonly TestLog Log = new TestLog();
        internal static UnityEngine.Transform FindDescendant(UnityEngine.Transform root, string name) =>
            root.gameObject.GetComponentsInChildren<UnityEngine.Transform>(true).FirstOrDefault(node => node.name == name);
    }
    internal sealed class TestLog { public void LogInfo(object value) { } public void LogWarning(object value) { } }
}
