using System;
using System.IO;
using System.Windows.Forms;

namespace Thesis_testing_1
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            if (Environment.OSVersion.Version.Major >= 6)
            {
                SetProcessDPIAware();
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            string csvPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "worldcities.csv");

            CityData.LoadCities(csvPath);

            Application.Run(new Menu());
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();
    }
}
