// EarthNavV810.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Thesis_testing_1
{
    public static class EarthNavV810
    {

        // Betölti az earth_nav.dat fájlban található navigációs pontokat / Loads the navigation points from the earth_nav.dat file
        public static Dictionary<string, List<NavaidPoint>> Load(string path)
        {
            // Szótár létrehozása, amely az azonosító alapján tárolja a navigációs pontokat / Creates a dictionary that stores navigation points by their identifier
            var res = new Dictionary<string, List<NavaidPoint>>(StringComparer.OrdinalIgnoreCase);

            // Ellenőrzi, hogy a megadott útvonal üres-e / Checks whether the specified path is empty
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("earth_nav.dat path is empty");

            // Ellenőrzi, hogy létezik-e a megadott fájl / Checks whether the specified file exists
            if (!File.Exists(path))
                throw new FileNotFoundException("earth_nav.dat not found", path);

            // A fájl sorainak bejárása / Iterates through the lines of the file
            foreach (var raw in File.ReadLines(path))
            {
                // A sor elején és végén lévő szóközök eltávolítása / Removes whitespace from the beginning and end of the line
                var line = (raw ?? "").Trim();

                // Üres sorok kihagyása / Skips empty lines
                if (line.Length == 0) continue;


                // Nem számmal kezdődő sorok kihagyása / Skips lines that do not start with a digit
                if (!char.IsDigit(line[0])) continue;


                // A sor feldarabolása szóközök mentén / Splits the line by whitespace
                var parts = SplitWs(line);

                // Csak a legalább nyolc elemből álló sorokat dolgozza fel / Processes only lines that contain at least eight parts
                if (parts.Count < 8) continue;


                // A navigációs pont típusának beolvasása / Reads the navigation point type
                if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int type))
                    continue;


                // Csak az NDB és VOR típusú pontokat tartja meg / Keeps only NDB and VOR type points
                if (type != 2 && type != 3) continue;

                // Szélességi koordináta beolvasása / Reads the latitude coordinate
                if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double lat)) continue;

                // Hosszúsági koordináta beolvasása / Reads the longitude coordinate
                if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double lon)) continue;

                // Az azonosító kiolvasása és nagybetűssé alakítása / Reads the identifier and converts it to uppercase
                string ident = (parts[7] ?? "").Trim().ToUpperInvariant();

                // Üres azonosító esetén a sor kihagyása / Skips the line if the identifier is empty
                if (string.IsNullOrWhiteSpace(ident)) continue;


                // A navigációs pont nevének alapértelmezett értéke / Default value of the navigation point name
                string name = "";

                // Ha van név a sorban, akkor a maradék részek összefűzése / If the line contains a name, joins the remaining parts
                if (parts.Count > 8)
                    name = string.Join(" ", parts.Skip(8));

                // Új navigációs pont objektum létrehozása / Creates a new navigation point object
                var p = new NavaidPoint
                {
                    Ident = ident,
                    Lat = lat,
                    Lon = lon,
                    Type = type,
                    Name = name
                };

                // Ha az adott azonosító még nem szerepel a szótárban, új lista jön létre hozzá / Creates a new list if the identifier is not yet present in the dictionary
                if (!res.TryGetValue(ident, out var list))
                {
                    list = new List<NavaidPoint>();
                    res[ident] = list;
                }

                // Az új navigációs pont hozzáadása az adott azonosítóhoz tartozó listához / Adds the new navigation point to the list belonging to the given identifier
                list.Add(p);
            }

            // A beolvasott navigációs pontok visszaadása / Returns the loaded navigation points
            return res;
        }

        // A megadott szöveget szóközök mentén részekre bontja / Splits the given text into parts by whitespace
        private static List<string> SplitWs(string s)
        {
            var list = new List<string>();
            int i = 0;
            while (i < s.Length)
            {
                // Szóközök átugrása / Skips whitespace characters
                while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
                if (i >= s.Length) break;

                // Az aktuális rész kezdőpozíciójának eltárolása / Stores the starting position of the current part
                int j = i;

                // Az aktuális rész végéig halad / Moves forward until the end of the current part
                while (j < s.Length && !char.IsWhiteSpace(s[j])) j++;

                // Az aktuális rész hozzáadása az eredménylistához / Adds the current part to the result list
                list.Add(s.Substring(i, j - i));
                i = j;
            }
            return list;
        }
    }
}
