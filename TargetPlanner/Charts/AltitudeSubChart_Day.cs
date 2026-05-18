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

        // Yellow gradient endpoints for dusk/dawn sections (matches MS Charts side).
        private static readonly SKColor YellowOpaque = new SKColor(255, 238, 88, 145);
        private static readonly SKColor YellowFaded  = new SKColor(255, 238, 88,   0);

        // The label-edge epsilon used to dodge LC2's Ceil/Floor edge-tick
        // floating-point sensitivity lives in ChartLayout (shared with Sky).

        // The Container hosts (top) the CartesianChart at fixed height + (bottom) a
        // FlowLayoutPanel hosting custom legend items that wrap as targets grow.
        // MainForm adds Container to Panel_AltitudeChart and resizes Panel +
        // GroupBox + Form to match Container's IdealHeight on legend changes.
        public Control Control { get; }
        private readonly Panel mContainer;
        private readonly CartesianChart mChart;
        private readonly FlowLayoutPanel mLegendPanel;

        // Chart-furniture state preserved across Render calls. Sections /
        // Axes objects are mutated in place; only Series can be re-listed.
        private readonly Axis mXAxis;
        private readonly Axis mYAxis;
        private readonly RectangularSection mDuskSection;
        private readonly RectangularSection mDawnSection;
        private readonly RectangularSection mNowLine;
        private readonly RectangularSection mHorizonLine;

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
        // the LineSeries it found via hit-test.
        private readonly Dictionary<LineSeries<ObservablePoint>, (double startOA, double endOA, double floor)>
            mTargetWindows = new Dictionary<LineSeries<ObservablePoint>, (double, double, double)>();

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
        private readonly RadioButton mFloorRadio;
        private readonly RadioButton mTransitRadio;
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

        // Cached IdealHeight from the last layout pass; used to detect changes so
        // the IdealHeightChanged event only fires when the form actually needs to
        // resize.
        private int mLastIdealHeight = -1;

        // Raised when the chart's IdealHeight changes (legend wrap count moved).
        // MainForm subscribes and resizes Panel_AltitudeChart + GroupBox_Altitude +
        // Form by the delta so the plot area stays in a fixed pixel position
        // regardless of target count.
        public event EventHandler IdealHeightChanged;

        // Total Container height = fixed chart height + the FlowLayoutPanel's
        // preferred height for its current legend items. Legend panel grows in
        // height as targets are added (FlowLayoutPanel auto-wraps).
        // With FlowLayoutPanel.Dock=Top + AutoSize=true, the panel's Height
        // auto-tracks its content after each layout pass. Container.IdealHeight
        // is just chart fixed height + that current Height.
        public int IdealHeight => ChartLayout.ChartFixedHeight + mLegendPanel.Height;

        public AltitudeSubChart_Day()
        {
            mXAxis = new Axis
            {
                Labeler = v => DateTime.FromOADate(v).ToString("h:mm tt"),
                UnitWidth = TimeSpan.FromHours(1).TotalDays,
                MinStep = TimeSpan.FromHours(1).TotalDays,
                // ForceStepToMin disables LC2's adaptive label-skip density logic,
                // which would otherwise occasionally drop the leftmost/rightmost
                // hour label when chart width vs. label width tips into the skip
                // branch. ChartStart/ChartStop always land on exact hour
                // boundaries (per ChartLayout.DayChartStart/Stop), so we want
                // every hour labeled regardless of pixel-density estimates.
                ForceStepToMin = true,
                LabelsPaint = new SolidColorPaint(SKColors.LightGray),
                SeparatorsPaint = new SolidColorPaint(ChartLayout.GridLineColor),
            };
            mYAxis = new Axis
            {
                Name = "Altitude (°)",
                MinLimit = MinAltitude,
                MaxLimit = MaxAltitude,
                MinStep = 10,
                ForceStepToMin = true,
                LabelsPaint = new SolidColorPaint(SKColors.LightGray),
                SeparatorsPaint = new SolidColorPaint(ChartLayout.GridLineColor),
                NamePaint = new SolidColorPaint(SKColors.LightGray),
            };

            // Initialize section objects with placeholder bounds; Render() rewrites
            // Xi/Xj/Yi/Yj per the actual night window.
            mDuskSection = new RectangularSection { Xi = 0, Xj = 0 };
            mDawnSection = new RectangularSection { Xi = 0, Xj = 0 };
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
                Sections = new[] { mDuskSection, mDawnSection, mNowLine, mHorizonLine },
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

            mLegendPanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                BackColor = ChartLayout.ChartBackground,
                Padding = new Padding(
                    ChartLayout.LeftChromePx, ChartLayout.LegendTopPaddingPx,
                    ChartLayout.RightChromePx, ChartLayout.LegendBottomPaddingPx),
            };

            mContainer = new Panel
            {
                BackColor = ChartLayout.ChartBackground,
                Dock = DockStyle.Fill,
            };
            // Order matters for Dock=Top stacking: the LAST control added docks
            // FIRST. Add legend first (lower z-order), then chart (higher z-order)
            // so chart claims the top region and legend docks below it.
            mContainer.Controls.Add(mLegendPanel);
            mContainer.Controls.Add(mChart);
            Control = mContainer;

            // Per-instance controllers wired to mChart (the CartesianChart inside
            // the container). Hover uses interpolated mode (300 ms) because Day
            // altitude curves are smooth.
            mOverlay = new OverlayController(
                mChart,
                () => mSeriesByTarget.Values,
                series => mTargetWindows.TryGetValue(series, out var w)
                    ? ((double, double, double)?)w
                    : null,
                _ => { });
            mHover = new HoverTooltipController(
                mChart,
                () => mSeriesByTarget.Values,
                curveTooltipFormatter: null,
                debounceMs: 300);

            mChart.MouseDown += OnChartMouseDown;
            mChart.SizeChanged += OnChartSizeChanged;

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
            mFloorRadio   = MakeModeRadio("Floor",   DayChartMode.Floor,   isChecked: true);
            mTransitRadio = MakeModeRadio("Transit", DayChartMode.Transit, isChecked: false);
            int radioY = ChartLayout.TopChromePx + 2;
            int radioX = ChartLayout.LeftChromePx + 5;
            mFloorRadio.Location   = new Point(radioX,       radioY);
            mTransitRadio.Location = new Point(radioX + 60,  radioY);
            mContainer.Controls.Add(mFloorRadio);
            mContainer.Controls.Add(mTransitRadio);
            mFloorRadio.BringToFront();
            mTransitRadio.BringToFront();
            mSuppressModeChangedEvent = false;
        }

        // Build one of the three placement-strategy radios. Dark theme to blend
        // into the chart background; AutoSize keeps label width tight regardless
        // of font metrics. The Tag holds the DayChartMode value so the shared
        // CheckedChanged handler can route without per-radio handlers.
        private RadioButton MakeModeRadio(string label, DayChartMode mode, bool isChecked)
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

        // Update the red now-line position in place.
        public void UpdateNowLine(DateTime now)
        {
            double oa = now.ToOADate();
            mNowLine.Xi = oa;
            mNowLine.Xj = oa;
        }

        public void Render(ChartContext ctx, IChartCacheStore cache, ChartEvaluation eval)
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
                    $"targets={ctx.Targets?.Count ?? 0} LocationChanged={eval?.LocationChanged} " +
                    $"TargetsChanged={eval?.TargetsChanged} HdmChanged={eval?.HdmChanged}");
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

            var dayWindow = ChartLayout.BuildDayWindow(night);
            DateTime chartStart = dayWindow.ChartStart;
            DateTime chartStop = dayWindow.ChartStop;
            DateTime startUtc = dayWindow.StartUtc;
            int count = dayWindow.Count;
            DayWindowKey dayKey = dayWindow.Key;
            DateTime duskLocal = night.AstronomicalDusk.ToLocalTime();
            DateTime dawnLocal = night.AstronomicalDawn.ToLocalTime();

            // Lock X axis to the night bounds so the HD overlay's null Y values
            // can't trigger LC2's auto-zoom-to-non-null-span behavior. MinLimit
            // / MaxLimit are nudged outward by ChartLayout.LabelEdgeEpsilonDays
            // (1 ms) so LC2's Ceil-based first-tick math reliably places the
            // edge tick at MinLimit's hour rather than occasionally rounding up
            // by a full step and silently dropping the edge label.
            mXAxis.MinLimit = chartStart.ToOADate() - ChartLayout.LabelEdgeEpsilonDays;
            mXAxis.MaxLimit = chartStop.ToOADate() + ChartLayout.LabelEdgeEpsilonDays;

            UpdateGradientSections(chartStart, duskLocal, dawnLocal, chartStop);
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
            // PrepareMoonAsync alongside per-target Day altitudes). Defensive
            // fallback to inline compute if the cache misses (race-condition
            // safety net; should never fire in practice).
            IReadOnlyList<double> moonAltitudes = cache?.GetMoonOrNull(dayKey)?.AltitudesPerMinute;
            if (moonAltitudes == null || moonAltitudes.Count != count)
            {
                Log.Warn($"Day moon cache miss; inline fallback (dayKey.Count={count}, cached={moonAltitudes?.Count ?? -1})");
                moonAltitudes = ComputeMoonAltitudesInline(location, startUtc, count);
            }
            BuildOrUpdateMoonSeries(moonAltitudes, chartStart, count, night.LunarIlluminationFraction);

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
                Target target = targets[t];
                if (target == null) continue;

                Color c = ChartLayout.ResolveTargetColor(ctx.TargetColors, target, t);
                mTargetColors[target] = c;

                // Altitude curve lives in the cache, keyed by (target, dayKey).
                // Coordinator's PrepareDayAsync await guarantees it's published
                // (modulo a raced location swap -- GetDayOrNull returns null in
                // that case and the target is skipped silently).
                TargetDayAltitudeEntry dayEntry = cache?.GetDayOrNull(target, dayKey);
                if (dayEntry == null) { dbgDayEntryNull++; continue; }
                IReadOnlyList<double> altitudes = dayEntry.AltitudesPerMinute;

                var series = GetOrCreateTargetSeries(target, c);
                FillTargetSeriesData(series, chartStart, count, altitudes);

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
                    mTargetWindows[series] = (
                        w.startUtc.ToLocalTime().ToOADate(),
                        w.endUtc.ToLocalTime().ToOADate(),
                        w.floor);
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
                    $"hdmKey=(H={ctx.Hdm.HorizonDeg},Dt={ctx.Hdm.DurationTicks},FNm={ctx.Hdm.FilterCenterNm})");
            }

            mSeriesByTarget.Clear();
            foreach (var kv in newSeriesByTarget) mSeriesByTarget[kv.Key] = kv.Value;
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

            RecomputeLayout();
        }

        // Rebuild the external legend FlowLayoutPanel from the current target
        // series collection. Each item is a small Panel with a color marker +
        // target-name Label; click toggles the corresponding LineSeries.IsVisible.
        // FlowLayoutPanel auto-wraps to multiple rows as the legend grows.
        //
        // "Fit tonight only" filter: targets without an entry in mTargetWindows
        // (no D-hour window fits tonight under current H/D/M) are excluded from
        // the legend entirely. Mirrors the mChart.Series filtering in Render --
        // the chart and the legend agree on what's visible tonight, independent
        // of which boxes are checked in CheckedListBox_SelectedTargets.
        private void BuildLegendItems()
        {
            mLegendPanel.SuspendLayout();
            mLegendPanel.Controls.Clear();
            foreach (var kv in mSeriesByTarget)
            {
                Target target = kv.Key;
                LineSeries<ObservablePoint> series = kv.Value;
                if (!mTargetWindows.ContainsKey(series)) continue;
                Color color = mTargetColors.TryGetValue(target, out var c) ? c : Color.LightGray;
                mLegendPanel.Controls.Add(MakeLegendItem(series, target, color));
            }
            mLegendPanel.ResumeLayout(performLayout: true);
        }

        private Control MakeLegendItem(
            LineSeries<ObservablePoint> series, Target target, Color color)
        {
            const int markerWidth = 18;
            const int markerHeight = 4;
            const int markerLabelGap = 6;

            var label = new Label
            {
                AutoSize = true,
                ForeColor = Color.LightGray,
                BackColor = ChartLayout.ChartBackground,
                Padding = new Padding(markerWidth + markerLabelGap, 2, 12, 2),
                Margin = new Padding(0, 0, 4, 2),
                Text = target.Name,
                Cursor = Cursors.Hand,
            };
            label.Paint += (s, e) =>
            {
                int y = (label.Height - markerHeight) / 2;
                using (var brush = new SolidBrush(color))
                {
                    e.Graphics.FillRectangle(brush, 0, y, markerWidth, markerHeight);
                }
            };
            label.Click += (s, e) =>
            {
                series.IsVisible = !series.IsVisible;
                label.ForeColor = series.IsVisible ? Color.LightGray : Color.DimGray;
                // Reassigning Series forces LC2 to re-iterate and re-evaluate
                // IsVisible. Plain Invalidate() repaints the cached layout but
                // doesn't pick up IsVisible changes on existing series.
                mChart.Series = mChart.Series.ToList();
                mChart.Invalidate();
            };
            return label;
        }

        private void RecomputeLayout()
        {
            int idealHeight = IdealHeight;
            if (idealHeight != mLastIdealHeight)
            {
                mLastIdealHeight = idealHeight;
                IdealHeightChanged?.Invoke(this, EventArgs.Empty);
            }
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
            mLegendPanel.Controls.Clear();
        }

        // Recreate gradient Fills sized to the actual dusk/dawn widths. LC2 caches
        // shaders per-Section; calling this every Render keeps the gradient correctly
        // sized when the night window changes (Location / DateTime edits).
        private void UpdateGradientSections(
            DateTime chartStart, DateTime duskLocal, DateTime dawnLocal, DateTime chartStop)
        {
            mDuskSection.Xi = chartStart.ToOADate();
            mDuskSection.Xj = duskLocal.ToOADate();
            mDawnSection.Xi = dawnLocal.ToOADate();
            mDawnSection.Xj = chartStop.ToOADate();

            // SKPoint coords for RectangularSection.Fill gradients are normalized
            // to the chart's plot area (NOT the section's bounds). So a section
            // of width W out of total night width T gets gradient endpoints from
            // 0 to W/T (dusk: opaque-left → faded-right) or 1-W/T to 1 (dawn).
            double total = (chartStop - chartStart).TotalMinutes;
            float duskFrac = (float)((duskLocal - chartStart).TotalMinutes / total);
            float dawnFrac = (float)((chartStop - dawnLocal).TotalMinutes / total);
            mDuskSection.Fill = new LinearGradientPaint(
                new[] { YellowOpaque, YellowFaded },
                new SKPoint(0f, 0.5f),
                new SKPoint(duskFrac, 0.5f));
            mDawnSection.Fill = new LinearGradientPaint(
                new[] { YellowFaded, YellowOpaque },
                new SKPoint(1f - dawnFrac, 0.5f),
                new SKPoint(1f, 0.5f));
        }

        private void OnChartSizeChanged(object sender, EventArgs e)
        {
            // LC2 caches the gradient shader at first paint; horizontal resize
            // would otherwise leave the dawn gradient progressively cut off.
            // Re-assigning Fill forces a fresh shader resolve.
            if (!mDuskSection.Xi.HasValue || !mDuskSection.Xj.HasValue
                || !mDawnSection.Xi.HasValue || !mDawnSection.Xj.HasValue) return;
            if (mDuskSection.Xi.Value == 0 && mDuskSection.Xj.Value == 0) return;  // pre-render
            DateTime chartStart = DateTime.FromOADate(mDuskSection.Xi.Value);
            DateTime duskLocal  = DateTime.FromOADate(mDuskSection.Xj.Value);
            DateTime dawnLocal  = DateTime.FromOADate(mDawnSection.Xi.Value);
            DateTime chartStop  = DateTime.FromOADate(mDawnSection.Xj.Value);
            UpdateGradientSections(chartStart, duskLocal, dawnLocal, chartStop);
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

        // Build the shared Moon-Day filled area series from a pre-computed
        // altitude array (sourced from the per-DayWindowKey moon cache). The
        // Y axis is Day's altitude [0, 90]; below-horizon points get null Y so
        // the fill gaps where the moon is down.
        //
        // Recreate fresh every Render so LC2 sees a brand-new series instance --
        // simpler than in-place mutation and avoids subtle reuse bugs. The
        // first-Sky->Day moon-absent paint bug we chased here was actually a
        // WinForms/SKControl paint-cycle issue fixed by Control.Refresh() in
        // MainForm.RenderArea, not by anything in this series construction.
        private void BuildOrUpdateMoonSeries(
            IReadOnlyList<double> altitudes,
            DateTime chartStart,
            int count,
            double lunarIllumination)
        {
            byte alpha = (byte)Math.Min(250, Math.Max(0, (int)(lunarIllumination * 250.0)));

            int aboveHorizon = 0;
            double minAlt = double.PositiveInfinity, maxAlt = double.NegativeInfinity;
            var data = new ObservableCollection<ObservablePoint>();
            for (int i = 0; i < count; i++)
            {
                double moonAlt = altitudes[i];
                if (moonAlt > 0) aboveHorizon++;
                if (moonAlt < minAlt) minAlt = moonAlt;
                if (moonAlt > maxAlt) maxAlt = moonAlt;
                double? plotY = moonAlt < 0 ? (double?)null : moonAlt;
                DateTime point = chartStart.AddMinutes(i);
                data.Add(new ObservablePoint(point.ToOADate(), plotY));
            }

            mMoonSeries = new LineSeries<ObservablePoint>
            {
                Name = "Moon",
                Values = data,
                Stroke = null,
                Fill = new SolidColorPaint(new SKColor(209, 209, 209, alpha)),
                GeometrySize = 0,
                LineSmoothness = 0.4,
                IsVisibleAtLegend = false,
                ZIndex = -1,
            };

            if (Log.IsDiagEnabled("Day"))
            {
                Log.Diag("Day",
                    $"BuildMoon illum={lunarIllumination:F3} alpha={alpha} count={count} " +
                    $"aboveHorizon={aboveHorizon} minAlt={minAlt:F2} maxAlt={maxAlt:F2} " +
                    $"chartStart={chartStart:yyyy-MM-dd HH:mm}");
            }
        }

        // Defensive fallback when the moon cache misses (e.g. a race where
        // Render runs before PrepareMoonAsync's await settled). Matches
        // ChartCacheStore.BuildMoonEntryAsync's compute path so the result is
        // byte-identical to the cached version.
        private static IReadOnlyList<double> ComputeMoonAltitudesInline(
            Location location, DateTime startUtc, int count)
        {
            double latSigned = location.LatSigned();
            double lonEast   = location.LonEast();
            ObserverInfo observer = new ObserverInfo(latSigned, lonEast, location.Elevation);
            double[] altitudes = new double[count];
            for (int i = 0; i < count; i++)
            {
                DateTime pointUtc = DateTime.SpecifyKind(
                    startUtc.AddMinutes(i), DateTimeKind.Utc);
                altitudes[i] = AstroUtil.GetMoonAltitude(pointUtc, observer);
            }
            return altitudes;
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
            DateTime chartStart,
            int count,
            IReadOnlyList<double> altitudes)
        {
            var data = series.Values as ObservableCollection<ObservablePoint>;
            if (data == null)
            {
                data = new ObservableCollection<ObservablePoint>();
                series.Values = data;
            }
            for (int i = 0; i < count; i++)
            {
                double alt = altitudes[i];
                double? plotY = alt < 0 ? (double?)null : alt;
                var p = new ObservablePoint(chartStart.AddMinutes(i).ToOADate(), plotY);
                if (i < data.Count) data[i] = p;
                else data.Add(p);
            }
            while (data.Count > count) data.RemoveAt(data.Count - 1);
        }

        public void Dispose()
        {
            mChart.MouseDown -= OnChartMouseDown;
            mChart.SizeChanged -= OnChartSizeChanged;
            mHover.Dispose();
            mContainer.Dispose();
        }
    }
}
