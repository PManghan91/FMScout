using FMScout.Reader;

namespace FMScout.Probe;

/// <summary>Finds where staff attributes live using UI word bands (ground truth from docs/ground-truth-brr.md).</summary>
static class StaffSolve
{
    static readonly string[] Order = ["Unsuited", "Reasonable", "Average", "Competent", "Good", "Very Good", "Outstanding", "Elite"];
    static readonly string[] Attrs = ["Attacking", "Defending", "Fitness", "Goalkeeping", "Possession", "SetPieces", "Tactical", "Technical", "WWY",
        "Adaptability", "Authority", "Determination", "Motivating", "PeopleMgmt", "JPA", "JPP", "JSA", "Negotiating", "TK", "AnalysingData", "Physio", "SportsScience"];

    // name, dob year, bands in Attrs order (null = not shown)
    static readonly (string Name, int Year, string?[] B)[] Truth =
    [
        ("Korey Smith", 1991, ["Competent","Very Good","Reasonable","Unsuited","Competent","Competent","Good","Good","Good", "Competent","Competent","Good","Very Good","Good", "Good","Good","Average","Good","Average", null,null,null]),
        ("Byron Anthony", 1984, ["Average","Good","Reasonable","Unsuited","Good","Unsuited","Very Good","Average","Very Good", "Average","Good","Good","Very Good","Average", "Competent","Good","Unsuited","Reasonable","Competent", null,null,null]),
        ("Richard Thomas", 1976, [null,null,null,null,null,null,null,null,null, "Good","Competent","Good","Elite","Average", "Good","Good","Good","Good","Good", "Reasonable",null,null]),
        ("Millie Bombroff", 2003, [null,null,null,null,null,null,null,null,null, "Good","Average","Average","Average","Competent", "Unsuited","Competent","Average","Competent","Reasonable", null,"Average","Competent"]),
        ("Marko Vukčević", 1985, ["Competent","Competent","Very Good","Unsuited","Competent","Unsuited","Competent","Good","Good", "Average","Average","Competent","Average","Reasonable", "Average","Competent","Reasonable","Reasonable","Competent", "Reasonable","Reasonable","Unsuited"]),
        ("Mauro Castañares", 1982, ["Average","Average","Reasonable","Reasonable","Reasonable","Reasonable","Average","Competent","Reasonable", "Good","Good","Good","Good","Average", "Reasonable","Average","Unsuited","Reasonable","Average", null,null,null]),
        ("Michael Reiziger", 1973, ["Good","Very Good","Reasonable","Unsuited","Good","Competent","Good","Good","Very Good", "Good","Good","Good","Competent","Good", "Good","Good","Good","Competent","Good", null,null,null]),
        ("Alan Wright", 1971, ["Competent","Good","Unsuited","Unsuited","Competent","Competent","Good","Average","Outstanding", "Reasonable","Very Good","Good","Good","Competent", "Average","Average","Competent","Reasonable","Good", null,null,null]),
    ];

    public static void Run(FmGame g)
    {
        var m = g.Memory; var gp = m.Module("game_plugin.dll")!.Value; ulong vt = gp.Base + 0x4D8DE78;
        var people = new List<(string name, ulong v)>();
        var staff = g.PersonPointers().Where(v => m.ReadPtr(v) == vt).ToList();
        foreach (var t in Truth)
        {
            var hits = staff.Where(v => Analysis.Name(m, v).StartsWith(t.Name) && Analysis.FmDate(m, v + 0x88)?.Year == t.Year).ToList();
            Console.WriteLine($"{t.Name} b.{t.Year}: {hits.Count} match(es) {string.Join(",", hits.Select(h => $"0x{h:X}"))}");
            if (hits.Count == 1) people.Add((t.Name, hits[0])); else throw new Exception("ambiguous/missing " + t.Name);
        }

        // candidate sources: direct window around the person, and blocks reachable through pointers in the person
        const int W = 0x400;
        var sources = new List<(string label, Func<ulong, ulong> addr)>();
        sources.Add(("direct", v => v - W));
        for (int s = 0; s < 0x240; s += 8) { int ss = s; sources.Add(($"[+0x{s:X}]", v => m.ReadPtr(v + (ulong)ss))); }
        for (int s = 0; s < 0x240; s += 8) for (int s2 = 0; s2 < 0x100; s2 += 8)
        { int ss = s, tt = s2; sources.Add(($"[[+0x{s:X}]+0x{s2:X}]", v => { ulong a = m.ReadPtr(v + (ulong)ss); return a < 0x10000 ? 0 : m.ReadPtr(a + (ulong)tt); })); }

        foreach (var (label, addr) in sources)
        {
            var bufs = people.Select(p => { ulong a = addr(p.v); return a < 0x10000 || a > 0x7FF000000000 ? null : m.ReadBytes(a, 2 * W); }).ToArray();
            if (bufs.Any(b => b == null)) continue;
            int found = 0; var lines = new List<string>();
            if (people.Select(p => addr(p.v)).Distinct().Count() < people.Count) continue; // shared/static block, skip
            for (int ai = 0; ai < Attrs.Length; ai++)
            {
                var c = new List<int>();
                for (int k = 0; k < 2 * W; k++)
                {
                    bool ok = true; int known = 0;
                    var vals = new List<(int rank, int val)>();
                    for (int p = 0; p < people.Count; p++)
                    {
                        var band = Truth[p].B[ai]; if (band == null) continue;
                        vals.Add((Array.IndexOf(Order, band), bufs[p]![k])); known++;
                    }
                    if (known < 2) { ok = false; }
                    for (int x = 0; x < vals.Count && ok; x++) for (int y = 0; y < vals.Count && ok; y++)
                        if (vals[x].rank < vals[y].rank && vals[x].val >= vals[y].val) ok = false;
                    // need some spread and plausible range
                    if (ok && vals.Select(v => v.val).Distinct().Count() < Math.Min(3, vals.Select(v => v.rank).Distinct().Count())) ok = false;
                    if (ok && vals.Any(v => v.val == 0 || v.val > 100)) ok = false;
                    if (ok) c.Add(k);
                }
                if (c.Count > 0 && c.Count < 40) found++;
                lines.Add($"   {Attrs[ai],-14} {(c.Count > 30 ? c.Count + " cands" : string.Join(",", c.Select(k => label == "direct" ? $"{k - W:+#;-#}" : $"+0x{k:X}")))}");
            }
            if (found >= 5) { Console.WriteLine($"== source {label}: {found}/{Attrs.Length} attributes have few candidates"); lines.ForEach(Console.WriteLine); }
        }
    }
}


