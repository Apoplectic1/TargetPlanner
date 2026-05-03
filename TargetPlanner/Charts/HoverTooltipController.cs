using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Windows.Forms;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.WinForms;

using LvcPointD = LiveChartsCore.Drawing.LvcPointD;

namespace TargetPlanner.Charts
{
    // Single-tooltip hover controller. Subscribes to the chart's MouseMove
    // + MouseLeave + a Timer; on tick, dispatches based on cursor data Y:
    //
    //  * Y in [0, 90] (plot area): finds the closest visible target curve
    //    via CurveHitTester and shows a tooltip whose text is built by the
    //    caller-supplied CurveTooltipFormatter (or a default smooth-curve
    //    interpolated formatter if null). Per-DataPoint snap formatters
    //    use segmentStart + cursor X to snap to the nearer endpoint and
    //    read data[snapped].Y directly.
    //  * Y < 0 (legend strip below the plot): uses LegendHitTester to find
    //    the legend item under the cursor, shows a tooltip with whatever
    //    text the caller-supplied legendTooltipFormatter returns.
    //
    // Both modes share a single WinForms ToolTip + Timer.
    public class HoverTooltipController : IDisposable
    {
        public const double MaxHoverDistanceDeg = 1.5;

        // Caller-supplied tooltip text builder for plot-area hover hits.
        public delegate string CurveTooltipFormatter(
            LineSeries<ObservablePoint> series,
            IList<ObservablePoint> data,
            double hoverX,
            double hoverY,
            double interpY,
            int segmentStart);

        private readonly CartesianChart mChart;
        private readonly Func<IEnumerable<LineSeries<ObservablePoint>>> mTargets;
        private readonly Func<ISeries, string> mLegendTooltipFormatter;
        private readonly CurveTooltipFormatter mCurveTooltipFormatter;
        private readonly System.Windows.Forms.ToolTip mTooltip = new System.Windows.Forms.ToolTip();
        private readonly System.Windows.Forms.Timer mTimer;
        private Point mLastMouseLoc;

        // Tracks which "thing" the tooltip is currently showing for, so the
        // tick can decide whether to update or hide. Either a curve series
        // (plot-area hover) or a legend item index (legend hover) — never
        // both. null means tooltip is hidden.
        private object mShownTarget;

        public HoverTooltipController(
            CartesianChart chart,
            Func<IEnumerable<LineSeries<ObservablePoint>>> targets,
            Func<ISeries, string> legendTooltipFormatter = null,
            CurveTooltipFormatter curveTooltipFormatter = null,
            int debounceMs = 300)
        {
            mChart = chart;
            mTargets = targets;
            mLegendTooltipFormatter = legendTooltipFormatter;
            mCurveTooltipFormatter = curveTooltipFormatter;
            // Smooth-curve interpolated tooltips look jittery if updated too
            // frequently as the cursor moves along a curve, so 300 ms is a
            // good "wait for the cursor to settle" value. Per-DataPoint snap
            // tooltips snap to discrete points and the user expects the
            // tooltip to track in near-real-time as the cursor walks across
            // data points — a much shorter interval (e.g. 30 ms) feels snappy
            // without flooding the UI thread with redundant Show calls when
            // the cursor stays within the same point's bucket.
            mTimer = new System.Windows.Forms.Timer { Interval = Math.Max(1, debounceMs) };
            mChart.MouseMove += OnChartMouseMove;
            mChart.MouseLeave += OnChartMouseLeave;
            mTimer.Tick += OnTimerTick;
        }

        public void Dispose()
        {
            mChart.MouseMove -= OnChartMouseMove;
            mChart.MouseLeave -= OnChartMouseLeave;
            mTimer.Tick -= OnTimerTick;
            mTimer.Dispose();
            mTooltip.Dispose();
        }

        private void OnChartMouseMove(object sender, MouseEventArgs e)
        {
            mLastMouseLoc = e.Location;
            // Restart the debounce — but DON'T hide the tooltip here. Hiding
            // on every MouseMove creates a hide/show flicker every debounce
            // cycle when the cursor is roughly stationary. The tick handler
            // decides whether to show, update, or hide.
            mTimer.Stop();
            mTimer.Start();
        }

        private void OnChartMouseLeave(object sender, EventArgs e)
        {
            mTimer.Stop();
            HideTooltip();
        }

        private void OnTimerTick(object sender, EventArgs e)
        {
            mTimer.Stop();

            var hover = mChart.ScalePixelsToData(new LvcPointD(mLastMouseLoc.X, mLastMouseLoc.Y));

            // Below the plot area: legend strip.
            if (hover.Y < 0)
            {
                ShowOrHideLegendTooltip();
                return;
            }
            // Above the plot area: title / no interactive content.
            if (hover.Y > 90)
            {
                HideTooltip();
                return;
            }
            // Inside the plot area: curve hit-test.
            ShowOrHideCurveTooltip(hover.X, hover.Y);
        }

        private void ShowOrHideCurveTooltip(double hoverX, double hoverY)
        {
            LineSeries<ObservablePoint> best = null;
            ObservableCollection<ObservablePoint> bestData = null;
            var bestDistance = double.MaxValue;
            var bestInterpY = 0.0;
            var bestSegmentStart = 0;
            foreach (var s in mTargets())
            {
                if (!s.IsVisible) continue;
                if (!(s.Values is ObservableCollection<ObservablePoint> data)) continue;
                var probe = CurveHitTester.At(data, p => p.X, p => p.Y, hoverX, hoverY);
                if (probe is null) continue;
                var dy = probe.Value.Distance;
                if (dy < bestDistance)
                {
                    bestDistance = dy;
                    best = s;
                    bestData = data;
                    bestInterpY = probe.Value.InterpY;
                    bestSegmentStart = probe.Value.SegmentStart;
                }
            }

            if (best is null || bestData is null || bestDistance > MaxHoverDistanceDeg)
            {
                HideTooltip();
                return;
            }

            var text = mCurveTooltipFormatter is not null
                ? mCurveTooltipFormatter(best, bestData, hoverX, hoverY, bestInterpY, bestSegmentStart)
                : DefaultInterpolatedTooltip(best, hoverX, bestInterpY);

            mTooltip.Show(text, mChart, mLastMouseLoc.X + 14, mLastMouseLoc.Y + 14, 4000);
            mShownTarget = best;
        }

        private static string DefaultInterpolatedTooltip(
            LineSeries<ObservablePoint> series, double hoverX, double interpY)
        {
            var time = DateTime.FromOADate(hoverX).ToString("h:mm tt");
            return $"{series.Name}\n{time}\nAltitude: {interpY:F1}°";
        }

        private void ShowOrHideLegendTooltip()
        {
            if (mLegendTooltipFormatter is null)
            {
                HideTooltip();
                return;
            }
            var hit = LegendHitTester.At(mChart, mLastMouseLoc.X);
            if (hit is null)
            {
                HideTooltip();
                return;
            }
            var text = mLegendTooltipFormatter(hit.Value.Series);
            if (string.IsNullOrEmpty(text))
            {
                HideTooltip();
                return;
            }
            mTooltip.Show(text, mChart, mLastMouseLoc.X + 14, mLastMouseLoc.Y + 14, 4000);
            mShownTarget = hit.Value.Index;
        }

        private void HideTooltip()
        {
            if (mShownTarget != null)
            {
                mTooltip.Hide(mChart);
                mShownTarget = null;
            }
        }
    }
}
