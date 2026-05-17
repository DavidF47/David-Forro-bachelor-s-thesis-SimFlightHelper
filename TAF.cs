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
    public partial class TAF : Form
    {
        // HTTP kliens az API kérésekhez / HTTP client for API requests
        private readonly HttpClient httpClient = new HttpClient();

        // API kulcsok a CheckWX és RapidAPI szolgáltatásokhoz / API keys for the CheckWX and RapidAPI services
        private const string CheckWxApiKey = "-----------------------";
        private const string RapidApiKey = ""-----------------------";";

        // Fejléc címke a TAF adatok megjelenítéséhez / Header label for displaying TAF information
        private Label TafHeaderLabel;

        public TAF()
        {
            InitializeComponent();

            // Az ablak maximalizálásának letiltása / Disables maximizing the window
            this.MaximizeBox = false;

            // Fix méretű ablakkeret beállítása / Sets a fixed-size window border
            this.FormBorderStyle = FormBorderStyle.FixedSingle;

            // Fejléc címke létrehozása és beállítása / Creates and configures the header label
            TafHeaderLabel = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Top,
                Height = 48,
                Padding = new Padding(4, 6, 4, 6),
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft
            };

            // A fejléc címke hozzáadása az ablakhoz / Adds the header label to the form
            Controls.Add(TafHeaderLabel);

            // A fejléc címke előtérbe helyezése / Brings the header label to the front
            TafHeaderLabel.BringToFront();

            // A TAF táblázat alapbeállításai / Basic settings of the TAF grid
            TafGrid.ReadOnly = true;
            TafGrid.AllowUserToAddRows = false;
            TafGrid.AllowUserToResizeRows = false;
            TafGrid.RowHeadersVisible = false;
            TafGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            TafGrid.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
            TafGrid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            TafGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            TafGrid.MultiSelect = false;

            // A TAF táblázat oszlopainak létrehozása / Creates the columns of the TAF grid
            TafGrid.Columns.Clear();
            TafGrid.Columns.Add("Type", "Type");
            TafGrid.Columns.Add("From", "From");
            TafGrid.Columns.Add("To", "To");
            TafGrid.Columns.Add("Wind", "Wind");
            TafGrid.Columns.Add("Vis", "Visibility");
            TafGrid.Columns.Add("Wx", "Weather");
            TafGrid.Columns.Add("Clouds", "Clouds");
            TafGrid.Columns.Add("Prob", "Prob");
        }

        private void TAF_Load(object sender, EventArgs e)
        {
            // Automatikus városkiegészítés beállítása az ICAO mezőhöz / Configures city autocomplete for the ICAO textbox
            ConfigureCityAutocomplete(ICAOTextBox);
        }

        private async void DataFetchButton_Click(object sender, EventArgs e)
        {
            // A felhasználói bevitel beolvasása és nagybetűssé alakítása / Reads the user input and converts it to uppercase
            string userInput = ICAOTextBox.Text.Trim().ToUpperInvariant();

            // Üres bevitel esetén figyelmeztetés megjelenítése / Shows a warning if the input is empty
            if (string.IsNullOrWhiteSpace(userInput))
            {
                MessageBox.Show("Please enter a city name, ICAO, or IATA code.");
                return;
            }

            // Korábbi repülőtér-választás és táblázat törlése / Clears previous airport selection and grid data
            ClearAirportSelectionBox();
            TafGrid.Rows.Clear();

            try
            {
                // Alapértelmezetten a bevitel ICAO kódként van kezelve / By default, the input is treated as an ICAO code
                string icao = userInput;

                // Ha a bevitel négy betűből áll, közvetlenül ICAO kódként használja / If the input has four letters, it is used directly as an ICAO code
                if (userInput.Length == 4 && userInput.All(char.IsLetter))
                {
                    await FetchAndDisplayTAF(icao);
                    return;
                }

                // Repülőterek keresése városnév, ICAO vagy IATA alapján / Searches airports by city name, ICAO, or IATA
                List<AirportOption> airports = await FindAirportsWithWxAsync(userInput);

                // Ha nincs megfelelő repülőtér, hibaüzenetet jelenít meg / Shows an error message if no matching airport is found
                if (airports.Count == 0)
                {
                    MessageBox.Show("No airports with METAR/TAF found for \"" + userInput + "\".");
                    return;
                }

                // A megtalált repülőterek hozzáadása a legördülő listához / Adds the found airports to the combo box
                foreach (AirportOption ap in airports)
                    AirportComboBox.Items.Add(ap);

                // Az első repülőtér kiválasztása / Selects the first airport
                AirportComboBox.SelectedIndex = 0;

                // Az első találat ICAO kódjának használata / Uses the ICAO code of the first result
                AirportOption first = airports[0];
                icao = first.Icao;
                ICAOTextBox.Text = icao;

                // TAF adatok lekérése és megjelenítése / Fetches and displays the TAF data
                await FetchAndDisplayTAF(icao);
            }
            catch (Exception ex)
            {
                // Általános hibaüzenet megjelenítése / Shows a general error message
                MessageBox.Show("Error: " + ex.Message);
            }
        }

        private void ClearAirportSelectionBox()
        {
            // Az eseménykezelő ideiglenes leválasztása / Temporarily detaches the event handler
            AirportComboBox.SelectedIndexChanged -= AirportComboBox_SelectedIndexChanged;

            // A legördülő lista tartalmának törlése / Clears the contents of the combo box
            AirportComboBox.Items.Clear();
            AirportComboBox.SelectedIndex = -1;
            AirportComboBox.Text = "";
            AirportComboBox.SelectedItem = null;

            // Az eseménykezelő visszacsatolása / Reattaches the event handler
            AirportComboBox.SelectedIndexChanged += AirportComboBox_SelectedIndexChanged;
        }

        private async Task FetchAndDisplayTAF(string icao)
        {
            try
            {
                // TAF JSON lekérése az adott ICAO kódhoz / Fetches TAF JSON for the given ICAO code
                string json = await GetTafJson(icao);

                // JSON szöveg feldolgozása objektummá / Parses the JSON text into an object
                JObject root = JObject.Parse(json);

                // Az első TAF adat kiválasztása / Selects the first TAF data item
                JToken taf = root["data"] != null ? root["data"].FirstOrDefault() : null;

                // Ha nincs TAF adat, figyelmeztetést jelenít meg / Shows a warning if no TAF data is available
                if (taf == null)
                {
                    MessageBox.Show("No TAF available for this station.");
                    return;
                }

                // Repülőtér nevének lekérése a TAF adatokból / Gets the airport name from the TAF data
                string name = GetTokenString(taf, "station.name");

                // Ha nincs név a TAF-ban, AeroDataBox alapján próbálja lekérni / If the name is missing in TAF, tries to get it from AeroDataBox
                if (string.IsNullOrWhiteSpace(name))
                    name = await GetAirportNameFromAeroDataBox(icao) ?? "-";

                // Előrejelzési időszakok lekérése / Gets the forecast periods
                JArray fcst = taf["forecast"] as JArray;

                // Ha nincs előrejelzési időszak, csak a fejlécet frissíti / If there are no forecast periods, only updates the header
                if (fcst == null || fcst.Count == 0)
                {
                    Text = "TAF data — " + icao + " / " + name;
                    TafHeaderLabel.Text = icao + " — " + name + "   |   No forecast periods present.";
                    TafGrid.Rows.Clear();
                    return;
                }

                // Ablakcím beállítása / Sets the window title
                Text = "TAF data — " + icao + " / " + name;

                // TAF érvényességi idő kiszámítása / Computes the TAF validity time
                Tuple<string, string> validity = ComputeValidity(taf, fcst);
                string validFrom = validity.Item1;
                string validTo = validity.Item2;

                // Fejléc szövegének összeállítása / Builds the header text
                TafHeaderLabel.Text = BuildHeaderText(icao, name, taf, validFrom, validTo);

                // Korábbi táblázatsorok törlése / Clears previous grid rows
                TafGrid.Rows.Clear();

                // Nyers TAF szöveg lekérése / Gets the raw TAF text
                string rawTaf = GetRawTafText(taf);

                // Kibocsátási idő lekérése / Gets the issued time
                DateTimeOffset issuedTime;
                TryParseUtc(GetTokenString(taf, "issued") ?? GetTokenString(taf, "timestamp.issued"), out issuedTime);

                // Nyers TAF változási csoportok feldolgozása / Parses raw TAF change groups
                List<RawChangeGroup> rawGroups = ParseRawTafChangeGroups(rawTaf, issuedTime);

                // A TAF befejezési idejének meghatározása / Determines the end time of the TAF
                string tafEndIso =
                    GetTokenString(taf, "period.to") ??
                    GetLastForecastEnd(fcst);

                // Alap időjárási értékek tárolása későbbi időszakokhoz / Stores base weather values for later periods
                string baseWind = "";
                string baseVis = "";
                string baseWx = "No significant weather";
                string baseClouds = "";

                // Előrejelzési időszakok feldolgozása / Processes the forecast periods
                for (int i = 0; i < fcst.Count; i++)
                {
                    // Aktuális és következő időszak kiválasztása / Selects the current and next period
                    JToken period = fcst[i];
                    JToken nextPeriod = i < fcst.Count - 1 ? fcst[i + 1] : null;

                    // Előrejelzés típusának és valószínűségének lekérése / Gets the forecast type and probability
                    string typeFromJson = GetForecastTypeFriendly(period);
                    string prob = GetProbability(period);

                    // Időszak kezdő és záró időpontjának lekérése / Gets the start and end time of the period
                    string fromIso = GetPeriodFromIso(period);
                    string toIso = GetPeriodToIso(period);

                    // Hiányzó záró idő esetén a következő időszak vagy a TAF vége lesz használva / Uses the next period or TAF end if the end time is missing
                    if (string.IsNullOrWhiteSpace(toIso))
                    {
                        toIso = GetPeriodFromIso(nextPeriod);

                        if (string.IsNullOrWhiteSpace(toIso))
                            toIso = tafEndIso;
                    }

                    // Előrejelzés típusának pontosítása a nyers TAF alapján / Refines the forecast type using the raw TAF
                    string type = GetForecastTypeFromRawGroups(rawGroups, fromIso, toIso, typeFromJson, prob);

                    // Időpontok olvasható formátumra alakítása / Converts times to readable format
                    string from = FormatIsoTimeZ(fromIso);
                    string to = string.IsNullOrWhiteSpace(toIso)
                        ? "Until end of TAF"
                        : FormatIsoTimeZ(toIso);

                    // Szél, látástávolság, időjárás és felhőzet formázása / Formats wind, visibility, weather, and clouds
                    string windRaw = FormatWindFriendly(period["wind"]);
                    string visRaw = FormatVisibilityFriendly(period);
                    string wxRaw = FormatConditionsFriendly(period["conditions"]);
                    string cloudsRaw = FormatCloudsFriendly(period["clouds"]);

                    // Annak meghatározása, hogy ideiglenes vagy alapot módosító előrejelzésről van-e szó / Determines whether the forecast is temporary or changes the base conditions
                    bool isTemporary = type.Contains("TEMPO") || type.Contains("PROB");
                    bool changesBase = type == "BASE" || type == "BECMG" || type == "FM";

                    // Hiányzó értékek pótlása az alapértékekkel / Fills missing values using the base values
                    string wind = !string.IsNullOrWhiteSpace(windRaw) ? windRaw : baseWind;
                    string vis = !string.IsNullOrWhiteSpace(visRaw) ? visRaw : baseVis;
                    string wx = !string.IsNullOrWhiteSpace(wxRaw) ? wxRaw : baseWx;
                    string clouds = !string.IsNullOrWhiteSpace(cloudsRaw) ? cloudsRaw : baseClouds;

                    // Alapértelmezett szélérték beállítása / Sets the default wind value
                    if (string.IsNullOrWhiteSpace(wind))
                        wind = "—";

                    // Alapértelmezett látástávolság beállítása / Sets the default visibility value
                    if (string.IsNullOrWhiteSpace(vis))
                        vis = "—";

                    // Alapértelmezett időjárási érték beállítása / Sets the default weather value
                    if (string.IsNullOrWhiteSpace(wx))
                        wx = "No significant weather";

                    // Alapértelmezett felhőzet beállítása / Sets the default cloud value
                    if (string.IsNullOrWhiteSpace(clouds))
                        clouds = "NSC";

                    // Az előrejelzési időszak hozzáadása a táblázathoz / Adds the forecast period to the grid
                    TafGrid.Rows.Add(type, from, to, wind, vis, wx, clouds, prob);

                    // Az alapértékek frissítése, ha az időszak nem ideiglenes / Updates the base values if the period is not temporary
                    if (changesBase && !isTemporary)
                    {
                        if (!string.IsNullOrWhiteSpace(windRaw))
                            baseWind = windRaw;

                        if (!string.IsNullOrWhiteSpace(visRaw))
                            baseVis = visRaw;

                        if (!string.IsNullOrWhiteSpace(wxRaw))
                            baseWx = wxRaw;
                        else if (type == "BECMG" || type == "FM")
                            baseWx = "No significant weather";

                        if (!string.IsNullOrWhiteSpace(cloudsRaw))
                            baseClouds = cloudsRaw;
                    }
                }
            }
            catch (HttpRequestException ex)
            {
                // Hálózati hiba kezelése / Handles network errors
                MessageBox.Show("Error fetching TAF (network): " + ex.Message);
            }
            catch (Exception ex)
            {
                // Általános TAF lekérési hiba kezelése / Handles general TAF fetching errors
                MessageBox.Show("Error fetching TAF: " + ex.Message);
            }
        }

        private async Task<string> GetTafJson(string icao)
        {
            // CheckWX TAF API URL összeállítása / Builds the CheckWX TAF API URL
            string url = "https://api.checkwx.com/v2/taf/" + icao + "/decoded";

            using (HttpClient c = new HttpClient())
            {
                // API kulcs hozzáadása a kérés fejlécéhez / Adds the API key to the request header
                c.DefaultRequestHeaders.Add("X-API-Key", CheckWxApiKey);

                // JSON válasz lekérése / Fetches the JSON response
                return await c.GetStringAsync(url);
            }
        }

        private async Task<List<AirportOption>> FindAirportsWithWxAsync(string query)
        {
            // Eredménylista létrehozása / Creates the result list
            List<AirportOption> result = new List<AirportOption>();

            // AeroDataBox keresési URL összeállítása / Builds the AeroDataBox search URL
            string url = "https://aerodatabox.p.rapidapi.com/airports/search/term?q="
                         + Uri.EscapeDataString(query)
                         + "&limit=10";

            using (HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, url))
            {
                // RapidAPI fejlécek hozzáadása / Adds RapidAPI headers
                req.Headers.Add("x-rapidapi-key", RapidApiKey);
                req.Headers.Add("x-rapidapi-host", "aerodatabox.p.rapidapi.com");

                // Kérés elküldése és válasz ellenőrzése / Sends the request and checks the response
                HttpResponseMessage resp = await httpClient.SendAsync(req);
                resp.EnsureSuccessStatusCode();

                // JSON válasz beolvasása és feldolgozása / Reads and parses the JSON response
                string json = await resp.Content.ReadAsStringAsync();
                JObject obj = JObject.Parse(json);

                // Találatok lekérése / Gets the result items
                JArray items = obj["items"] as JArray;

                // Üres találati lista esetén üres eredmény visszaadása / Returns an empty result if there are no items
                if (items == null || items.Count == 0)
                    return result;

                // Repülőtéri találatok feldolgozása / Processes airport search results
                foreach (JToken item in items)
                {
                    // ICAO kód lekérése / Gets the ICAO code
                    string itemIcao = GetTokenString(item, "icao");

                    // ICAO nélküli találatok kihagyása / Skips results without an ICAO code
                    if (string.IsNullOrWhiteSpace(itemIcao))
                        continue;

                    // Név és város lekérése / Gets the name and city
                    string name = GetTokenString(item, "name") ?? "";
                    string city = GetTokenString(item, "location.city") ?? "";

                    // Repülőtér hozzáadása az eredménylistához / Adds the airport to the result list
                    result.Add(new AirportOption
                    {
                        Icao = itemIcao,
                        Name = name,
                        City = city
                    });
                }
            }

            // Ha nincs találat, visszatér az üres listával / Returns the empty list if there are no results
            if (result.Count == 0)
                return result;

            // ICAO kódok vesszővel elválasztott listájának létrehozása / Creates a comma-separated list of ICAO codes
            string csv = string.Join(",", result.Select(a => a.Icao));

            // METAR és TAF adatokkal rendelkező állomások lekérése / Gets stations that have METAR and TAF data
            HashSet<string> metarStations = await GetStationsWithDataAsync("metar", csv);
            HashSet<string> tafStations = await GetStationsWithDataAsync("taf", csv);

            // Csak azok maradnak, amelyekhez METAR és TAF is elérhető / Keeps only stations where both METAR and TAF are available
            HashSet<string> validSet = new HashSet<string>(
                metarStations.Intersect(tafStations),
                StringComparer.OrdinalIgnoreCase);

            // Szűrt repülőtérlista visszaadása / Returns the filtered airport list
            return result.Where(a => validSet.Contains(a.Icao)).ToList();
        }

        private async Task<HashSet<string>> GetStationsWithDataAsync(string type, string stationsCsv)
        {
            // Állomáskódokat tároló halmaz létrehozása / Creates a set for storing station codes
            HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                // CheckWX API URL összeállítása / Builds the CheckWX API URL
                string url = "https://api.checkwx.com/" + type + "/" + stationsCsv + "/decoded";

                using (HttpClient client = new HttpClient())
                {
                    // API kulcs hozzáadása / Adds the API key
                    client.DefaultRequestHeaders.Add("X-API-Key", CheckWxApiKey);

                    // Kérés elküldése / Sends the request
                    HttpResponseMessage resp = await client.GetAsync(url);

                    // Sikertelen válasz esetén üres halmaz visszaadása / Returns an empty set if the response is unsuccessful
                    if (!resp.IsSuccessStatusCode)
                        return set;

                    // JSON válasz feldolgozása / Parses the JSON response
                    string json = await resp.Content.ReadAsStringAsync();
                    JObject obj = JObject.Parse(json);
                    JArray data = obj["data"] as JArray;

                    // Adatok hiánya esetén üres halmaz visszaadása / Returns an empty set if there is no data
                    if (data == null)
                        return set;

                    // Állomáskódok kigyűjtése / Collects station codes
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
                // Hibák figyelmen kívül hagyása, hogy a keresés ne álljon le / Ignores errors so the search does not stop
            }

            // Elérhető adatokkal rendelkező állomások visszaadása / Returns stations with available data
            return set;
        }

        private Tuple<string, string> ComputeValidity(JToken taf, JArray fcst)
        {
            // TAF érvényességi idő lekérése / Gets the TAF validity time
            string tafFrom = FormatIsoTimeZ(GetTokenString(taf, "period.from"));
            string tafTo = FormatIsoTimeZ(GetTokenString(taf, "period.to"));

            // Ha mindkét időpont elérhető, ezeket adja vissza / If both times are available, returns them
            if (tafFrom != "-" && tafTo != "-")
                return Tuple.Create(tafFrom, tafTo);

            // Legkorábbi kezdő és legkésőbbi záró idő tárolása / Stores the earliest start and latest end time
            DateTimeOffset? minFrom = null;
            DateTimeOffset? maxTo = null;

            // Előrejelzési időszakok alapján számolja ki az érvényességet / Computes validity from forecast periods
            foreach (JToken p in fcst)
            {
                string fromIso = GetPeriodFromIso(p);
                string toIso = GetPeriodToIso(p);

                DateTimeOffset f;
                if (TryParseUtc(fromIso, out f))
                    minFrom = !minFrom.HasValue || f < minFrom.Value ? f : minFrom;

                DateTimeOffset t;
                if (TryParseUtc(toIso, out t))
                    maxTo = !maxTo.HasValue || t > maxTo.Value ? t : maxTo;
            }

            // Kezdő idő formázása / Formats the start time
            string fromStr = minFrom.HasValue
                ? minFrom.Value.ToUniversalTime().ToString("dd MMM HH:mm'Z'", CultureInfo.InvariantCulture).ToUpperInvariant()
                : "-";

            // Záró idő formázása / Formats the end time
            string toStr = maxTo.HasValue
                ? maxTo.Value.ToUniversalTime().ToString("dd MMM HH:mm'Z'", CultureInfo.InvariantCulture).ToUpperInvariant()
                : "-";

            // Érvényességi idő visszaadása / Returns the validity time
            return Tuple.Create(fromStr, toStr);
        }

        private string BuildHeaderText(string icao, string name, JToken taf, string validFrom, string validTo)
        {
            // Kibocsátási idő formázása / Formats the issued time
            string issued = FormatIsoTimeZ(
                GetTokenString(taf, "issued") ??
                GetTokenString(taf, "timestamp.issued")
            );

            // Maximum hőmérséklet lekérése / Gets the maximum temperature
            string tx =
                GetTokenString(taf, "temperature.max_celsius") ??
                GetTokenString(taf, "temperature.max.celsius");

            // Minimum hőmérséklet lekérése / Gets the minimum temperature
            string tn =
                GetTokenString(taf, "temperature.min_celsius") ??
                GetTokenString(taf, "temperature.min.celsius");

            // Hőmérsékleti rész összeállítása, ha van adat / Builds the temperature part if data is available
            string tempPart = (!string.IsNullOrWhiteSpace(tx) || !string.IsNullOrWhiteSpace(tn))
                ? " | TX " + (tx ?? "-") + "°C / TN " + (tn ?? "-") + "°C"
                : "";

            // Fejléc szövegének visszaadása / Returns the header text
            return icao + " — " + name
                   + "   |   Issued: " + issued
                   + "   |   Valid: " + validFrom + " → " + validTo
                   + tempPart;
        }

        private string GetPeriodFromIso(JToken period)
        {
            // Null időszak esetén nincs kezdő idő / Returns no start time if the period is null
            if (period == null)
                return null;

            // Kezdő idő keresése több lehetséges JSON útvonalon / Searches for the start time in multiple possible JSON paths
            return GetTokenString(period, "change.period.from")
                   ?? GetTokenString(period, "period.from")
                   ?? GetTokenString(period, "timestamp.from");
        }

        private string GetPeriodToIso(JToken period)
        {
            // Null időszak esetén nincs záró idő / Returns no end time if the period is null
            if (period == null)
                return null;

            // Záró idő keresése több lehetséges JSON útvonalon / Searches for the end time in multiple possible JSON paths
            return GetTokenString(period, "change.period.to")
                   ?? GetTokenString(period, "period.to")
                   ?? GetTokenString(period, "timestamp.to");
        }

        private string GetLastForecastEnd(JArray fcst)
        {
            // Üres előrejelzés esetén nincs záró idő / Returns no end time if the forecast is empty
            if (fcst == null || fcst.Count == 0)
                return null;

            // Az utolsó elérhető záró idő keresése visszafelé / Searches backward for the last available end time
            for (int i = fcst.Count - 1; i >= 0; i--)
            {
                string to = GetPeriodToIso(fcst[i]);

                if (!string.IsNullOrWhiteSpace(to))
                    return to;
            }

            // Ha nincs záró idő, null értéket ad vissza / Returns null if no end time is found
            return null;
        }

        private string GetRawTafText(JToken taf)
        {
            // Nyers TAF szöveg keresése több lehetséges mezőben / Searches for the raw TAF text in multiple possible fields
            string raw =
                GetTokenString(taf, "raw_text") ??
                GetTokenString(taf, "raw") ??
                GetTokenString(taf, "text") ??
                GetTokenString(taf, "taf") ??
                GetTokenString(taf, "message");

            // Nyers TAF szöveg visszaadása vagy üres szöveg / Returns the raw TAF text or an empty string
            return raw ?? "";
        }

        private List<RawChangeGroup> ParseRawTafChangeGroups(string rawTaf, DateTimeOffset issuedTime)
        {
            // Változási csoportok listájának létrehozása / Creates the list of change groups
            List<RawChangeGroup> groups = new List<RawChangeGroup>();

            // Üres TAF esetén üres lista visszaadása / Returns an empty list if the TAF is empty
            if (string.IsNullOrWhiteSpace(rawTaf))
                return groups;

            // Hiányzó kibocsátási idő esetén az aktuális UTC idő használata / Uses current UTC time if issued time is missing
            if (issuedTime == default(DateTimeOffset))
                issuedTime = DateTimeOffset.UtcNow;

            // Nyers TAF normalizálása / Normalizes the raw TAF text
            string raw = Regex.Replace(rawTaf.ToUpperInvariant(), @"\s+", " ").Trim();

            // TEMPO, BECMG és PROB időszakok keresése / Searches for TEMPO, BECMG, and PROB periods
            Regex periodRegex = new Regex(
                @"(?:(PROB30|PROB40)\s+)?(TEMPO|BECMG)\s+(\d{4})/(\d{4})",
                RegexOptions.IgnoreCase);

            // Találatok lekérése / Gets the matches
            MatchCollection matches = periodRegex.Matches(raw);

            // Változási csoportok feldolgozása / Processes change groups
            foreach (Match m in matches)
            {
                // Valószínűség és típus kiolvasása / Reads the probability and type
                string prob = m.Groups[1].Success ? m.Groups[1].Value.ToUpperInvariant() : "";
                string kind = m.Groups[2].Value.ToUpperInvariant();

                // Csoporttípus összeállítása / Builds the group type
                string type = string.IsNullOrWhiteSpace(prob) ? kind : prob + " " + kind;

                // Kezdő és záró idő létrehozása / Builds the start and end time
                DateTimeOffset from = BuildTafDateTime(issuedTime, m.Groups[3].Value);
                DateTimeOffset to = BuildTafDateTime(issuedTime, m.Groups[4].Value);

                // Hónapváltás kezelése / Handles month rollover
                if (to <= from)
                    to = to.AddMonths(1);

                // Változási csoport hozzáadása / Adds the change group
                groups.Add(new RawChangeGroup
                {
                    Type = type,
                    From = from,
                    To = to
                });
            }

            // FM csoportok keresése / Searches for FM groups
            Regex fmRegex = new Regex(@"\bFM(\d{6})\b", RegexOptions.IgnoreCase);
            MatchCollection fmMatches = fmRegex.Matches(raw);

            // FM csoportok feldolgozása / Processes FM groups
            foreach (Match m in fmMatches)
            {
                // FM kezdő idő létrehozása / Builds the FM start time
                DateTimeOffset from = BuildTafDateTimeSixDigits(issuedTime, m.Groups[1].Value);

                // FM csoport hozzáadása / Adds the FM group
                groups.Add(new RawChangeGroup
                {
                    Type = "FM",
                    From = from,
                    To = null
                });
            }

            // Változási csoportok visszaadása / Returns the change groups
            return groups;
        }

        private DateTimeOffset BuildTafDateTime(DateTimeOffset reference, string ddhh)
        {
            // Nap és óra kiolvasása a DDHH formátumból / Reads the day and hour from DDHH format
            int day = int.Parse(ddhh.Substring(0, 2), CultureInfo.InvariantCulture);
            int hour = int.Parse(ddhh.Substring(2, 2), CultureInfo.InvariantCulture);

            // Alap dátum létrehozása a referencia hónap elejére / Creates a base date at the start of the reference month
            DateTimeOffset dt = new DateTimeOffset(
                reference.Year,
                reference.Month,
                1,
                0,
                0,
                0,
                TimeSpan.Zero);

            // A hónap napjainak számának lekérése / Gets the number of days in the month
            int daysInMonth = DateTime.DaysInMonth(reference.Year, reference.Month);

            // Túl nagy napérték esetén a hónap utolsó napjára állítja / If the day is too large, sets it to the last day of the month
            if (day > daysInMonth)
                day = daysInMonth;

            // TAF időpont létrehozása / Creates the TAF datetime
            dt = new DateTimeOffset(reference.Year, reference.Month, day, hour, 0, 0, TimeSpan.Zero);

            // Hónapváltás kezelése / Handles month rollover
            if (day < reference.Day - 15)
                dt = dt.AddMonths(1);

            // Elkészített időpont visszaadása / Returns the created datetime
            return dt;
        }

        private DateTimeOffset BuildTafDateTimeSixDigits(DateTimeOffset reference, string ddhhmm)
        {
            // Nap, óra és perc kiolvasása a DDHHMM formátumból / Reads the day, hour, and minute from DDHHMM format
            int day = int.Parse(ddhhmm.Substring(0, 2), CultureInfo.InvariantCulture);
            int hour = int.Parse(ddhhmm.Substring(2, 2), CultureInfo.InvariantCulture);
            int minute = int.Parse(ddhhmm.Substring(4, 2), CultureInfo.InvariantCulture);

            // Alap dátum létrehozása a referencia hónap elejére / Creates a base date at the start of the reference month
            DateTimeOffset dt = new DateTimeOffset(
                reference.Year,
                reference.Month,
                1,
                0,
                0,
                0,
                TimeSpan.Zero);

            // A hónap napjainak számának lekérése / Gets the number of days in the month
            int daysInMonth = DateTime.DaysInMonth(reference.Year, reference.Month);

            // Túl nagy napérték esetén a hónap utolsó napjára állítja / If the day is too large, sets it to the last day of the month
            if (day > daysInMonth)
                day = daysInMonth;

            // TAF időpont létrehozása perccel együtt / Creates the TAF datetime including minutes
            dt = new DateTimeOffset(reference.Year, reference.Month, day, hour, minute, 0, TimeSpan.Zero);

            // Hónapváltás kezelése / Handles month rollover
            if (day < reference.Day - 15)
                dt = dt.AddMonths(1);

            // Elkészített időpont visszaadása / Returns the created datetime
            return dt;
        }

        private string GetForecastTypeFromRawGroups(
            List<RawChangeGroup> rawGroups,
            string fromIso,
            string toIso,
            string fallbackType,
            string probability)
        {
            // Kezdő és záró idő változók létrehozása / Creates start and end time variables
            DateTimeOffset from;
            DateTimeOffset to;

            // ISO időpontok feldolgozása / Parses ISO times
            bool hasFrom = TryParseUtc(fromIso, out from);
            bool hasTo = TryParseUtc(toIso, out to);

            // Ha nincs kezdő idő, a tartalék típus használata / Uses the fallback type if there is no start time
            if (!hasFrom)
                return CombineProbabilityAndType(fallbackType, probability);

            // Nyers TAF csoportokkal való egyeztetés / Matches against raw TAF groups
            foreach (RawChangeGroup g in rawGroups)
            {
                // Kezdő idő egyezésének ellenőrzése / Checks whether the start time matches
                bool fromMatches = SameMinute(g.From, from);
                bool toMatches = true;

                // Záró idő egyezésének ellenőrzése, ha van / Checks the end time match if available
                if (g.To.HasValue && hasTo)
                    toMatches = SameMinute(g.To.Value, to);

                // Egyezés esetén a nyers csoport típusának visszaadása / Returns the raw group type if it matches
                if (fromMatches && toMatches)
                    return g.Type;
            }

            // Ha nincs egyezés, tartalék típus és valószínűség használata / Uses fallback type and probability if there is no match
            return CombineProbabilityAndType(fallbackType, probability);
        }

        private bool SameMinute(DateTimeOffset a, DateTimeOffset b)
        {
            // Időpontok UTC-re alakítása / Converts times to UTC
            DateTimeOffset ua = a.ToUniversalTime();
            DateTimeOffset ub = b.ToUniversalTime();

            // Perc pontosságú egyezés ellenőrzése / Checks equality with minute precision
            return ua.Year == ub.Year &&
                   ua.Month == ub.Month &&
                   ua.Day == ub.Day &&
                   ua.Hour == ub.Hour &&
                   ua.Minute == ub.Minute;
        }

        private string CombineProbabilityAndType(string type, string probability)
        {
            // Hiányzó típus esetén BASE használata / Uses BASE if the type is missing
            if (string.IsNullOrWhiteSpace(type))
                type = "BASE";

            // Valószínűség hozzáadása a típushoz, ha van / Adds probability to the type if available
            if (!string.IsNullOrWhiteSpace(probability))
            {
                string p = probability.Replace("%", "").Trim();

                if (type == "TEMPO")
                    return "PROB" + p + " TEMPO";

                if (type == "PROB")
                    return "PROB" + p;
            }

            // Előrejelzés típusának visszaadása / Returns the forecast type
            return type;
        }

        private string GetForecastTypeFriendly(JToken period)
        {
            // Előrejelzés típusára utaló szöveg keresése / Searches for text indicating the forecast type
            string repr =
                GetTokenString(period, "change.text")
                ?? GetTokenString(period, "change.repr")
                ?? GetTokenString(period, "change.indicator")
                ?? GetTokenString(period, "change.type")
                ?? GetTokenString(period, "type")
                ?? GetTokenString(period, "indicator")
                ?? "";

            // Szöveg nagybetűssé alakítása / Converts the text to uppercase
            repr = repr.ToUpperInvariant();

            // TAF típusjelölők felismerése / Detects TAF type indicators
            bool hasProb30 = repr.Contains("PROB30");
            bool hasProb40 = repr.Contains("PROB40");
            bool hasTempo = repr.Contains("TEMPO");
            bool hasBecmg = repr.Contains("BECMG") || repr.Contains("BECOMING");
            bool hasFm = repr.Contains("FM");

            // PROB30 TEMPO típus felismerése / Detects PROB30 TEMPO type
            if (hasProb30 && hasTempo)
                return "PROB30 TEMPO";

            // PROB40 TEMPO típus felismerése / Detects PROB40 TEMPO type
            if (hasProb40 && hasTempo)
                return "PROB40 TEMPO";

            // PROB30 típus felismerése / Detects PROB30 type
            if (hasProb30)
                return "PROB30";

            // PROB40 típus felismerése / Detects PROB40 type
            if (hasProb40)
                return "PROB40";

            // TEMPO típus felismerése / Detects TEMPO type
            if (hasTempo)
                return "TEMPO";

            // BECMG típus felismerése / Detects BECMG type
            if (hasBecmg)
                return "BECMG";

            // FM típus felismerése / Detects FM type
            if (hasFm)
                return "FM";

            // Valószínűségi mezők ellenőrzése / Checks probability fields
            string probability =
                GetTokenString(period, "probability")
                ?? GetTokenString(period, "change.probability");

            // Valószínűség esetén PROB típus visszaadása / Returns PROB type if probability exists
            if (!string.IsNullOrWhiteSpace(probability))
                return "PROB";

            // Alap előrejelzési típus visszaadása / Returns the base forecast type
            return "BASE";
        }

        private string FormatIsoTimeZ(string iso)
        {
            // Üres időpont esetén kötőjel visszaadása / Returns a dash if the time is empty
            if (string.IsNullOrWhiteSpace(iso))
                return "-";

            DateTimeOffset dto;

            // ISO idő UTC-re alakítása és formázása / Converts ISO time to UTC and formats it
            if (TryParseUtc(iso, out dto))
            {
                return dto.ToUniversalTime()
                          .ToString("dd MMM HH:mm'Z'", CultureInfo.InvariantCulture)
                          .ToUpperInvariant();
            }

            // Feldolgozhatatlan időpont esetén az eredeti érték visszaadása / Returns the original value if parsing fails
            return iso;
        }

        private bool TryParseUtc(string iso, out DateTimeOffset dto)
        {
            // Kimeneti változó alaphelyzetbe állítása / Resets the output variable
            dto = new DateTimeOffset();

            // Üres szöveg esetén sikertelen feldolgozás / Parsing fails if the text is empty
            if (string.IsNullOrWhiteSpace(iso))
                return false;

            DateTimeOffset parsedOffset;

            // DateTimeOffset feldolgozási kísérlet / Attempts to parse as DateTimeOffset
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

            // DateTime feldolgozási kísérlet / Attempts to parse as DateTime
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

            // Sikertelen feldolgozás jelzése / Indicates failed parsing
            return false;
        }

        private string FormatWindFriendly(JToken wind)
        {
            // Hiányzó széladat esetén üres szöveg visszaadása / Returns empty text if wind data is missing
            if (wind == null)
                return "";

            // Szélirány, fok, sebesség és széllökés lekérése / Gets wind direction, degrees, speed, and gust
            string direction = GetTokenString(wind, "direction") ?? "";
            string degrees = GetTokenString(wind, "degrees") ?? "";

            string speed =
                GetTokenString(wind, "speed.kts") ??
                GetTokenString(wind, "speed_kts");

            string gust =
                GetTokenString(wind, "gust.kts") ??
                GetTokenString(wind, "gust_kts");

            // Változó szélirány felismerése / Detects variable wind direction
            bool variable =
                string.Equals(GetTokenString(wind, "variable"), "true", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(direction, "VRB", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(degrees, "VRB", StringComparison.OrdinalIgnoreCase);

            // Hiányos széladat esetén üres szöveg visszaadása / Returns empty text if wind data is incomplete
            if (string.IsNullOrWhiteSpace(speed) &&
                string.IsNullOrWhiteSpace(degrees) &&
                string.IsNullOrWhiteSpace(direction))
            {
                return "";
            }

            string result;

            // Széladat olvasható formátumra alakítása / Converts wind data to readable format
            if (variable)
                result = "Variable at " + speed + " kt";
            else if (!string.IsNullOrWhiteSpace(degrees) && !string.IsNullOrWhiteSpace(speed))
                result = degrees + "° at " + speed + " kt";
            else if (!string.IsNullOrWhiteSpace(direction) && !string.IsNullOrWhiteSpace(speed))
                result = direction + " at " + speed + " kt";
            else if (!string.IsNullOrWhiteSpace(speed))
                result = speed + " kt";
            else
                result = "";

            // Széllökés hozzáadása, ha van / Adds gust information if available
            if (!string.IsNullOrWhiteSpace(gust))
                result += ", gusting " + gust + " kt";

            // Formázott széladat visszaadása / Returns the formatted wind data
            return result.Trim();
        }

        private string FormatVisibilityFriendly(JToken period)
        {
            // CAVOK mező lekérése / Gets the CAVOK field
            JToken cavokToken = period != null ? period["cavok"] : null;

            // CAVOK érték ellenőrzése / Checks the CAVOK value
            if (cavokToken != null && cavokToken.Type != JTokenType.Null)
            {
                string cavokStr = cavokToken.ToString();

                if (cavokStr.Equals("true", StringComparison.OrdinalIgnoreCase))
                    return "CAVOK";
            }

            // Látástávolság mező lekérése / Gets the visibility field
            JToken vis = period != null ? period["visibility"] : null;

            // Hiányzó látástávolság esetén üres szöveg visszaadása / Returns empty text if visibility is missing
            if (vis == null)
                return "";

            // Szöveges látástávolság lekérése / Gets textual visibility
            string text =
                GetTokenString(vis, "text") ??
                GetTokenString(vis, "repr");

            // Szöveges látástávolság visszaadása / Returns textual visibility
            if (!string.IsNullOrWhiteSpace(text))
                return text.Replace("P6SM", "More than 6 statute miles");

            // Méterben megadott látástávolság lekérése / Gets visibility in meters
            string meters =
                GetTokenString(vis, "meters_float") ??
                GetTokenString(vis, "meters");

            // Méteres látástávolság formázása / Formats visibility in meters
            if (!string.IsNullOrWhiteSpace(meters))
            {
                double m;

                if (double.TryParse(meters, NumberStyles.Any, CultureInfo.InvariantCulture, out m))
                {
                    if (m >= 10000)
                        return "10 km or more";

                    return m.ToString("0", CultureInfo.InvariantCulture) + " m";
                }

                return meters + " m";
            }

            // Mérföldben megadott látástávolság lekérése / Gets visibility in miles
            string miles =
                GetTokenString(vis, "miles") ??
                GetTokenString(vis, "miles_float");

            // Mérföldes látástávolság visszaadása / Returns visibility in miles
            if (!string.IsNullOrWhiteSpace(miles))
                return miles + " miles";

            // Ha nincs látástávolság adat, üres szöveg visszaadása / Returns empty text if no visibility data is available
            return "";
        }

        private string FormatConditionsFriendly(JToken condsToken)
        {
            // Időjárási jelenségek tömbbé alakítása / Converts weather conditions to an array
            JArray conds = condsToken as JArray;

            // Üres feltételek esetén üres szöveg visszaadása / Returns empty text if there are no conditions
            if (conds == null || conds.Count == 0)
                return "";

            // Időjárási jelenségek szövegének kigyűjtése / Collects the text of weather conditions
            List<string> parts = conds
                .Select(c => GetTokenString(c, "text") ?? GetTokenString(c, "code"))
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();

            // Ha nincs megjeleníthető elem, üres szöveg visszaadása / Returns empty text if there are no displayable parts
            if (parts.Count == 0)
                return "";

            // Időjárási jelenségek összefűzése / Joins the weather condition parts
            return string.Join(", ", parts);
        }

        private string FormatCloudsFriendly(JToken cloudsToken)
        {
            // Felhőzet tömbbé alakítása / Converts cloud data to an array
            JArray clouds = cloudsToken as JArray;

            // Üres felhőzet esetén üres szöveg visszaadása / Returns empty text if there are no clouds
            if (clouds == null || clouds.Count == 0)
                return "";

            // Felhőzeti részek listájának létrehozása / Creates the list of cloud parts
            List<string> parts = new List<string>();

            // Felhőzeti rétegek feldolgozása / Processes cloud layers
            foreach (JToken c in clouds)
            {
                // Felhőkód és magasság lekérése / Gets cloud code and height
                string code = GetTokenString(c, "code");
                string feetStr = GetTokenString(c, "feet");

                // Felhőkód szöveggé alakítása / Converts cloud code to text
                string description = CloudCodeToText(code);

                int feet;

                // Magasság hozzáadása, ha elérhető / Adds height if available
                if (int.TryParse(feetStr, out feet))
                    parts.Add(description + " at " + feet.ToString("N0", CultureInfo.InvariantCulture) + " ft");
                else if (!string.IsNullOrWhiteSpace(description))
                    parts.Add(description);
            }

            // Felhőzeti rétegek összefűzése / Joins cloud layers
            return string.Join("; ", parts);
        }

        private string CloudCodeToText(string code)
        {
            // Felhőkódok olvasható szöveggé alakítása / Converts cloud codes to readable text
            switch ((code ?? "").ToUpperInvariant())
            {
                case "SKC": return "Sky clear";
                case "CLR": return "Clear";
                case "NSC": return "No significant clouds";
                case "FEW": return "Few clouds";
                case "SCT": return "Scattered clouds";
                case "BKN": return "Broken clouds";
                case "OVC": return "Overcast";
                case "VV": return "Vertical visibility";
                default: return code;
            }
        }

        private string GetProbability(JToken period)
        {
            // Valószínűségi érték keresése több lehetséges mezőben / Searches for probability value in multiple possible fields
            string p =
                GetTokenString(period, "probability.percent") ??
                GetTokenString(period, "probability.value") ??
                GetTokenString(period, "probability") ??
                GetTokenString(period, "change.probability") ??
                GetTokenString(period, "change.percent") ??
                GetTokenString(period, "change.value");

            // Hiányzó valószínűség esetén üres szöveg visszaadása / Returns empty text if probability is missing
            if (string.IsNullOrWhiteSpace(p))
                return "";

            // Ha már százalékjellel végződik, változatlanul visszaadja / Returns unchanged if it already ends with a percent sign
            if (p.EndsWith("%"))
                return p;

            // Százalékjel hozzáadása / Adds the percent sign
            return p + "%";
        }

        private async Task<string> GetAirportNameFromAeroDataBox(string icao)
        {
            try
            {
                // AeroDataBox ICAO alapú keresési URL összeállítása / Builds the AeroDataBox ICAO lookup URL
                string url = "https://aerodatabox.p.rapidapi.com/airports/icao/" + icao;

                using (HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    // RapidAPI fejlécek hozzáadása / Adds RapidAPI headers
                    req.Headers.Add("x-rapidapi-key", RapidApiKey);
                    req.Headers.Add("x-rapidapi-host", "aerodatabox.p.rapidapi.com");

                    // Kérés elküldése és válasz ellenőrzése / Sends the request and checks the response
                    HttpResponseMessage resp = await httpClient.SendAsync(req);
                    resp.EnsureSuccessStatusCode();

                    // JSON válasz feldolgozása / Parses the JSON response
                    string json = await resp.Content.ReadAsStringAsync();
                    JObject obj = JObject.Parse(json);

                    // Repülőtér nevének visszaadása / Returns the airport name
                    return GetTokenString(obj, "name");
                }
            }
            catch
            {
                // Hiba esetén null értéket ad vissza / Returns null if an error occurs
                return null;
            }
        }

        private void ConfigureCityAutocomplete(TextBox textBox)
        {
            // Automatikus kiegészítési lista létrehozása / Creates the autocomplete list
            AutoCompleteStringCollection src = new AutoCompleteStringCollection();

            // Városnevek hozzáadása, ha elérhetők / Adds city names if available
            if (CityData.Cities != null && CityData.Cities.Count > 0)
                src.AddRange(CityData.Cities.ToArray());

            // Automatikus kiegészítés beállítása / Configures autocomplete
            textBox.AutoCompleteCustomSource = src;
            textBox.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            textBox.AutoCompleteSource = AutoCompleteSource.CustomSource;

            // Szövegváltozás eseménykezelő újracsatolása / Reattaches the text changed event handler
            textBox.TextChanged -= CityTextBox_TextChanged;
            textBox.TextChanged += CityTextBox_TextChanged;
        }

        private void CityTextBox_TextChanged(object sender, EventArgs e)
        {
            // Küldő TextBox lekérése / Gets the sender textbox
            TextBox tb = sender as TextBox;

            // Ha a küldő nem TextBox, kilép / Exits if the sender is not a TextBox
            if (tb == null)
                return;

            // Aktuális szöveg lekérése / Gets the current text
            string t = tb.Text ?? string.Empty;

            // Annak ellenőrzése, hogy a szöveg kódnak tűnik-e / Checks whether the text looks like a code
            bool looksLikeCode =
                t.Length >= 2 &&
                t.Length <= 4 &&
                t.All(ch => char.IsLetter(ch) && char.IsUpper(ch));

            // Kódszerű bevitel esetén kikapcsolja az automatikus kiegészítést / Disables autocomplete if the input looks like a code
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
                // Egyéb bevitel esetén visszakapcsolja az automatikus kiegészítést / Enables autocomplete again for other input
                if (tb.AutoCompleteMode != AutoCompleteMode.SuggestAppend)
                {
                    tb.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
                    tb.AutoCompleteSource = AutoCompleteSource.CustomSource;
                }
            }
        }

        private string GetTokenString(JToken token, string path)
        {
            // Érvénytelen token vagy útvonal esetén null visszaadása / Returns null if the token or path is invalid
            if (token == null || string.IsNullOrWhiteSpace(path))
                return null;

            // Kiinduló JSON token beállítása / Sets the starting JSON token
            JToken current = token;

            // Útvonal feldarabolása pontok mentén / Splits the path by dots
            string[] parts = path.Split('.');

            // JSON útvonal bejárása / Traverses the JSON path
            foreach (string part in parts)
            {
                if (current == null)
                    return null;

                current = current[part];
            }

            // Null JSON érték esetén null visszaadása / Returns null if the JSON value is null
            if (current == null || current.Type == JTokenType.Null)
                return null;

            // Token szöveggé alakítása / Converts the token to text
            string value = current.ToString();

            // Üres érték esetén null, különben a szöveg visszaadása / Returns null for empty values, otherwise returns the text
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        private void TafGrid_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
        }

        private void AirportComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            // Kiválasztott repülőtér lekérése / Gets the selected airport
            AirportOption ap = AirportComboBox.SelectedItem as AirportOption;

            // Ha van kiválasztott repülőtér, az ICAO mező frissítése / Updates the ICAO textbox if an airport is selected
            if (ap != null)
                ICAOTextBox.Text = ap.Icao;
        }

        private class RawChangeGroup
        {
            // Változási csoport típusa / Type of the change group
            public string Type { get; set; }

            // Változási csoport kezdő ideje / Start time of the change group
            public DateTimeOffset From { get; set; }

            // Változási csoport záró ideje / End time of the change group
            public DateTimeOffset? To { get; set; }
        }

        private class AirportOption
        {
            // Repülőtér ICAO kódja / ICAO code of the airport
            public string Icao { get; set; }

            // Repülőtér neve / Name of the airport
            public string Name { get; set; }

            // Repülőtér városa / City of the airport
            public string City { get; set; }

            public override string ToString()
            {
                // Várossal együtt formázott megjelenítési szöveg / Display text formatted with the city
                if (!string.IsNullOrWhiteSpace(City))
                    return Icao + " – " + City + " / " + Name;

                // Város nélküli megjelenítési szöveg / Display text without the city
                return Icao + " – " + Name;
            }
        }
    }
}
