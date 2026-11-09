using FMScout.Reader;

namespace FM27Scout.Model;

public sealed class StaffRow
{
    public required StaffInfo Source { get; init; }
    public string Name => Source.Name;
    public string Job { get; init; } = "";
    public string Club => Source.Club ?? "Unemployed / national team";
    public string Competition => Source.Competition ?? "";
    public int? Age { get; init; }
    public string Nation => Source.Nationality ?? "";
    public string Personality { get; init; } = "";
    public int Professionalism => Source.Personality.Length > 4 ? Source.Personality[4] : 0;
    public int Ambition => Source.Personality.Length > 1 ? Source.Personality[1] : 0;
    public int Loyalty => Source.Personality.Length > 2 ? Source.Personality[2] : 0;
    public long Wage => Source.WeeklyWage;
    public string WageText => Wage > 0 ? MoneyText.Format(Wage) : "-";
    public DateOnly? Expiry => Source.ContractExpiry;
    public string PreviousClub => Source.PreviousClub ?? "";
}
