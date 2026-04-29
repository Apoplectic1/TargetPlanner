using Astronomy.Core;
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
        }

        // Refresh the Day series' hover tooltip AND the Day/Year visibility for the current
        // Horizon / Duration. Called from AltitudeChart.RebuildOptimalData alongside
        // RebuildOptimalSeries so the tooltip and curve visibility stay in sync with the
        // Optimal-chart curves on spinner scrubs. Silently no-ops if the Day series hasn't
        // been built yet (initial async build hasn't finished and the user is already scrubbing).
        //
        // Visibility rule: if no D-hour session at the current Horizon fits tonight
        // (mBestDayWindow == null after ComposeDayTooltip refreshes it), the Day and Year
        // curves for this target are hidden by setting Color = Transparent. When a session
        // fits again, Color is restored to mSeriesColor. Spinner re-evaluation is the source
        // of truth and overrides any prior legend-click toggle on these two series.
        public void RebuildDayTooltip(double horizon, TimeSpan duration)
        {
            string tooltip = ComposeDayTooltip(horizon, duration);
            Color visibleColor = mBestDayWindow != null ? mSeriesColor : Color.Transparent;

            string dayName  = Target.Name + "-Day";
            string yearName = Target.Name + "-Year";
            foreach (Series s in TargetSeriesList)
            {
                if (s.Name == dayName)
                {
                    s.ToolTip = tooltip;
                    s.Color   = visibleColor;
                }
                else if (s.Name == yearName)
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
                alt => Math.Sin(alt * Math.PI / 180.0),
                profile: MoonAvoidanceProfile);

            if (best == null)
            {
                mBestDayWindow = null;
                return null;
            }

            double altStart = AltAzCalculator.At(Target, Location, best.Value.Start).Altitude;
            double altEnd   = AltAzCalculator.At(Target, Location, best.Value.End).Altitude;
            double floor    = Math.Min(altStart, altEnd);

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

                // Moon-aware short-circuit (Phase-3 Slice 1). When avoidance is enabled
                // and the night has no (visibility ∩ moon-clear) sub-interval of length
                // >= Duration, drop all three curves to -90. The cache is
                // profile-independent: raw moon samples were stored at build time; this
                // walks them and applies the active profile (no CoordinateSharp here).
                // On viable nights the existing placement runs unchanged, so the
                // three curves keep their distinct semantics and the year-trace
                // "oscillates" between natural maxima and the -90 sentinel as the
                // lunar cycle moves through.
                if (MoonAvoidanceProfile != null && MoonAvoidanceProfile.Enabled
                    && !HasMoonClearViableWindow(entry, raHours, durationHrs, haHorizon, MoonAvoidanceProfile))
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

        // ====================================================================
        // Phase-3 Slice 1: moon-aware short-circuit helpers for RenderOptimalSeries
        // ====================================================================

        // Does the night have at least one (visibility ∩ moon-clear) interval of length
        // >= Duration under the active profile? Used by RenderOptimalSeries to drop all
        // three Optimal curves to -90 on moon-impacted nights. Defensive: returns true
        // (no constraint) when the cache entry has no moon samples, so a stale or
        // pre-Slice-1 cache renders the moon-blind curves rather than going dark.
        private static bool HasMoonClearViableWindow(
            NightCacheEntry entry, double raHours, double durationHrs, double haHorizon,
            MoonAvoidanceProfile profile)
        {
            if (profile == null || !profile.Enabled) return true;
            if (entry.MoonSamples == null || entry.MoonSamples.Count == 0) return true;

            List<(DateTime Start, DateTime End)> visibility =
                EnumerateVisibilityWindowsUtc(entry, raHours, durationHrs, haHorizon);
            if (visibility.Count == 0) return false;

            List<(DateTime Start, DateTime End)> moonClear =
                EnumerateMoonClearIntervalsUtc(entry, profile);
            if (moonClear.Count == 0) return false;

            TimeSpan minDuration = TimeSpan.FromHours(durationHrs);
            foreach (var v in visibility)
            {
                foreach (var m in moonClear)
                {
                    DateTime s = v.Start > m.Start ? v.Start : m.Start;
                    DateTime e = v.End   < m.End   ? v.End   : m.End;
                    if (e - s >= minDuration) return true;
                }
            }
            return false;
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
