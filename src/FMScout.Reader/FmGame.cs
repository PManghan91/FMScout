using System.Diagnostics;
using System.Text;

namespace FMScout.Reader;

public sealed record Player(
    ulong Address,
    uint Uid,
    string Name,
    string? FullName,
    DateOnly? DateOfBirth,
    string? Nationality,
    string? Club,
    string? Competition,
    string? LinkedClub,
    long WeeklyWage,
    DateOnly? ContractExpiry,
    long Value,
    int CurrentAbility,
    int PotentialAbility,
    int HeightCm,
    byte[] Positions,
    byte[] Attributes,
    byte[] Personality,
    bool IsFemale,
    string? CommonName,
    int HomeReputation,
    int CurrentReputation,
    int WorldReputation,
    int SquadStatus,
    ulong ClubAddress,
    int ClubReputation,
    DateOnly? ContractStart,
    ulong TeamAddress,
    int CareerApps,
    int CareerGoals,
    string[] OtherNationalities,
    string[] FavouredClubs,
    string? LeagueNation);

public sealed record ClubInfo(ulong Address, string Name, int Reputation);

public sealed record StaffInfo(ulong Address, uint Uid, string Name, string? FullName, DateOnly? DateOfBirth, string? Nationality,
    string? Club, string? Competition, string? PreviousClub, int JobCode, long WeeklyWage, DateOnly? ContractExpiry, byte[] Personality, bool IsFemale);

/// <summary>Read-only view of a running FM27 game's database.</summary>
public sealed class FmGame : IDisposable
{
    public ProcessMemory Memory { get; }
    public BuildLayout Layout { get; }
    public string BuildKey { get; }
    public ulong ModuleBase { get; }
    private readonly Dictionary<ulong, long> _playerShift = new();
    private readonly int _blockLen;

    private FmGame(ProcessMemory m, BuildLayout layout, string key, ulong moduleBase)
    {
        Memory = m; Layout = layout; BuildKey = key; ModuleBase = moduleBase;
        _playerShift[moduleBase + (ulong)layout.PlayerVtableRva.Value] = 0;
        foreach (var pc in layout.ExtraPlayerClasses) _playerShift[moduleBase + (ulong)pc.VtableRva.Value] = pc.Shift.Value;
        _blockLen = (int)(-layout.Player.BlockStart.Value) + 0x10;
    }

    /// <summary>The engine's build string, e.g. "27.0.0+2493426" (ProductVersion of game_plugin.dll).</summary>
    public static string? DetectBuild(Process p, string module)
    {
        foreach (ProcessModule mod in p.Modules)
            if (string.Equals(mod.ModuleName, module, StringComparison.OrdinalIgnoreCase))
                return FileVersionInfo.GetVersionInfo(mod.FileName).ProductVersion;
        return null;
    }

    public static FmGame Attach(string layoutPath, string processName = "fm")
        => Attach(LayoutFile.Load(layoutPath), processName);

    public static FmGame Attach(Dictionary<string, BuildLayout> layouts, string processName = "fm")
    {
        var m = ProcessMemory.Attach(processName);
        var module = layouts.Values.First().Module;
        var build = DetectBuild(m.Process, module) ?? throw new InvalidOperationException($"{module} not loaded");
        if (!layouts.TryGetValue(build, out var layout))
            throw new NotSupportedException($"No layout for FM build {build}. Known: {string.Join(", ", layouts.Keys)}");
        var mb = m.Module(layout.Module)!.Value.Base;
        return new FmGame(m, layout, build, mb);
    }

    public DateOnly? GameDate => ReadDate(ModuleBase + (ulong)Layout.GameDateRva.Value);

    /// <summary>All person pointers (players, staff, retired, ...) from the database's master vector.</summary>
    public ulong[] PersonPointers()
    {
        var db = Memory.ReadPtr(ModuleBase + (ulong)Layout.PersonDb.RootRva.Value);
        ulong begin = Memory.ReadPtr(db + (ulong)Layout.PersonDb.VectorBegin.Value);
        ulong end = Memory.ReadPtr(db + (ulong)Layout.PersonDb.VectorEnd.Value);
        long count = (long)(end - begin) / 8;
        if (count <= 0 || count > 5_000_000) throw new InvalidOperationException($"Person vector looks wrong ({count})");
        var bytes = Memory.ReadBytes(begin, (int)(count * 8)) ?? throw new InvalidOperationException("Cannot read person vector");
        var res = new ulong[count];
        Buffer.BlockCopy(bytes, 0, res, 0, bytes.Length);
        return res;
    }

    public bool IsPlayer(ulong person) => _playerShift.ContainsKey(Memory.ReadPtr(person));

    public bool IsStaff(ulong person) => Layout.StaffVtableRva.Value != 0 && Memory.ReadPtr(person) == ModuleBase + (ulong)Layout.StaffVtableRva.Value;

    public StaffInfo ReadStaff(ulong v)
    {
        var L = Layout; var P = L.Person;
        string? first = NameEntry(v, P.FirstNameEntry), last = NameEntry(v, P.LastNameEntry);
        ulong nation = Memory.ReadPtr(v + (ulong)P.Nation.Value);
        ulong prev = Memory.ReadPtr(v + (ulong)P.Club.Value);
        ulong contract = Memory.ReadPtr(v + (ulong)P.Contract.Value);
        ulong team = contract < 0x10000 ? 0 : Memory.ReadPtr(contract + (ulong)L.Contract.Team.Value);
        ulong club = team < 0x10000 ? 0 : Memory.ReadPtr(team + (ulong)L.Team.Club.Value);
        ulong comp = team < 0x10000 ? 0 : Memory.ReadPtr(team + (ulong)L.Team.Competition.Value);
        return new StaffInfo(v, Memory.Read<uint>(v + (ulong)P.Uid.Value), $"{first} {last}".Trim(), FmString(Memory.ReadPtr(v + (ulong)P.FullName.Value)),
            ReadDate(v + (ulong)P.DateOfBirth.Value),
            nation < 0x10000 ? null : FmString(Memory.ReadPtr(nation + (ulong)L.Nation.Name.Value)),
            club < 0x10000 ? null : FmString(Memory.ReadPtr(club + (ulong)L.Club.Name.Value)),
            comp < 0x10000 ? null : FmString(Memory.ReadPtr(comp + (ulong)L.Team.CompetitionName.Value)),
            prev < 0x10000 ? null : FmString(Memory.ReadPtr(prev + (ulong)L.Club.Name.Value)),
            contract < 0x10000 ? 0 : Memory.Read<byte>(contract + (ulong)L.Contract.Job.Value),
            contract < 0x10000 ? 0 : Memory.Read<uint>(contract + (ulong)L.Contract.WeeklyWage.Value),
            contract < 0x10000 ? null : ReadDate(contract + (ulong)L.Contract.ExpiryDate.Value),
            Memory.ReadBytes(v + (ulong)P.Personality.Value, Math.Max(8, L.PersonalityNames.Length)) ?? [],
            (Memory.Read<byte>(v + (ulong)P.Flags.Value) >> P.FemaleBit & 1) == 1);
    }

    public ClubInfo? ReadClub(ulong club)
    {
        if (club < 0x10000) return null;
        var name = FmString(Memory.ReadPtr(club + (ulong)Layout.Club.Name.Value));
        return name == null ? null : new ClubInfo(club, name, Memory.Read<ushort>(club + (ulong)Layout.Club.Reputation.Value));
    }

    /// <summary>Club(s) managed by human players (person class = human manager, via their contract).</summary>
    public List<ClubInfo> HumanClubs()
    {
        var vt = ModuleBase + (ulong)Layout.HumanManagerVtableRva.Value;
        var res = new List<ClubInfo>();
        foreach (var v in PersonPointers())
        {
            if (Memory.ReadPtr(v) != vt) continue;
            ulong contract = Memory.ReadPtr(v + (ulong)Layout.Person.Contract.Value);
            if (contract < 0x10000) continue;
            ulong team = Memory.ReadPtr(contract + (ulong)Layout.Contract.Team.Value);
            if (team < 0x10000) continue;
            var c = ReadClub(Memory.ReadPtr(team + (ulong)Layout.Team.Club.Value));
            if (c != null && !res.Any(x => x.Address == c.Address)) res.Add(c);
        }
        return res;
    }

    public IEnumerable<ulong> PlayerPointers() => PersonPointers().Where(IsPlayer);

    public string? FmString(ulong p)
    {
        if (p < 0x10000) return null;
        uint len = Memory.Read<uint>(p);
        if (len == 0 || len > 512) return null;
        var b = Memory.ReadBytes(p + 4, (int)len);
        return b == null ? null : Encoding.UTF8.GetString(b);
    }

    /// <summary>Name-table entries: pointer to an entry whose first qword is the string.</summary>
    private string? NameEntry(ulong person, long off)
    {
        var entry = Memory.ReadPtr(person + (ulong)off);
        return entry < 0x10000 ? null : FmString(Memory.ReadPtr(entry));
    }

    public DateOnly? ReadDate(ulong a)
    {
        ushort raw = Memory.Read<ushort>(a), year = Memory.Read<ushort>(a + 2);
        int day = raw & 0x1FF; // upper bits are used for other data on some dates
        if (year <= 1900 || year > 2200 || day < 1 || day > 366) return null; // 1/1/1900 = "no date"
        return DateOnly.FromDateTime(new DateTime(year, 1, 1).AddDays(day - 1));
    }

    public Player ReadPlayer(ulong v)
    {
        var L = Layout; var P = L.Person; var PL = L.Player;
        string? first = NameEntry(v, P.FirstNameEntry), last = NameEntry(v, P.LastNameEntry);
        string name = $"{first} {last}".Trim();

        ulong nation = Memory.ReadPtr(v + (ulong)P.Nation.Value);
        ulong linked = Memory.ReadPtr(v + (ulong)P.Club.Value);
        ulong contract = Memory.ReadPtr(v + (ulong)P.Contract.Value);
        ulong team = contract < 0x10000 ? 0 : Memory.ReadPtr(contract + (ulong)L.Contract.Team.Value);
        ulong club = team < 0x10000 ? 0 : Memory.ReadPtr(team + (ulong)L.Team.Club.Value);
        ulong comp = team < 0x10000 ? 0 : Memory.ReadPtr(team + (ulong)L.Team.Competition.Value);

        var rel = Relations(v);

        // One read for the whole player block that sits before the person pointer.
        long shift = _playerShift.GetValueOrDefault(Memory.ReadPtr(v));
        ulong blockStart = (ulong)((long)v + PL.BlockStart.Value + shift);
        var block = Memory.ReadBytes(blockStart, _blockLen) ?? new byte[_blockLen];
        int Rel(long off) => (int)(off - PL.BlockStart.Value);

        return new Player(
            v,
            Memory.Read<uint>(v + (ulong)P.Uid.Value),
            name,
            FmString(Memory.ReadPtr(v + (ulong)P.FullName.Value)),
            ReadDate(v + (ulong)P.DateOfBirth.Value),
            nation < 0x10000 ? null : FmString(Memory.ReadPtr(nation + (ulong)L.Nation.Name.Value)),
            club < 0x10000 ? null : FmString(Memory.ReadPtr(club + (ulong)L.Club.Name.Value)),
            comp < 0x10000 ? null : FmString(Memory.ReadPtr(comp + (ulong)L.Team.CompetitionName.Value)),
            linked < 0x10000 ? null : FmString(Memory.ReadPtr(linked + (ulong)L.Club.Name.Value)),
            contract < 0x10000 ? 0 : Memory.Read<uint>(contract + (ulong)L.Contract.WeeklyWage.Value),
            contract < 0x10000 ? null : ReadDate(contract + (ulong)L.Contract.ExpiryDate.Value),
            BitConverter.ToUInt32(block, Rel(PL.Value)),
            BitConverter.ToInt16(block, Rel(PL.CurrentAbility)),
            BitConverter.ToInt16(block, Rel(PL.PotentialAbility)),
            block[Rel(PL.Height)],
            block.AsSpan(Rel(PL.Positions), L.PositionNames.Length).ToArray(),
            block.AsSpan(Rel(PL.Attributes), L.AttributeNames.Length).ToArray(),
            Memory.ReadBytes(v + (ulong)P.Personality.Value, L.PersonalityNames.Length) ?? [],
            (Memory.Read<byte>(v + (ulong)P.Flags.Value) >> P.FemaleBit & 1) == 1,
            PL.CommonName.Value == 0 ? null : FmString(BitConverter.ToUInt64(block, Rel(PL.CommonName))),
            BitConverter.ToUInt16(block, Rel(PL.HomeReputation)),
            BitConverter.ToUInt16(block, Rel(PL.CurrentReputation)),
            BitConverter.ToUInt16(block, Rel(PL.WorldReputation)),
            contract < 0x10000 ? 0 : Memory.Read<byte>(contract + (ulong)L.Contract.SquadStatus.Value),
            club,
            club < 0x10000 ? 0 : Memory.Read<ushort>(club + (ulong)L.Club.Reputation.Value),
            contract < 0x10000 ? null : ReadDate(contract + (ulong)L.Contract.StartDate.Value),
            team,
            CareerStat(block, Rel(PL.CareerStats), PL.CareerApps),
            CareerStat(block, Rel(PL.CareerStats), PL.CareerGoals),
            rel.nations, rel.clubs,
            comp < 0x10000 || L.Team.CompetitionNation.Value == 0 ? null : FmString(Memory.ReadPtr(Memory.ReadPtr(comp + (ulong)L.Team.CompetitionNation.Value) + (ulong)L.Nation.Name.Value)));
    }

    /// <summary>Other nationalities (nation relation 9) and favoured clubs (club relation 3/4) from the person's relationship list.</summary>
    (string[] nations, string[] clubs) Relations(ulong v)
    {
        if (Layout.Person.Relations.Value == 0) return ([], []);
        ulong obj = Memory.ReadPtr(v + (ulong)Layout.Person.Relations.Value);
        if (obj < 0x10000) return ([], []);
        ulong b = Memory.ReadPtr(obj), e = Memory.ReadPtr(obj + 8);
        long n = (long)(e - b) / 16;
        if (b < 0x10000 || n <= 0 || n > 400) return ([], []);
        var bytes = Memory.ReadBytes(b, (int)(n * 16)); if (bytes == null) return ([], []);
        var nations = new List<string>(); var clubs = new List<string>();
        for (int i = 0; i < n; i++)
        {
            int o = i * 16; byte kind = bytes[o + 0xA], relation = bytes[o + 0xB];
            ulong target = BitConverter.ToUInt64(bytes, o);
            if (kind == 8 && relation == 9) { var s = FmString(Memory.ReadPtr(target + (ulong)Layout.Nation.Name.Value)); if (s != null) nations.Add(s); }
            else if (kind == 1 && relation is 3 or 4) { var s = FmString(Memory.ReadPtr(target + (ulong)Layout.Club.Name.Value)); if (s != null) clubs.Add(s); }
        }
        return (nations.Distinct().ToArray(), clubs.Distinct().ToArray());
    }

    int CareerStat(byte[] block, int rel, Hex field)
    {
        if (Layout.Player.CareerStats.Value == 0 || rel < 0 || rel + 8 > block.Length) return 0;
        ulong p = BitConverter.ToUInt64(block, rel);
        return p < 0x10000 ? 0 : Memory.Read<ushort>(p + (ulong)field.Value);
    }

    public void Dispose() => Memory.Dispose();
}











