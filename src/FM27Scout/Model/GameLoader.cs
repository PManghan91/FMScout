using System.Collections.Concurrent;
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
    public int DecodeFailures { get; init; }
    public long Millis { get; init; }
    public string LayoutSource { get; init; } = "";
    public List<string> LayoutWarnings { get; init; } = [];
    public List<string> HumanClubs { get; init; } = [];
    public List<StaffRow> Staff { get; init; } = [];
    public BuildLayout Layout { get; init; } = null!;
}

public sealed class UserFacingException(string msg) : Exception(msg);

public static class Ages
{
    /// <summary>Age in whole years on a date (handles 29 February birthdays).</summary>
    public static int? On(DateOnly? dob, DateOnly today)
    {
        if (dob is not { } d) return null;
        int age = today.Year - d.Year;
        // birthday this year; 29 Feb birthdays count from 1 March in non-leap years (UK rule)
        var birthday = d.Month == 2 && d.Day == 29 && !DateTime.IsLeapYear(today.Year) ? new DateOnly(today.Year, 3, 1) : d.AddYears(age);
        if (today < birthday) age--;
        return age;
    }
}

public static class GameLoader
{
    static Regex _women = new(@"\b(women|ladies|feminin|femenin|frauen|dames)\b", RegexOptions.IgnoreCase);
    public static bool IsWomensName(string? s) => s != null && _women.IsMatch(s);

    public static string UserLayoutPath => Path.Combine(AppPaths.Data, "fm27-layout.json");

    /// <summary>
    /// Layouts: built-in, then fm27-layout.json next to the exe, then %APPDATA%\FM27Scout\fm27-layout.json (Patch Doctor output).
    /// Override files add builds; for a build that also exists built-in, the built-in (verified) one wins over a doctor one.
    /// Problems reading an override file are returned as warnings (shown in the status bar), never silently ignored.
    /// </summary>
    public static (Dictionary<string, BuildLayout> Layouts, string Source, List<string> Warnings) LoadLayouts()
    {
        var all = LayoutFile.Parse(Embedded.Read("fm27-layout.json"));
        var src = "built-in"; var warn = new List<string>();
        foreach (var path in new[] { Path.Combine(AppPaths.ExeDir, "fm27-layout.json"), UserLayoutPath })
        {
            if (!File.Exists(path)) continue;
            try
            {
                foreach (var (k, v) in LayoutFile.Parse(File.ReadAllText(path)))
                {
                    if (all.TryGetValue(k, out var existing) && existing.Origin == "built-in" && v.Origin == "doctor") continue;
                    all[k] = v;
                }
                src += " + " + path;
            }
            catch (Exception ex) { warn.Add($"Couldn't read {path}: {ex.Message}"); }
        }
        return (all, src, warn);
    }

    /// <summary>Newest layout by engine version (e.g. 27.0.0+2493426 &lt; 27.1.0+...), used as Patch Doctor's template.</summary>
    public static BuildLayout NewestTemplate(Dictionary<string, BuildLayout> layouts)
    {
        static (Version v, long b) Key(string k)
        {
            var parts = k.Split('+');
            Version.TryParse(parts[0], out var v);
            long.TryParse(parts.Length > 1 ? parts[1] : "0", out var b);
            return (v ?? new Version(0, 0), b);
        }
        return layouts.OrderByDescending(kv => Key(kv.Key).v).ThenByDescending(kv => Key(kv.Key).b)
            .ThenBy(kv => kv.Value.Origin == "built-in" ? 0 : 1).First().Value;
    }

    /// <summary>Runs Patch Doctor against the running game using the newest known layout as the template.</summary>
    public static PatchDoctor.Result RunPatchDoctor(string build, IProgress<string>? progress)
    {
        var (layouts, _, _) = LoadLayouts();
        var template = NewestTemplate(layouts);
        using var m = ProcessMemory.Attach("fm", template.Module);
        var r = PatchDoctor.Run(m, template, build, progress);
        if (r.Ok && r.Layout != null) PatchDoctor.Save(UserLayoutPath, build, r.Layout, "");
        return r;
    }

    public static uint NewgenUidFrom { get; private set; }

    /// <summary>
    /// Newgens: people the game generated during the save, i.e. UIDs in the block after the real database's IDs (anyone born
    /// 2013+ is always a newgen). Also the club-independent value statistics (value vs typical, typical wage).
    /// </summary>
    static void MarkNewgensAndValues(List<PlayerRow> rows)
    {
        var ids = rows.Select(r => r.Uid).Where(u => u >= 1_000_000_000).OrderBy(u => u).ToList();
        uint firstGenerated = uint.MaxValue; long bestGap = 0;
        for (int i = 1; i < ids.Count; i++)
        {
            long gap = (long)ids[i] - ids[i - 1];
            if (gap > bestGap && ids.Count - i > 1000) { bestGap = gap; firstGenerated = ids[i]; }
        }
        if (bestGap < 200_000) firstGenerated = uint.MaxValue;
        foreach (var r in rows) r.IsRegen = r.Uid >= firstGenerated || r.DateOfBirth?.Year >= 2013;
        NewgenUidFrom = firstGenerated;

        static int AgeBand(int? a) => a switch { null => 0, <= 20 => 1, <= 24 => 2, <= 28 => 3, <= 31 => 4, _ => 5 };
        var median = rows.Where(r => r.Value > 0 && !r.NotForSale).GroupBy(r => (r.CA / 5, AgeBand(r.Age)))
            .ToDictionary(g => g.Key, g => { var v = g.Select(r => r.Value).OrderBy(x => x).ToList(); return (double)v[v.Count / 2]; });
        var medWage = rows.Where(r => r.Wage > 0).GroupBy(r => r.CA / 5)
            .ToDictionary(g => g.Key, g => { var v = g.Select(r => r.Wage).OrderBy(x => x).ToList(); return (double)v[v.Count / 2]; });
        foreach (var r in rows)
        {
            r.ValueRatio = r.Value > 0 && !r.NotForSale && median.TryGetValue((r.CA / 5, AgeBand(r.Age)), out var med) && med > 0 ? r.Value / med : 0;
            r.TypicalWage = medWage.GetValueOrDefault(r.CA / 5);
        }
    }

    /// <summary>Second pass after a load: finds agreed future transfers (two memory scans, ~20-30 s). Returns player address → move.</summary>
    public static Dictionary<ulong, ArrangedTransfer> LoadArranged(IReadOnlyCollection<PlayerRow> rows, CancellationToken ct)
    {
        var (layouts, _, _) = LoadLayouts();
        using var game = FmGame.Attach(layouts);
        var today = game.GameDate ?? DateOnly.FromDateTime(DateTime.Today);
        return game.ArrangedTransfers(rows.Select(r => r.Source.Address).ToHashSet(), today, ct);
    }

    /// <summary>Reads the human manager(s)' in-game shortlist(s): club name → player UIDs.</summary>
    public static Dictionary<string, List<uint>> ReadFmShortlists()
    {
        var (layouts, _, _) = LoadLayouts();
        using var game = FmGame.Attach(layouts);
        return game.HumanShortlists();
    }

    public static LoadResult Load(RoleSet roles, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        try { return LoadCore(roles, progress, ct); }
        catch (NoSaveLoadedException)
        {
            throw new UserFacingException("FM27 is running, but no save is loaded yet (or it's still loading).\n\nLoad your save in FM27, wait until you're in the game, then press Load from game.");
        }
        catch (AttachException ax)
        {
            throw new UserFacingException(ax.Problem switch
            {
                AttachProblem.NotRunning => "Football Manager 27 isn't running.\n\nStart FM27, load your save, then press Load from game.",
                AttachProblem.Starting => "FM27 is still starting up.\n\nWait until it has loaded, open your save, then press Load from game.",
                AttachProblem.AccessDenied => "Windows refused access to FM27's memory.\n\nThis happens if FM27 runs as administrator: run FM27 Scout as administrator too (right-click › Run as administrator).",
                _ => "Couldn't connect to FM27: " + ax.Message,
            });
        }
    }

    static LoadResult LoadCore(RoleSet roles, IProgress<string>? progress, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var (layouts, src, warnings) = LoadLayouts();
        using var game = FmGame.Attach(layouts);
        var L = game.Layout;
        if (!string.IsNullOrEmpty(L.WomensNamePattern)) _women = new Regex(L.WomensNamePattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        var today = game.GameDate ?? DateOnly.FromDateTime(DateTime.Today);
        var attrIx = L.AttributeNames.Select((n, i) => (n, i)).ToDictionary(t => t.n, t => t.i, StringComparer.OrdinalIgnoreCase);
        int detIx = attrIx.GetValueOrDefault("Determination", 51);

        progress?.Report("Reading the player list…");
        var persons = game.PersonPointers();
        var humans = game.HumanClubs().Select(c => c.Name).ToList();
        var ptrs = persons.Where(game.IsPlayer).ToArray();
        long failsBefore = game.Memory.FailedReads;

        // players: reads are independent, so run them in parallel
        var bag = new ConcurrentBag<PlayerRow>();
        int women = 0, failed = 0, done = 0;
        Parallel.ForEach(ptrs, new ParallelOptions { MaxDegreeOfParallelism = Math.Min(8, Environment.ProcessorCount), CancellationToken = ct }, v =>
        {
            if (Interlocked.Increment(ref done) % 25000 == 0) progress?.Report($"Reading players… {done:N0}/{ptrs.Length:N0}");
            Player p;
            try { p = game.ReadPlayer(v); } catch { Interlocked.Increment(ref failed); return; }
            if (p.IsFemale || IsWomensName(p.Competition) || IsWomensName(p.Club)) { Interlocked.Increment(ref women); return; }
            if (p.CurrentAbility is <= 0 or > 200) { Interlocked.Increment(ref failed); return; } // stale / not a real record
            var a = p.Attributes.Select(PlayerRow.ToDisplay).ToArray();
            var row = new PlayerRow
            {
                Source = p, A = a,
                Age = Ages.On(p.DateOfBirth, today),
                PositionsText = Positions.Describe(p.Positions),
                SquadStatusName = p.Club == null ? "" : L.SquadStatusNames.TryGetValue(p.SquadStatus.ToString(), out var sn) ? sn : $"Status {p.SquadStatus}",
            };
            int det = a[detIx];
            row.PersonalityLabel = Personality.Label(p.Personality, det);
            row.Outlook = Personality.Outlook(p.Personality, det);
            bag.Add(row);
        });
        if (game.Memory.HasExited) throw new UserFacingException("FM27 closed while FM27 Scout was reading it. Start FM27 again and press Load from game.");
        if (ptrs.Length > 0 && failed > ptrs.Length * 0.05)
            throw new UserFacingException($"Too many players couldn't be read ({failed:N0} of {ptrs.Length:N0}). FM27 may still be loading or processing a day; try again in a moment.\n\nIf this keeps happening after an FM update, the layout may need Patch Doctor.");
        var rows = bag.OrderBy(r => r.Uid).ToList();

        progress?.Report("Reading staff…");
        var staffBag = new ConcurrentBag<StaffRow>();
        Parallel.ForEach(persons.Where(game.IsStaff), new ParallelOptions { MaxDegreeOfParallelism = Math.Min(8, Environment.ProcessorCount), CancellationToken = ct }, v =>
        {
            StaffInfo s; try { s = game.ReadStaff(v); } catch { Interlocked.Increment(ref failed); return; }
            if (IsWomensName(s.Club) || IsWomensName(s.Competition)) return; // men's game only (staff themselves may be any gender)
            staffBag.Add(new StaffRow
            {
                Source = s,
                Job = s.Club == null && s.JobCode == 0 ? "" : L.JobNames.TryGetValue(s.JobCode.ToString(), out var jn) ? jn : $"Job {s.JobCode}",
                Age = Ages.On(s.DateOfBirth, today),
                Personality = Personality.Label(s.Personality, null),
            });
        });

        progress?.Report("Fitting the potential model to this save…");
        roles.Potential = PotentialModel.Fit(rows);
        foreach (var r in rows) roles.Apply(r);
        MarkNewgensAndValues(rows);
        return new LoadResult
        {
            Players = rows, Build = game.BuildKey, GameDate = game.GameDate, PeopleInDb = persons.Length, ExcludedWomen = women,
            DecodeFailures = failed, Millis = sw.ElapsedMilliseconds, LayoutSource = src, LayoutWarnings = warnings,
            HumanClubs = humans, Staff = staffBag.OrderBy(s => s.Name).ToList(), Layout = L,
        };
    }
}

