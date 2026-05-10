using System;
using System.IO;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;
using System.Linq;

namespace Thesis_testing_1
{
    public partial class Chart_test : Form
    {
        public Chart_test()
        {
            InitializeComponent();
            InitWebView2();

            this.MaximizeBox = false;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
        }

        private void Chart_test_Load(object sender, EventArgs e)
        {
            ConfigureCityAutocomplete(ICAOTextBox);
        }

        private void ConfigureCityAutocomplete(TextBox textBox)
        {
            var src = new AutoCompleteStringCollection();

            if (CityData.Cities != null && CityData.Cities.Count > 0)
            {
                src.AddRange(CityData.Cities.ToArray());
            }

            textBox.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            textBox.AutoCompleteSource = AutoCompleteSource.CustomSource;
            textBox.AutoCompleteCustomSource = src;
        }

        private async void InitWebView2()
        {
            try
            {
                // EN: WebView2 is used to show the local PDF chart inside the form.
                // HU: A WebView2 jeleníti meg a lokálisan tárolt PDF chartot az űrlapon belül.
                var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(
                    null,
                    null,
                    new Microsoft.Web.WebView2.Core.CoreWebView2EnvironmentOptions("--allow-file-access-from-files")
                );

                await ChartBox.EnsureCoreWebView2Async(env);
            }
            catch (Exception ex)
            {
                MessageBox.Show("WebView2 initialization failed:\n" + ex.Message);
            }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            string icao = ICAOTextBox.Text.Trim().ToUpperInvariant();

            if (string.IsNullOrEmpty(icao))
            {
                MessageBox.Show("Please enter an ICAO code.");
                return;
            }

            if (!(icao.Length == 4 && icao.All(char.IsLetter)))
            {
                MessageBox.Show("Please enter a valid 4-letter ICAO code, for example LHBP or LDSP.");
                return;
            }

            ICAOTextBox.Text = icao;

            comboBox1.Items.Clear();

            if (ChartBox.CoreWebView2 != null)
                ChartBox.CoreWebView2.Navigate("about:blank");
            else
                ChartBox.Source = new Uri("about:blank");

            bool foundLocal = FetchLocalCharts(icao);

            if (!foundLocal)
            {
                MessageBox.Show(
                    $"No local charts found for {icao}.",
                    "No Local Charts",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }

        private bool FetchLocalCharts(string icao)
        {
            try
            {
                // EN: Charts are stored in folders named after the airport ICAO code.
                // HU: A chartok az adott repülőtér ICAO-kódja alapján elnevezett mappákban vannak.
                string chartsDir = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "Charts",
                    icao);

                comboBox1.Items.Clear();

                if (!Directory.Exists(chartsDir))
                    return false;

                string[] pdfFiles = Directory.GetFiles(chartsDir, "*.pdf");

                if (pdfFiles.Length == 0)
                    return false;

                foreach (var file in pdfFiles.OrderBy(f => f))
                {
                    string name = Path.GetFileNameWithoutExtension(file);
                    comboBox1.Items.Add(new ChartItem(name, file));
                }

                if (comboBox1.Items.Count > 0)
                    comboBox1.SelectedIndex = 0;

                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error reading local charts:\n" + ex.Message);
                return false;
            }
        }

        private void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (comboBox1.SelectedItem is ChartItem selected)
            {
                string path = selected.Path;

                try
                {
                    if (!File.Exists(path))
                    {
                        MessageBox.Show(
                            "Chart file not found:\n" + path,
                            "Missing Chart",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                        return;
                    }

                    string fullPath = Path.GetFullPath(path);
                    var uri = new Uri(fullPath);

                    if (ChartBox.CoreWebView2 != null)
                        ChartBox.CoreWebView2.Navigate(uri.AbsoluteUri);
                    else
                        ChartBox.Source = uri;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        "Error displaying chart:\n" + ex.Message + "\n\nPath:\n" + path,
                        "Chart Display Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            }
        }

        private class ChartItem
        {
            public string Name { get; }
            public string Path { get; }

            public ChartItem(string name, string path)
            {
                Name = name;
                Path = path;
            }

            public override string ToString()
            {
                return Name;
            }
        }
    }
}
