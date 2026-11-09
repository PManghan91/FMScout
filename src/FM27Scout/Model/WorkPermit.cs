namespace FM27Scout.Model;

/// <summary>
/// Work-permit check for the selected club's country. Nationalities (main + other passports) come from FM's own data
/// (person relationship list). The rules are ours: FM decides permits itself when a transfer is agreed, so "Needed" cases are
/// an estimate of England's points-based (GBE) system using reputation, current league strength and ability.
/// </summary>
public static class WorkPermit
{
    static readonly HashSet<string> Uk = new(StringComparer.OrdinalIgnoreCase) { "England", "Scotland", "Wales", "Northern Ireland" };
    static readonly HashSet<string> UkFree = new(StringComparer.OrdinalIgnoreCase) { "England", "Scotland", "Wales", "Northern Ireland", "Republic of Ireland", "Ireland" };
    static readonly HashSet<string> Eu = new(StringComparer.OrdinalIgnoreCase)
    {
        "Austria", "Belgium", "Bulgaria", "Croatia", "Cyprus", "Czech Republic", "Czechia", "Denmark", "Estonia", "Finland", "France", "Germany",
        "Greece", "Hungary", "Republic of Ireland", "Ireland", "Italy", "Latvia", "Lithuania", "Luxembourg", "Malta", "The Netherlands", "Netherlands",
        "Poland", "Portugal", "Romania", "Slovakia", "Slovenia", "Spain", "Sweden"
    };
    // leagues whose players usually qualify for a GBE (band 1-2 style), by FM competition name
    static readonly HashSet<string> TopLeagues = new(StringComparer.OrdinalIgnoreCase)
        { "Premier League", "LaLiga", "Bundesliga", "Serie A", "Ligue 1" };
    static readonly HashSet<string> GoodLeagues = new(StringComparer.OrdinalIgnoreCase)
        { "Eredivisie", "Liga Portugal", "Jupiler Pro League", "Pro League", "EFL Championship", "Brasileirão", "Liga Profesional", "MLS", "Super Lig",
          "Trendyol Süper Lig", "Scottish Premiership", "cinch Premiership", "Super League", "Superliga", "Eliteserien", "Allsvenskan", "Liga MX", "LaLiga 2", "2. Bundesliga", "Serie B", "Ligue 2" };

    public sealed record Result(string Text, string Detail, bool Needed);

    public static Result Check(PlayerRow p, string? clubNation, string myClub)
    {
        if (p.Club == myClub) return new("", "Already at your club", false);
        var nats = new[] { p.Nation }.Concat(p.Source.OtherNationalities).Where(n => !string.IsNullOrEmpty(n)).Distinct().ToList();
        string list = string.Join(", ", nats);
        if (string.IsNullOrEmpty(clubNation)) return new("?", "Your club's country is unknown", false);

        if (nats.Any(n => n.Equals(clubNation, StringComparison.OrdinalIgnoreCase)))
            return new("No", $"Nationality: {list}", false);
        if (Uk.Contains(clubNation))
        {
            if (nats.Any(UkFree.Contains)) return new("No", $"British/Irish passport ({list})", false);
            return Gbe(p, list);
        }
        if (Eu.Contains(clubNation) && nats.Any(Eu.Contains)) return new("No (EU)", $"EU passport ({list})", false);
        return new("Non-EU", $"No passport for {clubNation} or the EU ({list}). Rules vary by country (often a non-EU quota).", true);
    }

    /// <summary>Rough GBE-style estimate for England (our approximation, not FM's calculation).</summary>
    static Result Gbe(PlayerRow p, string list)
    {
        double pts = 0;
        pts += p.WorldRep / 250.0;                                    // international standing proxy (0-40)
        pts += TopLeagues.Contains(p.League) ? 25 : GoodLeagues.Contains(p.League) ? 12 : 0; // current league strength
        pts += Math.Max(0, (p.CA - 100) / 4.0);                        // ability
        if (p.Age <= 21 && p.PA >= 150) pts += 8;                      // youth / high-potential concession
        string verdict = pts >= 45 ? "Needed – likely OK" : pts >= 32 ? "Needed – borderline" : "Needed – unlikely";
        return new(verdict, $"No British/Irish passport ({list}). Estimated GBE points {pts:F0} (world rep {p.WorldRep:N0}, league {p.League}, CA {p.CA}). Estimate only.", true);
    }
}
