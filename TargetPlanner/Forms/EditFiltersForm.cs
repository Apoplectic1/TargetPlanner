using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using TargetPlanner.Filters;

namespace TargetPlanner.Forms
{
    // Modal editor for the per-filter library. Lets the user add / remove / rename
    // filters and edit their per-filter Lorentzian / relaxation defaults plus
    // bandwidth. Save writes through to the supplied FilterLibrary and persists to
    // %APPDATA%\TargetPlanner\filters.json; Cancel discards.
    //
    // No designer file -- simple enough to build inline (matches AboutDialog).
    internal sealed class EditFiltersForm : Form
    {
        private readonly FilterLibrary mLibrary;
        private readonly BindingList<FilterRow> mRows;
        private readonly DataGridView mGrid;

        public EditFiltersForm(FilterLibrary library)
        {
            if (library == null) throw new ArgumentNullException(nameof(library));
            mLibrary = library;

            // BindingList of mutable shadows. Filter is immutable; the grid edits these
            // shadows and Save converts them back via FilterRow.ToFilter().
            mRows = new BindingList<FilterRow>(library.Filters.Select(FilterRow.From).ToList());

            Text = "Edit Filters";
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Size = new Size(720, 360);
            MinimumSize = new Size(540, 240);

            mGrid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AutoGenerateColumns = false,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                EditMode = DataGridViewEditMode.EditOnEnter,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
                RowHeadersWidth = 30,
            };

            mGrid.Columns.Add(NewTextColumn(nameof(FilterRow.Name),           "Name",           60));
            mGrid.Columns.Add(NewTextColumn(nameof(FilterRow.SeparationDeg),  "Sep",            55));
            mGrid.Columns.Add(NewTextColumn(nameof(FilterRow.WidthDays),      "Width",          55));
            mGrid.Columns.Add(NewCheckColumn(nameof(FilterRow.RelaxEnabled),  "Relax",          50));
            mGrid.Columns.Add(NewTextColumn(nameof(FilterRow.RelaxMinAltDeg), "RelaxMin",       70));
            mGrid.Columns.Add(NewTextColumn(nameof(FilterRow.RelaxMaxAltDeg), "RelaxMax",       70));
            mGrid.Columns.Add(NewTextColumn(nameof(FilterRow.RelaxScale),     "RelaxScale",     80));
            mGrid.Columns.Add(NewTextColumn(nameof(FilterRow.BandwidthNm),    "Bandwidth (nm)", 110));

            mGrid.DataSource = mRows;
            Controls.Add(mGrid);

            // Bottom button strip. All four buttons left-aligned for v1; visual polish
            // can come later. Add Fill-docked grid first so the Bottom panel claims its
            // own space without contention.
            Panel buttonPanel = new Panel { Dock = DockStyle.Bottom, Height = 40 };
            Controls.Add(buttonPanel);

            Button addBtn = new Button
            {
                Text = "&Add",
                Location = new Point(10, 6),
                Size = new Size(75, 28),
            };
            addBtn.Click += (s, e) => mRows.Add(FilterRow.NewDefault());
            buttonPanel.Controls.Add(addBtn);

            Button removeBtn = new Button
            {
                Text = "&Remove",
                Location = new Point(95, 6),
                Size = new Size(75, 28),
            };
            removeBtn.Click += (s, e) =>
            {
                FilterRow row = mGrid.CurrentRow != null
                    ? mGrid.CurrentRow.DataBoundItem as FilterRow
                    : null;
                if (row != null) mRows.Remove(row);
            };
            buttonPanel.Controls.Add(removeBtn);

            Button saveBtn = new Button
            {
                Text = "&Save",
                Location = new Point(200, 6),
                Size = new Size(75, 28),
            };
            saveBtn.Click += SaveButton_Click;
            buttonPanel.Controls.Add(saveBtn);
            AcceptButton = saveBtn;

            Button cancelBtn = new Button
            {
                Text = "Cancel",
                Location = new Point(285, 6),
                Size = new Size(75, 28),
                DialogResult = DialogResult.Cancel,
            };
            buttonPanel.Controls.Add(cancelBtn);
            CancelButton = cancelBtn;
        }

        private static DataGridViewTextBoxColumn NewTextColumn(
            string dataPropertyName, string headerText, int width)
            => new DataGridViewTextBoxColumn
            {
                DataPropertyName = dataPropertyName,
                HeaderText = headerText,
                Width = width,
                SortMode = DataGridViewColumnSortMode.NotSortable,
            };

        private static DataGridViewCheckBoxColumn NewCheckColumn(
            string dataPropertyName, string headerText, int width)
            => new DataGridViewCheckBoxColumn
            {
                DataPropertyName = dataPropertyName,
                HeaderText = headerText,
                Width = width,
                SortMode = DataGridViewColumnSortMode.NotSortable,
            };

        private void SaveButton_Click(object sender, EventArgs e)
        {
            // Commit any in-progress cell edit so the BindingList sees the latest value.
            mGrid.EndEdit();

            // Validate: empty / whitespace-only Name rejected, duplicates rejected
            // (case-insensitive). Both flag the conflict via MessageBox and leave the
            // dialog open.
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (FilterRow r in mRows)
            {
                if (string.IsNullOrWhiteSpace(r.Name))
                {
                    MessageBox.Show(this,
                        "Every filter needs a name.",
                        "Edit Filters",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (!seen.Add(r.Name.Trim()))
                {
                    MessageBox.Show(this,
                        "Duplicate filter name '" + r.Name + "'. Names must be unique.",
                        "Edit Filters",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            mLibrary.ReplaceAll(mRows.Select(r => r.ToFilter()).ToList());
            try
            {
                mLibrary.Save();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    "Couldn't save filters.json:\n" + ex.Message,
                    "Edit Filters",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        }

        // Mutable view-model wrapping a Filter for binding to the DataGridView.
        // Filter is immutable; this row is the editable shadow committed to the
        // library on Save. NewDefault yields a sensible-narrowband baseline so a
        // freshly-added row isn't all-zeros.
        private class FilterRow
        {
            public string Name           { get; set; }
            public double SeparationDeg  { get; set; }
            public double WidthDays      { get; set; }
            public bool   RelaxEnabled   { get; set; }
            public double RelaxMinAltDeg { get; set; }
            public double RelaxMaxAltDeg { get; set; }
            public double RelaxScale     { get; set; }
            public double BandwidthNm    { get; set; }

            public static FilterRow From(Filter f) => new FilterRow
            {
                Name           = f.Name,
                SeparationDeg  = f.SeparationDeg,
                WidthDays      = f.WidthDays,
                RelaxEnabled   = f.RelaxEnabled,
                RelaxMinAltDeg = f.RelaxMinAltDeg,
                RelaxMaxAltDeg = f.RelaxMaxAltDeg,
                RelaxScale     = f.RelaxScale,
                BandwidthNm    = f.BandwidthNm,
            };

            public static FilterRow NewDefault() => new FilterRow
            {
                Name           = "NewFilter",
                SeparationDeg  = 60.0,
                WidthDays      = 7.0,
                RelaxEnabled   = false,
                RelaxMinAltDeg = -15.0,
                RelaxMaxAltDeg = 5.0,
                RelaxScale     = 0.0,
                BandwidthNm    = 3.0,
            };

            public Filter ToFilter() => new Filter(
                name:           Name,
                separationDeg:  SeparationDeg,
                widthDays:      WidthDays,
                relaxEnabled:   RelaxEnabled,
                relaxMinAltDeg: RelaxMinAltDeg,
                relaxMaxAltDeg: RelaxMaxAltDeg,
                relaxScale:     RelaxScale,
                bandwidthNm:    BandwidthNm);
        }
    }
}
