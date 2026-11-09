using FMScout.Reader;

namespace FM27Scout.Model;

/// <summary>One player as shown in the grid. Immutable snapshot taken at load time.</summary>
public sealed class PlayerRow
{
    public required Player Source { get; init; }
    public uint Uid => Source.Uid;
    /// <summary>Known-as name when the game has one (e.g. "Lamine Yamal"), else first + last.</summary>
    public string Name => Source.CommonName ?? Source.Name;
    public string? FullName => Source.FullName;
    public int? Age { get; init; }
    public DateOnly? DateOfBirth => Source.DateOfBirth;
    public string Club => Source.Club ?? "Free agent";
    public string League => Source.Competition ?? "";
    public string Nation => Source.Nationality ?? "";
    public string PositionsText { get; init; } = "";
    public byte[] Positions => Source.Positions;
    public int CA => Source.CurrentAbility;
    public int PA => Source.PotentialAbility;
    public int Height => Source.HeightCm;
    public long Value => Source.Value;
    public long Wage => Source.WeeklyWage;
    public DateOnly? ContractExpiry => Source.ContractExpiry;
    public int LeftFoot => A[Attr.LeftFoot];
    public int RightFoot => A[Attr.RightFoot];
    public string Foot => LeftFoot >= 18 && RightFoot >= 18 ? "Either" : LeftFoot > RightFoot ? "Left" : "Right";
    public bool IsGoalkeeper => Source.Positions[0] >= 15;
    public int CurrentRep => Source.CurrentReputation;
    public int HomeRep => Source.HomeReputation;
    public int WorldRep => Source.WorldReputation;
    public int ClubRep => Source.ClubReputation;
    public string SquadStatusName { get; init; } = "";

    /// <summary>"Would join" estimate 0-100 for the selected club (-1 = already ours).</summary>
    public double Join { get; set; }
    public string JoinLabel { get; set; } = "";
    public List<string> JoinReasons { get; set; } = [];

    /// <summary>Attributes on the FM 1-20 scale (index = layout attribute order).</summary>
    public required int[] A { get; init; }
    public byte[] Raw => Source.Attributes;
    public byte[] Personality => Source.Personality;

    /// <summary>Role scores 0-100, index = RoleSet.Roles order.</summary>
    public double[] RoleScores { get; set; } = [];
    public double[] PotentialRoleScores { get; set; } = [];
    public string BestRole { get; set; } = "";
    public double BestRoleScore { get; set; }
    public double BestPotentialScore { get; set; }

    /// <summary>Score for the role currently chosen in the sidebar (drives the "Role" grid columns).</summary>
    public double SelectedRoleScore { get; set; }
    public double SelectedPotentialScore { get; set; }

    public static int ToDisplay(byte raw) => Math.Clamp((raw + 2) / 5, 1, 20);
}

/// <summary>Attribute indices (FM's internal order, confirmed against the UI).</summary>
public static class Attr
{
    public static string[] Names = [];
    public static int LeftFoot = 24, RightFoot = 25;
    static Dictionary<string, int> _ix = new(StringComparer.OrdinalIgnoreCase);

    public static void Init(string[] names)
    {
        Names = names;
        _ix = names.Select((n, i) => (n, i)).ToDictionary(t => t.n, t => t.i, StringComparer.OrdinalIgnoreCase);
        LeftFoot = _ix.GetValueOrDefault("LeftFoot", 24);
        RightFoot = _ix.GetValueOrDefault("RightFoot", 25);
    }

    public static int Index(string name) => _ix.TryGetValue(name, out var i) ? i : -1;

    public static string Pretty(string n) => n switch
    {
        "OffTheBall" => "Off The Ball", "OneOnOnes" => "One On Ones", "CommandOfArea" => "Command Of Area",
        "FreeKicks" => "Free Kick Taking", "Penalties" => "Penalty Taking", "RushingOut" => "Rushing Out (Tendency)",
        "Punching" => "Punching (Tendency)", "ImportantMatches" => "Important Matches", "InjuryProneness" => "Injury Proneness",
        _ => System.Text.RegularExpressions.Regex.Replace(n, "(?<=[a-z])(?=[A-Z])", " ")
    };

    // UI groups, in FM's on-screen order.
    public static readonly string[] Technical = ["Corners", "Crossing", "Dribbling", "Finishing", "FirstTouch", "FreeKicks", "Heading", "LongShots", "LongThrows", "Marking", "Passing", "Penalties", "Tackling", "Technique"];
    public static readonly string[] Mental = ["Aggression", "Anticipation", "Bravery", "Composure", "Concentration", "Decisions", "Determination", "Flair", "Leadership", "OffTheBall", "Positioning", "Teamwork", "Vision", "WorkRate"];
    public static readonly string[] Physical = ["Acceleration", "Agility", "Balance", "JumpingReach", "NaturalFitness", "Pace", "Stamina", "Strength"];
    public static readonly string[] Goalkeeping = ["AerialReach", "CommandOfArea", "Communication", "Eccentricity", "FirstTouch", "Handling", "Kicking", "OneOnOnes", "Passing", "Punching", "Reflexes", "RushingOut", "Throwing"];
    public static readonly string[] Hidden = ["Consistency", "Dirtiness", "ImportantMatches", "InjuryProneness", "Versatility"];
}


