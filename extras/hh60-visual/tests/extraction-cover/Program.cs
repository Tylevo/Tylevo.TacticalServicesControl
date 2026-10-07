using System;
using System.Threading;
using EFT;
using TscHh60Visual;
using UnityEngine;
using Object = UnityEngine.Object;

internal static class Program
{
    private static int _checks;
    private static void Check(bool value, string message) { _checks++; if (!value) throw new Exception(message); }
    private static void Hold(params KeyCode[] keys) { Input.Held.Clear(); foreach (var key in keys) Input.Held.Add(key); }
    private static void Trigger() => Hold(KeyCode.RightControl, KeyCode.PageDown, KeyCode.W);

    private sealed class Fixture : IDisposable
    {
        internal readonly Plugin Plugin;
        internal readonly NativeAircraft Aircraft;
        internal readonly GameObject Main, Point;
        internal bool PointLostExternally;
        internal Fixture()
        {
            Time.time = 1; Time.deltaTime = .02f; Time.timeScale = 1;
            Application.isFocused = true; Cursor.visible = false; Cursor.lockState = CursorLockMode.Locked;
            Hold();
            EscortBenchBallistics.TotalRounds = 0; EscortBenchBallistics.AllowFire = true;
            EscortBenchTargeting.ThrowOnTick = false; EscortBenchTargeting.AllowShot = true;
            ExtractionGunVisuals.Ready = true;
            TscHh60Visual.Plugin.Model.Value = HelicopterModel.UH60;
            Aircraft = new GameObject("native service aircraft").AddComponent<NativeAircraft>();
            Main = new GameObject("b_vhc_main"); Main.transform.SetParent(Aircraft.transform, false);
            Point = new GameObject("native extraction point");
            Plugin = new GameObject("host").AddComponent<Plugin>(); Plugin.Initialize();
        }
        internal void Enable() { Plugin.Enabled = true; Plugin.Live = true; }
        internal void Request(CancellationToken cancellation = default, bool cargo = false, bool local = true, bool visualOnly = false)
        {
            Aircraft._cancellationToken = cancellation;
            Aircraft._requestSupportType = cargo ? FakeSupportType.PriorityExfil : FakeSupportType.Extract;
            Aircraft._allowLocalServicePoint = local;
            Plugin.Inbound(Aircraft, visualOnly);
        }
        internal void Arrive(CancellationToken cancellation = default) { Request(cancellation); Plugin.Begin(Aircraft, Point, cancellation); }
        internal void Ready() { Enable(); Arrive(); Plugin.Tick(); }
        internal void NativeIntact(string scope) => Check(Aircraft != null && Aircraft.gameObject.activeInHierarchy && Main != null && (PointLostExternally || Point != null), scope + ": native service objects must remain intact");
        public void Dispose()
        {
            Plugin.Shutdown();
            Check(EscortBenchAircraft.ActiveLoads == 0, "effect iterator must release when cover ends");
            NativeIntact("fixture cleanup");
        }
    }

    public static int Main()
    {
        try
        {
            ArrivalAndPermissions();
            InputAndCadence();
            DepartureAndCancellation();
            RaidAndModelChanges();
            FailureAndRecovery();
            PreparationWait();
            InboundAndArrivalContinuity();
            InboundEligibilityAndCancellation();
            PooledRequestIsolation();
            Console.WriteLine("PASS " + _checks + " production extraction-cover orchestration/input/cadence checks.");
            Console.WriteLine("Uses substitutes for Unity, EFT, Harmony, targeting, ballistics, donor geometry and effects; live integration remains untested.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static void ArrivalAndPermissions()
    {
        using var f = new Fixture();
        Trigger(); f.Plugin.Tick();
        Check(!f.Plugin.Enabled && !f.Plugin.HasRuntime && EscortBenchBallistics.TotalRounds == 0, "default option must be disabled");
        f.Enable(); f.Plugin.Tick();
        Check(!f.Plugin.OwnsTrigger && !f.Plugin.HasRuntime, "no request must mean no cover preparation or firing");
        f.Plugin.Begin(f.Aircraft, null); f.Plugin.Tick();
        Check(!f.Plugin.HasRuntime, "failed extraction-point creation must not open cover window");
        f.Plugin.Enabled = false; f.Arrive(); f.Plugin.Tick();
        Check(!f.Plugin.OwnsTrigger && !f.Plugin.HasRuntime && EscortBenchBallistics.TotalRounds == 0, "option off during arrival must not prepare or fire");
        f.Plugin.Enabled = true; f.Plugin.Live = false; f.Plugin.Tick();
        Check(!f.Plugin.HasRuntime && EscortBenchBallistics.TotalRounds == 0, "manual permission off must not prepare or fire");
        f.Plugin.Live = true; f.Plugin.Tick();
        Check(f.Plugin.OwnsTrigger && f.Plugin.HasRuntime && EscortBenchBallistics.TotalRounds == 1, "arrival plus permission should allow held manual trigger");
        Check(f.Plugin.Presentation.WeaponsOnly && f.Plugin.Presentation.NativeAircraft == f.Main, "cover presentation must use weapons-only animated-main mode");
        Check(f.Plugin.Presentation.Marker == ExtractionGunVisuals.MarkerName, "fresh UH60 effects must bind the same extraction guns as targeting");
        Check(f.Plugin.Ballistics.Owner == f.Plugin.Caller, "caller must own manual ballistics");
        var target = f.Plugin.Targeting; var guns = f.Plugin.Guns;
        f.Plugin.Live = false; f.Plugin.Tick(.2f);
        Check(target.Disposed && guns.Disposed && !f.Plugin.HasRuntime && EscortBenchBallistics.TotalRounds == 1, "removing manual permission must immediately stop and dispose cover");
        f.Plugin.Live = true; f.Plugin.Tick();
        Check(f.Plugin.HasRuntime && f.Plugin.Targeting != target && EscortBenchBallistics.TotalRounds == 2, "permission can resume within active window");
        f.NativeIntact("permissions");
    }

    private static void InputAndCadence()
    {
        using var f = new Fixture(); f.Ready();
        Trigger(); f.Plugin.Tick();
        Check(EscortBenchBallistics.TotalRounds == 1 && Input.GetKey(KeyCode.W), "held trigger must fire with simultaneous climb key W");
        f.Plugin.Tick(.02f);
        Check(EscortBenchBallistics.TotalRounds == 1, "held trigger must respect minimum interval");
        f.Plugin.Tick(.13f);
        Check(EscortBenchBallistics.TotalRounds == 2, "continued hold should fire another authorized round");
        Hold(KeyCode.W); f.Plugin.Tick(.13f);
        Check(EscortBenchBallistics.TotalRounds == 2, "release must immediately stop rounds while movement remains held");
        Hold(KeyCode.PageDown, KeyCode.W); f.Plugin.Tick(.13f);
        Check(EscortBenchBallistics.TotalRounds == 2, "trigger without required modifier must not fire");
        Hold(KeyCode.RightControl, KeyCode.PageUp, KeyCode.W); f.Plugin.Tick(); f.Plugin.Tick();
        Check(f.Plugin.Targeting.Cycles == 1 && f.Plugin.Targeting.SelectedGunIndex == 1, "held selection chord must cycle once");
        Hold(KeyCode.W); f.Plugin.Tick(); Hold(KeyCode.RightControl, KeyCode.PageUp, KeyCode.W); f.Plugin.Tick();
        Check(f.Plugin.Targeting.Cycles == 2 && f.Plugin.Targeting.SelectedGunIndex == 0, "selection release and press must cycle again");
        Trigger(); Application.isFocused = false; f.Plugin.Tick(.13f);
        Application.isFocused = true; Cursor.visible = true; f.Plugin.Tick(.13f);
        Cursor.visible = false; Cursor.lockState = CursorLockMode.None; f.Plugin.Tick(.13f);
        Cursor.lockState = CursorLockMode.Locked; Time.timeScale = 0; f.Plugin.Tick(.13f);
        Time.timeScale = 1; f.Plugin.Tick(.3f);
        Check(EscortBenchBallistics.TotalRounds == 2 && f.Plugin.Targeting.Suspends == 5, "focus/menu/unlocked cursor/pause/invalid long frame must suspend cover");
        f.Plugin.Tick(.02f);
        Check(EscortBenchBallistics.TotalRounds == 3, "eligible input should resume after pause without catch-up rounds");
        EscortBenchTargeting.AllowShot = false; f.Plugin.Tick(.13f);
        Check(EscortBenchBallistics.TotalRounds == 3, "fresh target authorization must be required per round");
        EscortBenchTargeting.AllowShot = true; EscortBenchBallistics.AllowFire = false; f.Plugin.Tick(.13f);
        Check(f.Plugin.Presentation.Flashes == 3 && f.Plugin.Presentation.Sounds == 3, "effects must follow accepted ballistics only");
    }

    private static void DepartureAndCancellation()
    {
        using (var f = new Fixture())
        {
            f.Ready(); Trigger(); f.Plugin.Tick(); var target = f.Plugin.Targeting;
            f.Plugin.End(new GameObject("another point"));
            Check(f.Plugin.HasRuntime && !target.Disposed, "unrelated departure point must not close active pickup");
            f.Plugin.End(f.Point);
            Check(target.Disposed && !f.Plugin.OwnsTrigger && !f.Plugin.HasRuntime && f.Plugin.Point == null, "departure prefix must stop cover before deferred native destruction");
            f.Plugin.Tick(.13f);
            Check(EscortBenchBallistics.TotalRounds == 1, "held trigger must remain stopped after departure");
            f.NativeIntact("departure prefix");
        }
        using (var f = new Fixture())
        using (var cancellation = new CancellationTokenSource())
        {
            f.Enable(); f.Arrive(cancellation.Token); Trigger(); f.Plugin.Tick(); var target = f.Plugin.Targeting;
            cancellation.Cancel();
            Check(!f.Plugin.OwnsTrigger, "cancellation must immediately invalidate trigger ownership");
            f.Plugin.Tick(.13f);
            Check(target.Disposed && !f.Plugin.HasRuntime && EscortBenchBallistics.TotalRounds == 1, "cancelled window must not fire another round");
        }
        using (var f = new Fixture())
        {
            f.Ready(); Trigger(); f.Point.SetActive(false); f.Plugin.Tick();
            Check(!f.Plugin.HasRuntime && EscortBenchBallistics.TotalRounds == 0, "inactive extraction point must immediately stop cover");
            f.Point.SetActive(true); f.Arrive(); f.Plugin.Tick(); var target = f.Plugin.Targeting;
            Object.Destroy(f.Point); f.PointLostExternally = true; f.Plugin.Tick(.13f);
            Check(target.Disposed && !f.Plugin.HasRuntime && EscortBenchBallistics.TotalRounds == 1, "lost extraction point must stop cover via Unity destroyed-object semantics");
            // Native point deletion was the injected external event, not cover cleanup.
            Check(f.Aircraft != null && f.Main != null, "lost point cleanup must retain native aircraft");
        }
    }

    private static void RaidAndModelChanges()
    {
        using var f = new Fixture(); f.Ready(); var target = f.Plugin.Targeting;
        f.Plugin.RaidValid = false; Trigger(); f.Plugin.Tick();
        Check(target.Disposed && !f.Plugin.HasRuntime && EscortBenchBallistics.TotalRounds == 0, "invalid or ended raid must clean runtime and stop firing");
        f.Plugin.RaidValid = true; f.Plugin.Raid = new GameWorld(); f.Plugin.Caller = new Player(); Hold(); f.Plugin.Tick();
        var oldBallistics = f.Plugin.Ballistics; target = f.Plugin.Targeting;
        f.Plugin.Raid = new GameWorld(); f.Plugin.Caller = new Player(); f.Plugin.Tick();
        Check(target.Disposed && oldBallistics.Disposed && f.Plugin.Ballistics.Owner == f.Plugin.Caller, "raid/caller identity change must dispose previous runtime before rebinding");
        var originalGuns = f.Plugin.Guns; target = f.Plugin.Targeting;
        TscHh60Visual.Plugin.Model.Value = HelicopterModel.HH60; f.Plugin.Tick();
        Check(target.Disposed && originalGuns.Disposed && f.Plugin.Targeting == null && f.Plugin.Guns == null, "UH60 to HH60 switch must discard donor guns and wait for visible HH60");
        var hh60 = new GameObject(TscHh60Visual.Plugin.MarkerName); hh60.transform.SetParent(f.Main.transform, false);
        int gunCount = ExtractionGunVisuals.Created; f.Plugin.Tick();
        Check(f.Plugin.Targeting.Marker == TscHh60Visual.Plugin.MarkerName && ExtractionGunVisuals.Created == gunCount, "HH60 must bind existing visible guns without donor duplicates");
        Check(f.Plugin.Presentation.Marker == TscHh60Visual.Plugin.MarkerName, "HH60 effects must bind the existing HH60 guns");
        target = f.Plugin.Targeting;
        TscHh60Visual.Plugin.Model.Value = HelicopterModel.UH60; f.Plugin.Tick();
        Check(target.Disposed && f.Plugin.Guns != null && hh60 != null, "switching back must replace cover runtime without destroying HH60 visuals");
        Check(f.Plugin.Presentation.Marker == ExtractionGunVisuals.MarkerName, "switched UH60 effects must use extraction guns even when HH60 remains cached");
        f.Plugin.Shutdown();
        Check(!f.Plugin.HasRuntime && !f.Plugin.OwnsTrigger && f.Plugin.Point == null, "explicit raid/plugin shutdown must clear window and runtime");
        f.NativeIntact("raid and model cleanup");
    }

    private static void FailureAndRecovery()
    {
        using var f = new Fixture(); f.Ready(); var target = f.Plugin.Targeting; var guns = f.Plugin.Guns;
        Trigger(); EscortBenchTargeting.ThrowOnTick = true; f.Plugin.Tick();
        Check(f.Plugin.Failed && target.Disposed && guns.Disposed && !f.Plugin.HasRuntime && EscortBenchBallistics.TotalRounds == 0, "fault must disable only cover before firing");
        f.NativeIntact("cover fault");
        Check(f.Plugin.Point == f.Point && f.Plugin.OwnsTrigger, "cover fault must retain native pickup window");
        EscortBenchTargeting.ThrowOnTick = false; f.Plugin.Tick(.13f);
        Check(!f.Plugin.HasRuntime && EscortBenchBallistics.TotalRounds == 0, "failed pickup must not retry without an explicit toggle");
        f.Plugin.Enabled = false; f.Plugin.Tick();
        Check(!f.Plugin.Failed && !f.Plugin.OwnsTrigger, "option off should clear fault and relinquish input");
        f.Plugin.Enabled = true; f.Plugin.Tick();
        Check(f.Plugin.HasRuntime && EscortBenchBallistics.TotalRounds == 1, "off/on should allow recovery in the same pickup window");
        target = f.Plugin.Targeting; f.Plugin.Enabled = false; f.Plugin.Tick(.13f);
        Check(target.Disposed && EscortBenchBallistics.TotalRounds == 1 && !f.Plugin.HasRuntime, "option off must stop a currently held trigger");
    }

    private static void PreparationWait()
    {
        using var f = new Fixture(); f.Enable(); f.Arrive(); Trigger(); ExtractionGunVisuals.Ready = false;
        f.Plugin.Tick(); f.Plugin.Tick(.13f);
        Check(f.Plugin.Targeting == null && EscortBenchBallistics.TotalRounds == 0 && !f.Plugin.Failed, "resource loading must wait without firing or poisoning pickup");
        ExtractionGunVisuals.Ready = true; f.Plugin.Tick();
        Check(f.Plugin.Targeting != null && EscortBenchBallistics.TotalRounds == 1, "completed donor assets should allow held-trigger firing");
        var target = f.Plugin.Targeting;
        var nextPoint = new GameObject("new native pickup point"); f.Request(); f.Plugin.Begin(f.Aircraft, nextPoint);
        Check(target.Disposed && !f.Plugin.HasRuntime && f.Plugin.Point == nextPoint, "new arrival must dispose previous pickup runtime before replacement");
    }

    private static void InboundAndArrivalContinuity()
    {
        foreach (var model in new[] { HelicopterModel.UH60, HelicopterModel.HH60 })
        {
            using var f = new Fixture();
            using var request = new CancellationTokenSource();
            f.Enable(); TscHh60Visual.Plugin.Model.Value = model;
            if (model == HelicopterModel.HH60)
            {
                var visible = new GameObject(TscHh60Visual.Plugin.MarkerName);
                visible.transform.SetParent(f.Main.transform, false);
            }
            f.Point.SetActive(false); f.Request(request.Token); Trigger(); f.Plugin.Tick();
            Check(f.Plugin.Point == null && f.Plugin.OwnsTrigger && f.Plugin.HasRuntime && EscortBenchBallistics.TotalRounds == 1,
                model + ": inbound cover must fire without any active extraction/rope point");
            Check(f.Plugin.Targeting.HullMotionSpeed == 80f, model + ": inbound targeting must use the native approach motion bound");
            Check((f.Plugin.Guns == null) == (model == HelicopterModel.HH60), model + ": only UH60 should prepare additional donor gun geometry");
            var targeting = f.Plugin.Targeting;
            var ballistics = f.Plugin.Ballistics;
            var presentation = f.Plugin.Presentation;
            var guns = f.Plugin.Guns;
            Check(EscortBenchAircraft.ActiveLoads == 1, model + ": inbound effects should have exactly one owned loader");
            Hold(KeyCode.RightControl, KeyCode.PageUp, KeyCode.W); f.Plugin.Tick();
            Check(targeting.SelectedGunIndex == 1, model + ": gun selection should work before arrival");
            f.Plugin.End(null);
            Check(f.Plugin.Targeting == targeting && f.Plugin.OwnsTrigger, model + ": native arrival's null-point cleanup must not stop inbound cover");
            Trigger(); f.Point.SetActive(true); f.Plugin.Begin(f.Aircraft, f.Point, request.Token);
            Check(f.Plugin.Targeting == targeting && f.Plugin.Ballistics == ballistics && f.Plugin.Presentation == presentation && f.Plugin.Guns == guns,
                model + ": arrival must preserve guns, targeting, ballistics and effects instances");
            Check(!targeting.Disposed && !ballistics.Disposed && !presentation.Disposed && EscortBenchAircraft.ActiveLoads == 1,
                model + ": arrival must not dispose or duplicate ongoing effects");
            f.Plugin.Tick();
            Check(EscortBenchBallistics.TotalRounds == 1, model + ": arrival must not reset cadence and create an early shot");
            Check(targeting.SelectedGunIndex == 1 && targeting.HullMotionSpeed == 70f, model + ": arrival must retain selected gun and restore hover motion bound");
            f.Plugin.Tick(.13f);
            Check(EscortBenchBallistics.TotalRounds == 2 && presentation.Flashes == 2 && presentation.Sounds == 2,
                model + ": held trigger and effects must continue during hover/climb after their original rate limit");
            f.Plugin.End(f.Point);
            Check(targeting.Disposed && EscortBenchAircraft.ActiveLoads == 0 && !f.Plugin.OwnsTrigger,
                model + ": departure must stop the entire inbound-to-hover cover lifetime");
        }
        foreach (float speed in new[] { .5f, 3f })
        {
            using var f = new Fixture(); f.Enable(); f.Aircraft._timingSnapshot.SpeedMultiplier = speed;
            f.Request(); f.Plugin.Tick();
            Check(f.Plugin.Targeting.HullMotionSpeed == (speed == .5f ? 70f : 240f), "inbound hull motion bound must track configured native animation speed " + speed);
        }
    }

    private static void InboundEligibilityAndCancellation()
    {
        using var f = new Fixture(); f.Enable(); Trigger();
        f.Request(cargo: true); f.Plugin.Tick();
        Check(!f.Plugin.OwnsTrigger && !f.Plugin.HasRuntime && EscortBenchBallistics.TotalRounds == 0, "cargo request must not start inbound manual cover");
        f.Request(local: false); f.Plugin.Tick();
        Check(!f.Plugin.HasRuntime && EscortBenchBallistics.TotalRounds == 0, "nonlocal request must not start inbound manual cover");
        f.Request(visualOnly: true); f.Plugin.Tick();
        Check(!f.Plugin.HasRuntime && EscortBenchBallistics.TotalRounds == 0, "visual-only request must not start inbound manual cover");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        f.Request(cancelled.Token); f.Plugin.Tick();
        Check(!f.Plugin.OwnsTrigger && !f.Plugin.HasRuntime, "already-cancelled native linked token must not open a cover request");
        using var active = new CancellationTokenSource(); f.Request(active.Token); f.Plugin.Tick(); var target = f.Plugin.Targeting;
        Check(f.Plugin.Point == null && EscortBenchBallistics.TotalRounds == 1, "active extraction request must allow inbound fire without creating a landing point");
        active.Cancel();
        Check(!f.Plugin.OwnsTrigger, "inbound token cancellation must immediately invalidate trigger ownership");
        f.Plugin.Tick(.13f);
        Check(target.Disposed && !f.Plugin.HasRuntime && EscortBenchBallistics.TotalRounds == 1, "inbound cancellation must clean up and prevent another shot");
        using var replacement = new CancellationTokenSource(); f.Request(replacement.Token); f.Plugin.Tick(); target = f.Plugin.Targeting;
        var unrelated = new GameObject("another aircraft").AddComponent<NativeAircraft>(); f.Plugin.Cancel(unrelated);
        Check(f.Plugin.Targeting == target && !target.Disposed, "another aircraft's cancellation must not stop this inbound request");
        f.Plugin.Cancel(f.Aircraft);
        Check(target.Disposed && !f.Plugin.HasRuntime && !f.Plugin.OwnsTrigger && !replacement.IsCancellationRequested,
            "native lifetime cancellation prefix must clean cover immediately, before token mutation");
        f.NativeIntact("inbound cancellation");
    }

    private static void PooledRequestIsolation()
    {
        using var f = new Fixture(); f.Enable(); Trigger();
        using var oldRequest = new CancellationTokenSource();
        using var newRequest = new CancellationTokenSource();
        f.Arrive(oldRequest.Token); f.Plugin.Tick(); var oldTarget = f.Plugin.Targeting;
        f.Request(newRequest.Token); f.Plugin.Tick(); var currentTarget = f.Plugin.Targeting;
        Check(oldTarget.Disposed && currentTarget != oldTarget && f.Plugin.Point == null, "pooled ProcessRequest must replace prior cover runtime and clear the old point");
        int rounds = EscortBenchBallistics.TotalRounds;
        oldRequest.Cancel(); f.Plugin.End(f.Point); f.Plugin.End(null);
        f.Plugin.Begin(f.Aircraft, f.Point, oldRequest.Token);
        Check(f.Plugin.Targeting == currentTarget && f.Plugin.Point == null && f.Plugin.OwnsTrigger,
            "stale cancellation, arrival and old/null-point cleanup must not alter a newer inbound request");
        f.Plugin.Tick(.13f);
        Check(EscortBenchBallistics.TotalRounds == rounds + 1 && !currentTarget.Disposed, "new request must keep firing after old token cancellation");
        var currentPoint = new GameObject("current request point"); f.Plugin.Begin(f.Aircraft, currentPoint, newRequest.Token);
        f.Plugin.End(f.Point);
        Check(f.Plugin.Point == currentPoint && f.Plugin.Targeting == currentTarget && !currentTarget.Disposed, "old departure point must not end a newer arrived request");
        f.Plugin.End(currentPoint);
        Check(currentTarget.Disposed && !f.Plugin.HasRuntime, "current departure point must end its matching pooled request");
        f.Request(); f.Plugin.Tick(); currentTarget = f.Plugin.Targeting;
        f.Request(cargo: true); f.Plugin.Tick();
        Check(currentTarget.Disposed && !f.Plugin.OwnsTrigger && !f.Plugin.HasRuntime, "reusing the pool for cargo must remove prior extraction cover without enabling cargo fire");
        f.Plugin.Begin(f.Aircraft, currentPoint);
        Check(!f.Plugin.OwnsTrigger && f.Plugin.Point == null, "an unmatched extraction-point callback must not reopen excluded cargo cover");
    }
}
