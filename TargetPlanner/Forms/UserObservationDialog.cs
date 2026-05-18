using System;
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
    // and writes start/end markers to tp.log so the user's actions while the
    // dialog is open are chronologically bracketed by the markers.
    //
    // Modeless + TopMost: user can interact with the main chart (toggle radios,
    // scrub date, etc) while the dialog stays open and visible. The dialog's
    // open period brackets the relevant UI / Coord / Cache / chart diag lines:
    //
    //   USER_OBS_START id=4f2a
    //   DIAG/UI CheckBox_Sky.CheckedChanged checked=True
    //   DIAG/Sky Render exit ...
    //   DIAG/UI CheckBox_Sky.CheckedChanged checked=False
    //   USER_OBS_END id=4f2a checked=[Moon shifts] notes="..."
    //
    // Grep id=4f2a surfaces the full investigation window. USER_OBS_CANCEL
    // replaces USER_OBS_END on Cancel or close-X so every START has a
    // matching terminator.
    //
    // Singleton: re-right-clicking the title bar while the dialog is already
    // open just focuses the existing instance (no second dialog, no second
    // START marker).
    internal sealed class UserObservationDialog : Form
    {
        // Static instance tracker so re-trigger focuses the existing dialog
        // instead of stacking. Cleared on FormClosing.
        private static UserObservationDialog sCurrent;

        private readonly string mId;
        private readonly CheckedListBox mChecklist;
        private readonly TextBox mNotes;
        private readonly Button mOk;
        private readonly Button mCancel;
        // True when we logged END/CANCEL ourselves; suppresses the FormClosing
        // handler from double-logging if Form.Close was called from button
        // handlers (which fire FormClosing themselves).
        private bool mTerminationLogged;

        private UserObservationDialog()
        {
            mId = Guid.NewGuid().ToString("N").Substring(0, 4);

            Text = "Observation (id=" + mId + ")";
            FormBorderStyle = FormBorderStyle.SizableToolWindow;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            TopMost = true;
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
            };
            mOk.Click += OnOkClick;

            mCancel = new Button
            {
                Text = "Cancel",
                Location = new Point(335, 340),
                Size = new Size(75, 28),
            };
            mCancel.Click += OnCancelClick;

            AcceptButton = mOk;
            CancelButton = mCancel;

            Controls.Add(lblChecklist);
            Controls.Add(mChecklist);
            Controls.Add(lblNotes);
            Controls.Add(mNotes);
            Controls.Add(mOk);
            Controls.Add(mCancel);

            FormClosing += OnFormClosing;
        }

        // Show modeless over the given owner. If a dialog is already open,
        // bring it to front and focus it instead of opening a second one --
        // there's only ever one observation window at a time.
        public static void ShowOrFocus(IWin32Window owner)
        {
            if (sCurrent != null && !sCurrent.IsDisposed)
            {
                if (sCurrent.WindowState == FormWindowState.Minimized)
                    sCurrent.WindowState = FormWindowState.Normal;
                sCurrent.BringToFront();
                sCurrent.Activate();
                return;
            }

            var dlg = new UserObservationDialog();
            sCurrent = dlg;
            Log.UserObservationStart(dlg.mId);
            dlg.Show(owner);
        }

        private void OnOkClick(object sender, EventArgs e)
        {
            var sb = new StringBuilder();
            bool first = true;
            foreach (object item in mChecklist.CheckedItems)
            {
                if (!first) sb.Append("; ");
                sb.Append(item?.ToString() ?? string.Empty);
                first = false;
            }
            Log.UserObservationEnd(mId, sb.ToString(), mNotes.Text);
            mTerminationLogged = true;
            Close();
        }

        private void OnCancelClick(object sender, EventArgs e)
        {
            Log.UserObservationCancel(mId);
            mTerminationLogged = true;
            Close();
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            // User clicked the X (or Alt-F4) without OK/Cancel -- log a
            // CANCEL so every START has a matching terminator.
            if (!mTerminationLogged)
            {
                Log.UserObservationCancel(mId);
                mTerminationLogged = true;
            }
            if (ReferenceEquals(sCurrent, this)) sCurrent = null;
        }
    }
}
