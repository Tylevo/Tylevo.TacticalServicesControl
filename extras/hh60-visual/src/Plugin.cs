using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace TscHh60Visual
{
    [BepInPlugin("local.tsc.hh60visual", "TSC HH60 Visual Adapter", "0.2.1")]
    [BepInDependency("com.tylevo.tacticalservicescontrol", "1.3.13")]
    public sealed class Plugin : BaseUnityPlugin
    {
        internal const string MarkerName = "TSC_HH60_VISUAL";
        internal const string ExpectedTscSha256 = "9144491E2C1A359E909148C55817905D00F1330DD783155E5809D5C712E0BF7E";
        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled;
        internal static string ScenePath;
        private Harmony _harmony;
        private static Plugin _host;

        internal static Coroutine StartHostCoroutine(IEnumerator routine)
        {
            if (_host == null) throw new InvalidOperationException("HH60 persistent plugin host unavailable.");
            return _host.StartCoroutine(routine);
        }
        internal static void StopHostCoroutine(Coroutine routine)
        {
            if (_host != null && routine != null) _host.StopCoroutine(routine);
        }

        private void Awake()
        {
            _host = this;
            Log = Logger;
            Enabled = Config.Bind("Visual", "Enabled", false, "Experimental HH60 appearance only. Disabled by default. Restart after changing this setting. No TSC service or interaction changes.");
            ScenePath = Path.Combine(Path.GetDirectoryName(Info.Location), "payload", "scene.json");
            if (!Enabled.Value) { Log.LogInfo("HH60 adapter disabled; original TSC unchanged."); return; }
            var heli = AccessTools.TypeByName("SamSWAT.FireSupport.ArysReloaded.Unity.UH60Behaviour");
            var awake = heli == null ? null : AccessTools.Method(heli, "OnAwake", Type.EmptyTypes);
            if (awake == null || !MatchesHash(heli.Assembly.Location, ExpectedTscSha256))
            {
                Log.LogWarning("TSC core does not match verified 1.3.13. No hooks installed; original model retained.");
                return;
            }
            if (!File.Exists(ScenePath)) { Log.LogWarning("HH60 payload missing; original model retained: " + ScenePath); return; }
            // Intentionally exactly one hook. In particular, no AssetLoader or AssetBundle
            // methods are patched, and no custom bundle is ever loaded by this adapter.
            _harmony = new Harmony("local.tsc.hh60visual");
            _harmony.Patch(awake, postfix: new HarmonyMethod(typeof(Plugin), nameof(AfterHelicopterAwake)));
            Log.LogInfo("HH60 v0.2.1 managed mesh/texture loader ready. Native TSC window material binding enabled. Only UH60Behaviour.OnAwake postfix installed; no custom bundle, ropes or service patches.");
        }

        private static void AfterHelicopterAwake(Component __instance)
        {
            if (__instance == null || !Enabled.Value || __instance.GetComponent<VisualInstance>() != null) return;
            VisualInstance visual = null;
            try
            {
                var anchor = FindDescendant(__instance.transform, "b_vhc_main");
                if (anchor == null) throw new InvalidDataException("Original TSC b_vhc_main anchor missing.");
                visual = __instance.gameObject.AddComponent<VisualInstance>();
                visual.Begin(__instance.transform, anchor, ScenePath);
            }
            catch (Exception error)
            {
                if (visual != null) { visual.AbortAndRestore(); Destroy(visual); }
                Log.LogError("HH60 setup failed; original TSC visual retained: " + error);
            }
        }

        internal static Transform FindDescendant(Transform root, string name)
        {
            foreach (var item in root.GetComponentsInChildren<Transform>(true)) if (item.name == name) return item;
            return null;
        }

        internal static Shader FindNativeShader(string name)
        {
            var finder = AccessTools.TypeByName("ShadersFinder");
            var find = finder == null ? null : AccessTools.Method(finder, "Find", new[] { typeof(string) });
            try
            {
                var shader = find?.Invoke(null, new object[] { name }) as Shader;
                if (shader != null && shader.name == name && shader.isSupported) return shader;
            }
            catch (Exception e) { Log.LogWarning("ShadersFinder failed for " + name + ": " + e.GetBaseException().Message); }
            var fallback = Shader.Find(name);
            return fallback != null && fallback.name == name && fallback.isSupported ? fallback : null;
        }

        private static bool MatchesHash(string path, string expected)
        {
            try
            {
                using (var stream = File.OpenRead(path)) using (var hash = SHA256.Create())
                    return string.Equals(BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", ""), expected, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception error) { Log.LogWarning("Cannot verify TSC core: " + error.Message); return false; }
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            foreach (var visual in Resources.FindObjectsOfTypeAll<VisualInstance>())
                if (visual != null) { visual.AbortAndRestore(); Destroy(visual); }
            SharedResources.Shutdown();
            _host = null;
        }
    }

    // One owner per original TSC aircraft. Objects are constructed under an inactive
    // marker. Original renderers are untouched until the complete replacement is ready.
    public sealed class VisualInstance : MonoBehaviour
    {
        private readonly List<OriginalRenderer> _original = new List<OriginalRenderer>();
        private readonly List<RotorBinding> _rotors = new List<RotorBinding>();
        private GameObject _visual;
        private bool _committed, _finished, _cleaning;
        private Coroutine _build;

        internal void Begin(Transform aircraft, Transform anchor, string scenePath)
        {
            if (_build != null || _finished) return;
            _build = Plugin.StartHostCoroutine(RunBuild(Build(aircraft, anchor, scenePath)));
        }

        private IEnumerator RunBuild(IEnumerator build)
        {
            while (true)
            {
                object current = null;
                bool moved = false;
                Exception failure = null;
                try { moved = build.MoveNext(); if (moved) current = build.Current; }
                catch (Exception error) { failure = error; }
                if (failure != null)
                {
                    // Do not stop this currently-executing coroutine from inside itself.
                    _build = null; AbortAndRestore();
                    Plugin.Log.LogError("HH60 visual build rejected; original model restored: " + failure);
                    yield break;
                }
                if (!moved) { _build = null; yield break; }
                yield return current;
            }
        }

        private IEnumerator Build(Transform aircraft, Transform anchor, string scenePath)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            SharedResources.Request(scenePath);
            while (SharedResources.Current == null)
            {
                if (SharedResources.Failure != null) throw new InvalidOperationException("Shared HH60 resource cache unavailable.", SharedResources.Failure);
                if (aircraft == null || anchor == null) throw new InvalidOperationException("Original TSC aircraft was destroyed while waiting for shared assets.");
                if (!Plugin.Enabled.Value) throw new InvalidOperationException("HH60 adapter disabled while waiting for shared assets.");
                yield return null;
            }
            var cache = SharedResources.Current;
            var scene = cache.Scene;
            var meshes = cache.Meshes;
            var materials = cache.Materials;
            if (Plugin.FindDescendant(aircraft, Plugin.MarkerName) != null) throw new InvalidDataException("Duplicate HH60 visual marker.");

            // Capture the actual flags without changing enabled, LODGroup state, collider,
            // Animator, audio, transforms or service triggers on any original object.
            foreach (var renderer in aircraft.GetComponentsInChildren<Renderer>(true))
                if (renderer is MeshRenderer || renderer is SkinnedMeshRenderer)
                    _original.Add(new OriginalRenderer { Renderer = renderer, ForceOff = renderer.forceRenderingOff });
            if (_original.Count == 0) throw new InvalidDataException("Original TSC model has no renderers.");
            // Borrow the already-loaded, working UH60 glass material without cloning or
            // mutating it. Keep its actual shader, blending, textures, reflection bindings
            // and runtime material updates instead of rebuilding the donor EFT/Glass.
            var windowIds = new HashSet<string>(scene.materials
                .Where(data => GlassMaterialPolicy.IsHh60Window(data.id, data.name))
                .Select(data => data.id), StringComparer.Ordinal);
            if (windowIds.Count != 1) throw new InvalidDataException("Expected exactly one HH60 aircraft window material.");
            var nativeGlass = FindOriginalGlass();
            int glassSlots = 0;
            _visual = new GameObject(Plugin.MarkerName);
            _visual.SetActive(false);
            _visual.transform.SetParent(anchor, false);
            _visual.layer = anchor.gameObject.layer;
            var nodes = new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (var data in scene.nodes)
            {
                var node = new GameObject(data.name);
                node.layer = anchor.gameObject.layer;
                node.transform.SetParent(_visual.transform, false);
                nodes.Add(data.id, node.transform);
            }
            foreach (var data in scene.nodes)
            {
                var node = nodes[data.id];
                node.SetParent(string.IsNullOrEmpty(data.parent) ? _visual.transform : nodes[data.parent], false);
                node.localPosition = Vector(data.position);
                node.localRotation = new Quaternion(data.rotation[0], data.rotation[1], data.rotation[2], data.rotation[3]);
                node.localScale = Vector(data.scale);
            }
            yield return null;

            foreach (var data in scene.renderers)
            {
                var node = nodes[data.node];
                node.gameObject.AddComponent<MeshFilter>().sharedMesh = meshes[data.mesh];
                var renderer = node.gameObject.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = data.materials
                    .Select(id => windowIds.Contains(id) ? nativeGlass.Material : materials[id]).ToArray();
                renderer.enabled = data.enabled;
                renderer.shadowCastingMode = (ShadowCastingMode)data.shadowCastingMode;
                renderer.receiveShadows = data.receiveShadows;
                // Door meshes contain both glass and metal: only replace the glass slot.
                for (int slot = 0; slot < data.materials.Length; slot++)
                    if (windowIds.Contains(data.materials[slot]))
                    {
                        var properties = new MaterialPropertyBlock();
                        nativeGlass.Renderer.GetPropertyBlock(properties, nativeGlass.Slot);
                        if (properties.isEmpty) nativeGlass.Renderer.GetPropertyBlock(properties);
                        if (!properties.isEmpty) renderer.SetPropertyBlock(properties, slot);
                        glassSlots++;
                    }
            }
            if (glassSlots != 3) throw new InvalidDataException("Expected cockpit and both rear-door glass slots; found " + glassSlots);
            AddRotor(aircraft, "b_vhc_rotor", "helicopter_top_rotor");
            AddRotor(aircraft, "b_vhc_rotor_tail", "helicopter_tail_rotor");
            if (_rotors.Count != 2) throw new InvalidDataException("Required main/tail rotor bridge targets missing.");
            if (!Plugin.Enabled.Value) throw new InvalidOperationException("Adapter disabled during visual construction.");

            // Atomic visual commit: execution cannot yield between hiding and showing.
            // If any preceding operation failed, all original flags remain unchanged.
            foreach (var entry in _original) if (entry.Renderer != null) entry.Renderer.forceRenderingOff = true;
            _visual.SetActive(true);
            _committed = true; _finished = true;
            Plugin.Log.LogInfo("HH60 native glass bound: " + glassSlots + " aircraft window slots -> "
                + nativeGlass.Material.name + "; shader=" + nativeGlass.Material.shader.name
                + "; renderQueue=" + nativeGlass.Material.renderQueue
                + ". Original shared material reused unchanged; crew optics and door metal unchanged.");
            Plugin.Log.LogInfo("HH60 appearance ready: " + scene.renderers.Length + " renderers, " + scene.materials.Length + " materials, " + (scene.crew?.Length ?? 0) + " baked crew, 2 rotor bridges; constructed in " + timer.ElapsedMilliseconds + " ms. Original TSC services/colliders/Animator untouched.");
        }

        private static Vector3 Vector(float[] v) { return new Vector3(v[0], v[1], v[2]); }

        private OriginalGlass FindOriginalGlass()
        {
            foreach (var original in _original)
            {
                var renderer = original.Renderer;
                if (renderer == null) continue;
                var materials = renderer.sharedMaterials;
                for (int slot = 0; slot < materials.Length; slot++)
                {
                    var material = materials[slot];
                    if (material != null && material.shader != null && material.shader.isSupported
                        && GlassMaterialPolicy.IsOriginalGlass(material.name, material.shader.name))
                        return new OriginalGlass { Renderer = renderer, Material = material, Slot = slot };
                }
            }
            // Fail before the atomic visual commit. Do not silently use opaque blue glass.
            throw new InvalidDataException("Verified original UH60 glass material unavailable; retain original aircraft.");
        }

        private void AddRotor(Transform aircraft, string sourceName, string targetName)
        {
            var source = Plugin.FindDescendant(aircraft, sourceName);
            var target = Plugin.FindDescendant(_visual.transform, targetName);
            if (source == null || target == null || target.IsChildOf(source)) return;
            _rotors.Add(new RotorBinding { Source = source, Target = target, SourceRestInverse = Quaternion.Inverse(source.localRotation), TargetRest = target.localRotation });
        }

        private void LateUpdate()
        {
            if (!_committed) return;
            if (!Plugin.Enabled.Value) { AbortAndRestore(); return; }
            foreach (var entry in _original) if (entry.Renderer != null) entry.Renderer.forceRenderingOff = true;
            foreach (var binding in _rotors)
            {
                if (binding.Source == null || binding.Target == null || binding.Source.parent == null || binding.Target.parent == null) continue;
                var basis = Quaternion.Inverse(binding.Target.parent.rotation) * binding.Source.parent.rotation;
                var delta = binding.Source.localRotation * binding.SourceRestInverse;
                binding.Target.localRotation = basis * delta * Quaternion.Inverse(basis) * binding.TargetRest;
            }
        }

        internal void AbortAndRestore()
        {
            if (_cleaning) return;
            _cleaning = true;
            if (_build != null) { Plugin.StopHostCoroutine(_build); _build = null; }
            if (_visual != null) { _visual.SetActive(false); Destroy(_visual); _visual = null; }
            foreach (var entry in _original) if (entry.Renderer != null) entry.Renderer.forceRenderingOff = entry.ForceOff;
            _original.Clear(); _rotors.Clear();
            _committed = false; _finished = true;
            _cleaning = false;
        }

        private void OnDestroy() { AbortAndRestore(); }
        private sealed class OriginalRenderer { public Renderer Renderer; public bool ForceOff; }
        private sealed class OriginalGlass { public Renderer Renderer; public Material Material; public int Slot; }
        private sealed class RotorBinding { public Transform Source, Target; public Quaternion SourceRestInverse, TargetRest; }
    }
}
