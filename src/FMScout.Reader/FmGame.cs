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
    string? CommonName);

/// <summary>Read-only view of a running FM27 game's database.</summary>
public sealed class FmGame : IDisposable
{
    public ProcessMemory Memory { get; }
    public BuildLayout Layout { get; }
    public string BuildKey { get; }
    public ulong ModuleBase { get; }
    private readonly ulong _playerVtable;
    private readonly int _blockLen;

    private FmGame(ProcessMemory m, BuildLayout layout, string key, ulong moduleBase)
    {
        Memory = m; Layout = layout; BuildKey = key; ModuleBase = moduleBase;
        _playerVtable = moduleBase + (ulong)layout.PlayerVtableRva.Value;
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

    public bool IsPlayer(ulong person) => Memory.ReadPtr(person) == _playerVtable;

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
        if (year < 1900 || year > 2200 || day < 1 || day > 366) return null;
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

        // One read for the whole player block that sits before the person pointer.
        ulong blockStart = v - (ulong)(-PL.BlockStart.Value);
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
            PL.CommonName.Value == 0 ? null : FmString(BitConverter.ToUInt64(block, Rel(PL.CommonName))));
    }

    public void Dispose() => Memory.Dispose();
}




