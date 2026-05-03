using System;
using System.Collections.Generic;

namespace TargetPlanner.Charts
{
    /// <summary>
    /// Vertical-distance hit-tester between a cursor and a polyline of
    /// (X, Y?) data points. Generic over the point type so the algorithm
    /// is decoupled from any specific chart library's value type — caller
    /// supplies <paramref name="getX"/> / <paramref name="getY"/> selectors.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Bracket-hit semantics:</b> <c>Distance</c> returns 0 whenever
    /// <paramref name="cy"/> is bracketed by the segment's two endpoint
    /// Ys. Intentional for steep / step segments (e.g. the HD overlay's
    /// vertical window edges) where the cursor is "on the line" anywhere
    /// within the segment's Y range.
    /// </para>
    ///
    /// <para>
    /// <b>Sentinel filter:</b> if either endpoint's Y equals
    /// <paramref name="sentinelY"/>, the segment is treated as invalid and
    /// the method returns null. TP's Sessions chart uses <c>-90</c> as a
    /// "no fit tonight" sentinel; passing <c>sentinelY: -90</c> filters
    /// those out. Pass <see cref="double.NaN"/> (the default) to disable.
    /// </para>
    ///
    /// <para>
    /// <b>Segment index:</b> the returned <c>SegmentStart</c> is the index
    /// of the segment's left endpoint (i.e. the cursor is between
    /// <c>data[SegmentStart]</c> and <c>data[SegmentStart + 1]</c>).
    /// </para>
    /// </remarks>
    public static class CurveHitTester
    {
        public static (double InterpY, double Distance, int SegmentStart)? At<T>(
            IList<T> data,
            Func<T, double?> getX,
            Func<T, double?> getY,
            double cx, double cy,
            double sentinelY = double.NaN)
        {
            if (data.Count < 2) return null;
            if (!(getX(data[0]) is double firstX)
                || !(getX(data[data.Count - 1]) is double lastX)) return null;
            if (cx < firstX || cx > lastX) return null;
            for (int i = 0; i < data.Count - 1; i++)
            {
                if (!(getX(data[i]) is double xi)
                    || !(getX(data[i + 1]) is double xj)) continue;
                if (cx < xi || cx > xj) continue;
                if (!(getY(data[i]) is double yi)
                    || !(getY(data[i + 1]) is double yj)) return null;
                if (yi == sentinelY || yj == sentinelY) return null;
                var t = (xj - xi) > 0 ? (cx - xi) / (xj - xi) : 0;
                var interpY = yi + t * (yj - yi);
                var bracketHit = cy >= Math.Min(yi, yj) && cy <= Math.Max(yi, yj);
                var distance = bracketHit ? 0 : Math.Abs(interpY - cy);
                return (interpY, distance, i);
            }
            return null;
        }
    }
}
