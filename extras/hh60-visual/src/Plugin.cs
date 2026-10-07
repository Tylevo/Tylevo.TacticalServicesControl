using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace TscHh60Visual
{
    public enum HelicopterModel { UH60, HH60 }

    [BepInPlugin("local.tsc.hh60visual", "TSC Helicopter Appearance", "0.10.1")]
    [BepInDependency("com.tylevo.tacticalservicescontrol", "1.3.13")]
    public sealed partial class Plugin : BaseUnityPlugin
    {
        internal const string MarkerName = "TSC_HH60_VISUAL";
#if HH60_VALIDATED_CORE
        internal const string ExpectedTscSha256 = ValidatedCoreIdentity.Sha256;
#else
        // Historical local baseline only. Canonical builds explicitly supply an
        // audited identity because Core ProductVersion includes the Git commit.
        internal const string ExpectedTscSha256 = "3B5CF9B9F8647E5C3DD9D701C1C6B1C19054B81FB4B0C12F22441AC25FA57F63";
#endif
        internal static ManualLogSource Log;
        internal static ConfigEntry<HelicopterModel> Model;
        internal static string ScenePath;
        private Harmony _harmony;
        private static Plugin _host;
        private static readonly HashSet<VisualInstance> Instances = new HashSet<VisualInstance>();
        private int _selectionDirty;
        private bool _compatible;

        // The optional local bench is compiled separately from appearance-only tests.
        partial void BindEscortBench();
        partial void TickEscortBench();
        partial void ShutdownEscortBench();
        partial void DrawEscortBench();
        partial void BindExtractionCover();
        partial void HookExtractionCover(Type aircraft);
        partial void TickExtractionCover();
        partial void ShutdownExtractionCover();

        internal static void Register(VisualInstance visual) { Instances.Add(visual); }
        internal static void Unregister(VisualInstance visual) { Instances.Remove(visual); }

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
            var legacyEnabled = Config.Bind("Visual", "Enabled", false, "Previous appearance setting.");
            Model = Config.Bind("Visual", "Helicopter model",
                legacyEnabled.Value ? HelicopterModel.HH60 : HelicopterModel.UH60,
                "Choose the original UH-60 or the Icebreaker HH-60 with static crew. Changes apply without restarting. The first HH-60 selection may take several seconds to load.");
            Config.Remove(legacyEnabled.Definition);
            HelicopterDebugOverlay.Bind(Config);
            BindEscortBench();
            BindExtractionCover();
            Config.Save();
            ScenePath = Path.Combine(Path.GetDirectoryName(Info.Location), "payload", "scene.json");
            var heli = AccessTools.TypeByName("SamSWAT.FireSupport.ArysReloaded.Unity.UH60Behaviour");
            var awake = heli == null ? null : AccessTools.Method(heli, "OnAwake", Type.EmptyTypes);
            if (awake == null || !MatchesHash(heli.Assembly.Location, ExpectedTscSha256))
            {
                Log.LogWarning("TSC core does not match verified 1.3.13. No hooks installed; original model retained.");
                return;
            }
            if (!File.Exists(ScenePath)) { Log.LogWarning("HH60 payload missing; original model retained: " + ScenePath); return; }
            // One appearance hook. No AssetLoader or AssetBundle patches. The optional
            // bench installs separate, read-only impact observers only when summoned.
            _harmony = new Harmony("local.tsc.hh60visual");
            _harmony.Patch(awake, postfix: new HarmonyMethod(typeof(Plugin), nameof(AfterHelicopterAwake)));
            HookExtractionCover(heli);
            _compatible = true;
            Model.SettingChanged += OnModelChanged;
            Interlocked.Exchange(ref _selectionDirty, 1);
            Log.LogInfo("Helicopter appearance selector ready: " + Model.Value + ". Change Visual / Helicopter model in F12 to switch models.");
        }

        private void OnModelChanged(object sender, EventArgs args)
        {
            // Config reloads can arrive off-thread; all Unity work stays in Update.
            Interlocked.Exchange(ref _selectionDirty, 1);
        }

        private void Update()
        {
            if (!_compatible) return;
            TickExtractionCover();
            TickEscortBench();
            if (Interlocked.Exchange(ref _selectionDirty, 0) == 0) return;
            var selected = Model.Value;
            foreach (var visual in Instances.ToArray())
            {
                if (visual == null) { Instances.Remove(visual); continue; }
                visual.SetModel(selected);
            }
            Log.LogInfo("Helicopter model selected: " + selected);
        }

        private static void AfterHelicopterAwake(Component __instance)
        {
            if (__instance == null) return;
            VisualInstance visual = null;
            try
            {
                var anchor = FindDescendant(__instance.transform, "b_vhc_main");
                if (anchor == null) throw new InvalidDataException("Original TSC b_vhc_main anchor missing.");
                visual = __instance.GetComponent<VisualInstance>();
                if (visual == null)
                {
                    visual = __instance.gameObject.AddComponent<VisualInstance>();
                    visual.Initialize(__instance.transform, anchor, ScenePath);
                }
                visual.SetModel(Model.Value);
            }
            catch (Exception error)
            {
                if (visual != null) { visual.AbortAndRestore(); Destroy(visual); }
                Log.LogError("HH60 setup failed; original TSC visual retained: " + error);
            }
            try { HelicopterDebugOverlay.Attach(__instance.gameObject); }
            catch (Exception error) { Log.LogWarning("Helicopter debug overlay unavailable: " + error); }
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
            _compatible = false;
            ShutdownExtractionCover();
            ShutdownEscortBench();
            if (Model != null) Model.SettingChanged -= OnModelChanged;
            _harmony?.UnpatchSelf();
            foreach (var visual in Instances.ToArray())
                if (visual != null) { visual.AbortAndRestore(); Destroy(visual); }
            Instances.Clear();
            HelicopterDebugOverlay.Shutdown();
            SharedResources.Shutdown();
            _host = null;
        }

        private void OnGUI() { DrawEscortBench(); }
    }

    // One owner per original TSC aircraft. Objects are constructed under an inactive
    // marker. Original renderers are untouched until the complete replacement is ready.
    public sealed class VisualInstance : MonoBehaviour
    {
        private readonly List<OriginalRenderer> _original = new List<OriginalRenderer>();
        private readonly List<RotorBinding> _rotors = new List<RotorBinding>();
        private GameObject _visual;
        private Transform _aircraft, _anchor;
        private string _scenePath;
        private bool _committed, _ready, _failed, _building, _showRequested, _cleaning;
        private Coroutine _build;
        private bool _pinnedToHh60;

        internal bool IsReady => _ready;
        internal bool HasFailed => _failed;
        internal void PinToHh60() { _pinnedToHh60 = true; SetModel(HelicopterModel.HH60); }

        internal void Initialize(Transform aircraft, Transform anchor, string scenePath)
        {
            _aircraft = aircraft;
            _anchor = anchor;
            _scenePath = scenePath;
            Plugin.Register(this);
        }

        internal void SetModel(HelicopterModel model)
        {
            _showRequested = _pinnedToHh60 || model == HelicopterModel.HH60;
            if (!_showRequested) { Hide(); return; }
            if (_ready) { Show(); return; }
            if (_building || _failed) return;
            _building = true;
            var routine = Plugin.StartHostCoroutine(RunBuild(Build(_aircraft, _anchor, _scenePath)));
            // StartCoroutine may finish or fail before returning its handle.
            if (_building) _build = routine;
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
                    _building = false; _build = null; AbortAndRestore();
                    Plugin.Log.LogError("HH60 visual build rejected; original model restored: " + failure);
                    yield break;
                }
                if (!moved) { _building = false; _build = null; yield break; }
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
            // Finish hidden if the player switched back while construction was running.
            // The cached visual can then be shown again without rebuilding it.
            _ready = true;
            if (_showRequested) Show();
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
            foreach (var entry in _original) if (entry.Renderer != null) entry.Renderer.forceRenderingOff = true;
            SyncRotors();
        }

        private void SyncRotors()
        {
            foreach (var binding in _rotors)
            {
                if (binding.Source == null || binding.Target == null || binding.Source.parent == null || binding.Target.parent == null) continue;
                var basis = Quaternion.Inverse(binding.Target.parent.rotation) * binding.Source.parent.rotation;
                var delta = binding.Source.localRotation * binding.SourceRestInverse;
                binding.Target.localRotation = basis * delta * Quaternion.Inverse(basis) * binding.TargetRest;
            }
        }

        private void Show()
        {
            if (_committed || !_ready || _visual == null) return;
            // Capture each renderer's current flag on every transition, so other
            // visual settings changed while the original was visible are preserved.
            foreach (var entry in _original)
                if (entry.Renderer != null)
                {
                    entry.ForceOff = entry.Renderer.forceRenderingOff;
                    entry.Renderer.forceRenderingOff = true;
                }
            SyncRotors();
            _visual.SetActive(true);
            _committed = true;
        }

        private void Hide()
        {
            if (_visual != null) _visual.SetActive(false);
            if (!_committed) return;
            foreach (var entry in _original)
                if (entry.Renderer != null) entry.Renderer.forceRenderingOff = entry.ForceOff;
            _committed = false;
        }

        internal void AbortAndRestore()
        {
            if (_cleaning) return;
            _cleaning = true;
            if (_build != null) { Plugin.StopHostCoroutine(_build); _build = null; }
            _building = false;
            Hide();
            if (_visual != null) { _visual.SetActive(false); Destroy(_visual); _visual = null; }
            _original.Clear(); _rotors.Clear();
            _ready = false; _failed = true;
            _cleaning = false;
        }

        private void OnDestroy() { AbortAndRestore(); Plugin.Unregister(this); }
        private sealed class OriginalRenderer { public Renderer Renderer; public bool ForceOff; }
        private sealed class OriginalGlass { public Renderer Renderer; public Material Material; public int Slot; }
        private sealed class RotorBinding { public Transform Source, Target; public Quaternion SourceRestInverse, TargetRest; }
    }
}
