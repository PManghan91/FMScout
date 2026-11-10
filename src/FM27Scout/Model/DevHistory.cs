using System.IO;

namespace FM27Scout.Model;

/// <summary>
/// Development tracking across loads. Per save (keyed by your club + the newgen UID boundary), keeps two compact snapshots
/// of every player (UID → CA, PA, attribute sum): the previous load on a different game day, and the first load of the
/// current season (seasons start 1 July). Deltas: CA since last load, CA and attribute total since the season started.
/// </summary>
public static class DevHistory
{
    sealed record Snap(DateOnly Date, Dictionary<uint, (byte Ca, byte Pa, ushort Sum)> Rows);

    static string FileFor(string key) => Path.Combine(AppPaths.Data, "history", string.Concat(key.Split(Path.GetInvalidFileNameChars())) + ".bin");

    public static DateOnly SeasonStart(DateOnly d) => new(d.Month >= 7 ? d.Year : d.Year - 1, 7, 1);

    /// <summary>Fills DeltaCa* on rows and updates the history file. Returns a short status note.</summary>
    public static string Apply(List<PlayerRow> rows, string saveKey, DateOnly today)
    {
        var path = FileFor(saveKey);
        Snap? prev = null, season = null;
        try { (prev, season) = Read(path); }
        catch { FMScout.Reader.AtomicFile.MoveAside(path); }

        static ushort Sum(PlayerRow p) => (ushort)p.A.Sum();
        foreach (var p in rows)
        {
            if (prev != null && prev.Rows.TryGetValue(p.Uid, out var a)) p.DeltaCaLast = p.CA - a.Ca;
            if (season != null && season.Rows.TryGetValue(p.Uid, out var s)) { p.DeltaCaSeason = p.CA - s.Ca; p.DeltaAttrSeason = Sum(p) - s.Sum; }
        }

        var cur = new Snap(today, rows.ToDictionary(p => p.Uid, p => ((byte)Math.Clamp(p.CA, 0, 255), (byte)Math.Clamp(p.PA, 0, 255), Sum(p))));
        bool newSeason = season == null || SeasonStart(season.Date) != SeasonStart(today);
        // "previous" for the next load is today's state, unless we already loaded today (then keep the older baseline)
        var toStorePrev = prev != null && prev.Date == today ? prev : cur;
        var toStoreSeason = newSeason ? cur : season!;
        try { Write(path, toStorePrev, toStoreSeason); } catch { /* history is best-effort */ }

        string note = season == null ? "development tracking started" : $"development since {season.Date:dd/MM/yyyy}";
        if (prev != null && prev.Date != today) note += $", last load {prev.Date:dd/MM/yyyy}";
        return note;
    }

    static (Snap? prev, Snap? season) Read(string path)
    {
        if (!File.Exists(path)) return (null, null);
        using var br = new BinaryReader(File.OpenRead(path));
        if (br.ReadInt32() != 0x31564544) throw new InvalidDataException("bad history file"); // "DEV1"
        Snap One()
        {
            var date = DateOnly.FromDayNumber(br.ReadInt32());
            int n = br.ReadInt32();
            var d = new Dictionary<uint, (byte, byte, ushort)>(n);
            for (int i = 0; i < n; i++) d[br.ReadUInt32()] = (br.ReadByte(), br.ReadByte(), br.ReadUInt16());
            return new Snap(date, d);
        }
        return (One(), One());
    }

    static void Write(string path, Snap prev, Snap season)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        using (var bw = new BinaryWriter(File.Create(tmp)))
        {
            bw.Write(0x31564544);
            foreach (var s in new[] { prev, season })
            {
                bw.Write(s.Date.DayNumber); bw.Write(s.Rows.Count);
                foreach (var (uid, (ca, pa, sum)) in s.Rows) { bw.Write(uid); bw.Write(ca); bw.Write(pa); bw.Write(sum); }
            }
        }
        if (File.Exists(path)) File.Replace(tmp, path, null, true); else File.Move(tmp, path);
    }
}

