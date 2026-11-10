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

    /// <summary>"Find upgrade": only players better than this one in Role, who beat him on CA or PA and would join.</summary>
    public uint? UpgradeForUid { get; set; }
    public string? UpgradeForName { get; set; }
    public double UpgradeMinScore { get; set; }
    public int UpgradeMinCa { get; set; }
    public int UpgradeMinPa { get; set; }
    public double UpgradeIncScore { get; set; }
    public double UpgradeIncPot { get; set; }
    public int UpgradeIncCa { get; set; }
    public int UpgradeIncPa { get; set; }
    public bool ShortlistOnly { get; set; }
    public bool NewgensOnly { get; set; }
    public bool BargainsOnly { get; set; }
    public bool HideWpUnlikely { get; set; }
    public bool HideCantSign { get; set; }
    public bool HideArranged { get; set; }
    // Find upgrade settings (editable in the dialog)
    public double UpgradeMargin { get; set; } = 5;
    public double? UpgradeMaxFee { get; set; }
    public double? UpgradeMaxWage { get; set; }
    public int? UpgradeAgeMin { get; set; }
    public int? UpgradeAgeMax { get; set; }
    public double UpgradeMinJoin { get; set; } = 50;
    public string? UpgradeClub { get; set; }
    public string Query { get; set; } = "";
    /// <summary>Optional default sort for built-in searches (column key, descending unless prefixed with "+").</summary>
    public string? Sort { get; set; }
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
        catch { FMScout.Reader.AtomicFile.MoveAside(FilePath); return []; } // keep the broken file for inspection, don't overwrite it
    }

    public static void Save(List<SavedSearch> list) => FMScout.Reader.AtomicFile.WriteAllText(FilePath, JsonSerializer.Serialize(list, Opts));
}

public sealed class QueryError(string msg) : Exception(msg);

/// <summary>
/// Builds a predicate from the sidebar state plus the query bar.
/// Query syntax (space separated, AND): field op value, op one of &lt;= &gt;= &lt; &gt; = != :
///   age&lt;=21 pa&gt;=150 pos:ST pos:D,DM league:"Premier League" club:arsenal nation:brazil
///   value&lt;=20m wage&lt;50k finishing&gt;=15 poa&gt;=70 (role code = role score) pot.poa&gt;=80 best&gt;=75
///   contract&lt;=2037 foot:left gk   (bare words match the name)
///   expires&lt;=6m joined&gt;=-12m (relative to the game date)  ca&gt;=squad+5 (vs your squad's median CA)  age:18-23 (range)
///   -newgen / !bargain (negation)  loan  cansign  precontract  trn (transfer arranged; -trn to hide)  risky  risk:injury  nfs  improving  dca&gt;=5
///   regen bargain valueratio&lt;=0.5   join&gt;=60 rep&lt;=6000 wrep&gt;=3000 status:"Squad Player" willing:likely
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
        if (f.ShortlistOnly) preds.Add(p => Shortlist.Contains(p.Uid));
        if (f.NewgensOnly) preds.Add(p => p.IsRegen);
        if (f.BargainsOnly) preds.Add(p => p.IsBargain);
        if (f.HideWpUnlikely) preds.Add(p => !p.WpText.Contains("unlikely"));
        if (f.HideCantSign) preds.Add(p => p.CanSign);
        if (f.HideArranged || f.UpgradeForUid != null) preds.Add(p => !p.HasArranged); // upgrades never suggest players already sold
        if (f.GettableOnly) { var gm = f.GettableMin; preds.Add(p => p.Join >= gm); }

        foreach (var (name, min) in f.AttributeMin)
        {
            int i = Attr.Index(name);
            if (i >= 0 && min > 0) preds.Add(p => p.A[i] >= min);
        }

        if (f.UpgradeForUid != null && Has(f.Role) && roles.IndexOf(f.Role!) is int ur && ur >= 0)
        {
            var club = f.UpgradeClub; double inc = f.UpgradeIncScore, incPot = f.UpgradeIncPot; int pa = f.UpgradeIncPa;
            double margin = f.UpgradeMargin, minJoin = f.UpgradeMinJoin;
            double maxFee = f.UpgradeMaxFee ?? double.PositiveInfinity, maxWage = f.UpgradeMaxWage ?? double.PositiveInfinity;
            int ageMin = f.UpgradeAgeMin ?? 0, ageMax = f.UpgradeAgeMax ?? 99;
            preds.Add(p => p.Uid != f.UpgradeForUid && p.Club != club && p.CanSign && p.Join >= minJoin
                && !(p.Fee > maxFee) && !double.IsNaN(p.Fee) && p.Wage <= maxWage && (p.Age ?? 0) >= ageMin && (p.Age ?? 0) <= ageMax
                && Upgrade.Qualifies(p, ur, inc, incPot, pa, margin));
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
            bool negate = false;
            if (m.Groups["w"].Success)
            {
                var w = m.Groups["w"].Value.Trim('"');
                if (w.Length > 1 && (w[0] == '-' || w[0] == '!')) { negate = true; w = w[1..]; }
                var one = Word(w);
                res.Add(negate ? p => !one(p) : one);
                continue;
            }
            string field = m.Groups["f"].Value.ToLowerInvariant(), op = m.Groups["op"].Value, val = m.Groups["v"].Value.Trim('"');
            if (field.Length > 1 && (field[0] == '-' || field[0] == '!')) { negate = true; field = field[1..]; }
            var pr = Field(field, op, val, roles);
            res.Add(negate ? p => !pr(p) : pr);
        }
        return res;
    }

    static Func<PlayerRow, bool> Word(string w)
    {
        switch (w.ToLowerInvariant())
        {
            case "gk": return p => p.IsGoalkeeper;
            case "free": return p => p.Source.Club == null;
            case "regen" or "newgen": return p => p.IsRegen;
            case "real": return p => !p.IsRegen;
            case "bargain": return p => p.IsBargain;
            case "shortlist": return p => Shortlist.Contains(p.Uid);
            case "cansign" or "signable": return p => p.CanSign;
            case "precontract": return p => p.PreContract;
            case "trn" or "arranged" or "transferarranged": return p => p.HasArranged;
            case "infm" or "fmshortlist": return p => p.InFm;
            case "risky": return p => p.RiskCount > 0;
            case "nfs": return p => p.NotForSale;
            case "improving": return p => p.DeltaCaSeason > 0;
            // loan targets: young, not a first-choice at a clearly bigger club, but at least as good as our typical player
            case "loan" or "loantarget":
                return p => p.Club != ClubContext.MyClubName && p.Source.Club != null && p.Age <= 21 && p.CanSign
                    && p.Source.SquadStatus is 4 or 5 or 7 or 10 or 11 or 13 or 14 or 16 or 20
                    && p.ClubRep >= ClubContext.MyReputation + 800 && p.CA >= ClubContext.SquadMedianCa;
        }
        return p => Contains(p.Name, w) || Contains(p.FullName, w) || Contains(p.Source.Name, w);
    }

    static Func<PlayerRow, bool> Field(string field, string op, string val, RoleSet roles)
    {
        // text fields
        Func<PlayerRow, string?>? text = field switch
        {
            "name" => p => p.Name + " " + p.FullName, "club" => p => p.Club, "league" or "comp" => p => p.League,
            "nation" or "nat" => p => p.Nation, "foot" => p => p.Foot, "status" => p => p.SquadStatusName,
            "joinlabel" or "willing" => p => p.JoinLabel, "personality" or "pers" => p => p.PersonalityLabel, "nat2" or "passport" => p => p.Nationalities, "favclub" => p => string.Join(", ", p.Source.FavouredClubs), "outlook" or "development" => p => p.OutlookText, "risk" => p => p.RiskText, "rule" => p => p.TransferRule, _ => null
        };
        if (text != null)
        {
            bool neg = op == "!=";
            if (op is not (":" or "=" or "!=")) throw new QueryError($"'{field}' only supports : or =");
            return p => Contains(text(p), val) != neg;
        }
        if (field is "wp" or "workpermit")
        {
            var w = val.ToLowerInvariant();
            Func<PlayerRow, bool> m = w switch
            {
                "needed" or "yes" => p => p.WpNeeded, "none" or "no" => p => !p.WpNeeded && p.WpText.Length > 0,
                "ok" or "likely" => p => !p.WpNeeded || p.WpText.Contains("likely OK"), "unlikely" => p => p.WpText.Contains("unlikely"),
                _ => p => p.WpText.Contains(val, StringComparison.OrdinalIgnoreCase)
            };
            return op == "!=" ? p => !m(p) : m;
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
            "contract" or "expiry" or "expires" => p => YearFrac(p.ContractExpiry), "join" => p => p.Join, "fee" => p => double.IsNaN(p.Fee) ? double.MaxValue : p.Fee,
            "gain" => p => p.GainOverSquad, "vfmpot" => p => p.VfmPot, "risks" => p => p.RiskCount, "dca" => p => p.DeltaCaSeason ?? 0, "dcalast" => p => p.DeltaCaLast ?? 0,
            "valueratio" or "vr" => p => p.ValueRatio, "uid" => p => p.Uid, "joined" => p => YearFrac(p.Joined), "vfm" => p => p.Vfm, "rep" => p => p.CurrentRep, "ambition" => p => p.Ambition, "apps" or "careerapps" => p => p.CareerApps, "goals" or "careergoals" => p => p.CareerGoals, "gpg" => p => p.GoalsPerGame, "loyalty" => p => p.Loyalty, "professionalism" or "prof" => p => p.Professionalism, "hrep" or "homerep" => p => p.HomeRep, "wrep" or "worldrep" => p => p.WorldRep, "clubrep" => p => p.ClubRep, _ => null
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

        bool dateField = field is "contract" or "expiry" or "expires" or "joined";
        // ranges: age:18-23, ca:120-140
        if (op is ":" or "=" && Regex.Match(val, @"^(-?[\d\.]+)-([\d\.]+)$") is { Success: true } rg)
        {
            double lo = Num(rg.Groups[1].Value, field, dateField), hi = Num(rg.Groups[2].Value, field, dateField, upper: true);
            return p => num(p) >= lo && num(p) <= hi;
        }
        double v = Num(val, field, dateField, upper: op is "<=" or "<");
        return op switch
        {
            "<=" => p => num(p) <= v, ">=" => p => num(p) >= v, "<" => p => num(p) < v, ">" => p => num(p) > v,
            "!=" => p => num(p) != v, _ => p => num(p) == v
        };
    }

    /// <summary>Date as a fractional year (2037.5 = early July 2037); 0 if missing.</summary>
    public static double YearFrac(DateOnly? d) => d is { } x ? x.Year + (x.DayOfYear - 1) / 366.0 : 0;

    /// <summary>
    /// Parses a query value. Money: 20m, 500k. Dates: 2037 (a year), or relative to the game date: 6m, +6m, -12m, 1y.
    /// CA/PA: squad, squad+10, squad-5 (your squad's median CA).
    /// </summary>
    static double Num(string val, string field, bool dateField, bool upper = false)
    {
        val = val.Trim();
        if (field is "value" or "wage" or "fee") return Money(val);
        if (dateField)
        {
            var rel = Regex.Match(val, @"^([+-]?)(\d+)([my])$", RegexOptions.IgnoreCase);
            if (rel.Success)
            {
                int n = int.Parse(rel.Groups[2].Value) * (rel.Groups[1].Value == "-" ? -1 : 1);
                var d = rel.Groups[3].Value.ToLowerInvariant() == "y" ? ClubContext.Today.AddYears(n) : ClubContext.Today.AddMonths(n);
                return YearFrac(d);
            }
            if (int.TryParse(val, out var year) && year is > 1900 and < 3000) return upper ? year + 0.9999 : year; // a plain year
        }
        if (field is "ca" or "pa" && val.StartsWith("squad", StringComparison.OrdinalIgnoreCase))
        {
            var rest = val[5..];
            return ClubContext.SquadMedianCa + (rest.Length > 0 ? double.Parse(rest, CultureInfo.InvariantCulture) : 0);
        }
        if (!double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var r)) throw new QueryError($"bad value '{val}' for {field}");
        return r;
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






/// <summary>Upgrade rule: better in the role now (+margin), or for under-23s better at potential (+5, higher PA) without being much worse now.</summary>
public static class Upgrade
{
    public const double NowMargin = 3, PotMargin = 5, YoungMaxDrop = 3; public const int YoungAge = 23;

    public static bool Qualifies(PlayerRow p, int ri, double inc, double incPot, int incPa, double margin)
    {
        double now = p.RoleScores[ri], pot = p.PotentialRoleScores[ri];
        bool better = now >= inc + margin; // role score, not CA (specialists can be upgrades with lower CA)
        bool young = p.Age < YoungAge && pot >= incPot + PotMargin && now >= inc - YoungMaxDrop && p.PA > incPa;
        if (better) { p.Gain = now - inc; p.GainText = $"+{now - inc:F0} now"; }
        else if (young) { p.Gain = pot - incPot; p.GainText = $"+{pot - incPot:F0} at potential"; }
        return better || young;
    }
}

/// <summary>Persisted shortlist of player UIDs.</summary>
public static class Shortlist
{
    static string FilePath => Path.Combine(AppPaths.Data, "shortlist.json");
    static HashSet<uint>? _ids;
    static HashSet<uint> Ids => _ids ??= Load();
    static HashSet<uint> Load() { try { return File.Exists(FilePath) ? JsonSerializer.Deserialize<HashSet<uint>>(File.ReadAllText(FilePath)) ?? [] : []; } catch { FMScout.Reader.AtomicFile.MoveAside(FilePath); return []; } }
    public static bool Contains(uint uid) => Ids.Contains(uid);
    public static void Toggle(uint uid) { if (!Ids.Remove(uid)) Ids.Add(uid); FMScout.Reader.AtomicFile.WriteAllText(FilePath, JsonSerializer.Serialize(Ids)); }
    public static int Count => Ids.Count;
    public static IReadOnlyCollection<uint> All => Ids;
    /// <summary>Replaces the whole shortlist with the given players.</summary>
    public static void ReplaceWith(IEnumerable<uint> uids)
    {
        Ids.Clear(); foreach (var u in uids) Ids.Add(u);
        FMScout.Reader.AtomicFile.WriteAllText(FilePath, JsonSerializer.Serialize(Ids));
    }

    /// <summary>Adds the given players; returns how many were new.</summary>
    public static int AddRange(IEnumerable<uint> uids)
    {
        int n = 0; foreach (var u in uids) if (Ids.Add(u)) n++;
        if (n > 0) FMScout.Reader.AtomicFile.WriteAllText(FilePath, JsonSerializer.Serialize(Ids));
        return n;
    }
}







