using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Astronomy.Core.Astrometry;
using Astronomy.Core.Horizons;
using Astronomy.Core.Moon;
using Astronomy.Core.Night;
using Astronomy.Core.Session;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.WinForms;
using SkiaSharp;
using TargetPlanner.Caches;
using TargetPlanner.State;
using TargetPlanner.Support;

using Location = Astronomy.Core.Locations.Location;
using Target   = Astronomy.Core.Targets.Target;
using LvcPointD = LiveChartsCore.Drawing.LvcPointD;

namespace TargetPlanner.Charts
{
    // LiveCharts2 implementation of TP's Day chart area. Stateless renderer:
    // every Render(...) call refreshes the chart from the supplied inputs,
    // preserving Series identity across calls (ObservableCollection mutation
    // triggers reactive redraw without rebuilding the series object).
    //
    // Owns two controllers wired to its CartesianChart instance:
    //   - OverlayController: HD click-to-toggle best-window step rectangle.
    //   - HoverTooltipController: smooth-curve interpolated tooltip
    //     (300 ms debounce — Day altitude is continuous).
    public class AltitudeSubChart_Day : IAltitudeSubChart
    {
        // Y axis bounds for Day (altitude, degrees). MaxAltitude stays at 90 so
        // hover tests can use the same [0, 90] plot-area gate as the prototype.
        public const double MinAltitude = 0.0;
        public const double MaxAltitude = 90.0;

        // The label-edge epsilon used to dodge LC2's Ceil/Floor edge-tick
        // floating-point sensitivity lives in ChartLayout (shared with Sky).

        // The Container hosts (top) the CartesianChart at fixed height + (bottom) a
        // FlowLayoutPanel hosting custom legend items that wrap as targets grow.
        // MainForm adds Container to Panel_AltitudeChart and resizes Panel +
        // GroupBox + Form to match Container's IdealHeight on legend changes.
        public Control Control { get; }
        private readonly Panel mContainer;
        private readonly CartesianChart mChart;
        private readonly ChartLegendPanel mLegend;

        // Chart-furniture state preserved across Render calls. Sections /
        // Axes objects are mutated in place; only Series can be re-listed.
        private readonly Axis mXAxis;
        private readonly Axis mYAxis;
        private readonly DuskDawnGradient mGradient;
        private readonly RectangularSection mNowLine;
        private readonly RectangularSection mHorizonLine;

        // Site time zone for the current Render, captured so the X-axis Labeler
        // (created once in the ctor) and the hover-tooltip formatter can convert
        // a UTC-OADate axis value to real local clock. The X-axis is UTC-internal
        // -- every plotted X is the OADate of a UTC instant -- and this zone is
        // the single seam where UTC becomes a displayed wall-clock label, so DST
        // transitions resolve correctly per-instant. Null before the first
        // Render; consumers fall back to a raw (zone-blind) format then.
        private TimeZoneInfo mAxisZone;

        // Per-target series keyed by Target. Series identity preserved across
        // renders; ObservableCollection mutation drives the reactive redraw.
        private readonly Dictionary<Target, LineSeries<ObservablePoint>> mSeriesByTarget
            = new Dictionary<Target, LineSeries<ObservablePoint>>();

        // Per-target stable color from the palette. Stashed during Render so the
        // hide-on-no-fit refresh path can restore the original color when a curve
        // becomes visible again. Mirrors AltitudeChart.mTargetColors.
        private readonly Dictionary<Target, Color> mTargetColors
            = new Dictionary<Target, Color>();

        // Per-target best D-hour window for the HD overlay click handler.
        // Keyed by LineSeries (not Target) because OverlayController operates on
        // the LineSeries it found via hit-test. The trailing transitOA is the
        // per-target upper-transit X for the HD-overlay's downward tick decoration,
        // already clipped to [startOA, endOA] (null when transit falls outside the
        // window -- typically Floor-mode wall-pushed placements).
        private readonly Dictionary<LineSeries<ObservablePoint>, (double startOA, double endOA, double floor, double? transitOA)>
            mTargetWindows = new Dictionary<LineSeries<ObservablePoint>, (double, double, double, double?)>();

        private LineSeries<ObservablePoint> mMoonSeries;

        private readonly OverlayController mOverlay;
        private readonly HoverTooltipController mHover;

        // Two placement-strategy radios overlaid on the plot area top-left
        // (right of the Y-axis "90°" label). Pure target filter: Floor shows
        // all fit-tonight targets (default, current behavior), Transit shows
        // only targets whose strict transit-centered placement fits
        // (Tonight.CenteredFloor.HasValue) -- the Sessions-chart Symmetric
        // subset. Mode lives on ChartContext; MainForm projects it via
        // SnapshotCurrent and a DayMode flip flows through the coordinator's
        // Apply pipeline as a normal Render (cache eval surfaces
        // DayModeChanged=true for any future short-circuit consumer).
        private readonly RadioButton mAllRadio;
        private readonly RadioButton mCenteredTransitRadio;
        // Shared ToolTip component for the mode radios. WinForms ToolTip is a
        // Component (not a Control); kept as a field so it stays alive past the
        // ctor (a GC'd ToolTip silently stops showing tips on its attached controls).
        private readonly ToolTip mModeTooltip = new ToolTip();
        // Suppress DayChartModeChanged event during programmatic radio updates
        // (constructor seed, future external SetMode calls) so the form's
        // subscription doesn't trigger a spurious snapshot+apply on startup.
        private bool mSuppressModeChangedEvent;

        /// <summary>Currently selected placement-strategy mode.</summary>
        public DayChartMode Mode { get; private set; } = DayChartMode.Floor;

        /// <summary>Raised when the user clicks a different Floor/Meridian/Wall
        /// radio. MainForm subscribes and routes through
        /// <c>mCoordinator.Apply(SnapshotCurrent())</c> so the new DayMode
        /// reaches the active sub-chart through the single Render seam.</summary>
        public event EventHandler DayChartModeChanged;

        // Most recent dayKey passed to a successful Render. Used to decide whether
        // HD overlay backups (which snapshot per-minute altitude Y values) remain
        // valid across the next Render: same dayKey = altitude unchanged = prune
        // + refresh; different dayKey = altitude about to be replaced = wipe.
        private DayWindowKey mLastDayKey;

        // Raised when the chart's IdealHeight changes (legend wrap count moved).
        // MainForm subscribes and resizes Panel_AltitudeChart + GroupBox_Altitude +
        // Form by the delta so the plot area stays in a fixed pixel position
        // regardless of target count. Forwarded from mLegend (wired in the ctor).
        public event EventHandler IdealHeightChanged;

        // Fixed chart height + the legend's current wrapped height -- owned by
        // ChartLegendPanel.
        public int IdealHeight => mLegend.IdealHeight;

        public AltitudeSubChart_Day()
        {
            mXAxis = ChartLayout.MakeTimeXAxis(() => mAxisZone);
            mYAxis = ChartLayout.MakeAltitudeYAxis("Altitude (°)");

            // Initialize section objects with placeholder bounds; Render() rewrites
            // Xi/Xj/Yi/Yj per the actual night window.
            mGradient = new DuskDawnGradient();
            mNowLine = new RectangularSection
            {
                Xi = 0, Xj = 0,
                Stroke = new SolidColorPaint(SKColors.Red, 2),
            };
            mHorizonLine = new RectangularSection
            {
                Yi = 30, Yj = 30,
                Stroke = new SolidColorPaint(SKColors.Green, 2),
            };

            mChart = new CartesianChart
            {
                XAxes = new[] { mXAxis },
                YAxes = new[] { mYAxis },
                Sections = new[] { mGradient.Dusk, mGradient.Dawn, mNowLine, mHorizonLine },
                Series = Array.Empty<ISeries>(),
                LegendPosition = LegendPosition.Hidden,
                FindingStrategy = FindingStrategy.ExactMatch,
                TooltipPosition = TooltipPosition.Hidden,
                AnimationsSpeed = TimeSpan.Zero,
                BackColor = ChartLayout.ChartBackground,
                Dock = DockStyle.Top,
                Height = ChartLayout.ChartFixedHeight,
            };

            // Lock the plot area to a fixed pixel rectangle. Bottom margin is just
            // X-axis label space — the legend lives outside the chart in a sibling
            // FlowLayoutPanel, so chart height is constant and X-axis labels sit at
            // a fixed pixel position relative to the plot area.
            mChart.DrawMargin = new LiveChartsCore.Measure.Margin(
                ChartLayout.LeftChromePx, ChartLayout.TopChromePx,
                ChartLayout.RightChromePx, ChartLayout.XAxisLabelHeightPx);

            mLegend = new ChartLegendPanel(mChart);
            mLegend.IdealHeightChanged += (s, e) => IdealHeightChanged?.Invoke(this, EventArgs.Empty);

            mContainer = new Panel
            {
                BackColor = ChartLayout.ChartBackground,
                Dock = DockStyle.Fill,
            };
            // Order matters for Dock=Top stacking: the LAST control added docks
            // FIRST. Add legend first (lower z-order), then chart (higher z-order)
            // so chart claims the top region and legend docks below it.
            mContainer.Controls.Add(mLegend.Panel);
            mContainer.Controls.Add(mChart);
            Control = mContainer;

            // Per-instance controllers wired to mChart (the CartesianChart inside
            // the container). Hover uses interpolated mode (300 ms) because Day
            // altitude curves are smooth.
            mOverlay = new OverlayController(
                mChart,
                () => mSeriesByTarget.Values,
                series => mTargetWindows.TryGetValue(series, out var w)
                    ? ((double, double, double, double?)?)w
                    : null,
                s => Log.Diag("Overlay", s));
            // Custom curve tooltip so the hover time reads the site's wall clock.
            // HoverTooltipController.DefaultInterpolatedTooltip formats hoverX
            // (a UTC OADate, since the X axis is UTC-internal) zone-blind, which
            // would be 1 h off after a DST transition; AxisTimeLabel does the
            // ConvertTimeFromUtc the axis Labeler does.
            mHover = new HoverTooltipController(
                mChart,
                () => mSeriesByTarget.Values,
                curveTooltipFormatter: (series, data, hoverX, interpY, segmentStart) =>
                    $"{series.Name}\n{ChartLayout.FormatZonedAxisLabel(hoverX, mAxisZone)}\nAltitude: {interpY:F1}°",
                debounceMs: 300);

            mChart.MouseDown += OnChartMouseDown;
            mGradient.WireSizeChanged(mChart);

            // Two placement-strategy radios overlaid on the plot area top-left,
            // just right of the Y-axis "90°" label. Parented to mContainer (a
            // sibling of mChart) rather than mChart itself: CartesianChart
            // derives from SkiaSharp's SKControl, which paints its entire client
            // area via Skia and does not reliably render child WinForms controls.
            // mContainer.Controls.Add positions the radios in mContainer's
            // coordinate space; since mChart is Dock=Top at y=0, the radios at
            // (LeftChromePx + ..., TopChromePx + ...) land visually inside the
            // plot area's chrome margin. BringToFront() ensures they paint above
            // the chart in WinForms Z-order.
            mSuppressModeChangedEvent = true;
            mAllRadio = MakeModeRadio("All",   DayChartMode.Floor,   isChecked: true,
                                          "Tonight's targets filtered by 'Target Floor', 'Duration' and 'Moon Avoidance' spinners");
            mCenteredTransitRadio = MakeModeRadio("Centered Transit", DayChartMode.Transit, isChecked: false,
                                          "Tonight's targets further filtered by symmetric transit - *right click*");
            int radioY = ChartLayout.TopChromePx + 2;
            int radioX = ChartLayout.LeftChromePx + 5;
            mAllRadio.Location = new Point(radioX, radioY);
            mCenteredTransitRadio.Location = new Point(radioX + 38,  radioY);
            mContainer.Controls.Add(mAllRadio);
            mContainer.Controls.Add(mCenteredTransitRadio);
            mAllRadio.BringToFront();
            mCenteredTransitRadio.BringToFront();
            mSuppressModeChangedEvent = false;
        }

        // Build one of the three placement-strategy radios. Dark theme to blend
        // into the chart background; AutoSize keeps label width tight regardless
        // of font metrics. The Tag holds the DayChartMode value so the shared
        // CheckedChanged handler can route without per-radio handlers.
        private RadioButton MakeModeRadio(string label, DayChartMode mode, bool isChecked, string tooltip)
        {
            var radio = new RadioButton
            {
                Text = label,
                Tag = mode,
                AutoSize = true,
                BackColor = ChartLayout.ChartBackground,
                ForeColor = SystemColors.ControlLightLight,
                FlatStyle = FlatStyle.Flat,
                Checked = isChecked,
            };
            radio.CheckedChanged += OnModeRadioCheckedChanged;
            mModeTooltip.SetToolTip(radio, tooltip);
            return radio;
        }

        // Shared handler for all three placement-strategy radios. CheckedChanged
        // fires twice per click (once on the old radio going false, once on the
        // new going true); only the going-true edge updates Mode + fires the
        // public event. Suppressed during construction so the initial seed
        // doesn't generate a phantom mode change.
        private void OnModeRadioCheckedChanged(object sender, EventArgs e)
        {
            if (mSuppressModeChangedEvent) return;
            if (!(sender is RadioButton rb) || !rb.Checked) return;
            if (!(rb.Tag is DayChartMode newMode) || newMode == Mode) return;
            Mode = newMode;
            DayChartModeChanged?.Invoke(this, EventArgs.Empty);
        }

        // Mode-aware overlay-window selector. Collapses two concerns into one:
        // (a) "is this target visible in the active mode?" and (b) "what's the
        // overlay step's window?" -- returns null when the target is filtered
        // out for the active mode, returns the (start, end, floor) triple
        // otherwise. Floor mode reads PlaceBest's window + floor; Transit mode
        // reads PlaceCentered's strict-centered window + floor. The per-mode
        // trios in NightFit are populated atomically (all three non-null when
        // the placement succeeded, all three null when it didn't), so the
        // pattern-match conjunction here is equivalent to "the placement fit."
        private static (DateTime startUtc, DateTime endUtc, double floor)? GetModeWindow(
            NightFit tonight, DayChartMode mode)
        {
            switch (mode)
            {
                case DayChartMode.Transit:
                    return tonight.CenteredStartUtc is { } cs
                        && tonight.CenteredEndUtc is { } ce
                        && tonight.CenteredFloor is { } cf
                        ? (cs, ce, cf)
                        : ((DateTime, DateTime, double)?)null;
                case DayChartMode.Floor:
                default:
                    return tonight.StartUtc is { } s
                        && tonight.EndUtc is { } e
                        && tonight.Floor is { } f
                        ? (s, e, f)
                        : ((DateTime, DateTime, double)?)null;
            }
        }

        // Update the green horizon line in place. Cheap; called from spinner ticks.
        public void UpdateHorizonLine(double horizon)
        {
            mHorizonLine.Yi = horizon;
            mHorizonLine.Yj = horizon;
        }

        // Update the red now-line position in place. The X axis is UTC-internal
        // so the now instant (already UTC) plots as its own OADate directly --
        // ToOADate ignores Kind.
        public void UpdateNowLine(DateTime nowUtc)
        {
            double oa = nowUtc.ToOADate();
            mNowLine.Xi = oa;
            mNowLine.Xj = oa;
        }

        public void Render(ChartContext ctx, IChartCacheStore cache,
            IProgress<(int Done, int Total)> progress = null)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            // Phase 7's short-circuit-on-eval-flags was reverted: LC2's paint
            // behaviour across hidden->visible Control transitions isn't
            // reliably stable even when Series/Values data is unchanged --
            // moon position/shape would visibly shift across Sky->Day toggles
            // even though the underlying data was identical. The perf saving
            // wasn't worth the visual regression. Render runs unconditionally;
            // sub-chart correctness now flows from cache-warm reads in the
            // body, not from skipping work at the entrance.
            if (Log.IsDiagEnabled("Day"))
            {
                Log.Diag("Day",
                    $"Render enter obs={ctx.Observation.Utc:yyyy-MM-dd HH:mm}Z " +
                    $"loc=({ctx.Location.Latitude:F3},{ctx.Location.Longitude:F3}) " +
                    $"targets={ctx.Targets?.Count ?? 0}");
            }
            if (ctx.Location == null) throw new ArgumentException("ctx.Location must not be null", nameof(ctx));
            if (ctx.Policy == null) throw new ArgumentException("ctx.Policy must not be null", nameof(ctx));

            Location location = ctx.Location;
            IReadOnlyList<Target> targets = ctx.Targets;
            double horizonFloor = ctx.Policy.TargetFloorDeg;
            DateTime now = ctx.Observation.Utc;

            NightWindow night = cache?.LocationNightCache?.Starting ?? NightCalculator.ComputeNight(location, now);
            if (!night.IsValid)
            {
                ClearAll();
                return;
            }

            TimeZoneInfo zone = ctx.Observation.Zone;
            mAxisZone = zone;
            var dayWindow = ChartLayout.BuildDayWindow(night, zone);
            DateTime startUtc = dayWindow.StartUtc;
            DateTime endUtc = dayWindow.EndUtc;
            int count = dayWindow.Count;
            DayWindowKey dayKey = dayWindow.Key;

            // Lock X axis to the night bounds so the HD overlay's null Y values
            // can't trigger LC2's auto-zoom-to-non-null-span behavior. The axis
            // is UTC-internal -- bounds are the OADate of the UTC start/end
            // instants. MinLimit / MaxLimit are nudged outward by
            // ChartLayout.LabelEdgeEpsilonDays (1 ms) so LC2's Ceil-based
            // first-tick math reliably places the edge tick at MinLimit's hour
            // rather than occasionally rounding up by a full step and silently
            // dropping the edge label.
            mXAxis.MinLimit = startUtc.ToOADate() - ChartLayout.LabelEdgeEpsilonDays;
            mXAxis.MaxLimit = endUtc.ToOADate() + ChartLayout.LabelEdgeEpsilonDays;

            // Gradient sections are UTC-anchored: dusk gradient spans
            // [startUtc, AstronomicalDusk], dawn gradient [AstronomicalDawn, endUtc].
            mGradient.Update(startUtc, night.AstronomicalDusk,
                             night.AstronomicalDawn, endUtc);
            UpdateNowLine(now);
            // Green horizon line follows the scalar TargetFloor spinner, not the polyline's
            // minimum sample -- the polyline drives per-azimuth fit decisions in the cache.
            UpdateHorizonLine(horizonFloor);

            // HD overlay preservation: when dayKey is unchanged (only targets/Hdm
            // shifted, not location/date), altitude data is identical so existing
            // backups remain valid -- PruneStaleBackups + RefreshActiveOverlays at
            // the end of Render updates them against any shifted windows. When
            // dayKey changes, altitude data is about to be replaced; existing
            // backups would reference stale Y values, so wipe them.
            bool dayKeyChanged = !dayKey.Equals(mLastDayKey);
            if (dayKeyChanged) mOverlay.ClearAll();
            mTargetWindows.Clear();
            mTargetColors.Clear();

            // Moon altitudes from cache (prepared by ChartCacheStore.EnsureAsync ->
            // PrepareMoonAsync alongside per-target trajectories); FetchOrCompute
            // falls back to inline compute if the cache misses.
            NightDate nightDate = NightDate.Of(night, ctx.Observation.Zone);
            IReadOnlyList<double> moonAltitudes = MoonOverlay.FetchOrCompute(
                cache, nightDate, location, startUtc, count, "Day");
            mMoonSeries = MoonOverlay.BuildSeries(
                moonAltitudes, startUtc, count, night.LunarIlluminationFraction,
                alt => alt, "Day");

            // "Fit tonight only" filter for Day: targets without a D-hour window
            // tonight are excluded from mChart.Series and the legend entirely.
            // Their altitude data is still sampled into mSeriesByTarget so a
            // subsequent H/D/M scrub that brings them back into fit re-admits
            // them on the next Render without recomputing altitudes (the
            // cached TargetDayAltitudeEntry is unchanged across H/D/M scrubs;
            // only the per-target fit decision flips).
            var newSeriesByTarget = new Dictionary<Target, LineSeries<ObservablePoint>>();
            var seriesList = new List<ISeries>();
            if (mMoonSeries != null) seriesList.Add(mMoonSeries);

            int dbgDayEntryNull = 0, dbgFitEntryNull = 0, dbgTonightFloorNull = 0, dbgWindowAdded = 0;
            for (int t = 0; t < targets.Count; t++)
            {
                progress?.Report((t + 1, targets.Count));
                Target target = targets[t];
                if (target == null) continue;

                Color c = ChartLayout.ResolveTargetColor(ctx.TargetColors, target, t);
                mTargetColors[target] = c;

                // Trajectory (per-minute AltAz) lives in the cache, keyed by
                // (target, NightDate). Coordinator's PrepareTrajectoryAsync
                // await guarantees it's published (modulo a raced location swap
                // -- GetTrajectoryOrNull returns null in that case and the
                // target is skipped silently). Day chart paints altitude only;
                // azimuth available in the same entry for future polyline
                // gating / sky-position consumers.
                TargetTrajectoryEntry trajEntry = cache?.GetTrajectoryOrNull(target, nightDate);
                if (trajEntry == null) { dbgDayEntryNull++; continue; }
                IReadOnlyList<AltAzSample> samples = trajEntry.Samples;

                var series = GetOrCreateTargetSeries(target, c);
                FillTargetSeriesData(series, startUtc, count, samples);

                // Tonight's fit lives on TargetFitEntry.Tonight (computed once at
                // BuildFitEntryAsync time); we read Start/End/Floor straight off
                // it -- no UI-thread BestSession.For call, byte-identical to the
                // pre-consolidation ad-hoc compute.
                var fitEntry = cache?.GetFitOrNull(target, ctx.Hdm);
                if (fitEntry == null) dbgFitEntryNull++;
                NightFit? tonight = fitEntry?.Tonight;
                var window = tonight is { } tn ? GetModeWindow(tn, ctx.DayMode) : null;
                if (window is { } w)
                {
                    ApplyTargetVisibility(series, c, true);
                    // UTC-internal axis: window endpoints plot as their own UTC
                    // OADate. OverlayController compares these bounds against the
                    // per-minute data-point Xs (also UTC OADate), so the frames
                    // match without any zone conversion here.
                    //
                    // transitOA carries the per-target upper-transit X for the
                    // overlay's downward tick decoration. Clipped to the window
                    // here so OverlayController never receives an out-of-window
                    // transit (the tick is meaningful only when it hangs from the
                    // floor bar, not in empty chart space).
                    double startOA = w.startUtc.ToOADate();
                    double endOA   = w.endUtc.ToOADate();
                    double? transitOA = null;
                    DateTime? transitUtc = tonight?.TransitUtc;
                    if (transitUtc.HasValue)
                    {
                        double tOA = transitUtc.Value.ToOADate();
                        if (tOA >= startOA && tOA <= endOA) transitOA = tOA;
                    }
                    mTargetWindows[series] = (startOA, endOA, w.floor, transitOA);
                    seriesList.Add(series);
                    dbgWindowAdded++;
                }
                else if (fitEntry != null)
                {
                    dbgTonightFloorNull++;
                }

                newSeriesByTarget[target] = series;
            }
            if (Log.IsDiagEnabled("Day"))
            {
                Log.Diag("Day",
                    $"Render target-filter targets={targets.Count} dayEntryNull={dbgDayEntryNull} " +
                    $"fitEntryNull={dbgFitEntryNull} tonightFloorNull={dbgTonightFloorNull} added={dbgWindowAdded} " +
                    $"hdmKey=(H={ctx.Hdm.HorizonDeg},Dt={ctx.Hdm.DurationTicks},F={ctx.Hdm.ActiveFilter?.Name ?? "(none)"},MoonOn={ctx.Hdm.MoonAvoidanceEnabled})");
            }

            ChartLayout.SwapSeriesDict(mSeriesByTarget, newSeriesByTarget);
            mChart.Series = seriesList;
            if (Log.IsDiagEnabled("Day"))
            {
                Log.Diag("Day",
                    $"Render exit mChart.Series.Count={seriesList.Count} moonPresent={mMoonSeries != null} " +
                    $"moonValuesCount={(mMoonSeries?.Values as System.Collections.ICollection)?.Count ?? -1} " +
                    $"moonFillAlpha={((mMoonSeries?.Fill as SolidColorPaint)?.Color.Alpha ?? 0)}");
            }
            BuildLegendItems();

            // HD overlay reconciliation. With ClearAll skipped (above) when dayKey
            // is unchanged, surviving backups need: (a) stale entries for removed
            // targets dropped, (b) preserved overlays re-rendered against the new
            // window bounds, (c) in global mode, overlay applied to newly-added
            // visible targets so "show windows for all" intent extends through add.
            // All three calls no-op when mBackups is empty (the dayKey-changed
            // path that hit ClearAll above), so this is safe in both cases.
            mOverlay.PruneStaleBackups(mSeriesByTarget.Values);
            mOverlay.RefreshActiveOverlays();
            if (mOverlay.IsGlobalMode) mOverlay.EnsureGlobalApplied();
            mLastDayKey = dayKey;
        }

        // Rebuild the external legend from the current target series.
        // "Fit tonight only" filter: targets without an entry in mTargetWindows
        // (no D-hour window fits tonight under current H/D/M) are excluded, so
        // the chart and the legend agree on what's visible tonight, independent
        // of which boxes are checked in CheckedListBox_SelectedTargets.
        private void BuildLegendItems()
        {
            var entries = new List<ChartLegendPanel.LegendEntry>();
            foreach (var kv in mSeriesByTarget)
            {
                Target target = kv.Key;
                LineSeries<ObservablePoint> series = kv.Value;
                if (!mTargetWindows.ContainsKey(series)) continue;
                Color color = mTargetColors.TryGetValue(target, out var c) ? c : Color.LightGray;
                entries.Add(new ChartLegendPanel.LegendEntry(
                    target.Name, color,
                    () => series.IsVisible,
                    () =>
                    {
                        series.IsVisible = !series.IsVisible;
                        // Mirror onto any active HD-overlay tick so the legend toggle
                        // hides/shows the floor bar's transit marker alongside the curve.
                        mOverlay.SyncTickVisibility(series, series.IsVisible);
                    }));
            }
            mLegend.SetItems(entries);
        }

        // Hide via fully-transparent stroke (zero alpha) when no D-hour window fits
        // tonight; restore the palette stroke when a window fits. Mirrors the legacy
        // RebuildDayTooltip's Color.Transparent / mSeriesColor toggle. Stroke width 2
        // matches the visible-curve build path.
        private static void ApplyTargetVisibility(
            LineSeries<ObservablePoint> series, Color color, bool hasWindow)
        {
            byte a = hasWindow ? color.A : (byte)0;
            series.Stroke = new SolidColorPaint(new SKColor(color.R, color.G, color.B, a), 2);
        }

        private void ClearAll()
        {
            mOverlay.ClearAll();
            mTargetWindows.Clear();
            mTargetColors.Clear();
            mSeriesByTarget.Clear();
            mMoonSeries = null;
            mChart.Series = Array.Empty<ISeries>();
            mLegend.Clear();
        }

        private void OnChartMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                mOverlay.ToggleAll();
                mChart.Invalidate();
                return;
            }
            if (e.Button != MouseButtons.Left) return;

            var clickData = mChart.ScalePixelsToData(new LvcPointD(e.X, e.Y));
            if (clickData.Y < MinAltitude || clickData.Y > MaxAltitude) return;

            // Inside the plot area: HD overlay hit-test. Legend clicks are
            // handled per-item in the external FlowLayoutPanel, not here.
            // Pixel coords flow through so the controller's sticky fast-path
            // can re-toggle the last target when the mouse hasn't moved.
            mOverlay.TryToggleAt(clickData.X, clickData.Y, e.X, e.Y);
            mChart.Invalidate();
        }

        private LineSeries<ObservablePoint> GetOrCreateTargetSeries(Target target, Color c)
        {
            if (mSeriesByTarget.TryGetValue(target, out var existing)) return existing;
            return new LineSeries<ObservablePoint>
            {
                Name = target.Name,
                Values = new ObservableCollection<ObservablePoint>(),
                Stroke = new SolidColorPaint(new SKColor(c.R, c.G, c.B, c.A), 2),
                Fill = null,
                GeometrySize = 0,
                LineSmoothness = 0.4,
            };
        }

        private static void FillTargetSeriesData(
            LineSeries<ObservablePoint> series,
            DateTime startUtc,
            int count,
            IReadOnlyList<AltAzSample> samples)
        {
            var data = series.Values as ObservableCollection<ObservablePoint>;
            if (data == null)
            {
                data = new ObservableCollection<ObservablePoint>();
                series.Values = data;
            }
            for (int i = 0; i < count; i++)
            {
                double alt = samples[i].AltDegGeometric;
                double? plotY = alt < 0 ? (double?)null : alt;
                // UTC-internal X axis: sample i is at startUtc + i minutes.
                var p = new ObservablePoint(startUtc.AddMinutes(i).ToOADate(), plotY);
                if (i < data.Count) data[i] = p;
                else data.Add(p);
            }
            while (data.Count > count) data.RemoveAt(data.Count - 1);
        }

        public void Dispose()
        {
            mChart.MouseDown -= OnChartMouseDown;
            mGradient.Dispose();
            mHover.Dispose();
            mContainer.Dispose();
        }
    }
}
