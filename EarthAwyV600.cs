using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Thesis_testing_1
{
    public sealed class AirwayEdge
    {
        public string FromId { get; }
        public string ToId { get; }
        public int Dir { get; }
        public int Low { get; }
        public int High { get; }
        public string AirwayRaw { get; }

        public AirwayEdge(string fromId, string toId, int dir, int low, int high, string airwayRaw)
        {
            FromId = fromId;
            ToId = toId;
            Dir = dir;
            Low = low;
            High = high;
            AirwayRaw = airwayRaw;
        }
    }

    public sealed class AirwayNode
    {
        public string Id { get; }
        public double Lat { get; }
        public double Lon { get; }

        public AirwayNode(string id, double lat, double lon)
        {
            Id = id;
            Lat = lat;
            Lon = lon;
        }
    }

    public sealed class AirwayGraph
    {
        public Dictionary<string, AirwayNode> Nodes =
            new Dictionary<string, AirwayNode>(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, List<AirwayEdge>> Adj =
            new Dictionary<string, List<AirwayEdge>>(StringComparer.OrdinalIgnoreCase);

        public int EdgeCount { get; private set; }

        public void AddNodeIfMissing(string id, double lat, double lon)
        {
            if (!Nodes.ContainsKey(id))
                Nodes[id] = new AirwayNode(id, lat, lon);
        }

        public void AddEdge(string fromId, string toId, int dir, int low, int high, string airwayRaw)
        {
            List<AirwayEdge> list;
            if (!Adj.TryGetValue(fromId, out list))
            {
                list = new List<AirwayEdge>();
                Adj[fromId] = list;
            }

            list.Add(new AirwayEdge(fromId, toId, dir, low, high, airwayRaw));
            EdgeCount++;
        }
    }

    public static class EarthAwyV600
    {
        public static AirwayGraph Load(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("earth_awy.dat not found", filePath);

            var graph = new AirwayGraph();

            using (var sr = new StreamReader(filePath))
            {
                string first = sr.ReadLine();
                string second = sr.ReadLine();

                bool firstIsHeader = first != null && first.Trim() == "I";
                bool secondIsHeader = second != null && second.IndexOf("Version", StringComparison.OrdinalIgnoreCase) >= 0;

                if (!(firstIsHeader || secondIsHeader))
                {
                    ProcessLine(graph, first);
                    ProcessLine(graph, second);
                }

                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    if (line.Trim() == "99") break;
                    ProcessLine(graph, line);
                }
            }

            return graph;
        }

        private static void ProcessLine(AirwayGraph graph, string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;

            line = line.Trim();
            if (line.Length == 0) return;
            if (line.StartsWith("#")) return;

            var parts = SplitWhitespace(line);
            if (parts.Count < 10) return;

            string fromId = parts[0];
            double fromLat, fromLon, toLat, toLon;

            if (!TryParseDouble(parts[1], out fromLat)) return;
            if (!TryParseDouble(parts[2], out fromLon)) return;

            string toId = parts[3];
            if (!TryParseDouble(parts[4], out toLat)) return;
            if (!TryParseDouble(parts[5], out toLon)) return;

            int dir, low, high;
            if (!int.TryParse(parts[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out dir)) dir = 1;
            if (!int.TryParse(parts[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out low)) low = 0;
            if (!int.TryParse(parts[8], NumberStyles.Integer, CultureInfo.InvariantCulture, out high)) high = 0;

            string airwayRaw = parts[9];

            graph.AddNodeIfMissing(fromId, fromLat, fromLon);
            graph.AddNodeIfMissing(toId, toLat, toLon);

            // EN: Each airway segment is added in both directions, so the router can search both ways.
            // HU: A légifolyosó-szakasz mindkét irányban bekerül, így az útvonalkereső mindkét irányban tud vele számolni.
            graph.AddEdge(fromId, toId, dir, low, high, airwayRaw);
            graph.AddEdge(toId, fromId, dir, low, high, airwayRaw);
        }

        private static List<string> SplitWhitespace(string s)
        {
            var result = new List<string>(12);
            int i = 0;

            while (i < s.Length)
            {
                while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
                if (i >= s.Length) break;

                int start = i;
                while (i < s.Length && !char.IsWhiteSpace(s[i])) i++;

                result.Add(s.Substring(start, i - start));
            }

            return result;
        }

        private static bool TryParseDouble(string s, out double value)
        {
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }
    }
}
