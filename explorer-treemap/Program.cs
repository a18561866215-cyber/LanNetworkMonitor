using System;
using System.Windows.Forms;

namespace ExplorerTreemap
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            DpiBootstrap.EnablePerMonitorV2();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (var context = new CompanionContextV4())
                Application.Run(context);
        }
    }
}
