using System.IO;
using System.Text.Json;

namespace FM27Scout.Model;

/// <summary>The club the user manages, plus squad figures used as an affordability proxy.</summary>
public sealed record MyClub(string Name, int Reputation, long TopWage, long SquadValue, int Players)
{
    public override string ToString() => Name;
}

public sealed class JoinModel
{
    public sealed class Curve { public double PointsPer { get; set; } = 40; public double MaxPenalty { get; set; } = 60; public double BonusPer { get; set; } = 100; public double MaxBonus { get; set; } = 15; }
    public sealed class AgeRule { public int MinAge { get; set; } public double Factor { get; set; } = 1; }
    public sealed class ContractRules { public double FreeAgent { get; set; } = 20; public double Within6Months { get; set; } = 12; public double Within12Months { get; set; } = 6; }
    public sealed class WageRules { public double OverTopEarnerFactor { get; set; } = 1.3; public double OverTopEarnerPoints { get; set; } = -15; public double WayOverFactor { get; set; } = 2; public double WayOverPoints { get; set; } = -40; public double WithinTopEarnerPoints { get; set; } = 4; }
    public sealed class PersonalityRules { public double PerAmbitionPoint { get; set; } = 0.05; public double LoyaltyPointsPerPoint { get; set; } = -1.5; }
    public sealed class FeeRules { public double OverSquadValueShare { get; set; } = 0.25; public double Points { get; set; } = -20; }
    public sealed class Label { public double Min { get; set; } public string Text { get; set; } = ""; public string LabelName { get => Text; set => Text = value; } }

    public double Base { get; set; } = 55;
    public Curve PlayerReputation { get; set; } = new();
    public Curve ClubStep { get; set; } = new();
    public AgeRule[] AgePenaltyFactor { get; set; } = [];
    public ContractRules Contract { get; set; } = new();
    public Dictionary<string, double> SquadStatus { get; set; } = new();
    public WageRules Wage { get; set; } = new();
    public FeeRules Fee { get; set; } = new();
    public PersonalityRules Personality { get; set; } = new();
    public JsonElement[] Labels { get; set; } = [];
    public string? SourcePath { get; private set; }
    List<(double min, string text)> _labels = [];

    static readonly JsonSerializerOptions Opts = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public static JoinModel LoadUserOrDefault()
    {
        var path = Path.Combine(AppPaths.Data, "join.json");
        if (!File.Exists(path)) File.WriteAllText(path, Embedded.Read("join.json"));
        JoinModel m;
        try { m = JsonSerializer.Deserialize<JoinModel>(File.ReadAllText(path), Opts)!; }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"join.json could not be read ({ex.Message}). Using built-in defaults.", "FM27 Scout");
            m = JsonSerializer.Deserialize<JoinModel>(Embedded.Read("join.json"), Opts)!;
        }
        m.SourcePath = path;
        m._labels = m.Labels.Select(l => (l.GetProperty("min").GetDouble(), l.GetProperty("label").GetString() ?? "")).OrderByDescending(t => t.Item1).ToList();
        if (m._labels.Count == 0) m._labels = [(70, "Likely"), (50, "Possible"), (30, "Unlikely"), (0, "Very unlikely")];
        return m;
    }

    public string LabelFor(double score) => _labels.FirstOrDefault(l => score >= l.min).text ?? "";

    static double Gap(Curve c, double gap)
        => gap > 0 ? -Math.Min(c.MaxPenalty, gap / c.PointsPer) : Math.Min(c.MaxBonus, -gap / c.BonusPer);

    public void Evaluate(PlayerRow p, MyClub my, DateOnly today)
    {
        var reasons = new List<string>();
        if (p.Club == my.Name)
        {
            p.Join = -1; p.JoinLabel = "Our player"; p.JoinReasons = ["Already at your club."];
            return;
        }
        double s = Base;
        double ageF = AgePenaltyFactor.OrderByDescending(a => a.MinAge).FirstOrDefault(a => (p.Age ?? 0) >= a.MinAge)?.Factor ?? 1;
        double ambF = 1 + (p.Ambition - 10) * Personality.PerAmbitionPoint;
        ageF *= Math.Max(0.5, ambF);

        // reputation of the player vs our club
        double rg = p.CurrentRep - my.Reputation;
        double rp = Gap(PlayerReputation, rg); if (rp < 0) rp *= ageF;
        s += rp;
        reasons.Add(rg > 0
            ? $"{Pts(rp)} Reputation {p.CurrentRep:N0} vs your club {my.Reputation:N0} (×{ageF:0.##} for age {p.Age}, ambition {p.Ambition})"
            : $"{Pts(rp)} Your club's reputation ({my.Reputation:N0}) is at or above his ({p.CurrentRep:N0})");

        // stepping down from his current club
        if (p.Source.Club != null && p.ClubRep > 0)
        {
            double cg = p.ClubRep - my.Reputation;
            double cp = Gap(ClubStep, cg); if (cp < 0) cp *= ageF;
            s += cp;
            reasons.Add(cg > 0 ? $"{Pts(cp)} Step down from {p.Club} (club rep {p.ClubRep:N0})" : $"{Pts(cp)} Step up from {p.Club} (club rep {p.ClubRep:N0})");
        }

        // loyalty to his current club
        if (p.Source.Club != null && p.Loyalty > 10)
        {
            double lp = (p.Loyalty - 10) * Personality.LoyaltyPointsPerPoint;
            s += lp; reasons.Add($"{Pts(lp)} Loyal to his club (loyalty {p.Loyalty})");
        }

        // contract
        if (p.Source.Club == null) { s += Contract.FreeAgent; reasons.Add($"{Pts(Contract.FreeAgent)} Free agent"); }
        else if (p.ContractExpiry is { } exp)
        {
            int months = (exp.Year - today.Year) * 12 + exp.Month - today.Month;
            if (months <= 6) { s += Contract.Within6Months; reasons.Add($"{Pts(Contract.Within6Months)} Contract expires {exp:MMM yyyy} (≤ 6 months)"); }
            else if (months <= 12) { s += Contract.Within12Months; reasons.Add($"{Pts(Contract.Within12Months)} Contract expires {exp:MMM yyyy} (≤ 12 months)"); }
        }

        // squad status
        if (SquadStatus.TryGetValue(p.Source.SquadStatus.ToString(), out var sp) && sp != 0)
        {
            s += sp; reasons.Add($"{Pts(sp)} Squad status: {p.SquadStatusName}");
        }

        // affordability proxies
        if (my.TopWage > 0 && p.Wage > 0)
        {
            double ratio = p.Wage / (double)my.TopWage;
            if (ratio > Wage.WayOverFactor) { s += Wage.WayOverPoints; reasons.Add($"{Pts(Wage.WayOverPoints)} Earns {MoneyText.Format(p.Wage)} p/w, {ratio:0.#}× your top earner"); }
            else if (ratio > Wage.OverTopEarnerFactor) { s += Wage.OverTopEarnerPoints; reasons.Add($"{Pts(Wage.OverTopEarnerPoints)} Earns {MoneyText.Format(p.Wage)} p/w, more than your top earner ({MoneyText.Format(my.TopWage)})"); }
            else if (ratio <= 1) { s += Wage.WithinTopEarnerPoints; reasons.Add($"{Pts(Wage.WithinTopEarnerPoints)} Wage {MoneyText.Format(p.Wage)} p/w fits your structure"); }
        }
        bool cheapExit = p.Source.Club == null || (p.ContractExpiry is { } e2 && (e2.Year - today.Year) * 12 + e2.Month - today.Month <= 6);
        if (!cheapExit && my.SquadValue > 0 && p.Value > my.SquadValue * Fee.OverSquadValueShare)
        {
            s += Fee.Points; reasons.Add($"{Pts(Fee.Points)} Value {MoneyText.Format(p.Value)} is large vs your squad value {MoneyText.Format(my.SquadValue)}");
        }

        reasons.Add("Not included: favourite/disliked clubs, real budgets, work permits (not decoded yet).");
        p.Join = Math.Clamp(s, 0, 100);
        p.JoinLabel = LabelFor(p.Join);
        p.JoinReasons = reasons;
    }

    static string Pts(double v) => v >= 0 ? $"+{v:0}" : $"{v:0}";
}

