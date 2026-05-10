using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;

namespace TargetPlanner.Forms
{
    // CheckedListBox subclass that re-enables OwnerDrawFixed AND owns the per-row
    // paint logic. Two .NET-framework gotchas drive both halves:
    //
    //   1. CheckedListBox hard-codes DrawMode = Normal in its property setter.
    //      Setting it externally is a silent no-op; without LBS_OWNERDRAWFIXED in
    //      the native window style, Windows never sends WM_DRAWITEM. CreateParams
    //      below adds the flag at handle-creation time.
    //
    //   2. CheckedListBox.OnDrawItem does its own custom painting (the checkbox
    //      glyph + the text) and does NOT call base.OnDrawItem. The user-facing
    //      DrawItem event (which lives on ListBox) therefore never fires from a
    //      CheckedListBox-derived control. We work around this by overriding
    //      OnDrawItem and doing the entire paint here -- including the checkbox
    //      glyph and the text -- so consumers can tint backgrounds via the
    //      RowBackground callback without needing to subscribe to a never-firing
    //      event.
    //
    // ItemCheck, SelectedIndexChanged, MouseDoubleClick, MouseMove, etc. still
    // fire as usual; only the paint path is rerouted.
    public sealed class DupeAwareCheckedListBox : CheckedListBox
    {
        private const int LBS_OWNERDRAWFIXED = 0x0010;

        // Lookup callback: given a row index, return a background color tint or
        // null for "use the default background". Consumer (MainForm) sets this
        // to its dupe-set lookup; the listbox reads it on every paint. Hidden
        // from the WinForms designer (Browsable=false / DesignerSerialization=Hidden)
        // since it's a runtime-only delegate, not a designable property.
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Func<int, Color?> RowBackground { get; set; }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.Style |= LBS_OWNERDRAWFIXED;
                return cp;
            }
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= Items.Count)
            {
                e.DrawBackground();
                return;
            }

            bool isSelected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            Color? tint = isSelected ? null : RowBackground?.Invoke(e.Index);

            if (isSelected)
            {
                using (var b = new SolidBrush(SystemColors.Highlight))
                    e.Graphics.FillRectangle(b, e.Bounds);
            }
            else if (tint.HasValue)
            {
                using (var b = new SolidBrush(tint.Value))
                    e.Graphics.FillRectangle(b, e.Bounds);
            }
            else
            {
                e.DrawBackground();
            }

            CheckState state = GetItemCheckState(e.Index);
            CheckBoxState glyph = state == CheckState.Checked
                ? CheckBoxState.CheckedNormal
                : CheckBoxState.UncheckedNormal;
            Size glyphSize = CheckBoxRenderer.GetGlyphSize(e.Graphics, glyph);
            int glyphY = e.Bounds.Top + Math.Max(0, (e.Bounds.Height - glyphSize.Height) / 2);
            CheckBoxRenderer.DrawCheckBox(
                e.Graphics, new Point(e.Bounds.Left + 2, glyphY), glyph);

            int textLeft = e.Bounds.Left + glyphSize.Width + 6;
            var textBounds = new Rectangle(
                textLeft, e.Bounds.Top, e.Bounds.Width - (textLeft - e.Bounds.Left), e.Bounds.Height);
            string text = Items[e.Index]?.ToString() ?? string.Empty;
            Color textColor = isSelected ? SystemColors.HighlightText : ForeColor;
            TextRenderer.DrawText(
                e.Graphics, text, Font, textBounds, textColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            e.DrawFocusRectangle();
        }
    }
}
