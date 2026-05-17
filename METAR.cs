using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;

namespace Thesis_testing_1
{
    public partial class METAR : Form
    {
        private readonly HttpClient httpClient = new HttpClient();

        private const string CheckWxApiKey = "------------------";
        private const string RapidApiKey = "------------------";

        private Label MetarHeaderLabel;

        public METAR()
        {
            InitializeComponent();

            // EN: The form is kept fixed size because the layout was designed for this window size.
            // HU: Az ablak fix méretű, mert a felület ehhez az elrendezéshez készült.
            this.MaximizeBox = false;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;

            // EN: This label shows the selected airport and the observation time above the table.
            // HU: Ez a címke a kiválasztott repülőteret és a megfigyelés idejét mutatja a táblázat felett.
            MetarHeaderLabel = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Top,
                Height = 48,
                Padding = new Padding(4, 6, 4, 6),
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft
            };

            Controls.Add(MetarHeaderLabel);
            MetarHeaderLabel.BringToFront();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            // EN: Enables city name suggestions in the input field.
            // HU: Bekapcsolja a városnév-javaslatokat a beviteli mezőben.
            ConfigureCityAutocomplete(ICAOTextBox);
        }

        private async void DataFetchButton_Click(object sender, EventArgs e)
        {
            string userInput = ICAOTextBox.Text.Trim().ToUpperInvariant();

            if (string.IsNullOrWhiteSpace(userInput))
            {
                MessageBox.Show("Please enter a city name, ICAO, or IATA code.");
                return;
            }

            AirportComboBox.Items.Clear();
            AirportComboBox.SelectedIndex = -1;

            try
            {
                string icaoCode = userInput;

                // EN: If the user enters a 4-letter ICAO code, the METAR can be requested directly.
                // HU: Ha a felhasználó 4 betűs ICAO-kódot ad meg, a METAR közvetlenül lekérhető.
                if (userInput.Length == 4 && userInput.All(char.IsLetter))
                {
                    await FetchAndDisplayMetar(icaoCode);
                    return;
                }

                MetarData.Rows.Clear();
                MetarData.Refresh();

                // EN: If the input is a city name, possible airports are searched first.
                // HU: Ha a bemenet városnév, először a lehetséges repülőterek keresése történik.
                List<AirportOption> airports = await FindAirportsWithWxAsync(userInput);

                if (airports.Count == 0)
                {
                    MessageBox.Show("No airports with METAR/TAF found for \"" + userInput + "\".");
                    return;
                }

                foreach (AirportOption ap in airports)
                    AirportComboBox.Items.Add(ap);

                AirportComboBox.SelectedIndex = 0;

                icaoCode = airports[0].Icao;
                ICAOTextBox.Text = icaoCode;

                await FetchAndDisplayMetar(icaoCode);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error: " + ex.Message);
            }
        }

        private async Task FetchAndDisplayMetar(string icao)
        {
            try
            {
                // EN: The decoded METAR data is requested from the CheckWX API.
                // HU: A dekódolt METAR adatok a CheckWX API-ból kerülnek lekérdezésre.
                string json = await GetMetarJson(icao);

                if (string.IsNullOrWhiteSpace(json))
                {
                    MessageBox.Show("Empty response from METAR API.");
                    return;
                }

                JObject root = JObject.Parse(json);

                JArray dataArray = root["data"] as JArray;

                if (dataArray == null || dataArray.Count == 0)
                {
                    MessageBox.Show("No METAR data found for " + icao + ".");
                    return;
                }

                JObject metar = dataArray[0] as JObject;

                if (metar == null)
                {
                    MessageBox.Show("Unexpected METAR format.");
                    return;
                }

                // EN: The airport name is taken from the METAR response when available.
                // HU: A repülőtér neve lehetőség szerint a METAR válaszból kerül kiolvasásra.
                string name =
                    GetTokenString(metar, "station.name") ??
                    GetTokenString(metar, "location.name");

                // EN: If the METAR response does not contain a name, AeroDataBox is used as a fallback.
                // HU: Ha a METAR válasz nem tartalmaz nevet, akkor az AeroDataBox szolgál tartalékként.
                if (string.IsNullOrWhiteSpace(name))
                    name = await GetAirportNameFromAeroDataBox(icao) ?? "-";

                string rawText = GetRawMetarText(metar);
                string icaoFromMetar = GetTokenString(metar, "icao") ?? ExtractIcaoFromRaw(rawText) ?? icao;

                string observedIso = GetTokenString(metar, "observed");
                string observedFmt = FormatObservedTime(rawText, observedIso);

                Text = "METAR — " + icaoFromMetar + " / " + name;
                MetarHeaderLabel.Text = icaoFromMetar + " — " + name + " | Observed: " + observedFmt;

                PopulateDataGridView(metar, name, rawText, observedFmt, icaoFromMetar);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error fetching METAR:\n" + ex.Message);
            }
        }

        private async Task<string> GetMetarJson(string icao)
        {
            string url = "https://api.checkwx.com/metar/" + icao + "/decoded";

            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("X-API-Key", CheckWxApiKey);
                return await client.GetStringAsync(url);
            }
        }

        private async Task<List<AirportOption>> FindAirportsWithWxAsync(string query)
        {
            List<AirportOption> result = new List<AirportOption>();

            string url = "https://aerodatabox.p.rapidapi.com/airports/search/term?q="
                         + Uri.EscapeDataString(query)
                         + "&limit=10";

            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                request.Headers.Add("x-rapidapi-key", RapidApiKey);
                request.Headers.Add("x-rapidapi-host", "aerodatabox.p.rapidapi.com");

                HttpResponseMessage response = await httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                string json = await response.Content.ReadAsStringAsync();
                JObject obj = JObject.Parse(json);

                JArray items = obj["items"] as JArray;

                if (items == null || items.Count == 0)
                    return result;

                // EN: The found airports are converted into combo box items.
                // HU: A megtalált repülőterek a legördülő lista elemeivé alakulnak.
                foreach (JToken item in items)
                {
                    string itemIcao = GetTokenString(item, "icao");

                    if (string.IsNullOrWhiteSpace(itemIcao))
                        continue;

                    string name = GetTokenString(item, "name") ?? "";
                    string city = GetTokenString(item, "location.city") ?? "";

                    result.Add(new AirportOption
                    {
                        Icao = itemIcao,
                        Name = name,
                        City = city
                    });
                }
            }

            if (result.Count == 0)
                return result;

            string csv = string.Join(",", result.Select(a => a.Icao));

            // EN: Only airports with both METAR and TAF data are kept.
            // HU: Csak azok a repülőterek maradnak meg, amelyekhez METAR és TAF adat is elérhető.
            HashSet<string> metarStations = await GetStationsWithDataAsync("metar", csv);
            HashSet<string> tafStations = await GetStationsWithDataAsync("taf", csv);

            HashSet<string> validSet = new HashSet<string>(
                metarStations.Intersect(tafStations),
                StringComparer.OrdinalIgnoreCase);

            return result.Where(a => validSet.Contains(a.Icao)).ToList();
        }

        private async Task<HashSet<string>> GetStationsWithDataAsync(string type, string stationsCsv)
        {
            HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                string url = "https://api.checkwx.com/" + type + "/" + stationsCsv + "/decoded";

                using (HttpClient client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("X-API-Key", CheckWxApiKey);

                    HttpResponseMessage resp = await client.GetAsync(url);

                    if (!resp.IsSuccessStatusCode)
                        return set;

                    string json = await resp.Content.ReadAsStringAsync();
                    JObject obj = JObject.Parse(json);
                    JArray data = obj["data"] as JArray;

                    if (data == null)
                        return set;

                    foreach (JToken entry in data)
                    {
                        string itemIcao = GetTokenString(entry, "icao");

                        if (!string.IsNullOrWhiteSpace(itemIcao))
                            set.Add(itemIcao);
                    }
                }
            }
            catch
            {
            }

            return set;
        }

        private async Task<string> GetAirportNameFromAeroDataBox(string icao)
        {
            try
            {
                string url = "https://aerodatabox.p.rapidapi.com/airports/icao/" + icao;

                using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    request.Headers.Add("x-rapidapi-key", RapidApiKey);
                    request.Headers.Add("x-rapidapi-host", "aerodatabox.p.rapidapi.com");

                    HttpResponseMessage response = await httpClient.SendAsync(request);
                    response.EnsureSuccessStatusCode();

                    string json = await response.Content.ReadAsStringAsync();
                    JObject obj = JObject.Parse(json);

                    return GetTokenString(obj, "name");
                }
            }
            catch
            {
                return null;
            }
        }

        private void PopulateDataGridView(
            JObject metar,
            string name,
            string rawText,
            string observedFmt,
            string icaoFromMetar)
        {
            MetarData.Rows.Clear();
            MetarData.ColumnCount = 2;
            MetarData.Columns[0].Name = "Property";
            MetarData.Columns[1].Name = "Value";
            MetarData.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

            Action<string, string> AddRow = delegate (string key, string value)
            {
                MetarData.Rows.Add(key, string.IsNullOrWhiteSpace(value) ? "-" : value);
            };

            // EN: Basic airport and observation information.
            // HU: Alap repülőtéri és megfigyelési adatok.
            AddRow("Observed", observedFmt);
            AddRow("ICAO", icaoFromMetar);
            AddRow("Name", name);

            // EN: The important METAR fields are shown in a readable form.
            // HU: A fontosabb METAR mezők áttekinthető formában jelennek meg.
            AddRow("Pressure", ParsePressure(rawText, metar));
            AddRow("Wind", ParseWind(rawText, metar));
            AddRow("Temperature", ParseTemperature(rawText, metar));
            AddRow("Dewpoint", ParseDewpoint(rawText, metar));
            AddRow("Humidity", ParseHumidity(metar));
            AddRow("Cloud", ParseClouds(rawText, metar));
            AddRow("Visibility", ParseVisibility(rawText, metar));
            AddRow("Weather", ParseWeatherFromRawText(rawText));
            AddRow("Raw METAR", rawText);
        }

        private string GetRawMetarText(JObject metar)
        {
            // EN: Different APIs may use different names for the raw METAR text.
            // HU: Különböző API-válaszokban a nyers METAR szöveg eltérő néven szerepelhet.
            return GetTokenString(metar, "raw_text") ??
                   GetTokenString(metar, "raw") ??
                   GetTokenString(metar, "text") ??
                   GetTokenString(metar, "message") ??
                   "";
        }

        private string ExtractIcaoFromRaw(string rawText)
        {
            if (string.IsNullOrWhiteSpace(rawText))
                return null;

            Match m = Regex.Match(rawText, @"\b(?:METAR|SPECI)\s+(?<icao>[A-Z]{4})\b");

            if (m.Success)
                return m.Groups["icao"].Value;

            return null;
        }

        private string FormatObservedTime(string rawText, string observedIso)
        {
            DateTimeOffset dto;

            // EN: The time group from the raw METAR is preferred because it is already in UTC.
            // HU: A nyers METAR időcsoportja az elsődleges, mert eleve UTC időt tartalmaz.
            if (TryParseObservedFromRaw(rawText, observedIso, out dto))
                return dto.ToString("dd MMM HH:mm'Z'", CultureInfo.InvariantCulture).ToUpperInvariant();

            if (TryParseUtc(observedIso, out dto))
                return dto.ToString("dd MMM HH:mm'Z'", CultureInfo.InvariantCulture).ToUpperInvariant();

            return "-";
        }

        private bool TryParseObservedFromRaw(string rawText, string observedIso, out DateTimeOffset dto)
        {
            dto = new DateTimeOffset();

            if (string.IsNullOrWhiteSpace(rawText))
                return false;

            // EN: Example: 101320Z means day 10, 13:20 UTC.
            // HU: Példa: a 101320Z jelentése 10. nap, 13:20 UTC.
            Match m = Regex.Match(rawText, @"\b(?<day>\d{2})(?<hour>\d{2})(?<min>\d{2})Z\b");

            if (!m.Success)
                return false;

            int day = int.Parse(m.Groups["day"].Value, CultureInfo.InvariantCulture);
            int hour = int.Parse(m.Groups["hour"].Value, CultureInfo.InvariantCulture);
            int minute = int.Parse(m.Groups["min"].Value, CultureInfo.InvariantCulture);

            DateTimeOffset reference;

            if (!TryParseUtc(observedIso, out reference))
                reference = DateTimeOffset.UtcNow;

            int year = reference.Year;
            int month = reference.Month;
            int daysInMonth = DateTime.DaysInMonth(year, month);

            if (day > daysInMonth)
                day = daysInMonth;

            dto = new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero);

            if (day < reference.Day - 15)
                dto = dto.AddMonths(1);

            return true;
        }

        private string ParsePressure(string rawText, JObject metar)
        {
            // EN: Q1016 in the raw METAR means QNH 1016 hPa.
            // HU: A nyers METAR-ban a Q1016 jelentése QNH 1016 hPa.
            if (!string.IsNullOrWhiteSpace(rawText))
            {
                Match q = Regex.Match(rawText, @"\bQ(?<qnh>\d{4})\b");

                if (q.Success)
                    return int.Parse(q.Groups["qnh"].Value, CultureInfo.InvariantCulture) + " hPa";
            }

            string hpa =
                GetTokenString(metar, "barometer.hpa") ??
                GetTokenString(metar, "barometer.mb");

            if (!string.IsNullOrWhiteSpace(hpa))
                return hpa + " hPa";

            return "-";
        }

        private string ParseWind(string rawText, JObject metar)
        {
            string windText = "";

            if (!string.IsNullOrWhiteSpace(rawText))
            {
                // EN: Wind is parsed from the raw METAR to keep the original aviation format.
                // HU: A széladat a nyers METAR-ból kerül kiolvasásra, hogy megmaradjon az eredeti repülési forma.
                Match windMatch = Regex.Match(
                    rawText,
                    @"\b(?<dir>VRB|\d{3})(?<speed>\d{2,3})(G(?<gust>\d{2,3}))?KT\b");

                if (windMatch.Success)
                {
                    string dir = windMatch.Groups["dir"].Value;
                    string speed = windMatch.Groups["speed"].Value;
                    string gust = windMatch.Groups["gust"].Success ? windMatch.Groups["gust"].Value : "";

                    if (dir == "000" && (speed == "00" || speed == "0"))
                        windText = "Calm";
                    else if (dir == "VRB")
                        windText = "Variable at " + int.Parse(speed, CultureInfo.InvariantCulture) + " kt";
                    else
                        windText = dir + "° at " + int.Parse(speed, CultureInfo.InvariantCulture) + " kt";

                    if (!string.IsNullOrWhiteSpace(gust))
                        windText += ", gusting " + int.Parse(gust, CultureInfo.InvariantCulture) + " kt";

                    // EN: Example: 010V080 means the wind direction varies between 010° and 080°.
                    // HU: Példa: a 010V080 azt jelenti, hogy a szélirány 010° és 080° között változik.
                    Match variableMatch = Regex.Match(rawText, @"\b(?<from>\d{3})V(?<to>\d{3})\b");

                    if (variableMatch.Success)
                    {
                        windText += " (variable between "
                                    + variableMatch.Groups["from"].Value
                                    + "° and "
                                    + variableMatch.Groups["to"].Value
                                    + "°)";
                    }

                    return windText;
                }
            }

            // EN: If raw parsing does not work, the decoded JSON fields are used.
            // HU: Ha a nyers szövegből nem sikerül kiolvasni, akkor a dekódolt JSON mezők kerülnek használatra.
            JToken windToken = metar["wind"];

            if (windToken != null && windToken.Type == JTokenType.Object)
            {
                string dir = GetTokenString(windToken, "degrees");
                string speed =
                    GetTokenString(windToken, "speed_kts") ??
                    GetTokenString(windToken, "speed.kts");
                string gust =
                    GetTokenString(windToken, "gust_kts") ??
                    GetTokenString(windToken, "gust.kts");

                if (dir == "000" && (speed == "00" || speed == "0"))
                    windText = "Calm";
                else if (dir == "VRB")
                    windText = "Variable at " + speed + " kt";
                else if (!string.IsNullOrWhiteSpace(dir) && !string.IsNullOrWhiteSpace(speed))
                    windText = dir + "° at " + speed + " kt";
                else if (!string.IsNullOrWhiteSpace(speed))
                    windText = speed + " kt";

                if (!string.IsNullOrWhiteSpace(gust) && gust != "0" && gust != "00")
                    windText += ", gusting " + gust + " kt";
            }

            return string.IsNullOrWhiteSpace(windText) ? "-" : windText;
        }

        private string ParseTemperature(string rawText, JObject metar)
        {
            int temp;
            int dew;

            // EN: Temperature and dew point usually appear together, for example 12/05.
            // HU: A hőmérséklet és a harmatpont általában együtt szerepel, például 12/05.
            if (TryParseTempDewFromRaw(rawText, out temp, out dew))
                return temp + " °C";

            string jsonTemp = GetTokenString(metar, "temperature.celsius");

            if (!string.IsNullOrWhiteSpace(jsonTemp))
                return jsonTemp + " °C";

            return "-";
        }

        private string ParseDewpoint(string rawText, JObject metar)
        {
            int temp;
            int dew;

            if (TryParseTempDewFromRaw(rawText, out temp, out dew))
                return dew + " °C";

            string jsonDew = GetTokenString(metar, "dewpoint.celsius");

            if (!string.IsNullOrWhiteSpace(jsonDew))
                return jsonDew + " °C";

            return "-";
        }

        private bool TryParseTempDewFromRaw(string rawText, out int temp, out int dew)
        {
            temp = 0;
            dew = 0;

            if (string.IsNullOrWhiteSpace(rawText))
                return false;

            Match m = Regex.Match(rawText, @"\b(?<t>M?\d{2})/(?<d>M?\d{2})\b");

            if (!m.Success)
                return false;

            temp = ParseMetarSignedTemp(m.Groups["t"].Value);
            dew = ParseMetarSignedTemp(m.Groups["d"].Value);

            return true;
        }

        private int ParseMetarSignedTemp(string value)
        {
            // EN: In METAR reports, M means minus temperature.
            // HU: A METAR jelentésekben az M mínusz hőmérsékletet jelent.
            if (value.StartsWith("M"))
                return -int.Parse(value.Substring(1), CultureInfo.InvariantCulture);

            return int.Parse(value, CultureInfo.InvariantCulture);
        }

        private string ParseHumidity(JObject metar)
        {
            JToken humToken = metar["humidity"];

            if (humToken == null)
                return "-";

            string humidity;

            if (humToken.Type == JTokenType.Object)
                humidity = GetTokenString(humToken, "percent");
            else
                humidity = humToken.ToString();

            if (string.IsNullOrWhiteSpace(humidity))
                return "-";

            return humidity + " %";
        }

        private string ParseClouds(string rawText, JObject metar)
        {
            // EN: Clouds are first parsed from the raw METAR because it keeps codes like OVC029.
            // HU: A felhőzet először a nyers METAR-ból kerül feldolgozásra, mert abban megmaradnak az OVC029 típusú kódok.
            string fromRaw = ParseCloudsFromRaw(rawText);

            if (!string.IsNullOrWhiteSpace(fromRaw))
                return fromRaw;

            JToken cloudsToken = metar["clouds"];

            if (cloudsToken == null || cloudsToken.Type != JTokenType.Array || !cloudsToken.HasValues)
                return "Clear skies";

            List<string> clouds = new List<string>();

            foreach (JToken cloud in cloudsToken)
            {
                string code = GetTokenString(cloud, "code");
                string text = GetTokenString(cloud, "text");
                string feet = GetTokenString(cloud, "feet");

                if (string.IsNullOrWhiteSpace(text))
                    text = CloudCodeToText(code);

                if (string.IsNullOrWhiteSpace(text))
                    continue;

                if (text.ToUpperInvariant().Contains("CLEAR") ||
                    text == "CLR" || text == "SKC" || text == "NSC")
                {
                    clouds.Add("Clear skies");
                }
                else if (!string.IsNullOrWhiteSpace(feet))
                {
                    clouds.Add(text + " at " + feet + " ft");
                }
                else
                {
                    clouds.Add(text);
                }
            }

            return clouds.Count == 0 ? "Clear skies" : string.Join("; ", clouds);
        }

        private string ParseCloudsFromRaw(string rawText)
        {
            if (string.IsNullOrWhiteSpace(rawText))
                return "";

            if (Regex.IsMatch(rawText, @"\b(CLR|SKC|NSC|NCD)\b"))
                return "Clear skies";

            List<string> clouds = new List<string>();

            // EN: Cloud height is given in hundreds of feet, so OVC029 becomes 2900 ft.
            // HU: A felhőalap száz lábban van megadva, ezért az OVC029 jelentése 2900 ft.
            MatchCollection matches = Regex.Matches(
                rawText,
                @"\b(?<code>FEW|SCT|BKN|OVC|VV)(?<height>\d{3})(?<extra>CB|TCU)?\b");

            foreach (Match m in matches)
            {
                string code = m.Groups["code"].Value;
                int hundreds = int.Parse(m.Groups["height"].Value, CultureInfo.InvariantCulture);
                int feet = hundreds * 100;

                string text = CloudCodeToText(code) + " at " + feet.ToString("N0", CultureInfo.InvariantCulture) + " ft";

                if (m.Groups["extra"].Success)
                    text += " " + m.Groups["extra"].Value;

                clouds.Add(text);
            }

            return clouds.Count == 0 ? "" : string.Join("; ", clouds);
        }

        private string ParseVisibility(string rawText, JObject metar)
        {
            if (!string.IsNullOrWhiteSpace(rawText))
            {
                // EN: CAVOK means good visibility and no significant clouds or weather.
                // HU: A CAVOK jó látástávolságot, valamint jelentős felhőzet és időjárás hiányát jelenti.
                if (Regex.IsMatch(rawText, @"\bCAVOK\b"))
                    return "10 km or more (CAVOK)";

                // EN: American METAR reports often use statute miles.
                // HU: Az amerikai METAR jelentések gyakran statute mile egységet használnak.
                Match smMatch = Regex.Match(rawText, @"\b(P?\d+\s\d+/\d+|P?\d+/\d+|P?\d+)SM\b");

                if (smMatch.Success)
                {
                    string smRaw = smMatch.Value.Replace("SM", "").Trim();

                    if (smRaw.StartsWith("P"))
                        return smRaw.Substring(1) + "+ SM";

                    return smRaw + " SM";
                }

                // EN: In many European METAR reports, 9999 means 10 km or more.
                // HU: Sok európai METAR jelentésben a 9999 jelentése 10 km vagy annál nagyobb látástávolság.
                Match metricMatch = Regex.Match(rawText, @"\b(?<vis>\d{4})\b");

                if (metricMatch.Success)
                {
                    string vis = metricMatch.Groups["vis"].Value;

                    if (vis == "9999")
                        return "10 km or more";

                    int meters;

                    if (int.TryParse(vis, out meters))
                        return meters.ToString("N0", CultureInfo.InvariantCulture) + " m";
                }
            }

            JToken visToken = metar["visibility"];

            if (visToken != null && visToken.Type == JTokenType.Object)
            {
                string metersStr =
                    GetTokenString(visToken, "meters_float") ??
                    GetTokenString(visToken, "meters");

                double meters;

                if (double.TryParse(metersStr, NumberStyles.Any, CultureInfo.InvariantCulture, out meters))
                    return meters >= 10000
                        ? "10 km or more"
                        : meters.ToString("N0", CultureInfo.InvariantCulture) + " m";

                string text = GetTokenString(visToken, "text");

                if (!string.IsNullOrWhiteSpace(text))
                    return text;
            }
            else if (visToken != null)
            {
                string rawVis = visToken.ToString();

                if (rawVis == "9999")
                    return "10 km or more";

                return rawVis;
            }

            return "-";
        }

        private string ParseWeatherFromRawText(string rawText)
        {
            if (string.IsNullOrWhiteSpace(rawText))
                return "No METAR data";

            string cleaned = " " + rawText.ToUpperInvariant() + " ";
            List<string> weather = new List<string>();

            // EN: Common weather codes are translated into readable text.
            // HU: A gyakoribb időjárási kódok olvasható szöveggé alakulnak.
            if (Regex.IsMatch(cleaned, @"\s\+RA\s")) weather.Add("Heavy rain");
            else if (Regex.IsMatch(cleaned, @"\s-RA\s")) weather.Add("Light rain");
            else if (Regex.IsMatch(cleaned, @"\sRA\s")) weather.Add("Rain");

            if (Regex.IsMatch(cleaned, @"\sSHRA\s")) weather.Add("Rain showers");
            if (Regex.IsMatch(cleaned, @"\sSN\s")) weather.Add("Snow");
            if (Regex.IsMatch(cleaned, @"\sFG\s")) weather.Add("Fog");
            if (Regex.IsMatch(cleaned, @"\sBR\s")) weather.Add("Mist");
            if (Regex.IsMatch(cleaned, @"\sHZ\s")) weather.Add("Haze");
            if (Regex.IsMatch(cleaned, @"\sTS\s")) weather.Add("Thunderstorm");
            if (Regex.IsMatch(cleaned, @"\sDZ\s")) weather.Add("Drizzle");

            if (weather.Count == 0)
                return "No significant weather reported";

            return string.Join("; ", weather);
        }

        private string CloudCodeToText(string code)
        {
            switch ((code ?? "").ToUpperInvariant())
            {
                case "SKC": return "Sky clear";
                case "CLR": return "Clear";
                case "NSC": return "No significant clouds";
                case "NCD": return "No clouds detected";
                case "FEW": return "Few clouds";
                case "SCT": return "Scattered clouds";
                case "BKN": return "Broken clouds";
                case "OVC": return "Overcast";
                case "VV": return "Vertical visibility";
                default: return code;
            }
        }

        private void ConfigureCityAutocomplete(TextBox textBox)
        {
            AutoCompleteStringCollection src = new AutoCompleteStringCollection();

            if (CityData.Cities != null && CityData.Cities.Count > 0)
                src.AddRange(CityData.Cities.ToArray());

            textBox.AutoCompleteCustomSource = src;
            textBox.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            textBox.AutoCompleteSource = AutoCompleteSource.CustomSource;

            textBox.TextChanged -= CityTextBox_TextChanged;
            textBox.TextChanged += CityTextBox_TextChanged;
        }

        private void AirportComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            AirportOption ap = AirportComboBox.SelectedItem as AirportOption;

            if (ap != null)
                ICAOTextBox.Text = ap.Icao;
        }

        private void CityTextBox_TextChanged(object sender, EventArgs e)
        {
            TextBox tb = sender as TextBox;

            if (tb == null)
                return;

            string t = tb.Text ?? string.Empty;

            // EN: Autocomplete is disabled for short uppercase inputs because they are probably airport codes.
            // HU: Rövid nagybetűs bemenetnél az automatikus kiegészítés kikapcsol, mert az valószínűleg repülőtéri kód.
            bool looksLikeCode =
                t.Length >= 2 &&
                t.Length <= 4 &&
                t.All(ch => char.IsLetter(ch) && char.IsUpper(ch));

            if (looksLikeCode)
            {
                if (tb.AutoCompleteMode != AutoCompleteMode.None)
                {
                    tb.AutoCompleteMode = AutoCompleteMode.None;
                    tb.AutoCompleteSource = AutoCompleteSource.None;
                }
            }
            else
            {
                if (tb.AutoCompleteMode != AutoCompleteMode.SuggestAppend)
                {
                    tb.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
                    tb.AutoCompleteSource = AutoCompleteSource.CustomSource;
                }
            }
        }

        private void MetarData_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
        }

        private string FormatIsoTimeZ(string iso)
        {
            DateTimeOffset dto;

            if (TryParseUtc(iso, out dto))
                return dto.ToString("dd MMM HH:mm'Z'", CultureInfo.InvariantCulture).ToUpperInvariant();

            return "-";
        }

        private bool TryParseUtc(string iso, out DateTimeOffset dto)
        {
            dto = new DateTimeOffset();

            if (string.IsNullOrWhiteSpace(iso))
                return false;

            DateTimeOffset parsedOffset;

            if (DateTimeOffset.TryParse(
                iso,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out parsedOffset))
            {
                dto = parsedOffset.ToUniversalTime();
                return true;
            }

            DateTime parsedDate;

            if (DateTime.TryParse(
                iso,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out parsedDate))
            {
                DateTime utc = DateTime.SpecifyKind(parsedDate, DateTimeKind.Utc);
                dto = new DateTimeOffset(utc);
                return true;
            }

            return false;
        }

        private string GetTokenString(JToken token, string path)
        {
            if (token == null || string.IsNullOrWhiteSpace(path))
                return null;

            JToken current = token;
            string[] parts = path.Split('.');

            foreach (string part in parts)
            {
                if (current == null)
                    return null;

                current = current[part];
            }

            if (current == null || current.Type == JTokenType.Null)
                return null;

            string value = current.ToString();

            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        private class AirportOption
        {
            public string Icao { get; set; }
            public string Name { get; set; }
            public string City { get; set; }

            public override string ToString()
            {
                if (!string.IsNullOrWhiteSpace(City))
                    return Icao + " – " + City + " / " + Name;

                return Icao + " – " + Name;
            }
        }
    }
}
