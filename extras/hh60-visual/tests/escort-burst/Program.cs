using System;
using TscHh60Visual;

internal static class Program
{
    private static int _passed;
    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
        _passed++;
    }

    private static void Accepted(EscortBurstController gun, float now, string target = "hostile-A")
    {
        Check(gun.Tick(now, target, out var expected) && expected == target, "request preserves the settled target");
        gun.RecordResult(now, expected, true);
    }

    private static void SettleCadenceAndCooldown()
    {
        var gun = new EscortBurstController();
        Check(!gun.Tick(0f, "hostile-A", out _), "a newly acquired target cannot fire immediately");
        Check(!gun.Tick(0.59f, "hostile-A", out _), "same target must settle continuously");
        Accepted(gun, 0.6f);
        Check(gun.AcceptedRounds == 1, "accepted round increments exactly once");
        Check(!gun.Tick(0.6f, "hostile-A", out _), "same frame cannot produce a second round");
        Check(!gun.Tick(0.71f, "hostile-A", out _), "second round respects interval");
        Accepted(gun, 0.73f);
        Accepted(gun, 0.86f);
        Check(gun.AcceptedRounds == 3, "one burst contains three accepted rounds");
        Check(!gun.Tick(3.35f, "hostile-A", out _), "cooldown starts after the last accepted round");
        Check(!gun.Tick(3.37f, "hostile-A", out _), "next burst starts a fresh aim settle after cooldown");
        Accepted(gun, 3.98f);
    }

    private static void NoCatchUpOrUnacknowledgedRounds()
    {
        var gun = new EscortBurstController();
        gun.Tick(0f, "hostile-A", out _);
        Check(gun.Tick(20f, "hostile-A", out var target), "a long frame can request one round");
        Check(!gun.Tick(20f, "hostile-A", out _), "unacknowledged request cannot be repeated in the same frame");
        Check(!gun.Tick(21f, "hostile-A", out _), "unacknowledged request cannot produce another round later");
        gun.RecordResult(21f, target, true);
        Check(!gun.Tick(21f, "hostile-A", out _), "overdue cadence does not burst after delayed acknowledgement");
        Check(!gun.Tick(21.11f, "hostile-A", out _), "interval is measured from actual acceptance");
        Accepted(gun, 40f);
        Check(!gun.Tick(40f, "hostile-A", out _), "another long frame still produces only one attempt");
        Check(gun.AcceptedRounds == 2, "missed simulation time never creates extra ammunition");
    }

    private static void TargetChangesAndLoss()
    {
        var gun = new EscortBurstController();
        gun.Tick(0f, "hostile-A", out _);
        Check(!gun.Tick(0.5f, "hostile-B", out _), "switching targets resets settling");
        Check(!gun.Tick(1f, "hostile-B", out _), "new target cannot inherit old target settle time");
        Accepted(gun, 1.11f, "hostile-B");
        Check(!gun.Tick(1.12f, "hostile-C", out _), "switching during a burst stops it immediately");
        Check(!gun.Tick(1.8f, "hostile-C", out _), "retargeting cannot bypass partial-burst cooldown");
        Check(!gun.Tick(3.63f, "hostile-C", out _), "changed target starts its settle after aborted burst cooldown");
        Accepted(gun, 4.24f, "hostile-C");
        Check(!gun.Tick(4.25f, null, out _), "loss of eligibility stops the burst");
        Check(!gun.Tick(7f, "hostile-C", out _), "reacquisition after cooldown still needs a fresh settle");
        Check(!gun.Tick(7.59f, "hostile-C", out _), "lost target's old settle time cannot carry forward");
        Accepted(gun, 7.61f, "hostile-C");
    }

    private static void SuspensionAndFailure()
    {
        foreach (var reason in new[] { "permission off", "pause", "UI open", "transit", "departure", "obstructed flight" })
        {
            var gun = new EscortBurstController();
            gun.Tick(0f, "hostile-A", out _);
            Accepted(gun, 0.6f);
            gun.Suspend(0.61f);
            gun.Suspend(0.61f); // A paused scaled clock is valid.
            Check(!gun.Tick(10f, "hostile-A", out _), reason + " clears settle state despite elapsed cooldown");
            Accepted(gun, 10.61f);
            Check(gun.AcceptedRounds == 2, reason + " preserves session accounting");
        }
        var failed = new EscortBurstController();
        failed.Tick(0f, "hostile-A", out _);
        Check(failed.Tick(0.6f, "hostile-A", out var target), "first round reaches fresh safety check");
        failed.RecordResult(0.6f, target, false);
        Check(failed.AcceptedRounds == 0, "failed safety/emission consumes no round allowance");
        Check(!failed.Tick(0.61f, "hostile-A", out _), "failed burst cannot immediately retry");
        Check(!failed.Tick(3.09f, "hostile-A", out _), "failed attempt imposes full cooldown");
        Check(!failed.Tick(3.11f, "hostile-A", out _), "failed burst gets a fresh aim after cooldown");
        Accepted(failed, 3.72f);
        Check(failed.Tick(3.85f, "hostile-A", out target), "second burst round reaches safety check");
        failed.RecordResult(3.85f, target, false);
        Check(failed.AcceptedRounds == 1, "a failed later burst round does not advance allowance");
        Check(!failed.Tick(4f, "hostile-A", out _), "failure aborts the incomplete burst");
    }

    private static void ExactTargetResultsAndInvalidTime()
    {
        var wrong = new EscortBurstController();
        wrong.Tick(0f, "hostile-A", out _);
        wrong.Tick(0.6f, "hostile-A", out _);
        wrong.RecordResult(0.6f, "hostile-B", false);
        Check(!wrong.Tick(100f, "hostile-A", out _), "mismatched target acknowledgement permanently fails closed");
        Check(wrong.AcceptedRounds == 0, "refused mismatched result does not consume allowance");
        var stale = new EscortBurstController();
        stale.Tick(0f, "hostile-A", out _);
        stale.Tick(0.6f, "hostile-A", out var oldTarget);
        stale.Suspend(0.6f);
        stale.RecordResult(0.6f, oldTarget, false);
        Check(!stale.Tick(50f, "hostile-A", out _), "late result cannot resurrect a suspended pending burst");
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -1f })
        {
            var gun = new EscortBurstController();
            Check(!gun.Tick(invalid, "hostile-A", out _), "invalid time never requests a shot");
            Check(!gun.Tick(10f, "hostile-A", out _), "invalid clock cannot resume a live burst");
        }
        var reversed = new EscortBurstController();
        reversed.Tick(5f, "hostile-A", out _);
        Check(!reversed.Tick(4f, "hostile-A", out _), "backward scaled time resets eligibility without firing");
        Check(!reversed.Tick(6f, "hostile-A", out _), "reversed time requires a new settle instead of catch-up fire");
        Accepted(reversed, 6.61f);
        var empty = new EscortBurstController();
        foreach (var target in new[] { null, "", "  " })
            Check(!empty.Tick(0f, target, out _), "absent target never starts a burst");
        var changedPending = new EscortBurstController();
        changedPending.Tick(0f, "hostile-A", out _);
        changedPending.Tick(0.6f, "hostile-A", out _);
        Check(!changedPending.Tick(0.61f, "hostile-B", out _), "target change cancels an outstanding request");
        Check(!changedPending.Tick(1.3f, "hostile-B", out _), "canceled request enforces cooldown");
    }

    private static void AimGenerationAndClockRebase()
    {
        var gun = new EscortBurstController();
        Check(gun.AimGeneration == 0 && gun.TargetId == null, "idle gun has no invented aim generation or target");
        Check(!gun.Tick(100f, "hostile-A", out _), "first generation cannot fire even at a large initial timestamp");
        Check(gun.AimGeneration == 1 && gun.TargetId == "hostile-A", "aim generation exposes its exact target before settling");
        Check(!gun.Tick(100.3f, "hostile-A", out _) && gun.AimGeneration == 1, "stable settling does not replace the aim offset");
        Accepted(gun, 100.61f);
        Check(gun.AimGeneration == 1, "first fire never introduces a new aim offset");
        Accepted(gun, 100.74f);
        Accepted(gun, 100.87f);
        Check(!gun.Tick(101f, "hostile-A", out _) && gun.AimGeneration == 1, "cooldown does not create the next offset early");
        Check(!gun.Tick(150f, "hostile-A", out _) && gun.AimGeneration == 2,
            "large gap after cooldown creates only a new generation, never its first shot");
        Check(!gun.Tick(150.59f, "hostile-A", out _), "new offset receives a full settle interval");
        Accepted(gun, 150.61f);
        gun.Suspend(150.62f);
        Check(gun.TargetId == null && gun.AimGeneration == 2, "suspension clears target while keeping generation monotonic");
        for (int i = 1; i <= 40; i++) gun.Suspend(150.62f + i * 0.1f);
        Check(!gun.Tick(154.63f, "hostile-A", out _) && gun.AimGeneration == 3,
            "repeated suspended frames do not extend cooldown forever");
        Accepted(gun, 155.24f);

        gun.Tick(1f, "hostile-A", out _);
        Check(gun.AcceptedRounds == 5 && gun.TargetId == null, "clock rewind clears targeting but preserves all accepted rounds");
        Check(!gun.Tick(3.49f, "hostile-A", out _) && gun.AimGeneration == 3,
            "clock rewind preserves partial-burst cooldown duration");
        Check(!gun.Tick(3.51f, "hostile-A", out _) && gun.AimGeneration == 4,
            "rebased cooldown can finish without waiting for the old absolute clock");
        Accepted(gun, 4.12f);

        var unknownAccepted = new EscortBurstController();
        unknownAccepted.Tick(0f, "hostile-A", out _);
        unknownAccepted.Tick(0.6f, "hostile-A", out _);
        unknownAccepted.RecordResult(0.6f, "wrong-target", true);
        Check(unknownAccepted.AcceptedRounds == 1 && !unknownAccepted.Tick(20f, "hostile-A", out _),
            "an accepted mismatched result consumes allowance and disables further requests");
    }

    private static void SessionCapAndIndependentGuns()
    {
        var left = new EscortBurstController();
        var right = new EscortBurstController();
        float now = 0f;
        left.Tick(now, "hostile-A", out _);
        int attempts = 0;
        for (int frame = 1; frame <= 3000; frame++)
        {
            now = frame * 0.1f;
            if (!left.Tick(now, "hostile-A", out var target)) continue;
            attempts++;
            left.RecordResult(now, target, true);
        }
        Check(attempts == EscortBurstController.SessionRoundLimit && left.AcceptedRounds == 60,
            "continuous valid tracking stops at exactly sixty accepted rounds");
        Check(left.LimitReached, "round cap is observable for diagnostics");
        left.Suspend(now);
        Check(!left.Tick(now + 100f, "hostile-B", out _), "target change and suspension never reset session cap");
        Check(right.AcceptedRounds == 0 && !right.LimitReached, "each gun has an independent session allowance");
        right.Tick(0f, "hostile-B", out _);
        Accepted(right, 0.6f, "hostile-B");
        Check(right.AcceptedRounds == 1 && left.AcceptedRounds == 60, "one exhausted gun does not prevent the other gun from firing");
    }

    private static void Main()
    {
        SettleCadenceAndCooldown();
        NoCatchUpOrUnacknowledgedRounds();
        TargetChangesAndLoss();
        SuspensionAndFailure();
        ExactTargetResultsAndInvalidTime();
        AimGenerationAndClockRebase();
        SessionCapAndIndependentGuns();
        Console.WriteLine(_passed + " automatic burst scheduling checks passed.");
    }
}
