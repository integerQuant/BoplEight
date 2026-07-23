using System;
using System.Reflection;
using System.Windows.Forms;

[assembly: AssemblyTitle("BoplEight Setup")]
[assembly: AssemblyDescription("Installs BepInEx and BoplEight for Bopl Battle")]
[assembly: AssemblyCompany("BoplEight")]
[assembly: AssemblyProduct("BoplEight Setup")]
[assembly: AssemblyCopyright("Copyright (c) 2026")]
[assembly: AssemblyVersion("1.0.7.0")]
[assembly: AssemblyFileVersion("1.0.7.0")]

namespace BoplEight.Installer
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += delegate(object sender, System.Threading.ThreadExceptionEventArgs args)
            {
                MessageBox.Show(
                    "BoplEight Setup encountered an unexpected error:\n\n" + args.Exception.Message,
                    "BoplEight Setup",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            };
            Application.Run(new InstallerForm());
        }
    }
}
