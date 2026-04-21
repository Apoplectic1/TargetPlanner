using System;
using Astronomy.Core.Horizons;
using Astronomy.Core.Locations;
using Astronomy.Core.Night;
using Astronomy.Core.Targets;

namespace Astronomy.Core.Session
{
    public static class BestSession
    {
        // Find the single D-hour session that maximizes the integrated-quality objective,
        // scanning across all visibility windows within the night. Returns null if no window
        // can accommodate even minDuration.
        //
        // Placement heuristic per window: if the transit occurs inside the window, prefer a
        // transit-centered session; otherwise push the session against the wall of the window
        // closer to transit. Session length is the lesser of maxDuration and the window length.
        // Quality is computed via IntegratedQuality.OverSession using the caller-supplied
        // altitudeQuality function.
        //
        // Currently uses the scalar-horizon VisibilityWindows fast-path; will pick up the
        // azimuth-aware horizon-profile refinement automatically once VisibilityWindows gains
        // it (see the comment on VisibilityWindows.For for the pending refinement).
        public static (DateTime Start, DateTime End, double Quality)? For(
            Target target, Location location, NightWindow night, IHorizonProfile horizon,
            TimeSpan minDuration, TimeSpan maxDuration,
            Func<double, double> altitudeQuality)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (location == null) throw new ArgumentNullException(nameof(location));
            if (horizon == null) throw new ArgumentNullException(nameof(horizon));
            if (altitudeQuality == null) throw new ArgumentNullException(nameof(altitudeQuality));
            if (minDuration <= TimeSpan.Zero)
                throw new ArgumentException("minDuration must be positive", nameof(minDuration));
            if (minDuration > maxDuration)
                throw new ArgumentException("minDuration must be <= maxDuration");

            var windows = VisibilityWindows.For(target, location, night, horizon);
            if (windows.Count == 0) return null;

            double minHrs = minDuration.TotalHours;
            double maxHrs = maxDuration.TotalHours;

            (DateTime Start, DateTime End, double Quality)? best = null;

            foreach (var win in windows)
            {
                double winHrs = (win.End - win.Start).TotalHours;
                if (winHrs < minHrs) continue;

                double sessionHrs = Math.Min(winHrs, maxHrs);
                TimeSpan sessionDuration = TimeSpan.FromHours(sessionHrs);

                DateTime transitUtc = TransitTime.UtcAtOrAfter(target, location, win.Start);
                bool transitInWindow = transitUtc >= win.Start && transitUtc <= win.End;

                DateTime sessionStart;
                if (transitInWindow)
                {
                    // Try transit-centered, clamp to window.
                    sessionStart = transitUtc.AddHours(-sessionHrs / 2.0);
                    if (sessionStart < win.Start) sessionStart = win.Start;
                    if (sessionStart.AddHours(sessionHrs) > win.End)
                        sessionStart = win.End.AddHours(-sessionHrs);
                }
                else
                {
                    // Push against the edge closer to transit (alt is monotone inside the
                    // window when transit is outside, so the extreme end is the low-alt end).
                    sessionStart = transitUtc < win.Start
                        ? win.Start
                        : win.End.AddHours(-sessionHrs);
                }

                DateTime sessionEnd = sessionStart.AddHours(sessionHrs);
                double quality = IntegratedQuality.OverSession(
                    target, location, sessionStart, sessionDuration, altitudeQuality);

                if (best == null || quality > best.Value.Quality)
                    best = (sessionStart, sessionEnd, quality);
            }

            return best;
        }
    }
}
