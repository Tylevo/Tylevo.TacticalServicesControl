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

    private static void Main()
    {
        var trigger = new EscortHeldTrigger();
        Check(!trigger.Tick(0f, false, true), "idle never fires");
        Check(trigger.Tick(0f, true, true), "initial held input fires immediately");
        Check(!trigger.Tick(0f, true, true), "multiple updates in the same frame cannot fire twice");
        Check(!trigger.Tick(0.119f, true, true), "held input respects minimum interval");
        Check(trigger.Tick(0.12f, true, true), "held input fires at interval boundary");
        Check(!trigger.Tick(0.13f, false, true), "release stops firing");
        Check(!trigger.Tick(0.14f, true, true), "rapid re-press cannot bypass rate limit");
        Check(!trigger.Tick(0.25f, true, false), "permission loss blocks a due round");
        Check(trigger.Tick(0.26f, true, true), "fresh authorization may resume after interval");
        Check(!trigger.Tick(0.27f, true, false), "permission loss stops immediately");
        Check(!trigger.Tick(0.28f, true, true), "re-enabling permission retains cadence");
        Check(trigger.Tick(20f, true, true), "long frame produces one attempt");
        for (int i = 0; i < 8; i++) Check(!trigger.Tick(20f, true, true), "no catch-up backlog");
        Check(!trigger.Tick(21f, false, true), "release also blocks overdue round");
        Check(trigger.Tick(22f, true, true), "new held period starts once after idle");
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -1f })
            Check(!trigger.Tick(invalid, true, true), "invalid clock refuses firing");
        Check(!trigger.Tick(22.01f, true, true), "invalid time did not reset last attempt");
        Check(!trigger.Tick(1f, true, true), "clock rewind refuses frame and rebases interval");
        Check(!trigger.Tick(1.119f, true, true), "rewound clock still rate limited");
        Check(trigger.Tick(1.121f, true, true), "rewound clock resumes normally");
        trigger.Reset();
        Check(trigger.Tick(0f, true, true), "new summon resets clock and cadence");
        trigger.Reset();
        Check(!trigger.Tick(0f, true, false), "permission required on first held frame");
        Check(trigger.Tick(0.01f, true, true), "initial denied frame does not consume cadence");
        Check(!trigger.Tick(0.02f, true, true), "blocked authorization attempt cannot retry every frame");
        Console.WriteLine(_passed + " held-trigger cadence checks passed.");
    }
}
