using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FMScout.Reader;

/// <summary>Offsets for one FM build, loaded from fm27-layout.json. Hex strings like "0x138" or "-0x24".</summary>
public sealed class BuildLayout
{
    public string Module { get; set; } = "game_plugin.dll";
    public long SteamBuildId { get; set; }
    public PersonDbLayout PersonDb { get; set; } = new();
    public Hex GameDateRva { get; set; }
    public Hex PlayerVtableRva { get; set; }
    public PersonLayout Person { get; set; } = new();
    public NationLayout Nation { get; set; } = new();
    public ClubLayout Club { get; set; } = new();
    public ContractLayout Contract { get; set; } = new();
    public TeamLayout Team { get; set; } = new();
    public PlayerLayout Player { get; set; } = new();
    public string[] PositionNames { get; set; } = [];
    public string[] AttributeNames { get; set; } = [];
    public string[] PersonalityNames { get; set; } = [];
}

public sealed class PersonDbLayout { public Hex RootRva { get; set; } public Hex VectorBegin { get; set; } public Hex VectorEnd { get; set; } }

public sealed class PersonLayout
{
    public Hex Uid { get; set; }
    public Hex FullName { get; set; }
    public Hex FirstNameEntry { get; set; }
    public Hex LastNameEntry { get; set; }
    public Hex Nation { get; set; }
    public Hex Personality { get; set; }
    public Hex BirthCity { get; set; }
    public Hex DateOfBirth { get; set; }
    public Hex Club { get; set; }
    public Hex Contract { get; set; }
}

public sealed class ContractLayout { public Hex Team { get; set; } public Hex WeeklyWage { get; set; } public Hex StartDate { get; set; } public Hex ExpiryDate { get; set; } }
public sealed class TeamLayout { public Hex Club { get; set; } public Hex Competition { get; set; } public Hex CompetitionName { get; set; } }

public sealed class NationLayout { public Hex Name { get; set; } public Hex Code { get; set; } public Hex Nationality { get; set; } }
public sealed class ClubLayout { public Hex Name { get; set; } public Hex ShortName { get; set; } }

/// <summary>Player-specific block. Offsets are relative to the person pointer and lie BEFORE it (negative).</summary>
public sealed class PlayerLayout
{
    public Hex BlockStart { get; set; }   // start of the player's data block relative to the person pointer
    public Hex Positions { get; set; }
    public Hex Attributes { get; set; }
    public Hex Height { get; set; }
    public Hex CurrentAbility { get; set; }
    public Hex PotentialAbility { get; set; }
    public Hex Value { get; set; }
}

[JsonConverter(typeof(HexConverter))]
public readonly record struct Hex(long Value)
{
    public static implicit operator long(Hex h) => h.Value;
    public override string ToString() => Value < 0 ? $"-0x{-Value:X}" : $"0x{Value:X}";
}

public sealed class HexConverter : JsonConverter<Hex>
{
    public override Hex Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o)
    {
        if (r.TokenType == JsonTokenType.Number) return new Hex(r.GetInt64());
        var s = r.GetString()!.Trim();
        bool neg = s.StartsWith('-');
        if (neg) s = s[1..];
        long v = s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? long.Parse(s[2..], NumberStyles.HexNumber)
            : long.Parse(s);
        return new Hex(neg ? -v : v);
    }

    public override void Write(Utf8JsonWriter w, Hex v, JsonSerializerOptions o) => w.WriteStringValue(v.ToString());
}

public static class LayoutFile
{
    private sealed class Root { public Dictionary<string, BuildLayout> Builds { get; set; } = new(); }

    public static Dictionary<string, BuildLayout> Load(string path)
    {
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip };
        return JsonSerializer.Deserialize<Root>(File.ReadAllText(path), opts)!.Builds;
    }
}


