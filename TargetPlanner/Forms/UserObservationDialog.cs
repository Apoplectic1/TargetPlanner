using System;
using System.Drawing;
using System.Windows.Forms;

namespace TargetPlanner.Forms
{
    // Ctrl+N invoked debug dialog. Single free-form notes field; OK captures the
    // notes plus a screenshot + context snapshot, writes USER_OBS_END to tp.log
    // with start/end markers bracketing the user's actions while open.
    //
    // Modeless + TopMost so the user can interact with the main chart while
    // the dialog stays open. The dialog's open period brackets the relevant
    // UI / Coord / Cache / chart diag lines:
    //
    //   USER_OBS_START id=4f2a build=1.0.0+abc1234
    //   DIAG/UI CheckBox_Sky.CheckedChanged checked=True
    //   USER_OBS_CAP id=4f2a screenshot=...           (each mid-session Capture)
    //   DIAG/Sky Render exit ...
    //   USER_OBS_END id=4f2a ctx=(area=Day, ...) screenshot=... notes="..."
    //
    // grep id=4f2a tp.log surfaces the full investigation window.
    // Cancellation (Cancel button or close-X) writes USER_OBS_CANCEL with the
    // same id so every START has a matching terminator.
    //
    // Capture grabs the main window on demand and stays open, so one session can
    // interleave several timestamped shots with notes (capture -> change UI ->
    // capture -> type -> OK); since every line is local-time stamped, images and
    // notes can be ordered against each other after the fact.
    //
    // Empty / whitespace-only notes is the "all-okay checkpoint" gesture: the
    // log line carries notes="(checkpoint)" so grep finds those moments cleanly.
    //
    // Singleton: re-pressing Ctrl+N while the dialog is already open focuses
    // the existing instance (no second dialog, no second START marker).
    //
    // Logging + screen capture come from the shared Astronomy.Diagnostics
    // (global using) — formerly TargetPlanner.Support.Log.
    internal sealed class UserObservationDialog : Form
    {
        // Static instance tracker so re-trigger focuses the existing dialog
        // instead of stacking. Cleared on FormClosing.
        private static UserObservationDialog sCurrent;

        private readonly string mId;
        private readonly Form mOwnerForm;
        private readonly Func<string> mContextProvider;
        private readonly TextBox mNotes;
        private readonly Button mOk;
        private readonly Button mCancel;
        private readonly Button mCapture;
        private readonly Label mStatus;
        private int mCaptureCount;
        // True when we logged END/CANCEL ourselves; suppresses the FormClosing
        // handler from double-logging if Form.Close was called from button
        // handlers (which fire FormClosing themselves).
        private bool mTerminationLogged;

        private UserObservationDialog(Form ownerForm, Func<string> contextProvider)
        {
            mId = Guid.NewGuid().ToString("N").Substring(0, 4);
            mOwnerForm = ownerForm;
            mContextProvider = contextProvider ?? (() => string.Empty);

            Text = "Observation (id=" + mId + ")";
            FormBorderStyle = FormBorderStyle.SizableToolWindow;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            TopMost = true;
            ClientSize = new Size(420, 220);
            Padding = new Padding(10);

            var lblNotes = new Label
            {
                Text = "Notes (free-form, Ctrl+Enter for newline). Capture grabs the main window now " +
                       "(repeatable). Leave blank for a checkpoint:",
                AutoSize = false,
                Location = new Point(10, 8),
                Size = new Size(400, 18),
            };

            mNotes = new TextBox
            {
                Location = new Point(10, 30),
                Size = new Size(400, 140),
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

            // Capture stays open and re-shows itself after the grab; OK / Cancel are terminal.
            mCapture = new Button
            {
                Text = "Capture",
                Location = new Point(10, 180),
                Size = new Size(90, 28),
            };
            mCapture.Click += OnCaptureClick;

            mStatus = new Label
            {
                Location = new Point(108, 185),
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
            };

            mOk = new Button
            {
                Text = "OK",
                Location = new Point(255, 180),
                Size = new Size(75, 28),
            };
            mOk.Click += OnOkClick;

            mCancel = new Button
            {
                Text = "Cancel",
                Location = new Point(335, 180),
                Size = new Size(75, 28),
            };
            mCancel.Click += OnCancelClick;

            AcceptButton = mOk;
            CancelButton = mCancel;

            Controls.Add(lblNotes);
            Controls.Add(mNotes);
            Controls.Add(mCapture);
            Controls.Add(mStatus);
            Controls.Add(mOk);
            Controls.Add(mCancel);

            FormClosing += OnFormClosing;
        }

        // Show modeless over the given owner, or focus the existing instance
        // if one is already open. The contextProvider is called at OK time
        // to capture the current MainForm state for the USER_OBS_END line.
        public static void ShowOrFocus(Form owner, Func<string> contextProvider)
        {
            if (sCurrent != null && !sCurrent.IsDisposed)
            {
                if (sCurrent.WindowState == FormWindowState.Minimized)
                    sCurrent.WindowState = FormWindowState.Normal;
                sCurrent.BringToFront();
                sCurrent.Activate();
                return;
            }

            var dlg = new UserObservationDialog(owner, contextProvider);
            sCurrent = dlg;
            Log.UserObservationStart(dlg.mId);
            dlg.Show(owner);
        }

        // Capture button: take a mid-session shot and stay open. Hide this TopMost
        // dialog so it isn't in the shot, force the owner to repaint the area it was
        // occluding, grab, then re-show + refocus the notes. Repeatable -- each shot
        // is a USER_OBS_CAP line plus a bump to the status readout.
        private void OnCaptureClick(object sender, EventArgs e)
        {
            Hide();
            if (mOwnerForm != null && !mOwnerForm.IsDisposed) mOwnerForm.Refresh();
            string path = TryCaptureScreenshot();
            Show();
            mNotes.Focus();

            if (path != null)
            {
                mCaptureCount++;
                Log.UserObservationCapture(mId, path);
                mStatus.Text = string.Format("captured {0} - {1:HH:mm:ss}", mCaptureCount, DateTime.Now);
            }
            else
            {
                mStatus.Text = "capture failed - see tp.log";
            }
        }

        private void OnOkClick(object sender, EventArgs e)
        {
            // Hide ourselves before screenshotting so the dialog (which is
            // TopMost and typically overlaps part of the chart) doesn't end up
            // in the captured PNG. Force the owner to repaint synchronously
            // so the area the dialog was occluding gets re-rendered before
            // the screen-grab. We're closing anyway, so leaving the dialog
            // hidden is fine -- no need to Show() it back.
            Hide();
            if (mOwnerForm != null && !mOwnerForm.IsDisposed) mOwnerForm.Refresh();

            // Capture screenshot of the main form's current pixel state. Done
            // BEFORE writing USER_OBS_END so the path is included in the line.
            // Failure (disk full, bitmap throws, etc) leaves screenshot empty
            // and the END line is still written -- the observation report
            // still has value without the picture.
            string screenshotPath = TryCaptureScreenshot();

            // Capture current ctx snapshot from MainForm.
            string ctx = string.Empty;
            try { ctx = mContextProvider() ?? string.Empty; }
            catch (Exception ex) { Log.Warn("Observation contextProvider threw", ex); }

            Log.UserObservationEnd(mId, ctx, mNotes.Text, screenshotPath ?? string.Empty);
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
            if (!mTerminationLogged)
            {
                Log.UserObservationCancel(mId);
                mTerminationLogged = true;
            }
            if (ReferenceEquals(sCurrent, this)) sCurrent = null;
        }

        // Adapt the owner form's screen bounds to the shared screen capture + the shared
        // obs-<id>-<stamp> filename convention; Astronomy.Diagnostics owns the grab, encode,
        // local-time stamp, and best-effort failure path (it captures the literal rendered
        // pixels regardless of LiveCharts2's Skia paint, like the old CopyFromScreen did).
        private string TryCaptureScreenshot()
        {
            if (mOwnerForm == null || mOwnerForm.IsDisposed) return null;
            Rectangle b = mOwnerForm.Bounds;
            return ScreenCapture.ToPng(b.X, b.Y, b.Width, b.Height, Log.NewObservationScreenshotPath(mId));
        }
    }
}
