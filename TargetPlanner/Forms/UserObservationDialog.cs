using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using TargetPlanner.Settings;
using TargetPlanner.Support;

namespace TargetPlanner.Forms
{
    // Right-click-title-bar invoked debug dialog. Captures the user's in-the-
    // moment observation -- a pre-seeded checklist of recurring patterns
    // (PersonalDefaults.UserObservationChecklist) plus a free-form notes box --
    // and writes one USER_OBS line to tp.log. The surrounding DIAG lines give
    // the post-hoc context for what was happening at the moment of observation.
    //
    // Use case: user spots something visually wrong, right-clicks the title
    // bar, ticks the matching boxes, types a sentence, hits OK. Later, grep
    // tp.log for USER_OBS to find the report and read the surrounding DIAG
    // for the pipeline state at that timestamp.
    internal sealed class UserObservationDialog : Form
    {
        private readonly CheckedListBox mChecklist;
        private readonly TextBox mNotes;
        private readonly Button mOk;
        private readonly Button mCancel;

        public UserObservationDialog()
        {
            Text = "Observation";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(420, 380);
            Padding = new Padding(10);

            var lblChecklist = new Label
            {
                Text = "Check what you observed:",
                AutoSize = true,
                Location = new Point(10, 10),
            };

            mChecklist = new CheckedListBox
            {
                Location = new Point(10, 30),
                Size = new Size(400, 180),
                CheckOnClick = true,
                IntegralHeight = false,
            };
            foreach (string item in PersonalDefaults.UserObservationChecklist)
            {
                mChecklist.Items.Add(item, false);
            }

            var lblNotes = new Label
            {
                Text = "Notes (free-form, Ctrl+Enter for newline):",
                AutoSize = true,
                Location = new Point(10, 220),
            };

            mNotes = new TextBox
            {
                Location = new Point(10, 240),
                Size = new Size(400, 90),
                Multiline = true,
                AcceptsReturn = false,
                ScrollBars = ScrollBars.Vertical,
            };
            // Ctrl+Enter inserts a newline; bare Enter submits.
            mNotes.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter && e.Control)
                {
                    int caret = mNotes.SelectionStart;
                    mNotes.Text = mNotes.Text.Insert(caret, Environment.NewLine);
                    mNotes.SelectionStart = caret + Environment.NewLine.Length;
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
            };

            mOk = new Button
            {
                Text = "OK",
                Location = new Point(255, 340),
                Size = new Size(75, 28),
                DialogResult = DialogResult.OK,
            };
            mCancel = new Button
            {
                Text = "Cancel",
                Location = new Point(335, 340),
                Size = new Size(75, 28),
                DialogResult = DialogResult.Cancel,
            };

            AcceptButton = mOk;
            CancelButton = mCancel;

            Controls.Add(lblChecklist);
            Controls.Add(mChecklist);
            Controls.Add(lblNotes);
            Controls.Add(mNotes);
            Controls.Add(mOk);
            Controls.Add(mCancel);
        }

        // Show modally over the given owner and emit USER_OBS to tp.log on OK.
        // Returns true when the observation was logged; false on Cancel.
        public static bool ShowAndLog(IWin32Window owner)
        {
            using (var dlg = new UserObservationDialog())
            {
                if (dlg.ShowDialog(owner) != DialogResult.OK) return false;

                var sb = new StringBuilder();
                bool first = true;
                foreach (object item in dlg.mChecklist.CheckedItems)
                {
                    if (!first) sb.Append("; ");
                    sb.Append(item?.ToString() ?? string.Empty);
                    first = false;
                }
                Log.UserObservation(sb.ToString(), dlg.mNotes.Text);
                return true;
            }
        }
    }
}
