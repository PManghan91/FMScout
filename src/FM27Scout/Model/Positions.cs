namespace FM27Scout.Model;

public static class Positions
{
    public static string[] Names = ["GK", "SW", "DL", "DC", "DR", "DM", "ML", "MC", "MR", "AML", "AMC", "AMR", "ST", "WBL", "WBR"];
    public const int Accomplished = 15;

    public static int Index(string n) => Array.FindIndex(Names, x => x.Equals(n, StringComparison.OrdinalIgnoreCase));

    /// <summary>Expands "D", "M", "AM", "WB", "ST" or exact names (comma separated) to position indices.</summary>
    public static int[] Expand(string spec)
    {
        var res = new List<int>();
        foreach (var part in spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var p = part.ToUpperInvariant();
            string[] group = p switch
            {
                "D" => ["DL", "DC", "DR"], "M" => ["ML", "MC", "MR"], "AM" => ["AML", "AMC", "AMR"],
                "WB" => ["WBL", "WBR"], "CB" => ["DC"], "FB" => ["DL", "DR", "WBL", "WBR"], "W" => ["ML", "MR", "AML", "AMR"],
                "CM" => ["MC"], "CF" or "S" => ["ST"], _ => [p]
            };
            foreach (var g in group) { int i = Index(g); if (i >= 0) res.Add(i); }
        }
        return res.Distinct().ToArray();
    }

    /// <summary>FM-style summary like "D/WB (R)", "M/AM (R)", "ST (C)", "GK".</summary>
    public static string Describe(byte[] r)
    {
        // line -> sides with rating >= Accomplished
        var lines = new List<(string Line, string Sides)>();
        void Add(string line, params (string side, string pos)[] ps)
        {
            var sides = string.Concat(ps.Where(p => Get(r, p.pos) >= Accomplished).Select(p => p.side));
            if (sides.Length > 0) lines.Add((line, sides));
        }
        if (Get(r, "GK") >= Accomplished) lines.Add(("GK", ""));
        if (Get(r, "SW") >= Accomplished) lines.Add(("SW", ""));
        Add("D", ("R", "DR"), ("L", "DL"), ("C", "DC"));
        Add("WB", ("R", "WBR"), ("L", "WBL"));
        if (Get(r, "DM") >= Accomplished) lines.Add(("DM", ""));
        Add("M", ("R", "MR"), ("L", "ML"), ("C", "MC"));
        Add("AM", ("R", "AMR"), ("L", "AML"), ("C", "AMC"));
        if (Get(r, "ST") >= Accomplished) lines.Add(("ST", "C"));
        if (lines.Count == 0) return "";

        var parts = new List<string>();
        int i = 0;
        while (i < lines.Count)
        {
            int j = i;
            while (j + 1 < lines.Count && lines[j + 1].Sides == lines[i].Sides) j++;
            var name = string.Join("/", lines.Skip(i).Take(j - i + 1).Select(l => l.Line));
            var s = lines[i].Sides;
            parts.Add(s.Length == 0 ? name : $"{name} ({SortSides(s)})");
            i = j + 1;
        }
        return string.Join(", ", parts);
    }

    static string SortSides(string s) => string.Concat("RLC".Where(s.Contains));
    static int Get(byte[] r, string n) { int i = Index(n); return i >= 0 && i < r.Length ? r[i] : 0; }
}
