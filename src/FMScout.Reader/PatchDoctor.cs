using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FMScout.Reader;

/// <summary>
/// Re-locates the moved parts of the layout after an FM patch, starting from the last known layout:
///  1. find well-known real players by full name (string search) and so the player class (vtable);
///  2. find the master person vector containing them, the object holding it, and the static pointer to that object;
///  3. check the player block (positions / CA / PA / height) against stable facts (PA, height) and re-search it if it moved;
///  4. shift the other class vtables / static data RVAs by the same deltas and validate them.
/// Struct-internal offsets are kept from the template and validated; anything that fails validation is reported.
/// Read-only: only reads FM's memory. The result is written as a new build section in a layout JSON file.
/// </summary>
public static class PatchDoctor
{
    sealed record Anchor(string FullName, int Pa, int HeightCm, int StRating, int GkRating);

    // Stable facts (PA and height don't change during a save). Any one found is enough; more = more confidence.
    static readonly Anchor[] Anchors =
    [
        new("Erling Braut Haaland", 195, 195, 20, 1),
        new("Lamine Yamal Nasraoui Ebana", 195, 178, -1, 1),
        new("Jude Victor William Bellingham", 188, 186, -1, 1),
        new("Bukayo Ayoyinka Saka", 188, 178, -1, 1),
    ];

    public sealed class Result
    {
        public bool Ok;
        public string Build = "";
        public BuildLayout? Layout;
        public List<string> Log = [];
    }

    public static Result Run(ProcessMemory m, BuildLayout template, string build, IProgress<string>? progress = null)
    {
        var res = new Result { Build = build };
        void Log(string s) { res.Log.Add(s); progress?.Report(s); }
        var gp = m.Module(template.Module) ?? throw new InvalidOperationException($"{template.Module} not loaded");
        bool InMod(ulong x) => x >= gp.Base && x < gp.Base + (ulong)gp.Size;
        var L = Clone(template);
        long fullNameOff = L.Person.FullName.Value;

        // 1) anchors by full name
        Log("Searching for known players by full name…");
        var found = new List<(Anchor a, ulong v)>();
        foreach (var a in Anchors)
        {
            var bytes = Encoding.UTF8.GetBytes(a.FullName);
            var pat = BitConverter.GetBytes((uint)bytes.Length).Concat(bytes).ToArray();
            var strs = m.Scan(pat, null, privateOnly: false, alignment: 4, max: 20).ToHashSet();
            if (strs.Count == 0) continue;
            foreach (var (at, _) in m.ScanPointersToAny(strs, privateOnly: true, max: 50))
            {
                ulong v = at - (ulong)fullNameOff;
                if (InMod(m.ReadPtr(v))) { found.Add((a, v)); break; }
            }
            if (found.Count >= 2) break;
        }
        if (found.Count == 0) { Log("FAILED: none of the anchor players found (full-name offset may have moved)."); return res; }
        ulong playerVt = m.ReadPtr(found[0].v);
        long vtDelta = (long)(playerVt - gp.Base) - L.PlayerVtableRva.Value;
        Log($"Player class: vtable RVA 0x{playerVt - gp.Base:X} (was 0x{L.PlayerVtableRva.Value:X}, delta {vtDelta:+#;-#;0})");
        L.PlayerVtableRva = new Hex((long)(playerVt - gp.Base));

        // 2) master person vector containing the anchor
        Log("Locating the master person list…");
        var vectors = new List<(ulong begin, ulong end)>();
        foreach (var (at, _) in m.ScanPointersToAny(new HashSet<ulong> { found[0].v }, privateOnly: true, max: 500))
        {
            if (vectors.Any(x => at >= x.begin && at < x.end)) continue;
            ulong lo = at, hi = at;
            bool IsObj(ulong a) { ulong t = m.ReadPtr(a); return t > 0x10000 && InMod(m.ReadPtr(t)); }
            while (IsObj(lo - 8)) lo -= 8;
            while (IsObj(hi + 8)) hi += 8;
            if ((hi - lo) / 8 > 50_000) vectors.Add((lo, hi + 8));
        }
        if (vectors.Count == 0) { Log("FAILED: master person list not found."); return res; }
        ulong vecBegin = vectors[0].begin, vecEnd = vectors[0].end;
        var holders = m.ScanPointersToAny(vectors.Select(x => x.begin).ToHashSet(), privateOnly: true, max: 20).ToList();
        bool rootOk = false;
        var allHits = new List<(ulong rva, ulong off, ulong begin, ulong end)>();
        foreach (var (holder, beginVal) in holders)
        {
            var vec = vectors.First(x => x.begin == beginVal);
            ulong endVal = m.ReadPtr(holder + 8);
            if (endVal < vec.end - 0x4000 || endVal > vec.end + 0x4000) continue; // [begin, end) pair, allowing for entries the game adds while we scan
            vecBegin = vec.begin; vecEnd = endVal;
            // static pointer into the object that holds the vector (object start within 0x800 before the holder)
            var cands = new HashSet<ulong>(); for (ulong k = 0; k <= 0x800; k += 8) cands.Add(holder - k);
            var hits = new List<(ulong rva, ulong off)>();
            foreach (var r in m.Regions(false).Where(r => r.Base >= gp.Base && r.Base < gp.Base + (ulong)gp.Size && (r.Protect & 0x04) != 0))
            {
                var buf = m.ReadBytes(r.Base, (int)r.Size); if (buf == null) continue;
                for (int i = 0; i + 8 <= buf.Length; i += 8)
                {
                    ulong q = BitConverter.ToUInt64(buf, i);
                    if (cands.Contains(q)) hits.Add((r.Base + (ulong)i - gp.Base, holder - q));
                }
            }
            allHits.AddRange(hits.Select(h => (h.rva, h.off, vecBegin, vecEnd)));
        }
        if (allHits.Count > 0)
        {
            // prefer the same object/offset as before (struct layouts rarely change), then the closest static to the old one
            var best = allHits.OrderBy(h => h.off == (ulong)template.PersonDb.VectorBegin.Value ? 0 : 1)
                              .ThenBy(h => Math.Abs((long)h.rva - template.PersonDb.RootRva.Value)).First();
            L.PersonDb.RootRva = new Hex((long)best.rva);
            L.PersonDb.VectorBegin = new Hex((long)best.off);
            L.PersonDb.VectorEnd = new Hex((long)best.off + 8);
            vecBegin = best.begin; vecEnd = best.end;
            rootOk = true;
        }
        if (!rootOk) { Log("FAILED: static pointer to the person database not found."); return res; }
        Log($"Person DB: root RVA {L.PersonDb.RootRva}, vector at +{L.PersonDb.VectorBegin} ({(vecEnd - vecBegin) / 8:N0} people)");

        // 3) player block: validate with PA + height; re-search positions block if needed
        bool BlockOk(BuildLayout lay) => found.All(f =>
        {
            ulong v = f.v; var PL = lay.Player;
            short pa = m.Read<short>((ulong)((long)v + PL.PotentialAbility.Value));
            byte h = m.Read<byte>((ulong)((long)v + PL.Height.Value));
            return pa == f.a.Pa && h == f.a.HeightCm;
        });
        if (BlockOk(L)) Log("Player block offsets still valid (PA and height match the anchors).");
        else
        {
            Log("Player block moved: re-searching…");
            long oldPos = L.Player.Positions.Value; bool fixedIt = false;
            for (long shift = -0x400; shift <= 0x400 && !fixedIt; shift += 1)
            {
                var t = Clone(L); ShiftPlayer(t.Player, shift);
                if (BlockOk(t)) { L = t; fixedIt = true; Log($"Player block found shifted by {shift:+#;-#;0} bytes."); }
            }
            if (!fixedIt) { Log("FAILED: player block not found within ±0x400 (internal layout probably changed)."); return res; }
        }

        // 4) other classes / static data: same delta as the player vtable / root, validated
        var counts = CountClasses(m, vecBegin, vecEnd);
        Hex Shift(Hex rva, long delta) => new(rva.Value + delta);
        int Count(Hex rva) => counts.GetValueOrDefault(gp.Base + (ulong)rva.Value);
        foreach (var pc in L.ExtraPlayerClasses) pc.VtableRva = Shift(pc.VtableRva, vtDelta);
        L.StaffVtableRva = Shift(L.StaffVtableRva, vtDelta);
        L.HumanManagerVtableRva = Shift(L.HumanManagerVtableRva, vtDelta);
        long dataDelta = L.PersonDb.RootRva.Value - template.PersonDb.RootRva.Value;
        L.GameDateRva = Shift(L.GameDateRva, dataDelta);
        Log($"Players: {Count(L.PlayerVtableRva):N0}; extra player classes: {string.Join(", ", L.ExtraPlayerClasses.Select(pc => Count(pc.VtableRva).ToString("N0")))}; " +
            $"staff: {Count(L.StaffVtableRva):N0}; human managers: {Count(L.HumanManagerVtableRva)}");
        if (Count(L.StaffVtableRva) == 0) Log("WARNING: staff class not found at the shifted RVA (Staff tab will be empty).");
        if (Count(L.HumanManagerVtableRva) == 0) Log("WARNING: human manager class not found (pick your club manually).");
        ushort year = m.Read<ushort>(gp.Base + (ulong)L.GameDateRva.Value + 2);
        if (year is < 2026 or > 2100)
        {
            // look near the shifted spot for a date-shaped value (u16 day with flags, u16 year 2026-2100)
            bool fixedDate = false;
            for (long d = -0x400; d <= 0x400 && !fixedDate; d += 4)
            {
                ulong a = (ulong)((long)gp.Base + L.GameDateRva.Value + d);
                ushort y = m.Read<ushort>(a + 2), day = (ushort)(m.Read<ushort>(a) & 0x1FF);
                if (y is >= 2026 and <= 2100 && day is >= 1 and <= 366) { L.GameDateRva = new Hex(L.GameDateRva.Value + d); fixedDate = true; Log($"Game date re-found {d:+#;-#;0} bytes away (year {y}) - please sanity-check the date in the status bar."); }
            }
            if (!fixedDate) Log($"WARNING: game date not found (read year {year}); ages use today's date.");
        }

        res.Layout = L; res.Ok = true;
        Log("Done. New layout validated against " + string.Join(", ", found.Select(f => f.a.FullName)) + ".");
        return res;
    }

    static void ShiftPlayer(PlayerLayout p, long s)
    {
        foreach (var prop in typeof(PlayerLayout).GetProperties().Where(x => x.PropertyType == typeof(Hex)))
        {
            if (prop.Name is nameof(PlayerLayout.CareerGoals) or nameof(PlayerLayout.CareerApps)) continue; // offsets inside the stats object
            var h = (Hex)prop.GetValue(p)!;
            if (h.Value != 0) prop.SetValue(p, new Hex(h.Value + s));
        }
    }

    static Dictionary<ulong, int> CountClasses(ProcessMemory m, ulong begin, ulong end)
    {
        var bytes = m.ReadBytes(begin, (int)(end - begin)) ?? [];
        var d = new Dictionary<ulong, int>();
        for (int i = 0; i + 8 <= bytes.Length; i += 8) { ulong vt = m.ReadPtr(BitConverter.ToUInt64(bytes, i)); d[vt] = d.GetValueOrDefault(vt) + 1; }
        return d;
    }

    static BuildLayout Clone(BuildLayout l)
    {
        var o = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return JsonSerializer.Deserialize<BuildLayout>(JsonSerializer.Serialize(l, o), o)!;
    }

    /// <summary>Adds/replaces the build section in a layout JSON file (comments are not preserved in the new section).</summary>
    public static void Save(string path, string build, BuildLayout layout, string templateJson)
    {
        var docOpts = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        var root = JsonNode.Parse(File.Exists(path) ? File.ReadAllText(path) : templateJson, documentOptions: docOpts)!.AsObject();
        var builds = root["builds"]!.AsObject();
        var camel = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
        builds[build] = JsonNode.Parse(JsonSerializer.Serialize(layout, camel));
        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
}



