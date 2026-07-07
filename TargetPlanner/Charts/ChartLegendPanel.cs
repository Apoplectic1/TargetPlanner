using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using LiveChartsCore.SkiaSharpView.WinForms;

namespace TargetPlanner.Charts
{
    // The external clickable legend shared by the four sub-charts: a
    // FlowLayoutPanel of per-target colour-swatch labels that wrap as the
    // target set grows. Each sub-chart calls SetItems(...) from Render with one
    // LegendEntry per row; clicking a row runs the entry's Toggle, repaints the
    // chart, and dims/brightens the label. Owns IdealHeight + IdealHeightChanged
    // (the form resizes when the legend wraps to a new row). Extracted from
    // near-identical — and drifted — copies in the four AltitudeSubChart_*.cs
    // (docs/2026-05-19-code-quality-audit.md Tier 4).
    public sealed class ChartLegendPanel
    {
        // One legend row. Toggle flips the underlying series visibility — one
        // series for Day/Sky/Year, three together for Sessions; IsVisible
        // reports current state for the row's dim (hidden) / bright (shown)
        // colour. The 1-vs-3 difference lives entirely in the caller's entry.
        public readonly record struct LegendEntry(
            string Name, Color Color, Func<bool> IsVisible, Action Toggle);

        private const int MarkerWidth = 18;
        private const int MarkerHeight = 4;
        private const int MarkerLabelGap = 6;

        private readonly CartesianChart mChart;
        private readonly FlowLayoutPanel mPanel;
        // Cached IdealHeight; IdealHeightChanged fires only on a real change so
        // the form resizes only when the legend's wrapped row count moves.
        private int mLastIdealHeight = -1;

        // The FlowLayoutPanel — the host docks this below its CartesianChart.
        public Control Panel => mPanel;

        // Raised when IdealHeight changes (legend wrap count moved).
        public event EventHandler IdealHeightChanged;

        // Fixed chart height + the legend's current wrapped height.
        public int IdealHeight => ChartLayout.ChartFixedHeight + mPanel.Height;

        public ChartLegendPanel(CartesianChart chart)
        {
            mChart = chart;
            mPanel = new FlowLayoutPanel
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
        }

        // Rebuild the legend from the given entries (the caller has already
        // applied any fit filter). Fires IdealHeightChanged if the wrapped
        // height changed.
        public void SetItems(IEnumerable<LegendEntry> entries)
        {
            mPanel.SuspendLayout();
            mPanel.Controls.Clear();
            foreach (LegendEntry entry in entries)
                mPanel.Controls.Add(MakeLegendItem(entry));
            mPanel.ResumeLayout(performLayout: true);

            int idealHeight = IdealHeight;
            if (idealHeight != mLastIdealHeight)
            {
                mLastIdealHeight = idealHeight;
                IdealHeightChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        // Drop all legend rows (host ClearAll path).
        public void Clear() => SetItems(Array.Empty<LegendEntry>());

        private Control MakeLegendItem(LegendEntry entry)
        {
            var label = new Label
            {
                AutoSize = true,
                ForeColor = entry.IsVisible() ? Color.LightGray : Color.DimGray,
                BackColor = ChartLayout.ChartBackground,
                Padding = new Padding(MarkerWidth + MarkerLabelGap, 2, 12, 2),
                Margin = new Padding(0, 0, 4, 2),
                Text = entry.Name,
                Cursor = Cursors.Hand,
            };
            Color markerColor = entry.Color;
            label.Paint += (s, e) =>
            {
                int y = (label.Height - MarkerHeight) / 2;
                using (var brush = new SolidBrush(markerColor))
                    e.Graphics.FillRectangle(brush, 0, y, MarkerWidth, MarkerHeight);
            };
            label.Click += (s, e) =>
            {
                entry.Toggle();
                label.ForeColor = entry.IsVisible() ? Color.LightGray : Color.DimGray;
                // Re-list Series so LC2 picks up the visibility flip, then repaint.
                mChart.Series = mChart.Series.ToList();
                mChart.Invalidate();
            };
            return label;
        }
    }
}
