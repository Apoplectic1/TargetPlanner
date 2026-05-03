using System.Linq;
using System.Windows.Forms;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView.WinForms;

namespace TargetPlanner.Charts
{
    // Shared math for finding which legend entry is at a given pixel X
    // inside a CartesianChart's bottom-positioned legend. Both
    // LegendClickHandler (click-to-toggle) and HoverTooltipController
    // (hover-to-show-info) need to bucket a pixel coordinate to a series;
    // this is the canonical implementation.
    //
    // Approximation: LC2's legend entries are centered within the chart's
    // draw margin, with each item's pixel width roughly LegendMarkerWidth +
    // TextRenderer.MeasureText(name) + LegendItemPadding. Tunables exposed
    // as constants so callers (and any caller-facing diagnostics) share
    // the same values.
    public static class LegendHitTester
    {
        public const int LegendMarkerWidth = 40;
        public const int LegendItemPadding = 28;

        public static (int Index, ISeries Series)? At(CartesianChart chart, int pixelX)
        {
            var visible = chart.Series
                .Where(s => s.IsVisibleAtLegend)
                .ToList();
            if (visible.Count == 0) return null;

            var marginX = chart.CoreChart.DrawMarginLocation.X;
            var marginW = chart.CoreChart.DrawMarginSize.Width;
            if (marginW <= 0) return null;

            var widths = visible
                .Select(s => LegendMarkerWidth
                           + TextRenderer.MeasureText(s.Name ?? "", chart.Font).Width
                           + LegendItemPadding)
                .ToArray();
            var totalWidth = widths.Sum();
            var legendLeft = marginX + (marginW - totalWidth) / 2.0;

            var cursor = legendLeft;
            for (int i = 0; i < visible.Count; i++)
            {
                var next = cursor + widths[i];
                if (pixelX >= cursor && pixelX < next)
                    return (i, visible[i]);
                cursor = next;
            }
            return null;
        }
    }
}
