using System;
using System.Collections;
using System.Reflection;
using System.Threading;
using BepInEx.Configuration;
using EFT;
using HarmonyLib;
using UnityEngine;

namespace TscHh60Visual
{
    public sealed partial class Plugin
    {
        private ConfigEntry<bool> _extractionCoverEnabled;
        private static FieldInfo _coverSupportType, _coverLocalServicePoint, _coverRequestToken, _coverTimingSnapshot;
        private static PropertyInfo _coverSpeedMultiplier;
        private static object _coverExtractSupport;
        private Component _coverAircraft;
        private GameObject _coverPoint;
        private Transform _coverMain;
        private CancellationToken _coverCancellation;
        private float _coverInboundHullSpeed;
        private GameWorld _coverWorld;
        private Player _coverCaller;
        private HelicopterModel _coverModel;
        private ExtractionGunVisuals _coverGuns;
        private EscortBenchTargeting _coverTargeting;
        private EscortBenchBallistics _coverBallistics;
        private EscortBenchAircraft _coverPresentation;
        private readonly EscortHeldTrigger _coverCadence = new EscortHeldTrigger();
        private Coroutine _coverEffectsLoad;
        private IEnumerator _coverEffectsIterator;
        private int _coverGeneration;
        private bool _coverFailed, _coverGunKeyHeld, _coverHasRequest, _coverArrived;
        private string _coverMessage;

        private bool CoverWindowActive => _coverHasRequest &&
            (!_coverArrived || (_coverPoint != null && _coverPoint.activeInHierarchy)) &&
            _coverAircraft != null && _coverAircraft.gameObject.activeInHierarchy && _coverMain != null &&
            !_coverCancellation.IsCancellationRequested;
        private bool ExtractionOwnsManualTrigger => _extractionCoverEnabled != null &&
            _extractionCoverEnabled.Value && CoverWindowActive;

        partial void BindExtractionCover()
        {
            _extractionCoverEnabled = Config.Bind("Extraction showcase", "Enable manual cover fire", false,
                "Use the existing Manual trigger and Select other gun bindings during the inbound approach and pickup hover, including while climbing. " +
                "Requires Permit manual live fire. Tracks hostile AI when available; otherwise fires along the visible barrel. " +
                "Release to stop. Adds door guns to the UH-60 appearance. Solo showcase; shots count as yours.");
        }

        partial void HookExtractionCover(Type aircraft)
        {
            var create = AccessTools.Method(aircraft, "CreateExtractionPoint");
            var destroy = AccessTools.Method(aircraft, "DestroyLandingPoint", new[] { typeof(GameObject) });
            var request = AccessTools.Method(aircraft, "ProcessRequest");
            var cancel = AccessTools.Method(aircraft, "CancelRequestLifetime", Type.EmptyTypes);
            _coverSupportType = AccessTools.Field(aircraft, "_requestSupportType");
            _coverLocalServicePoint = AccessTools.Field(aircraft, "_allowLocalServicePoint");
            _coverRequestToken = AccessTools.Field(aircraft, "_cancellationToken");
            _coverTimingSnapshot = AccessTools.Field(aircraft, "_timingSnapshot");
            _coverSpeedMultiplier = _coverTimingSnapshot == null ? null : AccessTools.Property(_coverTimingSnapshot.FieldType, "SpeedMultiplier");
            if (create == null || destroy == null || request == null || cancel == null ||
                _coverSupportType == null || _coverLocalServicePoint == null || _coverRequestToken == null || _coverSpeedMultiplier == null)
                throw new MissingMethodException("Verified extraction cover lifecycle unavailable.");
            _coverExtractSupport = Enum.Parse(_coverSupportType.FieldType, "Extract");
            _harmony.Patch(request, postfix: new HarmonyMethod(typeof(Plugin), nameof(BeginInboundExtractionCover)));
            _harmony.Patch(cancel, prefix: new HarmonyMethod(typeof(Plugin), nameof(CancelExtractionCoverRequest)));
            _harmony.Patch(create, postfix: new HarmonyMethod(typeof(Plugin), nameof(BeginExtractionCoverWindow)));
            _harmony.Patch(destroy, prefix: new HarmonyMethod(typeof(Plugin), nameof(EndExtractionCoverWindow)));
        }

        private static void BeginInboundExtractionCover(Component __instance, bool visualOnly)
        {
            if (_host == null || __instance == null) return;
            try
            {
                // ProcessRequest has assigned the linked request token and final
                // service type. A pooled aircraft's prior request is no longer eligible.
                if (_host._coverAircraft == __instance) _host.ShutdownExtractionCover();
                if (visualOnly || !(bool)_coverLocalServicePoint.GetValue(__instance) ||
                    !_coverExtractSupport.Equals(_coverSupportType.GetValue(__instance))) return;
                var cancellation = (CancellationToken)_coverRequestToken.GetValue(__instance);
                if (cancellation.IsCancellationRequested) return;
                float speed = (float)_coverSpeedMultiplier.GetValue(_coverTimingSnapshot.GetValue(__instance), null);
                if (!EscortBenchSafetyPolicy.Finite(speed) || speed < .5f || speed > 3f)
                    throw new InvalidOperationException("Unsupported native helicopter speed multiplier.");
                _host.ShutdownExtractionCover();
                _host._coverAircraft = __instance;
                _host._coverMain = FindDescendant(__instance.transform, "b_vhc_main");
                _host._coverCancellation = cancellation;
                // Installed clip derivative bounds over every key/20ms interval
                // give <74m/s for the complete 11m physical hull/rotor radius.
                // Round up and scale by native animation speed (0.5-3), while
                // retaining the existing 70m/s minimum used by escort flight.
                _host._coverInboundHullSpeed = Mathf.Max(70f, 80f * speed);
                _host._coverHasRequest = true;
            }
            catch (Exception error)
            {
                _host.ShutdownExtractionCover();
                Log.LogError("[ExtractionCover] Optional inbound gun setup failed: " + error);
            }
        }

        private static void BeginExtractionCoverWindow(Component __instance, GameObject __result, CancellationToken cancellationToken)
        {
            if (_host == null || __result == null) return;
            try
            {
                // Keep the same guns, effects and cadence through arrival. Old
                // coroutine callbacks must not promote a newer pooled request.
                if (!_host._coverHasRequest || _host._coverAircraft != __instance ||
                    _host._coverCancellation != cancellationToken) return;
                _host._coverPoint = __result;
                _host._coverArrived = true;
                _host._coverTargeting?.SetHullMotionSpeed(70f);
            }
            catch (Exception error)
            {
                _host.ShutdownExtractionCover();
                Log.LogError("[ExtractionCover] Optional pickup gun transition failed: " + error);
            }
        }

        private static void CancelExtractionCoverRequest(Component __instance)
        {
            try
            {
                if (_host != null && _host._coverAircraft == __instance) _host.ShutdownExtractionCover();
            }
            catch (Exception error) { Log.LogError("[ExtractionCover] Optional request gun cleanup failed: " + error); }
        }

        private static void EndExtractionCoverWindow(GameObject landingPoint)
        {
            // Destroy is deferred by Unity; drop the trigger before the departure call returns.
            try
            {
                if (_host != null && landingPoint != null && _host._coverPoint == landingPoint) _host.ShutdownExtractionCover();
            }
            catch (Exception error) { Log.LogError("[ExtractionCover] Optional departure gun cleanup failed: " + error); }
        }

        partial void TickExtractionCover()
        {
            if (!CoverWindowActive)
            {
                if (_coverHasRequest || _coverWorld != null) ShutdownExtractionCover();
                return;
            }
            if (!_extractionCoverEnabled.Value || !_benchLiveShots.Value)
            {
                DisposeExtractionCoverRuntime();
                _coverFailed = false;
                return;
            }
            if (_coverFailed) return;
            try
            {
                if (!TryGetBenchRaid(out var world, out var caller, out var reason))
                { DisposeExtractionCoverRuntime(); CoverStatus(reason); return; }
                if (_coverWorld != null && (_coverWorld != world || _coverCaller != caller || _coverModel != Model.Value))
                    DisposeExtractionCoverRuntime();
                _coverWorld = world;
                _coverCaller = caller;
                _coverModel = Model.Value;
                if (_coverTargeting == null && !PrepareExtractionCover()) return;

                float dt = Time.deltaTime;
                _coverPresentation.Tick(dt);
                bool gameplayInput = Application.isFocused && !Cursor.visible &&
                    Cursor.lockState == CursorLockMode.Locked && Time.timeScale > 0f;
                bool validStep = EscortBenchSafetyPolicy.Finite(dt) && dt > 0f && dt <= .25f;
                bool held = gameplayInput && EscortManualTriggerInput.IsHeld(_benchShotKey.Value, BenchKeyHeld);
                bool gunKey = gameplayInput && EscortManualTriggerInput.IsHeld(_benchGunKey.Value, BenchKeyHeld);
                if (gunKey && !_coverGunKeyHeld) _coverTargeting.CycleGun();
                _coverGunKeyHeld = gunKey;
                if (gameplayInput && validStep) _coverTargeting.Tick(dt);
                else _coverTargeting.Suspend("manual extraction cover paused");
                if (!_coverCadence.Tick(Time.time, held, gameplayInput && validStep && CoverWindowActive)) return;
                if (!_coverTargeting.TryGetFreeShot(out var origin, out var direction, out var ammo, out reason))
                { CoverStatus("Shot blocked: " + reason); return; }
                if (!CoverWindowActive || !_extractionCoverEnabled.Value || !_benchLiveShots.Value) return;
                if (!_coverBallistics.Fire(origin, direction, null, ammo))
                { CoverStatus("Shot submission refused; see LogOutput.log."); return; }
                _coverPresentation.PlayMuzzleFlash(_coverTargeting.SelectedBarrel, origin, direction);
                _coverPresentation.PlayGunshot(origin);
                CoverStatus("Firing " + (_coverTargeting.SelectedGunIndex == 0 ? "left" : "right") +
                    " door gun; release " + _benchShotKey.Value + " to stop.");
            }
            catch (Exception error)
            {
                _coverFailed = true;
                DisposeExtractionCoverRuntime();
                Log.LogError("[ExtractionCover] Cover fire stopped for this pickup: " + error);
            }
        }

        private bool PrepareExtractionCover()
        {
            string marker;
            if (_coverModel == HelicopterModel.HH60)
            {
                var visual = FindDescendant(_coverMain, MarkerName);
                if (visual == null || !visual.gameObject.activeInHierarchy)
                { CoverStatus("Waiting for HH-60 door guns."); return false; }
                marker = MarkerName;
            }
            else
            {
                if (_coverGuns == null) _coverGuns = new ExtractionGunVisuals(_coverMain);
                if (!_coverGuns.TryPrepare(out var reason)) { CoverStatus(reason); return false; }
                marker = ExtractionGunVisuals.MarkerName;
            }
            // The native service root is on the ground. Its animated main bone is
            // the same body-relative basis used by the prepared escort gun rig.
            _coverBallistics = new EscortBenchBallistics(_coverCaller);
            _coverTargeting = new EscortBenchTargeting(_coverMain.gameObject, _coverCaller,
                _coverBallistics, true, -1, marker);
            _coverTargeting.SetHullMotionSpeed(_coverArrived ? 70f : _coverInboundHullSpeed);
            _coverPresentation = new EscortBenchAircraft(_coverMain.gameObject, weaponsOnly: true, gunMarkerName: marker);
            int generation = ++_coverGeneration;
            _coverEffectsIterator = LoadExtractionCoverEffects(_coverPresentation, generation);
            _coverEffectsLoad = StartCoroutine(_coverEffectsIterator);
            CoverStatus("Ready. Hold " + _benchShotKey.Value + " for inbound and pickup cover; " +
                _benchGunKey.Value + " selects the other gun.");
            return true;
        }

        private IEnumerator LoadExtractionCoverEffects(EscortBenchAircraft presentation, int generation)
        {
            Func<bool> current = () => generation == _coverGeneration && CoverWindowActive;
            foreach (var load in new[] { presentation.LoadGunshotAudio(current), presentation.LoadMuzzleFlash(current) })
            {
                try
                {
                    while (current())
                    {
                        bool moved;
                        try { moved = load.MoveNext(); }
                        catch (Exception error)
                        { Log.LogWarning("[ExtractionCover] Optional gun effect unavailable: " + error); break; }
                        if (!moved) break;
                        yield return load.Current;
                    }
                }
                finally { (load as IDisposable)?.Dispose(); }
            }
        }

        private void CoverStatus(string message)
        {
            if (message == _coverMessage) return;
            _coverMessage = message;
            Log.LogInfo("[ExtractionCover] " + message);
        }

        private void DisposeExtractionCoverRuntime()
        {
            ++_coverGeneration;
            try { if (_coverEffectsLoad != null) StopCoroutine(_coverEffectsLoad); }
            catch (Exception error) { Log.LogWarning("[ExtractionCover] Effect coroutine cleanup: " + error); }
            _coverEffectsLoad = null;
            DisposeCoverResource(_coverEffectsIterator as IDisposable);
            _coverEffectsIterator = null;
            DisposeCoverResource(_coverTargeting); _coverTargeting = null;
            DisposeCoverResource(_coverBallistics); _coverBallistics = null;
            DisposeCoverResource(_coverPresentation); _coverPresentation = null;
            DisposeCoverResource(_coverGuns); _coverGuns = null;
            _coverWorld = null; _coverCaller = null;
            _coverCadence.Reset(); _coverGunKeyHeld = false;
        }

        private static void DisposeCoverResource(IDisposable resource)
        {
            try { resource?.Dispose(); }
            catch (Exception error) { Log.LogWarning("[ExtractionCover] Optional resource cleanup: " + error); }
        }

        partial void ShutdownExtractionCover()
        {
            DisposeExtractionCoverRuntime();
            _coverPoint = null; _coverAircraft = null; _coverMain = null;
            _coverCancellation = default;
            _coverInboundHullSpeed = 0f;
            _coverHasRequest = _coverArrived = false;
            _coverFailed = false; _coverMessage = null;
        }
    }
}
