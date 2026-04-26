using System;
using System.Windows.Forms;
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

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
