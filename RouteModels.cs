using System;
using System.Collections.Generic;
using System.Text;

namespace Thesis_testing_1
{
    public sealed class RouteResult
    {
        // EN: Shows whether the route search was successful.
        // HU: Jelzi, hogy az útvonalkeresés sikeres volt-e.
        public bool Success { get; set; }

        // EN: Total route distance in nautical miles.
        // HU: Az útvonal teljes távolsága tengeri mérföldben.
        public double DistanceNm { get; set; }

        // EN: Ordered list of route nodes.
        // HU: Az útvonal csomópontjainak sorrendben tárolt listája.
        public List<string> NodeIds { get; set; } = new List<string>();

        // EN: Airway names used between the route nodes.
        // HU: Az útvonalpontok között használt légifolyosók nevei.
        public List<string> AirwayUsed { get; set; } = new List<string>();

        // EN: Suggested cruising flight level.
        // HU: A javasolt utazó repülési szint.
        public int SuggestedFl { get; set; }

        // EN: Extra information about the route search.
        // HU: Kiegészítő információ az útvonalkeresés eredményéről.
        public string DebugMessage { get; set; } = "";
    }


    public static class RouteStringBuilder
    {
        public static string BuildRouteShort(string originIcao, string destIcao, RouteResult rr)
        {
            if (rr == null || !rr.Success || rr.NodeIds == null || rr.NodeIds.Count == 0)
                return $"{originIcao} DCT {destIcao}";

            var sb = new StringBuilder();
            sb.Append(originIcao).Append(" DCT ").Append(rr.NodeIds[0]);

            if (rr.AirwayUsed == null || rr.AirwayUsed.Count == 0)
                return sb.Append(" DCT ").Append(destIcao).ToString();

            string current = rr.AirwayUsed[0];

            // EN: The short format only writes the airway name when it changes.
            // HU: A rövid formátum csak akkor írja ki a légifolyosó nevét, amikor az változik.
            for (int i = 1; i < rr.AirwayUsed.Count; i++)
            {
                string next = rr.AirwayUsed[i];
                if (!string.Equals(next, current, StringComparison.OrdinalIgnoreCase))
                {
                    sb.Append(" ").Append(current).Append(" ").Append(rr.NodeIds[i]);
                    current = next;
                }
            }

            sb.Append(" ").Append(current).Append(" ").Append(rr.NodeIds[rr.NodeIds.Count - 1]);
            sb.Append(" DCT ").Append(destIcao);
            return sb.ToString();
        }

        public static string BuildRouteLong(string originIcao, string destIcao, RouteResult rr)
        {
            if (rr == null || !rr.Success || rr.NodeIds == null || rr.NodeIds.Count == 0)
                return $"{originIcao} DCT {destIcao}";

            var sb = new StringBuilder();
            sb.Append(originIcao).Append(" DCT ").Append(rr.NodeIds[0]);

            int edgeCount = Math.Min(rr.AirwayUsed.Count, rr.NodeIds.Count - 1);

            // EN: The long format writes the route point by point.
            // HU: A hosszú formátum pontonként írja ki az útvonalat.
            for (int i = 0; i < edgeCount; i++)
            {
                string awy = rr.AirwayUsed[i];
                string nextNode = rr.NodeIds[i + 1];

                if (!string.IsNullOrWhiteSpace(awy) && !string.Equals(awy, "DCT", StringComparison.OrdinalIgnoreCase))
                    sb.Append(" ").Append(awy);

                sb.Append(" ").Append(nextNode);
            }

            sb.Append(" DCT ").Append(destIcao);
            return sb.ToString();
        }
    }
}
