using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Thesis_testing_1
{
    public static class SidStarParser
    {
        public sealed class CifpLeg
        {
            // EN: Sequence number of the leg inside the procedure.
            // HU: Az eljáráson belüli szakasz sorszáma.
            public int Seq { get; set; }

            // EN: Procedure name.
            // HU: Az eljárás neve.
            public string Proc { get; set; }    

            // EN: Runway connected to the procedure, if it is available.
            // HU: Az eljáráshoz tartozó futópálya, ha rendelkezésre áll.
            public string Runway { get; set; }  

            // EN: Main fix or waypoint of the leg.
            // HU: A szakasz fő fix pontja vagy waypointja.
            public string Fix { get; set; }    

            // EN: CIFP leg type, for example IF, TF, CF or RF.
            // HU: A CIFP szakasztípusa, például IF, TF, CF vagy RF.
            public string LegType { get; set; } 

            // EN: Reference navaid or fix used by some leg types.
            // HU: Egyes szakasztípusokhoz tartozó referencia navaid vagy fix pont.
            public string RefIdent { get; set; } 

            // EN: Bearing value stored in tenths of a degree.
            // HU: Irányérték tizedfokban tárolva.
            public int? Brg10 { get; set; }     

            // EN: Distance value stored in tenths.
            // HU: Távolságérték tizedes formában tárolva.
            public int? Dist10 { get; set; }     
        }

        private sealed class ParsedAirport
        {
            // EN: Stores procedures by type, procedure name, runway and sequence number.
            // HU: Az eljárásokat típus, név, futópálya és sorszám szerint tárolja.
            public Dictionary<string, Dictionary<string, Dictionary<string, SortedDictionary<int, CifpLeg>>>> Data
                = new Dictionary<string, Dictionary<string, Dictionary<string, SortedDictionary<int, CifpLeg>>>>(StringComparer.OrdinalIgnoreCase);
        }

        // EN: CIFP leg types that are relevant for the parser.
        // HU: A feldolgozás során használt CIFP szakasztípusok.
        private static readonly HashSet<string> LegTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "IF","TF","DF","CF","RF","AF","HF","HA","HM","PI","VI","CI","CD","CA","FA","FC","FD"
        };

        // EN: Tokens that should not be treated as real waypoint names.
        // HU: Olyan elemek, amelyeket nem szabad valódi waypoint névként kezelni.
        private static readonly HashSet<string> IgnoreTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "RW","RWY","ALL","NONE","DCT","...",
            "SID","STAR"
        };

        // EN: Two-letter region prefixes that can appear in the data but are not real fixes.
        // HU: Kétbetűs régiójelölések, amelyek szerepelhetnek az adatokban, de nem valódi fix pontok.
        private static readonly HashSet<string> TwoLetterNoise = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "EG","LF","ED","EH","LE","LI","LO","LK","LN","LP","LQ","LR","LS","LT","LU","LV","LY","LZ",
            "EB","EK","EL","EN","EP","ES","EV","EY"
        };

        public static object ParseFile(string airportDatPath)
        {
            if (string.IsNullOrWhiteSpace(airportDatPath))
                throw new ArgumentException("airportDatPath is empty");

            if (!File.Exists(airportDatPath))
                throw new FileNotFoundException("Airport .dat not found", airportDatPath);

            var parsed = new ParsedAirport();

            foreach (var rawLine in File.ReadLines(airportDatPath))
            {
                var line = rawLine?.Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;

                if (!(line.StartsWith("SID:", StringComparison.OrdinalIgnoreCase) ||
                      line.StartsWith("STAR:", StringComparison.OrdinalIgnoreCase)))
                    continue;

                int colon = line.IndexOf(':');
                if (colon < 0) continue;

                string kind = line.Substring(0, colon).Trim().ToUpperInvariant(); 
                string rest = line.Substring(colon + 1);

                int semi = rest.IndexOf(';');
                if (semi >= 0) rest = rest.Substring(0, semi);

                var parts = rest.Split(',');
                if (parts.Length < 5) continue;

                int seq = TryParseSeq(parts[0]);

                string proc = Safe(parts.ElementAtOrDefault(2)).ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(proc)) continue;

                string runway = Safe(parts.ElementAtOrDefault(3)).ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(runway)) runway = "*";

                string legType = "";
                int legTypeIndex = -1;

                // EN: The parser searches the line for the first known CIFP leg type.
                // HU: A feldolgozó megkeresi a sorban az első ismert CIFP szakasztípust.
                for (int i = 0; i < parts.Length; i++)
                {
                    var tok = Safe(parts[i]).ToUpperInvariant();
                    if (LegTypes.Contains(tok))
                    {
                        legType = tok;
                        legTypeIndex = i;
                        break;
                    }
                }

                string fix = "";
                int fixScanEnd = (legTypeIndex > 0) ? legTypeIndex : parts.Length;

                // EN: The main fix is usually before the leg type.
                // HU: A fő fix pont általában a szakasztípus előtt található.
                for (int i = 4; i < fixScanEnd; i++)
                {
                    var tok = Safe(parts[i]).ToUpperInvariant();
                    if (IsPlausibleFixToken(tok))
                    {
                        fix = tok;
                        break;
                    }
                }

                string refIdent = "";
                if (legTypeIndex >= 0)
                {
                    // EN: Some leg types also contain a reference point after the leg type.
                    // HU: Egyes szakasztípusoknál a szakasztípus után referencia pont is szerepel.
                    for (int i = legTypeIndex + 1; i < parts.Length; i++)
                    {
                        var tok = Safe(parts[i]).ToUpperInvariant();
                        if (string.IsNullOrWhiteSpace(tok)) continue;
                        if (LegTypes.Contains(tok)) break;

                        if (!IsPlausibleFixToken(tok)) continue;
                        if (tok.Length == 2 && TwoLetterNoise.Contains(tok)) continue;

                        refIdent = tok;
                        break;
                    }
                }

                int? brg10 = null;
                int? dist10 = null;

                int scanStart = (legTypeIndex >= 0) ? legTypeIndex + 1 : 0;

                var intsAfter = new List<(int idx, int val)>();
                for (int i = scanStart; i < parts.Length; i++)
                {
                    var s = Safe(parts[i]);
                    if (s.Length == 0) continue;

                    if (!IsAllDigits(s)) continue;

                    if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v))
                        continue;

                    if (s.Length == 4)
                        intsAfter.Add((i, v));
                }

                // EN: The first suitable four-digit value is used as bearing.
                // HU: Az első megfelelő négyjegyű érték irányként kerül felhasználásra.
                foreach (var t in intsAfter)
                {
                    if (t.val >= 0 && t.val <= 3600)
                    {
                        brg10 = t.val;
                        break;
                    }
                }

                // EN: The last four-digit value is kept as distance.
                // HU: Az utolsó négyjegyű érték távolságként kerül eltárolásra.
                if (intsAfter.Count > 0)
                    dist10 = intsAfter[intsAfter.Count - 1].val;

                var leg = new CifpLeg
                {
                    Seq = seq,
                    Proc = proc,
                    Runway = runway,
                    Fix = fix,
                    LegType = legType,
                    RefIdent = refIdent,
                    Brg10 = brg10,
                    Dist10 = dist10
                };

                if (!parsed.Data.TryGetValue(kind, out var procMap))
                {
                    procMap = new Dictionary<string, Dictionary<string, SortedDictionary<int, CifpLeg>>>(
                        StringComparer.OrdinalIgnoreCase);
                    parsed.Data[kind] = procMap;
                }

                if (!procMap.TryGetValue(proc, out var rwyMap))
                {
                    rwyMap = new Dictionary<string, SortedDictionary<int, CifpLeg>>(StringComparer.OrdinalIgnoreCase);
                    procMap[proc] = rwyMap;
                }

                if (!rwyMap.TryGetValue(runway, out var seqMap))
                {
                    seqMap = new SortedDictionary<int, CifpLeg>();
                    rwyMap[runway] = seqMap;
                }

                // EN: Only legs with a usable fix are stored.
                // HU: Csak azok a szakaszok kerülnek tárolásra, amelyekhez használható fix tartozik.
                if (!string.IsNullOrWhiteSpace(fix))
                    seqMap[seq] = leg;
            }

            return parsed;
        }

        public static IEnumerable<string> ListProcedureNames(object parsedAirport, string kind)
        {
            var parsed = RequireParsed(parsedAirport);
            if (string.IsNullOrWhiteSpace(kind)) return Enumerable.Empty<string>();
            kind = kind.Trim().ToUpperInvariant();

            if (!parsed.Data.TryGetValue(kind, out var procMap))
                return Enumerable.Empty<string>();

            return procMap.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase);
        }

        public static List<CifpLeg> GetLegSequence(object parsedAirport, string kind, string procName)
        {
            var parsed = RequireParsed(parsedAirport);

            if (string.IsNullOrWhiteSpace(kind) || string.IsNullOrWhiteSpace(procName))
                return new List<CifpLeg>();

            kind = kind.Trim().ToUpperInvariant();
            procName = procName.Trim().ToUpperInvariant();

            if (!parsed.Data.TryGetValue(kind, out var procMap))
                return new List<CifpLeg>();

            if (!procMap.TryGetValue(procName, out var rwyMap))
                return new List<CifpLeg>();

            var candidates = rwyMap
                .Select(kv => new
                {
                    Runway = kv.Key,
                    SeqMap = kv.Value,
                    Count = kv.Value.Values.Count(v => v != null && !string.IsNullOrWhiteSpace(v.Fix))
                })
                .Where(x => x.Count > 0)
                .ToList();

            if (candidates.Count == 0)
                return new List<CifpLeg>();

            // EN: If there are multiple runway variants, the most complete one is selected.
            // HU: Ha több futópálya-változat is van, a legtöbb használható pontot tartalmazó kerül kiválasztásra.
            var best = candidates
                .OrderByDescending(x => x.Runway != "*" ? 1 : 0)
                .ThenByDescending(x => x.Count)
                .First();

            var result = new List<CifpLeg>();
            string prevFix = null;

            foreach (var kv in best.SeqMap)
            {
                var leg = kv.Value;
                if (leg == null) continue;

                var fix = (leg.Fix ?? "").Trim().ToUpperInvariant();
                if (!IsPlausibleFixToken(fix)) continue;

                if (prevFix != null && string.Equals(prevFix, fix, StringComparison.OrdinalIgnoreCase))
                    continue;

                leg.Fix = fix;
                leg.LegType = (leg.LegType ?? "").Trim().ToUpperInvariant();
                leg.RefIdent = (leg.RefIdent ?? "").Trim().ToUpperInvariant();

                result.Add(leg);
                prevFix = fix;
            }

            return result;
        }

        public static List<string> GetFixSequence(object parsedAirport, string kind, string procName)
        {
            var legs = GetLegSequence(parsedAirport, kind, procName);
            return legs.Select(l => (l.Fix ?? "").Trim().ToUpperInvariant())
                       .Where(x => IsPlausibleFixToken(x))
                       .ToList();
        }

        private static bool IsPlausibleFixToken(string tok)
        {
            if (string.IsNullOrWhiteSpace(tok)) return false;
            tok = tok.Trim().ToUpperInvariant();

            if (IgnoreTokens.Contains(tok)) return false;
            if (LegTypes.Contains(tok)) return false;
            if (tok.StartsWith("RW", StringComparison.OrdinalIgnoreCase)) return false;

            if (tok.Length < 3) return false;
            if (tok.Length > 10) return false;

            for (int i = 0; i < tok.Length; i++)
            {
                char c = tok[i];
                if (!(char.IsLetterOrDigit(c))) return false;
            }

            bool anyLetter = tok.Any(char.IsLetter);
            if (!anyLetter) return false;

            if (tok.Length == 2 && TwoLetterNoise.Contains(tok)) return false;

            return true;
        }

        private static bool IsAllDigits(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            for (int i = 0; i < s.Length; i++)
                if (!char.IsDigit(s[i])) return false;
            return true;
        }

        private static ParsedAirport RequireParsed(object parsedAirport)
        {
            if (parsedAirport is ParsedAirport ok) return ok;
            throw new ArgumentException("parsedAirport is not a ParsedAirport. Did you cache something else?");
        }

        private static int TryParseSeq(string s)
        {
            s = Safe(s);
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) return n;
            s = s.Trim();
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) return n;
            return 0;
        }

        private static string Safe(string s) => (s ?? "").Trim();
    }
}
