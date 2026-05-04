using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
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

using Location = Astronomy.Core.Locations.Location;
using Target   = Astronomy.Core.Targets.Target;

namespace TargetPlanner.Charts
{
    // LiveCharts2 implementation of TP's Year chart area. 12-month per-night max
    // altitude sweep -- one DataPoint per night sourced from the cache store's
    // pre-built TargetCacheEntry.YearDays. By the time Render(...) fires,
    // MainForm has already awaited mAltitudeChart.ReloadWithTargets which awaits
    // mCache.PrepareManyAsync, so the year cache is guaranteed populated for
    // every target in the render list (modulo a target whose build raced --
    // GetOrNull returns null in that case and the target is skipped).
    //
    // Owns one controller wired to its CartesianChart instance:
    //   - HoverTooltipController: per-DataPoint snap tooltip (30 ms debounce);
    //     custom formatter reads pre-formatted text from a parallel string[]
    //     populated during Render -- the user sees the actual max altitude for
    //     a hovered night, no interpolation.
    //
    // No OverlayController (no HD overlay on Year), no Moon series (the Year
    // sweep is target-vs-time geometry only), no dusk/dawn gradient (a 12-month
    // axis has no single sun-twilight context).
    public class AltitudeSubChart_Year : IDisposable
    {
        // Y axis bounds (altitude, degrees). 0-90 to match Day so the plot area
        // template stays uniform across radio swaps.
        public const double MinAltitude = 0.0;
        public const double MaxAltitude = 90.0;

        public Control Control { get; }
        private readonly Panel mContainer;
        private readonly CartesianChart mChart;
        private readonly FlowLayoutPanel mLegendPanel;

        private readonly Axis mXAxis;
        private readonly Axis mYAxis;
        private readonly RectangularSection mNowLine;
        private readonly RectangularSection mHorizonLine;

        private readonly Dictionary<Target, LineSeries<ObservablePoint>> mSeriesByTarget
            = new Dictionary<Target, LineSeries<ObservablePoint>>();

        private readonly Dictionary<Target, Color> mTargetColors
            = new Dictionary<Target, Color>();

        // Pre-formatted per-night tooltip text, keyed by series. The custom
        // CurveTooltipFormatter reads mTooltipText[series][segmentStart] to
        // surface the actual max-altitude / date pair rather than an
        // interpolated number that doesn't correspond to any real night.
        private readonly Dictionary<LineSeries<ObservablePoint>, string[]> mTooltipText
            = new Dictionary<LineSeries<ObservablePoint>, string[]>();

        // Per-target year-day cache snapshot stashed at Render so the
        // background visibility task can read it without going through the
        // cache store again. Captured by reference -- IReadOnlyList<NightCacheEntry>
        // is published immutable per the cache store's contract.
        private readonly Dictionary<Target, IReadOnlyList<NightCacheEntry>> mYearDaysByTarget
            = new Dictionary<Target, IReadOnlyList<NightCacheEntry>>();

        // Cancellation for the in-flight visibility task. Replaced (cancelling
        // the prior) on every RefreshYearVisibility call so a rapid scrub
        // doesn't queue stale work behind in-flight stale work.
        private CancellationTokenSource mVisibilityCts;

        // sin(altitude) airmass-weighted quality metric -- same probe Day uses.
        // Per-night fit decision: BestSession.For with this quality returns
        // null when no D-hour window fits; any non-null result counts as fit
        // and the night's YearAlt is plotted; null result becomes a line break.
        private static readonly Func<double, double> SinAltQuality =
            alt => Math.Sin(alt * Math.PI / 180.0);

        private readonly HoverTooltipController mHover;

        private int mLastIdealHeight = -1;
        public event EventHandler IdealHeightChanged;

        public int IdealHeight => ChartLayout.ChartFixedHeight + mLegendPanel.Height;

        public AltitudeSubChart_Year()
        {
            mXAxis = new Axis
            {
                Labeler = v => DateTime.FromOADate(v).ToString("MMM", CultureInfo.InvariantCulture),
                UnitWidth = TimeSpan.FromDays(30).TotalDays,
                MinStep = TimeSpan.FromDays(30).TotalDays,
                LabelsPaint = new SolidColorPaint(SKColors.LightGray),
                SeparatorsPaint = new SolidColorPaint(ChartLayout.GridLineColor),
            };
            mYAxis = new Axis
            {
                Name = "Maximum daily altitude (°)",
                MinLimit = MinAltitude,
                MaxLimit = MaxAltitude,
                MinStep = 10,
                ForceStepToMin = true,
                LabelsPaint = new SolidColorPaint(SKColors.LightGray),
                SeparatorsPaint = new SolidColorPaint(ChartLayout.GridLineColor),
                NamePaint = new SolidColorPaint(SKColors.LightGray),
            };

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
                Sections = new[] { mNowLine, mHorizonLine },
                Series = Array.Empty<ISeries>(),
                LegendPosition = LegendPosition.Hidden,
                FindingStrategy = FindingStrategy.ExactMatch,
                TooltipPosition = TooltipPosition.Hidden,
                BackColor = ChartLayout.ChartBackground,
                Dock = DockStyle.Top,
                Height = ChartLayout.ChartFixedHeight,
            };

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
            mContainer.Controls.Add(mLegendPanel);
            mContainer.Controls.Add(mChart);
            Control = mContainer;

            // Per-DataPoint snap tooltip: each night is a discrete data point,
            // 30 ms debounce, custom formatter surfaces pre-formatted text by
            // segmentStart so the user sees the actual date / max-altitude.
            mHover = new HoverTooltipController(
                mChart,
                () => mSeriesByTarget.Values,
                legendTooltipFormatter: null,
                curveTooltipFormatter: YearTooltipFormatter,
                debounceMs: 30);
        }

        // Update the green horizon line in place. Cheap; called from horizon
        // spinner ticks on the MainForm.
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

        private string YearTooltipFormatter(
            LineSeries<ObservablePoint> series,
            IList<ObservablePoint> data,
            double hoverX, double hoverY,
            double interpY,
            int segmentStart)
        {
            if (!mTooltipText.TryGetValue(series, out var arr)) return string.Empty;
            if (segmentStart < 0 || segmentStart >= arr.Length) return string.Empty;
            return arr[segmentStart] ?? string.Empty;
        }

        public void Render(
            IReadOnlyList<Target> targets,
            IChartCacheStore cache,
            MoonAvoidanceProfile profile,
            Location location,
            double horizon,
            TimeSpan duration,
            DateTime now,
            CancellationToken ct = default)
        {
            if (location == null) throw new ArgumentNullException(nameof(location));
            ct.ThrowIfCancellationRequested();

            UpdateHorizonLine(horizon);
            UpdateNowLine(now);

            mTargetColors.Clear();
            mYearDaysByTarget.Clear();

            // X axis bounds locked to the first / last SentinelX of any target's
            // year cache. All targets share the same year-day grid (cache is
            // keyed by Location, not by Target), so the first non-empty cache
            // entry is sufficient. If no targets have a cache yet, leave bounds
            // unset and LC2 auto-fits to the data.
            DateTime? gridStart = null;
            DateTime? gridEnd   = null;

            var newSeriesByTarget = new Dictionary<Target, LineSeries<ObservablePoint>>();
            var seriesList = new List<ISeries>();
            for (int t = 0; t < targets.Count; t++)
            {
                ct.ThrowIfCancellationRequested();
                Target target = targets[t];
                if (target == null) continue;

                TargetCacheEntry cacheEntry = cache?.GetOrNull(target);
                if (cacheEntry == null) continue;       // build raced; skip silently
                IReadOnlyList<NightCacheEntry> yearDays = cacheEntry.YearDays;
                if (yearDays == null || yearDays.Count == 0) continue;

                if (gridStart == null)
                {
                    gridStart = yearDays[0].SentinelX;
                    gridEnd   = yearDays[yearDays.Count - 1].SentinelX;
                }

                Color c = ChartLayout.TargetColorPalette[t % ChartLayout.TargetColorPalette.Length];
                mTargetColors[target] = c;
                mYearDaysByTarget[target] = yearDays;

                var series = GetOrCreateTargetSeries(target, c);
                BuildOrUpdateTargetSeries(series, target, yearDays, ct);

                newSeriesByTarget[target] = series;
                seriesList.Add(series);
            }

            if (gridStart.HasValue && gridEnd.HasValue)
            {
                mXAxis.MinLimit = gridStart.Value.ToOADate();
                mXAxis.MaxLimit = gridEnd.Value.ToOADate();
            }

            // Drop tooltip arrays for targets dropped from the render list.
            var dropped = mSeriesByTarget
                .Where(kv => !newSeriesByTarget.ContainsKey(kv.Key))
                .Select(kv => kv.Value)
                .ToList();
            foreach (var s in dropped) mTooltipText.Remove(s);

            mSeriesByTarget.Clear();
            foreach (var kv in newSeriesByTarget) mSeriesByTarget[kv.Key] = kv.Value;
            mChart.Series = seriesList;
            BuildLegendItems();

            // Kick off the per-night H/D/M fit pass on a background thread.
            // First paint already shows geometric-fit nights (polar / sub-
            // horizon nullified inline above); the H/D/M pass refines by
            // null-ing nights where BestSession.For returns null. Subsequent
            // scrubs cancel + restart this same task path.
            RefreshYearVisibility(profile, location, horizon, duration);

            RecomputeLayout();
        }

        // Recompute per-night fit under the current Horizon / Duration / Moon
        // profile and apply line breaks (null Y) on unfit nights. Runs the
        // BestSession.For probe per (target, night) on a background thread so
        // the UI stays responsive during a scrub. Replaces any in-flight task
        // (cancellation token) so a rapid spinner scrub doesn't queue stale
        // work behind in-flight stale work.
        //
        // Called from Render's tail (first-paint refinement) and from
        // MainForm.SessionsRebuildDebounce_Tick (live scrub refinement). No-op
        // when no targets are rendered.
        public void RefreshYearVisibility(
            MoonAvoidanceProfile profile,
            Location location,
            double horizon,
            TimeSpan duration)
        {
            if (location == null || mSeriesByTarget.Count == 0) return;

            // Cancel any in-flight task before starting a new one so the UI
            // marshalling continuation we attach below sees a fresh CTS state.
            mVisibilityCts?.Cancel();
            mVisibilityCts = new CancellationTokenSource();
            CancellationToken ct = mVisibilityCts.Token;

            // Snapshot inputs onto stack-local references so the background
            // task captures stable values even if the UI thread keeps mutating
            // mSeriesByTarget / mYearDaysByTarget for a subsequent Render.
            var snapshot = new List<(Target Target, LineSeries<ObservablePoint> Series, IReadOnlyList<NightCacheEntry> Days)>();
            foreach (var kv in mSeriesByTarget)
            {
                if (mYearDaysByTarget.TryGetValue(kv.Key, out var days))
                    snapshot.Add((kv.Key, kv.Value, days));
            }
            if (snapshot.Count == 0) return;

            IHorizonProfile horizonProfile = new ScalarHorizonProfile(horizon);
            TimeSpan dur = duration;
            MoonAvoidanceProfile profileCapture = profile;
            Location locationCapture = location;

            Task.Run(() =>
            {
                // Per-target, per-night bool[] -- true iff BestSession.For
                // returns non-null. Computed off the UI thread; the only
                // shared mutation comes via the BeginInvoke below.
                var perTarget = new List<(LineSeries<ObservablePoint> Series, IReadOnlyList<NightCacheEntry> Days, bool[] Fits)>();
                foreach (var (target, series, days) in snapshot)
                {
                    if (ct.IsCancellationRequested) return;

                    bool[] fits = new bool[days.Count];
                    for (int i = 0; i < days.Count; i++)
                    {
                        if (ct.IsCancellationRequested) return;
                        NightCacheEntry night = days[i];
                        if (night.IsPolar || night.YearAlt < 0)
                        {
                            fits[i] = false;
                            continue;
                        }
                        if (dur <= TimeSpan.Zero)
                        {
                            fits[i] = false;
                            continue;
                        }

                        NightWindow nw = new NightWindow
                        {
                            AstronomicalDusk = night.Dusk,
                            AstronomicalDawn = night.Dawn,
                            // LunarIlluminationFraction is consumed by Day's moon-curve
                            // alpha only; BestSession.For doesn't read it.
                            LunarIlluminationFraction = 0,
                        };
                        var best = BestSession.For(
                            target, locationCapture, nw, horizonProfile,
                            dur, dur, SinAltQuality, profile: profileCapture);
                        fits[i] = best != null;
                    }
                    perTarget.Add((series, days, fits));
                }

                if (ct.IsCancellationRequested) return;

                // Marshal the Y-value rewrite back to the UI thread. LC2
                // ObservableCollection mutation must happen on the thread that
                // owns the Control; BeginInvoke is the standard WinForms hop.
                if (mChart.IsHandleCreated)
                {
                    mChart.BeginInvoke(new Action(() =>
                    {
                        if (ct.IsCancellationRequested) return;
                        foreach (var (series, days, fits) in perTarget)
                        {
                            ApplyYearVisibility(series, days, fits);
                        }
                    }));
                }
            }, ct);
        }

        // UI-thread continuation: rewrite each data point's Y and tooltip
        // text to reflect the (geometric AND H/D/M) fit decision. Mutates
        // the existing ObservableCollection in place so series identity --
        // and the user's legend toggle state -- survives the refresh.
        private void ApplyYearVisibility(
            LineSeries<ObservablePoint> series,
            IReadOnlyList<NightCacheEntry> days,
            bool[] fits)
        {
            if (!mTooltipText.TryGetValue(series, out var tooltips)) return;
            if (!(series.Values is ObservableCollection<ObservablePoint> data)) return;

            // Find the target name for tooltip text. Reverse-lookup from
            // mSeriesByTarget; small dict walk on the UI thread, no contention.
            string targetName = "";
            foreach (var kv in mSeriesByTarget)
            {
                if (ReferenceEquals(kv.Value, series)) { targetName = kv.Key.Name; break; }
            }

            int count = Math.Min(Math.Min(data.Count, fits.Length), days.Count);
            for (int i = 0; i < count; i++)
            {
                NightCacheEntry night = days[i];
                bool baseFit = !night.IsPolar && night.YearAlt >= 0;
                bool effectiveFit = baseFit && fits[i];
                double? plotY = effectiveFit ? night.YearAlt : (double?)null;
                data[i] = new ObservablePoint(night.SentinelX.ToOADate(), plotY);

                if (i < tooltips.Length)
                {
                    if (night.IsPolar)
                        tooltips[i] = string.Format(CultureInfo.InvariantCulture,
                            "{0}\n{1:MMM dd, yyyy}\n(polar period)",
                            targetName, night.SentinelX);
                    else if (night.YearAlt < 0)
                        tooltips[i] = string.Format(CultureInfo.InvariantCulture,
                            "{0}\n{1:MMM dd, yyyy}\n(target never above horizon)",
                            targetName, night.SentinelX);
                    else if (!fits[i])
                        tooltips[i] = string.Format(CultureInfo.InvariantCulture,
                            "{0}\n{1:MMM dd, yyyy}\n(no fit at current Horizon / Duration / Moon)",
                            targetName, night.SentinelX);
                    else
                        tooltips[i] = string.Format(CultureInfo.InvariantCulture,
                            "{0}\n{1:MMM dd, yyyy}\nMax altitude: {2:0.0}°",
                            targetName, night.SentinelX, night.YearAlt);
                }
            }
        }

        private void BuildOrUpdateTargetSeries(
            LineSeries<ObservablePoint> series,
            Target target,
            IReadOnlyList<NightCacheEntry> yearDays,
            CancellationToken ct)
        {
            var data = series.Values as ObservableCollection<ObservablePoint>;
            if (data == null)
            {
                data = new ObservableCollection<ObservablePoint>();
                series.Values = data;
            }

            int count = yearDays.Count;
            string[] tooltips = new string[count];
            for (int i = 0; i < count; i++)
            {
                ct.ThrowIfCancellationRequested();
                NightCacheEntry night = yearDays[i];

                // null Y for polar nights and nights where the target never
                // rises above the mathematical horizon -- LC2 renders nullable
                // points as line breaks, which is more honest than dropping the
                // line to 0° (where it would visually merge with the bottom
                // edge or below the imaging horizon and confuse "this night
                // has data" with "this night doesn't").
                double? plotY = (night.IsPolar || night.YearAlt < 0)
                    ? (double?)null
                    : night.YearAlt;
                var p = new ObservablePoint(night.SentinelX.ToOADate(), plotY);
                if (i < data.Count) data[i] = p;
                else data.Add(p);

                tooltips[i] = night.IsPolar
                    ? string.Format(CultureInfo.InvariantCulture,
                        "{0}\n{1:MMM dd, yyyy}\n(polar period)",
                        target.Name, night.SentinelX)
                    : night.YearAlt < 0
                    ? string.Format(CultureInfo.InvariantCulture,
                        "{0}\n{1:MMM dd, yyyy}\n(target never above horizon)",
                        target.Name, night.SentinelX)
                    : string.Format(CultureInfo.InvariantCulture,
                        "{0}\n{1:MMM dd, yyyy}\nMax altitude: {2:0.0}°",
                        target.Name, night.SentinelX, night.YearAlt);
            }
            while (data.Count > count) data.RemoveAt(data.Count - 1);

            mTooltipText[series] = tooltips;
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

        private void BuildLegendItems()
        {
            mLegendPanel.SuspendLayout();
            mLegendPanel.Controls.Clear();
            foreach (var kv in mSeriesByTarget)
            {
                Target target = kv.Key;
                LineSeries<ObservablePoint> series = kv.Value;
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
                ForeColor = series.IsVisible ? Color.LightGray : Color.DimGray,
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

        public void Dispose()
        {
            mVisibilityCts?.Cancel();
            mVisibilityCts?.Dispose();
            mHover.Dispose();
            mContainer.Dispose();
        }
    }
}
