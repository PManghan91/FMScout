using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FM27Scout.Model;

/// <summary>Everything the sidebar can set. Serialised as part of a saved search.</summary>
public sealed class FilterState
{
    public string? Name { get; set; }
    public int? AgeMin { get; set; }
    public int? AgeMax { get; set; }
    public int? CaMin { get; set; }
    public int? CaMax { get; set; }
    public int? PaMin { get; set; }
    public int? PaMax { get; set; }
    public int? HeightMin { get; set; }
    public string? Nation { get; set; }

    public List<string> Positions { get; set; } = [];
    public int PositionMin { get; set; } = Model.Positions.Accomplished;
    public string? Role { get; set; }
    public double? RoleMin { get; set; }
    public double? RolePotentialMin { get; set; }

    public string? Club { get; set; }
    public string? League { get; set; }
    public string? ValueMax { get; set; }
    public string? WageMax { get; set; }
    public int? ContractExpiresBy { get; set; }
    public bool FreeAgentsOnly { get; set; }
    public bool GettableOnly { get; set; }
    public double GettableMin { get; set; } = 50;

    public Dictionary<string, int> AttributeMin { get; set; } = new();
    public string Query { get; set; } = "";
}

public sealed class SavedSearch
{
    public string Name { get; set; } = "";
    public FilterState Filter { get; set; } = new();
    public override string ToString() => Name;
}

public static class SavedSearches
{
    static string FilePath => Path.Combine(AppPaths.Data, "saved-searches.json");
    static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    public static List<SavedSearch> Load()
    {
        try { return File.Exists(FilePath) ? JsonSerializer.Deserialize<List<SavedSearch>>(File.ReadAllText(FilePath)) ?? [] : []; }
        catch { return []; }
    }

    public static void Save(List<SavedSearch> list) => File.WriteAllText(FilePath, JsonSerializer.Serialize(list, Opts));
}

public sealed class QueryError(string msg) : Exception(msg);

/// <summary>
/// Builds a predicate from the sidebar state plus the query bar.
/// Query syntax (space separated, AND): field op value, op one of &lt;= &gt;= &lt; &gt; = != :
///   age&lt;=21 pa&gt;=150 pos:ST pos:D,DM league:"Premier League" club:arsenal nation:brazil
///   value&lt;=20m wage&lt;50k finishing&gt;=15 poa&gt;=70 (role code = role score) pot.poa&gt;=80 best&gt;=75
///   contract&lt;=2037 foot:left gk   (bare words match the name)
///   join&gt;=60 rep&lt;=6000 wrep&gt;=3000 status:"Squad Player" willing:likely
/// </summary>
public static class FilterBuilder
{
    public static Func<PlayerRow, bool> Build(FilterState f, RoleSet roles)
    {
        var preds = new List<Func<PlayerRow, bool>>();
        bool Has(string? s) => !string.IsNullOrWhiteSpace(s);

        if (Has(f.Name)) { var n = f.Name!.Trim(); preds.Add(p => Contains(p.Name, n) || Contains(p.FullName, n) || Contains(p.Source.Name, n)); }
        if (f.AgeMin is int a1) preds.Add(p => p.Age >= a1);
        if (f.AgeMax is int a2) preds.Add(p => p.Age <= a2);
        if (f.CaMin is int c1) preds.Add(p => p.CA >= c1);
        if (f.CaMax is int c2) preds.Add(p => p.CA <= c2);
        if (f.PaMin is int p1) preds.Add(p => p.PA >= p1);
        if (f.PaMax is int p2) preds.Add(p => p.PA <= p2);
        if (f.HeightMin is int h) preds.Add(p => p.Height >= h);
        if (Has(f.Nation)) { var n = f.Nation!.Trim(); preds.Add(p => Contains(p.Nation, n)); }

        if (f.Positions.Count > 0)
        {
            var idx = f.Positions.SelectMany(Model.Positions.Expand).Distinct().ToArray();
            int min = f.PositionMin;
            preds.Add(p => idx.Any(i => p.Positions[i] >= min));
        }
        if (Has(f.Role))
        {
            int ri = roles.IndexOf(f.Role!);
            if (ri >= 0)
            {
                if (f.RoleMin is double m) preds.Add(p => p.RoleScores[ri] >= m);
                if (f.RolePotentialMin is double pm) preds.Add(p => p.PotentialRoleScores[ri] >= pm);
            }
        }

        if (Has(f.Club)) { var n = f.Club!.Trim(); preds.Add(p => Contains(p.Club, n)); }
        if (Has(f.League)) { var n = f.League!.Trim(); preds.Add(p => Contains(p.League, n)); }
        if (Has(f.ValueMax)) { var v = Money(f.ValueMax!); preds.Add(p => p.Value <= v); }
        if (Has(f.WageMax)) { var v = Money(f.WageMax!); preds.Add(p => p.Wage <= v); }
        if (f.ContractExpiresBy is int y) preds.Add(p => p.ContractExpiry is { } d && d.Year <= y);
        if (f.FreeAgentsOnly) preds.Add(p => p.Source.Club == null);
        if (f.GettableOnly) { var gm = f.GettableMin; preds.Add(p => p.Join >= gm); }

        foreach (var (name, min) in f.AttributeMin)
        {
            int i = Attr.Index(name);
            if (i >= 0 && min > 0) preds.Add(p => p.A[i] >= min);
        }

        if (Has(f.Query)) preds.AddRange(ParseQuery(f.Query, roles));
        return p => { foreach (var pr in preds) if (!pr(p)) return false; return true; };
    }

    static readonly Regex Token = new(@"(?<f>[\w\.]+)\s*(?<op><=|>=|!=|<|>|=|:)\s*(?<v>""[^""]*""|\S+)|(?<w>""[^""]*""|\S+)", RegexOptions.Compiled);

    public static List<Func<PlayerRow, bool>> ParseQuery(string q, RoleSet roles)
    {
        var res = new List<Func<PlayerRow, bool>>();
        foreach (Match m in Token.Matches(q))
        {
            if (m.Groups["w"].Success)
            {
                var w = m.Groups["w"].Value.Trim('"');
                if (w.Equals("gk", StringComparison.OrdinalIgnoreCase)) { res.Add(p => p.IsGoalkeeper); continue; }
                if (w.Equals("free", StringComparison.OrdinalIgnoreCase)) { res.Add(p => p.Source.Club == null); continue; }
                res.Add(p => Contains(p.Name, w) || Contains(p.FullName, w) || Contains(p.Source.Name, w));
                continue;
            }
            string field = m.Groups["f"].Value.ToLowerInvariant(), op = m.Groups["op"].Value, val = m.Groups["v"].Value.Trim('"');
            res.Add(Field(field, op, val, roles));
        }
        return res;
    }

    static Func<PlayerRow, bool> Field(string field, string op, string val, RoleSet roles)
    {
        // text fields
        Func<PlayerRow, string?>? text = field switch
        {
            "name" => p => p.Name + " " + p.FullName, "club" => p => p.Club, "league" or "comp" => p => p.League,
            "nation" or "nat" => p => p.Nation, "foot" => p => p.Foot, "status" => p => p.SquadStatusName,
            "joinlabel" or "willing" => p => p.JoinLabel, _ => null
        };
        if (text != null)
        {
            bool neg = op == "!=";
            if (op is not (":" or "=" or "!=")) throw new QueryError($"'{field}' only supports : or =");
            return p => Contains(text(p), val) != neg;
        }
        if (field is "pos" or "position")
        {
            // pos:ST   pos:D,DM   pos:ST>=10 is written as pos:ST and posmin via sidebar; also allow "pos>=" not supported
            var idx = Model.Positions.Expand(val);
            if (idx.Length == 0) throw new QueryError($"unknown position '{val}'");
            return p => idx.Any(i => p.Positions[i] >= Model.Positions.Accomplished);
        }

        Func<PlayerRow, double>? num = field switch
        {
            "age" => p => p.Age ?? 0, "ca" => p => p.CA, "pa" => p => p.PA, "height" => p => p.Height,
            "value" => p => p.Value, "wage" => p => p.Wage, "best" => p => p.BestRoleScore, "pot" or "potbest" => p => p.BestPotentialScore,
            "contract" or "expiry" => p => p.ContractExpiry?.Year ?? 0, "join" => p => p.Join,
            "rep" => p => p.CurrentRep, "hrep" or "homerep" => p => p.HomeRep, "wrep" or "worldrep" => p => p.WorldRep, "clubrep" => p => p.ClubRep, _ => null
        };
        if (num == null)
        {
            int ai = Attr.Index(field);
            if (ai < 0) ai = Array.FindIndex(Attr.Names, n => Attr.Pretty(n).Replace(" ", "").Equals(field, StringComparison.OrdinalIgnoreCase));
            if (ai >= 0) num = p => p.A[ai];
        }
        if (num == null)
        {
            bool pot = field.StartsWith("pot.");
            int ri = roles.IndexOf(pot ? field[4..] : field);
            // role codes win over position names (e.g. "dm" = Defensive Midfielder score); position ratings via "r.dm"
            if (ri >= 0) num = pot ? p => p.PotentialRoleScores[ri] : p => p.RoleScores[ri];
            else if (field.StartsWith("r.") && Model.Positions.Index(field[2..]) is int pi && pi >= 0) num = p => p.Positions[pi];
        }
        if (num == null) throw new QueryError($"unknown field '{field}'");

        double v = field is "value" or "wage" ? Money(val) : double.Parse(val, CultureInfo.InvariantCulture);
        return op switch
        {
            "<=" => p => num(p) <= v, ">=" => p => num(p) >= v, "<" => p => num(p) < v, ">" => p => num(p) > v,
            "!=" => p => num(p) != v, _ => p => num(p) == v
        };
    }

    public static long Money(string s)
    {
        s = s.Trim().Replace("£", "").Replace(",", "").ToLowerInvariant();
        double mul = 1;
        if (s.EndsWith("m")) { mul = 1_000_000; s = s[..^1]; }
        else if (s.EndsWith("k")) { mul = 1_000; s = s[..^1]; }
        if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) throw new QueryError($"bad amount '{s}'");
        return (long)(d * mul);
    }

    static bool Contains(string? hay, string needle) =>
        hay != null && CultureInfo.InvariantCulture.CompareInfo.IndexOf(hay, needle, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;
}


