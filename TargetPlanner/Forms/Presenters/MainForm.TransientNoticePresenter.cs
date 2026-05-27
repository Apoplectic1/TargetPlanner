using System.Drawing;
using System.Windows.Forms;

namespace TargetPlanner
{
    // Transient notice widget: small auto-dismissing popup centered on the main
    // form. Non-modal -- the main form stays interactive while the notice is on
    // screen. Pooled to avoid GDI handle churn (the prior implementation built
    // a fresh Form + Label + Timer per call). Lifted out of MainForm.cs --
    // partial-class file split, same pattern as the other presenter partials.
    //
    // Single producer today: Button_Graph_Click when no targets are picked /
    // checked / typed (silent no-op was confusing). Add/Remove target clicks
    // also call it for the "No Target" error case.
    public partial class MainForm
    {
        // Pooled instances for the transient-notice popup. Allocated lazily on first
        // ShowTransientMessage call and reused across subsequent invocations -- prior
        // implementation built a fresh Form + Label + Timer per call (GDI handle churn
        // for a notice that fires once every few minutes at most).
        private Form mTransientNotice;
        private Label mTransientLabel;
        private System.Windows.Forms.Timer mTransientTimer;

        // Show a small auto-dismissing notice centered on the main form. Used by
        // Button_Graph_Click when no targets are picked / checked / typed -- a silent
        // no-op was confusing. Non-modal: the main form stays interactive while the
        // notice is on screen. The pooled Form is hidden (not disposed) on Tick so the
        // next call reuses it.
        private void ShowTransientMessage(string text, int durationMs = 2000)
        {
            if (mTransientNotice == null || mTransientNotice.IsDisposed)
            {
                mTransientLabel = new Label
                {
                    Dock      = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font      = new Font(SystemFonts.MessageBoxFont.FontFamily, 12F, FontStyle.Bold),
                };
                mTransientNotice = new Form
                {
                    // Manual positioning: FormStartPosition.CenterParent only fires on the
                    // first Show(); the pooled instance's subsequent Show() calls would
                    // keep the original position even if the main form has moved. We
                    // re-center against the main form's live bounds on every Show below.
                    FormBorderStyle = FormBorderStyle.FixedToolWindow,
                    StartPosition   = FormStartPosition.Manual,
                    ShowInTaskbar   = false,
                    ControlBox      = false,
                    Text            = string.Empty,
                    Size            = new Size(220, 80),
                    BackColor       = SystemColors.Info,
                };
                mTransientNotice.Controls.Add(mTransientLabel);
                mTransientTimer = new System.Windows.Forms.Timer();
                mTransientTimer.Tick += (s, e) =>
                {
                    mTransientTimer.Stop();
                    if (mTransientNotice != null && !mTransientNotice.IsDisposed && mTransientNotice.Visible)
                        mTransientNotice.Hide();
                };
            }

            mTransientLabel.Text = text;
            mTransientTimer.Stop();
            mTransientTimer.Interval = durationMs;

            // Center on the main form's live bounds (screen coords). Recomputed on every
            // Show so the notice tracks the user moving / resizing the main form between
            // displays.
            mTransientNotice.Location = new Point(
                this.Left + (this.Width  - mTransientNotice.Width)  / 2,
                this.Top  + (this.Height - mTransientNotice.Height) / 2);

            if (!mTransientNotice.Visible) mTransientNotice.Show(this);
            mTransientTimer.Start();
        }
    }
}
