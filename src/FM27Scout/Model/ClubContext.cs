using FMScout.Reader;

namespace FM27Scout.Model;

/// <summary>
/// Club-dependent evaluation, recomputed when "My club" changes: transfer rules for your country, fee estimate, bargains,
/// value for money (gain over your squad per £), work permit and the Join estimate.
/// </summary>
public static class ClubContext
{
    static readonly HashSet<string> Uk = new(StringComparer.OrdinalIgnoreCase) { "England", "Scotland", "Wales", "Northern Ireland" };
    static readonly HashSet<string> UkIrl = new(StringComparer.OrdinalIgnoreCase) { "England", "Scotland", "Wales", "Northern Ireland", "Republic of Ireland", "Ireland" };

    public static double SquadMedianCa { get; private set; } = 100;
    public static int MyReputation { get; private set; }
    public static string MyClubName { get; private set; } = "";
    public static DateOnly Today { get; private set; } = DateOnly.FromDateTime(DateTime.Today);

    public static void Evaluate(List<PlayerRow> all, MyClub my, DateOnly today, RoleSet roles, JoinModel join)
    {
        Today = today; MyClubName = my.Name; MyReputation = my.Reputation;
        var mine = all.Where(p => p.Club == my.Name).ToList();
        // "squad level" = median CA of the club's top 22 players (the senior squad, not the youth team)
        var cas = mine.Select(p => p.CA).OrderByDescending(x => x).Take(22).OrderBy(x => x).ToList();
        SquadMedianCa = cas.Count == 0 ? 100 : cas[cas.Count / 2];
        var myBest = new double[roles.Roles.Length];
        foreach (var p in mine) for (int i = 0; i < myBest.Length && i < p.RoleScores.Length; i++) myBest[i] = Math.Max(myBest[i], p.RoleScores[i]);
        // baseline per position group too, so a role nobody at the club plays doesn't make everyone look like an upgrade
        var groupBest = new double[PotentialModel.Groups.Length];
        foreach (var p in mine) { int g = PotentialModel.GroupOf(p.Positions); groupBest[g] = Math.Max(groupBest[g], p.BestRoleScore); }
        bool uk = my.Nation != null && Uk.Contains(my.Nation);

        Parallel.ForEach(all, p =>
        {
            ApplyTransferRules(p, my, today, uk);
            // value for money: best role gain over our best in that role, per £1M of fee + 2 years' wages
            int ri = 0; for (int i = 1; i < p.RoleScores.Length; i++) if (p.RoleScores[i] > p.RoleScores[ri]) ri = i;
            double wage = Math.Max(p.Wage, p.TypicalWage);
            double cost = (double.IsNaN(p.Fee) ? double.PositiveInfinity : p.Fee + wage * 104.0) / 1e6;
            double baseline = Math.Max(myBest.Length > ri ? myBest[ri] : 0, groupBest[PotentialModel.GroupOf(p.Positions)]);
            p.GainOverSquad = p.RoleScores.Length == 0 ? 0 : p.RoleScores[ri] - baseline;
            p.Vfm = p.Club == my.Name || p.GainOverSquad <= 0 ? 0 : p.GainOverSquad / Math.Max(0.05, cost);
            int rp = 0; for (int i = 1; i < p.PotentialRoleScores.Length; i++) if (p.PotentialRoleScores[i] > p.PotentialRoleScores[rp]) rp = i;
            double gainPot = p.PotentialRoleScores.Length == 0 ? 0 : p.PotentialRoleScores[rp] - Math.Max(myBest.Length > rp ? myBest[rp] : 0, groupBest[PotentialModel.GroupOf(p.Positions)]);
            p.VfmPot = p.Club == my.Name || gainPot <= 0 ? 0 : gainPot / Math.Max(0.05, cost);
            p.BargainText = !p.CanSign ? ""
                : p.Source.Club == null ? "Free agent"
                : p.PreContract ? "Pre-contract (free)"
                : p.TransferRule.StartsWith("No pre-contract") ? "Free in summer"
                : p.ValueRatio is > 0 and <= 0.5 ? $"{p.ValueRatio:P0} of typical value" : "";
            var wp = WorkPermit.Check(p, my.Nation, my.Name);
            p.WpText = wp.Text; p.WpDetail = wp.Detail; p.WpNeeded = wp.Needed;
            join.Evaluate(p, my, today);
        });
    }

    static void ApplyTransferRules(PlayerRow p, MyClub my, DateOnly today, bool uk)
    {
        p.CanSign = true; p.TransferRule = ""; p.PreContract = false;
        if (p.Club == my.Name) { p.Fee = 0; return; }
        int months = p.ContractExpiry is { } e ? (e.Year - today.Year) * 12 + e.Month - today.Month : 99;
        bool free = p.Source.Club == null;
        var nats = new[] { p.Nation }.Concat(p.Source.OtherNationalities).ToList();
        bool sameCountryClub = p.Source.LeagueNation != null && my.Nation != null &&
            (p.Source.LeagueNation.Equals(my.Nation, StringComparison.OrdinalIgnoreCase) || (uk && Uk.Contains(p.Source.LeagueNation)));

        if (uk)
        {
            // post-Brexit: foreign under-18s can't join a UK club (unless already registered in the UK)
            if (p.Age < 18 && !nats.Any(UkIrl.Contains) && !sameCountryClub) { p.CanSign = false; p.TransferRule = "Can't sign: foreign U18"; }
            // English/UK clubs can't sign another UK club's player on a pre-contract: he's free only once his deal ends
            else if (!free && months <= 6 && sameCountryClub) p.TransferRule = $"No pre-contract (UK club): free from {p.ContractExpiry:d MMM}";
            else if (!free && months <= 6) p.PreContract = true;
            if (p.CanSign && p.Age < 24 && (free || p.TransferRule.StartsWith("No pre-contract")) && p.Source.LinkedClub != null)
                p.TransferRule += (p.TransferRule.Length > 0 ? " · " : "") + "U24: compensation may be due";
        }
        else
        {
            // FIFA: international transfers of minors are restricted (EU/EEA allows 16-17 within the EU)
            if (p.Age < 18 && !sameCountryClub && !(p.Age >= 16 && WorkPermit.IsEu(my.Nation) && nats.Any(WorkPermit.IsEu)))
            { p.CanSign = false; p.TransferRule = "Can't sign: international U18"; }
            else if (!free && months <= 6) p.PreContract = true;
        }
        p.Fee = free || p.PreContract ? 0 : p.NotForSale ? double.NaN : p.Value;
    }
}

