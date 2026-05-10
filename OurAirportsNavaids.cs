using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Thesis_testing_1
{
    public sealed class NavaidInfo
    {
        // EN: The identifier of the navaid, for example a VOR or NDB code.
        // HU: A navigációs berendezés azonosítója, például VOR vagy NDB kód.
        public string Ident { get; set; }

        // EN: The type of the navaid, for example VOR, NDB, DME or VORTAC.
        // HU: A navigációs berendezés típusa, például VOR, NDB, DME vagy VORTAC.
        public string Type { get; set; } // VOR, NDB, DME, VORTAC,...
    }

    public static class OurAirportsNavaids
    {
        public static Dictionary<string, NavaidInfo> Load(string csvPath)
        {
            var dict = new Dictionary<string, NavaidInfo>(StringComparer.OrdinalIgnoreCase);

            if (!File.Exists(csvPath))
                return dict;

            using (var sr = new StreamReader(csvPath))
            {
                // EN: The first CSV line contains the column names.
                // HU: Az első CSV sor az oszlopneveket tartalmazza.
                string header = sr.ReadLine(); // skip header

                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    var cols = CsvSplit(line);
                    if (cols.Count < 6) continue;

                    string ident = cols[2];
                    string type = cols[4];

                    if (string.IsNullOrWhiteSpace(ident)) continue;

                    // EN: If the same navaid appears more than once, the first one is kept.
                    // HU: Ha ugyanaz a navigációs berendezés többször szerepel, az első bejegyzés marad meg.
                    if (!dict.ContainsKey(ident))
                        dict[ident] = new NavaidInfo { Ident = ident, Type = type };
                }
            }

            return dict;
        }

        private static List<string> CsvSplit(string line)
        {
            var result = new List<string>();
            bool inQuotes = false;
            var sb = new System.Text.StringBuilder();

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (c == '"')
                {
                    // EN: Two quotation marks inside quoted text mean one real quotation mark.
                    // HU: Idézőjelezett szövegben két idézőjel egy valódi idézőjelet jelent.
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == ',' && !inQuotes)
                {
                    result.Add(sb.ToString());
                    sb.Length = 0;
                }
                else
                {
                    sb.Append(c);
                }
            }

            result.Add(sb.ToString());
            return result;
        }
    }
}
