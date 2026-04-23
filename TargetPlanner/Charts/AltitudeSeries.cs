using Astronomy.Core;
using Astronomy.Core.Night;
using Astronomy.Core.Time;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms.DataVisualization.Charting;

using Location = Astronomy.Core.Locations.Location;
using Target   = Astronomy.Core.Targets.Target;

namespace TargetPlanner.Charts
{
    public class AltitudeSeries
    {
        // Per-day intermediates that do not depend on Horizon or Duration. Populated once by
        // BuildYearSeries and consumed on every Horizon/Duration spinner tick by
        // BuildOptimalSeries, so the rebuild path never re-enters ComputeNight (CoordinateSharp,
        // the actual hot path) or GetAltitudeAzimuth.
        private struct NightCacheEntry
        {
            public DateTime Dusk;
            public DateTime Dawn;
            public double   AltDusk;         // target altitude at Dusk, degrees
            public double   AltDawn;         // target altitude at Dawn, degrees
            public double   LstDusk;         // Local Sidereal Time at Dusk, hours
            public double   LstDawn;         // LST at Dawn, hours; linearized so LstDawn > LstDusk
            public bool     TransitInNight;  // does RA_hours (mod 24) fall in [LstDusk, LstDawn]?
            public double   YearAlt;         // max night altitude -- the Year series value
            public bool     IsPolar;         // night.Dusk/Dawn were DateTime.MinValue
            public DateTime SentinelX;       // X coordinate for Year and Optimal points on this day
        }

        // Init-only snapshot: Location and Target are captured at construction and cannot be
        // swapped afterward. Spinner edits on the main form update mLocation there; the
        // chart's copy stays frozen until the next Graph-click tear-and-rebuild. The one
        // exception is Horizon / Duration, which are scrub-able post-hoc via
        // RebuildOptimalSeries(horizon, duration) -- those two analysis parameters are
        // passed explicitly per call rather than being read from this snapshot.
        public Location Location { get; }
        public Target Target { get; }
        public List<Series> TargetSeriesList { get; private set; }
        private List<NightCacheEntry> mYearCache;

        // Explicit per-target color, assigned once by AltitudeChart at reload from a stable
        // palette. Every Series this instance creates (Day / Year / Optimal / OptimalFloor /
        // OptimalFloorCentered) uses this color. Moon is target-independent and keeps its
        // alpha-scaled-gray color set directly in BuildMoonSeries.
        //
        // Why explicit: with Color.Empty the DataVisualization Chart auto-assigns palette
        // colors by Series index at render time. Toggling one series to Color.Transparent
        // shifts the remaining Empty series' palette slots, which visibly reshuffles colors
        // on a legend click. Setting a concrete Color here opts out of the auto-palette.
        private readonly Color mSeriesColor;

        public AltitudeSeries(Location location, Target target, Color seriesColor)
        {
            if (location == null) throw new ArgumentNullException(nameof(location));
            if (target == null)   throw new ArgumentNullException(nameof(target));
            Location = location;
            Target = target;
            mSeriesColor = seriesColor;
            TargetSeriesList = new List<Series>();
        }

        // Day-chart X bounds. Single source of truth for BuildDaySeries / BuildMoonSeries
        // (this class) and AltitudeChart.AddDawnDuskGradient. Both surfaces must agree --
        // the gradient strip positions are computed in minutes off the same `start` the
        // data series uses for index 0.
        //
        // Start = the integer hour mark strictly before duskLocal.
        // Stop  = the integer hour mark strictly past dawnLocal.
        // If dusk/dawn lands exactly on an hour the bound steps one full hour outward so
        // dusk and dawn never coincide with an edge label.
        public static DateTime DayChartStart(DateTime duskLocal)
        {
            DateTime start = duskLocal.Date.AddHours(duskLocal.Hour);
            if (start >= duskLocal) start = start.AddHours(-1);
            return start;
        }

        public static DateTime DayChartStop(DateTime dawnLocal)
        {
            DateTime stop = dawnLocal.Date.AddHours(dawnLocal.Hour);
            if (stop <= dawnLocal) stop = stop.AddHours(1);
            return stop;
        }

        public void ClearTargetList()
        {
            TargetSeriesList.Clear();
        }

        private static Series MakeSeries(string name, string seriesType, Color color) => new Series
        {
            Name = name + "-" + seriesType,
            Color = color,
            IsXValueIndexed = true,
            XValueType = ChartValueType.DateTime,
            ChartType = SeriesChartType.Line,
        };

        // Return the existing Series by name if present so repeat calls reuse the same Series
        // object (preserves chart binding); otherwise create and register a fresh one. This is
        // what makes BuildYearAndOptimalSeries safe to call more than once per AltitudeSeries
        // instance -- the Series identity survives rebuilds so mChart.Series keeps referencing
        // the points that just got repopulated.
        private Series FindOrCreateSeries(string name, string seriesType, Color color)
        {
            string fullName = name + "-" + seriesType;
            foreach (Series existing in TargetSeriesList)
            {
                if (existing.Name == fullName) return existing;
            }
            Series fresh = MakeSeries(name, seriesType, color);
            TargetSeriesList.Add(fresh);
            return fresh;
        }

        // phaseProgress (if non-null) is reported exactly three times per successful build:
        //   "Day"     -- after the synchronous minute-loop Day and Moon series are populated.
        //   "Year"    -- after the Task.Run background compute + UI-thread RenderYearSeries.
        //   "Optimal" -- after RenderOptimalSeries lands the three Optimal-area curves.
        // Progress reports are marshalled back to the IProgress<string>'s creation context,
        // so the subscriber (e.g. a ProgressBar Value setter) runs on the UI thread.
        // On exception, a phase may not tick -- subscribers that track a tick count should
        // not rely on exact counts to infer success.
        public async Task BuildSeriesList(IProgress<string> phaseProgress = null)
        {
            // Each Target owns its AltitudeSeries, so a second build on the same Target (user
            // re-clicks Graph Target, or opens the multi-target popup after the main chart)
            // must start from a clean TargetSeriesList -- otherwise BuildMoonSeries and
            // BuildDaySeries, which unconditionally create fresh Series objects, would leave
            // duplicates next to the prior run. Year and Optimal are idempotent on their own via
            // FindOrCreateSeries, but clearing here keeps all four series on the same lifecycle.
            TargetSeriesList.Clear();

            BuildMoonSeries();
            BuildDaySeries();
            phaseProgress?.Report("Day");

            // Pre-allocate every Series up front on the UI thread so the background compute
            // phase never touches TargetSeriesList (which AltitudeChart.ShowChartAreaSeries /
            // UpdateNowLine iterate concurrently on the UI thread).
            FindOrCreateSeries(Target.Name, "Year",                 mSeriesColor);
            FindOrCreateSeries(Target.Name, "Optimal",              mSeriesColor);
            FindOrCreateSeries(Target.Name, "OptimalFloor",         mSeriesColor);
            FindOrCreateSeries(Target.Name, "OptimalFloorCentered", mSeriesColor);

            // Compute the 365-day cache on a background thread (the CoordinateSharp-heavy
            // part). The continuation resumes on the UI thread via the captured
            // SynchronizationContext, so every Series.Points mutation below is safely on the
            // UI thread. Mutating Series.Points from a background thread triggers
            // Chart.Invalidate() cross-thread, which Windows Forms either throws on or silently
            // corrupts into misplaced data points -- the source of the "spikes" on the chart.
            //
            // Returning Task (not void) lets exceptions propagate to callers that await. Most
            // current call sites are fire-and-forget (discard the Task), so the try/catch here
            // ensures a failed compute at least lands in the debugger output instead of
            // vanishing to SynchronizationContext.
            try
            {
                List<NightCacheEntry> cache = await Task.Run(() => ComputeYearCache());

                mYearCache = cache;
                RenderYearSeries();
                phaseProgress?.Report("Year");
                // Initial render uses the snapshot's Horizon and Duration; Horizon / Duration
                // spinner scrubs later call RebuildOptimalSeries(horizon, duration) with fresh
                // values without touching the snapshot.
                RenderOptimalSeries(Location.Horizon, Location.Duration);
                phaseProgress?.Report("Optimal");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"AltitudeSeries.BuildSeriesList failed for target '{Target?.Name}': {ex}");
            }
        }

        // Rebuild only the Optimal series on Horizon or Duration change. Day, Moon, and Year
        // don't depend on either input, so they stay as-is. Reads mYearCache (populated during
        // the initial build) instead of recomputing dusk/dawn/LSTs etc. Cold-start path: if
        // the cache hasn't been populated yet -- e.g., the user scrubbed a spinner before the
        // initial Task.Run completed -- build Year synchronously first on the UI thread.
        //
        // Horizon and Duration are passed explicitly rather than read from the snapshot so
        // scrubbing a spinner updates the rendered curve without mutating the snapshot.
        public void RebuildOptimalSeries(double horizon, TimeSpan duration)
        {
            if (mYearCache == null || mYearCache.Count == 0)
            {
                mYearCache = ComputeYearCache();
                RenderYearSeries();
            }
            RenderOptimalSeries(horizon, duration);
        }

        private void BuildDaySeries()
        {
            NightWindow night = NightCalculator.ComputeNight(Location);
            // NightWindow fields are UTC as of the Core DST fix; convert to local once here
            // because the minute-loop rounds to wall-clock hour boundaries for the X axis.
            DateTime duskLocal = night.AstronomicalDusk.ToLocalTime();
            DateTime dawnLocal = night.AstronomicalDawn.ToLocalTime();

            Series daySeries = MakeSeries(Target.Name, "Day", mSeriesColor);

            DateTime start = DayChartStart(duskLocal);
            DateTime stop  = DayChartStop(dawnLocal);
            TimeSpan delta = stop.Subtract(start);

            // Inclusive endpoint: emit the point AT minute = delta.TotalMinutes too, so the
            // X axis ranges over [0, delta] indices. The chart's IsXValueIndexed=true mode
            // anchors tick labels to data points by index; without the inclusive endpoint
            // the rightmost hourly tick (e.g. "5:00 AM") has no data point and its label
            // doesn't render.
            int totalMinutes = Convert.ToInt32(Math.Round(delta.TotalMinutes, 0));
            for (int minutes = 0; minutes <= totalMinutes; minutes++)
            {
                DateTime point = start.AddMinutes(minutes);
                // Location is immutable; ask AltAzCalculator to evaluate at `point` via a
                // With-variant instead of mutating a clone in place.
                AltAz targetPosition = AltAzCalculator.Of(Target, Location.With(dateTime: point));
                daySeries.Points.AddXY(point, targetPosition.Altitude);
            }

            TargetSeriesList.Add(daySeries);
        }

        // Walk 365 days, call ComputeNight and two AltAz.Of calls per day, return the
        // Horizon/Duration-independent intermediates. Year values are invariant to Horizon and
        // Duration so this never needs to run on a spinner tick.
        //
        // Pure compute: no WinForms Series.Points access, no mYearCache assignment. Safe to
        // run on a background thread via Task.Run. The caller assigns the returned list to
        // mYearCache on the UI thread before rendering.
        private List<NightCacheEntry> ComputeYearCache()
        {
            double latSigned  = Location.North ?  Location.Latitude  : -Location.Latitude;
            double decSigned  = Target.North   ?  Target.Declination : -Target.Declination;
            double lonDegEast = Location.West  ? -Location.Longitude :  Location.Longitude;
            double raHours    = Target.RightAscension;

            double meridianAlt = TargetGeometry.MeridianAltitude(latSigned, decSigned);

            // Seed the 365-day scan from the observer's picked moment, not real-world "now".
            // Location is the immutable snapshot captured at AltitudeSeries construction, so
            // each Graph click produces a fresh instance keyed to the DatePicker's value at
            // that moment. Previously this read DateTime.Now unconditionally, so picking a
            // future / past date updated the Year / Optimal title but the data stayed
            // anchored to the current real-world month.
            DateTime seed     = Location.DateTime;
            DateTime startDay = seed.AddDays(-seed.Day);
            DateTime endDay   = startDay.AddYears(1);
            int      totalDays = (int)endDay.Subtract(startDay).TotalDays;

            List<NightCacheEntry> cache = new List<NightCacheEntry>(totalDays);

            for (int day = 0; day < totalDays; day++)
            {
                // Location is immutable; each per-day DateTime becomes a new With-variant.
                Location dayLoc = Location.With(dateTime: startDay.AddDays(day));
                NightWindow night = NightCalculator.ComputeNight(dayLoc);

                NightCacheEntry entry = new NightCacheEntry();

                if (!night.IsValid)
                {
                    entry.IsPolar   = true;
                    entry.SentinelX = startDay.AddDays(day).AddHours(12);
                    entry.YearAlt   = -90.0;
                    cache.Add(entry);
                    continue;
                }

                // night.AstronomicalDusk / AstronomicalDawn are Kind=Utc (see NightCalculator).
                // Cached as-is; downstream math (AltAz.Of, SiderealTime.Local) wants UTC anyway.
                entry.Dusk      = night.AstronomicalDusk;
                entry.Dawn      = night.AstronomicalDawn;
                // SentinelX is the X-axis coordinate for Year/Optimal points. Kept in UTC
                // here; the chart's X axis is DateTime-valued and Windows renders Kind=Utc
                // values against the local time zone, so the visual is wall-clock local.
                entry.SentinelX = entry.Dawn.AddMinutes(-1);

                // AltAz.Of internally calls location.DateTime.ToUniversalTime(), which is a
                // no-op on Kind=Utc -- so entry.Dusk / Dawn feed through unchanged to the
                // altitude math.
                entry.AltDusk = AltAzCalculator.Of(Target, Location.With(dateTime: entry.Dusk)).Altitude;
                entry.AltDawn = AltAzCalculator.Of(Target, Location.With(dateTime: entry.Dawn)).Altitude;

                // .ToUniversalTime() on Kind=Utc is a no-op; left in place to advertise that
                // SiderealTime.Local wants a UTC instant, so future refactors don't quietly
                // swap in a Kind=Local value and shift LST by the local offset.
                entry.LstDusk = SiderealTime.Local(entry.Dusk.ToUniversalTime(), lonDegEast);
                entry.LstDawn = SiderealTime.Local(entry.Dawn.ToUniversalTime(), lonDegEast);
                if (entry.LstDawn < entry.LstDusk) entry.LstDawn += 24.0;

                entry.TransitInNight = false;
                for (int k = -1; k <= 1; k++)
                {
                    double t = raHours + 24.0 * k;
                    if (t >= entry.LstDusk && t <= entry.LstDawn)
                    {
                        entry.TransitInNight = true;
                        break;
                    }
                }

                double yearAlt = Math.Max(entry.AltDusk, entry.AltDawn);
                if (entry.TransitInNight && meridianAlt > yearAlt) yearAlt = meridianAlt;
                entry.YearAlt = yearAlt;

                cache.Add(entry);
            }

            return cache;
        }

        // UI-thread-only: push the cached Year altitudes into the chart's Year series. Keep
        // this strictly separate from ComputeYearCache -- every Points.Clear / AddXY call
        // eventually triggers Chart.Invalidate(), which is illegal off the UI thread.
        //
        // Null guard: mYearCache is null until BuildSeriesList's Task.Run completes (or forever
        // if the compute threw). A UI-thread caller (e.g. RebuildOptimalSeries via a spinner
        // scrub) that races the initial build could hit this path before the cache exists.
        private void RenderYearSeries()
        {
            if (mYearCache == null) return;

            Series yearSeries = FindOrCreateSeries(Target.Name, "Year", mSeriesColor);
            yearSeries.Points.Clear();
            foreach (NightCacheEntry entry in mYearCache)
            {
                yearSeries.Points.AddXY(entry.SentinelX, entry.YearAlt);
            }
        }

        // Walk mYearCache to emit the three Optimal-area curves:
        //   Optimal               -- peak altitude reached inside any above-horizon window of
        //                            length >= Duration on that night.
        //   OptimalFloor          -- lowest altitude experienced during the best D-hour session
        //                            that fits inside such a window; the session is transit-
        //                            centered when possible, otherwise pushed against the window
        //                            wall closer to transit.
        //   OptimalFloorCentered  -- floor of a strictly transit-centered D-hour session. Emits
        //                            xIdealDeg iff the centered session [T - dL/2, T + dL/2]
        //                            fits inside [LstDusk, LstDawn] for some shifted transit
        //                            T = RA + 24k, and xIdealDeg >= Horizon; else -90. Useful
        //                            when you specifically want a session symmetric about the
        //                            meridian (e.g., to balance hour angle / field orientation).
        //
        // All three read cached dusk/dawn altitudes, LSTs, and TransitInNight from mYearCache;
        // no ComputeNight, no GetAltitudeAzimuth. The Year-as-upper-bound pre-filter short-
        // circuits every day where YearAlt is below the current horizon.
        //
        // Floor placement (OptimalFloor): for each qualifying above-horizon window [s, e] with
        // shifted transit T = RA + 24k, the best D-hour session is transit-centered if
        // [T - dL/2, T + dL/2] fits; otherwise it's pushed against the wall of [s, e] closer to
        // T. The floor altitude is then AltAtHa evaluated at the session endpoint farther from
        // transit -- which is always the session's low point since alt(HA) is monotone away
        // from HA=0.
        // UI-thread-only: walks mYearCache and writes the three Optimal-area series. All math
        // is local arithmetic -- no CoordinateSharp, no ComputeNight -- so this is fast enough
        // to call synchronously from spinner handlers.
        //
        // Null guard: see RenderYearSeries. RebuildOptimalSeries should have populated the
        // cache before calling this, but the defensive check here lets a spinner scrub during
        // the initial async build no-op cleanly instead of throwing.
        //
        // Horizon and duration are passed as parameters (not read from Location) so scrubbing
        // the Horizon / Duration spinners updates the rendered curve without requiring us to
        // mutate the chart's frozen Location snapshot.
        private void RenderOptimalSeries(double horizon, TimeSpan duration)
        {
            if (mYearCache == null) return;

            Series optimalSeries         = FindOrCreateSeries(Target.Name, "Optimal",              mSeriesColor);
            Series optimalFloorSeries    = FindOrCreateSeries(Target.Name, "OptimalFloor",         mSeriesColor);
            Series optimalCenteredSeries = FindOrCreateSeries(Target.Name, "OptimalFloorCentered", mSeriesColor);
            optimalSeries.Points.Clear();
            optimalFloorSeries.Points.Clear();
            optimalCenteredSeries.Points.Clear();

            double latSigned   = Location.North ?  Location.Latitude  : -Location.Latitude;
            double decSigned   = Target.North   ?  Target.Declination : -Target.Declination;
            double raHours     = Target.RightAscension;
            double horizonDeg  = horizon;
            double durationHrs = duration.TotalHours;

            double meridianAlt = TargetGeometry.MeridianAltitude(latSigned, decSigned);
            double haHorizon   = TargetGeometry.HourAngleAtAltitude(latSigned, decSigned, horizonDeg);

            const double SiderealHoursPerSolarDay = 24.06570982441908;
            double durationLst = durationHrs * SiderealHoursPerSolarDay / 24.0;
            double halfDurationLst = durationLst / 2.0;
            double xIdealDeg = TargetGeometry.AltitudeAtHourAngle(halfDurationLst, latSigned, decSigned);

            foreach (NightCacheEntry entry in mYearCache)
            {
                if (entry.IsPolar || entry.YearAlt < horizonDeg)
                {
                    optimalSeries.Points.AddXY(entry.SentinelX, -90.0);
                    optimalFloorSeries.Points.AddXY(entry.SentinelX, -90.0);
                    optimalCenteredSeries.Points.AddXY(entry.SentinelX, -90.0);
                    continue;
                }

                double optimalAlt  = -90.0;
                double floorAlt    = -90.0;
                double centeredAlt = -90.0;

                // Strict transit-centered floor: does a symmetric D-hour session around some
                // shifted transit fit inside the night window, AND does xIdealDeg clear Horizon?
                if (xIdealDeg >= horizonDeg)
                {
                    for (int k = -1; k <= 1; k++)
                    {
                        double t = raHours + 24.0 * k;
                        if (t - halfDurationLst >= entry.LstDusk && t + halfDurationLst <= entry.LstDawn)
                        {
                            centeredAlt = xIdealDeg;
                            break;
                        }
                    }
                }

                if (double.IsPositiveInfinity(haHorizon))
                {
                    double lengthSolar = (entry.LstDawn - entry.LstDusk) * 24.0 / SiderealHoursPerSolarDay;
                    if (lengthSolar >= durationHrs)
                    {
                        optimalAlt = Math.Max(entry.AltDusk, entry.AltDawn);
                        if (entry.TransitInNight && meridianAlt > optimalAlt) optimalAlt = meridianAlt;

                        // Shifted transit closest to the night (only one of k=-1,0,1 can be).
                        double nightMid = 0.5 * (entry.LstDusk + entry.LstDawn);
                        double t = raHours;
                        double bestDist = Math.Abs(t - nightMid);
                        for (int k = -1; k <= 1; k += 2)
                        {
                            double cand = raHours + 24.0 * k;
                            double dist = Math.Abs(cand - nightMid);
                            if (dist < bestDist) { bestDist = dist; t = cand; }
                        }

                        if (t - halfDurationLst >= entry.LstDusk && t + halfDurationLst <= entry.LstDawn)
                        {
                            floorAlt = xIdealDeg;
                        }
                        else if (t < entry.LstDusk + halfDurationLst)
                        {
                            floorAlt = TargetGeometry.AltitudeAtHourAngle(entry.LstDusk + durationLst - t, latSigned, decSigned);
                        }
                        else
                        {
                            floorAlt = TargetGeometry.AltitudeAtHourAngle(entry.LstDawn - durationLst - t, latSigned, decSigned);
                        }
                    }
                }
                else
                {
                    for (int k = -1; k <= 1; k++)
                    {
                        double center  = raHours + 24.0 * k;
                        double ahStart = center - haHorizon;
                        double ahEnd   = center + haHorizon;
                        double s = Math.Max(entry.LstDusk, ahStart);
                        double e = Math.Min(entry.LstDawn, ahEnd);
                        if (s >= e) continue;

                        double lengthSolar = (e - s) * 24.0 / SiderealHoursPerSolarDay;
                        if (lengthSolar < durationHrs) continue;

                        // Peak altitude in this window.
                        double altAtStart = (s == entry.LstDusk) ? entry.AltDusk : horizonDeg;
                        double altAtEnd   = (e == entry.LstDawn) ? entry.AltDawn : horizonDeg;
                        bool   transitInWindow = (center >= s && center <= e);
                        double windowMax = transitInWindow
                            ? meridianAlt
                            : Math.Max(altAtStart, altAtEnd);
                        if (windowMax > optimalAlt) optimalAlt = windowMax;

                        // Floor altitude: best D-hour session placement within [s, e].
                        double windowFloor;
                        if (center - halfDurationLst >= s && center + halfDurationLst <= e)
                        {
                            windowFloor = xIdealDeg;
                        }
                        else if (center < s + halfDurationLst)
                        {
                            windowFloor = TargetGeometry.AltitudeAtHourAngle(s + durationLst - center, latSigned, decSigned);
                        }
                        else
                        {
                            windowFloor = TargetGeometry.AltitudeAtHourAngle(e - durationLst - center, latSigned, decSigned);
                        }
                        if (windowFloor > floorAlt) floorAlt = windowFloor;
                    }
                }

                optimalSeries.Points.AddXY(entry.SentinelX, optimalAlt);
                optimalFloorSeries.Points.AddXY(entry.SentinelX, floorAlt);
                optimalCenteredSeries.Points.AddXY(entry.SentinelX, centeredAlt);
            }
        }

        private void BuildMoonSeries()
        {
            TimeSpan utcOffset = TimeZoneInfo.Local.GetUtcOffset(Location.DateTime);
            double longitudeSign = Location.West ? -1.0 : 1.0;

            NightWindow night = NightCalculator.ComputeNight(Location);
            // NightWindow fields are UTC as of the Core DST fix; convert to local once here
            // because the minute-loop rounds to wall-clock hour boundaries for the X axis.
            DateTime duskLocal = night.AstronomicalDusk.ToLocalTime();
            DateTime dawnLocal = night.AstronomicalDawn.ToLocalTime();

            DateTime start = DayChartStart(duskLocal);
            DateTime stop  = DayChartStop(dawnLocal);

            Series moonSeries = MakeSeries("Moon", "Day",
                Color.FromArgb((int)(night.LunarIlluminationFraction * 250.0), 209, 209, 209));
            moonSeries.ChartType = SeriesChartType.Area;
            moonSeries.IsVisibleInLegend = false;

            TimeSpan delta = stop.Subtract(start);

            // Inclusive endpoint -- match BuildDaySeries so the Day chart's index range covers
            // the rightmost hour tick. Without this the moon series is one index shorter than
            // the target series and the rightmost hourly label has no anchor.
            int totalMinutes = Convert.ToInt32(Math.Round(delta.TotalMinutes, 0));
            for (int minutes = 0; minutes <= totalMinutes; minutes++)
            {
                DateTime point = start.AddMinutes(minutes);
                CoordinateSharp.Celestial cCelestial = CoordinateSharpGate.Calculate(
                    Location.Latitude, longitudeSign * Location.Longitude, point, utcOffset.Hours);
                moonSeries.Points.AddXY(point, cCelestial.MoonAltitude);
            }

            TargetSeriesList.Add(moonSeries);
        }
    }
}
