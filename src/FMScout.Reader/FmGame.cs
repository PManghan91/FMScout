using System.Collections.Concurrent;
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

public sealed class NoSaveLoadedException() : Exception("FM is running but its people database is empty - no save loaded yet?");
public sealed class UnknownBuildException(string build) : Exception($"No layout for FM build {build}") { public string Build { get; } = build; }

public sealed record ClubInfo(ulong Address, string Name, int Reputation);

public sealed record StaffInfo(ulong Address, uint Uid, string Name, string? FullName, DateOnly? DateOfBirth, string? Nationality,
    string? Club, string? Competition, string? PreviousClub, int JobCode, long WeeklyWage, DateOnly? ContractExpiry, byte[] Personality, bool IsFemale);

/// <summary>
/// Read-only view of a running FM27 game's database. Thread-safe for reading (ReadPlayer/ReadStaff may run in parallel):
/// each person record is read in one call, and strings of shared objects (nations, clubs, competitions, name-table entries)
/// are cached by address.
/// </summary>
public sealed class FmGame : IDisposable
{
    public ProcessMemory Memory { get; }
    public BuildLayout Layout { get; }
    public string BuildKey { get; }
    public ulong ModuleBase { get; }
    public ulong ModuleSize { get; }
    private readonly Dictionary<ulong, long> _playerShift = new();
    private readonly int _blockLen, _personLen;
    private readonly ConcurrentDictionary<ulong, string?> _strCache = new();
    private readonly ConcurrentDictionary<(ulong, long), ulong> _ptrCache = new();

    private FmGame(ProcessMemory m, BuildLayout layout, string key, ulong moduleBase, ulong moduleSize)
    {
        Memory = m; Layout = layout; BuildKey = key; ModuleBase = moduleBase; ModuleSize = moduleSize;
        _playerShift[moduleBase + (ulong)layout.PlayerVtableRva.Value] = 0;
        foreach (var pc in layout.ExtraPlayerClasses) _playerShift[moduleBase + (ulong)pc.VtableRva.Value] = pc.Shift.Value;

        // player block: from BlockStart to the end of the furthest field (+8 covers pointers / u32 / u16)
        var PL = layout.Player;
        long maxOff = typeof(PlayerLayout).GetProperties().Where(p => p.PropertyType == typeof(Hex)
                && p.Name is not (nameof(PlayerLayout.CareerGoals) or nameof(PlayerLayout.CareerApps) or nameof(PlayerLayout.BlockStart)))
            .Select(p => ((Hex)p.GetValue(PL)!).Value).Where(v => v != 0).DefaultIfEmpty(0).Max();
        long extra = Math.Max(layout.AttributeNames.Length, layout.PositionNames.Length);
        _blockLen = (int)Math.Max(maxOff - PL.BlockStart.Value + extra, 0x10) + 8;
        if (_blockLen > 0x2000) throw new InvalidOperationException($"Player block length {_blockLen} looks wrong - check the layout.");

        // person record: up to the furthest person field + 8
        var P = layout.Person;
        long personMax = typeof(PersonLayout).GetProperties().Where(p => p.PropertyType == typeof(Hex)).Select(p => ((Hex)p.GetValue(P)!).Value).Max();
        _personLen = (int)Math.Max(personMax + 8, P.Personality.Value + layout.PersonalityNames.Length + 8);
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

    /// <summary>Attaches read-only. Throws AttachException (not running / starting / access denied) or UnknownBuildException.</summary>
    public static FmGame Attach(Dictionary<string, BuildLayout> layouts, string processName = "fm")
    {
        var module = layouts.Values.First().Module;
        var m = ProcessMemory.Attach(processName, module);
        try
        {
            var build = DetectBuild(m.Process, module) ?? throw new AttachException(AttachProblem.Starting, $"{module} not loaded yet");
            if (!layouts.TryGetValue(build, out var layout)) throw new UnknownBuildException(build);
            var mod = m.Module(layout.Module) ?? throw new AttachException(AttachProblem.Starting, $"{layout.Module} not loaded yet");
            return new FmGame(m, layout, build, mod.Base, (ulong)mod.Size);
        }
        catch { m.Dispose(); throw; }
    }

    public bool InModule(ulong q) => q >= ModuleBase && q < ModuleBase + ModuleSize;

    public DateOnly? GameDate => ReadDate(ModuleBase + (ulong)Layout.GameDateRva.Value);

    ulong[]? _persons;

    /// <summary>
    /// All person pointers (players, staff, retired, ...) from the database's master vector. Several static paths lead to it;
    /// a path is accepted only if it is a plausible vector of C++ objects (aligned pointers whose vtables lie in the game
    /// module). The largest valid one wins. None valid = no save loaded yet.
    /// </summary>
    public ulong[] PersonPointers()
    {
        if (_persons != null) return _persons;
        var paths = new[] { Layout.PersonDb }.Concat(Layout.PersonDb.Alternatives);
        ulong[]? best = null;
        foreach (var path in paths)
        {
            try
            {
                var db = Memory.ReadPtr(ModuleBase + (ulong)path.RootRva.Value);
                if (!ProcessMemory.IsObjPtr(db)) continue;
                ulong begin = Memory.ReadPtr(db + (ulong)path.VectorBegin.Value);
                ulong end = Memory.ReadPtr(db + (ulong)path.VectorEnd.Value);
                if (!ProcessMemory.IsObjPtr(begin) || end <= begin || ((end - begin) & 7) != 0) continue;
                long count = (long)(end - begin) / 8;
                if (count < 1000 || count > 5_000_000 || (best != null && count <= best.Length)) continue;
                var bytes = Memory.ReadBytes(begin, (int)(count * 8));
                if (bytes == null) continue;
                var arr = new ulong[count];
                Buffer.BlockCopy(bytes, 0, arr, 0, bytes.Length);
                // validate a sample: >= 90% must be objects whose vtable is inside the game module
                int ok = 0, n = 0;
                for (long i = 0; i < count; i += Math.Max(1, count / 64)) { n++; ulong o = arr[i]; if (ProcessMemory.IsObjPtr(o) && InModule(Memory.ReadPtr(o))) ok++; }
                if (ok < n * 0.9) continue;
                best = arr;
            }
            catch { /* unreadable path: try the next one */ }
        }
        return _persons = best ?? throw new NoSaveLoadedException();
    }

    public bool IsPlayer(ulong person) => _playerShift.ContainsKey(Memory.ReadPtr(person));
    public bool IsStaff(ulong person) => Layout.StaffVtableRva.Value != 0 && Memory.ReadPtr(person) == ModuleBase + (ulong)Layout.StaffVtableRva.Value;

    // ---------------------------------------------------------------- cached helpers
    string? CachedString(ulong obj, long nameOff)
    {
        if (!ProcessMemory.IsObjPtr(obj)) return null;
        ulong sp = Memory.ReadPtr(obj + (ulong)nameOff);
        return CachedFmString(sp);
    }

    string? CachedFmString(ulong sp) => ProcessMemory.IsPtr(sp) ? _strCache.GetOrAdd(sp, FmString) : null;

    /// <summary>Pointer stored in a shared object (team -> club etc.), cached.</summary>
    ulong CachedPtr(ulong obj, long off) => !ProcessMemory.IsObjPtr(obj) ? 0 : _ptrCache.GetOrAdd((obj, off), k => Memory.ReadPtr(k.Item1 + (ulong)k.Item2));

    public string? FmString(ulong p)
    {
        if (!ProcessMemory.IsPtr(p)) return null;
        uint len = Memory.Read<uint>(p);
        if (len == 0 || len > 512) return null;
        var b = Memory.ReadBytes(p + 4, (int)len);
        return b == null ? null : Encoding.UTF8.GetString(b);
    }

    /// <summary>Name-table entry: pointer to an entry whose first qword is the string.</summary>
    string? NameEntry(ulong entry) => ProcessMemory.IsObjPtr(entry) ? CachedFmString(CachedPtr(entry, 0)) : null;

    public DateOnly? ReadDate(ulong a) => DecodeDate(Memory.Read<uint>(a));

    /// <summary>u16 day-of-year (low 9 bits; upper bits are flags) + u16 year. 1/1/1900 = "no date".</summary>
    public static DateOnly? DecodeDate(uint raw)
    {
        int day = (int)(raw & 0x1FF), year = (int)(raw >> 16);
        if (year <= 1900 || year > 2200 || day < 1 || day > 366) return null;
        if (day == 366 && !DateTime.IsLeapYear(year)) return null;
        return DateOnly.FromDateTime(new DateTime(year, 1, 1).AddDays(day - 1));
    }

    static ulong U64(byte[] b, long off) => off >= 0 && off + 8 <= b.Length ? BitConverter.ToUInt64(b, (int)off) : 0;
    static uint U32(byte[] b, long off) => off >= 0 && off + 4 <= b.Length ? BitConverter.ToUInt32(b, (int)off) : 0;
    static ushort U16(byte[] b, long off) => off >= 0 && off + 2 <= b.Length ? BitConverter.ToUInt16(b, (int)off) : (ushort)0;
    static short S16(byte[] b, long off) => off >= 0 && off + 2 <= b.Length ? BitConverter.ToInt16(b, (int)off) : (short)0;
    static byte U8(byte[] b, long off) => off >= 0 && off < b.Length ? b[off] : (byte)0;

    // ---------------------------------------------------------------- shared lookups via contract
    (ulong contract, ulong team, ulong club, ulong comp) ContractChain(byte[] pr)
    {
        var L = Layout;
        ulong contract = U64(pr, L.Person.Contract);
        if (!ProcessMemory.IsObjPtr(contract)) return (0, 0, 0, 0);
        ulong team = Memory.ReadPtr(contract + (ulong)L.Contract.Team.Value);
        if (!ProcessMemory.IsObjPtr(team)) return (contract, 0, 0, 0);
        return (contract, team, CachedPtr(team, L.Team.Club), CachedPtr(team, L.Team.Competition));
    }

    public StaffInfo ReadStaff(ulong v)
    {
        var L = Layout; var P = L.Person;
        var pr = Memory.ReadBytes(v, _personLen) ?? throw new InvalidOperationException("person unreadable");
        var (contract, _, club, comp) = ContractChain(pr);
        byte[]? con = ProcessMemory.IsObjPtr(contract) ? Memory.ReadBytes(contract, 0x60) : null;
        return new StaffInfo(v, U32(pr, P.Uid), $"{NameEntry(U64(pr, P.FirstNameEntry))} {NameEntry(U64(pr, P.LastNameEntry))}".Trim(),
            CachedFmString(U64(pr, P.FullName)),
            DecodeDate(U32(pr, P.DateOfBirth)),
            CachedString(U64(pr, P.Nation), L.Nation.Name),
            CachedString(club, L.Club.Name),
            CachedString(comp, L.Team.CompetitionName),
            CachedString(U64(pr, P.Club), L.Club.Name),
            con == null ? 0 : U8(con, L.Contract.Job),
            con == null ? 0 : U32(con, L.Contract.WeeklyWage),
            con == null ? null : DecodeDate(U32(con, L.Contract.ExpiryDate)),
            pr.AsSpan((int)P.Personality.Value, Math.Min(L.PersonalityNames.Length, pr.Length - (int)P.Personality.Value)).ToArray(),
            (U8(pr, P.Flags) >> P.FemaleBit & 1) == 1);
    }

    public ClubInfo? ReadClub(ulong club)
    {
        var name = CachedString(club, Layout.Club.Name);
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
            var pr = Memory.ReadBytes(v, _personLen); if (pr == null) continue;
            var (_, _, club, _) = ContractChain(pr);
            var c = ReadClub(club);
            if (c != null && !res.Any(x => x.Address == c.Address)) res.Add(c);
        }
        return res;
    }

    public IEnumerable<ulong> PlayerPointers() => PersonPointers().Where(IsPlayer);

    public Player ReadPlayer(ulong v)
    {
        var L = Layout; var P = L.Person; var PL = L.Player;
        var pr = Memory.ReadBytes(v, _personLen) ?? throw new InvalidOperationException("person unreadable");
        long shift = _playerShift.TryGetValue(U64(pr, 0), out var s) ? s : 0;
        var block = Memory.ReadBytes((ulong)((long)v + PL.BlockStart.Value + shift), _blockLen) ?? throw new InvalidOperationException("player block unreadable");
        long Rel(long off) => off - PL.BlockStart.Value;

        var (contract, team, club, comp) = ContractChain(pr);
        byte[]? con = ProcessMemory.IsObjPtr(contract) ? Memory.ReadBytes(contract, 0x60) : null;
        var rel = Relations(U64(pr, P.Relations));
        ulong compNation = L.Team.CompetitionNation.Value == 0 ? 0 : CachedPtr(comp, L.Team.CompetitionNation);
        int pers = L.PersonalityNames.Length;

        return new Player(
            v,
            U32(pr, P.Uid),
            $"{NameEntry(U64(pr, P.FirstNameEntry))} {NameEntry(U64(pr, P.LastNameEntry))}".Trim(),
            CachedFmString(U64(pr, P.FullName)),
            DecodeDate(U32(pr, P.DateOfBirth)),
            CachedString(U64(pr, P.Nation), L.Nation.Name),
            CachedString(club, L.Club.Name),
            CachedString(comp, L.Team.CompetitionName),
            CachedString(U64(pr, P.Club), L.Club.Name),
            con == null ? 0 : U32(con, L.Contract.WeeklyWage),
            con == null ? null : DecodeDate(U32(con, L.Contract.ExpiryDate)),
            U32(block, Rel(PL.Value)),
            S16(block, Rel(PL.CurrentAbility)),
            S16(block, Rel(PL.PotentialAbility)),
            U8(block, Rel(PL.Height)),
            block.AsSpan((int)Rel(PL.Positions), L.PositionNames.Length).ToArray(),
            block.AsSpan((int)Rel(PL.Attributes), L.AttributeNames.Length).ToArray(),
            pr.AsSpan((int)P.Personality.Value, Math.Min(pers, pr.Length - (int)P.Personality.Value)).ToArray(),
            (U8(pr, P.Flags) >> P.FemaleBit & 1) == 1,
            PL.CommonName.Value == 0 ? null : CachedFmString(U64(block, Rel(PL.CommonName))),
            U16(block, Rel(PL.HomeReputation)),
            U16(block, Rel(PL.CurrentReputation)),
            U16(block, Rel(PL.WorldReputation)),
            con == null ? 0 : U8(con, L.Contract.SquadStatus),
            club,
            ProcessMemory.IsObjPtr(club) ? Memory.Read<ushort>(club + (ulong)L.Club.Reputation.Value) : 0,
            con == null ? null : DecodeDate(U32(con, L.Contract.StartDate)),
            team,
            CareerStat(U64(block, Rel(PL.CareerStats)), PL.CareerApps),
            CareerStat(U64(block, Rel(PL.CareerStats)), PL.CareerGoals),
            rel.nations, rel.clubs,
            CachedString(compNation, L.Nation.Name));
    }

    /// <summary>Other nationalities and favoured clubs from the person's relationship list (codes from the layout).</summary>
    (string[] nations, string[] clubs) Relations(ulong obj)
    {
        var R = Layout.RelationCodes;
        if (Layout.Person.Relations.Value == 0 || !ProcessMemory.IsObjPtr(obj)) return ([], []);
        ulong b = Memory.ReadPtr(obj), e = Memory.ReadPtr(obj + 8);
        if (!ProcessMemory.IsObjPtr(b) || e <= b) return ([], []);
        long n = (long)(e - b) / 16;
        if (n <= 0 || n > 400) return ([], []);
        var bytes = Memory.ReadBytes(b, (int)(n * 16)); if (bytes == null) return ([], []);
        var nations = new List<string>(); var clubs = new List<string>();
        for (int i = 0; i < n; i++)
        {
            int o = i * 16; byte kind = bytes[o + 0xA], relation = bytes[o + 0xB];
            ulong target = BitConverter.ToUInt64(bytes, o);
            if (!ProcessMemory.IsObjPtr(target)) continue;
            if (kind == R.NationKind && relation == R.Nationality) { var s = CachedString(target, Layout.Nation.Name); if (s != null) nations.Add(s); }
            else if (kind == R.ClubKind && R.FavouredClub.Contains(relation)) { var s = CachedString(target, Layout.Club.Name); if (s != null) clubs.Add(s); }
        }
        return (nations.Distinct().ToArray(), clubs.Distinct().ToArray());
    }

    int CareerStat(ulong statsObj, Hex field)
        => Layout.Player.CareerStats.Value == 0 || !ProcessMemory.IsObjPtr(statsObj) ? 0 : Memory.Read<ushort>(statsObj + (ulong)field.Value);

    public void Dispose() => Memory.Dispose();
}
