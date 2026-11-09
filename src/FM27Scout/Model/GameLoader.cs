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
}

public sealed class UserFacingException(string msg) : Exception(msg);

public static class GameLoader
{
    // Women's competitions/clubs are excluded in addition to the person's gender flag.
    static readonly Regex WomenWords = new(@"\b(women|women's|womens|ladies|feminin|feminino|femenino|femenina|femení|féminin|féminine|frauen|dames|damallsvenskan|toppserien|kvinde|naisten|wsl|nwsl|liga f)\b|\bfem\b|\(w\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool IsWomensName(string? s) => s != null && WomenWords.IsMatch(s);

    /// <summary>Layout: fm27-layout.json next to the exe overrides the built-in copy (quick patching).</summary>
    public static (Dictionary<string, BuildLayout> Layouts, string Source) LoadLayouts()
    {
        var local = Path.Combine(AppPaths.ExeDir, "fm27-layout.json");
        if (File.Exists(local)) return (LayoutFile.Parse(File.ReadAllText(local)), local);
        return (LayoutFile.Parse(Embedded.Read("fm27-layout.json")), "built-in");
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
        catch (NotSupportedException ex)
        {
            throw new UserFacingException(
                "This FM27 build isn't supported yet (the game was probably patched).\n\n" + ex.Message +
                "\n\nTo fix: put an updated fm27-layout.json next to FM27Scout.exe. The app reads it in place of its built-in layout.");
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
                roles.Apply(row);
                rows.Add(row);
            }
            return new LoadResult
            {
                Players = rows, Build = game.BuildKey, GameDate = game.GameDate, PeopleInDb = persons.Length,
                ExcludedWomen = women, Millis = sw.ElapsedMilliseconds, LayoutSource = src, HumanClubs = humans
            };
        }
    }
}

