using System;
using System.Collections;
using System.Threading.Tasks;
using Comfort.Common;
using Diz.DependencyManager;
using Diz.Resources;
using EFT;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace TscHh60Visual
{
    // Only the native NSV jet data, shader and pulse curves are reused. No weapon
    // prefab, MuzzleManager, global effects system or projectile is instantiated.
    internal sealed class EscortMuzzleFlash : IDisposable
    {
        internal const string TemplateBundle = "assets/content/weapons/wip/kibas tuning prefabs/muzzlejets_templates/default_assets.bundle";
        internal const string MaterialBundle = "assets/systems/effects/muzzleflash/muzzleflash.bundle";
        internal const float ShotLength = .12f;
        private static readonly Vector2 AtlasCellSize = new Vector2(.5f, .5f);
        private static readonly int ShotValues = Shader.PropertyToID("_ShotVals");
        private readonly GameObject _aircraft;
        private readonly string _gunMarkerName;
        private readonly Flash[] _flashes = new Flash[2];
        // Exact key values/tangents from the installed NSV MuzzleManager.
        private readonly AnimationCurve _move = new AnimationCurve(
            new Keyframe(0f, -.5302548408508301f, 1.746815323829651f, 1.746815323829651f),
            new Keyframe(1f, 1.216560482978821f, .4464404881000519f, 1.523885488510132f));
        private readonly AnimationCurve _brightness = new AnimationCurve(
            new Keyframe(0f, 0f, 2f, 2f),
            new Keyframe(.1278825849294663f, 1f, .01679978147149086f, .01679978147149086f),
            new Keyframe(.6092601418495178f, 1f, .05665117129683495f, .05665117129683495f),
            new Keyframe(1f, 0f, 0f, 0f));
        private DependencyGraph<IEasyBundle>.Token _retain;
        private IEasyAssets _assets;
        private Task _loading;
        private bool _started, _disposed, _ready, _warned;
        private float _deadline;

        internal bool Available => !_disposed && _ready;
        internal string Failure { get; private set; }

        internal EscortMuzzleFlash(GameObject aircraft, string gunMarkerName = Plugin.MarkerName)
        {
            _aircraft = aircraft;
            _gunMarkerName = gunMarkerName ?? throw new ArgumentNullException(nameof(gunMarkerName));
        }

        internal IEnumerator Load(Func<bool> stillCurrent)
        {
            if (_started || _disposed) yield break;
            _started = true;
            try
            {
                if (!Current(stillCurrent) || !Begin()) yield break;
                while (!_disposed)
                {
                    if (!Current(stillCurrent)) yield break;
                    if (Poll()) yield break;
                    yield return null;
                }
            }
            finally
            {
                if (!_ready || _disposed) Cleanup();
            }
        }

        private bool Current(Func<bool> stillCurrent)
        {
            try { return !_disposed && _aircraft != null && stillCurrent != null && stillCurrent(); }
            catch (Exception error) { Fail(error); return false; }
        }

        private bool Begin()
        {
            try
            {
                _assets = Singleton<IEasyAssets>.Instance;
                if (_assets == null) throw new InvalidOperationException("native asset manager unavailable");
                _retain = _assets.Retain(new[] { TemplateBundle, MaterialBundle });
                if (_retain == null) throw new InvalidOperationException("native muzzle asset retain unavailable");
                _loading = EasyAssetsExtensions.LoadBundles(_retain);
                if (_loading == null) throw new InvalidOperationException("native muzzle asset loading job unavailable");
                _deadline = Time.realtimeSinceStartup + 15f;
                return true;
            }
            catch (Exception error) { Fail(error); return false; }
        }

        private bool Poll()
        {
            try
            {
                if (Time.realtimeSinceStartup >= _deadline) throw new TimeoutException("native muzzle flash load exceeded 15 seconds");
                if (!_loading.IsCompleted) return false;
                _loading.GetAwaiter().GetResult();
                var template = _assets.GetAsset<GameObject>(TemplateBundle, "utes_muzzleflash");
                var material = _assets.GetAsset<Material>(MaterialBundle, "MuzzleFlashTest");
                var jet = template == null ? null : template.GetComponent<MuzzleJet>();
                if (template == null || template.name != "utes_muzzleflash" || jet == null ||
                    material == null || material.name != "MuzzleFlashTest" || material.shader == null || material.mainTexture == null)
                    throw new InvalidOperationException("native NSV jet template or material unavailable");
                ValidateJet(jet);
                var marker = Plugin.FindDescendant(_aircraft.transform, _gunMarkerName);
                if (marker == null || !marker.gameObject.activeInHierarchy)
                    throw new InvalidOperationException("Selected gun visual is unavailable for muzzle flashes: " + _gunMarkerName);
                _flashes[0] = Build(marker, "left", jet, material);
                _flashes[1] = Build(marker, "right", jet, material);
                _ready = true;
                Plugin.Log?.LogInfo("[EscortBench] Native NSV muzzle flashes ready: two private barrel meshes, 0.12-second accepted-shot pulses.");
                return true;
            }
            catch (Exception error) { Fail(error); return true; }
        }

        private static void ValidateJet(MuzzleJet jet)
        {
            if (jet.Particles == null || jet.Particles.Length < 2 || jet.Particles.Length > 64 ||
                !Finite(jet.JetBounds) || !Finite(jet.Chance) || jet.Chance <= 0f || jet.Chance > 1f)
                throw new InvalidOperationException("native muzzle jet data is invalid");
            float minimum = float.PositiveInfinity, maximum = float.NegativeInfinity;
            foreach (var particle in jet.Particles)
            {
                if (particle == null || !Finite(particle.Position) || !Finite(particle.Size) || particle.Size <= 0f ||
                    !Finite(particle.AxisShift) || !Finite(particle.RandomShift.x) || !Finite(particle.RandomShift.y))
                    throw new InvalidOperationException("native muzzle jet particle data is invalid");
                minimum = Mathf.Min(minimum, particle.Position);
                maximum = Mathf.Max(maximum, particle.Position);
            }
            if (maximum - minimum <= .000001f)
                throw new InvalidOperationException("native muzzle jet particle positions are degenerate");
        }

        private static Flash Build(Transform marker, string side, MuzzleJet template, Material sourceMaterial)
        {
            var barrel = Plugin.FindDescendant(marker, "helicopter_gun_" + side);
            var mount = Plugin.FindDescendant(marker, "helicopter_gun_box_" + side);
            if (barrel == null || mount == null || barrel.parent != mount)
                throw new InvalidOperationException("HH60 " + side + " gun barrel is unavailable");
            var flash = new Flash { Barrel = barrel };
            try
            {
                flash.Root = new GameObject("Escort " + side + " native muzzle flash");
                flash.Root.SetActive(false);
                flash.Root.transform.SetParent(barrel, false);
                flash.Root.transform.localRotation = Quaternion.FromToRotation(Vector3.down, Vector3.up);
                flash.Material = new Material(sourceMaterial) { name = "Escort " + side + " native jet material" };
                var jet = flash.Root.AddComponent<MuzzleJet>();
                // These are the only serialized fields read by native FillJet/FillParticle.
                jet.JetBounds = template.JetBounds;
                jet.Chance = template.Chance;
                jet.Particles = new MuzzleJet.Particle[template.Particles.Length];
                float radius = 1f + Mathf.Abs(jet.JetBounds.z - jet.JetBounds.y);
                for (int i = 0; i < jet.Particles.Length; i++)
                {
                    var source = template.Particles[i];
                    jet.Particles[i] = new MuzzleJet.Particle
                    {
                        Position = source.Position, Size = source.Size,
                        RandomShift = source.RandomShift, AxisShift = source.AxisShift
                    };
                    radius = Mathf.Max(radius, 1f + Mathf.Abs(source.Position) + 2f * source.Size +
                        Mathf.Abs(source.AxisShift) + source.RandomShift.magnitude);
                }
                var randomState = Random.state;
                try { MuzzleJet.UpdateOrCreateMesh(new[] { jet }, flash.Root.transform, flash.Material, AtlasCellSize); }
                finally { Random.state = randomState; }
                var meshTransform = flash.Root.transform.Find("MuzzleJetCombinedMesh");
                if (meshTransform == null) throw new InvalidOperationException("native muzzle mesh creation failed");
                meshTransform.localScale = Vector3.one;
                flash.Renderer = meshTransform.GetComponent<MeshRenderer>();
                flash.Mesh = meshTransform.GetComponent<MeshFilter>()?.sharedMesh;
                if (flash.Renderer == null || flash.Mesh == null || flash.Mesh.vertexCount != jet.Particles.Length * 4)
                    throw new InvalidOperationException("native muzzle mesh is incomplete");
                // Shader-expanded billboards exceed the collinear CPU vertex bounds.
                flash.Mesh.bounds = new Bounds(Vector3.zero, Vector3.one * (radius * 2f));
                flash.Renderer.shadowCastingMode = ShadowCastingMode.Off;
                flash.Renderer.receiveShadows = false;
                flash.Renderer.enabled = false;
                flash.Material.SetVector(ShotValues, new Vector4(1f, 1.216560482978821f, 0f, 0f));
                // Brief warm point light uses native MuzzleLight defaults. It is our
                // private light, not a serialized light from the NSV template.
                flash.Light = flash.Root.AddComponent<Light>();
                flash.Light.type = LightType.Point;
                flash.Light.color = new Color(1f, .65f, .45f);
                flash.Light.shadows = LightShadows.None;
                flash.Light.range = 6f;
                flash.Light.intensity = 0f;
                flash.Light.enabled = false;
                flash.Root.SetActive(true);
                return flash;
            }
            catch { flash.Dispose(); throw; }
        }

        internal void Pulse(Transform barrel, Vector3 origin, Vector3 direction)
        {
            if (!Available) return;
            try
            {
                Flash flash = null;
                foreach (var candidate in _flashes) if (candidate != null && candidate.Barrel == barrel) { flash = candidate; break; }
                if (flash == null || barrel == null || !barrel.gameObject.activeInHierarchy ||
                    !Finite(origin) || !Finite(direction) || !Finite(direction.sqrMagnitude) || direction.sqrMagnitude < .000001f)
                    throw new InvalidOperationException("accepted shot has no matching live muzzle flash barrel");
                flash.Root.transform.localPosition = barrel.InverseTransformPoint(origin);
                // Native jet forward is -Y; the HH60 barrel is +Y. Parenting preserves
                // this achieved-shot alignment while the gun and aircraft keep moving.
                flash.Root.transform.localRotation = Quaternion.FromToRotation(Vector3.down,
                    barrel.InverseTransformDirection(direction.normalized));
                var randomState = Random.state;
                try
                {
                    MuzzleJet.RandomizeMaterial(flash.Material, AtlasCellSize);
                    flash.Light.range = Random.Range(6f, 12f);
                }
                finally { Random.state = randomState; }
                flash.Age = 0f;
                flash.Pulsing = true;
                Apply(flash);
            }
            catch (Exception error) { Fail(error); Cleanup(); }
        }

        internal void Tick(float dt)
        {
            if (!Available) return;
            try
            {
                foreach (var flash in _flashes)
                {
                    if (flash == null || flash.Root == null || flash.Barrel == null)
                        throw new InvalidOperationException("muzzle flash barrel was removed");
                    if (!flash.Pulsing) continue;
                    if (Finite(dt) && dt > 0f) flash.Age += dt;
                    if (!Finite(dt) || dt <= 0f || flash.Age >= ShotLength)
                    {
                        flash.Pulsing = false;
                        flash.Renderer.enabled = false;
                        flash.Light.enabled = false;
                        flash.Light.intensity = 0f;
                    }
                    else Apply(flash);
                }
            }
            catch (Exception error) { Fail(error); Cleanup(); }
        }

        private void Apply(Flash flash)
        {
            float t = Mathf.Clamp01(flash.Age / ShotLength);
            flash.Material.SetVector(ShotValues, new Vector4(1f - _brightness.Evaluate(t), _move.Evaluate(t), 0f, 0f));
            flash.Renderer.enabled = true;
            // Native MuzzleLight default curve is linear (0,1) -> (.5,0), and
            // SetIntensity multiplies by two and disables values below .001.
            float intensity = Mathf.Max(0f, 1f - 2f * t);
            flash.Light.intensity = 2f * intensity;
            flash.Light.enabled = intensity > .001f;
        }

        private void Fail(Exception error)
        {
            _ready = false;
            Failure = error.GetBaseException().Message;
            if (_warned) return;
            _warned = true;
            Plugin.Log?.LogWarning("[EscortBench] Optional muzzle flashes unavailable: " + Failure + ". Shot simulation is unaffected.");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _ready = false;
            Cleanup();
        }

        private void Cleanup()
        {
            for (int i = 0; i < _flashes.Length; i++)
            {
                var flash = _flashes[i];
                _flashes[i] = null;
                try { flash?.Dispose(); }
                catch (Exception error) { Fail(error); }
            }
            var retain = _retain;
            _retain = null;
            _assets = null;
            if (_loading != null)
            {
                // Explicit native Release can leave the task pending. Do not wait
                // for it, and never touch Unity/this from a fault continuation.
                _loading.ContinueWith(task => { var ignored = task.Exception; },
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
                _loading = null;
            }
            if (retain == null) return;
            try { retain.Release(); }
            catch (Exception error) { Fail(error); }
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);

        private sealed class Flash : IDisposable
        {
            internal Transform Barrel;
            internal GameObject Root;
            internal Mesh Mesh;
            internal Material Material;
            internal MeshRenderer Renderer;
            internal Light Light;
            internal float Age;
            internal bool Pulsing;

            public void Dispose()
            {
                if (Root != null) Root.SetActive(false);
                // Native mesh creation may have thrown after allocating its child.
                if (Mesh == null && Root != null)
                {
                    var filter = Root.GetComponentInChildren<MeshFilter>(true);
                    if (filter != null) Mesh = filter.sharedMesh;
                }
                if (Root != null) Object.Destroy(Root);
                if (Mesh != null) Object.Destroy(Mesh);
                if (Material != null) Object.Destroy(Material);
                Root = null; Mesh = null; Material = null; Renderer = null; Light = null; Barrel = null;
            }
        }
    }
}
