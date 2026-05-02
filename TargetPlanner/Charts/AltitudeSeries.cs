using Astronomy.Core;
using Astronomy.Core.Astrometry;
using Astronomy.Core.Brightness;
using Astronomy.Core.Horizons;
using Astronomy.Core.Moon;
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
using TargetPlanner.Caches;
using TargetPlanner.Support;

using Location = Astronomy.Core.Locations.Location;
using Target   = Astronomy.Core.Targets.Target;

namespace TargetPlanner.Charts
{
    public class AltitudeSeries
    {
        // Per-day intermediates that do not depend on Horizon or Duration. Owned by
        // ChartCacheStore as of Phase 3 of the SoC refactor; AltitudeSeries holds a reference
        // to the cache entry's read-only list and renders against it. Profile changes scrub
        // through the cached MoonSamples without re-hitting CoordinateSharp.

        // Init-only snapshot: Location and Target are captured at construction and cannot be
        // swapped afterward. Spinner edits on the main form update mLocation there; the
        // chart's copy stays frozen until the next Graph-click tear-and-rebuild. The one
        // exception is Horizon / Duration, which are scrub-able post-hoc via
        // RebuildOptimalSeries(horizon, duration) -- those two analysis parameters are
        // passed explicitly per call rather than being read from this snapshot.
        public Location Location { get; }
        public Target Target { get; }
        public List<Series> TargetSeriesList { get; private set; }
        private IReadOnlyList<NightCacheEntry> mYearCache;

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

        // Cache store reference (Phase 3 of the SoC refactor). Owns the per-(Location, Target)
        // year cache + the per-Location NightCache. AltitudeSeries reads from it; never
        // builds its own. Null is treated as "no cache available" -- BuildSeriesList early-
        // returns instead of synchronously building (the prior fallback path is retired).
        private readonly IChartCacheStore mCache;

        // Cached best D-hour session for the Day chart, in local time. Populated by
        // BuildDaySeries and refreshed by RebuildDayTooltip (Horizon / Duration spinner
        // scrubs). Consumed by AltitudeChart's Day-chart left-click handler to materialize
        // the window as a three-sided rectangle on top of the curve. Null when no window
        // fits tonight (duration is 0, polar-night, or the target never clears the horizon
        // long enough).
        private (DateTime Start, DateTime End, double Floor)? mBestDayWindow;
        public  (DateTime Start, DateTime End, double Floor)? BestDayWindow => mBestDayWindow;

        // Active moon-avoidance profile. AltitudeChart writes this before each rebuild
        // (Filters menu radio toggle, Lorentzian-control scrub, Edit Filters save) so
        // ComputeBestDayWindow's BestSession.For call gets the live profile. Null is the
        // backwards-compatible default -- Core's BestSession.For overload short-circuits
        // to the moon-blind path when it sees null or a Disabled profile.
        public MoonAvoidanceProfile MoonAvoidanceProfile { get; set; }

        // Active filter's center wavelength (nm). Pushed by AltitudeChart on filter
        // selection changes (Filters menu / radio / dialog Save). Used by the K-S
        // sky-brightness minute-loop to scale Location.ExtinctionK to the band via
        // SkyBrightness.ScaleK (Rayleigh λ⁻⁴). Default 550 nm (mid-visible) when no
        // filter is active.
        public double ActiveFilterCenterNm { get; set; } = 550.0;

        public AltitudeSeries(Location location, Target target, Color seriesColor,
                              IChartCacheStore cache)
        {
            if (location == null) throw new ArgumentNullException(nameof(location));
            if (target == null)   throw new ArgumentNullException(nameof(target));
            Location = location;
            Target = target;
            mSeriesColor = seriesColor;
            mCache = cache;
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
        // Progress<T> marshals each Report to the subscriber's creation context (UI thread),
        // so the subscribing ProgressBar Value setter runs on the UI thread regardless of
        // where the report originated. On exception, a phase may not tick -- subscribers that
        // track a tick count should not rely on exact counts to infer success.
        //
        // Threading model:
        //   - Caller's thread (UI, typically): TargetSeriesList.Clear, BuildMoonSeries,
        //     BuildDaySeries, FindOrCreateSeries x4. Sync preamble -- bounded (~O(points)
        //     across a single night), and runs before the first await yields.
        //   - Threadpool (via Task.Run): ComputeYearCache's 365-day CoordinateSharp loop.
        //     This is the expensive phase; multiple targets run their Year computes in
        //     parallel across threadpool threads when ReloadWithTargets kicks them off.
        //   - Caller's thread again (continuation post-await): RenderYearSeries +
        //     RenderOptimalSeries. Reads mYearCache and populates Series.Points.
        //
        // Keeping Moon/Day/Year-render on the caller's thread preserves the pre-refactor
        // parallel-compute-with-serialized-render pattern that benchmarks well even on
        // multi-target builds -- CoordinateSharp doesn't scale linearly beyond a handful of
        // parallel callers, so the serialization-on-render isn't a bottleneck and avoids
        // thread-affinity concerns the DataVisualization Series objects can exhibit.
        //
        // Exceptions (including OperationCanceledException from ComputeYearCache's
        // ThrowIfCancellationRequested) propagate to the caller so ReloadWithTargets'
        // Task.WhenAll can observe failure / cancellation and skip / defer the atomic swap.
        // Fire-and-forget callers (startup) wrap with their own try/catch.
        public async Task BuildSeriesList(IProgress<string> phaseProgress = null,
                                          CancellationToken ct = default)
        {
            // Each Target owns its AltitudeSeries, so a second build on the same Target
            // must start from a clean TargetSeriesList -- otherwise BuildMoonSeries /
            // BuildDaySeries, which unconditionally create fresh Series, would leave
            // duplicates next to the prior run. Year / Optimal are idempotent via
            // FindOrCreateSeries, but clearing here keeps all four on the same lifecycle.
            TargetSeriesList.Clear();

            // Phase 3: AltitudeChart hoists the shared Moon-Day series itself; per-target
            // BuildMoonSeries inside this method is no longer needed. Tick budget stays at
            // 2 + N*4 ticks because Moon is reported synthetically before any per-target
            // work begins (AltitudeChart.ReloadWithTargets reports it once after building
            // the shared moon series).
            phaseProgress?.Report("Moon");
            BuildDaySeries();
            phaseProgress?.Report("Day");

            // Pre-allocate the Year + Optimal Series up front so the background compute
            // phase never touches TargetSeriesList concurrently with ShowChartAreaSeries /
            // UpdateNowLine (both iterate TargetSeriesList on the UI thread).
            FindOrCreateSeries(Target.Name, "Year",                 mSeriesColor);
            FindOrCreateSeries(Target.Name, "Optimal",              mSeriesColor).LegendToolTip = "Ceiling";
            FindOrCreateSeries(Target.Name, "OptimalFloor",         mSeriesColor).LegendToolTip = "Floor";
            FindOrCreateSeries(Target.Name, "OptimalFloorCentered", mSeriesColor).LegendToolTip = "Symmetric";

            // Pull (or trigger build of) the per-target year cache from ChartCacheStore.
            // GetOrBuildAsync runs the compute on the threadpool; if multiple targets are
            // building concurrently, the cache store's gate amortizes them. Cancellation
            // surfaces as OperationCanceledException -- caller (ReloadWithTargets) catches
            // it and unwinds.
            if (mCache == null)
            {
                mYearCache = new List<NightCacheEntry>();  // empty placeholder; render no-ops
            }
            else
            {
                TargetCacheEntry entry = await mCache.GetOrBuildAsync(Target, ct);
                mYearCache = entry.YearDays;
            }

            RenderYearSeries();
            phaseProgress?.Report("Year");

            // Initial render uses the snapshot's Horizon and Duration; spinner scrubs
            // later call RebuildOptimalSeries(horizon, duration) with fresh values on
            // the UI thread without touching the snapshot.
            RenderOptimalSeries(Location.Horizon, Location.Duration);
            phaseProgress?.Report("Optimal");

            // Apply initial Day/Year visibility based on whether tonight qualifies at the
            // build-time spinner state. Subsequent spinner scrubs re-run this through
            // RebuildOptimalData -> RebuildDayTooltip on the UI thread.
            RebuildDayTooltip(Location.Horizon, Location.Duration);
        }

        // Rebuild only the Optimal series on Horizon / Duration / MoonAvoidanceProfile change.
        // Day, Moon, and Year don't depend on these inputs, so they stay as-is. Reads
        // mYearCache (populated during the async BuildSeriesList) instead of recomputing
        // dusk/dawn/LSTs etc. Cold-start: silent no-op when the cache hasn't been populated
        // yet -- the async builder is the sole owner of the cache build (a synchronous
        // ComputeYearCache fallback here used to be safe but became a UI-thread hazard after
        // moon samples were added: each invocation now runs ~25,600 CoordinateSharp moon
        // calls per target, which hangs the UI on profile / spinner scrubs that race the
        // initial async build). The async builder reads the latest MoonAvoidanceProfile
        // and re-renders Optimal at completion (line 247 below), so the chart converges on
        // the user's intent without us forcing it here.
        //
        // Horizon and Duration are passed explicitly rather than read from the snapshot so
        // scrubbing a spinner updates the rendered curve without mutating the snapshot.
        public void RebuildOptimalSeries(double horizon, TimeSpan duration)
        {
            if (mYearCache == null || mYearCache.Count == 0) return;
            RenderOptimalSeries(horizon, duration);
        }

        private void BuildDaySeries()
        {
            // Use the shared cache's Starting NightWindow when AltitudeChart supplied one;
            // otherwise fall through to the direct (gated) NightCalculator call for the
            // fire-and-forget startup path.
            NightWindow night = mCache?.LocationNightCache?.Starting ?? NightCalculator.ComputeNight(Location);
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
            // Chart renders Series.ToolTip natively; no mouse handler needed. ComposeDayTooltip
            // also writes mBestDayWindow as a side effect, so AltitudeChart's click handler
            // can materialize the same window as a chart rectangle without recomputing.
            daySeries.ToolTip = ComposeDayTooltip(Location.Horizon, Location.Duration);

            TargetSeriesList.Add(daySeries);

            // K-S sky-brightness companion series. Same minute grid + count as the
            // altitude series so the chart's IsXValueIndexed=true count invariant
            // holds. Visibility is toggled by AltitudeChart's Day sub-mode (Sky shows
            // it; Altitude hides it).
            BuildDaySkySeries(start, startUtc, count);
        }

        // Per-minute K-S sky-brightness curve. Computes target Alt/Az, moon Alt/Az,
        // phase angle, and atmospheric extinction at the active filter's wavelength;
        // feeds them into SkyBrightness.KsAt. Per-DataPoint tooltips show the time +
        // sky brightness for the hovered minute.
        private void BuildDaySkySeries(DateTime startLocal, DateTime startUtc, int count)
        {
            Series sky = MakeSeries(Target.Name, "MoonSky-Day", mSeriesColor);

            double kAtBand = SkyBrightness.ScaleK(Location.ExtinctionK, ActiveFilterCenterNm);
            double v0 = Bortle.DefaultZenithMag(Location.BortleClass);

            // Sun position is target-independent (depends only on observer + UTC). The
            // observer info is reused across all minute samples so we don't rebuild
            // the ObserverInfo struct per call -- AstroUtil.GetSunAltitude is the only
            // per-minute solar work added by twilight modeling.
            double latSigned = Location.LatSigned();
            double lonEast   = Location.LonEast();
            ObserverInfo observer = new ObserverInfo(latSigned, lonEast, Location.Elevation);

            for (int i = 0; i < count; i++)
            {
                DateTime utc = startUtc.AddMinutes(i);
                AltAz t = AltAzCalculator.At(Target, Location, utc);
                var m = MoonSeparation.ObserveAt(Target, Location, utc);
                double phase = SkyBrightness.PhaseAngleDegFromAgeDays(LunarAge.DaysAt(utc));
                double sunAlt = AstroUtil.GetSunAltitude(utc, observer);
                double mag = SkyBrightness.KsAt(
                    t.Altitude, t.Azimuth,
                    m.MoonAltDeg, m.MoonAzDeg,
                    phase, sunAlt, kAtBand, v0);

                // Sky-mode plot inverts Y so brighter sky (lower mag) renders HIGHER
                // on the chart while AxisY.IsReversed stays false (which keeps the X
                // axis at the visual bottom). AltitudeChart's ConfigureDayYAxis
                // installs CustomLabels that re-label these inverted positions with
                // the actual magnitude values. Tooltip surfaces the actual mag.
                double plotY = double.IsNaN(mag)
                    ? -90.0
                    : (SkyAxisMinMag + SkyAxisMaxMag - mag);
                int idx = sky.Points.AddXY(startLocal.AddMinutes(i), plotY);
                sky.Points[idx].ToolTip = string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "{0}\n{1:h:mm tt}\n{2}",
                    Target.Name,
                    startLocal.AddMinutes(i),
                    double.IsNaN(mag) ? "(target below horizon)" : mag.ToString("0.0") + " mag/arcsec²");
            }

            TargetSeriesList.Add(sky);
        }

        // Sky sub-mode Y-axis range. Held here because BuildDaySkySeries and
        // RebuildDaySkySeries both invert plot Y around the (Min + Max) midpoint;
        // AltitudeChart.ConfigureDayYAxis uses the same constants for axis range +
        // CustomLabels so the displayed labels match the actual magnitudes.
        public const double SkyAxisMinMag = 16.0;
        public const double SkyAxisMaxMag = 22.0;

        // Re-emit the MoonSky-Day series in place. Called from AltitudeChart on Bortle /
        // Extinction / ActiveFilter changes that don't invalidate the year cache (no
        // change to visibility geometry; just sky-brightness inputs). Preserves the Day
        // axis IsXValueIndexed=true count invariant by overwriting Y values rather than
        // rebuilding the series identity (mirrors the HD-overlay click pattern).
        public void RebuildDaySkySeries()
        {
            string skyName = Target.Name + "-MoonSky-Day";
            Series sky = null;
            foreach (Series s in TargetSeriesList)
            {
                if (s.Name == skyName) { sky = s; break; }
            }
            if (sky == null || sky.Points.Count == 0) return;

            double kAtBand = SkyBrightness.ScaleK(Location.ExtinctionK, ActiveFilterCenterNm);
            double v0 = Bortle.DefaultZenithMag(Location.BortleClass);

            // The first DataPoint's X value is local-time AddMinutes(0); convert via
            // OADate to recover the start instant, then march forward by minutes.
            DateTime startLocal = DateTime.FromOADate(sky.Points[0].XValue);
            DateTime startUtc = DateTime.SpecifyKind(startLocal, DateTimeKind.Local).ToUniversalTime();

            // Reused per-minute (target-independent).
            double latSigned = Location.LatSigned();
            double lonEast   = Location.LonEast();
            ObserverInfo observer = new ObserverInfo(latSigned, lonEast, Location.Elevation);

            for (int i = 0; i < sky.Points.Count; i++)
            {
                DateTime utc = startUtc.AddMinutes(i);
                AltAz t = AltAzCalculator.At(Target, Location, utc);
                var m = MoonSeparation.ObserveAt(Target, Location, utc);
                double phase = SkyBrightness.PhaseAngleDegFromAgeDays(LunarAge.DaysAt(utc));
                double sunAlt = AstroUtil.GetSunAltitude(utc, observer);
                double mag = SkyBrightness.KsAt(
                    t.Altitude, t.Azimuth,
                    m.MoonAltDeg, m.MoonAzDeg,
                    phase, sunAlt, kAtBand, v0);

                // Same Sky-mode plot-Y inversion as BuildDaySkySeries.
                double plotY = double.IsNaN(mag)
                    ? -90.0
                    : (SkyAxisMinMag + SkyAxisMaxMag - mag);
                sky.Points[i].YValues = new double[] { plotY };
                sky.Points[i].ToolTip = string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "{0}\n{1:h:mm tt}\n{2}",
                    Target.Name,
                    startLocal.AddMinutes(i),
                    double.IsNaN(mag) ? "(target below horizon)" : mag.ToString("0.0") + " mag/arcsec²");
            }
        }

        // Refresh the Day series' hover tooltip AND the Day visibility for the current
        // Horizon / Duration. Called from AltitudeChart.RebuildOptimalData alongside
        // RebuildOptimalSeries so the tooltip and Day-curve visibility stay in sync with
        // the Optimal-chart curves on spinner scrubs. Silently no-ops if the Day series
        // hasn't been built yet (initial async build hasn't finished and the user is
        // already scrubbing).
        //
        // Visibility rule (Day only): if no D-hour session at the current Horizon fits
        // tonight (mBestDayWindow == null after ComposeDayTooltip refreshes it), the Day
        // curve is hidden by setting Color = Transparent; when a session fits again,
        // Color is restored to mSeriesColor. Year curves are intentionally NOT scoped by
        // this rule -- a target with no fit tonight may still have D-hour windows other
        // months of the year, and the user expects the Year chart to show all checked
        // targets so they can pick a future imaging window.
        public void RebuildDayTooltip(double horizon, TimeSpan duration)
        {
            string tooltip = ComposeDayTooltip(horizon, duration);
            Color visibleColor = mBestDayWindow != null ? mSeriesColor : Color.Transparent;

            // Day altitude curve: tooltip + transparency-based hide when no moon-clear
            // D-hour window fits tonight. The MoonSky-Day companion curve mirrors the
            // hide-on-no-fit (consistency: a target hidden by the Lorentzian fit-check
            // shouldn't have its sky-brightness curve still visible in Sky sub-mode).
            // Note: the MoonSky color is only used when sub-mode = Sky; in Altitude mode
            // ApplyDaySubModeVisibility already disables the series.
            string dayName     = Target.Name + "-Day";
            string skyName     = Target.Name + "-MoonSky-Day";
            foreach (Series s in TargetSeriesList)
            {
                if (s.Name == dayName)
                {
                    s.ToolTip = tooltip;
                    s.Color   = visibleColor;
                }
                else if (s.Name == skyName)
                {
                    s.Color = visibleColor;
                }
            }
        }

        // Compute the best D-hour session for tonight and cache it in mBestDayWindow (in
        // local time). Returns null when duration is non-positive or when no window fits
        // (polar-night, target never clears the horizon long enough, etc.). Shared by
        // ComposeDayTooltip (for the hover-text) and -- via BestDayWindow -- by the Day-chart
        // left-click handler in AltitudeChart.
        //
        // Floor altitude is the minimum of the session's start and end altitudes. alt(HA) is
        // monotone away from transit, so:
        //   - transit-centered session: both endpoints are below peak; min(start, end) is the
        //     lower of the two, i.e. the session floor.
        //   - wall-pushed session (transit outside window): alt is monotone across the
        //     session; one endpoint is the high end, the other the low -- min is the wall.
        // Simpler and equivalent to the transit-distance argument, without needing a separate
        // TransitTime lookup.
        private (DateTime Start, DateTime End, double Floor)? ComputeBestDayWindow(
            double horizon, TimeSpan duration)
        {
            if (duration <= TimeSpan.Zero)
            {
                mBestDayWindow = null;
                return null;
            }

            // Honor the shared NightCache when AltitudeChart provided one (Graph-click
            // path). The prior inline tooltip code unconditionally called the gated
            // ComputeNight, adding one CoordinateSharp serial-lock hit per target on the
            // UI thread during BuildDaySeries; for 13+ target builds this was visible as
            // a noticeable "build takes a while" stall. Cache-first keeps the per-target
            // cost at pure math.
            NightWindow night = mCache?.LocationNightCache?.Starting ?? NightCalculator.ComputeNight(Location);
            IHorizonProfile horizonProfile = new ScalarHorizonProfile(horizon);

            var best = BestSession.For(
                Target, Location, night, horizonProfile,
                duration, duration,
                SinAltQuality,
                profile: MoonAvoidanceProfile);

            if (best == null)
            {
                mBestDayWindow = null;
                return null;
            }

            double floor = SessionAltitude.Floor(Target, Location, best.Value.Start, best.Value.End);

            var triple = (
                Start: best.Value.Start.ToLocalTime(),
                End:   best.Value.End.ToLocalTime(),
                Floor: floor);

            mBestDayWindow = triple;
            return triple;
        }

        // Build the tooltip string shown on hover over the Day-chart line. Falls back to
        // just the target name if Duration is non-positive (BestSession.For requires positive
        // minDuration) or if no D-hour window fits tonight (includes the polar-night case,
        // where VisibilityWindows.For -> BestSession.For returns null).
        private string ComposeDayTooltip(double horizon, TimeSpan duration)
        {
            string durLabel = duration.TotalHours.ToString("0.##", CultureInfo.InvariantCulture);

            if (duration <= TimeSpan.Zero)
            {
                mBestDayWindow = null;
                return Target.Name;
            }

            var window = ComputeBestDayWindow(horizon, duration);

            if (window == null)
            {
                return string.Format(CultureInfo.InvariantCulture,
                    "{0}\nNo {1}h window above {2:0}° tonight",
                    Target.Name, durLabel, horizon);
            }

            return string.Format(CultureInfo.InvariantCulture,
                "{0}\nBest {1}h window: {2:h:mm tt} → {3:h:mm tt}\nBest {1}h window Floor: {4:0}°",
                Target.Name, durLabel, window.Value.Start, window.Value.End, window.Value.Floor);
        }

        // The 365-day cache build moved to ChartCacheStore.ComputeYearDays in Phase 3 of
        // the SoC refactor. AltitudeSeries.BuildSeriesList obtains its mYearCache list via
        // mCache.GetOrBuildAsync(Target, ct) instead.

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
                int idx = yearSeries.Points.AddXY(entry.SentinelX, entry.YearAlt);
                yearSeries.Points[idx].ToolTip = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}\n{1:MMM dd, yyyy}\nMax altitude: {2:0.0}°",
                    Target.Name, entry.SentinelX, entry.YearAlt);
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
        // UI-thread-only: walks mYearCache and writes the three Optimal-area series. Per
        // night, computes candidate windows (visibility, optionally intersected with the
        // active moon-clear sub-intervals) then delegates placement + altitude evaluation
        // to Core (BestSession.PlaceBest / PlaceCentered + SessionAltitude.Floor /
        // Ceiling). The chart's per-night cost is dominated by the IntegratedQuality
        // Simpson sweep inside PlaceBest -- ~20 alt-az calls per candidate window, all
        // pure Meeus, lock-free. For 365 nights x 1-3 sub-intervals this is tens of ms,
        // well under the 150 ms OptimalRebuildDebounce_Tick cadence.
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

            double latSigned   = Location.LatSigned();
            double decSigned   = Target.DecSigned();
            double raHours     = Target.RightAscension;
            double horizonDeg  = horizon;
            double durationHrs = duration.TotalHours;
            double haHorizon   = TargetGeometry.HourAngleAtAltitude(latSigned, decSigned, horizonDeg);

            foreach (NightCacheEntry entry in mYearCache)
            {
                double ceilingAlt  = -90.0;
                double floorAlt    = -90.0;
                double centeredAlt = -90.0;

                if (!entry.IsPolar && entry.YearAlt >= horizonDeg)
                {
                    // Visibility windows in UTC, derived from cached LST/Alt fields. Empty
                    // when no above-horizon arc fits Duration (target never gets high
                    // enough or never stays up long enough).
                    List<(DateTime Start, DateTime End)> candidates =
                        EnumerateVisibilityWindowsUtc(entry, raHours, durationHrs, haHorizon);

                    // When moon avoidance is on AND the cache has moon samples, intersect
                    // with the moon-clear sub-intervals derived from cached samples +
                    // active profile. Defensive hatch: a stale cache without moon samples
                    // (pre-Slice-1) falls through to the moon-blind candidate set so the
                    // chart renders curves rather than going dark.
                    if (MoonAvoidanceProfile != null && MoonAvoidanceProfile.Enabled
                        && entry.MoonSamples != null && entry.MoonSamples.Count > 0)
                    {
                        List<(DateTime Start, DateTime End)> moonClear =
                            EnumerateMoonClearIntervalsUtc(entry, MoonAvoidanceProfile);
                        candidates = IntersectWindows(candidates, moonClear);
                    }

                    if (candidates.Count > 0)
                    {
                        // Floor / Ceiling: best-quality placement (transit-centered or
                        // wall-pushed) across the candidates; sin(alt) ranks them when
                        // multiple sub-intervals exist on the same night.
                        var session = BestSession.PlaceBest(
                            Target, Location, candidates, duration, duration, SinAltQuality);
                        if (session != null)
                        {
                            floorAlt   = SessionAltitude.Floor(
                                Target, Location, session.Value.Start, session.Value.End);
                            ceilingAlt = SessionAltitude.Ceiling(
                                Target, Location, session.Value.Start, session.Value.End);
                        }

                        // Symmetric: strict transit-centered placement, no wall-push.
                        // Reports the floor (= either endpoint altitude, since centered
                        // sessions are symmetric about transit).
                        var centered = BestSession.PlaceCentered(
                            Target, Location, candidates, duration);
                        if (centered != null)
                        {
                            centeredAlt = SessionAltitude.Floor(
                                Target, Location, centered.Value.Start, centered.Value.End);
                        }
                    }
                }

                int cIdx = optimalSeries.Points.AddXY(entry.SentinelX, ceilingAlt);
                int fIdx = optimalFloorSeries.Points.AddXY(entry.SentinelX, floorAlt);
                int sIdx = optimalCenteredSeries.Points.AddXY(entry.SentinelX, centeredAlt);
                AssignOptimalTooltip(
                    optimalSeries, cIdx, ceilingAlt,
                    optimalFloorSeries, fIdx, floorAlt,
                    optimalCenteredSeries, sIdx, centeredAlt,
                    entry.SentinelX);
            }
        }

        // Quality function for Optimal-chart placement. sin(altitude) is the standard
        // airmass-weighted proxy and matches what ComputeBestDayWindow uses.
        private static readonly Func<double, double> SinAltQuality =
            alt => Math.Sin(alt * Math.PI / 180.0);

        // Format an altitude value for the Optimal hover tooltip. Sentinel '-90' values
        // (polar / below-horizon / moon-aware short-circuit / centered-window doesn't fit)
        // render as '—' so the unified tooltip reads cleanly when one or more curves are
        // unviable for the hovered date.
        private static string FormatAlt(double alt)
            => alt <= -89.0
                ? "—"
                : alt.ToString("0.0", CultureInfo.InvariantCulture) + "°";

        // Build one unified Optimal hover-tooltip string and assign it to the just-added
        // DataPoints in all three Optimal curves. Hovering any of the three curves at this
        // date surfaces the same target+date+Ceiling/Floor/Symmetric block, so the user
        // sees the value of the curve they're hovering plus the relationship to the other
        // two without moving the mouse. -90 sentinels render via FormatAlt as '—'.
        private void AssignOptimalTooltip(
            Series cSeries, int cIdx, double cAlt,
            Series fSeries, int fIdx, double fAlt,
            Series sSeries, int sIdx, double sAlt,
            DateTime sentinelX)
        {
            string text = string.Format(
                CultureInfo.InvariantCulture,
                "{0} — {1:MMM dd, yyyy}\nCeiling: {2}\nFloor: {3}\nSymmetric: {4}",
                Target.Name, sentinelX,
                FormatAlt(cAlt), FormatAlt(fAlt), FormatAlt(sAlt));
            cSeries.Points[cIdx].ToolTip = text;
            fSeries.Points[fIdx].ToolTip = text;
            sSeries.Points[sIdx].ToolTip = text;
        }

        // ====================================================================
        // Moon-aware placement helpers for RenderOptimalSeries
        // ====================================================================

        // Pairwise interval intersection, O(|a| * |b|). Both inputs are typically tiny
        // (visibility: 1-3 windows; moon-clear: 1-3 sub-intervals on a typical night),
        // so the quadratic shape is fine without sweep-line bookkeeping. Returns the
        // common time covered by both lists; empty if no overlap exists. Output is
        // unsorted relative to neither input but sub-intervals are non-overlapping.
        private static List<(DateTime Start, DateTime End)> IntersectWindows(
            List<(DateTime Start, DateTime End)> a,
            List<(DateTime Start, DateTime End)> b)
        {
            var result = new List<(DateTime Start, DateTime End)>();
            foreach (var x in a)
            {
                foreach (var y in b)
                {
                    DateTime s = x.Start > y.Start ? x.Start : y.Start;
                    DateTime e = x.End   < y.End   ? x.End   : y.End;
                    if (s < e) result.Add((s, e));
                }
            }
            return result;
        }

        // Enumerate above-horizon visibility windows for the day in UTC, mirroring the
        // shifted-transit (k = -1, 0, +1) logic in RenderOptimalSeries' placement loop.
        // Result is empty when no above-horizon arc of length >= Duration fits the night;
        // single entry for circumpolar above-horizon targets; up to three otherwise.
        private static List<(DateTime Start, DateTime End)> EnumerateVisibilityWindowsUtc(
            NightCacheEntry entry, double raHours, double durationHrs, double haHorizon)
        {
            const double SiderealHoursPerSolarDay = 24.06570982441908;
            var result = new List<(DateTime Start, DateTime End)>();

            if (double.IsPositiveInfinity(haHorizon))
            {
                double lengthSolar = (entry.LstDawn - entry.LstDusk) * 24.0 / SiderealHoursPerSolarDay;
                if (lengthSolar >= durationHrs) result.Add((entry.Dusk, entry.Dawn));
                return result;
            }
            if (double.IsNaN(haHorizon)) return result;

            double lstRange = entry.LstDawn - entry.LstDusk;
            if (lstRange <= 0.0) return result;
            long durationTicks = (entry.Dawn - entry.Dusk).Ticks;

            for (int k = -1; k <= 1; k++)
            {
                double center = raHours + 24.0 * k;
                double ahStart = center - haHorizon;
                double ahEnd   = center + haHorizon;
                double sLst = Math.Max(entry.LstDusk, ahStart);
                double eLst = Math.Min(entry.LstDawn, ahEnd);
                if (sLst >= eLst) continue;
                double lengthSolar = (eLst - sLst) * 24.0 / SiderealHoursPerSolarDay;
                if (lengthSolar < durationHrs) continue;

                double sFrac = (sLst - entry.LstDusk) / lstRange;
                double eFrac = (eLst - entry.LstDusk) / lstRange;
                DateTime utcS = entry.Dusk.AddTicks((long)(sFrac * durationTicks));
                DateTime utcE = entry.Dusk.AddTicks((long)(eFrac * durationTicks));
                result.Add((utcS, utcE));
            }
            return result;
        }

        // Walk MoonSamples and emit moon-clear (Start, End) sub-intervals in UTC by
        // applying MoonAvoidance.IsRejected(sep, age, moonAlt, profile) per sample.
        // Boundary crossings use the half-step midpoint -- matches the fallback in
        // BestSession.MoonClearIntersect when the linear-interpolation denominator
        // collapses; ~5-min boundary uncertainty is well under the 10-min sweep cadence.
        private static List<(DateTime Start, DateTime End)> EnumerateMoonClearIntervalsUtc(
            NightCacheEntry entry, MoonAvoidanceProfile profile)
        {
            var result = new List<(DateTime Start, DateTime End)>();
            if (entry.MoonSamples == null || entry.MoonSamples.Count == 0) return result;

            DateTime? clearStart = null;
            DateTime tPrev = default;
            bool clearPrev = false;
            bool first = true;

            foreach (MoonSample s in entry.MoonSamples)
            {
                bool clearCur = !MoonAvoidance.IsRejected(
                    s.SepDeg, entry.MoonAgeDays, s.MoonAltDeg, profile);
                if (first)
                {
                    if (clearCur) clearStart = s.Utc;
                    tPrev = s.Utc;
                    clearPrev = clearCur;
                    first = false;
                    continue;
                }

                if (clearPrev != clearCur)
                {
                    DateTime crossing = tPrev.AddTicks((s.Utc - tPrev).Ticks / 2);
                    if (clearCur)
                    {
                        clearStart = crossing;
                    }
                    else if (clearStart.HasValue)
                    {
                        result.Add((clearStart.Value, crossing));
                        clearStart = null;
                    }
                }

                tPrev = s.Utc;
                clearPrev = clearCur;
            }

            if (clearStart.HasValue) result.Add((clearStart.Value, tPrev));
            return result;
        }

    }
}
