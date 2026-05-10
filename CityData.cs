using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public static class CityData
{
    private static List<string> _cities = new List<string>();
    public static IReadOnlyList<string> Cities => _cities;

    public static void LoadCities(string csvPath)
    {
        if (!File.Exists(csvPath))
        {
            _cities = new List<string>();
            return;
        }

        try
        {
            var cities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // EN: The first CSV line is skipped because it contains the column names.
            // HU: Az első CSV sor kimarad, mert az oszlopneveket tartalmazza.
            foreach (var line in File.ReadLines(csvPath).Skip(1)) 
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var parts = line.Split(',');
                if (parts.Length == 0)
                    continue;

                string cityRaw = parts[0]?.Trim();
                if (string.IsNullOrEmpty(cityRaw))
                    continue;

                if (cityRaw.Length >= 2 && cityRaw[0] == '"' && cityRaw[cityRaw.Length - 1] == '"')
                    cityRaw = cityRaw.Substring(1, cityRaw.Length - 2);

                string city = cityRaw.Trim();
                if (string.IsNullOrWhiteSpace(city))
                    continue;

                cities.Add(city);
            }

            _cities = cities
                .OrderBy(c => c)
                .ToList();
        }
        catch
        {
            _cities = new List<string>();
        }
    }
}
