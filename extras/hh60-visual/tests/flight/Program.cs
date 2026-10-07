using System;
using TscHh60Visual;

internal static class Program
{
    private static int _passed;
    private static readonly FlightVector Current = new FlightVector(5, -2, 9);
    private static readonly float[] Invalid = { float.NaN, float.PositiveInfinity, float.NegativeInfinity };
    private static void Check(string name, bool value)
    {
        if (!value) throw new InvalidOperationException("FAIL " + name);
        ++_passed;
        Console.WriteLine("PASS " + name);
    }
    private static bool Near(double a, double b, double tolerance = 0.00001) => Math.Abs(a - b) <= tolerance;
    private static double Distance(FlightVector a, FlightVector b)
    {
        double x = (double)a.X - b.X, y = (double)a.Y - b.Y, z = (double)a.Z - b.Z;
        return Math.Sqrt(x * x + y * y + z * z);
    }
    private static bool Same(FlightVector a, FlightVector b) => Distance(a, b) == 0;
    private static float Radians(float degrees) => degrees * (float)(Math.PI / 180);
    private static double AngleDelta(float a, float b) => Math.IEEERemainder((double)b - a, Math.PI * 2);

    private static int Main()
    {
        try
        {
            DeltaTime();
            Center();
            Velocity();
            Heading();
            InvalidInputs();
            Sequences();
            Console.WriteLine("RESULT " + _passed + "/" + _passed + " PASS; real managed flight helper only, no Unity physics or EFT integration");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static void DeltaTime()
    {
        Check("ordinary timestep retained", EscortFlightMath.LimitDeltaTime(.016f) == .016f);
        Check("long frame clamped to 100ms", Near(EscortFlightMath.LimitDeltaTime(20), .1));
        Check("paused timestep is zero", EscortFlightMath.LimitDeltaTime(0) == 0);
        Check("negative timestep rejected", EscortFlightMath.LimitDeltaTime(-1) == 0);
        foreach (float invalid in Invalid)
            Check("nonfinite timestep rejected " + invalid, EscortFlightMath.LimitDeltaTime(invalid) == 0);
    }

    private static void Center()
    {
        Check("stationary center stays still", Same(Current, EscortFlightMath.MoveCenter(Current, Current, 15, 8, .1f)));
        Check("center ignores player movement within deadband", Same(Current,
            EscortFlightMath.MoveCenter(Current, new FlightVector(8, 2, 9), 15, 8, .1f)));
        Check("center ignores exact deadband boundary", Same(Current,
            EscortFlightMath.MoveCenter(Current, new FlightVector(20, -2, 9), 15, 8, .1f)));
        Check("center stops at deadband without overshoot", Near(Distance(Current,
            EscortFlightMath.MoveCenter(Current, new FlightVector(20.25f, -2, 9), 15, 8, .1f)), .25));
        var teleported = EscortFlightMath.MoveCenter(Current, new FlightVector(10000, 2000, -5000), 15, 8, 5);
        Check("teleport does not teleport patrol center", Distance(Current, teleported) <= .80002);
        Check("teleport center update approaches caller", Distance(teleported, new FlightVector(10000, 2000, -5000)) < Distance(Current, new FlightVector(10000, 2000, -5000)));
        Check("paused center preserves position", Same(Current, EscortFlightMath.MoveCenter(Current, FlightVector.Zero, 0, 8, 0)));
    }

    private static void Velocity()
    {
        var desired = new FlightVector(30, 40, 0);
        var moved = EscortFlightMath.MoveVelocity(FlightVector.Zero, desired, 8, .1f);
        Check("acceleration magnitude is bounded", Near(Distance(FlightVector.Zero, moved), .8));
        Check("acceleration follows desired vector", Near(moved.X, .48) && Near(moved.Y, .64) && moved.Z == 0);
        var braking = EscortFlightMath.MoveVelocity(new FlightVector(20, 0, 0), new FlightVector(-20, 0, 0), 8, .1f);
        Check("velocity reversal brakes without snapping", Near(braking.X, 19.2));
        var small = new FlightVector(.1f, -.2f, .3f);
        Check("velocity converges without overshooting", Same(small, EscortFlightMath.MoveVelocity(FlightVector.Zero, small, 8, .1f)));
        Check("paused velocity preserves state", Same(Current, EscortFlightMath.MoveVelocity(Current, desired, 8, 0)));
    }

    private static void Heading()
    {
        Check("heading wraps multiple positive turns", Near(EscortFlightMath.WrapRadians((float)(Math.PI * 8 + .25)), .25, .000003));
        Check("heading wraps multiple negative turns", Near(EscortFlightMath.WrapRadians((float)(-Math.PI * 8 - .25)), -.25, .000003));
        float current = Radians(179), target = Radians(-179);
        float turned = EscortFlightMath.TurnHeading(current, target, Radians(5), .1f);
        Check("positive wrap boundary takes short turn", Near(AngleDelta(current, turned), Radians(.5f), .000001));
        current = Radians(-179); target = Radians(179);
        turned = EscortFlightMath.TurnHeading(current, target, Radians(5), .1f);
        Check("negative wrap boundary takes short turn", Near(AngleDelta(current, turned), Radians(-.5f), .000001));
        Check("small remaining heading does not overshoot", Near(EscortFlightMath.TurnHeading(0, .01f, 1, .1f), .01));
        Check("paused heading preserves normalized state", Near(EscortFlightMath.TurnHeading(.7f, 2, 1, 0), .7));
        Check("long frame cannot exceed turn budget", Near(EscortFlightMath.TurnHeading(0, 2, 1, 30), .1));
    }

    private static void InvalidInputs()
    {
        foreach (float invalid in Invalid)
        {
            var bad = new FlightVector(1, invalid, 3);
            Check("nonfinite target preserves center " + invalid, Same(Current, EscortFlightMath.MoveCenter(Current, bad, 15, 8, .1f)));
            Check("invalid current recovers finite state " + invalid, EscortFlightMath.IsFinite(EscortFlightMath.MoveCenter(bad, Current, 0, 8, .1f)));
            Check("nonfinite desired velocity preserves state " + invalid, Same(Current, EscortFlightMath.MoveVelocity(Current, bad, 8, .1f)));
            Check("nonfinite speed cannot move center " + invalid, Same(Current, EscortFlightMath.MoveCenter(Current, FlightVector.Zero, 0, invalid, .1f)));
            Check("nonfinite deadband cannot move center " + invalid, Same(Current, EscortFlightMath.MoveCenter(Current, FlightVector.Zero, invalid, 8, .1f)));
            Check("nonfinite heading target preserves angle " + invalid, Near(EscortFlightMath.TurnHeading(.7f, invalid, 1, .1f), .7));
            Check("nonfinite current heading recovers finite angle " + invalid, EscortFlightMath.IsFinite(EscortFlightMath.TurnHeading(invalid, 2, 1, .1f)));
            Check("nonfinite heading speed cannot turn " + invalid, Near(EscortFlightMath.TurnHeading(.7f, 2, invalid, .1f), .7));
        }
        Check("negative deadband is rejected", Same(Current, EscortFlightMath.MoveCenter(Current, FlightVector.Zero, -1, 8, .1f)));
        Check("zero speed cannot move center", Same(Current, EscortFlightMath.MoveCenter(Current, FlightVector.Zero, 0, 0, .1f)));
        Check("negative acceleration cannot move velocity", Same(Current, EscortFlightMath.MoveVelocity(Current, FlightVector.Zero, -8, .1f)));
        var huge = new FlightVector(float.MaxValue, float.MaxValue, float.MaxValue);
        var opposite = new FlightVector(-float.MaxValue, -float.MaxValue, -float.MaxValue);
        Check("opposite extreme coordinates remain finite", EscortFlightMath.IsFinite(EscortFlightMath.MoveCenter(huge, opposite, 15, 8, .1f)));
        Check("extreme finite speed remains finite", EscortFlightMath.IsFinite(EscortFlightMath.MoveCenter(huge, opposite, 0, float.MaxValue, .1f)));
        Check("extreme finite heading remains finite", EscortFlightMath.IsFinite(EscortFlightMath.TurnHeading(float.MaxValue, -float.MaxValue, 1, .1f)));
    }

    private static void Sequences()
    {
        var random = new Random(41921);
        FlightVector center = FlightVector.Zero, velocity = FlightVector.Zero;
        float heading = 0;
        bool centerBound = true, accelerationBound = true, headingBound = true, finite = true;
        for (int i = 0; i < 5000; i++)
        {
            float dt = (float)random.NextDouble() * .3f;
            var target = new FlightVector(random.Next(-5000, 5001), random.Next(-50, 51), random.Next(-5000, 5001));
            var next = EscortFlightMath.MoveCenter(center, target, 15, 8, dt);
            centerBound &= Distance(center, next) <= 8 * EscortFlightMath.LimitDeltaTime(dt) + .00002;
            center = next;
            var desired = new FlightVector(random.Next(-24, 25), random.Next(-4, 5), random.Next(-24, 25));
            next = EscortFlightMath.MoveVelocity(velocity, desired, 8, dt);
            accelerationBound &= Distance(velocity, next) <= 8 * EscortFlightMath.LimitDeltaTime(dt) + .00002;
            velocity = next;
            float aim = (float)(random.NextDouble() * Math.PI * 20 - Math.PI * 10);
            float nextHeading = EscortFlightMath.TurnHeading(heading, aim, .8f, dt);
            headingBound &= Math.Abs(AngleDelta(heading, nextHeading)) <= .8 * EscortFlightMath.LimitDeltaTime(dt) + .000002;
            heading = nextHeading;
            finite &= EscortFlightMath.IsFinite(center) && EscortFlightMath.IsFinite(velocity) && EscortFlightMath.IsFinite(heading);
        }
        Check("5000 varying target steps obey center continuity bound", centerBound);
        Check("5000 reversals obey acceleration bound", accelerationBound);
        Check("5000 wrapped turns obey shortest-angle rate bound", headingBound);
        Check("5000 mixed updates remain finite", finite);
    }
}
