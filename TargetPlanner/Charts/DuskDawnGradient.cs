using System;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.WinForms;
using SkiaSharp;

namespace TargetPlanner.Charts
{
    // The dusk/dawn yellow gradient shared by the Day and Sky single-night
    // charts. Owns two RectangularSections; the host chart adds Dusk + Dawn to
    // its X-axis Sections array and calls Update(...) per Render with the
    // night's UTC bounds. The dusk gradient fades opaque-at-the-left-edge to
    // transparent-at-astronomical-dusk; the dawn gradient mirrors it on the
    // right. Extracted from byte-identical copies in AltitudeSubChart_Day/_Sky
    // (code-quality-audit.md Tier 4).
    public sealed class DuskDawnGradient : IDisposable
    {
        // Yellow gradient endpoints (matches the legacy MS Charts areas).
        private static readonly SKColor YellowOpaque = new SKColor(255, 238, 88, 145);
        private static readonly SKColor YellowFaded  = new SKColor(255, 238, 88,   0);

        private CartesianChart mChart;

        // Placeholder bounds; Update() rewrites Xi/Xj per the actual night.
        public RectangularSection Dusk { get; } = new RectangularSection { Xi = 0, Xj = 0 };
        public RectangularSection Dawn { get; } = new RectangularSection { Xi = 0, Xj = 0 };

        // Wire the host chart's SizeChanged so a horizontal resize re-resolves
        // the gradient shader. LC2 caches the shader at first paint; without
        // this the dawn gradient is progressively cut off as the chart widens.
        // Called after the host CartesianChart is constructed (its Sections
        // initializer already references Dusk / Dawn).
        public void WireSizeChanged(CartesianChart chart)
        {
            mChart = chart;
            chart.SizeChanged += OnChartSizeChanged;
        }

        public void Dispose()
        {
            if (mChart != null) mChart.SizeChanged -= OnChartSizeChanged;
        }

        // Position + repaint the two gradient sections for the night window
        // [startUtc, endUtc] -- dusk gradient runs startUtc->duskUtc, dawn
        // gradient dawnUtc->endUtc. All four are UTC instants (the Day/Sky X
        // axis is UTC-internal); the gradient math is purely relative fractions.
        public void Update(DateTime startUtc, DateTime duskUtc, DateTime dawnUtc, DateTime endUtc)
        {
            Dusk.Xi = startUtc.ToOADate();
            Dusk.Xj = duskUtc.ToOADate();
            Dawn.Xi = dawnUtc.ToOADate();
            Dawn.Xj = endUtc.ToOADate();

            // SKPoint coords for RectangularSection.Fill gradients are normalized
            // to the chart's plot area (NOT the section's bounds). So a section
            // of width W out of total night width T gets gradient endpoints from
            // 0 to W/T (dusk: opaque-left → faded-right) or 1-W/T to 1 (dawn).
            double total = (endUtc - startUtc).TotalMinutes;
            float duskFrac = (float)((duskUtc - startUtc).TotalMinutes / total);
            float dawnFrac = (float)((endUtc - dawnUtc).TotalMinutes / total);
            Dusk.Fill = new LinearGradientPaint(
                new[] { YellowOpaque, YellowFaded },
                new SKPoint(0f, 0.5f),
                new SKPoint(duskFrac, 0.5f));
            Dawn.Fill = new LinearGradientPaint(
                new[] { YellowFaded, YellowOpaque },
                new SKPoint(1f - dawnFrac, 0.5f),
                new SKPoint(1f, 0.5f));
        }

        // LC2 caches the gradient shader at first paint; re-running Update on
        // resize forces a fresh resolve. The section Xi/Xj round-trip is
        // frame-consistent (UTC OADate in, UTC OADate out).
        private void OnChartSizeChanged(object sender, EventArgs e)
        {
            if (!Dusk.Xi.HasValue || !Dusk.Xj.HasValue
                || !Dawn.Xi.HasValue || !Dawn.Xj.HasValue) return;
            if (Dusk.Xi.Value == 0 && Dusk.Xj.Value == 0) return;  // pre-render
            DateTime startUtc = DateTime.FromOADate(Dusk.Xi.Value);
            DateTime duskUtc  = DateTime.FromOADate(Dusk.Xj.Value);
            DateTime dawnUtc  = DateTime.FromOADate(Dawn.Xi.Value);
            DateTime endUtc   = DateTime.FromOADate(Dawn.Xj.Value);
            Update(startUtc, duskUtc, dawnUtc, endUtc);
        }
    }
}
