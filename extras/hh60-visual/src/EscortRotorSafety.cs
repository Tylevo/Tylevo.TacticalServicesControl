using System;

namespace TscHh60Visual
{
    internal static class EscortRotorSafety
    {
        // Exact exported stock COLLIDER + BALLISTICS_METAL vertices, swept over
        // every rotor angle, in the upright outer aircraft root's coordinates.
        // Boxes conservatively enclose the circular main/tail swept volumes.
        internal static bool Intersects(FlightVector start, FlightVector end, float padding)
        {
            if (!EscortFlightMath.IsFinite(start) || !EscortFlightMath.IsFinite(end) ||
                !EscortFlightMath.IsFinite(padding) || padding < 0f) return true;
            return Box(start, end, new FlightVector(-0.003312f, 2.983768f, 2.010406f),
                       new FlightVector(7.735605f, 0.511568f, 7.735605f), padding) ||
                   Box(start, end, new FlightVector(0.406924f, 3.044187f, -8.574857f),
                       new FlightVector(0.378095f, 1.719079f, 1.719079f), padding);
        }

        private static bool Box(FlightVector start, FlightVector end, FlightVector center, FlightVector extents, float padding)
        {
            double first = 0, last = 1;
            return Slab(start.X, end.X, center.X, extents.X, padding, ref first, ref last) &&
                   Slab(start.Y, end.Y, center.Y, extents.Y, padding, ref first, ref last) &&
                   Slab(start.Z, end.Z, center.Z, extents.Z, padding, ref first, ref last);
        }

        private static bool Slab(double start, double end, double center, double extent, double padding, ref double first, ref double last)
        {
            double minimum = center - extent - padding, maximum = center + extent + padding;
            double delta = end - start;
            if (Math.Abs(delta) < 1e-12) return start >= minimum && start <= maximum;
            double a = (minimum - start) / delta, b = (maximum - start) / delta;
            if (a > b) { double swap = a; a = b; b = swap; }
            first = Math.Max(first, a);
            last = Math.Min(last, b);
            return first <= last;
        }
    }
}
