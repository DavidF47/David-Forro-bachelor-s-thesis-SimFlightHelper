// Planner.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Thesis_testing_1
{
    public partial class Planner : Form
    {
        // EN: Shows whether the map is ready to receive route data.
        // HU: Jelzi, hogy a térkép készen áll-e az útvonal megjelenítésére.
        private bool _mapReady = false;

        // EN: Fix points loaded from the navigation data.
        // HU: A navigációs adatokból betöltött fix pontok.
        private Dictionary<string, List<FixPoint>> _fixes =
            new Dictionary<string, List<FixPoint>>(StringComparer.OrdinalIgnoreCase);

        // EN: Navaids loaded from the navigation data.
        // HU: A navigációs adatokból betöltött navigációs berendezések.
        private Dictionary<string, List<NavaidPoint>> _navaids =
            new Dictionary<string, List<NavaidPoint>>(StringComparer.OrdinalIgnoreCase);

        // EN: The airway graph is used for the enroute part of the route.
        // HU: A légifolyosó-gráf az útvonal enroute szakaszához szükséges.
        private AirwayGraph _airwayGraph = new AirwayGraph();

        private SidStarResolver _sidStarResolver;

        // EN: Folder where the airport CIFP procedure files are stored.
        // HU: Az a mappa, ahol a repülőtéri CIFP eljárásfájlok találhatók.
        private string _cifpFolder = null;

        private readonly Timer _sidTimer = new Timer();
        private readonly Timer _starTimer = new Timer();

        // EN: Parsed CIFP files are cached so they do not have to be read again every time.
        // HU: A feldolgozott CIFP fájlok gyorsítótárba kerülnek, így nem kell őket minden alkalommal újra beolvasni.
        private readonly Dictionary<string, object> _airportCifpCache =
            new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        // EN: Local fixes found inside airport procedure files.
        // HU: A repülőtéri eljárásfájlokban található lokális fix pontok.
        private readonly Dictionary<string, Dictionary<string, List<(double Lat, double Lon)>>> _airportLocalFixCache =
            new Dictionary<string, Dictionary<string, List<(double Lat, double Lon)>>>(StringComparer.OrdinalIgnoreCase);

        // EN: Airport coordinates loaded from airports.csv.
        // HU: Az airports.csv fájlból betöltött repülőtéri koordináták.
        private readonly Dictionary<string, (double Lat, double Lon)> _airports =
            new Dictionary<string, (double Lat, double Lon)>(StringComparer.OrdinalIgnoreCase);

        private string _airportsCsvPath = null;

        public Planner()
        {
            InitializeComponent();

            this.MaximizeBox = false;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;

            this.Load -= Planner_Load;
            this.Load += Planner_Load;

            btnGenerate.Click -= btnGenerate_Click;
            btnGenerate.Click += btnGenerate_Click;

            btnCopyFlightPlan.Click -= btnCopyFlightPlan_Click;
            btnCopyFlightPlan.Click += btnCopyFlightPlan_Click;
        }

        private async void Planner_Load(object sender, EventArgs e)
        {
            // EN: Loads the main navigation files before route planning starts.
            // HU: Az útvonaltervezés előtt betölti a fő navigációs fájlokat.
            TryLoadNavdata();

            _sidStarResolver = new SidStarResolver(_fixes, _navaids, _airwayGraph);

            _airportsCsvPath = DetectAirportsCsv();
            if (!string.IsNullOrWhiteSpace(_airportsCsvPath) && File.Exists(_airportsCsvPath))
            {
                LoadAirportsCsv(_airportsCsvPath);
            }

            _cifpFolder = DetectCifpFolder();

            SetupSidStarDropdowns();

            if (txtFlightPlan != null)
                txtFlightPlan.Text = "";

            await InitMapAsync();
        }

        private void TryLoadNavdata()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;

                // EN: Fixes, airways and navaids are loaded from the local navdata folder.
                // HU: A fix pontok, légifolyosók és navigációs berendezések a lokális navdata mappából töltődnek be.
                string fixPath = Path.Combine(baseDir, "Data", "Navdata", "earth_fix.dat");
                _fixes = EarthFixV600.Load(fixPath);

                string awyPath = Path.Combine(baseDir, "Data", "Navdata", "earth_awy.dat");
                _airwayGraph = EarthAwyV600.Load(awyPath);

                string navPath = Path.Combine(baseDir, "Data", "Navdata", "earth_nav.dat");
                _navaids = EarthNavV810.Load(navPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Data load failed:\n\n" + ex.Message);
            }
        }

        private string DetectAirportsCsv()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            // EN: The program checks several possible locations for airports.csv.
            // HU: A program több lehetséges helyen is megkeresi az airports.csv fájlt.
            var candidates = new List<string>
            {
                Path.Combine(baseDir, "airports.csv"),
                Path.Combine(baseDir, "Data", "airports.csv"),
                Path.Combine(baseDir, "Data", "Navdata", "airports.csv"),
                Path.Combine(baseDir, "Data", "NavData", "airports.csv"),
            };

            foreach (var c in candidates)
                if (File.Exists(c)) return c;

            string dataRoot = Path.Combine(baseDir, "Data");
            if (Directory.Exists(dataRoot))
            {
                try
                {
                    var hit = Directory.EnumerateFiles(dataRoot, "airports.csv", SearchOption.AllDirectories)
                                       .FirstOrDefault();

                    if (!string.IsNullOrWhiteSpace(hit))
                        return hit;
                }
                catch
                {
                }
            }

            return null;
        }

        private void LoadAirportsCsv(string path)
        {
            _airports.Clear();

            using (var sr = new StreamReader(path, Encoding.UTF8, true))
            {
                string headerLine = sr.ReadLine();
                if (string.IsNullOrWhiteSpace(headerLine)) return;

                var header = CsvSplit(headerLine);
                var col = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                for (int i = 0; i < header.Count; i++)
                {
                    var h = (header[i] ?? "").Trim();

                    if (!col.ContainsKey(h))
                        col[h] = i;
                }

                int idxLat = GetCol(col, "latitude_deg", "lat", "latitude");
                int idxLon = GetCol(col, "longitude_deg", "lon", "longitude");
                int idxGps = GetCol(col, "gps_code");
                int idxIdent = GetCol(col, "ident");
                int idxIcao = GetCol(col, "icao_code");

                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var fields = CsvSplit(line);
                    if (fields.Count < 3) continue;

                    string icao = null;

                    // EN: The ICAO code can appear in different columns depending on the CSV source.
                    // HU: Az ICAO-kód a CSV forrásától függően több különböző oszlopban is szerepelhet.
                    if (idxIcao >= 0) icao = SafeGet(fields, idxIcao);
                    if (string.IsNullOrWhiteSpace(icao) && idxGps >= 0) icao = SafeGet(fields, idxGps);
                    if (string.IsNullOrWhiteSpace(icao) && idxIdent >= 0) icao = SafeGet(fields, idxIdent);

                    icao = (icao ?? "").Trim().ToUpperInvariant();

                    if (!IsValidIcao(icao)) continue;
                    if (idxLat < 0 || idxLon < 0) continue;

                    if (!TryParseDoubleInvariant(SafeGet(fields, idxLat), out double lat)) continue;
                    if (!TryParseDoubleInvariant(SafeGet(fields, idxLon), out double lon)) continue;

                    if (lat < -90 || lat > 90) continue;
                    if (lon < -180 || lon > 180) continue;

                    _airports[icao] = (lat, lon);
                }
            }
        }

        private static int GetCol(Dictionary<string, int> col, params string[] names)
        {
            foreach (var n in names)
                if (col.TryGetValue(n, out int i)) return i;

            return -1;
        }

        private static string SafeGet(List<string> fields, int idx)
        {
            if (idx < 0 || idx >= fields.Count) return "";
            return fields[idx] ?? "";
        }

        private static bool TryParseDoubleInvariant(string s, out double v)
        {
            return double.TryParse((s ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }

        private static List<string> CsvSplit(string line)
        {
            var res = new List<string>();
            if (line == null) return res;

            var sb = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (c == '"')
                {
                    // EN: Handles quotation marks inside CSV fields.
                    // HU: Kezeli a CSV mezőkön belüli idézőjeleket.
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
                    res.Add(sb.ToString());
                    sb.Clear();
                    continue;
                }

                sb.Append(c);
            }

            res.Add(sb.ToString());
            return res;
        }

        private void SetupSidStarDropdowns()
        {
            cmbSid.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbStar.DropDownStyle = ComboBoxStyle.DropDownList;

            // EN: Timers delay the refresh a little, so the list is not rebuilt after every single key press.
            // HU: Az időzítők kicsit késleltetik a frissítést, így a lista nem épül újra minden egyes billentyűleütés után.
            _sidTimer.Interval = 300;
            _sidTimer.Tick += (s, ea) =>
            {
                _sidTimer.Stop();
                RefreshSidDropdown();
            };

            _starTimer.Interval = 300;
            _starTimer.Tick += (s, ea) =>
            {
                _starTimer.Stop();
                RefreshStarDropdown();
            };

            txtOrigin.TextChanged += (s, ea) =>
            {
                _sidTimer.Stop();
                _sidTimer.Start();
            };

            txtDestination.TextChanged += (s, ea) =>
            {
                _starTimer.Stop();
                _starTimer.Start();
            };

            txtOrigin.Leave += (s, ea) => RefreshSidDropdown();
            txtDestination.Leave += (s, ea) => RefreshStarDropdown();

            RefreshSidDropdown();
            RefreshStarDropdown();
        }

        private string DetectCifpFolder()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            // EN: CIFP files are searched in the most likely project folders.
            // HU: A CIFP fájlok a legvalószínűbb projektmappákban kerülnek keresésre.
            var candidates = new List<string>
            {
                Path.Combine(baseDir, "Data", "CIFP"),
                Path.Combine(baseDir, "Data", "Cifp"),
                Path.Combine(baseDir, "Data", "Procedures"),
                Path.Combine(baseDir, "Data", "Navdata", "CIFP"),
                Path.Combine(baseDir, "Data")
            };

            foreach (var c in candidates)
            {
                if (!Directory.Exists(c)) continue;

                if (Directory.EnumerateFiles(c, "????.dat", SearchOption.TopDirectoryOnly).Any())
                    return c;
            }

            string dataRoot = Path.Combine(baseDir, "Data");
            if (!Directory.Exists(dataRoot)) return null;

            try
            {
                foreach (var dir in Directory.EnumerateDirectories(dataRoot, "*", SearchOption.AllDirectories))
                {
                    if (Directory.EnumerateFiles(dir, "????.dat", SearchOption.TopDirectoryOnly).Any())
                        return dir;
                }
            }
            catch
            {
            }

            return null;
        }

        private string FindAirportDat(string icao)
        {
            if (string.IsNullOrWhiteSpace(_cifpFolder)) return null;
            if (string.IsNullOrWhiteSpace(icao) || icao.Length != 4) return null;

            string file = Path.Combine(_cifpFolder, icao.Trim().ToUpperInvariant() + ".dat");
            return File.Exists(file) ? file : null;
        }

        private void RefreshSidDropdown()
        {
            cmbSid.Items.Clear();

            string origin = (txtOrigin.Text ?? "").Trim().ToUpperInvariant();

            if (!IsValidIcao(origin))
            {
                cmbSid.Items.Add("(enter ICAO)");
                cmbSid.SelectedIndex = 0;
                return;
            }

            string path = FindAirportDat(origin);

            if (path == null)
            {
                cmbSid.Items.Add("(no SID data)");
                cmbSid.SelectedIndex = 0;
                return;
            }

            cmbSid.Items.Add("(none)");

            // EN: SID names are read from the departure airport procedure file.
            // HU: A SID nevek az indulási repülőtér eljárásfájljából kerülnek beolvasásra.
            foreach (var s in ListProceduresViaParser(path, "SID"))
                cmbSid.Items.Add(s);

            cmbSid.SelectedIndex = 0;
        }

        private void RefreshStarDropdown()
        {
            cmbStar.Items.Clear();

            string dest = (txtDestination.Text ?? "").Trim().ToUpperInvariant();

            if (!IsValidIcao(dest))
            {
                cmbStar.Items.Add("(enter ICAO)");
                cmbStar.SelectedIndex = 0;
                return;
            }

            string path = FindAirportDat(dest);

            if (path == null)
            {
                cmbStar.Items.Add("(no STAR data)");
                cmbStar.SelectedIndex = 0;
                return;
            }

            cmbStar.Items.Add("(none)");

            // EN: STAR names are read from the destination airport procedure file.
            // HU: A STAR nevek az érkezési repülőtér eljárásfájljából kerülnek beolvasásra.
            foreach (var s in ListProceduresViaParser(path, "STAR"))
                cmbStar.Items.Add(s);

            cmbStar.SelectedIndex = 0;
        }

        private List<string> ListProceduresViaParser(string airportDatPath, string kind)
        {
            try
            {
                if (!_airportCifpCache.TryGetValue(airportDatPath, out var parsed))
                {
                    parsed = SidStarParser.ParseFile(airportDatPath);
                    _airportCifpCache[airportDatPath] = parsed;
                }

                return SidStarParser.ListProcedureNames(parsed, kind)
                                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                                    .ToList();
            }
            catch
            {
                return new List<string>();
            }
        }

        private Dictionary<string, List<(double Lat, double Lon)>> GetLocalFixesForAirportDat(string airportDatPath)
        {
            if (string.IsNullOrWhiteSpace(airportDatPath) || !File.Exists(airportDatPath))
                return new Dictionary<string, List<(double Lat, double Lon)>>(StringComparer.OrdinalIgnoreCase);

            if (_airportLocalFixCache.TryGetValue(airportDatPath, out var cached))
                return cached;

            var res = new Dictionary<string, List<(double Lat, double Lon)>>(StringComparer.OrdinalIgnoreCase);

            bool IsPointLine(string line)
            {
                if (string.IsNullOrWhiteSpace(line)) return false;

                line = line.TrimStart();

                return line.StartsWith("FIX:", StringComparison.OrdinalIgnoreCase) ||
                       line.StartsWith("WPT:", StringComparison.OrdinalIgnoreCase) ||
                       line.StartsWith("WAYPOINT:", StringComparison.OrdinalIgnoreCase) ||
                       line.StartsWith("INT:", StringComparison.OrdinalIgnoreCase);
            }

            foreach (var raw in File.ReadLines(airportDatPath))
            {
                var line = (raw ?? "").Trim();

                if (!IsPointLine(line)) continue;

                int colon = line.IndexOf(':');
                if (colon < 0) continue;

                string rest = line.Substring(colon + 1);
                int semi = rest.IndexOf(';');

                if (semi >= 0)
                    rest = rest.Substring(0, semi);

                var parts = rest.Split(',');
                if (parts.Length < 3) continue;

                string ident = "";

                // EN: The point name can be placed in different positions, so the first usable identifier is selected.
                // HU: A pont neve több helyen is szerepelhet, ezért az első használható azonosító kerül kiválasztásra.
                for (int i = 0; i < Math.Min(parts.Length, 3); i++)
                {
                    var t = (parts[i] ?? "").Trim().ToUpperInvariant();

                    if (t.Length >= 3 &&
                        t.Length <= 10 &&
                        t.All(ch => char.IsLetterOrDigit(ch)) &&
                        t.Any(char.IsLetter))
                    {
                        ident = t;
                        break;
                    }
                }

                if (string.IsNullOrWhiteSpace(ident)) continue;

                var nums = new List<double>();

                for (int i = 0; i < parts.Length; i++)
                {
                    string s = (parts[i] ?? "").Trim();

                    if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                    {
                        nums.Add(v);

                        if (nums.Count == 2)
                            break;
                    }
                }

                if (nums.Count < 2) continue;

                double lat = nums[0];
                double lon = nums[1];

                if (lat < -90 || lat > 90) continue;
                if (lon < -180 || lon > 180) continue;

                if (!res.TryGetValue(ident, out var list))
                {
                    list = new List<(double Lat, double Lon)>();
                    res[ident] = list;
                }

                list.Add((lat, lon));
            }

            _airportLocalFixCache[airportDatPath] = res;
            return res;
        }

        private async Task InitMapAsync()
        {
            _mapReady = false;

            try
            {
                EnsureWebViewOnForm();

                var userData = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ThesisTesting1_WebView2");

                // EN: GPU rendering is disabled because WebView2 could silently crash on some systems.
                // HU: A GPU renderelés ki van kapcsolva, mert egyes rendszereken a WebView2 csendben összeomolhat.
                var options = new CoreWebView2EnvironmentOptions(
                    "--disable-gpu --disable-gpu-compositing");

                var env = await CoreWebView2Environment.CreateAsync(null, userData, options);
                await WV2_map.EnsureCoreWebView2Async(env);

                string mapFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MapHost");
                Directory.CreateDirectory(mapFolder);

                string htmlPath = Path.Combine(mapFolder, "planner_map.html");
                File.WriteAllText(htmlPath, GetLeafletHtml(), Encoding.UTF8);

                // EN: The local HTML map is opened through a virtual host name for WebView2.
                // HU: A lokális HTML térkép WebView2 alatt virtuális hosztnéven keresztül nyílik meg.
                WV2_map.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "appassets.thesis.local",
                    mapFolder,
                    CoreWebView2HostResourceAccessKind.Allow);

                WV2_map.CoreWebView2.Settings.IsStatusBarEnabled = false;
                WV2_map.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                WV2_map.CoreWebView2.Settings.AreDevToolsEnabled = false;

                WV2_map.Source = new Uri("https://appassets.thesis.local/planner_map.html");

                _mapReady = true;
            }
            catch (Exception ex)
            {
                _mapReady = false;
                MessageBox.Show("WebView2 init failed:\n\n" + ex.Message);
            }
        }

        private void EnsureWebViewOnForm()
        {
            if (WV2_map == null)
            {
                // EN: If the WebView2 control is missing from the designer, it is created in code.
                // HU: Ha a WebView2 vezérlő hiányzik a tervezőből, akkor a programkód hozza létre.
                var wv = new WebView2();

                wv.Name = "WV2_map";
                wv.Visible = true;
                wv.Left = 520;
                wv.Top = 20;
                wv.Width = Math.Max(600, this.ClientSize.Width - wv.Left - 20);
                wv.Height = Math.Max(400, this.ClientSize.Height - wv.Top - 20);
                wv.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

                this.Controls.Add(wv);
                WV2_map = wv;
            }
            else
            {
                if (!this.Controls.Contains(WV2_map))
                    this.Controls.Add(WV2_map);

                WV2_map.Visible = true;
                WV2_map.BringToFront();
                WV2_map.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            }
        }

        private static string GetLeafletHtml()
        {
            return @"
<!doctype html>
<html>
<head>
  <meta charset='utf-8' />
  <meta name='viewport' content='width=device-width, initial-scale=1.0'>
  <link rel='stylesheet' href='https://unpkg.com/leaflet@1.9.4/dist/leaflet.css' />
  <script src='https://unpkg.com/leaflet@1.9.4/dist/leaflet.js'></script>
  <style>
    html, body { height:100%; margin:0; padding:0; }
    #map { height:100%; width:100%; }
    .wp-label{
      font-size:12px;
      font-weight:700;
      color:#111;
      text-shadow:0 0 2px rgba(255,255,255,.95), 0 0 4px rgba(255,255,255,.95);
      white-space:nowrap;
    }
  </style>
</head>
<body>
  <div id='map'></div>
  <script>
    const map = L.map('map', {
      minZoom: 2,
      maxZoom: 19,
      zoomControl: true
    }).setView([50,10], 4);

    window._map = map;

    L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
      minZoom: 2,
      maxZoom: 19,
      maxNativeZoom: 19,
      attribution: '&copy; OpenStreetMap contributors',
      crossOrigin: true
    }).addTo(map);

    let routeLine = null;
    let originMarker = null;
    let destMarker = null;
    let wpLayer = L.layerGroup().addTo(map);

    function clearRoute(){
      if(routeLine) map.removeLayer(routeLine);
      if(originMarker) map.removeLayer(originMarker);
      if(destMarker) map.removeLayer(destMarker);
      wpLayer.clearLayers();
    }

    function updateLabelVisibility(){
      const z = map.getZoom();
      wpLayer.eachLayer(layer => {
        if(layer && layer._isWpLabel){
          layer.setOpacity(z >= 7 ? 1 : 0);
        }
      });
    }

    map.on('zoomend', updateLabelVisibility);

    window.setRouteWithWaypoints = function(olat, olon, dlat, dlon, points, waypoints, originLabel, destLabel){
      clearRoute();

      originMarker = L.marker([olat,olon]).addTo(map).bindPopup(originLabel);
      destMarker = L.marker([dlat,dlon]).addTo(map).bindPopup(destLabel);

      routeLine = L.polyline(points, { weight:4 }).addTo(map);

      if(waypoints && waypoints.length){
        for(let i=0;i<waypoints.length;i++){
          const w = waypoints[i];

          L.circleMarker([w.lat, w.lon], { radius:4, weight:1, fillOpacity:0.9 })
            .addTo(wpLayer)
            .bindTooltip(w.id || '', { sticky:true });

          const label = L.marker([w.lat, w.lon], {
            interactive:false,
            icon: L.divIcon({
              className:'wp-label',
              html: w.id,
              iconSize:[0,0],
              iconAnchor:[-6,10]
            })
          }).addTo(wpLayer);

          label._isWpLabel = true;
        }
      }

      map.fitBounds(routeLine.getBounds(), { padding:[30,30] });
      updateLabelVisibility();
    };
  </script>
</body>
</html>";
        }

        private async void btnGenerate_Click(object sender, EventArgs e)
        {
            try
            {
                btnGenerate.Enabled = false;
                await GenerateRouteAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Unexpected planner error:\n\n" + ex,
                    "Planner Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                btnGenerate.Enabled = true;
            }
        }

        private async Task GenerateRouteAsync()
        {
            string origin = (txtOrigin.Text ?? "").Trim().ToUpperInvariant();
            string dest = (txtDestination.Text ?? "").Trim().ToUpperInvariant();

            string selSid = (cmbSid.SelectedItem as string) ?? "(none)";
            string selStar = (cmbStar.SelectedItem as string) ?? "(none)";

            if (!IsValidIcao(origin) || !IsValidIcao(dest))
            {
                MessageBox.Show("Origin and destination must be 4-letter ICAO codes.");
                return;
            }

            var originPos = LookupAirport(origin);
            var destPos = LookupAirport(dest);

            if (originPos == null || destPos == null)
            {
                MessageBox.Show("Airport coords not found. Make sure airports.csv is present and contains the ICAO codes.");
                return;
            }

            // EN: The planner is limited to the supported regional area.
            // HU: Az útvonaltervező csak a támogatott régióban működik.
            if (!SidStarResolver.IsInsideSupportedArea(originPos.Value.Lat, originPos.Value.Lon) ||
                !SidStarResolver.IsInsideSupportedArea(destPos.Value.Lat, destPos.Value.Lon))
            {
                if (txtFlightPlan != null)
                    txtFlightPlan.Text = "Route rejected: outside supported Europe/surrounding region.";

                MessageBox.Show(
                    "This planner currently supports Europe and the surrounding region only.\n\n" +
                    "Transatlantic, oceanic, and non-European airspace routing is not implemented yet.",
                    "Unsupported Route",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                return;
            }

            var sidLegs = GetProcedureLegs(origin, "SID", selSid);
            var starLegs = GetProcedureLegs(dest, "STAR", selStar);

            const double sidStepKm = 250;
            const double starStepKm = 250;

            List<(string Name, double Lat, double Lon)> sidResolved =
                new List<(string Name, double Lat, double Lon)>();

            List<(string Name, double Lat, double Lon)> starResolved =
                new List<(string Name, double Lat, double Lon)>();

            var originDat = FindAirportDat(origin);
            var originLocal = GetLocalFixesForAirportDat(originDat);
            _sidStarResolver.UpdateLocalFixes(originLocal);

            // EN: SID resolving is protected, because some procedure files may contain unsupported or unusual legs.
            // HU: A SID feloldás külön védve van, mert egyes eljárásfájlok tartalmazhatnak nem támogatott vagy szokatlan szakaszokat.
            try
            {
                var sidRes = _sidStarResolver.ResolveLegSequence(
                    sidLegs,
                    originPos.Value.Lat,
                    originPos.Value.Lon,
                    sidStepKm);

                if (sidRes != null && sidRes.Points != null)
                    sidResolved = sidRes.Points;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "SID could not be resolved:\n\n" + ex.Message,
                    "SID Warning",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }

            var destDat = FindAirportDat(dest);
            var destLocal = GetLocalFixesForAirportDat(destDat);
            _sidStarResolver.UpdateLocalFixes(destLocal);

            // EN: STAR resolving is protected separately, so a problematic STAR does not crash the whole planner.
            // HU: A STAR feloldás külön védve van, így egy problémás STAR nem állítja le az egész útvonaltervezőt.
            try
            {
                var starResBack = _sidStarResolver.ResolveLegSequence(
                    starLegs.AsEnumerable().Reverse(),
                    destPos.Value.Lat,
                    destPos.Value.Lon,
                    starStepKm);

                if (starResBack != null && starResBack.Points != null)
                {
                    starResolved = new List<(string Name, double Lat, double Lon)>(starResBack.Points);
                    starResolved.Reverse();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "STAR could not be resolved:\n\n" + ex.Message,
                    "STAR Warning",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }

            (double Lat, double Lon) startAnchor = originPos.Value;
            (double Lat, double Lon) endAnchor = destPos.Value;

            if (sidResolved.Count > 0)
                startAnchor = (sidResolved[sidResolved.Count - 1].Lat, sidResolved[sidResolved.Count - 1].Lon);

            if (starResolved.Count > 0)
                endAnchor = (starResolved[0].Lat, starResolved[0].Lon);

            int preferredFL = ParseCruiseFlightLevel(txtPrefAlt.Text);

            RouteResult rr;

            try
            {
                // EN: The nearest airway nodes are used as entry and exit points for the enroute route.
                // HU: A legközelebbi légifolyosó-csomópontok adják az enroute útvonal belépési és kilépési pontjait.
                var startCandidates = AirwayRouter.FindNearestNodes(
                    _airwayGraph,
                    startAnchor.Lat,
                    startAnchor.Lon,
                    3);

                var endCandidates = AirwayRouter.FindNearestNodes(
                    _airwayGraph,
                    endAnchor.Lat,
                    endAnchor.Lon,
                    3);

                double anchorDistanceNm = AirwayRouter.HaversineNm(
                    startAnchor.Lat,
                    startAnchor.Lon,
                    endAnchor.Lat,
                    endAnchor.Lon);

                double transitionPenaltyNm;
                double altPenaltyPerFL;
                double maxDirectNm;
                int maxDirectCandidates;
                double directPenaltyNm;

                if (anchorDistanceNm < 400)
                {
                    // EN: Short regional routes can use more DCT segments.
                    // HU: Rövidebb regionális útvonalaknál több DCT szakasz is megengedett.
                    transitionPenaltyNm = 1.0;
                    altPenaltyPerFL = 0.02;
                    maxDirectNm = 260.0;
                    maxDirectCandidates = 16;
                    directPenaltyNm = 1.0;
                }
                else if (anchorDistanceNm < 700)
                {
                    // EN: Medium routes allow some shortcuts, but still try to keep the airway structure.
                    // HU: Közepes útvonalaknál néhány rövidítés megengedett, de az airway szerkezet továbbra is fontos.
                    transitionPenaltyNm = 3.0;
                    altPenaltyPerFL = 0.05;
                    maxDirectNm = 140.0;
                    maxDirectCandidates = 8;
                    directPenaltyNm = 8.0;
                }
                else
                {
                    // EN: Long routes mostly stay on airways to avoid unstable route shapes.
                    // HU: Hosszabb útvonalaknál az útvonal főként légifolyosókon marad a stabilabb eredmény miatt.
                    transitionPenaltyNm = 5.0;
                    altPenaltyPerFL = 0.10;
                    maxDirectNm = 60.0;
                    maxDirectCandidates = 3;
                    directPenaltyNm = 25.0;
                }

                rr = AirwayRouter.FindBestRoute(
                    _airwayGraph,
                    startCandidates,
                    endCandidates,
                    preferredFL,
                    transitionPenaltyNm,
                    altPenaltyPerFL,
                    maxDirectNm,
                    maxDirectCandidates,
                    directPenaltyNm);
            }
            catch (Exception ex)
            {
                rr = new RouteResult
                {
                    Success = false,
                    DebugMessage = "Router error: " + ex.Message
                };
            }

            bool routeOk = RouteGeometryLooksSane(rr, maxLegNm: 600);

            string flightPlan = BuildFlightPlanString(origin, dest, sidResolved, rr, starResolved);

            if (txtFlightPlan != null)
                txtFlightPlan.Text = flightPlan;

            double totalNm = CalculateTotalDistanceNm(
                originPos.Value,
                sidResolved,
                rr,
                starResolved,
                destPos.Value,
                routeOk);

            double totalKm = totalNm * 1.852;

            var drawPts = new List<(double Lat, double Lon)>();
            drawPts.Add((originPos.Value.Lat, originPos.Value.Lon));

            foreach (var w in sidResolved)
                AddUnique(drawPts, (w.Lat, w.Lon));

            if (routeOk && rr != null && rr.Success && rr.NodeIds != null && rr.NodeIds.Count > 0)
            {
                foreach (var id in rr.NodeIds)
                {
                    if (_airwayGraph.Nodes.TryGetValue(id, out var n))
                        AddUnique(drawPts, (n.Lat, n.Lon));
                }
            }
            else
            {
                AddUnique(drawPts, (endAnchor.Lat, endAnchor.Lon));
            }

            foreach (var w in starResolved)
                AddUnique(drawPts, (w.Lat, w.Lon));

            AddUnique(drawPts, (destPos.Value.Lat, destPos.Value.Lon));

            string wpJson = BuildWaypointsJsonForMap(rr, sidResolved, starResolved);

            // EN: The line is smoothed lightly before drawing it on the Leaflet map.
            // HU: A vonal enyhén simításra kerül a Leaflet térképen való kirajzolás előtt.
            var smoothPts = SmoothCatmullRom(drawPts, samplesPerSegment: 2, maxSegmentNm: 150);

            if (smoothPts.Count > 800)
            {
                MessageBox.Show(
                    "Route was calculated, but it has too many map points to display safely.",
                    "Map Display Warning",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                return;
            }

            if (_mapReady && WV2_map != null && WV2_map.CoreWebView2 != null)
            {
                string jsPoints = "[" + string.Join(",", smoothPts.Select(p =>
                    $"[{p.Lat.ToString(CultureInfo.InvariantCulture)},{p.Lon.ToString(CultureInfo.InvariantCulture)}]")) + "]";

                string js = $@"
window.setRouteWithWaypoints(
  {originPos.Value.Lat.ToString(CultureInfo.InvariantCulture)},
  {originPos.Value.Lon.ToString(CultureInfo.InvariantCulture)},
  {destPos.Value.Lat.ToString(CultureInfo.InvariantCulture)},
  {destPos.Value.Lon.ToString(CultureInfo.InvariantCulture)},
  {jsPoints},
  {wpJson},
  '{EscapeJs(origin)}',
  '{EscapeJs(dest)}'
);";

                try
                {
                    await WV2_map.ExecuteScriptAsync(js);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        "Route was calculated, but the map could not be updated:\n\n" + ex.Message,
                        "Map Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            }

            string info =
                "Flight information\n\n" +
                "Origin: " + origin + "\n" +
                "Destination: " + dest + "\n" +
                "SID: " + selSid + "\n" +
                "STAR: " + selStar + "\n" +
                "Preferred FL: " + (preferredFL > 0 ? "FL" + preferredFL : "(none)") + "\n" +
                "Suggested FL: " + (rr != null && rr.SuggestedFl > 0 ? "FL" + rr.SuggestedFl : "(unknown)") + "\n" +
                "Estimated distance: " + totalNm.ToString("0.0", CultureInfo.InvariantCulture) + " NM\n" +
                "Estimated distance: " + totalKm.ToString("0.0", CultureInfo.InvariantCulture) + " km\n" +
                "Flight plan:\n" + flightPlan;

            MessageBox.Show(info, "Flight Information", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private double CalculateTotalDistanceNm(
            (double Lat, double Lon) originPos,
            List<(string Name, double Lat, double Lon)> sidResolved,
            RouteResult rr,
            List<(string Name, double Lat, double Lon)> starResolved,
            (double Lat, double Lon) destPos,
            bool routeOk)
        {
            var pts = new List<(double Lat, double Lon)>();
            pts.Add(originPos);

            if (sidResolved != null)
            {
                foreach (var w in sidResolved)
                    AddUnique(pts, (w.Lat, w.Lon));
            }

            if (routeOk && rr != null && rr.Success && rr.NodeIds != null)
            {
                foreach (var id in rr.NodeIds)
                {
                    if (_airwayGraph.Nodes.TryGetValue(id, out var n))
                        AddUnique(pts, (n.Lat, n.Lon));
                }
            }

            if (starResolved != null)
            {
                foreach (var w in starResolved)
                    AddUnique(pts, (w.Lat, w.Lon));
            }

            AddUnique(pts, destPos);

            double totalNm = 0.0;

            for (int i = 1; i < pts.Count; i++)
            {
                totalNm += AirwayRouter.HaversineNm(
                    pts[i - 1].Lat,
                    pts[i - 1].Lon,
                    pts[i].Lat,
                    pts[i].Lon);
            }

            return totalNm;
        }

        private bool RouteGeometryLooksSane(RouteResult rr, double maxLegNm)
        {
            if (rr == null || !rr.Success || rr.NodeIds == null || rr.NodeIds.Count < 2)
                return false;

            for (int i = 1; i < rr.NodeIds.Count; i++)
            {
                if (!_airwayGraph.Nodes.TryGetValue(rr.NodeIds[i - 1], out var a))
                    return false;

                if (!_airwayGraph.Nodes.TryGetValue(rr.NodeIds[i], out var b))
                    return false;

                double d = AirwayRouter.HaversineNm(a.Lat, a.Lon, b.Lat, b.Lon);

                if (d > maxLegNm)
                    return false;
            }

            return true;
        }

        private List<SidStarParser.CifpLeg> GetProcedureLegs(string icao, string kind, string procName)
        {
            if (string.IsNullOrWhiteSpace(procName) ||
                procName.Equals("(none)", StringComparison.OrdinalIgnoreCase))
            {
                return new List<SidStarParser.CifpLeg>();
            }

            string path = FindAirportDat(icao);
            if (path == null)
                return new List<SidStarParser.CifpLeg>();

            if (!_airportCifpCache.TryGetValue(path, out var parsed))
            {
                parsed = SidStarParser.ParseFile(path);
                _airportCifpCache[path] = parsed;
            }

            var legs = SidStarParser.GetLegSequence(parsed, kind, procName)
                       ?? new List<SidStarParser.CifpLeg>();

            // EN: Empty and runway-only legs are removed from the generated sequence.
            // HU: Az üres és csak futópályára utaló szakaszok nem kerülnek be az útvonalba.
            legs = legs
                .Where(l => l != null)
                .Where(l =>
                {
                    string f = (l.Fix ?? "").Trim().ToUpperInvariant();

                    if (string.IsNullOrWhiteSpace(f)) return false;
                    if (f == "..." || f == "RW" || f == "RWY") return false;

                    return true;
                })
                .ToList();

            return legs;
        }

        private string BuildFlightPlanString(
            string origin,
            string dest,
            List<(string Name, double Lat, double Lon)> sidResolved,
            RouteResult rr,
            List<(string Name, double Lat, double Lon)> starResolved)
        {
            var parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(origin))
                parts.Add(origin);

            if (sidResolved != null && sidResolved.Count > 0)
            {
                foreach (var w in sidResolved)
                {
                    var id = (w.Name ?? "").Trim().ToUpperInvariant();

                    if (string.IsNullOrWhiteSpace(id))
                        continue;

                    parts.Add(id);
                }
            }

            if (rr != null && rr.Success && rr.NodeIds != null && rr.NodeIds.Count > 0)
            {
                string prevAwy = null;

                parts.Add(rr.NodeIds[0]);

                for (int i = 1; i < rr.NodeIds.Count; i++)
                {
                    string awy = null;

                    if (rr.AirwayUsed != null && rr.AirwayUsed.Count > i - 1)
                        awy = rr.AirwayUsed[i - 1];

                    if (!string.IsNullOrWhiteSpace(awy) &&
                        !string.Equals(awy, prevAwy, StringComparison.OrdinalIgnoreCase))
                    {
                        parts.Add(awy.Trim().ToUpperInvariant());
                        prevAwy = awy;
                    }

                    parts.Add(rr.NodeIds[i]);
                }
            }

            if (starResolved != null && starResolved.Count > 0)
            {
                foreach (var w in starResolved)
                {
                    var id = (w.Name ?? "").Trim().ToUpperInvariant();

                    if (string.IsNullOrWhiteSpace(id))
                        continue;

                    parts.Add(id);
                }
            }

            if (!string.IsNullOrWhiteSpace(dest))
                parts.Add(dest);

            var cleaned = new List<string>();

            // EN: Consecutive duplicate entries are removed from the final flight plan text.
            // HU: Az egymás után ismétlődő elemek kikerülnek a végső repülési terv szövegéből.
            foreach (var p in parts)
            {
                if (cleaned.Count > 0 &&
                    string.Equals(cleaned[cleaned.Count - 1], p, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                cleaned.Add(p);
            }

            string s = string.Join(" ", cleaned);
            s = s.Replace("\r", " ").Replace("\n", " ").Trim();

            if (s.Length > 220)
                s = s.Substring(0, 220) + " ...";

            return s;
        }

        private string BuildWaypointsJsonForMap(
            RouteResult rr,
            List<(string Name, double Lat, double Lon)> sidResolved,
            List<(string Name, double Lat, double Lon)> starResolved)
        {
            var items = new List<(string id, double lat, double lon)>();

            if (sidResolved != null)
            {
                foreach (var w in sidResolved)
                    items.Add((w.Name, w.Lat, w.Lon));
            }

            if (starResolved != null)
            {
                foreach (var w in starResolved)
                    items.Add((w.Name, w.Lat, w.Lon));
            }

            if (rr != null && rr.Success && rr.NodeIds != null && rr.NodeIds.Count > 0)
            {
                var key = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                key.Add(rr.NodeIds[0]);
                key.Add(rr.NodeIds[rr.NodeIds.Count - 1]);

                if (rr.AirwayUsed != null && rr.AirwayUsed.Count > 0)
                {
                    string prevAwy = rr.AirwayUsed[0];

                    for (int edgeIndex = 1;
                         edgeIndex < rr.AirwayUsed.Count && edgeIndex + 1 < rr.NodeIds.Count;
                         edgeIndex++)
                    {
                        string awy = rr.AirwayUsed[edgeIndex];

                        // EN: Airway change points are useful labels on the map.
                        // HU: Az airway-váltási pontok hasznos címkék a térképen.
                        if (!string.Equals(awy, prevAwy, StringComparison.OrdinalIgnoreCase))
                        {
                            key.Add(rr.NodeIds[edgeIndex]);
                            prevAwy = awy;
                        }
                    }
                }

                foreach (var id in key)
                {
                    if (_airwayGraph.Nodes.TryGetValue(id, out var n))
                        items.Add((id, n.Lat, n.Lon));
                }
            }

            var dedup = items
                .Where(x => !string.IsNullOrWhiteSpace(x.id))
                .GroupBy(x => (x.id, Math.Round(x.lat, 6), Math.Round(x.lon, 6)))
                .Select(g => g.First())
                .ToList();

            var sb = new StringBuilder();

            sb.Append("[");

            for (int i = 0; i < dedup.Count; i++)
            {
                if (i > 0)
                    sb.Append(",");

                sb.Append("{");

                sb.AppendFormat(
                    CultureInfo.InvariantCulture,
                    "id:'{0}',lat:{1},lon:{2}",
                    EscapeJs(dedup[i].id),
                    dedup[i].lat,
                    dedup[i].lon);

                sb.Append("}");
            }

            sb.Append("]");
            return sb.ToString();
        }

        private static void AddUnique(List<(double Lat, double Lon)> pts, (double Lat, double Lon) p)
        {
            if (pts.Count == 0)
            {
                pts.Add(p);
                return;
            }

            var last = pts[pts.Count - 1];

            double dLat = Math.Abs(last.Lat - p.Lat);
            double dLon = Math.Abs(last.Lon - p.Lon);

            if (dLat < 1e-6 && dLon < 1e-6)
                return;

            pts.Add(p);
        }

        private static List<(double Lat, double Lon)> SmoothCatmullRom(
            List<(double Lat, double Lon)> pts,
            int samplesPerSegment,
            double maxSegmentNm)
        {
            if (pts == null || pts.Count < 3)
                return pts ?? new List<(double, double)>();

            if (samplesPerSegment < 1)
                samplesPerSegment = 1;

            var clean = new List<(double Lat, double Lon)>();

            foreach (var p in pts)
            {
                if (clean.Count == 0)
                {
                    clean.Add(p);
                    continue;
                }

                var last = clean[clean.Count - 1];

                if (Math.Abs(last.Lat - p.Lat) < 1e-9 &&
                    Math.Abs(last.Lon - p.Lon) < 1e-9)
                {
                    continue;
                }

                clean.Add(p);
            }

            if (clean.Count < 3)
                return clean;

            var outPts = new List<(double Lat, double Lon)>();
            outPts.Add(clean[0]);

            for (int i = 0; i < clean.Count - 1; i++)
            {
                var p1 = clean[i];
                var p2 = clean[i + 1];

                double segNm = AirwayRouter.HaversineNm(p1.Lat, p1.Lon, p2.Lat, p2.Lon);

                if (segNm > maxSegmentNm)
                {
                    outPts.Add(p2);
                    continue;
                }

                var p0 = (i == 0) ? clean[i] : clean[i - 1];
                var p3 = (i + 2 < clean.Count) ? clean[i + 2] : clean[i + 1];

                // EN: Catmull-Rom interpolation adds extra points between route points.
                // HU: A Catmull-Rom interpoláció plusz pontokat ad az útvonalpontok közé.
                for (int s = 1; s <= samplesPerSegment; s++)
                {
                    double t = s / (double)(samplesPerSegment + 1);

                    var q = CatmullRom(p0, p1, p2, p3, t);

                    double lat = Math.Max(-89.9999, Math.Min(89.9999, q.Lat));
                    double lon = NormalizeLon(q.Lon);

                    outPts.Add((lat, lon));
                }

                outPts.Add(p2);
            }

            var finalPts = new List<(double Lat, double Lon)>();

            foreach (var p in outPts)
            {
                if (finalPts.Count == 0)
                {
                    finalPts.Add(p);
                    continue;
                }

                var last = finalPts[finalPts.Count - 1];

                if (Math.Abs(last.Lat - p.Lat) < 1e-9 &&
                    Math.Abs(last.Lon - p.Lon) < 1e-9)
                {
                    continue;
                }

                finalPts.Add(p);
            }

            return finalPts;
        }

        private static (double Lat, double Lon) CatmullRom(
            (double Lat, double Lon) p0,
            (double Lat, double Lon) p1,
            (double Lat, double Lon) p2,
            (double Lat, double Lon) p3,
            double t)
        {
            double t2 = t * t;
            double t3 = t2 * t;

            double lat =
                0.5 * ((2.0 * p1.Lat) +
                (-p0.Lat + p2.Lat) * t +
                (2.0 * p0.Lat - 5.0 * p1.Lat + 4.0 * p2.Lat - p3.Lat) * t2 +
                (-p0.Lat + 3.0 * p1.Lat - 3.0 * p2.Lat + p3.Lat) * t3);

            double lon =
                0.5 * ((2.0 * p1.Lon) +
                (-p0.Lon + p2.Lon) * t +
                (2.0 * p0.Lon - 5.0 * p1.Lon + 4.0 * p2.Lon - p3.Lon) * t2 +
                (-p0.Lon + 3.0 * p1.Lon - 3.0 * p2.Lon + p3.Lon) * t3);

            return (lat, lon);
        }

        private static double NormalizeLon(double lon)
        {
            while (lon > 180)
                lon -= 360;

            while (lon < -180)
                lon += 360;

            return lon;
        }

        private static bool IsValidIcao(string s)
        {
            return !string.IsNullOrWhiteSpace(s) &&
                   s.Length == 4 &&
                   s.All(char.IsLetter);
        }

        private int ParseCruiseFlightLevel(string text)
        {
            string s = (text ?? "").Trim().ToUpperInvariant();

            if (string.IsNullOrEmpty(s))
                return 0;

            s = s.Replace("FL", "").Trim();

            if (!int.TryParse(s, out int val))
                return 0;

            if (val > 1000)
                val = val / 100;

            return val;
        }

        private static string EscapeJs(string s)
        {
            if (s == null)
                return "";

            return s
                .Replace("\\", "\\\\")
                .Replace("'", "\\'")
                .Replace("\r", "")
                .Replace("\n", "")
                .Replace("\t", " ");
        }

        private (double Lat, double Lon)? LookupAirport(string icao)
        {
            if (string.IsNullOrWhiteSpace(icao))
                return null;

            icao = icao.Trim().ToUpperInvariant();

            if (_airports.TryGetValue(icao, out var pos))
                return pos;

            return null;
        }

        private void btnCopyFlightPlan_Click(object sender, EventArgs e)
        {
            string fp = (txtFlightPlan.Text ?? "").Trim();

            if (string.IsNullOrWhiteSpace(fp))
            {
                MessageBox.Show("There is no flight plan to copy yet.");
                return;
            }

            try
            {
                Clipboard.SetText(fp);
                MessageBox.Show("Flight plan copied to clipboard.");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Copy failed:\n\n" + ex.Message);
            }
        }
    }
}
