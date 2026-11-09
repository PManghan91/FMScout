using System.Diagnostics;
using FMScout.Reader;

namespace FMScout.Probe;

/// <summary>The real probe: uses only FmGame + fm27-layout.json (no scanning).</summary>
static class Report
{
    public static string FindLayout()
    {
        foreach (var dir in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
            for (var d = new DirectoryInfo(dir); d != null; d = d.Parent)
            {
                var f = Path.Combine(d.FullName, "fm27-layout.json");
                if (File.Exists(f)) return f;
            }
        throw new FileNotFoundException("fm27-layout.json not found");
    }

    public static void Run(string[] names)
    {
        using var g = FmGame.Attach(FindLayout());
        var fmExe = g.Memory.Process.MainModule?.FileVersionInfo;
        Console.WriteLine($"FM process: pid {g.Memory.Process.Id}, fm.exe {fmExe?.FileVersion} (Unity {fmExe?.ProductVersion})");
        Console.WriteLine($"Engine build: {g.BuildKey} ({g.Layout.Module} @ 0x{g.ModuleBase:X})");
        var today = g.GameDate;
        Console.WriteLine($"In-game date: {today:yyyy-MM-dd}");

        var sw = Stopwatch.StartNew();
        var persons = g.PersonPointers();
        var players = persons.Where(g.IsPlayer).ToArray();
        Console.WriteLine($"People in DB: {persons.Length:N0}; players: {players.Length:N0} (enumerated in {sw.ElapsedMilliseconds} ms)");

        sw.Restart();
        var all = players.Select(g.ReadPlayer).ToList();
        Console.WriteLine($"Read all {all.Count:N0} players in {sw.ElapsedMilliseconds} ms");
        var ca = all.Where(p => p.CurrentAbility is > 0 and <= 200).ToList();
        Console.WriteLine($"Sanity: {ca.Count * 100.0 / all.Count:F1}% have CA in 1-200; PA>=CA for {all.Count(p => p.PotentialAbility >= p.CurrentAbility) * 100.0 / all.Count:F1}%; " +
                          $"heights {all.Where(p => p.HeightCm > 0).Min(p => p.HeightCm)}-{all.Max(p => p.HeightCm)} cm");
        Console.WriteLine();

        foreach (var n in names)
        {
            var hits = all.Where(p => (p.Name + " " + p.FullName).Contains(n, StringComparison.OrdinalIgnoreCase))
                          .OrderByDescending(p => p.CurrentAbility).Take(1).ToList();
            if (hits.Count == 0) { Console.WriteLine($"## {n}: not found among players\n"); continue; }
            Print(g, hits[0], today);
        }
    }

    static void Print(FmGame g, Player p, DateOnly? today)
    {
        int? age = p.DateOfBirth is { } d && today is { } t ? t.Year - d.Year - (t.DayOfYear < d.DayOfYear ? 1 : 0) : null;
        Console.WriteLine($"## {p.Name}{(p.FullName != null ? $" ({p.FullName})" : "")}   uid {p.Uid}   @0x{p.Address:X}");
        Console.WriteLine($"   DOB {p.DateOfBirth:yyyy-MM-dd} (age {age})   {p.Nationality}   club: {p.Club ?? "-"} ({p.Competition})   [previous club @+0x108: {p.LinkedClub ?? "-"}]");
        Console.WriteLine($"   wage £{p.WeeklyWage:N0} p/w   contract to {p.ContractExpiry:dd/MM/yyyy}   value £{p.Value:N0}");
        Console.WriteLine($"   CA {p.CurrentAbility}   PA {p.PotentialAbility}   height {p.HeightCm} cm   weight: (unknown)");
        var pos = g.Layout.PositionNames;
        Console.WriteLine("   positions: " + string.Join(" ", p.Positions.Select((v, i) => $"{pos[i]}={v}")));
        Console.WriteLine("   personality (1-20): " + string.Join(" ", p.Personality.Select(v => v.ToString())));
        Console.WriteLine("   raw: " + string.Join(",", p.Attributes));
        Console.WriteLine("   attributes (raw 1-100 | /5 = UI 1-20):");
        var an = g.Layout.AttributeNames;
        for (int i = 0; i < p.Attributes.Length; i += 3)
            Console.WriteLine("     " + string.Join("  ", Enumerable.Range(i, Math.Min(3, p.Attributes.Length - i))
                .Select(j => $"{an[j],-22}{p.Attributes[j],3} ({(p.Attributes[j] + 2) / 5,2})")));
        Console.WriteLine();
    }
}





