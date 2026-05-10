// AirwayRouter.cs
using System;
using System.Collections.Generic;
using System.Linq;

namespace Thesis_testing_1
{
    // EN: Finds the enroute part of the route.
    // HU: Az útvonal enroute szakaszának keresését végzi.
    // The SID and STAR parts are handled in the Planner class.
    public static class AirwayRouter
    {
        public static List<string> FindNearestNodes(AirwayGraph g, double lat, double lon, int k)
        {
            var list = new List<Tuple<string, double>>();

            if (g == null || g.Nodes == null)
                return new List<string>();

            foreach (var kv in g.Nodes)
            {
                var n = kv.Value;
                double d = HaversineNm(lat, lon, n.Lat, n.Lon);
                list.Add(Tuple.Create(kv.Key, d));
            }

            return list
                .OrderBy(t => t.Item2)
                .Take(Math.Max(1, k))
                .Select(t => t.Item1)
                .ToList();
        }

        public static RouteResult FindBestRoute(
            AirwayGraph g,
            List<string> startCandidates,
            List<string> endCandidates,
            int preferredFl,
            double transitionPenaltyNm,
            double altPenaltyPerFL = 0.10,
            double maxDirectNm = 0.0,
            int maxDirectCandidates = 0,
            double directPenaltyNm = 999.0)
        {
            var res = new RouteResult();

            if (g == null || g.Nodes == null || g.Nodes.Count == 0)
            {
                res.Success = false;
                res.DebugMessage = "Graph empty.";
                return res;
            }

            if (startCandidates == null || startCandidates.Count == 0 ||
                endCandidates == null || endCandidates.Count == 0)
            {
                res.Success = false;
                res.DebugMessage = "No start/end candidates.";
                return res;
            }

            if (maxDirectNm < 0) maxDirectNm = 0;
            if (maxDirectCandidates < 0) maxDirectCandidates = 0;
            if (directPenaltyNm < 0) directPenaltyNm = 0;

            var goals = new HashSet<string>(endCandidates, StringComparer.OrdinalIgnoreCase);

            var dist = new Dictionary<StateKey, double>(new StateKeyComparer());
            var prev = new Dictionary<StateKey, PrevState>(new StateKeyComparer());

            var directCache = new Dictionary<string, List<DirectCandidate>>(StringComparer.OrdinalIgnoreCase);

            var heap = new MinHeap();

            foreach (var s in startCandidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(s)) continue;
                if (!g.Nodes.ContainsKey(s)) continue;

                var sk = new StateKey(s, "");
                dist[sk] = 0.0;
                heap.Push(sk, 0.0);
            }

            if (heap.Count == 0)
            {
                res.Success = false;
                res.DebugMessage = "No valid start candidates.";
                return res;
            }

            StateKey bestGoal = default(StateKey);
            bool found = false;

            int expandedStates = 0;
            const int maxExpandedStates = 200000;

            while (heap.Count > 0)
            {
                expandedStates++;

                if (expandedStates > maxExpandedStates)
                {
                    res.Success = false;
                    res.DebugMessage = "Router stopped: too many states.";
                    return res;
                }

                var cur = heap.Pop();
                var curKey = cur.Key;
                double curDist = cur.Priority;

                double bestKnown;

                if (!dist.TryGetValue(curKey, out bestKnown) || curDist > bestKnown + 1e-9)
                    continue;

                if (goals.Contains(curKey.NodeId))
                {
                    bestGoal = curKey;
                    found = true;
                    break;
                }

                if (!g.Nodes.TryGetValue(curKey.NodeId, out var curNode))
                    continue;

                if (g.Adj != null && g.Adj.TryGetValue(curKey.NodeId, out var edges) && edges != null)
                {
                    for (int i = 0; i < edges.Count; i++)
                    {
                        var e = edges[i];

                        string nextAirway = string.IsNullOrWhiteSpace(e.AirwayRaw)
                            ? "DCT"
                            : e.AirwayRaw.Trim().ToUpperInvariant();

                        if (!g.Nodes.TryGetValue(e.FromId, out var fromNode)) continue;
                        if (!g.Nodes.TryGetValue(e.ToId, out var toNode)) continue;

                        double w = HaversineNm(fromNode.Lat, fromNode.Lon, toNode.Lat, toNode.Lon);

                        if (!string.IsNullOrWhiteSpace(curKey.LastAirway) &&
                            !string.Equals(curKey.LastAirway, nextAirway, StringComparison.OrdinalIgnoreCase))
                        {
                            w += transitionPenaltyNm;
                        }

                        if (preferredFl > 0)
                        {
                            int diff = DistanceToBand(preferredFl, e.Low, e.High);
                            w += diff * altPenaltyPerFL;
                        }

                        Relax(
                            dist,
                            prev,
                            heap,
                            curKey,
                            e.ToId,
                            nextAirway,
                            curDist,
                            w,
                            e.Low,
                            e.High);
                    }
                }

                if (maxDirectNm > 0 && maxDirectCandidates > 0)
                {
                    var dctCandidates = GetDirectCandidates(
                        g,
                        curKey.NodeId,
                        maxDirectNm,
                        maxDirectCandidates,
                        directCache);

                    foreach (var dct in dctCandidates)
                    {
                        if (string.Equals(dct.ToId, curKey.NodeId, StringComparison.OrdinalIgnoreCase))
                            continue;

                        string nextAirway = "DCT";

                        double w = dct.DistanceNm + directPenaltyNm;

                        if (!string.IsNullOrWhiteSpace(curKey.LastAirway) &&
                            !string.Equals(curKey.LastAirway, nextAirway, StringComparison.OrdinalIgnoreCase))
                        {
                            w += transitionPenaltyNm;
                        }

                        int low = 50;
                        int high = 600;

                        Relax(
                            dist,
                            prev,
                            heap,
                            curKey,
                            dct.ToId,
                            nextAirway,
                            curDist,
                            w,
                            low,
                            high);
                    }
                }
            }

            if (!found)
            {
                res.Success = false;
                res.DebugMessage = "No path found.";
                return res;
            }

            var nodesRev = new List<string>();
            var awyRev = new List<string>();
            var bandsRev = new List<AltBand>();

            var walk = bestGoal;
            nodesRev.Add(walk.NodeId);

            while (prev.ContainsKey(walk))
            {
                awyRev.Add(walk.LastAirway);
                bandsRev.Add(prev[walk].Band);

                walk = prev[walk].Prev;
                nodesRev.Add(walk.NodeId);
            }

            nodesRev.Reverse();
            awyRev.Reverse();
            bandsRev.Reverse();

            if (awyRev.Count > nodesRev.Count - 1)
                awyRev = awyRev.Take(nodesRev.Count - 1).ToList();

            for (int i = 0; i < awyRev.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(awyRev[i]))
                    awyRev[i] = "DCT";
            }

            res.Success = true;
            res.DistanceNm = dist[bestGoal];
            res.NodeIds = nodesRev;
            res.AirwayUsed = awyRev;
            res.SuggestedFl = SuggestAltitude(preferredFl, bandsRev);

            int dctCount = awyRev.Count(a => string.Equals(a, "DCT", StringComparison.OrdinalIgnoreCase));
            res.DebugMessage = dctCount > 0 ? $"OK ({dctCount} DCT)" : "OK";

            return res;
        }

        private static void Relax(
            Dictionary<StateKey, double> dist,
            Dictionary<StateKey, PrevState> prev,
            MinHeap heap,
            StateKey curKey,
            string toId,
            string nextAirway,
            double curDist,
            double edgeCost,
            int low,
            int high)
        {
            if (string.IsNullOrWhiteSpace(toId))
                return;

            var nk = new StateKey(toId, nextAirway);
            double nd = curDist + edgeCost;

            if (!dist.TryGetValue(nk, out var old) || nd < old)
            {
                dist[nk] = nd;
                prev[nk] = new PrevState(curKey, low, high);
                heap.Push(nk, nd);
            }
        }

        private sealed class DirectCandidate
        {
            public string ToId;
            public double DistanceNm;
        }

        private static List<DirectCandidate> GetDirectCandidates(
            AirwayGraph g,
            string fromId,
            double maxDirectNm,
            int maxDirectCandidates,
            Dictionary<string, List<DirectCandidate>> cache)
        {
            if (cache.TryGetValue(fromId, out var cached))
                return cached;

            var result = new List<DirectCandidate>();

            if (g == null || g.Nodes == null || !g.Nodes.TryGetValue(fromId, out var from))
            {
                cache[fromId] = result;
                return result;
            }

            var alreadyConnected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (g.Adj != null && g.Adj.TryGetValue(fromId, out var existingEdges) && existingEdges != null)
            {
                foreach (var e in existingEdges)
                {
                    if (!string.IsNullOrWhiteSpace(e.ToId))
                        alreadyConnected.Add(e.ToId);
                }
            }

            foreach (var kv in g.Nodes)
            {
                string toId = kv.Key;

                if (string.Equals(toId, fromId, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (alreadyConnected.Contains(toId))
                    continue;

                var to = kv.Value;

                double latDiffNm = Math.Abs(to.Lat - from.Lat) * 60.0;

                if (latDiffNm > maxDirectNm)
                    continue;

                double cosLat = Math.Cos(ToRad((to.Lat + from.Lat) * 0.5));
                double lonDiffNm = Math.Abs(to.Lon - from.Lon) * 60.0 * Math.Max(0.1, cosLat);

                if (lonDiffNm > maxDirectNm)
                    continue;

                double d = HaversineNm(from.Lat, from.Lon, to.Lat, to.Lon);

                if (d <= 0.01 || d > maxDirectNm)
                    continue;

                result.Add(new DirectCandidate
                {
                    ToId = toId,
                    DistanceNm = d
                });
            }

            result = result
                .OrderBy(x => x.DistanceNm)
                .Take(maxDirectCandidates)
                .ToList();

            cache[fromId] = result;
            return result;
        }

        private static int SuggestAltitude(int preferredFl, List<AltBand> bands)
        {
            if (bands == null || bands.Count == 0)
                return preferredFl > 0 ? preferredFl : 0;

            int pref = preferredFl > 0 ? preferredFl : 350;

            int interLow = bands.Max(b => b.Low);
            int interHigh = bands.Min(b => b.High);

            int chosen;

            if (interLow <= interHigh)
            {
                chosen = Clamp(pref, interLow, interHigh);
            }
            else
            {
                var clamped = new List<int>(bands.Count);

                foreach (var b in bands)
                    clamped.Add(Clamp(pref, b.Low, b.High));

                clamped.Sort();
                chosen = clamped[clamped.Count / 2];
            }

            if (chosen < 50) chosen = 50;
            if (chosen > 600) chosen = 600;

            return chosen;
        }

        private static int DistanceToBand(int fl, int low, int high)
        {
            if (fl < low) return low - fl;
            if (fl > high) return fl - high;
            return 0;
        }

        private static int Clamp(int v, int lo, int hi)
        {
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }

        public static double HaversineNm(double lat1, double lon1, double lat2, double lon2)
        {
            const double R_km = 6371.0;
            const double kmToNm = 0.539956803;

            double dLat = ToRad(lat2 - lat1);
            double dLon = ToRad(lon2 - lon1);

            double a =
                Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRad(lat1)) * Math.Cos(ToRad(lat2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

            double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

            return (R_km * c) * kmToNm;
        }

        private static double ToRad(double deg)
        {
            return deg * Math.PI / 180.0;
        }

        private struct StateKey
        {
            public string NodeId;
            public string LastAirway;

            public StateKey(string nodeId, string lastAirway)
            {
                NodeId = nodeId;
                LastAirway = lastAirway ?? "";
            }
        }

        private sealed class StateKeyComparer : IEqualityComparer<StateKey>
        {
            public bool Equals(StateKey a, StateKey b)
            {
                return string.Equals(a.NodeId, b.NodeId, StringComparison.OrdinalIgnoreCase) &&
                       string.Equals(a.LastAirway, b.LastAirway, StringComparison.OrdinalIgnoreCase);
            }

            public int GetHashCode(StateKey k)
            {
                unchecked
                {
                    int h1 = (k.NodeId ?? "").ToUpperInvariant().GetHashCode();
                    int h2 = (k.LastAirway ?? "").ToUpperInvariant().GetHashCode();

                    return (h1 * 397) ^ h2;
                }
            }
        }

        private struct AltBand
        {
            public int Low;
            public int High;

            public AltBand(int low, int high)
            {
                Low = low;
                High = high;
            }
        }

        private struct PrevState
        {
            public StateKey Prev;
            public AltBand Band;

            public PrevState(StateKey prev, int low, int high)
            {
                Prev = prev;
                Band = new AltBand(low, high);
            }
        }

        private sealed class MinHeap
        {
            public struct Item
            {
                public StateKey Key;
                public double Priority;

                public Item(StateKey key, double pri)
                {
                    Key = key;
                    Priority = pri;
                }
            }

            private readonly List<Item> _a = new List<Item>();

            public int Count
            {
                get { return _a.Count; }
            }

            public void Push(StateKey key, double priority)
            {
                _a.Add(new Item(key, priority));
                SiftUp(_a.Count - 1);
            }

            public Item Pop()
            {
                var root = _a[0];
                var last = _a[_a.Count - 1];

                _a.RemoveAt(_a.Count - 1);

                if (_a.Count > 0)
                {
                    _a[0] = last;
                    SiftDown(0);
                }

                return root;
            }

            private void SiftUp(int i)
            {
                while (i > 0)
                {
                    int p = (i - 1) / 2;

                    if (_a[p].Priority <= _a[i].Priority)
                        break;

                    var tmp = _a[p];
                    _a[p] = _a[i];
                    _a[i] = tmp;

                    i = p;
                }
            }

            private void SiftDown(int i)
            {
                int n = _a.Count;

                while (true)
                {
                    int l = i * 2 + 1;
                    int r = l + 1;
                    int m = i;

                    if (l < n && _a[l].Priority < _a[m].Priority)
                        m = l;

                    if (r < n && _a[r].Priority < _a[m].Priority)
                        m = r;

                    if (m == i)
                        break;

                    var tmp = _a[m];
                    _a[m] = _a[i];
                    _a[i] = tmp;

                    i = m;
                }
            }
        }
    }
}
