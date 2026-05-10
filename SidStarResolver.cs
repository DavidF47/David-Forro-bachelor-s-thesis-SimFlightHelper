// SidStarResolver.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Thesis_testing_1
{
    public sealed class SidStarResolver
    {
        private Dictionary<string, List<FixPoint>> _fixes;
        private Dictionary<string, List<NavaidPoint>> _navaids;
        private AirwayGraph _airwayGraph;

        // EN: Local fixes from the selected airport procedure file.
        // HU: A kiválasztott repülőtér eljárásfájljából származó lokális fix pontok.
        private Dictionary<string, List<(double Lat, double Lon)>> _localFixes =
            new Dictionary<string, List<(double Lat, double Lon)>>(StringComparer.OrdinalIgnoreCase);

        public SidStarResolver(
            Dictionary<string, List<FixPoint>> fixes,
            Dictionary<string, List<NavaidPoint>> navaids,
            AirwayGraph airwayGraph,
            Dictionary<string, List<(double Lat, double Lon)>> localFixes = null)
        {
            _fixes = fixes;
            _navaids = navaids;
            _airwayGraph = airwayGraph;

            if (localFixes != null)
                _localFixes = localFixes;
        }

        public void UpdateSources(
            Dictionary<string, List<FixPoint>> fixes,
            Dictionary<string, List<NavaidPoint>> navaids,
            AirwayGraph airwayGraph,
            Dictionary<string, List<(double Lat, double Lon)>> localFixes = null)
        {
            _fixes = fixes;
            _navaids = navaids;
            _airwayGraph = airwayGraph;

            if (localFixes != null)
                _localFixes = localFixes;
        }

        public void UpdateLocalFixes(Dictionary<string, List<(double Lat, double Lon)>> localFixes)
        {
            _localFixes = localFixes ?? new Dictionary<string, List<(double Lat, double Lon)>>(StringComparer.OrdinalIgnoreCase);
        }

        public sealed class ResolveResult
        {
            // EN: Resolved points that can be used for the route and map.
            // HU: Feloldott pontok, amelyek az útvonalhoz és a térképhez használhatók.
            public List<(string Name, double Lat, double Lon)> Points { get; } =
                new List<(string Name, double Lat, double Lon)>();

            // EN: Basic counters used for checking how much of the procedure was resolved.
            // HU: Alap számlálók, amelyek megmutatják, mennyi elemet sikerült feloldani az eljárásból.
            public int TotalTokens { get; set; }
            public int Resolved { get; set; }
            public int RejectedTooFar { get; set; }
            public int NotFound { get; set; }

            public List<string> DebugSteps { get; } = new List<string>();
        }

        // EN: Checks whether a point is inside the supported Europe/surrounding area.
        // HU: Ellenőrzi, hogy a pont a támogatott európai és környező területen belül van-e.
        public static bool IsInsideSupportedArea(double lat, double lon)
        {
            // EN: The polygon uses lon/lat order, but the method receives lat/lon.
            // HU: A poligon lon/lat sorrendet használ, de a függvény lat/lon értékeket kap.
            var poly = new (double Lon, double Lat)[]
            {
                (-25, 34),
                (-25, 72),
                (45, 72),
                (60, 60),
                (60, 50),
                (45, 40),
                (36, 34),
                (-25, 34)
            };

            bool inside = false;

            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                bool intersects =
                    ((poly[i].Lat > lat) != (poly[j].Lat > lat)) &&
                    (lon < (poly[j].Lon - poly[i].Lon) * (lat - poly[i].Lat) /
                           (poly[j].Lat - poly[i].Lat) + poly[i].Lon);

                if (intersects)
                    inside = !inside;
            }

            return inside;
        }

        public ResolveResult ResolveLegSequence(
            IEnumerable<SidStarParser.CifpLeg> legs,
            double startLat,
            double startLon,
            double maxStepKm)
        {
            var rr = new ResolveResult();
            if (legs == null) return rr;

            double refLat = startLat;
            double refLon = startLon;

            foreach (var leg in legs)
            {
                rr.TotalTokens++;

                string name = (leg?.Fix ?? "").Trim().ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(name))
                {
                    rr.DebugSteps.Add("?:NF()");
                    rr.NotFound++;
                    continue;
                }

                // EN: Some CIFP legs, mainly CF legs, can be calculated from bearing and distance.
                // HU: Egyes CIFP szakaszok, főleg a CF típusúak, irány és távolság alapján számíthatók ki.
                var computed = TryComputeFromLeg(leg, refLat, refLon);
                if (computed != null)
                {
                    double km = computed.Value.Km;

                    if (!IsInsideSupportedArea(computed.Value.Lat, computed.Value.Lon))
                    {
                        rr.NotFound++;
                        rr.DebugSteps.Add($"{name}:NF(OUTSIDE-GEOFENCE|CF-COMP)");
                        continue;
                    }

                    if (km > maxStepKm)
                    {
                        rr.RejectedTooFar++;
                        rr.DebugSteps.Add($"{name}:REJ({km.ToString("0", CultureInfo.InvariantCulture)}km|CF-COMP)");
                        continue;
                    }

                    if (IsConsecutiveDuplicate(rr, computed.Value.Lat, computed.Value.Lon))
                    {
                        rr.DebugSteps.Add($"{name}:OK(CF-COMP|DUP)");
                        refLat = computed.Value.Lat;
                        refLon = computed.Value.Lon;
                        rr.Resolved++;
                        continue;
                    }

                    rr.Points.Add((name, computed.Value.Lat, computed.Value.Lon));
                    rr.Resolved++;
                    rr.DebugSteps.Add($"{name}:OK(CF-COMP:{computed.Value.Meta})");

                    refLat = computed.Value.Lat;
                    refLon = computed.Value.Lon;
                    continue;
                }

                // EN: If the point cannot be calculated, it is searched in local fixes, fixes, navaids and airway nodes.
                // HU: Ha a pont nem számítható ki, akkor a lokális fixek, fix pontok, navaidok és airway csomópontok között keresi meg.
                var resolved = ResolveClosestWithSource(name, refLat, refLon);
                if (resolved == null)
                {
                    rr.NotFound++;
                    rr.DebugSteps.Add($"{name}:NF()");
                    continue;
                }

                if (resolved.Value.Km > maxStepKm)
                {
                    rr.RejectedTooFar++;
                    rr.DebugSteps.Add($"{name}:REJ({resolved.Value.Km.ToString("0", CultureInfo.InvariantCulture)}km|{resolved.Value.Src})");
                    continue;
                }

                if (IsConsecutiveDuplicate(rr, resolved.Value.Lat, resolved.Value.Lon))
                {
                    rr.DebugSteps.Add($"{name}:OK({resolved.Value.Src}|DUP)");
                    refLat = resolved.Value.Lat;
                    refLon = resolved.Value.Lon;
                    rr.Resolved++;
                    continue;
                }

                rr.Points.Add((name, resolved.Value.Lat, resolved.Value.Lon));
                rr.Resolved++;
                rr.DebugSteps.Add($"{name}:OK({resolved.Value.Src})");

                refLat = resolved.Value.Lat;
                refLon = resolved.Value.Lon;
            }

            return rr;
        }

        public ResolveResult ResolveSequence(IEnumerable<string> tokens, double startLat, double startLon, double maxStepKm)
        {
            var rr = new ResolveResult();
            if (tokens == null) return rr;

            double refLat = startLat;
            double refLon = startLon;

            foreach (var raw in tokens)
            {
                rr.TotalTokens++;

                var name = (raw ?? "").Trim().ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(name)) continue;

                var resolved = ResolveClosestWithSource(name, refLat, refLon);
                if (resolved == null)
                {
                    rr.NotFound++;
                    rr.DebugSteps.Add($"{name}:NF()");
                    continue;
                }

                if (resolved.Value.Km > maxStepKm)
                {
                    rr.RejectedTooFar++;
                    rr.DebugSteps.Add($"{name}:REJ({resolved.Value.Km.ToString("0", CultureInfo.InvariantCulture)}km|{resolved.Value.Src})");
                    continue;
                }

                if (IsConsecutiveDuplicate(rr, resolved.Value.Lat, resolved.Value.Lon))
                {
                    rr.DebugSteps.Add($"{name}:OK({resolved.Value.Src}|DUP)");
                    refLat = resolved.Value.Lat;
                    refLon = resolved.Value.Lon;
                    rr.Resolved++;
                    continue;
                }

                rr.Points.Add((name, resolved.Value.Lat, resolved.Value.Lon));
                rr.Resolved++;
                rr.DebugSteps.Add($"{name}:OK({resolved.Value.Src})");

                refLat = resolved.Value.Lat;
                refLon = resolved.Value.Lon;
            }

            return rr;
        }

        private static bool IsConsecutiveDuplicate(ResolveResult rr, double lat, double lon)
        {
            if (rr.Points.Count == 0) return false;
            var last = rr.Points[rr.Points.Count - 1];
            return Math.Abs(last.Lat - lat) < 1e-6 && Math.Abs(last.Lon - lon) < 1e-6;
        }

        private struct ComputedPoint
        {
            public double Lat;
            public double Lon;
            public double Km;
            public string Meta;
        }

        private ComputedPoint? TryComputeFromLeg(SidStarParser.CifpLeg leg, double curRefLat, double curRefLon)
        {
            if (leg == null) return null;

            string legType = (leg.LegType ?? "").Trim().ToUpperInvariant();
            if (!string.Equals(legType, "CF", StringComparison.OrdinalIgnoreCase))
                return null;

            string refIdent = (leg.RefIdent ?? "").Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(refIdent))
                return null;

            if (!leg.Brg10.HasValue || !leg.Dist10.HasValue)
                return null;

            double brg = leg.Brg10.Value / 10.0;
            double distNm = leg.Dist10.Value / 10.0;

            if (brg < 0 || brg > 360) return null;
            if (distNm <= 0 || distNm > 300) return null;

            var refPos = ResolveRefIdent(refIdent, curRefLat, curRefLon);
            if (refPos == null) return null;

            // EN: The destination point is calculated from the reference point, bearing and distance.
            // HU: A célpont a referencia pontból, irányból és távolságból kerül kiszámításra.
            var dest = DestinationPointWgs84(refPos.Value.Lat, refPos.Value.Lon, brg, distNm);

            double kmFromCur = HaversineKm(curRefLat, curRefLon, dest.Lat, dest.Lon);

            return new ComputedPoint
            {
                Lat = dest.Lat,
                Lon = dest.Lon,
                Km = kmFromCur,
                Meta = $"{refIdent} {brg.ToString("0.0", CultureInfo.InvariantCulture)}/{distNm.ToString("0.0", CultureInfo.InvariantCulture)}"
            };
        }

        private (double Lat, double Lon)? ResolveRefIdent(string ident, double refLat, double refLon)
        {
            // EN: For reference points, navaids are checked first.
            // HU: Referencia pontoknál először a navigációs berendezések kerülnek ellenőrzésre.
            if (_navaids != null && _navaids.TryGetValue(ident, out var navs) && navs != null && navs.Count > 0)
            {
                NavaidPoint best = null;
                double bestKm = double.MaxValue;

                foreach (var n in navs)
                {
                    if (!IsInsideSupportedArea(n.Lat, n.Lon))
                        continue;

                    double km = HaversineKm(refLat, refLon, n.Lat, n.Lon);
                    if (km < bestKm)
                    {
                        bestKm = km;
                        best = n;
                    }
                }

                if (best != null) return (best.Lat, best.Lon);
            }

            if (_fixes != null && _fixes.TryGetValue(ident, out var fixes) && fixes != null && fixes.Count > 0)
            {
                FixPoint best = null;
                double bestKm = double.MaxValue;

                foreach (var f in fixes)
                {
                    if (!IsInsideSupportedArea(f.Lat, f.Lon))
                        continue;

                    double km = HaversineKm(refLat, refLon, f.Lat, f.Lon);
                    if (km < bestKm)
                    {
                        bestKm = km;
                        best = f;
                    }
                }

                if (best != null) return (best.Lat, best.Lon);
            }

            if (_localFixes != null && _localFixes.TryGetValue(ident, out var locals) && locals != null && locals.Count > 0)
            {
                (double Lat, double Lon) best = default;
                double bestKm = double.MaxValue;
                bool found = false;

                foreach (var p in locals)
                {
                    if (!IsInsideSupportedArea(p.Lat, p.Lon))
                        continue;

                    double km = HaversineKm(refLat, refLon, p.Lat, p.Lon);
                    if (km < bestKm)
                    {
                        bestKm = km;
                        best = p;
                        found = true;
                    }
                }

                if (found) return (best.Lat, best.Lon);
            }

            if (_airwayGraph != null && _airwayGraph.Nodes != null && _airwayGraph.Nodes.ContainsKey(ident))
            {
                var n = _airwayGraph.Nodes[ident];

                if (!IsInsideSupportedArea(n.Lat, n.Lon))
                    return null;

                return (n.Lat, n.Lon);
            }

            return null;
        }

        private static (double Lat, double Lon) DestinationPointWgs84(double latDeg, double lonDeg, double bearingDeg, double distNm)
        {
            const double R = 6371000.0;
            double distM = distNm * 1852.0;

            double brng = Deg2Rad(bearingDeg);
            double lat1 = Deg2Rad(latDeg);
            double lon1 = Deg2Rad(lonDeg);

            double ang = distM / R;

            double sinLat1 = Math.Sin(lat1);
            double cosLat1 = Math.Cos(lat1);

            double sinAng = Math.Sin(ang);
            double cosAng = Math.Cos(ang);

            double lat2 = Math.Asin(sinLat1 * cosAng + cosLat1 * sinAng * Math.Cos(brng));

            double lon2 = lon1 + Math.Atan2(
                Math.Sin(brng) * sinAng * cosLat1,
                cosAng - sinLat1 * Math.Sin(lat2));

            double outLat = Rad2Deg(lat2);
            double outLon = Rad2Deg(lon2);

            outLon = NormalizeLon(outLon);

            if (outLat > 89.9999) outLat = 89.9999;
            if (outLat < -89.9999) outLat = -89.9999;

            return (outLat, outLon);
        }

        private static double NormalizeLon(double lon)
        {
            while (lon > 180) lon -= 360;
            while (lon < -180) lon += 360;
            return lon;
        }

        private (double Lat, double Lon, double Km, string Src)? ResolveClosestWithSource(string id, double refLat, double refLon)
        {
            // EN: Local procedure fixes have the highest priority.
            // HU: A lokális eljárásfixek kapják a legnagyobb prioritást.
            if (_localFixes != null && _localFixes.TryGetValue(id, out var locals) && locals != null && locals.Count > 0)
            {
                (double Lat, double Lon) best = default;
                double bestKm = double.MaxValue;
                bool found = false;

                foreach (var p in locals)
                {
                    if (!IsInsideSupportedArea(p.Lat, p.Lon))
                        continue;

                    double km = HaversineKm(refLat, refLon, p.Lat, p.Lon);
                    if (km < bestKm)
                    {
                        bestKm = km;
                        best = p;
                        found = true;
                    }
                }

                if (found)
                    return (best.Lat, best.Lon, bestKm, "LOCAL");
            }

            if (_fixes != null && _fixes.TryGetValue(id, out var candidates) && candidates != null && candidates.Count > 0)
            {
                FixPoint best = null;
                double bestKm = double.MaxValue;

                foreach (var c in candidates)
                {
                    if (!IsInsideSupportedArea(c.Lat, c.Lon))
                        continue;

                    double km = HaversineKm(refLat, refLon, c.Lat, c.Lon);
                    if (km < bestKm)
                    {
                        bestKm = km;
                        best = c;
                    }
                }

                if (best != null)
                    return (best.Lat, best.Lon, bestKm, "FIX");
            }

            if (_navaids != null && _navaids.TryGetValue(id, out var navs) && navs != null && navs.Count > 0)
            {
                NavaidPoint best = null;
                double bestKm = double.MaxValue;

                foreach (var n in navs)
                {
                    if (!IsInsideSupportedArea(n.Lat, n.Lon))
                        continue;

                    double km = HaversineKm(refLat, refLon, n.Lat, n.Lon);
                    if (km < bestKm)
                    {
                        bestKm = km;
                        best = n;
                    }
                }

                if (best != null)
                    return (best.Lat, best.Lon, bestKm, "NAV");
            }

            if (_airwayGraph != null && _airwayGraph.Nodes != null && _airwayGraph.Nodes.ContainsKey(id))
            {
                var n = _airwayGraph.Nodes[id];

                if (!IsInsideSupportedArea(n.Lat, n.Lon))
                    return null;

                double km = HaversineKm(refLat, refLon, n.Lat, n.Lon);
                return (n.Lat, n.Lon, km, "AWY");
            }

            return null;
        }

        private static double HaversineKm(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6371.0;
            double dLat = Deg2Rad(lat2 - lat1);
            double dLon = Deg2Rad(lon2 - lon1);

            double a =
                Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(Deg2Rad(lat1)) * Math.Cos(Deg2Rad(lat2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

            double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return R * c;
        }

        private static double Deg2Rad(double deg) => deg * (Math.PI / 180.0);
        private static double Rad2Deg(double rad) => rad * (180.0 / Math.PI);
    }
}
