using System;
using TscHh60Visual;

internal static class Program
{
    private static int _count;
    private static FlightVector V(float x, float y = 0, float z = 0) => new FlightVector(x, y, z);
    private static void Check(string name, bool result)
    {
        if (!result) throw new InvalidOperationException("FAIL " + name);
        _count++;
        Console.WriteLine("PASS " + name);
    }
    private static EscortActorMotion Stable(float speed = 8)
    {
        var sample = new EscortActorMotion();
        for (int i = 0; i <= 30; i++) sample.Observe(V(speed * i / 60f), i / 60f);
        Check("60fps stable actor becomes ready at speed " + speed, sample.Ready(.5f));
        Check("estimated speed remains bounded " + speed, Math.Abs(sample.Velocity.X - speed) < .001f);
        return sample;
    }
    private static int Main()
    {
        try
        {
            Stable(0); Stable(8); Stable(14);
            var sample = Stable();
            sample.Observe(V(100), .51f);
            Check("teleport inside25ms immediately resets readiness", !sample.Ready(.51f));
            sample = Stable(); sample.Observe(V(5), .5f);
            Check("same-time position discontinuity resets readiness", !sample.Ready(.5f));
            sample = Stable(); sample.Observe(V(4), 1f);
            Check("stale observation resets readiness", !sample.Ready(1f));
            sample = Stable(); sample.Observe(V(4), .4f);
            Check("clock rewind resets readiness", !sample.Ready(.4f));
            sample = Stable(); sample.Observe(V(float.NaN), .52f);
            Check("nonfinite actor fails closed", !sample.Ready(.52f));
            sample = Stable(); sample.Observe(V(4.8f), .51f, true);
            Check("forced discontinuity resets", !sample.Ready(.51f));
            sample = Stable(); sample.Observe(V(4.08f), .51f, true);
            Check("forced fresh normal velocity remains valid", sample.Ready(.51f) && Math.Abs(sample.Velocity.X - 8) < .001);
            sample = new EscortActorMotion();
            int ready = 0;
            for (int frame = 0; frame <= 180; frame++)
            {
                float t = frame / 60f;
                float physicsTime = (float)(Math.Floor((double)frame / 60 * 50 + 1e-6) / 50);
                sample.Observe(V(8 * physicsTime), t);
                if (frame > 30 && sample.Ready(t)) ready++;
            }
            Check("60fps observes50Hz actor without perpetual motion resets", ready >= 130);
            for (int phase = 0; phase < 12; phase++)
            {
                sample = new EscortActorMotion();
                ready = 0;
                for (int frame = 0; frame <= 180; frame++)
                {
                    float t = frame / 60f;
                    float physicsTime = (float)(Math.Floor((double)frame / 60 * 50 + 1e-6) / 50);
                    sample.Observe(V(14 * physicsTime), t);
                    if (frame % 12 == phase) sample.Observe(V(14 * physicsTime), t, true);
                    if (frame > 30 && sample.Ready(t)) ready++;
                }
                Check("50Hz14m/s remains ready at manual60fps phase " + phase, ready >= 130 && sample.Velocity.X <= 15);
            }
            sample = new EscortActorMotion();
            ready = 0;
            for (int frame = 0; frame <= 180; frame++)
            {
                float t = frame / 60f;
                sample.Observe(V(16 * t), t);
                if (sample.Ready(t)) ready++;
            }
            Check("sustained speed above15m/s never authorizes", ready == 0);
            Check("point-to-segment distance clamps to endpoints", Math.Abs(EscortActorMotion.DistanceToSegment(V(3, 4), V(0), V(2)) - Math.Sqrt(17)) < 1e-10);
            Check("point-to-segment distance handles zero length", EscortActorMotion.DistanceToSegment(V(3,4), V(0), V(0)) == 5);
            Check("invalid separation fails closed asNaN", double.IsNaN(EscortActorMotion.DistanceToSegment(V(float.NaN), V(0), V(1))));
            Check("main rotor disc crossing is rejected", EscortRotorSafety.Intersects(V(0, 0, 2), V(0, 5, 2), 0));
            Check("tail rotor disc crossing is rejected", EscortRotorSafety.Intersects(V(-2, 3, -8.5f), V(2, 3, -8.5f), 0));
            Check("main blade phase gap still protected", EscortRotorSafety.Intersects(V(6, 0, 6), V(6, 5, 6), 0));
            Check("downward left muzzle path clears rotors", !EscortRotorSafety.Intersects(V(-2.875453f,.773940f,3.768389f), V(-10,-2,4), .81f));
            Check("downward right muzzle path clears rotors", !EscortRotorSafety.Intersects(V(2.884573f,.710185f,3.685260f), V(10,-2,4), .81f));
            Check("inflated rotor contact is rejected", EscortRotorSafety.Intersects(V(0,2,0), V(1,2,0), .5f));
            Check("invalid rotor path is rejected", EscortRotorSafety.Intersects(V(float.NaN), V(0), 0));
            Check("negative rotor margin is rejected", EscortRotorSafety.Intersects(V(0), V(0), -1));
            Console.WriteLine("RESULT " + _count + "/" + _count + " passed");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
