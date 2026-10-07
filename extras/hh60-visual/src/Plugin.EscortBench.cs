using System;
using System.Collections;
using System.Linq;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using Comfort.Common;
using Cysharp.Threading.Tasks;
using EFT;
using EFT.Communications;
using HarmonyLib;
using UnityEngine;

namespace TscHh60Visual
{
    // Local tests. Weapon capabilities are captured when the aircraft is summoned.
    public sealed partial class Plugin
    {
        private ConfigEntry<bool> _benchEnabled, _benchLiveShots, _benchShowHud;
        private ConfigEntry<EscortTestMode> _benchMode;
        private EscortTestMode _activeBenchMode;
        private ConfigEntry<EscortPatrolPattern> _benchPatrolPattern;
        private EscortPatrolPattern _activePatrolPattern;
        private ConfigEntry<KeyboardShortcut> _benchSummonKey, _benchGunKey, _benchShotKey;
        private GameWorld _benchWorld;
        private Player _benchCaller;
        private GameObject _benchAircraft;
        private EscortBenchAircraft _benchPresentation;
        private EscortBenchTargeting _benchTargeting;
        private EscortBenchBallistics _benchBallistics;
        private EscortFlight _benchFlight;
        private Coroutine _benchLoad;
        private bool _benchLoading, _benchCancelRequested;
        private int _benchGeneration;
        private float _benchExpiresAt, _benchNextLog;
        private float _benchLastShotNotice = -10f;
        private string _benchMessage = "Ready for an observation-only test.";
        private string _benchLastSummary;
        private GUIStyle _benchText;
        private bool HasBenchState => _benchAircraft != null || _benchLoading || _benchWorld != null ||
            _benchPresentation != null || _benchTargeting != null || _benchRightTargeting != null || _benchBallistics != null || _benchFlight != null;

        partial void BindEscortBench()
        {
            const string section = "Escort bench (experimental)";
            var legacyEnabled = Config.Bind(section, "Enable stationary test", false, "Previous escort test enable setting.");
            _benchEnabled = Config.Bind(section, "Enable escort test", legacyEnabled.Value,
                "Enable a manually summoned HH-60 test in a solo outdoor raid. Choose stationary shots, flight only, moving manual shots, or automatic patrol bursts.");
            Config.Remove(legacyEnabled.Definition);
            _benchShowHud = Config.Bind(section, "Show escort test HUD", false,
                "Show the helicopter test instructions, status box, target labels and shot notifications. Turn off for clean recordings. Changes apply immediately; helicopter controls and logging stay active.");
            _benchMode = Config.Bind(section, "Test mode", EscortTestMode.StationaryShots,
                "StationaryShots keeps the fixed-position held-trigger test. PatrolFlightOnly keeps all guns disabled. PatrolManualShots tracks with manually held fire. PatrolAutomaticBursts operates both door guns independently with separate automatic permission, and allows the manual trigger to take control. Changes apply to the next summoned helicopter.");
            _benchPatrolPattern = Config.Bind(section, "Patrol pattern", EscortPatrolPattern.Mixed,
                "Mixed alternates broadside passes with occasional orbits. Racetrack keeps long passes with rounded turns. Orbit keeps the original circle. All patterns fly toward you directly when you get far ahead. Changes apply to the next summoned helicopter.");
            var previousManualPermission = Config.Bind(section, "Permit single live shots", false, "Previous manual fire permission.");
            _benchLiveShots = Config.Bind(section, "Permit manual live fire", previousManualPermission.Value,
                "Hold the manual trigger to fire the selected visible barrel every 0.12 seconds, including without a target. Release to stop. Helicopter and protected-actor checks still apply. The raid player owns these shots: kills, quests, XP and faction consequences may apply.");
            Config.Remove(previousManualPermission.Definition);
            _benchSummonKey = Config.Bind(section, "Summon or recall", new KeyboardShortcut(KeyCode.Insert, KeyCode.RightControl),
                "Summon the selected test above and to your right. Press again to recall; a patrol departs smoothly. Press again during departure for immediate removal. Face along the desired heading before summoning.");
            _benchGunKey = Config.Bind(section, "Select other gun", new KeyboardShortcut(KeyCode.PageUp, KeyCode.RightControl),
                "Select the left or right gun for the manual trigger. Both automatic controllers pause while the manual trigger is held.");
            var previousShotKey = Config.Bind(section, "Fire one test round", new KeyboardShortcut(KeyCode.PageDown, KeyCode.RightControl), "Previous manual trigger binding.");
            _benchShotKey = Config.Bind(section, "Manual trigger", previousShotKey.Value,
                "Hold to fire the selected gun along its current visible barrel, even without an enemy. Shared with Extraction showcase / Enable manual cover fire. Release to stop. Requires Permit manual live fire. Overrides automatic bursts while held.");
            Config.Remove(previousShotKey.Definition);
            MigrateBenchShortcut(_benchSummonKey, KeyCode.F8, KeyCode.Insert);
            MigrateBenchShortcut(_benchGunKey, KeyCode.F9, KeyCode.PageUp);
            MigrateBenchShortcut(_benchShotKey, KeyCode.F10, KeyCode.PageDown);
            BindAutomaticBench(section);
        }

        private static void MigrateBenchShortcut(ConfigEntry<KeyboardShortcut> entry, KeyCode legacy, KeyCode replacement)
        {
            var shortcut = entry.Value;
            if (shortcut.MainKey != legacy || shortcut.Modifiers.Count() != 1 || !shortcut.Modifiers.Contains(KeyCode.LeftAlt)) return;
            entry.Value = new KeyboardShortcut(replacement, KeyCode.RightControl);
            Log?.LogInfo("[EscortBench] Migrated conflicting escort shortcut: " + entry.Definition.Key + " -> " + entry.Value);
        }

        partial void TickEscortBench()
        {
            EscortBenchBallistics.TickObservers();
            if (_benchEnabled == null) return;
            if (!_benchEnabled.Value)
            {
                if (HasBenchState) EndBench("Escort test disabled.");
                return;
            }

            if (!TryGetBenchRaid(out var world, out var caller, out var unavailable))
            {
                if (HasBenchState) EndBench(unavailable);
                return;
            }
            if (_benchWorld != null && (_benchWorld != world || _benchCaller != caller))
                EndBench("Raid or protected player changed.");
            if (!_benchLoading && _benchAircraft == null && HasBenchState)
                EndBench("Test aircraft no longer exists.");

            // Do not consume test hotkeys while inventory, console, or a menu owns the cursor.
            bool inputAvailable = Application.isFocused && Cursor.lockState == CursorLockMode.Locked && Time.timeScale > 0f;
            if (inputAvailable && _benchSummonKey.Value.IsDown())
            {
                if (_benchFlight != null && !_benchFlight.IsDeparting)
                {
                    _benchFlight.BeginDeparture();
                    SetBenchMessage("Patrol departing. Press summon/recall again to remove it immediately.");
                }
                else if (_benchLoading || _benchAircraft != null) EndBench("Test recalled.");
                else StartBench(world, caller);
            }
            // BepInEx shortcuts reject unrelated held keys, including WASD.
            // A gameplay trigger must remain held while the player/freecam moves.
            bool rawHeld = EscortManualTriggerInput.IsHeld(_benchShotKey.Value, BenchKeyHeld);
            bool shotPressed = inputAvailable && !ExtractionOwnsManualTrigger && rawHeld && !_benchManualHeld;
            if (rawHeld != _benchManualRawHeld)
                Log.LogInfo("[EscortBench] manual-trigger input=" + (rawHeld ? "held" : "released") +
                    " binding=" + _benchShotKey.Value + " gameplayInput=" + inputAvailable +
                    " focused=" + Application.isFocused + " cursor=" + Cursor.lockState + " timeScale=" + Time.timeScale);
            _benchManualRawHeld = rawHeld;
            _benchManualHeld = inputAvailable && !ExtractionOwnsManualTrigger && rawHeld;
            if (_benchAircraft == null || _benchLoading)
            {
                if (shotPressed) ReportBenchShot(_benchLoading ? "Helicopter is still preparing." : "Summon the escort test helicopter first.");
                return;
            }
            if (Time.time >= _benchExpiresAt)
            {
                EndBench("Test lifetime expired; summon again if needed.");
                return;
            }

            try
            {
                if (_benchFlight != null && !_benchFlight.Tick(Time.deltaTime))
                {
                    EndBench("Patrol finished: " + _benchFlight.Status);
                    return;
                }
                _benchPresentation.Tick(Time.deltaTime);
                if (_benchFlight != null) Physics.SyncTransforms();
                TickBenchTargeting(Time.deltaTime);
                if (inputAvailable && !ExtractionOwnsManualTrigger && _benchGunKey.Value.IsDown())
                {
                    if (EscortBenchModePolicy.IsAutomatic(_activeBenchMode))
                    {
                        _benchManualGun = 1 - _benchManualGun;
                        SetBenchMessage("Manual trigger selects the " + (_benchManualGun == 0 ? "left" : "right") + " door gun.");
                    }
                    else if (_benchTargeting != null) _benchTargeting.CycleGun();
                    else SetBenchMessage("Patrol flight test: guns are disabled.");
                }
                TickAutomaticBench(inputAvailable, Time.deltaTime);
                if (_benchAircraft == null) return;
                TickHeldBenchTrigger(shotPressed, Time.deltaTime);
                if (_benchAircraft == null) return;
                if (Time.unscaledTime >= _benchNextLog)
                {
                    _benchNextLog = Time.unscaledTime + 3f;
                    string summary = _benchFlight != null ? _benchFlight.Status + " | " + _benchFlight.Telemetry : _benchTargeting.Summary;
                    if (_benchFlight != null && _benchTargeting != null) summary += " | " + _benchTargeting.Summary;
                    if (_benchRightTargeting != null) summary += " | " + _benchRightTargeting.Summary + " | " + AutomaticBenchSummary;
                    if (summary != _benchLastSummary)
                    {
                        _benchLastSummary = summary;
                        Log.LogInfo("[EscortBench] " + summary);
                    }
                }
            }
            catch (Exception error)
            {
                Log.LogError("[EscortBench] Test stopped after a controller error: " + error);
                EndBench("Controller error; see LogOutput.log.");
            }
        }

        private static bool TryGetBenchRaid(out GameWorld world, out Player caller, out string reason)
        {
            world = Singleton<GameWorld>.Instance;
            caller = world == null ? null : world.MainPlayer;
            if (Chainloader.PluginInfos.ContainsKey("com.fika.core"))
            {
                reason = "Escort test is unavailable with Fika loaded; multiplayer behavior is not validated.";
                return false;
            }
            var game = Singleton<AbstractGame>.Instance;
            if (world == null || caller == null || !caller.IsYourPlayer || caller.IsAI ||
                caller.HealthController == null || !caller.HealthController.IsAlive || game == null ||
                (game.Status != GameStatus.Started && game.Status != GameStatus.Running))
            {
                reason = "A live solo raid is required.";
                return false;
            }
            for (var type = world.GetType(); type != null; type = type.BaseType)
                if (type.Name.IndexOf("Hideout", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    reason = "The escort test is unavailable in the hideout.";
                    return false;
                }
            // Accommodate unfamiliar networking plugins by refusing additional human actors.
            foreach (var player in world.AllAlivePlayersList)
                if (player != null && player != caller && !player.IsAI)
                {
                    reason = "Additional human actor detected; solo test stopped.";
                    return false;
                }
            reason = null;
            return true;
        }

        private void StartBench(GameWorld world, Player caller)
        {
            if (!EscortBenchModePolicy.Valid(_benchMode.Value))
            {
                ReportBenchShot("Unknown test mode. Select a supported mode in F12 before summoning.");
                return;
            }
            _benchWorld = world;
            _benchCaller = caller;
            _activeBenchMode = _benchMode.Value;
            _activePatrolPattern = Enum.IsDefined(typeof(EscortPatrolPattern), _benchPatrolPattern.Value)
                ? _benchPatrolPattern.Value : EscortPatrolPattern.Mixed;
            _benchLoading = true;
            _benchCancelRequested = false;
            _benchLastSummary = null;
            _benchHoldFire = false;
            ResetHeldBenchTrigger();
            int generation = ++_benchGeneration;
            SetBenchMessage("Preparing the helicopter; first asset load may take a moment.");
            var routine = StartCoroutine(GuardBenchLoad(LoadBench(generation), generation));
            if (_benchLoading) _benchLoad = routine;
        }

        private IEnumerator GuardBenchLoad(IEnumerator builder, int generation)
        {
            while (true)
            {
                object current = null;
                Exception failure = null;
                bool moved = false;
                try { moved = builder.MoveNext(); if (moved) current = builder.Current; }
                catch (Exception error) { failure = error; }
                if (failure != null)
                {
                    Log.LogError("[EscortBench] Preparation failed: " + failure);
                    if (generation == _benchGeneration) EndBench("Preparation failed: " + failure.GetBaseException().Message);
                }
                if (failure != null || !moved)
                {
                    (builder as IDisposable)?.Dispose();
                    if (generation == _benchGeneration) { _benchLoading = false; _benchLoad = null; }
                    yield break;
                }
                yield return current;
            }
        }

        private IEnumerator LoadBench(int generation)
        {
            // Borrow TSC's bundle cache and load lock. No second bundle owner or unload path.
            var loader = AccessTools.TypeByName("SamSWAT.FireSupport.ArysReloaded.Utils.AssetLoader");
            var method = loader?.GetMethods(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public)
                .SingleOrDefault(item => item.Name == "LoadAssetAsync" && !item.IsGenericMethod && item.GetParameters().Length == 2);
            if (method == null) throw new MissingMethodException("Verified TSC prefab loader unavailable.");
            var request = (UniTask<GameObject>)method.Invoke(null,
                new object[] { "assets/content/vehicles/uh60_blackhawk.bundle", null });
            var awaiter = request.GetAwaiter();
            while (!awaiter.IsCompleted) yield return null;
            var prefab = awaiter.GetResult();
            if (!BenchLoadCurrent(generation)) yield break;
            if (prefab == null) throw new InvalidOperationException("Native UH-60 prefab failed to load.");

            _benchAircraft = EscortBenchAircraft.Prepare(prefab, _benchCaller.Transform.Original,
                EscortBenchModePolicy.HasFlight(_activeBenchMode) ? 65f : 40f);
            var anchor = FindDescendant(_benchAircraft.transform, "b_vhc_main");
            if (anchor == null) throw new InvalidOperationException("Native body anchor unavailable.");
            var visual = _benchAircraft.AddComponent<VisualInstance>();
            visual.Initialize(_benchAircraft.transform, anchor, ScenePath);
            visual.PinToHh60();
            float deadline = Time.unscaledTime + 60f;
            while (!visual.IsReady)
            {
                if (!BenchLoadCurrent(generation)) yield break;
                if (visual.HasFailed) throw new InvalidOperationException("HH-60 visual preparation failed.");
                if (Time.unscaledTime > deadline) throw new TimeoutException("HH-60 visual preparation timed out.");
                yield return null;
            }
            if (!BenchLoadCurrent(generation)) yield break;
            _benchPresentation = new EscortBenchAircraft(_benchAircraft);
            if (EscortBenchModePolicy.HasWeapons(_activeBenchMode))
            {
                var audioLoad = _benchPresentation.LoadGunshotAudio(() => BenchLoadCurrent(generation));
                try
                {
                    while (audioLoad.MoveNext()) yield return audioLoad.Current;
                }
                finally { (audioLoad as IDisposable)?.Dispose(); }
                if (!BenchLoadCurrent(generation)) yield break;
                var flashLoad = _benchPresentation.LoadMuzzleFlash(() => BenchLoadCurrent(generation));
                try
                {
                    while (flashLoad.MoveNext()) yield return flashLoad.Current;
                }
                finally { (flashLoad as IDisposable)?.Dispose(); }
                if (!BenchLoadCurrent(generation)) yield break;
            }
            _benchAircraft.SetActive(true);
            Physics.SyncTransforms();
            HelicopterDebugOverlay.Attach(_benchAircraft);
            if (EscortBenchModePolicy.HasFlight(_activeBenchMode))
                _benchFlight = new EscortFlight(_benchAircraft, _benchCaller, pattern: _activePatrolPattern);
            if (EscortBenchModePolicy.HasWeapons(_activeBenchMode))
            {
                _benchBallistics = new EscortBenchBallistics(_benchCaller);
                if (EscortBenchModePolicy.IsAutomatic(_activeBenchMode)) PrepareAutomaticBench();
                else _benchTargeting = _activeBenchMode == EscortTestMode.PatrolManualShots
                    ? new EscortBenchTargeting(_benchAircraft, _benchCaller, _benchBallistics, true)
                    : new EscortBenchTargeting(_benchAircraft, _benchCaller, _benchBallistics, false);
            }
            _benchExpiresAt = Time.time + 300f;
            _benchNextLog = 0f;
            SetBenchMessage(EscortBenchModePolicy.IsAutomatic(_activeBenchMode)
                ? "Automatic patrol ready (" + _activePatrolPattern + "). Both guns use short bursts when Permit automatic bursts is enabled; " + _benchHoldFireKey.Value + " holds fire."
                : _activeBenchMode == EscortTestMode.PatrolManualShots
                ? "Moving-gun test ready (" + _activePatrolPattern + "). Hold the manual trigger to fire the selected barrel; enemy acquisition is optional."
                : _activeBenchMode == EscortTestMode.PatrolFlightOnly
                ? "Patrol flight test ready (" + _activePatrolPattern + "). All guns are disabled; recall to end the patrol."
                : "Stationary test ready. Hold the manual trigger to fire the selected barrel when manual live fire is enabled.");
            if (_benchBallistics != null) Log.LogInfo("[EscortBench] " + _benchBallistics.Description);
        }

        private bool BenchLoadCurrent(int generation)
        {
            if (generation != _benchGeneration || _benchCancelRequested || !_benchEnabled.Value) return false;
            return TryGetBenchRaid(out var world, out var caller, out _) && world == _benchWorld && caller == _benchCaller;
        }

        private void ReportBenchShot(string message, bool warning = true)
        {
            SetBenchMessage(message);
            if (_benchShowHud == null || !_benchShowHud.Value) return;
            if (Time.unscaledTime - _benchLastShotNotice < 1.5f) return;
            _benchLastShotNotice = Time.unscaledTime;
            try
            {
                string notice = "HH-60: " + (message.Length > 180 ? message.Substring(0, 177) + "..." : message);
                if (warning) NotificationManager.DisplayWarningNotification(notice, ENotificationDurationType.Default);
                else NotificationManager.DisplayMessageNotification(notice, ENotificationDurationType.Default, ENotificationIconType.Default, null);
            }
            catch (Exception error) { Log.LogWarning("[EscortBench] Shot notice unavailable: " + error.GetBaseException().Message); }
        }

        private void EndBench(string reason)
        {
            _benchCancelRequested = true;
            ResetHeldBenchTrigger();
            _benchFlight = null;
            // Pending shared asset loading is observed to completion, but cannot create another rig.
            _benchTargeting?.Dispose(); _benchTargeting = null;
            DisposeAutomaticBench();
            _benchBallistics?.Dispose(); _benchBallistics = null;
            _benchPresentation?.Dispose(); _benchPresentation = null;
            if (_benchAircraft != null)
            {
                // Never-activated Unity components may receive no OnDestroy.
                // Explicitly release their persistent-host coroutines and registration.
                foreach (var visual in _benchAircraft.GetComponentsInChildren<VisualInstance>(true))
                {
                    visual.AbortAndRestore();
                    Unregister(visual);
                }
                _benchAircraft.SetActive(false);
                Destroy(_benchAircraft);
                _benchAircraft = null;
            }
            _benchWorld = null; _benchCaller = null;
            if (_benchMessage != reason) SetBenchMessage(reason);
        }

        private void SetBenchMessage(string message)
        {
            _benchMessage = message;
            Log.LogInfo("[EscortBench] " + message);
        }

        partial void ShutdownEscortBench()
        {
            EndBench("Plugin shutting down.");
            ++_benchGeneration;
            if (_benchLoad != null) StopCoroutine(_benchLoad);
            _benchLoad = null; _benchLoading = false;
            EscortBenchBallistics.ShutdownObservers();
        }

        partial void DrawEscortBench()
        {
            if (!_compatible || _benchEnabled == null || !_benchEnabled.Value || _benchShowHud == null || !_benchShowHud.Value) return;
            var world = Singleton<GameWorld>.Instance;
            if (world == null || world.MainPlayer == null) return;
            if (_benchText == null) _benchText = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true };
            string availability = TryGetBenchRaid(out _, out _, out var unavailable) ? _benchMessage : unavailable;
            var mode = _benchAircraft != null || _benchLoading ? _activeBenchMode : _benchMode.Value;
            bool patrol = EscortBenchModePolicy.HasFlight(mode);
            bool weapons = EscortBenchModePolicy.HasWeapons(mode);
            bool automatic = EscortBenchModePolicy.IsAutomatic(mode);
            var pattern = _benchAircraft != null || _benchLoading ? _activePatrolPattern : _benchPatrolPattern.Value;
            string title = automatic ? "HH-60 AUTOMATIC PATROL TEST" : mode == EscortTestMode.PatrolManualShots ? "HH-60 MOVING-GUN TEST" : patrol ? "HH-60 PATROL FLIGHT TEST" : "HH-60 STATIONARY BENCH";
            string text = title + "  |  " + (automatic ? (!_benchAutomaticBursts.Value ? "AUTO DISABLED" : _benchHoldFire ? "AUTO HOLD FIRE" : "AUTOMATIC BURSTS PERMITTED") : !weapons ? "GUNS DISABLED" : _benchLiveShots.Value ? "MANUAL LIVE FIRE PERMITTED" : "OBSERVATION ONLY") +
                (patrol ? "  |  " + pattern : "") +
                "\n" + _benchSummonKey.Value + " summon/recall" + (automatic ? "   |   " + _benchHoldFireKey.Value + " hold/resume auto" : "") +
                (!weapons ? "" : "\n" + _benchGunKey.Value + " select gun   |   hold " + _benchShotKey.Value + " to fire") +
                "\n" + availability;
            if (_benchTargeting != null) text += "\n" + _benchTargeting.Summary;
            if (_benchRightTargeting != null) text += "\n" + _benchRightTargeting.Summary + "\n" + AutomaticBenchSummary;
            if (weapons) text += "\nManual trigger: " + ManualBenchGunName + " | " + (!_benchLiveShots.Value ? "disabled in F12" :
                !Application.isFocused || Cursor.lockState != CursorLockMode.Locked || Time.timeScale <= 0f ? "INPUT PAUSED (window unfocused, menu/cursor or game paused)" :
                _benchManualHeld ? (_benchManualBlockReason == null ? "HELD" : "BLOCKED: " + _benchManualBlockReason) : "ready");
            if (_benchFlight != null) text += "\n" + _benchFlight.Status;
            if (weapons && _benchPresentation != null && !_benchPresentation.HasGunshotAudio)
                text += "\nGunshot sound is not loaded; native projectile tests can still run.";
            float width = Mathf.Min(580f, Screen.width - 40f);
            float height = _benchText.CalcHeight(new GUIContent(text), width - 20f) + 20f;
            GUI.Box(new Rect(20f, 120f, width, height), GUIContent.none);
            GUI.Label(new Rect(30f, 130f, width - 20f, height - 20f), text, _benchText);
            if (_benchTargeting?.AimPoint is Vector3 aim && Camera.main != null)
            {
                var point = Camera.main.WorldToScreenPoint(aim);
                if (point.z > 0f)
                    GUI.Label(new Rect(point.x - 35f, Screen.height - point.y - 10f, 100f, 25f), "+ TRACK", _benchText);
            }
            if (_benchRightTargeting?.AimPoint is Vector3 rightAim && Camera.main != null)
            {
                var point = Camera.main.WorldToScreenPoint(rightAim);
                if (point.z > 0f) GUI.Label(new Rect(point.x - 35f, Screen.height - point.y + 10f, 100f, 25f), "+ RIGHT", _benchText);
            }
        }
    }
}
