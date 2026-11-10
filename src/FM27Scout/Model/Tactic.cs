using System.IO;
using System.Text.Json;

namespace FM27Scout.Model;

/// <summary>One slot of a formation: FM position code, our role code, pitch coordinates (0-1, attack at the top).</summary>
public sealed class TacticSlot
{
    public string Pos { get; set; } = "";
    public string Role { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
}

public sealed class TacticDef
{
    public string Formation { get; set; } = "4-2-3-1";
    public List<TacticSlot> Slots { get; set; } = [];
    /// <summary>Where it came from: "manual" (picked in FM27 Scout) or "game" (read from FM).</summary>
    public string Source { get; set; } = "manual";
}

public static class Tactics
{
    static string FilePath => Path.Combine(AppPaths.Data, "tactic.json");

    // formation → 11 positions with coordinates (x: 0 left … 1 right, y: 0 attack … 1 own goal)
    // declared before Formations: static fields initialise in order
    static readonly (string, double, double) GK = ("GK", .5, .94), DL = ("DL", .1, .7), DR = ("DR", .9, .7);

    public static readonly Dictionary<string, (string Pos, double X, double Y)[]> Formations = new()
    {
        ["4-2-3-1"] = [GK, DL, ("DC", .3, .83), ("DC", .7, .83), DR, ("DM", .3, .55), ("DM", .7, .55), ("AML", .12, .34), ("AMC", .5, .38), ("AMR", .88, .34), ("ST", .5, .13)],
        ["4-3-3"] = [GK, DL, ("DC", .3, .83), ("DC", .7, .83), DR, ("DM", .5, .62), ("MC", .25, .47), ("MC", .75, .47), ("AML", .12, .25), ("AMR", .88, .25), ("ST", .5, .13)],
        ["4-4-2"] = [GK, DL, ("DC", .3, .83), ("DC", .7, .83), DR, ("ML", .1, .42), ("MC", .35, .55), ("MC", .65, .55), ("MR", .9, .42), ("ST", .3, .15), ("ST", .7, .15)],
        ["4-1-4-1"] = [GK, DL, ("DC", .3, .83), ("DC", .7, .83), DR, ("DM", .5, .62), ("ML", .1, .38), ("MC", .35, .46), ("MC", .65, .46), ("MR", .9, .38), ("ST", .5, .13)],
        ["4-4-1-1"] = [GK, DL, ("DC", .3, .83), ("DC", .7, .83), DR, ("ML", .1, .44), ("MC", .35, .56), ("MC", .65, .56), ("MR", .9, .44), ("AMC", .5, .32), ("ST", .5, .13)],
        ["4-3-1-2"] = [GK, DL, ("DC", .3, .83), ("DC", .7, .83), DR, ("DM", .5, .62), ("MC", .25, .48), ("MC", .75, .48), ("AMC", .5, .33), ("ST", .3, .15), ("ST", .7, .15)],
        ["4-2-2-2"] = [GK, DL, ("DC", .3, .83), ("DC", .7, .83), DR, ("DM", .3, .55), ("DM", .7, .55), ("AML", .15, .36), ("AMR", .85, .36), ("ST", .3, .14), ("ST", .7, .14)],
        ["3-4-3"] = [GK, ("DC", .2, .78), ("DC", .5, .82), ("DC", .8, .78), ("WBL", .1, .56), ("MC", .35, .54), ("MC", .65, .54), ("WBR", .9, .56), ("AML", .15, .27), ("AMR", .85, .27), ("ST", .5, .13)],
        ["3-5-2"] = [GK, ("DC", .2, .78), ("DC", .5, .82), ("DC", .8, .78), ("WBL", .1, .52), ("DM", .5, .62), ("MC", .3, .44), ("MC", .7, .44), ("WBR", .9, .52), ("ST", .3, .15), ("ST", .7, .15)],
        ["3-4-2-1"] = [GK, ("DC", .2, .78), ("DC", .5, .82), ("DC", .8, .78), ("WBL", .1, .55), ("DM", .35, .6), ("DM", .65, .6), ("WBR", .9, .55), ("AMC", .3, .33), ("AMC", .7, .33), ("ST", .5, .13)],
        ["5-3-2"] = [GK, ("WBL", .1, .62), ("DC", .25, .8), ("DC", .5, .84), ("DC", .75, .8), ("WBR", .9, .62), ("MC", .25, .46), ("DM", .5, .6), ("MC", .75, .46), ("ST", .3, .15), ("ST", .7, .15)],
        ["5-2-3"] = [GK, ("WBL", .1, .62), ("DC", .25, .8), ("DC", .5, .84), ("DC", .75, .8), ("WBR", .9, .62), ("MC", .3, .48), ("MC", .7, .48), ("AML", .15, .27), ("AMR", .85, .27), ("ST", .5, .13)],
    };

    /// <summary>Sensible default role for a position (in-possession).</summary>
    public static string DefaultRole(string pos) => pos switch
    {
        "GK" => "GK", "DC" or "SW" => "CB", "DL" or "DR" => "FB", "WBL" or "WBR" => "WB", "DM" => "DM", "MC" => "CM",
        "ML" or "MR" => "WM", "AML" or "AMR" => "IF", "AMC" => "AM", "ST" => "CF", _ => "CM",
    };

    public static TacticDef Build(string formation, TacticDef? keepRolesFrom = null)
    {
        var f = Formations.TryGetValue(formation, out var s) ? s : Formations["4-2-3-1"];
        var t = new TacticDef { Formation = Formations.ContainsKey(formation) ? formation : "4-2-3-1" };
        var used = new HashSet<int>();
        foreach (var (pos, x, y) in f)
        {
            // keep the role the user set for the same position in the previous formation
            string role = DefaultRole(pos);
            if (keepRolesFrom != null)
                for (int i = 0; i < keepRolesFrom.Slots.Count; i++)
                    if (!used.Contains(i) && keepRolesFrom.Slots[i].Pos == pos) { role = keepRolesFrom.Slots[i].Role; used.Add(i); break; }
            t.Slots.Add(new TacticSlot { Pos = pos, Role = role, X = x, Y = y });
        }
        return t;
    }

    public static TacticDef Load()
    {
        try
        {
            if (File.Exists(FilePath) && JsonSerializer.Deserialize<TacticDef>(File.ReadAllText(FilePath)) is { Slots.Count: 11 } t) return t;
        }
        catch { FMScout.Reader.AtomicFile.MoveAside(FilePath); }
        return Build("4-2-3-1");
    }

    public static void Save(TacticDef t) => FMScout.Reader.AtomicFile.WriteAllText(FilePath, JsonSerializer.Serialize(t, new JsonSerializerOptions { WriteIndented = true }));

    /// <summary>
    /// Picks a starter per slot from the squad: scarce slots first (fewest decent candidates), each takes the best
    /// unassigned player for its role. Returns starter index per slot (-1 if nobody).
    /// </summary>
    public static PlayerRow?[] PickXi(TacticDef t, IReadOnlyList<PlayerRow> squad, RoleSet roles)
    {
        var res = new PlayerRow?[t.Slots.Count];
        var ri = t.Slots.Select(s => Math.Max(0, roles.IndexOf(s.Role))).ToArray();
        var order = Enumerable.Range(0, t.Slots.Count)
            .OrderBy(i => squad.Count(p => p.RoleScores.Length > ri[i] && p.RoleScores[ri[i]] >= 55))
            .ThenByDescending(i => squad.Select(p => p.RoleScores.Length > ri[i] ? p.RoleScores[ri[i]] : 0).DefaultIfEmpty(0).Max());
        var used = new HashSet<PlayerRow>();
        foreach (var i in order)
        {
            var best = squad.Where(p => !used.Contains(p) && p.RoleScores.Length > ri[i]).OrderByDescending(p => p.RoleScores[ri[i]]).FirstOrDefault();
            if (best != null) { res[i] = best; used.Add(best); }
        }
        return res;
    }
}
