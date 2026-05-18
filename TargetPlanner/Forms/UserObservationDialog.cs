using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Windows.Forms;
using TargetPlanner.Settings;
using TargetPlanner.Support;

namespace TargetPlanner.Forms
{
    // Ctrl+N invoked debug dialog. Captures the user's in-the-moment
    // observation -- a pre-seeded checklist of recurring patterns
    // (PersonalDefaults.UserObservationChecklist), a free-form notes box, and
    // an auto-captured screenshot + context snapshot saved when the user
    // clicks OK -- and writes start/end markers to tp.log so the user's
    // actions while the dialog is open are chronologically bracketed.
    //
    // Modeless + TopMost so the user can interact with the main chart while
    // the dialog stays open. The dialog's open period brackets the relevant
    // UI / Coord / Cache / chart diag lines:
    //
    //   USER_OBS_START id=4f2a build=1.0.0+abc1234
    //   DIAG/UI CheckBox_Sky.CheckedChanged checked=True
    //   DIAG/Sky Render exit ...
    //   DIAG/UI CheckBox_Sky.CheckedChanged checked=False
    //   USER_OBS_END id=4f2a ctx=(area=Day, ...) screenshot=... checked=[...] notes="..."
    //
    // grep id=4f2a tp.log surfaces the full investigation window.
    // Cancellation (Cancel button or close-X) writes USER_OBS_CANCEL with the
    // same id so every START has a matching terminator.
    //
    // Singleton: re-pressing Ctrl+N while the dialog is already open focuses
    // the existing instance (no second dialog, no second START marker).
    internal sealed class UserObservationDialog : Form
    {
        // Static instance tracker so re-trigger focuses the existing dialog
        // instead of stacking. Cleared on FormClosing.
        private static UserObservationDialog sCurrent;

        private readonly string mId;
        private readonly Form mOwnerForm;
        private readonly Func<string> mContextProvider;
        private readonly CheckedListBox mChecklist;
        private readonly TextBox mNotes;
        private readonly Button mOk;
        private readonly Button mCancel;
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

        private void OnOkClick(object sender, EventArgs e)
        {
            // Capture screenshot of the main form's current pixel state. Done
            // BEFORE writing USER_OBS_END so the path is included in the line.
            // Failure (disk full, bitmap throws, etc) leaves screenshot empty
            // and the END line is still written -- the observation report
            // still has value without the picture.
            string screenshotPath = TryCaptureScreenshot();

            // Collect checked items.
            var sb = new StringBuilder();
            bool first = true;
            foreach (object item in mChecklist.CheckedItems)
            {
                if (!first) sb.Append("; ");
                sb.Append(item?.ToString() ?? string.Empty);
                first = false;
            }

            // Capture current ctx snapshot from MainForm.
            string ctx = string.Empty;
            try { ctx = mContextProvider() ?? string.Empty; }
            catch (Exception ex) { Log.Warn("Observation contextProvider threw", ex); }

            Log.UserObservationEnd(mId, ctx, sb.ToString(), mNotes.Text, screenshotPath ?? string.Empty);
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

        // Capture the main form's screen pixels (full window bounds) and
        // save as PNG under %APPDATA%\TargetPlanner\screenshots\.
        // CopyFromScreen is used because LiveCharts2's SKControl paints via
        // Skia and Control.DrawToBitmap returns blank for it -- the screen
        // grab captures the actual rendered pixels regardless of the
        // underlying paint mechanism.
        private string TryCaptureScreenshot()
        {
            try
            {
                if (mOwnerForm == null || mOwnerForm.IsDisposed) return null;
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "TargetPlanner", "screenshots");
                Directory.CreateDirectory(dir);
                string name = string.Format("obs-{0}-{1:yyyyMMddHHmmss}.png",
                    mId, DateTime.UtcNow);
                string path = Path.Combine(dir, name);
                Rectangle bounds = mOwnerForm.Bounds;
                using (var bmp = new Bitmap(bounds.Width, bounds.Height))
                {
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
                    }
                    bmp.Save(path, ImageFormat.Png);
                }
                return path;
            }
            catch (Exception ex)
            {
                Log.Warn("Observation screenshot capture failed", ex);
                return null;
            }
        }
    }
}
