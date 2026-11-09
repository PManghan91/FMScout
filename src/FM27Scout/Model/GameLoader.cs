using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using FMScout.Reader;

namespace FM27Scout.Model;

public sealed class LoadResult
{
    public List<PlayerRow> Players { get; init; } = [];
    public string Build { get; init; } = "";
    public DateOnly? GameDate { get; init; }
    public int PeopleInDb { get; init; }
    public int ExcludedWomen { get; init; }
    public long Millis { get; init; }
    public string LayoutSource { get; init; } = "";
    public List<string> HumanClubs { get; init; } = [];
    public List<StaffRow> Staff { get; init; } = [];
}

public sealed class UserFacingException(string msg) : Exception(msg);
public sealed class UnknownBuildException(string build) : Exception($"Unknown FM build {build}") { public string Build { get; } = build; }

public static class GameLoader
{
    // Women's competitions/clubs are excluded in addition to the person's gender flag.
    static readonly Regex WomenWords = new(@"\b(women|women's|womens|ladies|feminin|feminino|femenino|femenina|femení|féminin|féminine|frauen|dames|damallsvenskan|toppserien|kvinde|naisten|wsl|nwsl|liga f)\b|\bfem\b|\(w\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool IsWomensName(string? s) => s != null && WomenWords.IsMatch(s);

    public static string UserLayoutPath => Path.Combine(AppPaths.Data, "fm27-layout.json");

    /// <summary>
    /// Layouts: built-in, then fm27-layout.json next to the exe, then %APPDATA%\FM27Scout\fm27-layout.json (written by
    /// Patch Doctor). Later files add builds or override the same build.
    /// </summary>
    public static (Dictionary<string, BuildLayout> Layouts, string Source) LoadLayouts()
    {
        var all = LayoutFile.Parse(Embedded.Read("fm27-layout.json"));
        var src = "built-in";
        foreach (var path in new[] { Path.Combine(AppPaths.ExeDir, "fm27-layout.json"), UserLayoutPath })
        {
            if (!File.Exists(path)) continue;
            try { foreach (var (k, v) in LayoutFile.Parse(File.ReadAllText(path))) all[k] = v; src += " + " + path; }
            catch { /* ignore a broken override file */ }
        }
        return (all, src);
    }

    /// <summary>Runs Patch Doctor against the running game using the newest known layout as the template.</summary>
    public static PatchDoctor.Result RunPatchDoctor(string build, IProgress<string>? progress)
    {
        var (layouts, _) = LoadLayouts();
        var template = layouts.Values.Last();
        using var m = ProcessMemory.Attach("fm");
        var r = PatchDoctor.Run(m, template, build, progress);
        if (r.Ok && r.Layout != null) PatchDoctor.Save(UserLayoutPath, build, r.Layout, Embedded.Read("fm27-layout.json"));
        return r;
    }

    /// <summary>
    /// Newgens: people the game generated during the save, i.e. UIDs in the block after the real database's IDs.
    /// Bargains: value well below the median for players of similar CA and age, or obtainable without a fee.
    /// </summary>
    public static uint NewgenUidFrom { get; private set; }

    static void MarkRegensAndBargains(List<PlayerRow> rows, DateOnly today)
    {
        // UIDs: the real database occupies one block; everyone the game creates during the save (youth intakes, filler
        // players) gets UIDs from a new block. In BRR: real <= ~2,000.75M, generated >= 2,002M. Find the largest gap.
        var ids = rows.Select(r => r.Uid).Where(u => u >= 1_000_000_000).OrderBy(u => u).ToList();
        uint firstGenerated = uint.MaxValue; long bestGap = 0;
        for (int i = 1; i < ids.Count; i++)
        {
            long gap = (long)ids[i] - ids[i - 1];
            if (gap > bestGap && ids.Count - i > 1000) { bestGap = gap; firstGenerated = ids[i]; }
        }
        if (bestGap < 200_000) firstGenerated = uint.MaxValue; // no clear block: fall back to birth year only
        foreach (var r in rows) r.IsRegen = r.Uid >= firstGenerated || r.DateOfBirth?.Year >= 2013;
        NewgenUidFrom = firstGenerated;
        static int AgeBand(int? a) => a switch { null => 0, <= 20 => 1, <= 24 => 2, <= 28 => 3, <= 31 => 4, _ => 5 };
        var median = rows.Where(r => r.Value > 0).GroupBy(r => (r.CA / 5, AgeBand(r.Age)))
            .ToDictionary(g => g.Key, g => { var v = g.Select(r => r.Value).OrderBy(x => x).ToList(); return (double)v[v.Count / 2]; });
        var medWage = rows.Where(r => r.Wage > 0).GroupBy(r => r.CA / 5)
            .ToDictionary(g => g.Key, g => { var v = g.Select(r => r.Wage).OrderBy(x => x).ToList(); return (double)v[v.Count / 2]; });
        foreach (var r in rows)
        {
            r.ValueRatio = r.Value > 0 && median.TryGetValue((r.CA / 5, AgeBand(r.Age)), out var med) && med > 0 ? r.Value / med : 0;
            int months = r.ContractExpiry is { } e ? (e.Year - today.Year) * 12 + e.Month - today.Month : 99;
            double fee = r.Source.Club == null || months <= 6 ? 0 : r.Value;
            double wage = Math.Max(r.Wage, medWage.GetValueOrDefault(r.CA / 5)); // free agents: assume a typical wage for their level
            double cost = (fee + wage * 104.0) / 1e6;
            r.Vfm = r.BestPotentialScore / Math.Max(0.05, cost);
            r.BargainText = r.Source.Club == null ? "Free agent"
                : months <= 6 ? "Contract ending"
                : r.ValueRatio is > 0 and <= 0.5 ? $"{r.ValueRatio:P0} of typical value"
                : "";
        }
    }

    public static LoadResult Load(RoleSet roles, IProgress<string>? progress = null)
    {
        var sw = Stopwatch.StartNew();
        var (layouts, src) = LoadLayouts();
        FmGame game;
        try { game = FmGame.Attach(layouts); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not running"))
        {
            throw new UserFacingException("Football Manager 27 isn't running.\n\nStart FM27, load your save, then press Load from game.");
        }
        catch (NotSupportedException)
        {
            var p = System.Diagnostics.Process.GetProcessesByName("fm").FirstOrDefault();
            throw new UnknownBuildException(p == null ? "?" : FmGame.DetectBuild(p, layouts.Values.First().Module) ?? "?");
        }

        using (game)
        {
            Attr.Init(game.Layout.AttributeNames);
            Positions.Names = game.Layout.PositionNames;
            var today = game.GameDate ?? DateOnly.FromDateTime(DateTime.Today);

            progress?.Report("Reading the player list…");
            var humans = game.HumanClubs().Select(c => c.Name).ToList();
            var persons = game.PersonPointers();
            var ptrs = persons.Where(game.IsPlayer).ToArray();

            var rows = new List<PlayerRow>(ptrs.Length);
            int women = 0, n = 0;
            foreach (var v in ptrs)
            {
                if (++n % 20000 == 0) progress?.Report($"Reading players… {n:N0}/{ptrs.Length:N0}");
                Player p;
                try { p = game.ReadPlayer(v); } catch { continue; }
                if (p.IsFemale || IsWomensName(p.Competition) || IsWomensName(p.Club)) { women++; continue; }
                if (p.CurrentAbility is <= 0 or > 200) continue; // stale / not a real record
                var a = p.Attributes.Select(PlayerRow.ToDisplay).ToArray();
                var row = new PlayerRow
                {
                    Source = p,
                    A = a,
                    Age = p.DateOfBirth is { } d ? today.Year - d.Year - (today.DayOfYear < d.DayOfYear ? 1 : 0) : null,
                    PositionsText = Positions.Describe(p.Positions),
                    SquadStatusName = p.Club == null ? "" : game.Layout.SquadStatusNames.TryGetValue(p.SquadStatus.ToString(), out var sn) ? sn : $"Status {p.SquadStatus}",
                };
                int det = a[Math.Max(0, Attr.Index("Determination"))];
                row.PersonalityLabel = Personality.Label(p.Personality, det);
                row.Outlook = Personality.Outlook(p.Personality, det);
                rows.Add(row);
            }
            progress?.Report("Reading staff…");
            var staff = new List<StaffRow>();
            foreach (var v in persons)
            {
                if (!game.IsStaff(v)) continue;
                StaffInfo s; try { s = game.ReadStaff(v); } catch { continue; }
                if (IsWomensName(s.Club) || IsWomensName(s.Competition)) continue; // men's game only (staff themselves may be any gender)
                staff.Add(new StaffRow
                {
                    Source = s,
                    Job = s.Club == null && s.JobCode == 0 ? "" : game.Layout.JobNames.TryGetValue(s.JobCode.ToString(), out var jn) ? jn : $"Job {s.JobCode}",
                    Age = s.DateOfBirth is { } d ? today.Year - d.Year - (today.DayOfYear < d.DayOfYear ? 1 : 0) : null,
                    Personality = Personality.Label(s.Personality, null),
                });
            }
            progress?.Report("Fitting the potential model to this save…");
            roles.Potential = PotentialModel.Fit(rows);
            foreach (var r in rows) roles.Apply(r);
            MarkRegensAndBargains(rows, today);
            return new LoadResult
            {
                Players = rows, Build = game.BuildKey, GameDate = game.GameDate, PeopleInDb = persons.Length,
                ExcludedWomen = women, Millis = sw.ElapsedMilliseconds, LayoutSource = src, HumanClubs = humans, Staff = staff
            };
        }
    }
}













