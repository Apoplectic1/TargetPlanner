using System;
using LiveChartsCore.SkiaSharpView.WinForms;

namespace TargetPlanner.Charts
{
    // Manual legend hit-test for LC2 v2.1.0-dev-365 WinForms — the built-in
    // legend at LegendPosition.Bottom paints fine but doesn't fire
    // Series.IsVisible toggles on click. Delegates the pixel→series math
    // to LegendHitTester (shared with HoverTooltipController), focuses
    // solely on the toggle action and status reporting.
    public class LegendClickHandler
    {
        private readonly CartesianChart mChart;
        private readonly Action<string> mReportStatus;

        public LegendClickHandler(CartesianChart chart, Action<string> reportStatus)
        {
            mChart = chart;
            mReportStatus = reportStatus;
        }

        public void HandleClick(int pixelX)
        {
            var hit = LegendHitTester.At(mChart, pixelX);
            if (hit is null)
            {
                mReportStatus?.Invoke($"Legend: click missed (pxX={pixelX})");
                return;
            }
            var series = hit.Value.Series;
            series.IsVisible = !series.IsVisible;
            mReportStatus?.Invoke($"Legend: '{series.Name}' = {(series.IsVisible ? "ON" : "OFF")}  (idx={hit.Value.Index} pxX={pixelX})");
            mChart.Invalidate();
        }
    }
}
