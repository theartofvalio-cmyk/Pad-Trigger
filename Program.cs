using System;
using System.Windows.Forms;

namespace PadTrigger
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            // This matches Windows "DPI override: System" behavior.
            // It prevents the UI from exploding on PCs that use different monitor scaling.
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Form1());
        }
    }
}
