using System;
using System.IO;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;
using System.Linq;
using System.Collections.Generic;
using System.Text;

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
            string input = ICAOTextBox.Text.Trim();

            if (string.IsNullOrEmpty(input))
            {
                MessageBox.Show("Please enter an ICAO code or city name.");
                return;
            }

            string icao = ResolveInputToIcao(input);

            if (string.IsNullOrWhiteSpace(icao))
            {
                MessageBox.Show("Airport could not be found. Please enter a valid ICAO code or city name.");
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

        private string ResolveInputToIcao(string input)
        {
            string value = input.Trim();

            if (value.Length == 4 && value.All(char.IsLetter))
                return value.ToUpperInvariant();

            // EN: If the user typed a city name, the program tries to find its airport ICAO code.
            // HU: Ha a felhasználó városnevet írt be, a program megpróbálja megkeresni a hozzá tartozó ICAO-kódot.
            string fromAirportsCsv = FindIcaoByCityName(value);

            if (!string.IsNullOrWhiteSpace(fromAirportsCsv))
                return fromAirportsCsv.ToUpperInvariant();

            return null;
        }

        private string FindIcaoByCityName(string cityName)
        {
            try
            {
                string airportsCsv = DetectAirportsCsv();

                if (string.IsNullOrWhiteSpace(airportsCsv) || !File.Exists(airportsCsv))
                    return null;

                using (var sr = new StreamReader(airportsCsv, Encoding.UTF8, true))
                {
                    string headerLine = sr.ReadLine();

                    if (string.IsNullOrWhiteSpace(headerLine))
                        return null;

                    var header = CsvSplit(headerLine);
                    var col = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                    for (int i = 0; i < header.Count; i++)
                    {
                        string h = (header[i] ?? "").Trim();

                        if (!col.ContainsKey(h))
                            col[h] = i;
                    }

                    int idxMunicipality = GetCol(col, "municipality", "city");
                    int idxName = GetCol(col, "name");
                    int idxGps = GetCol(col, "gps_code");
                    int idxIdent = GetCol(col, "ident");
                    int idxIcao = GetCol(col, "icao_code");

                    string wanted = cityName.Trim();

                    string line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        if (string.IsNullOrWhiteSpace(line))
                            continue;

                        var fields = CsvSplit(line);

                        string municipality = idxMunicipality >= 0 ? SafeGet(fields, idxMunicipality) : "";
                        string airportName = idxName >= 0 ? SafeGet(fields, idxName) : "";

                        bool cityMatches =
                            string.Equals(municipality, wanted, StringComparison.OrdinalIgnoreCase) ||
                            airportName.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) >= 0;

                        if (!cityMatches)
                            continue;

                        string icao = "";

                        if (idxIcao >= 0)
                            icao = SafeGet(fields, idxIcao);

                        if (string.IsNullOrWhiteSpace(icao) && idxGps >= 0)
                            icao = SafeGet(fields, idxGps);

                        if (string.IsNullOrWhiteSpace(icao) && idxIdent >= 0)
                            icao = SafeGet(fields, idxIdent);

                        icao = (icao ?? "").Trim().ToUpperInvariant();

                        if (icao.Length == 4 && icao.All(char.IsLetter))
                            return icao;
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private string DetectAirportsCsv()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            string[] candidates =
            {
                Path.Combine(baseDir, "airports.csv"),
                Path.Combine(baseDir, "Data", "airports.csv"),
                Path.Combine(baseDir, "Data", "Navdata", "airports.csv"),
                Path.Combine(baseDir, "Data", "NavData", "airports.csv")
            };

            foreach (string path in candidates)
            {
                if (File.Exists(path))
                    return path;
            }

            string dataRoot = Path.Combine(baseDir, "Data");

            if (Directory.Exists(dataRoot))
            {
                try
                {
                    return Directory
                        .EnumerateFiles(dataRoot, "airports.csv", SearchOption.AllDirectories)
                        .FirstOrDefault();
                }
                catch
                {
                }
            }

            return null;
        }

        private static int GetCol(Dictionary<string, int> col, params string[] names)
        {
            foreach (string name in names)
            {
                if (col.TryGetValue(name, out int index))
                    return index;
            }

            return -1;
        }

        private static string SafeGet(List<string> fields, int index)
        {
            if (index < 0 || index >= fields.Count)
                return "";

            return fields[index] ?? "";
        }

        private static List<string> CsvSplit(string line)
        {
            var result = new List<string>();

            if (line == null)
                return result;

            var sb = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }

                    continue;
                }

                if (c == ',' && !inQuotes)
                {
                    result.Add(sb.ToString());
                    sb.Clear();
                    continue;
                }

                sb.Append(c);
            }

            result.Add(sb.ToString());
            return result;
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
