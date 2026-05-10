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
            // EN: Enables better scaling on high-DPI displays.
            // HU: Jobb méretezést biztosít nagy DPI-s kijelzőkön.
            if (Environment.OSVersion.Version.Major >= 6)
            {
                SetProcessDPIAware();
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // EN: City names are loaded once at startup for the autocomplete fields.
            // HU: A városnevek induláskor töltődnek be az automatikus kiegészítéshez.
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
