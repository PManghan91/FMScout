using System.IO;
using System.Text.Json;

namespace FM27Scout.Model;

public sealed class RoleDef
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string[] Positions { get; set; } = [];
    public string[] Key { get; set; } = [];
    public string[] Important { get; set; } = [];
    public string[] Useful { get; set; } = [];

    internal (int idx, double w)[] Weights = [];
    internal int[] PosIdx = [];
    public string Display => Code == "-" ? Name : $"{Name} ({Code})";
    public override string ToString() => Display;
}

public sealed class FamiliarityRule { public int Min { get; set; } public double Factor { get; set; } }
public sealed class AgeRule { public int MaxAge { get; set; } public double Factor { get; set; } }

public sealed class RoleSet
{
    public Dictionary<string, double> Tiers { get; set; } = new() { ["key"] = 3, ["important"] = 2, ["useful"] = 1 };
    public FamiliarityRule[] Familiarity { get; set; } = [];
    public AgeRule[] PotentialAge { get; set; } = [];
    public RoleDef[] Roles { get; set; } = [];
    [System.Text.Json.Serialization.JsonIgnore] public int MergedBuiltIns { get; set; }
    public string? SourcePath { get; private set; }
    /// <summary>Fitted from the save at load time; when set, potential growth follows CA importance per position.</summary>
    [System.Text.Json.Serialization.JsonIgnore] public PotentialModel? Potential { get; set; }

    static readonly JsonSerializerOptions Opts = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    /// <summary>Loads %APPDATA%\FM27Scout\roles.json, creating it from the built-in defaults on first run.</summary>
    public static RoleSet LoadUserOrDefault()
    {
        var path = Path.Combine(AppPaths.Data, "roles.json");
        if (!File.Exists(path)) File.WriteAllText(path, Embedded.Read("roles.json"));
        RoleSet set;
        try { set = JsonSerializer.Deserialize<RoleSet>(File.ReadAllText(path), Opts)!; }
        catch (Exception ex)
        {
            var moved = FMScout.Reader.AtomicFile.MoveAside(path);
            System.Windows.MessageBox.Show($"roles.json could not be read ({ex.Message}).\n\nIt was renamed to {moved} so your edits aren't lost; the built-in defaults are used and a fresh roles.json was created.", "FM27 Scout");
            File.WriteAllText(path, Embedded.Read("roles.json"));
            set = JsonSerializer.Deserialize<RoleSet>(Embedded.Read("roles.json"), Opts)!;
        }
        // built-in roles added in newer versions: merge any the user's file doesn't have yet (their own edits are kept)
        try
        {
            var builtIn = JsonSerializer.Deserialize<RoleSet>(Embedded.Read("roles.json"), Opts)!;
            var have = set.Roles.Select(r => r.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missing = builtIn.Roles.Where(r => !have.Contains(r.Code)).ToArray();
            if (missing.Length > 0) { set.Roles = [.. set.Roles, .. missing]; set.MergedBuiltIns = missing.Length; }
        }
        catch { }
        set.SourcePath = path;
        set.Compile();
        return set;
    }

    void Compile()
    {
        double T(string k) => Tiers.TryGetValue(k, out var v) ? v : 1;
        foreach (var r in Roles)
        {
            var w = new Dictionary<int, double>();
            void AddAll(string[] names, double weight)
            {
                foreach (var n in names) { int i = Attr.Index(n); if (i >= 0) w[i] = Math.Max(w.GetValueOrDefault(i), weight); }
            }
            AddAll(r.Useful, T("useful")); AddAll(r.Important, T("important")); AddAll(r.Key, T("key"));
            r.Weights = w.Select(kv => (kv.Key, kv.Value)).ToArray();
            r.PosIdx = r.Positions.Select(Model.Positions.Index).Where(i => i >= 0).ToArray();
        }
    }

    public double Familiarity01(int rating)
    {
        foreach (var f in Familiarity.OrderByDescending(f => f.Min)) if (rating >= f.Min) return f.Factor;
        return 0.5;
    }

    /// <summary>0-100. attrs = 1-20 scale.</summary>
    public double Score(RoleDef r, int[] attrs, byte[] positions)
    {
        double s = 0, tw = 0;
        foreach (var (i, w) in r.Weights) { s += w * attrs[i]; tw += w; }
        if (tw == 0) return 0;
        int fam = r.PosIdx.Length == 0 ? 20 : r.PosIdx.Max(i => (int)positions[i]);
        return s / tw * 5 * Familiarity01(fam);
    }

    /// <summary>
    /// Fallback potential estimate (used only before the save-fitted PotentialModel exists): every attribute grows
    /// towards 20 in proportion to the remaining headroom ((PA-CA)/(200-CA)), damped by age.
    /// This is our own heuristic - FM does not store "future attributes".
    /// </summary>
    public double AgeFactor(int? age)
    {
        if (age is not int a || PotentialAge.Length == 0) return 1;
        foreach (var r in PotentialAge.OrderBy(r => r.MaxAge)) if (a <= r.MaxAge) return r.Factor;
        return 0;
    }

    public double PotentialScore(RoleDef r, PlayerRow p, double[]? projected)
    {
        if (projected == null) return Score(r, p.A, p.Positions);
        double s = 0, tw = 0;
        foreach (var (i, w) in r.Weights) { s += w * projected[i]; tw += w; }
        int fam = r.PosIdx.Length == 0 ? 20 : r.PosIdx.Max(i => (int)p.Positions[i]);
        return tw == 0 ? 0 : s / tw * 5 * Familiarity01(fam);
    }

    public double Growth(PlayerRow p) => p.PA <= p.CA || p.CA >= 200 ? 0 : (p.PA - p.CA) / (double)(200 - p.CA) * AgeFactor(p.Age);

    public double PotentialScore(RoleDef r, int[] attrs, byte[] positions, int ca, int pa, int? age)
    {
        double growth = pa <= ca || ca >= 200 ? 0 : (pa - ca) / (double)(200 - ca) * AgeFactor(age);
        if (growth <= 0) return Score(r, attrs, positions);
        double s = 0, tw = 0;
        foreach (var (i, w) in r.Weights) { s += w * (attrs[i] + (20 - attrs[i]) * growth); tw += w; }
        int fam = r.PosIdx.Length == 0 ? 20 : r.PosIdx.Max(i => (int)positions[i]);
        return tw == 0 ? 0 : s / tw * 5 * Familiarity01(fam);
    }

    public void Apply(PlayerRow p)
    {
        var sc = new double[Roles.Length];
        var pot = new double[Roles.Length];
        int best = -1;
        double g = Growth(p);
        // expected CA gain = remaining headroom (PA-CA) damped by age; then spread over attributes by the fitted model
        double caGain = p.PA > p.CA ? (p.PA - p.CA) * AgeFactor(p.Age) : 0;
        // development outlook (personality): poor trainers reach less of their PA, good ones a bit more (0.85x - 1.1x)
        if (p.Outlook > 0) caGain = Math.Min(p.PA - p.CA, caGain * Math.Clamp(0.85 + (p.Outlook - 8) * 0.025, 0.85, 1.1));
        double[]? proj = Potential != null && caGain > 0 ? Potential.ProjectToCa(p, caGain) : null;
        for (int i = 0; i < Roles.Length; i++)
        {
            sc[i] = Score(Roles[i], p.A, p.Positions);
            pot[i] = Potential != null ? PotentialScore(Roles[i], p, proj) : PotentialScore(Roles[i], p.A, p.Positions, p.CA, p.PA, p.Age);
            if (best < 0 || sc[i] > sc[best]) best = i;
        }
        p.RoleScores = sc; p.PotentialRoleScores = pot;
        if (best >= 0) { p.BestRole = Roles[best].Name; p.BestRoleScore = sc[best]; p.BestPotentialScore = pot.Max(); }
    }

    public int IndexOf(string codeOrName) => Array.FindIndex(Roles, r =>
        r.Code.Equals(codeOrName, StringComparison.OrdinalIgnoreCase) || r.Name.Equals(codeOrName, StringComparison.OrdinalIgnoreCase));
}







