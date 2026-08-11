using System;
using System.Windows.Forms;

namespace TargetPlanner.Support
{
    /// <summary>
    /// Application-level Ctrl+N hook for the diagnostics dialog. Registered via
    /// <see cref="Application.AddMessageFilter"/> so the keystroke works from any UI state a
    /// WinForms message loop serves — including MenuStrip menu mode (whose ModalMenuFilter is
    /// registered later and therefore filters after this one) and modal WinForms dialogs like
    /// the filter editor, both of which bypass MainForm's ProcessCmdKey chain (user obs f231).
    /// </summary>
    /// <remarks>
    /// Native modal loops (FolderBrowserDialog, MessageBox, common dialogs) never consult
    /// WinForms message filters, so Ctrl+N stays dark there — a Win32 boundary, not a gap here.
    /// </remarks>
    public sealed class DiagnosticsKeyFilter : IMessageFilter
    {
        private const int WM_KEYDOWN = 0x0100;

        private readonly Action mShowDiagnostics;

        public DiagnosticsKeyFilter(Action showDiagnostics)
        {
            mShowDiagnostics = showDiagnostics;
        }

        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg != WM_KEYDOWN) return false;
            if ((Keys)m.WParam != Keys.N) return false;
            if (Control.ModifierKeys != Keys.Control) return false;

            mShowDiagnostics();
            return true;
        }
    }
}
