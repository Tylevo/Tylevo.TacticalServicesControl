using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace TscHh60Visual
{
    // Inspection only: no collider, renderer, gun transform, or weapon state is changed.
    public sealed class HelicopterDebugOverlay : MonoBehaviour
    {
        private static ConfigEntry<bool> _enabled, _collisions, _guns, _throughWalls, _triggers;
        private static ConfigEntry<float> _yaw, _down, _up, _range;
        private static readonly HashSet<HelicopterDebugOverlay> Instances = new HashSet<HelicopterDebugOverlay>();
        private static readonly Dictionary<Mesh, Mesh[]> WireMeshes = new Dictionary<Mesh, Mesh[]>();
        private static Material _material;
        private static bool _shaderFailed;
        private static int _hudFrame = -1;
        private static readonly Color SurfaceColor = new Color(0.25f, 1f, 0.3f, 0.8f);
        private static readonly Color BoundsColor = new Color(1f, 0.55f, 0.12f, 0.9f);
        private static readonly Color LeftColor = new Color(0.15f, 0.9f, 1f, 1f);
        private static readonly Color RightColor = new Color(1f, 0.25f, 0.85f, 1f);
        private readonly List<CollisionShape> _shapes = new List<CollisionShape>();
        private Transform _left, _right;
        private float _refreshAt;
        private bool _failed;
        private int _lastRenderedFrame = -10, _surfaceCount, _boundsCount;

        internal static void Bind(ConfigFile config)
        {
            _enabled = config.Bind("Debug", "Show helicopter overlay", false,
                "Show helicopter collision surfaces, HH60 muzzle tips, and proposed gun arcs in the main game view. Inspection only; these settings do not enable weapons.");
            _collisions = config.Bind("Debug", "Show collision surfaces", true,
                "Green: collision surface edges. Orange: approximate bounds when surface data is unavailable. The HH60 still uses the original UH60 colliders.");
            _guns = config.Bind("Debug", "Show gun arcs", true,
                "Cyan: left gun. Pink: right gun. Crosses mark barrel tips, short white lines show barrel directions. Fans are proposed aiming limits relative to the helicopter, not existing weapon behavior.");
            _throughWalls = config.Bind("Debug", "See through helicopter", true,
                "Draw the overlay through the helicopter and surrounding scenery. Disable for normal depth occlusion.");
            _triggers = config.Bind("Debug", "Include triggers", false,
                "Include trigger colliders attached to the aircraft, in yellow. Detached landing-zone triggers are not part of this overlay.");
            _yaw = config.Bind("Debug", "Gun horizontal half-angle", 55f,
                new ConfigDescription("Proposed left/right sweep from each gun's outward heading, in degrees. 55 means a total 110-degree sector.", new AcceptableValueRange<float>(5f, 85f)));
            _down = config.Bind("Debug", "Gun downward angle", 65f,
                new ConfigDescription("Proposed downward limit, measured from the helicopter's horizontal plane.", new AcceptableValueRange<float>(0f, 85f)));
            _up = config.Bind("Debug", "Gun upward angle", 10f,
                new ConfigDescription("Proposed upward limit, measured from the helicopter's horizontal plane.", new AcceptableValueRange<float>(0f, 45f)));
            _range = config.Bind("Debug", "Arc display distance", 12f,
                new ConfigDescription("Length of the preview fans in metres. This is only the drawing size, not a weapon range.", new AcceptableValueRange<float>(3f, 40f)));
        }

        internal static void Attach(GameObject aircraft)
        {
            if (aircraft.GetComponent<HelicopterDebugOverlay>() == null)
                aircraft.AddComponent<HelicopterDebugOverlay>();
        }

        private void Awake() { Instances.Add(this); }
        private void OnDestroy() { Instances.Remove(this); }

        internal static void Shutdown()
        {
            foreach (var instance in Instances.ToArray()) if (instance != null) Object.Destroy(instance);
            Instances.Clear();
            foreach (var pair in WireMeshes) foreach (var wire in pair.Value) if (wire != null) Object.Destroy(wire);
            WireMeshes.Clear();
            if (_material != null) Object.Destroy(_material);
            _material = null;
            _shaderFailed = false;
            ColliderOverlayData.Clear();
        }

        private void Update()
        {
            if (_enabled == null || !_enabled.Value || _failed || Time.unscaledTime < _refreshAt) return;
            _refreshAt = Time.unscaledTime + 1f;
            try
            {
                // Delayed discovery also handles lazy HH60 construction and pooled reuse.
                var marker = Plugin.FindDescendant(transform, Plugin.MarkerName);
                _left = marker == null ? null : Plugin.FindDescendant(marker, "helicopter_gun_left");
                _right = marker == null ? null : Plugin.FindDescendant(marker, "helicopter_gun_right");
                if (!_collisions.Value) return;
                var colliders = GetComponentsInChildren<Collider>(true);
                _shapes.RemoveAll(shape => shape.Collider == null || !colliders.Contains(shape.Collider));
                foreach (var collider in colliders)
                {
                    var existing = _shapes.Find(shape => shape.Collider == collider);
                    var mesh = (collider as MeshCollider)?.sharedMesh;
                    if (existing != null && existing.Mesh == mesh && existing.IsTrigger == collider.isTrigger) continue;
                    if (existing != null) _shapes.Remove(existing);
                    Vector3[] edges = null;
                    // Convex cooking changes the hull; source triangles are not its collision surface.
                    if (collider is MeshCollider meshCollider && mesh != null && !meshCollider.convex)
                        ColliderOverlayData.TryGet(mesh, out edges);
                    _shapes.Add(new CollisionShape { Collider = collider, Mesh = mesh, IsTrigger = collider.isTrigger,
                        Wire = edges == null ? null : GetWireMesh(mesh, edges, collider.isTrigger) });
                }
            }
            catch (Exception error) { Fail(error); }
        }

        private static Mesh GetWireMesh(Mesh source, Vector3[] points, bool trigger)
        {
            if (!WireMeshes.TryGetValue(source, out var variants)) WireMeshes.Add(source, variants = new Mesh[2]);
            int variant = trigger ? 1 : 0;
            if (variants[variant] != null) return variants[variant];
            var wire = new Mesh { name = "TSC debug collision edges", hideFlags = HideFlags.HideAndDontSave,
                indexFormat = points.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            try
            {
                var indices = new int[points.Length];
                var colors = new Color32[points.Length];
                Color32 color = trigger ? Color.yellow : SurfaceColor;
                for (int i = 0; i < points.Length; i++) { indices[i] = i; colors[i] = color; }
                wire.vertices = points;
                wire.colors32 = colors;
                wire.SetIndices(indices, MeshTopology.Lines, 0);
                wire.UploadMeshData(true);
                variants[variant] = wire;
                return wire;
            }
            catch { Object.Destroy(wire); throw; }
        }

        private static bool PrepareMaterial()
        {
            if (_shaderFailed) return false;
            if (_material == null)
            {
                var shader = Plugin.FindNativeShader("Hidden/Internal-Colored");
                if (shader == null)
                {
                    _shaderFailed = true;
                    Plugin.Log.LogWarning("Helicopter debug overlay: line shader unavailable; overlay disabled.");
                    return false;
                }
                _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                _material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                _material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                _material.SetInt("_Cull", (int)CullMode.Off);
                _material.SetInt("_ZWrite", 0);
            }
            _material.SetInt("_ZTest", (int)(_throughWalls.Value ? CompareFunction.Always : CompareFunction.LessEqual));
            return _material.SetPass(0);
        }

        private void OnRenderObject()
        {
            if (_enabled == null || !_enabled.Value || _failed) return;
            var camera = Camera.current;
            // Intentionally main screen only, excluding scope textures, previews and reflections.
            if (camera == null || camera.cameraType != CameraType.Game || camera.orthographic || camera.targetTexture != null) return;
            if (Camera.main != null && camera != Camera.main) return;
            if ((camera.transform.position - transform.position).sqrMagnitude > 600f * 600f) return;
            if ((camera.cullingMask & (1 << gameObject.layer)) == 0) return;
            try
            {
                if (!PrepareMaterial()) return;
                GL.PushMatrix();
                try
                {
                    GL.modelview = camera.worldToCameraMatrix;
                    GL.LoadProjectionMatrix(camera.projectionMatrix);
                    _surfaceCount = _boundsCount = 0;
                    if (_collisions.Value)
                        foreach (var shape in _shapes)
                            if (Visible(shape.Collider) && shape.Wire != null)
                            {
                                // Native line meshes avoid hundreds of thousands of managed GL calls.
                                Graphics.DrawMeshNow(shape.Wire, shape.Collider.transform.localToWorldMatrix);
                                _surfaceCount++;
                            }
                    GL.Begin(GL.LINES);
                    try
                    {
                        if (_collisions.Value) foreach (var shape in _shapes) DrawCollider(shape);
                        if (_guns.Value)
                        {
                            // Measured centres of the final barrel rings in the pinned donor mesh.
                            // Gun local +Y is the barrel axis; local +Z is not the firing direction.
                            DrawGun(_left, new Vector3(0.002512f, 1.059167f, 0.111553f), LeftColor);
                            DrawGun(_right, new Vector3(0.025021f, 1.059167f, 0.111553f), RightColor);
                        }
                    }
                    finally { GL.End(); }
                }
                finally { GL.PopMatrix(); }
                _lastRenderedFrame = Time.frameCount;
            }
            catch (Exception error) { Fail(error); }
        }

        private void DrawCollider(CollisionShape shape)
        {
            var collider = shape.Collider;
            if (!Visible(collider) || shape.Wire != null) return;
            if (collider is BoxCollider box)
            {
                GL.Color(collider.isTrigger ? Color.yellow : SurfaceColor);
                DrawBox(new Bounds(box.center, box.size), collider.transform.localToWorldMatrix);
                _surfaceCount++;
            }
            else
            {
                GL.Color(collider.isTrigger ? Color.yellow : BoundsColor);
                // For unknown/cooked/unreadable geometry draw honestly labelled bounds only.
                if (shape.Mesh != null) DrawBox(shape.Mesh.bounds, collider.transform.localToWorldMatrix);
                else DrawBox(collider.bounds, Matrix4x4.identity);
                _boundsCount++;
            }
        }

        private static bool Visible(Collider collider)
        {
            return collider != null && collider.enabled && collider.gameObject.activeInHierarchy && (!collider.isTrigger || _triggers.Value);
        }

        private static void DrawBox(Bounds bounds, Matrix4x4 matrix)
        {
            var min = bounds.min;
            var size = bounds.size;
            for (int i = 0; i < 8; i++)
            {
                var point = min + new Vector3((i & 1) != 0 ? size.x : 0f, (i & 2) != 0 ? size.y : 0f, (i & 4) != 0 ? size.z : 0f);
                var world = matrix.MultiplyPoint3x4(point);
                if ((i & 1) == 0) Line(world, matrix.MultiplyPoint3x4(point + new Vector3(size.x, 0f, 0f)));
                if ((i & 2) == 0) Line(world, matrix.MultiplyPoint3x4(point + new Vector3(0f, size.y, 0f)));
                if ((i & 4) == 0) Line(world, matrix.MultiplyPoint3x4(point + new Vector3(0f, 0f, size.z)));
            }
        }

        private void DrawGun(Transform gun, Vector3 localTip, Color color)
        {
            if (gun == null || !gun.gameObject.activeInHierarchy) return;
            var origin = gun.TransformPoint(localTip);
            var barrel = gun.TransformDirection(Vector3.up).normalized;
            var up = transform.up;
            var forward = Vector3.ProjectOnPlane(barrel, up).normalized;
            if (forward.sqrMagnitude < 0.5f) return;
            var right = Vector3.Cross(up, forward).normalized;
            GL.Color(color);
            Line(origin - right * 0.2f, origin + right * 0.2f);
            Line(origin - up * 0.2f, origin + up * 0.2f);
            Line(origin - forward * 0.2f, origin + forward * 0.2f);
            float yaw = _yaw.Value, low = -_down.Value, high = _up.Value, range = _range.Value;
            // Bounded azimuth/elevation patch, never a full circle. Angles use aircraft-up.
            for (int i = 0; i < 24; i++)
            {
                float a = Mathf.Lerp(-yaw, yaw, i / 24f), b = Mathf.Lerp(-yaw, yaw, (i + 1) / 24f);
                DrawArcSegment(origin, forward, right, up, range, a, b, low);
                DrawArcSegment(origin, forward, right, up, range, a, b, 0f);
                DrawArcSegment(origin, forward, right, up, range, a, b, high);
                float p = Mathf.Lerp(low, high, i / 24f), q = Mathf.Lerp(low, high, (i + 1) / 24f);
                Line(origin + Direction(forward, right, up, -yaw, p) * range, origin + Direction(forward, right, up, -yaw, q) * range);
                Line(origin + Direction(forward, right, up, yaw, p) * range, origin + Direction(forward, right, up, yaw, q) * range);
            }
            for (int i = -1; i <= 1; i++)
            {
                Line(origin, origin + Direction(forward, right, up, i * yaw, low) * range);
                Line(origin, origin + Direction(forward, right, up, i * yaw, high) * range);
            }
            GL.Color(Color.white);
            Line(origin, origin + barrel * 2f);
        }

        private static void DrawArcSegment(Vector3 origin, Vector3 forward, Vector3 right, Vector3 up, float range, float a, float b, float pitch)
        {
            Line(origin + Direction(forward, right, up, a, pitch) * range, origin + Direction(forward, right, up, b, pitch) * range);
        }

        private static Vector3 Direction(Vector3 forward, Vector3 right, Vector3 up, float yaw, float pitch)
        {
            yaw *= Mathf.Deg2Rad;
            pitch *= Mathf.Deg2Rad;
            return (forward * Mathf.Cos(yaw) + right * Mathf.Sin(yaw)) * Mathf.Cos(pitch) + up * Mathf.Sin(pitch);
        }

        private static void Line(Vector3 from, Vector3 to) { GL.Vertex(from); GL.Vertex(to); }

        private void OnGUI()
        {
            if (_enabled == null || !_enabled.Value || _failed || Time.frameCount - _lastRenderedFrame > 1
                || Event.current.type != EventType.Repaint || _hudFrame == Time.frameCount) return;
            _hudFrame = Time.frameCount;
            string gunStatus = _left != null && _left.gameObject.activeInHierarchy ? "Cyan: left muzzle / Pink: right muzzle / White: barrel axis" : "Select HH60 to inspect its gun positions.";
            GUI.Box(new Rect(18f, 90f, 620f, 100f), "Helicopter inspection (F12 > TSC Helicopter Appearance > Debug)\n"
                + "Green: collision surface edges / Orange: approximate bounds / Yellow: triggers\n"
                + gunStatus + "\nProposed arcs only; weapons are inactive. HH60 uses the original UH60 colliders.\n"
                + _surfaceCount + " surfaces, " + _boundsCount + " bounds visible on this aircraft.");
        }

        private void Fail(Exception error)
        {
            _failed = true;
            Plugin.Log.LogWarning("Helicopter debug overlay stopped for this aircraft: " + error);
        }

        private sealed class CollisionShape
        {
            internal Collider Collider;
            internal Mesh Mesh;
            internal Mesh Wire;
            internal bool IsTrigger;
        }
    }
}
