using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Audio;
using Object = UnityEngine.Object;

namespace TscHh60Visual
{
    // Presentation for one explicitly spawned test aircraft, or owned weapon
    // effects on a live extraction aircraft. This never fires a projectile.
    internal sealed class EscortBenchAircraft : IDisposable
    {
        private const string BallisticType = "EFT.Ballistics.BallisticCollider";
        private const float NativeRotorDegreesPerSecond = 675f;
        private readonly GameObject _aircraft;
        private readonly bool _weaponsOnly;
        private readonly string _gunMarkerName;
        private readonly Transform _mainRotor, _tailRotor;
        private readonly Quaternion _mainRest, _tailRest;
        private readonly AudioSource _engineClose, _engineDistant, _rotorClose, _rotorDistant;
        private readonly AudioSource[] _loops;
        private readonly AudioClip[] _gunTails = new AudioClip[3];
        private EscortGunshotAudio _gunshotAssets;
        private EscortMuzzleFlash _muzzleFlash;
        private readonly List<AudioSource> _shotSources = new List<AudioSource>();
        private readonly AnimationCurve _closeMix = new AnimationCurve(
            new Keyframe(0f, 1f, -.00001496098048f, -.00001496098048f),
            new Keyframe(250f, 0f, -.007630655076f, -.007630655076f));
        private Transform _listener;
        private float _nextListenerSearch, _nextGunshotSearch, _phase;
        private bool _started, _paused, _disposed, _warnedMissingGunshot, _warnedGunshotFailure;

        internal bool HasGunshotAudio => !_disposed && (ClipReady(_gunTails[0]) || ClipReady(_gunTails[1]) || ClipReady(_gunTails[2]));
        private static bool ClipReady(AudioClip clip) => clip != null && clip.loadState == AudioDataLoadState.Loaded;

        internal static GameObject Prepare(GameObject prefab, Transform caller, float height = 40f)
        {
            if (prefab == null || caller == null) throw new ArgumentNullException(prefab == null ? nameof(prefab) : nameof(caller));
            if (float.IsNaN(height) || float.IsInfinity(height) || height < 40f || height > 100f)
                throw new ArgumentOutOfRangeException(nameof(height));
            // Instantiate directly beneath an already inactive parent. Deactivating
            // a root after Instantiate would be too late to prevent UH60.Awake.
            var staging = new GameObject("TSC escort bench inactive staging");
            staging.SetActive(false);
            GameObject clone = null;
            try
            {
                clone = Object.Instantiate(prefab, staging.transform, false);
                clone.name = "TSC escort test";
                clone.SetActive(false);
                foreach (AudioSource source in clone.GetComponentsInChildren<AudioSource>(true))
                {
                    source.playOnAwake = false;
                    source.Stop();
                    source.enabled = false;
                }
                foreach (Animator animator in clone.GetComponentsInChildren<Animator>(true))
                {
                    animator.enabled = false;
                    Object.DestroyImmediate(animator);
                }
                foreach (ParticleSystem particles in clone.GetComponentsInChildren<ParticleSystem>(true))
                {
                    ParticleSystem.MainModule mainModule = particles.main;
                    mainModule.playOnAwake = false;
                    particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    particles.Clear(true);
                }
                foreach (TrailRenderer trail in clone.GetComponentsInChildren<TrailRenderer>(true))
                {
                    trail.emitting = false;
                    trail.Clear();
                    trail.enabled = false;
                }

                MonoBehaviour[] nativeScripts = clone.GetComponentsInChildren<MonoBehaviour>(true);
                int retainedBallistics = 0;
                foreach (MonoBehaviour script in nativeScripts)
                {
                    if (script == null) throw new InvalidOperationException("Escort prefab contains an unresolved native script.");
                    string type = script.GetType().FullName;
                    if (type == BallisticType)
                    {
                        // Verified game IL: Awake only calls Associate(TypeOfMaterial),
                        // which assigns the inherited physical-material field. Its base
                        // declares no service/event/lifecycle initialization.
                        retainedBallistics++;
                        continue;
                    }
                    if (type != "SamSWAT.FireSupport.ArysReloaded.Unity.UH60Behaviour" &&
                        type != "SamSWAT.FireSupport.ArysReloaded.Unity.SimpleSpinBlur" &&
                        type != "EFT.Visual.LightFlicker")
                        throw new InvalidOperationException("Unreviewed native escort component: " + type);
                    script.enabled = false;
                    // Deferred destruction could leave native Awake reachable when
                    // the caller activates the returned clone in the same frame.
                    Object.DestroyImmediate(script);
                }
                foreach (MonoBehaviour script in clone.GetComponentsInChildren<MonoBehaviour>(true))
                    if (script == null || script.GetType().FullName != BallisticType)
                        throw new InvalidOperationException("Escort service component survived preparation.");
                foreach (Light light in clone.GetComponentsInChildren<Light>(true)) light.enabled = false;
                foreach (Rigidbody body in clone.GetComponentsInChildren<Rigidbody>(true))
                {
                    body.isKinematic = true;
                    body.useGravity = false;
                }

                Collider[] colliders = clone.GetComponentsInChildren<Collider>(true);
                if (colliders.Length != 21 || retainedBallistics != 14)
                    throw new InvalidOperationException("Unexpected UH60 collision contract: " + colliders.Length + " colliders / " + retainedBallistics + " ballistic components.");
                Transform main = RequiredTransform(clone, "b_vhc_main");
                Transform bodyTransform = RequiredTransform(clone, "b_vhc_body");
                main.localPosition = Vector3.zero;
                bodyTransform.localPosition = Vector3.zero;
                // Keep the serialized body -90 degree X correction and all child
                // collider transforms; resetting body rotation would rotate the hull.
                Vector3 forward = Vector3.ProjectOnPlane(caller.forward, Vector3.up);
                if (forward.sqrMagnitude < .001f) forward = Vector3.forward;
                forward.Normalize();
                Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
                clone.transform.SetParent(null, false);
                Quaternion heading = Quaternion.LookRotation(forward, Vector3.up);
                Vector3 initialPosition = caller.position + right * 65f + Vector3.up * height;
                Physics.SyncTransforms();
                var overlap = new Collider[64];
                bool placed = false;
                string blocker = string.Empty;
                for (int attempt = 0; attempt < 6; attempt++)
                {
                    Vector3 position = initialPosition + Vector3.up * (10f * attempt);
                    if (!HasClearance(clone, position, heading, overlap, out blocker)) continue;
                    clone.transform.SetPositionAndRotation(position, heading);
                    placed = true;
                    if (attempt != 0) Plugin.Log?.LogInfo("[EscortBench] Raised test placement by " + (attempt * 10) + " m for world clearance.");
                    break;
                }
                if (!placed) throw new InvalidOperationException("[EscortBench] No clear test aircraft space after six bounded placement attempts. Last obstruction: " + blocker);
                Plugin.Log?.LogInfo("[EscortBench] Prepared inactive: 21 original colliders and 14 ballistic material components retained; extraction, flicker, spin-blur and Animator removed before activation.");
                GameObject result = clone;
                clone = null;
                return result;
            }
            finally
            {
                if (clone != null) Object.DestroyImmediate(clone);
                Object.DestroyImmediate(staging);
            }
        }

        internal EscortBenchAircraft(GameObject aircraft, bool weaponsOnly = false, string gunMarkerName = Plugin.MarkerName)
        {
            if (aircraft == null) throw new ArgumentNullException(nameof(aircraft));
            _aircraft = aircraft;
            _gunMarkerName = gunMarkerName ?? throw new ArgumentNullException(nameof(gunMarkerName));
            _weaponsOnly = weaponsOnly;
            if (weaponsOnly)
            {
                // A real extraction keeps its Animator, rotor transforms and
                // engine audio under TSC ownership. Own only effects we create.
                _loops = Array.Empty<AudioSource>();
                RefreshGunshotClips();
                return;
            }
            ValidateReady(aircraft);
            _mainRotor = RequiredTransform(aircraft, "b_vhc_rotor");
            _tailRotor = RequiredTransform(aircraft, "b_vhc_rotor_tail");
            _mainRest = _mainRotor.localRotation;
            _tailRest = _tailRotor.localRotation;
            _engineClose = RequiredLoop(aircraft, "engines_close", "UH60_engine_close");
            _engineDistant = RequiredLoop(aircraft, "engines_distant", "UH60_engine_distant");
            _rotorClose = RequiredLoop(aircraft, "rotor_close", "UH60_rotors_close");
            _rotorDistant = RequiredLoop(aircraft, "rotor_distant", "UH60_rotors_distant");
            _loops = new[] { _engineClose, _engineDistant, _rotorClose, _rotorDistant };
            AudioMixerGroup mixer = FindTechnicalMixer();
            foreach (AudioSource source in _loops)
            {
                source.Stop();
                source.playOnAwake = false;
                source.loop = true;
                source.mute = false;
                source.pitch = 1f;
                source.volume = 0f;
                source.spatialBlend = 1f;
                source.enabled = true;
                if (mixer != null) source.outputAudioMixerGroup = mixer;
                // Preserve the native clip, custom rolloff, range and doppler setup.
            }
            RefreshGunshotClips();
        }

        internal static void ValidateReady(GameObject aircraft)
        {
            if (aircraft == null || aircraft.activeInHierarchy || aircraft.activeSelf)
                throw new InvalidOperationException("Escort bench must remain inactive until presentation is ready.");
            if (aircraft.GetComponentsInChildren<Animator>(true).Length != 0)
                throw new InvalidOperationException("Native Animator survived escort preparation.");
            int ballisticCount = 0, visualCount = 0;
            foreach (MonoBehaviour script in aircraft.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (script != null && script.GetType().FullName == BallisticType) { ballisticCount++; continue; }
                if (script is VisualInstance visual && visual.IsReady) { visualCount++; continue; }
                throw new InvalidOperationException("Escort activation blocked by unready or unreviewed script: " + (script == null ? "missing script" : script.GetType().FullName));
            }
            if (ballisticCount != 14 || visualCount != 1 || aircraft.GetComponentsInChildren<Collider>(true).Length != 21)
                throw new InvalidOperationException("Escort activation requires the verified hull and exactly one completed HH60 visual.");
            Transform main = RequiredTransform(aircraft, "b_vhc_main");
            Transform body = RequiredTransform(aircraft, "b_vhc_body");
            if (main.localPosition.sqrMagnitude > .000001f || body.localPosition.sqrMagnitude > .000001f ||
                Quaternion.Angle(body.localRotation, Quaternion.Euler(-90f, 0f, 0f)) > .01f)
                throw new InvalidOperationException("Escort native body transform no longer matches the verified upright basis.");
            Physics.SyncTransforms();
            if (!HasClearance(aircraft, aircraft.transform.position, aircraft.transform.rotation, new Collider[64], out string blocker))
                throw new InvalidOperationException("[EscortBench] Aircraft space is obstructed before activation: " + blocker);
        }

        internal IEnumerator LoadGunshotAudio(Func<bool> stillCurrent)
        {
            if (_disposed || _gunshotAssets != null) yield break;
            var assets = _gunshotAssets = new EscortGunshotAudio();
            var load = assets.Load(() => !_disposed && stillCurrent != null && stillCurrent());
            bool completed = false;
            try
            {
                while (load.MoveNext()) yield return load.Current;
                if (_disposed || stillCurrent == null || !stillCurrent()) yield break;
                var clips = assets.Clips;
                if (clips != null)
                {
                    Array.Copy(clips, _gunTails, _gunTails.Length);
                    Plugin.Log?.LogInfo("[EscortBench] Native NSV gunshot audio ready: retained " + EscortGunshotAudio.BundleKey +
                        " / " + EscortGunshotAudio.BankName + "; close/distant/far clips loaded independently of the map.");
                }
                else
                {
                    RefreshGunshotClips();
                    Plugin.Log?.LogWarning("[EscortBench] Native gunshot audio preparation unavailable: " +
                        (assets.Failure ?? "cancelled") + ". Existing loaded clips remain available; shot simulation is unaffected.");
                }
                completed = true;
            }
            finally
            {
                (load as IDisposable)?.Dispose();
                if (!completed) assets.Dispose();
            }
        }

        internal IEnumerator LoadMuzzleFlash(Func<bool> stillCurrent)
        {
            if (_disposed || _muzzleFlash != null) yield break;
            var flash = _muzzleFlash = new EscortMuzzleFlash(_aircraft, _gunMarkerName);
            var load = flash.Load(() => !_disposed && stillCurrent != null && stillCurrent());
            bool completed = false;
            try
            {
                while (load.MoveNext()) yield return load.Current;
                completed = !_disposed && stillCurrent != null && stillCurrent();
            }
            finally
            {
                (load as IDisposable)?.Dispose();
                if (!completed) flash.Dispose();
            }
        }

        internal void PlayMuzzleFlash(Transform barrel, Vector3 origin, Vector3 direction)
        {
            _muzzleFlash?.Pulse(barrel, origin, direction);
        }

        internal void Tick(float dt)
        {
            if (_disposed || _aircraft == null || !_aircraft.activeInHierarchy) return;
            if (float.IsNaN(dt) || float.IsInfinity(dt)) return;
            _muzzleFlash?.Tick(dt);
            if (dt <= 0f)
            {
                if ((_started || _weaponsOnly) && !_paused)
                {
                    foreach (AudioSource source in _loops) if (source != null) source.Pause();
                    foreach (AudioSource source in _shotSources) if (source != null) source.Pause();
                    _paused = true;
                }
                return;
            }
            if (_paused)
            {
                foreach (AudioSource source in _loops) if (source != null) source.UnPause();
                foreach (AudioSource source in _shotSources) if (source != null) source.UnPause();
                _paused = false;
            }
            if (_weaponsOnly) return;
            _phase = Mathf.Repeat(_phase + Mathf.Min(dt, .25f) * NativeRotorDegreesPerSecond, 360f);
            // anim_uh60_rotors binding hashes map main Euler Z and tail Euler X
            // to the same 675 degrees/second. VisualInstance copies these deltas.
            if (_mainRotor != null) _mainRotor.localRotation = _mainRest * Quaternion.AngleAxis(_phase, Vector3.forward);
            if (_tailRotor != null) _tailRotor.localRotation = _tailRest * Quaternion.AngleAxis(_phase, Vector3.right);

            float close = Mathf.Clamp01(_closeMix.Evaluate(ListenerDistance(_rotorClose.transform.position)));
            _rotorClose.volume = close;
            _engineClose.volume = Mathf.Clamp01(close - .2f);
            _rotorDistant.volume = _engineDistant.volume = 1f - close;
            if (!_started)
            {
                foreach (AudioSource source in _loops) source.Play();
                _started = true;
                Plugin.Log?.LogInfo("[EscortBench] Native helicopter audio started: four loop sources, original rolloff and close/distant volume curve. Door sounds remain disabled.");
            }
        }

        internal void PlayGunshot(Vector3 muzzle)
        {
            try { PlayGunshotCore(muzzle); }
            catch (Exception error) { WarnGunshotFailure(error); }
        }

        private void WarnGunshotFailure(Exception error)
        {
            if (_warnedGunshotFailure) return;
            _warnedGunshotFailure = true;
            Plugin.Log?.LogWarning("[EscortBench] Optional gunshot audio failed; flight and shot simulation continue: " + error.Message);
        }

        private void PlayGunshotCore(Vector3 muzzle)
        {
            if (_disposed || _aircraft == null || !_aircraft.activeInHierarchy || Time.timeScale <= 0f) return;
            if (!HasGunshotAudio && Time.unscaledTime >= _nextGunshotSearch)
            {
                _nextGunshotSearch = Time.unscaledTime + 1f;
                RefreshGunshotClips();
            }
            if (!HasGunshotAudio)
            {
                if (!_warnedMissingGunshot)
                {
                    _warnedMissingGunshot = true;
                    Plugin.Log?.LogWarning("[EscortBench] Gunshot audio unavailable after native loading: " +
                        (_gunshotAssets?.Failure ?? "audio preparation was not requested") + ". Shot simulation is unchanged.");
                }
                return;
            }
            float distance = ListenerDistance(muzzle);
            int preferred = distance < 100f ? 0 : distance < 300f ? 1 : 2;
            AudioClip clip = _gunTails[preferred];
            if (!ClipReady(clip)) foreach (AudioClip candidate in _gunTails) if (ClipReady(candidate)) { clip = candidate; break; }
            AudioSource source = null;
            foreach (AudioSource candidate in _shotSources)
                if (candidate != null && !candidate.isPlaying) { source = candidate; break; }
            if (source == null)
            {
                // The native far tail lasts 7.55s. A held trigger every 0.12s needs
                // up to 64 overlapping voices; 16 would mute new rounds after 2s.
                if (_shotSources.Count >= 64) return;
                var holder = new GameObject("Escort bench native gunshot");
                holder.transform.SetParent(_aircraft.transform, false);
                source = holder.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = false;
                source.spatialBlend = 1f;
                source.rolloffMode = AudioRolloffMode.Logarithmic;
                source.minDistance = 60f;
                source.maxDistance = 1000f;
                source.dopplerLevel = 0f;
                source.volume = .8f; // Verified utes_tail native SoundBank.BaseVolume.
                source.priority = 100;
                _shotSources.Add(source);
            }
            source.transform.position = muzzle;
            // The NSV release clip includes a single attack followed by decay.
            // Playing its 1.62-second body loop would falsely imply a full burst.
            source.PlayOneShot(clip);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _muzzleFlash?.Dispose();
            _muzzleFlash = null;
            foreach (AudioSource source in _loops)
                if (source != null) { source.Stop(); source.enabled = false; }
            foreach (AudioSource source in _shotSources)
                if (source != null) { source.Stop(); Object.Destroy(source.gameObject); }
            _shotSources.Clear();
            Array.Clear(_gunTails, 0, _gunTails.Length);
            _gunshotAssets?.Dispose();
            _gunshotAssets = null;
            // The caller owns the clone. AudioClips, meshes and materials are
            // shared assets and are never destroyed by this presentation helper.
        }

        private float ListenerDistance(Vector3 position)
        {
            if ((_listener == null || !_listener.gameObject.activeInHierarchy) && Time.unscaledTime >= _nextListenerSearch)
            {
                _nextListenerSearch = Time.unscaledTime + 1f;
                foreach (AudioListener candidate in Object.FindObjectsOfType<AudioListener>())
                    if (candidate.enabled && candidate.gameObject.activeInHierarchy) { _listener = candidate.transform; break; }
                if (_listener == null && Camera.main != null) _listener = Camera.main.transform;
            }
            return _listener != null ? Vector3.Distance(position, _listener.position) : 150f;
        }

        private void RefreshGunshotClips()
        {
            try { RefreshGunshotClipsCore(); }
            catch (Exception error) { WarnGunshotFailure(error); }
        }

        private void RefreshGunshotClipsCore()
        {
            foreach (AudioClip clip in Resources.FindObjectsOfTypeAll<AudioClip>())
            {
                if (!ClipReady(clip)) continue;
                switch (clip.name)
                {
                    case "kord_outdoor_close_loop_tail": _gunTails[0] = clip; break;
                    case "utes_outdoor_distant_loop_tail": _gunTails[1] = clip; break;
                    case "utes_outdoor_far_loop_tail": _gunTails[2] = clip; break;
                }
            }
            if (HasGunshotAudio) Plugin.Log?.LogInfo("[EscortBench] Gunshot uses already-loaded native NSV shot-tail audio; no weapon bundle or synthetic audio created.");
        }

        private static bool HasClearance(GameObject aircraft, Vector3 position, Quaternion rotation,
            Collider[] overlaps, out string obstruction)
        {
            // Includes the verified HH60 16.57 x 5.83 x 20.68 m visual envelope,
            // rotor sweep, muzzle probes, and margin, even before HH60 is built.
            Bounds local = new Bounds(new Vector3(0f, 2.5f, 0f), new Vector3(20f, 11f, 26f));
            foreach (MeshFilter filter in aircraft.GetComponentsInChildren<MeshFilter>(true))
                if (filter.sharedMesh != null) IncludeMeshBounds(ref local, aircraft.transform, filter.transform, filter.sharedMesh.bounds);
            foreach (SkinnedMeshRenderer renderer in aircraft.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (renderer.sharedMesh != null) IncludeMeshBounds(ref local, aircraft.transform, renderer.transform, renderer.localBounds);
            foreach (MeshCollider collider in aircraft.GetComponentsInChildren<MeshCollider>(true))
                if (collider.sharedMesh != null) IncludeMeshBounds(ref local, aircraft.transform, collider.transform, collider.sharedMesh.bounds);
            if (local.size.sqrMagnitude > 10000f || !Finite(local.center) || !Finite(local.size))
                throw new InvalidOperationException("[EscortBench] Unexpected aircraft bounds; refusing a placement with an invalid clearance envelope.");
            int count = Physics.OverlapBoxNonAlloc(position + rotation * local.center, local.extents,
                overlaps, rotation, Physics.AllLayers, QueryTriggerInteraction.Ignore);
            if (count >= overlaps.Length)
            {
                obstruction = "world overlap buffer saturated";
                return false;
            }
            for (int i = 0; i < count; i++)
            {
                Collider collider = overlaps[i];
                if (collider == null || collider.transform.IsChildOf(aircraft.transform)) continue;
                obstruction = collider.name;
                return false;
            }
            obstruction = string.Empty;
            return true;
        }

        private static void IncludeMeshBounds(ref Bounds result, Transform aircraft, Transform mesh, Bounds bounds)
        {
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 signs = new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1);
                Vector3 point = bounds.center + Vector3.Scale(bounds.extents, signs);
                result.Encapsulate(aircraft.InverseTransformPoint(mesh.TransformPoint(point)));
            }
        }

        private static bool Finite(Vector3 vector) =>
            !float.IsNaN(vector.x) && !float.IsInfinity(vector.x) &&
            !float.IsNaN(vector.y) && !float.IsInfinity(vector.y) &&
            !float.IsNaN(vector.z) && !float.IsInfinity(vector.z);

        private static AudioSource RequiredLoop(GameObject aircraft, string objectName, string clipName)
        {
            AudioSource source = RequiredTransform(aircraft, objectName).GetComponent<AudioSource>();
            if (source == null || source.clip == null || source.clip.name != clipName)
                throw new InvalidOperationException("Expected original UH60 loop source: " + objectName + " / " + clipName);
            return source;
        }

        private static Transform RequiredTransform(GameObject aircraft, string name)
        {
            Transform result = null;
            foreach (Transform item in aircraft.GetComponentsInChildren<Transform>(true))
            {
                if (item.name != name) continue;
                if (result != null) throw new InvalidOperationException("Ambiguous escort transform: " + name);
                result = item;
            }
            if (result == null) throw new InvalidOperationException("Missing escort transform: " + name);
            return result;
        }

        private static AudioMixerGroup FindTechnicalMixer()
        {
            try
            {
                Type audio = AccessTools.TypeByName("BetterAudio");
                Type singleton = AccessTools.TypeByName("Comfort.Common.Singleton`1");
                if (audio == null || singleton == null) return null;
                Type closedSingleton = singleton.MakeGenericType(audio);
                const BindingFlags singletonFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
                object instance = closedSingleton.GetProperty("Instance", singletonFlags)?.GetValue(null, null)
                    ?? closedSingleton.GetField("Instance", singletonFlags)?.GetValue(null);
                if (instance == null) return null;
                const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                return (audio.GetProperty("EnvTechnicalSoundsGroup", flags)?.GetValue(instance, null)
                    ?? audio.GetField("EnvTechnicalSoundsGroup", flags)?.GetValue(instance)) as AudioMixerGroup;
            }
            catch (Exception error)
            {
                Plugin.Log?.LogWarning("[EscortBench] Native mixer lookup unavailable; original source routing retained: " + error.Message);
                return null;
            }
        }
    }
}
