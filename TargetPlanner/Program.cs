using System;
using System.Windows.Forms;
using TargetPlanner.Support;
using Velopack;

namespace TargetPlanner
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            // VelopackApp.Run handles the install / uninstall / first-run / update hooks
            // (passed in as command-line args by Velopack's Setup.exe and Update.exe). Must
            // be the very first call -- before any WinForms init -- so those special-flag
            // invocations don't accidentally show the main UI. Reads command-line args from
            // Environment.GetCommandLineArgs() internally; no need to pass them here.
            VelopackApp.Build().Run();

            // Configure the shared diagnostics log for this app, then rotate tp.log -> tp.log.prev and start
            // fresh. Each run's diag trail is self-contained; one run back is preserved for postmortem. Diag
            // channels default to all in Debug / off in Release; TP_DIAG overrides either at runtime.
#if DEBUG
            const DiagDefault diag = DiagDefault.All;
#else
            const DiagDefault diag = DiagDefault.None;
#endif
            Log.Init(new AppLogIdentity("TargetPlanner", "tp.log", "TP_DIAG", diag));
            Log.StartNewSession();

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
