using Astronomy.Core;
using Astronomy.Core.Horizons;
using Astronomy.Core.Night;
using Astronomy.Core.Session;
using Astronomy.Core.Time;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Threading;
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
        //   "Day"     -- after BuildMoonSeries + BuildDaySeries populate the minute-loop data.
        //   "Year"    -- after ComputeYearCache populates mYearCache and RenderYearSeries lands.
        //   "Optimal" -- after RenderOptimalSeries lands the three Optimal-area curves.
        // Progress<T> marshals each Report to the subscriber's creation context (UI thread),
        // so the subscribing ProgressBar Value setter runs on the UI thread regardless of
        // where the report originated. On exception, a phase may not tick -- subscribers that
        // track a tick count should not rely on exact counts to infer success.
        //
        // Two entry points share BuildSeriesListBody:
        //
        //   BuildSeriesList          -> async, wraps the whole body in Task.Run. Used by
        //                               user-driven multi-target builds (AltitudeChart.Reload-
        //                               WithTargets) so the UI thread stays responsive to
        //                               Cancel clicks even while N targets are compiling their
        //                               Moon / Day series in parallel.
        //   BuildSeriesListBlocking  -> synchronous on the caller's thread. Used at startup
        //                               (AltitudeChart.BuildTargetSeriesList) where the caller
        //                               immediately calls ShowChartAreaSeries and needs Day /
        //                               Moon / Year / Optimal already populated in
        //                               TargetSeriesList when it runs.
        //
        // Cross-thread Series.Points mutation is safe in both paths because
        // AltitudeChart.ReloadWithTargets stages every Series in TargetSeriesList (not
        // mChart.Series) until the atomic swap AFTER WhenAll completes -- so no Series
        // attached to mChart ever has its Points mutated off the UI thread.
        //
        // Exceptions (including OperationCanceledException from ComputeYearCache's ct-checks)
        // propagate out so ReloadWithTargets' Task.WhenAll / the startup try-catch can
        // observe failure / cancellation and skip / defer their follow-up work.
        public Task BuildSeriesList(IProgress<string> phaseProgress = null,
                                    CancellationToken ct = default)
        {
            return Task.Run(() => BuildSeriesListBody(phaseProgress, ct), ct);
        }

        public void BuildSeriesListBlocking(IProgress<string> phaseProgress = null,
                                            CancellationToken ct = default)
        {
            BuildSeriesListBody(phaseProgress, ct);
        }

        private void BuildSeriesListBody(IProgress<string> phaseProgress, CancellationToken ct)
        {
            // Each Target owns its AltitudeSeries, so a second build on the same Target
            // must start from a clean TargetSeriesList -- otherwise BuildMoonSeries /
            // BuildDaySeries, which unconditionally create fresh Series, would leave
            // duplicates next to the prior run. Year / Optimal are idempotent via
            // FindOrCreateSeries, but clearing here keeps all four on the same lifecycle.
            TargetSeriesList.Clear();

            BuildMoonSeries();
            BuildDaySeries();
            phaseProgress?.Report("Day");

            // Pre-allocate the Year + Optimal Series up front; RenderYearSeries and
            // RenderOptimalSeries expect FindOrCreateSeries to have already added them.
            FindOrCreateSeries(Target.Name, "Year",                 mSeriesColor);
            FindOrCreateSeries(Target.Name, "Optimal",              mSeriesColor);
            FindOrCreateSeries(Target.Name, "OptimalFloor",         mSeriesColor);
            FindOrCreateSeries(Target.Name, "OptimalFloorCentered", mSeriesColor);

            mYearCache = ComputeYearCache(ct);

            RenderYearSeries();
            phaseProgress?.Report("Year");

            // Initial render uses the snapshot's Horizon and Duration; spinner scrubs
            // later call RebuildOptimalSeries(horizon, duration) with fresh values on
            // the UI thread without touching the snapshot.
            RenderOptimalSeries(Location.Horizon, Location.Duration);
            phaseProgress?.Report("Optimal");
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
            int count = totalMinutes + 1;

            // Batch-evaluate the altitude grid via a single Core call: computes LST once at
            // the grid start, advances linearly per sample, and calls AltitudeAtHourAngle in
            // place of per-minute AltAzCalculator.Of + Location.With allocation. Measured
            // ~2.6x faster and ~11x less allocation than the per-minute form (see
            // Astronomy.Core.Tests/Benchmarks/AltitudeCurveBenchmark, 2026-04-23). Kind=Local
            // -> UTC conversion happens at the call site so the Core helper gets its
            // contracted Kind=Utc start.
            DateTime startUtc = DateTime.SpecifyKind(start, DateTimeKind.Local).ToUniversalTime();
            IReadOnlyList<double> altitudes = AltitudeCurve.Sample(
                Target, Location, startUtc, TimeSpan.FromMinutes(1), count);

            for (int i = 0; i < count; i++)
            {
                daySeries.Points.AddXY(start.AddMinutes(i), altitudes[i]);
            }

            // Per-series hover tooltip summarizing the best D-hour imaging session tonight.
            // Rebuilt on Horizon / Duration spinner scrubs via RebuildDayTooltip(...). The
            // Chart renders Series.ToolTip natively; no mouse handler needed.
            daySeries.ToolTip = ComposeDayTooltip(Location.Horizon, Location.Duration);

            TargetSeriesList.Add(daySeries);
        }

        // Refresh the Day series' hover tooltip to reflect current Horizon / Duration values.
        // Called from AltitudeChart.RebuildOptimalData alongside RebuildOptimalSeries so the
        // tooltip stays in sync with the Optimal-chart curves on spinner scrubs. Silently
        // no-ops if the Day series hasn't been built yet (initial async build hasn't finished
        // and the user is already scrubbing).
        public void RebuildDayTooltip(double horizon, TimeSpan duration)
        {
            string dayName = Target.Name + "-Day";
            foreach (Series s in TargetSeriesList)
            {
                if (s.Name == dayName)
                {
                    s.ToolTip = ComposeDayTooltip(horizon, duration);
                    return;
                }
            }
        }

        // Build the tooltip string shown on hover over the Day-chart line. Falls back to
        // just the target name if Duration is non-positive (BestSession.For requires positive
        // minDuration) or if no D-hour window fits tonight (includes the polar-night case,
        // where VisibilityWindows.For -> BestSession.For returns null).
        //
        // Floor altitude is the minimum of the session's start and end altitudes. alt(HA) is
        // monotone away from transit, so:
        //   - transit-centered session: both endpoints are below peak; min(start, end) is the
        //     lower of the two, i.e. the session floor.
        //   - wall-pushed session (transit outside window): alt is monotone across the
        //     session; one endpoint is the high end, the other the low -- min is the wall.
        // Simpler and equivalent to the transit-distance argument, without needing a separate
        // TransitTime lookup.
        private string ComposeDayTooltip(double horizon, TimeSpan duration)
        {
            if (duration <= TimeSpan.Zero) return Target.Name;

            NightWindow night = NightCalculator.ComputeNight(Location);
            IHorizonProfile horizonProfile = new ScalarHorizonProfile(horizon);

            var best = BestSession.For(
                Target, Location, night, horizonProfile,
                duration, duration,
                alt => Math.Sin(alt * Math.PI / 180.0));

            string durLabel = duration.TotalHours.ToString("0.##", CultureInfo.InvariantCulture);

            if (best == null)
            {
                return string.Format(CultureInfo.InvariantCulture,
                    "{0}\nNo {1}h window above {2:0}° tonight",
                    Target.Name, durLabel, horizon);
            }

            double altStart = AltAzCalculator.At(Target, Location, best.Value.Start).Altitude;
            double altEnd   = AltAzCalculator.At(Target, Location, best.Value.End).Altitude;
            double floor    = Math.Min(altStart, altEnd);

            DateTime startLocal = best.Value.Start.ToLocalTime();
            DateTime endLocal   = best.Value.End.ToLocalTime();

            return string.Format(CultureInfo.InvariantCulture,
                "{0}\nBest {1}h window: {2:HH:mm} → {3:HH:mm}\nFloor: {4:0}°",
                Target.Name, durLabel, startLocal, endLocal, floor);
        }

        // Walk 365 days, call ComputeNight and two AltAz.Of calls per day, return the
        // Horizon/Duration-independent intermediates. Year values are invariant to Horizon and
        // Duration so this never needs to run on a spinner tick.
        //
        // Pure compute: no WinForms Series.Points access, no mYearCache assignment. Safe to
        // run on a background thread via Task.Run. The caller assigns the returned list to
        // mYearCache on the UI thread before rendering. ct.ThrowIfCancellationRequested()
        // runs once per day; caller catches OperationCanceledException to unwind cleanly.
        private List<NightCacheEntry> ComputeYearCache(CancellationToken ct = default)
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
                ct.ThrowIfCancellationRequested();

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
