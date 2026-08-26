using System;
using System.Windows.Forms;

namespace ExplorerTreemap
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (var context = new CompanionContext())
                Application.Run(context);
        }
    }
}
