using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Thesis_testing_1
{
    // Egy fixpont adatait tároló osztály / Class storing the data of one fix point
    public sealed class FixPoint
    {
        public string Id { get; }
        public double Lat { get; }
        public double Lon { get; }

        // Konstruktor, amely beállítja a fixpont azonosítóját, szélességét és hosszúságát / Constructor that sets the fix point identifier, latitude, and longitude
        public FixPoint(string id, double lat, double lon)
        {
            Id = id;
            Lat = lat;
            Lon = lon;
        }
    }

    public static class EarthFixV600
    {

        // Betölti az earth_fix.dat fájlban található fixpontokat / Loads the fix points from the earth_fix.dat file
        public static Dictionary<string, List<FixPoint>> Load(string filePath)
        {
            // Ellenőrzi, hogy létezik-e a megadott fájl / Checks whether the specified file exists
            if (!File.Exists(filePath))
                throw new FileNotFoundException("earth_fix.dat not found", filePath);

            // Szótár létrehozása, amely az azonosító alapján tárolja a fixpontokat / Creates a dictionary that stores fix points by their identifier
            var fixes = new Dictionary<string, List<FixPoint>>(StringComparer.OrdinalIgnoreCase);

            using (var sr = new StreamReader(filePath))
            {

                // Az első két fejlécsor kihagyása / Skips the first two header lines
                sr.ReadLine();
                sr.ReadLine();

                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    // A sor elején és végén lévő szóközök eltávolítása / Removes whitespace from the beginning and end of the line
                    line = line.Trim();

                    // Üres sorok kihagyása / Skips empty lines
                    if (line.Length == 0) continue;

                    // A fájl végét jelző sor esetén kilép a ciklusból / Exits the loop when the end marker of the file is reached
                    if (line == "99") break;

                    // Komment sorok kihagyása / Skips comment lines
                    if (line.StartsWith("#")) continue;

                    // A sor feldarabolása szóközök mentén / Splits the line by whitespace
                    var parts = SplitWhitespace(line);

                    // Csak a pontosan három elemből álló sorokat dolgozza fel / Processes only lines that contain exactly three parts
                    if (parts.Count != 3) continue;

                    // Szélességi koordináta beolvasása / Reads the latitude coordinate
                    if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double lat))
                        continue;

                    // Hosszúsági koordináta beolvasása / Reads the longitude coordinate
                    if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double lon))
                        continue;

                    string id = parts[2];

                    // Üres azonosító esetén a sor kihagyása / Skips the line if the identifier is empty
                    if (string.IsNullOrWhiteSpace(id)) continue;

                    // Ha az adott azonosító még nem szerepel a szótárban, új lista jön létre hozzá / Creates a new list if the identifier is not yet present in the dictionary
                    if (!fixes.TryGetValue(id, out var list))
                    {
                        list = new List<FixPoint>(1);
                        fixes[id] = list;
                    }

                    // Az új fixpont hozzáadása az adott azonosítóhoz tartozó listához / Adds the new fix point to the list belonging to the given identifier
                    list.Add(new FixPoint(id, lat, lon));
                }
            }

            // A beolvasott fixpontok visszaadása / Returns the loaded fix points
            return fixes;
        }

        // A megadott szöveget szóközök mentén legfeljebb három részre bontja / Splits the given text by whitespace into at most three parts
        private static List<string> SplitWhitespace(string s)
        {
            var result = new List<string>(3);
            int i = 0;

            while (i < s.Length)
            {
                // Szóközök átugrása / Skips whitespace characters
                while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
                if (i >= s.Length) break;

                // Az aktuális rész kezdőpozíciójának eltárolása / Stores the starting position of the current part
                int start = i;

                // Az aktuális rész végéig halad / Moves forward until the end of the current part
                while (i < s.Length && !char.IsWhiteSpace(s[i])) i++;

                // Az aktuális rész hozzáadása az eredménylistához / Adds the current part to the result list
                result.Add(s.Substring(start, i - start));

                // Három rész után megáll, mert a várt formátum három oszlopból áll / Stops after three parts because the expected format contains three columns
                if (result.Count == 3) break;
            }

            return result;
        }
    }
}
