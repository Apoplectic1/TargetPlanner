using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace TargetPlanner.Forms
{
    // Minimal About dialog. Shows the app name, version (read from the entry assembly's
    // AssemblyInformationalVersion -- stamped by MinVer from the latest git tag), and a
    // clickable GitHub repo link. No designer file -- simple enough to build inline.
    internal sealed class AboutDialog : Form
    {
        private const string RepoUrl = "https://github.com/Apoplectic1/TargetPlanner";

        public AboutDialog()
        {
            Text = "About TargetPlanner";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(360, 170);

            var name = new Label
            {
                Text = "TargetPlanner",
                Font = new Font(Font.FontFamily, 12, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(20, 20),
            };

            var version = new Label
            {
                Text = "Version " + GetDisplayVersion(),
                AutoSize = true,
                Location = new Point(20, 50),
            };

            var link = new LinkLabel
            {
                Text = RepoUrl,
                AutoSize = true,
                Location = new Point(20, 80),
            };
            link.LinkClicked += (s, e) =>
            {
                try { Process.Start(RepoUrl); } catch { /* user clicked link, browser missing -- nothing to do */ }
            };

            var ok = new Button
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                Size = new Size(80, 28),
                Location = new Point(ClientSize.Width - 100, ClientSize.Height - 40),
            };

            Controls.Add(name);
            Controls.Add(version);
            Controls.Add(link);
            Controls.Add(ok);
            AcceptButton = ok;
        }

        // MinVer stamps AssemblyInformationalVersion with the full SemVer (e.g. "1.0.0" for a
        // tagged release, "0.0.0-alpha.0.107+sha" for a dev build). Strip the +sha suffix for
        // display since end-users don't need the commit hash here.
        private static string GetDisplayVersion()
        {
            var asm = Assembly.GetExecutingAssembly();
            var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            string raw = info?.InformationalVersion ?? asm.GetName().Version?.ToString() ?? "unknown";
            int plus = raw.IndexOf('+');
            return plus >= 0 ? raw.Substring(0, plus) : raw;
        }
    }
}
