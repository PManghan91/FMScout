using FMScout.Probe;
using FMScout.Reader;

if (args.Length > 0 && args[0] == "poscands")
{
    using var g = FmGame.Attach(Report.FindLayout());
    var ps = g.PlayerPointers().Select(g.ReadPlayer).Where(p => p.CurrentAbility >= 140).ToList();
    foreach (var p in ps.Where(p => p.Positions[4] >= 10 || p.Positions[13] >= 15 || p.Positions[14] >= 15 || p.Positions[1] >= 15 || p.Positions[3] >= 15)
                        .OrderByDescending(p => p.CurrentAbility).Take(25))
        Console.WriteLine($"{p.Name,-30} CA {p.CurrentAbility} {p.Club,-25} pos " + string.Join(" ", p.Positions));
    var gk = ps.Where(p => p.Positions[0] == 20).OrderByDescending(p => p.CurrentAbility).Take(5);
    foreach (var p in gk) Console.WriteLine($"GK {p.Name,-28} CA {p.CurrentAbility} {p.Club} AR {p.Attributes[12]} Com {p.Attributes[14]} Ref {p.Attributes[21]} Kick {p.Attributes[15]} Thr {p.Attributes[16]}");
    return;
}
if (args.Length > 0 && args[0] == "patchtest")
{
    var layouts = LayoutFile.Load(Report.FindLayout());
    var good = layouts["27.0.0+2493426"];
    var opts = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    var bad = System.Text.Json.JsonSerializer.Deserialize<BuildLayout>(System.Text.Json.JsonSerializer.Serialize(good, opts), opts)!;
    // simulate a patch: statics moved +0x1000, vtables +0x40, player block +0x10
    bad.PersonDb.RootRva = new Hex(good.PersonDb.RootRva + 0x1000); bad.GameDateRva = new Hex(good.GameDateRva + 0x1000);
    bad.PlayerVtableRva = new Hex(good.PlayerVtableRva + 0x40); bad.StaffVtableRva = new Hex(good.StaffVtableRva + 0x40); bad.HumanManagerVtableRva = new Hex(good.HumanManagerVtableRva + 0x40);
    foreach (var pc in bad.ExtraPlayerClasses) pc.VtableRva = new Hex(pc.VtableRva + 0x40);
    bad.Shortlist.Lists = new Hex(good.Shortlist.Lists + 0x18); bad.Shortlist.ListPlayers = new Hex(good.Shortlist.ListPlayers + 0x8);
    foreach (var pr in typeof(PlayerLayout).GetProperties().Where(x => x.PropertyType == typeof(Hex) && x.Name is not ("CareerGoals" or "CareerApps")))
    { var h = (Hex)pr.GetValue(bad.Player)!; if (h.Value != 0) pr.SetValue(bad.Player, new Hex(h.Value + 0x10)); }
    using var m3 = ProcessMemory.Attach("fm");
    var sw = System.Diagnostics.Stopwatch.StartNew();
    var r = PatchDoctor.Run(m3, bad, "test-build", new Progress<string>(Console.WriteLine));
    Console.WriteLine($"ok={r.Ok} in {sw.Elapsed.TotalSeconds:F0}s");
    if (r.Layout != null)
    {
        void Cmp(string n, long a, long b) => Console.WriteLine($"  {n,-22} found {a:X} expected {b:X} {(a == b ? "OK" : "MISMATCH")}");
        Cmp("rootRva", r.Layout.PersonDb.RootRva, good.PersonDb.RootRva); Cmp("vectorBegin", r.Layout.PersonDb.VectorBegin, good.PersonDb.VectorBegin);
        Cmp("playerVtable", r.Layout.PlayerVtableRva, good.PlayerVtableRva); Cmp("staffVtable", r.Layout.StaffVtableRva, good.StaffVtableRva);
        Cmp("humanVtable", r.Layout.HumanManagerVtableRva, good.HumanManagerVtableRva); Cmp("gameDate", r.Layout.GameDateRva, good.GameDateRva);
        Cmp("player.positions", r.Layout.Player.Positions, good.Player.Positions); Cmp("player.CA", r.Layout.Player.CurrentAbility, good.Player.CurrentAbility);
        Cmp("player.careerStats", r.Layout.Player.CareerStats, good.Player.CareerStats);
        Cmp("shortlist.teamData", r.Layout.Shortlist.TeamData, good.Shortlist.TeamData); Cmp("shortlist.lists", r.Layout.Shortlist.Lists, good.Shortlist.Lists); Cmp("shortlist.listPlayers", r.Layout.Shortlist.ListPlayers, good.Shortlist.ListPlayers);
    }
    return;
}
if (args.Length > 0 && args[0] == "statscross")
{
    using var g = FmGame.Attach(Report.FindLayout());
    var m2 = g.Memory;
    ulong h = 0x227E07609C8, y = 0x227E57990D8; // Haaland, Yamal
    // ranges (UI at 4/2/2037, game moved on ~4 weeks): [haaLo, haaHi, yamLo, yamHi]
    var targets = new (string n, int a, int b, int c, int d)[] { ("career apps", 583, 600, 441, 456), ("career goals", 364, 376, 212, 224), ("season apps", 24, 34, 27, 36), ("season goals", 5, 10, 13, 19) };
    IEnumerable<(string label, ulong addr)> Sources(ulong v)
    {
        yield return ("block", v - 0x288);
        for (int s = 0; s < 0x3C8; s += 8)
        {
            ulong p1 = m2.ReadPtr(v - 0x288 + (ulong)s);
            if (p1 < 0x10000000000 || p1 > 0x7FF000000000) continue;
            yield return ($"[blk+0x{s:X}]", p1);
            for (int t = 0; t < 0x100; t += 8) { ulong p2 = m2.ReadPtr(p1 + (ulong)t); if (p2 > 0x10000000000 && p2 < 0x7FF000000000) yield return ($"[[blk+0x{s:X}]+0x{t:X}]", p2); }
        }
    }
    var hs = Sources(h).ToList(); var ys = Sources(y).ToDictionary(x => x.label, x => x.addr);
    foreach (var (label, ha) in hs)
    {
        if (!ys.TryGetValue(label, out var ya)) continue;
        var hb = m2.ReadBytes(ha, 0x400); var yb = m2.ReadBytes(ya, 0x400); if (hb == null || yb == null) continue;
        foreach (var t in targets)
            for (int i = 0; i + 2 <= 0x400; i += 2)
            {
                int hv = BitConverter.ToUInt16(hb, i), yv = BitConverter.ToUInt16(yb, i);
                if (hv >= t.a && hv <= t.b && yv >= t.c && yv <= t.d) Console.WriteLine($"{t.n}: {label}+0x{i:X} haaland {hv} yamal {yv}");
            }
    }
    return;
}
if (args.Length > 0 && args[0] == "statsfind")
{
    using var g = FmGame.Attach(Report.FindLayout());
    var m2 = g.Memory; ulong v = Explore.Hex(args[1]);
    var refs = m2.ScanPointersToAny(new HashSet<ulong> { v }, privateOnly: true, max: 5000);
    Console.WriteLine($"{refs.Count} refs");
    foreach (var (at, _) in refs)
    {
        var b = m2.ReadBytes(at - 0x80, 0x180); if (b == null) continue;
        var floats = new List<string>();
        for (int i = 0; i + 4 <= b.Length; i += 4) { float f = BitConverter.ToSingle(b, i); if (f > 5.5f && f < 8.5f) floats.Add($"{i - 0x80:+#;-#;0}:{f:0.00}"); }
        var u16s = new List<string>();
        for (int i = 0; i + 2 <= b.Length; i += 2) { ushort u = BitConverter.ToUInt16(b, i); if (u is >= 580 and <= 600 || u is >= 360 and <= 375) u16s.Add($"{i - 0x80:+#;-#;0}:{u}"); }
        if (floats.Count >= 2 || u16s.Count >= 2) Console.WriteLine($"ref @0x{at:X}: floats[{string.Join(" ", floats.Take(8))}] u16[{string.Join(" ", u16s.Take(8))}]");
    }
    return;
}
if (args.Length > 0 && args[0] == "fmshortlist")
{
    using var g = FmGame.Attach(Report.FindLayout());
    var byUid = g.PlayerPointers().Select(g.ReadPlayer).GroupBy(x => x.Uid).ToDictionary(x => x.Key, x => x.First());
    foreach (var (club, uids) in g.HumanShortlists()) Console.WriteLine($"{club}: {uids.Count}: {string.Join(", ", uids.Select(u => byUid.TryGetValue(u, out var pl) ? pl.Name : u.ToString()))}");
    return;
}
if (args.Length > 0 && args[0] == "clublists")
{
    using var g = FmGame.Attach(Report.FindLayout());
    var m2 = g.Memory; var players = g.PlayerPointers().ToHashSet();
    ulong hvt = g.ModuleBase + (ulong)g.Layout.HumanManagerVtableRva.Value;
    var paul = g.PersonPointers().First(p2 => m2.ReadPtr(p2) == hvt && (g.FmString(m2.ReadPtr(p2 + 0x40)) ?? "").Contains("Manghan"));
    ulong team = m2.ReadPtr(m2.ReadPtr(paul + 0xA8) + 0x10);
    ulong obj = m2.ReadPtr(team + 0x80);
    Console.WriteLine($"team {team:X} team+0x30 {m2.ReadPtr(team + 0x30):X} obj {obj:X} vt {m2.ReadPtr(obj) - g.ModuleBase:X}");
    for (int o = 0x210; o <= 0x250; o += 8) Console.WriteLine($"  obj+{o:X}: {m2.ReadPtr(obj + (ulong)o):X}");
    ulong b = m2.ReadPtr(obj + 0x230), e = m2.ReadPtr(obj + 0x238);
    Console.WriteLine($"lists vector {b:X}..{e:X} n={(e - b) / 8}");
    for (ulong a = b; a < e && a < b + 8 * 40; a += 8)
    {
        ulong q = m2.ReadPtr(a);
        ulong vb = m2.ReadPtr(q + 0x80), ve = m2.ReadPtr(q + 0x88);
        int n = ve > vb && ve - vb < 8 * 10000 ? (int)((ve - vb) / 8) : -1;
        var names = new List<string>();
        for (int i = 0; i < Math.Min(n, 6); i++) { ulong r = m2.ReadPtr(vb + (ulong)i * 8); ulong pp = m2.ReadPtr(r); if (players.Contains(pp)) names.Add(g.ReadPlayer(pp).Name); }
        string nm = "";
        for (int o = 0; o < 0x80; o += 8) { var s2 = g.FmString(m2.ReadPtr(q + (ulong)o)); if (!string.IsNullOrWhiteSpace(s2) && s2.Length < 60) { nm += $" +{o:X}=\"{s2}\""; } }
        Console.WriteLine($"  list {q:X} vt {(g.InModule(m2.ReadPtr(q)) ? (m2.ReadPtr(q) - g.ModuleBase).ToString("X") : "-")} n={n}{nm}: {string.Join(", ", names)}");
        Console.WriteLine($"     head {Convert.ToHexString(m2.ReadBytes(q, 0x80) ?? [])}");
    }
    return;
}
if (args.Length > 0 && args[0] == "listnames")
{
    using var g = FmGame.Attach(Report.FindLayout());
    var m2 = g.Memory; var players = g.PlayerPointers().ToHashSet();
    foreach (var s in args.Skip(1))
    {
        ulong v = ulong.Parse(s, System.Globalization.NumberStyles.HexNumber);
        ulong b = m2.ReadPtr(v), e = m2.ReadPtr(v + 8);
        Console.WriteLine($"--- vector at {v:X}: {(e - b) / 8} entries");
        for (ulong a = b; a < e; a += 8)
        {
            ulong r = m2.ReadPtr(a); ulong pp = players.Contains(r) ? r : players.Contains(m2.ReadPtr(r)) ? m2.ReadPtr(r) : 0;
            Console.WriteLine($"   {(pp != 0 ? g.ReadPlayer(pp).Name + " (" + g.ReadPlayer(pp).Club + ")" : "?")}  rec {Convert.ToHexString(m2.ReadBytes(r, 0x40) ?? [])}");
        }
    }
    return;
}
if (args.Length > 0 && args[0] == "dumpat")
{
    using var g = FmGame.Attach(Report.FindLayout());
    var m2 = g.Memory; var players = g.PlayerPointers().ToHashSet();
    foreach (var s in args.Skip(1))
    {
        ulong a = ulong.Parse(s, System.Globalization.NumberStyles.HexNumber);
        Console.WriteLine($"--- {a:X}");
        for (long k = -0x60; k <= 0x60; k += 8)
        {
            ulong q = m2.ReadPtr((ulong)((long)a + k));
            string d = players.Contains(q) ? " PLAYER " + g.ReadPlayer(q).Name : g.InModule(q) ? $" vt {q - g.ModuleBase:X}" : ProcessMemory.IsObjPtr(q) && g.InModule(m2.ReadPtr(q)) ? $" ->obj vt {m2.ReadPtr(q) - g.ModuleBase:X}" : "";
            Console.WriteLine($"  {k,4:X}: {q:X16}{d}");
        }
    }
    foreach (var pp in players) { var n = g.ReadPlayer(pp).Name; if (n.Contains("Sinul") || n.EndsWith(" Rao")) Console.WriteLine($"name match: {n} {pp:X}"); }
    return;
}
if (args.Length > 0 && args[0] == "findlist")
{
    using var g = FmGame.Attach(Report.FindLayout());
    var m2 = g.Memory;
    var names = args.Skip(1).ToArray();
    var players = g.PlayerPointers().ToHashSet();
    var t = players.Where(pp => names.Any(n => g.ReadPlayer(pp).Name.Contains(n))).ToDictionary(pp => pp, pp => g.ReadPlayer(pp).Name);
    foreach (var (k, v) in t) Console.WriteLine($"{v} {k:X}");
    // direct refs and refs to wrapper objects (entry -> person at +0..+0x20)
    var refs = m2.ScanPointersToAny(t.Keys.ToHashSet(), privateOnly: true, max: 100000);
    Console.WriteLine($"{refs.Count} direct refs");
    var byRegion = refs.GroupBy(r => r.At >> 16).Where(gr => gr.Select(x => x.Value).Distinct().Count() >= 2).ToList();
    foreach (var gr in byRegion) Console.WriteLine($"  region {gr.Key << 16:X}: {string.Join(", ", gr.Select(x => $"{x.At:X}={t[x.Value]}"))}");
    // wrappers
    var wrappers = new Dictionary<ulong, ulong>();
    foreach (var (at, val) in refs) for (int k = 0; k <= 0x20; k += 8) wrappers.TryAdd(at - (ulong)k, val);
    var wrefs = m2.ScanPointersToAny(wrappers.Keys.ToHashSet(), privateOnly: true, max: 200000);
    var byRegion2 = wrefs.GroupBy(r => r.At >> 16).Where(gr => gr.Select(x => wrappers[x.Value]).Distinct().Count() >= 3).ToList();
    foreach (var gr in byRegion2) Console.WriteLine($"  wrapper region {gr.Key << 16:X}: {string.Join(", ", gr.Select(x => $"{x.At:X}->{x.Value:X}(+{x.Value - 0:X})={t[wrappers[x.Value]]}"))}");
    return;
}
if (args.Length > 0 && args[0] == "clubvecs")
{
    using var g = FmGame.Attach(Report.FindLayout());
    var m2 = g.Memory;
    var players = g.PlayerPointers().ToHashSet();
    var sl = System.Text.Json.JsonSerializer.Deserialize<uint[]>(File.ReadAllText(Environment.ExpandEnvironmentVariables(@"%APPDATA%\FM27Scout\shortlist.json")))!.ToHashSet();
    ulong hvt = g.ModuleBase + (ulong)g.Layout.HumanManagerVtableRva.Value;
    var paul = g.PersonPointers().First(p2 => m2.ReadPtr(p2) == hvt && (g.FmString(m2.ReadPtr(p2 + 0x40)) ?? "").Contains("Manghan"));
    ulong obj = m2.ReadPtr(m2.ReadPtr(m2.ReadPtr(paul + 0xA8) + 0x10) + 0x80);
    Console.WriteLine($"obj {obj:X} vt {m2.ReadPtr(obj) - g.ModuleBase:X}; team+0x30 club? {m2.ReadPtr(m2.ReadPtr(paul + 0xA8) + 0x10) + 0x30:X}");
    var buf = m2.ReadBytes(obj, 0x600)!;
    for (int o = 0; o + 16 <= buf.Length; o += 8)
    {
        ulong b = BitConverter.ToUInt64(buf, o), e = BitConverter.ToUInt64(buf, o + 8);
        if (!ProcessMemory.IsObjPtr(b) || e <= b || e - b > 8 * 20000 || (e - b) % 8 != 0) continue;
        int n = (int)((e - b) / 8);
        var names = new List<string>(); int inSl = 0, pl = 0; var fieldOff = -1;
        for (int i = 0; i < n; i++)
        {
            ulong q = m2.ReadPtr(b + (ulong)i * 8); ulong pp = 0;
            if (players.Contains(q)) pp = q;
            else if (ProcessMemory.IsObjPtr(q)) for (int k = 0; k <= 0x20; k += 8) if (players.Contains(m2.ReadPtr(q + (ulong)k))) { pp = m2.ReadPtr(q + (ulong)k); fieldOff = k; break; }
            if (pp == 0) continue;
            pl++; var pr = g.ReadPlayer(pp); if (sl.Contains(pr.Uid)) inSl++; if (names.Count < 5) names.Add(pr.Name);
        }
        if (pl > 0) Console.WriteLine($"+{o:X}: n={n} players={pl} (entry field +{fieldOff:X}) in Scout shortlist {inSl}/{sl.Count}: {string.Join(", ", names)}");
    }
    return;
}
if (args.Length > 0 && args[0] == "humanvecs")
{
    using var g = FmGame.Attach(Report.FindLayout());
    var m2 = g.Memory;
    var players = g.PlayerPointers().ToHashSet();
    ulong hvt = g.ModuleBase + (ulong)g.Layout.HumanManagerVtableRva.Value;
    var humans = g.PersonPointers().Where(p2 => m2.ReadPtr(p2) == hvt).ToList();
    Console.WriteLine($"{humans.Count} human managers");
    var seen = new HashSet<ulong>();
    void Walk(ulong obj, string path, int depth)
    {
        if (depth > 3 || !seen.Add(obj)) return;
        var buf = m2.ReadBytes(obj, 0x400); if (buf == null) return;
        for (int o = 0; o + 24 <= buf.Length; o += 8)
        {
            ulong b = BitConverter.ToUInt64(buf, o), e = BitConverter.ToUInt64(buf, o + 8);
            if (ProcessMemory.IsObjPtr(b) && e > b && e - b <= 8 * 5000 && (e - b) % 8 == 0 && ProcessMemory.IsObjPtr(e))
            {
                int n = (int)((e - b) / 8), hits = 0, hitsInd = 0;
                for (int i = 0; i < Math.Min(n, 50); i++) { ulong q = m2.ReadPtr(b + (ulong)i * 8); if (players.Contains(q)) hits++; else if (ProcessMemory.IsObjPtr(q) && (players.Contains(m2.ReadPtr(q)) || players.Contains(m2.ReadPtr(q + 8)) || players.Contains(m2.ReadPtr(q + 0x10)))) hitsInd++; }
                if (hits + hitsInd > 0 && hits + hitsInd >= Math.Min(n, 50) / 2)
                {
                    Console.WriteLine($"  {path}+{o:X} vector n={n} direct={hits} indirect={hitsInd}");
                    for (int i = 0; i < Math.Min(n, 6); i++) { ulong q = m2.ReadPtr(b + (ulong)i * 8); ulong pp = players.Contains(q) ? q : players.Contains(m2.ReadPtr(q)) ? m2.ReadPtr(q) : players.Contains(m2.ReadPtr(q + 8)) ? m2.ReadPtr(q + 8) : m2.ReadPtr(q + 0x10); if (players.Contains(pp)) Console.WriteLine($"      {g.ReadPlayer(pp).Name} ({g.ReadPlayer(pp).Club})"); }
                }
            }
        }
        for (int o = 0; o + 8 <= buf.Length; o += 8)
        {
            ulong q = BitConverter.ToUInt64(buf, o);
            if (ProcessMemory.IsObjPtr(q) && !players.Contains(q) && g.InModule(m2.ReadPtr(q))) Walk(q, $"{path}+{o:X}>", depth + 1);
        }
    }
    foreach (var h in humans) { Console.WriteLine($"human {g.FmString(m2.ReadPtr(h + 0x40))}"); Walk(h, "H", 0); }
    return;
}
if (args.Length > 0 && args[0] == "arranged")
{
    using var g = FmGame.Attach(Report.FindLayout());
    var sw = System.Diagnostics.Stopwatch.StartNew();
    var pl = g.PlayerPointers().ToHashSet();
    var t = g.ArrangedTransfers(pl, g.GameDate!.Value);
    Console.WriteLine($"{t.Count} arranged in {sw.ElapsedMilliseconds} ms");
    foreach (var (person, a) in t.OrderBy(x => x.Value.Date).Take(25)) { var p2 = g.ReadPlayer(person); Console.WriteLine($"  {p2.Name,-28} CA {p2.CurrentAbility,3} {p2.Club,-28} -> {a.ToClub,-28} {a.Date} {(a.Free ? "free" : "")}"); }
    foreach (var n in args.Skip(1)) foreach (var (person, a) in t) { var p2 = g.ReadPlayer(person); if (p2.Name.Contains(n)) Console.WriteLine($"  * {p2.Name} {p2.Club} -> {a.ToClub} {a.Date} free={a.Free}"); }
    return;
}
if (args.Length > 0 && args[0] == "who")
{
    using var g = FmGame.Attach(Report.FindLayout());
    var m2 = g.Memory; var today = g.GameDate!.Value;
    ulong contractVt = 0;
    foreach (var v in g.PlayerPointers()) { ulong con = m2.ReadPtr(v + 0xA8); if (con > 0x10000) { contractVt = m2.ReadPtr(con); break; } }
    var names = args.Skip(1).ToArray();
    var targets = g.PlayerPointers().Where(pp => names.Any(n => g.ReadPlayer(pp).Name.Contains(n))).ToHashSet();
    foreach (var t in targets) { var pl = g.ReadPlayer(t); Console.WriteLine($"{pl.Name} {pl.Club} person {t:X} cur con {m2.ReadPtr(t + 0xA8):X}"); }
    var all = m2.ScanPointersToAny(new HashSet<ulong> { contractVt }, privateOnly: true, max: 2_000_000).Select(h => h.At).Where(c2 => targets.Contains(m2.ReadPtr(c2 + 8))).ToList();
    foreach (var con in all)
    {
        ulong person = m2.ReadPtr(con + 8);
        ulong club = m2.ReadPtr(m2.ReadPtr(con + 0x10) + 0x30);
        Console.WriteLine($" contract {con:X} {(m2.ReadPtr(person + 0xA8) == con ? "CUR " : "xtra")} -> {g.FmString(m2.ReadPtr(club + 0xC0))} start {g.ReadDate(con + 0x44)} exp {g.ReadDate(con + 0x48)} wage {m2.Read<uint>(con + 0x20)}");
        foreach (var (at, _) in m2.ScanPointersToAny(new HashSet<ulong> { con }, privateOnly: true, max: 20))
        {
            var rb = m2.ReadBytes(at, 0x38);
            Console.WriteLine($"    ref {at:X}: {(rb == null ? "" : Convert.ToHexString(rb))}");
        }
    }
    return;
}
if (args.Length > 0 && args[0] == "offers")
{
    using var g = FmGame.Attach(Report.FindLayout());
    var m2 = g.Memory; var today = g.GameDate!.Value;
    var names = args.Skip(1).ToArray();
    int withOffers = 0, agreed = 0;
    foreach (var person in g.PlayerPointers())
    {
        ulong con = m2.ReadPtr(person + 0xA8); if (!ProcessMemory.IsObjPtr(con)) continue;
        ulong neg = m2.ReadPtr(con + 0x130); if (!ProcessMemory.IsObjPtr(neg)) continue;
        ulong b = m2.ReadPtr(neg + 0x20), e = m2.ReadPtr(neg + 0x28);
        if (!ProcessMemory.IsObjPtr(b) || e < b || e - b > 8 * 200) continue;
        bool show = names.Length > 0 && names.Any(n => g.ReadPlayer(person).Name.Contains(n));
        if (e > b) withOffers++;
        bool any = false;
        for (ulong a = b; a < e; a += 8)
        {
            ulong r = m2.ReadPtr(a); ulong oc = m2.ReadPtr(r + 0x10);
            var rb = m2.ReadBytes(r + 0x10, 0x48); if (rb == null) continue;
            if (rb[0x2E] == 2 && rb[0x31] != 8) any = true;
            if (show)
            {
                ulong club = ProcessMemory.IsObjPtr(oc) ? m2.ReadPtr(m2.ReadPtr(oc + 0x10) + 0x30) : 0;
                Console.WriteLine($"  {g.ReadPlayer(person).Name}: -> {(club != 0 ? g.FmString(m2.ReadPtr(club + 0xC0)) : "?")} start {(oc != 0 ? g.ReadDate(oc + 0x44) : null)} 2E={rb[0x2E]} 30={rb[0x30]} 31={rb[0x31]} bytes {Convert.ToHexString(rb, 0x18, 0x30)}");
            }
        }
        if (show) Console.WriteLine($"  {g.ReadPlayer(person).Name}: con {con:X} neg {neg:X} n={(e - b) / 8}");
        if (any) agreed++;
    }
    Console.WriteLine($"players with offers list entries {withOffers}, with agreed (2E=2, not renewal) {agreed}");
    return;
}
if (args.Length > 0 && args[0] == "backrefs")
{
    using var g = FmGame.Attach(Report.FindLayout());
    var m2 = g.Memory;
    var mod = m2.Module("game_plugin.dll")!.Value;
    bool InMod(ulong q) => q >= mod.Base && q < mod.Base + (ulong)mod.Size;
    ulong at = ulong.Parse(args[1], System.Globalization.NumberStyles.HexNumber);
    for (int depth = 0; depth < 6; depth++)
    {
        var near = new HashSet<ulong>(); for (ulong a = at - 0x180; a <= at; a += 8) near.Add(a);
        var hits = m2.ScanPointersToAny(near, privateOnly: false, max: 40);
        Console.WriteLine($"depth {depth}: target {at:X}: {hits.Count} refs");
        for (ulong a = at - 0x40; a <= at + 0x18; a += 8) Console.Write($" {m2.ReadPtr(a):X}"); Console.WriteLine();
        foreach (var (h, v) in hits.Take(12)) Console.WriteLine($"   {h:X} -> {v:X} (off -{at - v:X}) {(InMod(h) ? $"STATIC rva {h - mod.Base:X}" : "")} vt@v={(InMod(m2.ReadPtr(v)) ? (m2.ReadPtr(v) - mod.Base).ToString("X") : "-")}");
        var pick = hits.Where(x => !InMod(x.At)).Select(x => x.At).ToList();
        if (hits.Any(x => InMod(x.At)) || pick.Count == 0) break;
        at = pick[0];
    }
    return;
}
if (args.Length > 0 && args[0] == "ptrarray")
{
    using var g = FmGame.Attach(Report.FindLayout());
    var m2 = g.Memory;
    var mod = m2.Module("game_plugin.dll")!.Value;
    bool InMod(ulong q) => q >= mod.Base && q < mod.Base + (ulong)mod.Size;
    ulong at0 = ulong.Parse(args[1], System.Globalization.NumberStyles.HexNumber);
    int stride = args.Length > 2 ? int.Parse(args[2]) : 8;
    ulong b = at0; while (ProcessMemory.IsObjPtr(m2.ReadPtr(b - (ulong)stride))) b -= (ulong)stride;
    ulong e = at0; while (ProcessMemory.IsObjPtr(m2.ReadPtr(e))) e += (ulong)stride;
    Console.WriteLine($"ptr array {b:X}..{e:X} n={(e - b) / (ulong)stride}; before: {m2.ReadPtr(b - 8):X} {m2.ReadPtr(b - 16):X}");
    var level = new HashSet<ulong> { b };
    for (int depth = 0; depth < 6; depth++)
    {
        var hits = m2.ScanPointersToAny(level, privateOnly: false, max: 50);
        Console.WriteLine($"depth {depth}: {hits.Count} refs to {string.Join(",", level.Select(x => x.ToString("X")))}");
        var next = new HashSet<ulong>();
        foreach (var (at, val) in hits)
        {
            bool stat = InMod(at);
            Console.WriteLine($"   {at:X} -> {val:X} {(stat ? $"STATIC rva {at - mod.Base:X}" : "")}  +8={m2.ReadPtr(at + 8):X} +10={m2.ReadPtr(at + 16):X}");
            if (stat) continue;
            for (int k = 0; k <= 0x800; k += 8) { ulong q = m2.ReadPtr(at - (ulong)k); if (InMod(q)) { Console.WriteLine($"      owner {at - (ulong)k:X} vt {q - mod.Base:X} field +{k:X}"); next.Add(at - (ulong)k); break; } }
        }
        if (next.Count == 0 || next.Count > 10) break; level = next;
    }
    return;
}
if (args.Length > 0 && args[0] == "offerroot")
{
    using var g = FmGame.Attach(Report.FindLayout());
    var m2 = g.Memory;
    var mod = m2.Module("game_plugin.dll")!.Value;
    bool InMod(ulong q) => q >= mod.Base && q < mod.Base + (ulong)mod.Size;
    ulong contractVt = 0;
    foreach (var v in g.PlayerPointers()) { ulong con = m2.ReadPtr(v + 0xA8); if (con > 0x10000) { contractVt = m2.ReadPtr(con); break; } }
    bool IsCon(ulong q) => ProcessMemory.IsObjPtr(q) && m2.TryRead(q, new byte[8]) && m2.ReadPtr(q) == contractVt;
    ulong rec = ulong.Parse(args[1], System.Globalization.NumberStyles.HexNumber);
    ulong begin = rec; while (IsCon(m2.ReadPtr(begin - 0x48))) begin -= 0x48;
    ulong end = rec; while (IsCon(m2.ReadPtr(end))) end += 0x48;
    Console.WriteLine($"array {begin:X}..{end:X} = {(end - begin) / 0x48} records");
    for (ulong a = begin - 0x90; a < begin + 0x10; a += 8) Console.WriteLine($"  {a:X}: {m2.ReadPtr(a):X16}");
    for (ulong a = end - 0x10; a < end + 0x60; a += 8) Console.WriteLine($"  {a:X}: {m2.ReadPtr(a):X16}");
    var near = new HashSet<ulong>(); for (ulong a = begin - 0x100; a <= begin + 0x48; a += 8) near.Add(a);
    foreach (var (at, val) in m2.ScanPointersToAny(near, privateOnly: false, max: 50)) Console.WriteLine($"  ref {at:X} -> {val:X} (begin{(long)val - (long)begin:+#;-#;0})  +8={m2.ReadPtr(at + 8):X} +10={m2.ReadPtr(at + 16):X}");
    var level = new HashSet<ulong> { begin };
    for (int depth = 0; depth < 4; depth++)
    {
        var hits = m2.ScanPointersToAny(level, privateOnly: false, max: 200);
        Console.WriteLine($"depth {depth}: {hits.Count} refs");
        var next = new HashSet<ulong>();
        foreach (var (at, val) in hits)
        {
            bool stat = at >= mod.Base && at < mod.Base + (ulong)mod.Size;
            Console.WriteLine($"   {at:X} -> {val:X} {(stat ? $"STATIC rva {at - mod.Base:X}" : "")}  +8={m2.ReadPtr(at + 8):X} +10={m2.ReadPtr(at + 16):X}");
            if (stat) continue;
            for (int k = 0; k <= 0x400; k += 8) { ulong q = m2.ReadPtr(at - (ulong)k); if (InMod(q)) { Console.WriteLine($"      owner {at - (ulong)k:X} vt {q - mod.Base:X} field +{k:X}"); next.Add(at - (ulong)k); break; } }
        }
        if (next.Count == 0) break; level = next;
    }
    return;
}
if (args.Length > 0 && args[0] == "offerhist")
{
    using var g = FmGame.Attach(Report.FindLayout());
    var m2 = g.Memory; var today = g.GameDate!.Value;
    ulong contractVt = 0;
    foreach (var v in g.PlayerPointers()) { ulong con = m2.ReadPtr(v + 0xA8); if (con > 0x10000) { contractVt = m2.ReadPtr(con); break; } }
    var all = m2.ScanPointersToAny(new HashSet<ulong> { contractVt }, privateOnly: true, max: 2_000_000).Select(h => h.At).ToList();
    var players = g.PlayerPointers().ToHashSet();
    var per = new Dictionary<ulong, int>(); var extra = new HashSet<ulong>();
    foreach (var con in all) { ulong person = m2.ReadPtr(con + 8); if (players.Contains(person) && m2.ReadPtr(person + 0xA8) != con) { per[person] = per.GetValueOrDefault(person) + 1; extra.Add(con); } }
    var hist = new SortedDictionary<string, int>(); var ex = new Dictionary<string, List<string>>();
    foreach (var (at, con) in m2.ScanPointersToAny(extra, privateOnly: true, max: 100000))
    {
        var r = m2.ReadBytes(at, 0x48); if (r == null) continue;
        ulong person = m2.ReadPtr(con + 8);
        var s = g.ReadDate(con + 0x44);
        string k = $"{(per[person] == 1 ? "single" : "multi ")} {(s > today ? "fut " : "past")} 2E={r[0x2E]:X2} 2F={r[0x2F]:X2} 30={r[0x30]:X2} 31={r[0x31]:X2} 1Cneg={(BitConverter.ToInt32(r, 0x1C) < 0 ? 1 : 0)} 8={(BitConverter.ToUInt64(r, 8) != 0 ? 1 : 0)}";
        hist[k] = hist.GetValueOrDefault(k) + 1;
        if (!ex.TryGetValue(k, out var l)) ex[k] = l = new();
        if (l.Count < 3) { var pl = g.ReadPlayer(person); ulong club = m2.ReadPtr(m2.ReadPtr(con + 0x10) + 0x30); l.Add($"{pl.Name} ({pl.Club}) -> {g.FmString(m2.ReadPtr(club + 0xC0))} {s}"); }
    }
    foreach (var (k, n) in hist) if (n >= 5) Console.WriteLine($"{n,6} {k}   e.g. {string.Join(" | ", ex[k])}");
    return;
}
if (args.Length > 0 && args[0] == "contractrefs2")
{
    using var g = FmGame.Attach(Report.FindLayout());
    var m2 = g.Memory; var today = g.GameDate!.Value;
    var mod = m2.Module("game_plugin.dll")!.Value;
    ulong contractVt = 0;
    foreach (var v in g.PlayerPointers()) { ulong con = m2.ReadPtr(v + 0xA8); if (con > 0x10000) { contractVt = m2.ReadPtr(con); break; } }
    var all = m2.ScanPointersToAny(new HashSet<ulong> { contractVt }, privateOnly: true, max: 2_000_000).Select(h => h.At).ToList();
    var players = g.PlayerPointers().ToHashSet();
    var names = args.Skip(1).ToArray();
    var want = new Dictionary<ulong, string>();
    foreach (var con in all)
    {
        ulong person = m2.ReadPtr(con + 8);
        if (!players.Contains(person) || m2.ReadPtr(person + 0xA8) == con) continue;
        var pl = g.ReadPlayer(person); if (!names.Any(n => pl.Name.Contains(n))) continue;
        ulong team = m2.ReadPtr(con + 0x10), club = m2.ReadPtr(team + 0x30);
        want[con] = $"{pl.Name} -> {g.FmString(m2.ReadPtr(club + 0xC0))}";
    }
    foreach (var (at, val) in m2.ScanPointersToAny(want.Keys.ToHashSet(), privateOnly: true, max: 1000))
    {
        Console.WriteLine($"{want[val]}  ref at {at:X}");
        for (int k = -0x40; k <= 0x40; k += 8)
        {
            ulong q = m2.ReadPtr((ulong)((long)at + k));
            string d = q >= mod.Base && q < mod.Base + (ulong)mod.Size ? $" vt {q - mod.Base:X}" : "";
            if (ProcessMemory.IsObjPtr(q)) { ulong vt = m2.ReadPtr(q); if (vt >= mod.Base && vt < mod.Base + (ulong)mod.Size) d += $" -> obj vt {vt - mod.Base:X}"; }
            Console.WriteLine($"    {k,4}: {q:X16}{d}");
        }
    }
    return;
}
if (args.Length > 0 && args[0] == "contractrefs")
{
    using var g = FmGame.Attach(Report.FindLayout());
    var m2 = g.Memory; var today = g.GameDate!.Value;
    var mod = m2.Module("game_plugin.dll")!.Value;
    bool InMod(ulong q) => q >= mod.Base && q < mod.Base + (ulong)mod.Size;
    ulong contractVt = 0;
    foreach (var v in g.PlayerPointers()) { ulong con = m2.ReadPtr(v + 0xA8); if (con > 0x10000) { contractVt = m2.ReadPtr(con); break; } }
    var all = m2.ScanPointersToAny(new HashSet<ulong> { contractVt }, privateOnly: true, max: 2_000_000).Select(h => h.At).ToList();
    var players = g.PlayerPointers().ToHashSet();
    var kind = new Dictionary<ulong, string>();
    var per = new Dictionary<ulong, int>();
    foreach (var con in all) { ulong person = m2.ReadPtr(con + 8); if (players.Contains(person) && m2.ReadPtr(person + 0xA8) != con) per[person] = per.GetValueOrDefault(person) + 1; }
    foreach (var con in all)
    {
        ulong person = m2.ReadPtr(con + 8);
        if (!players.Contains(person)) continue;
        if (m2.ReadPtr(person + 0xA8) == con) kind[con] = "cur";
        else { var s = g.ReadDate(con + 0x44); kind[con] = (per[person] == 1 ? "single-" : "multi-") + (s > today ? "fut" : "past"); }
    }
    var refs = m2.ScanPointersToAny(kind.Keys.ToHashSet(), privateOnly: true, max: 5_000_000);
    Console.WriteLine($"{refs.Count} refs");
    var hist = new Dictionary<string, int>();
    foreach (var (at, val) in refs)
    {
        string owner = "?";
        for (int k = 0; k <= 0x200; k += 8) { ulong q = m2.ReadPtr(at - (ulong)k); if (InMod(q)) { owner = $"vt{q - mod.Base:X}+{k:X}"; break; } }
        string key = $"{kind[val],-12} {owner}";
        hist[key] = hist.GetValueOrDefault(key) + 1;
    }
    foreach (var (k, n) in hist.OrderBy(x => x.Key)) if (n >= 20) Console.WriteLine($"{n,7} {k}");
    return;
}
if (args.Length > 0 && args[0] == "contracthist")
{
    using var g = FmGame.Attach(Report.FindLayout());
    var m2 = g.Memory; var today = g.GameDate!.Value;
    ulong contractVt = 0;
    foreach (var v in g.PlayerPointers()) { ulong con = m2.ReadPtr(v + 0xA8); if (con > 0x10000) { contractVt = m2.ReadPtr(con); break; } }
    var all = m2.ScanPointersToAny(new HashSet<ulong> { contractVt }, privateOnly: true, max: 2_000_000).Select(h => h.At).ToList();
    var players = g.PlayerPointers().ToHashSet();
    var per = new Dictionary<ulong, List<ulong>>();
    foreach (var con in all)
    {
        ulong person = m2.ReadPtr(con + 8);
        if (!players.Contains(person) || m2.ReadPtr(person + 0xA8) == con) continue;
        if (!per.TryGetValue(person, out var l)) per[person] = l = new();
        l.Add(con);
    }
    var hist = new SortedDictionary<string, int>();
    foreach (var (person, l) in per)
        foreach (var con in l)
        {
            var start = g.ReadDate(con + 0x44); string when = start == null ? "none" : start > today ? "fut" : "past";
            string k = $"{(l.Count == 1 ? "single" : "multi ")} {when} 5A={m2.Read<byte>(con + 0x5A):X2} 59={m2.Read<byte>(con + 0x59):X2} 5B={m2.Read<byte>(con + 0x5B):X2} 54={m2.Read<byte>(con + 0x54):X2}";
            hist[k] = hist.GetValueOrDefault(k) + 1;
        }
    foreach (var (k, n) in hist) if (n >= 15) Console.WriteLine($"{n,6} {k}");
    Console.WriteLine($"players with extras {per.Count}, single {per.Count(x => x.Value.Count == 1)}");
    return;
}
if (args.Length > 0 && args[0] == "contractdump")
{
    using var g = FmGame.Attach(Report.FindLayout());
    var m2 = g.Memory; var today = g.GameDate!.Value;
    ulong contractVt = 0;
    foreach (var v in g.PlayerPointers()) { ulong con = m2.ReadPtr(v + 0xA8); if (con > 0x10000) { contractVt = m2.ReadPtr(con); break; } }
    var all = m2.ScanPointersToAny(new HashSet<ulong> { contractVt }, privateOnly: true, max: 2_000_000).Select(h => h.At).ToList();
    var names = args.Skip(1).ToHashSet();
    var players = g.PlayerPointers().ToHashSet();
    foreach (var con in all)
    {
        ulong person = m2.ReadPtr(con + 8);
        if (!players.Contains(person)) continue;
        var pl = g.ReadPlayer(person);
        if (!names.Any(n => pl.Name.Contains(n))) continue;
        bool cur = m2.ReadPtr(person + 0xA8) == con;
        ulong team = m2.ReadPtr(con + 0x10), club = team > 0x10000 ? m2.ReadPtr(team + 0x30) : 0;
        var b = m2.ReadBytes(con, 0x90);
        Console.WriteLine($"{pl.Name} {(cur ? "CUR" : "xtra")} -> {g.FmString(m2.ReadPtr(club + 0xC0))}");
        for (int i = 0x18; i < 0x90; i += 0x10) Console.WriteLine($"   {i:X2}: {Convert.ToHexString(b, i, Math.Min(16, 0x90 - i))}");
    }
    return;
}
if (args.Length > 0 && args[0] == "extracontracts")
{
    using var g = FmGame.Attach(Report.FindLayout());
    var m2 = g.Memory; var today = g.GameDate!.Value;
    ulong contractVt = 0;
    foreach (var v in g.PlayerPointers()) { ulong con = m2.ReadPtr(v + 0xA8); if (con > 0x10000) { contractVt = m2.ReadPtr(con); break; } }
    var all = m2.ScanPointersToAny(new HashSet<ulong> { contractVt }, privateOnly: true, max: 2_000_000).Select(h => h.At).ToList();
    Console.WriteLine($"{all.Count} contract objects");
    var players = g.PlayerPointers().ToHashSet();
    int extra = 0; var byStart = new Dictionary<string, int>();
    foreach (var con in all)
    {
        ulong person = m2.ReadPtr(con + 8);
        if (!players.Contains(person)) continue;
        if (m2.ReadPtr(person + 0xA8) == con) continue;
        extra++;
        var start = g.ReadDate(con + 0x44); var key = start == null ? "none" : start > today ? "future" : "past";
        byStart[key] = byStart.GetValueOrDefault(key) + 1;
        if (extra <= 20)
        {
            var pl = g.ReadPlayer(person);
            ulong team = m2.ReadPtr(con + 0x10), club = team > 0x10000 ? m2.ReadPtr(team + 0x30) : 0;
            Console.WriteLine($"  {pl.Name,-26} CA {pl.CurrentAbility,3} now {pl.Club,-26} -> {g.FmString(m2.ReadPtr(club + 0xC0)),-26} start {start} exp {g.ReadDate(con + 0x48)} wage {m2.Read<uint>(con + 0x20)} job {m2.Read<byte>(con + 0x26)} st {m2.Read<byte>(con + 0x54)}");
        }
    }
    Console.WriteLine($"extra contracts on players: {extra}; by start date: {string.Join(", ", byStart.Select(k => $"{k.Key} {k.Value}"))}");
    return;
}
if (args.Length > 0 && args[0] == "futurecontract")
{
    using var g = FmGame.Attach(Report.FindLayout());
    var m2 = g.Memory; var counts = new Dictionary<int, int>(); var examples = new Dictionary<int, List<string>>();
    ulong contractVt = 0;
    foreach (var v in g.PlayerPointers())
    {
        ulong con = m2.ReadPtr(v + 0xA8); if (con < 0x10000) continue;
        if (contractVt == 0) contractVt = m2.ReadPtr(con);
        var pr = m2.ReadBytes(v, 0x200); if (pr == null) continue;
        for (int off = 0; off < 0x200; off += 8)
        {
            if (off == 0xA8) continue;
            ulong q = BitConverter.ToUInt64(pr, off);
            if (q < 0x10000 || (q & 7) != 0) continue;
            if (m2.ReadPtr(q) != contractVt) continue;
            counts[off] = counts.GetValueOrDefault(off) + 1;
            if (!examples.TryGetValue(off, out var l)) examples[off] = l = new();
            if (l.Count < 8)
            {
                var pl = g.ReadPlayer(v);
                ulong team = m2.ReadPtr(q + 0x10), club = team > 0x10000 ? m2.ReadPtr(team + 0x30) : 0;
                l.Add($"{pl.Name} (now {pl.Club}) -> {g.FmString(m2.ReadPtr(club + 0xC0))} start {g.ReadDate(q + 0x44)} wage {m2.Read<uint>(q + 0x20)}");
            }
        }
    }
    Console.WriteLine($"contract vtable 0x{contractVt:X}");
    foreach (var (off, n) in counts.OrderBy(k => k.Key)) { Console.WriteLine($"person+0x{off:X}: {n} players"); foreach (var s in examples[off]) Console.WriteLine("    " + s); }
    return;
}
if (args.Length > 0 && args[0] == "rel74")
{
    using var g = FmGame.Attach(Report.FindLayout());
    var m2 = g.Memory; int code = int.Parse(args[1]); int shown = 0, same = 0, total = 0, expSoon = 0;
    foreach (var v in g.PlayerPointers())
    {
        ulong obj = m2.ReadPtr(v + 0x78); if (obj < 0x10000) continue;
        ulong b = m2.ReadPtr(obj), e = m2.ReadPtr(obj + 8); long n = (long)(e - b) / 16; if (n <= 0 || n > 400) continue;
        var bytes = m2.ReadBytes(b, (int)n * 16); if (bytes == null) continue;
        for (int i = 0; i < n; i++)
        {
            if (bytes[i * 16 + 0xA] != 1 || bytes[i * 16 + 0xB] != code) continue;
            var p = g.ReadPlayer(v); ulong t = BitConverter.ToUInt64(bytes, i * 16);
            string target = g.FmString(m2.ReadPtr(t + 0xC0)) ?? "?";
            total++; if (target == p.Club) same++; if (p.ContractExpiry is { } x && x <= new DateOnly(2037, 6, 30)) expSoon++;
            if (shown++ < 25) Console.WriteLine($"  {p.Name,-26} CA {p.CurrentAbility,3} now: {p.Club,-28} -> {target,-28} exp {p.ContractExpiry} raw {BitConverter.ToString(bytes, i * 16 + 8, 8)}");
        }
    }
    Console.WriteLine($"total {total}, target == current club {same}, contract ending by 30/6/2037 {expSoon}");
    return;
}
if (args.Length > 0 && args[0] == "relhist")
{
    using var g = FmGame.Attach(Report.FindLayout());
    var m2 = g.Memory; var hist = new Dictionary<(int kind, int rel), int>(); var ex = new Dictionary<(int, int), string>();
    foreach (var v in g.PlayerPointers().Take(150000))
    {
        ulong obj = m2.ReadPtr(v + 0x78); if (obj < 0x10000) continue;
        ulong b = m2.ReadPtr(obj), e = m2.ReadPtr(obj + 8); long n = (long)(e - b) / 16; if (n <= 0 || n > 400) continue;
        var bytes = m2.ReadBytes(b, (int)n * 16); if (bytes == null) continue;
        var seen = new HashSet<(int, int)>();
        for (int i = 0; i < n; i++)
        {
            var k = ((int)bytes[i * 16 + 0xA], (int)bytes[i * 16 + 0xB]);
            if (!seen.Add(k)) continue;
            hist[k] = hist.GetValueOrDefault(k) + 1;
            if (!ex.ContainsKey(k) && k.Item1 == 1) { ulong t = BitConverter.ToUInt64(bytes, i * 16); ex[k] = $"{Analysis.Name(m2, v)} -> {g.FmString(m2.ReadPtr(t + 0xC0))} (b8-9: {bytes[i * 16 + 8]} {bytes[i * 16 + 9]} str {bytes[i * 16 + 0xC]})"; }
        }
    }
    foreach (var kv in hist.OrderBy(k => k.Key)) Console.WriteLine($"kind {kv.Key.kind,3} rel {kv.Key.rel,3}: {kv.Value,7} players   {ex.GetValueOrDefault(kv.Key)}");
    return;
}
if (args.Length > 0 && args[0] == "futurejoins")
{
    using var g = FmGame.Attach(Report.FindLayout());
    var today = g.GameDate!.Value;
    var ps = g.PlayerPointers().Select(g.ReadPlayer).Where(p => p.ContractStart > today).ToList();
    Console.WriteLine($"{ps.Count} players with a contract starting after {today}");
    foreach (var p in ps.OrderByDescending(p => p.CurrentAbility).Take(15))
        Console.WriteLine($"  {p.Name,-26} club now: {p.Club,-26} starts {p.ContractStart} expires {p.ContractExpiry} prev(+0x108): {p.LinkedClub}");
    return;
}
if (args.Length > 0 && args[0] == "staffsolve") { using var g = FmGame.Attach(Report.FindLayout()); StaffSolve.Run(g); return; }
if (args.Length > 0 && args[0] == "staffpick")
{
    using var g = FmGame.Attach(Report.FindLayout());
    var m2 = g.Memory; var gp = m2.Module("game_plugin.dll")!.Value; ulong vt = gp.Base + 0x4D8DE78;
    string Club(ulong v) { ulong c = m2.ReadPtr(v + 0x108); return c < 0x10000 ? "" : g.FmString(m2.ReadPtr(c + 0xC0)) ?? ""; }
    var staff = g.PersonPointers().Where(v => m2.ReadPtr(v) == vt).Select(v => (v, b: m2.ReadBytes(v + 0x150, 52)!, club: Club(v)))
        .Where(t => t.b != null && t.club.Length > 0).ToList();
    // greedy: pick people who set new extremes (max or min) on the most bytes
    var picked = new List<(ulong v, byte[] b, string club)>();
    var lo = Enumerable.Repeat(255, 52).ToArray(); var hi = new int[52];
    for (int k = 0; k < 6; k++)
    {
        var best = staff.Where(s => !picked.Contains(s)).MaxBy(s => Enumerable.Range(0, 52).Sum(i => Math.Max(0, s.b[i] - hi[i]) + Math.Max(0, lo[i] - s.b[i])));
        picked.Add(best);
        for (int i = 0; i < 52; i++) { hi[i] = Math.Max(hi[i], best.b[i]); lo[i] = Math.Min(lo[i], best.b[i]); }
    }
    foreach (var s in picked) Console.WriteLine($"{Analysis.Name(m2, s.v)} | {s.club} | dob {Analysis.FmDate(m2, s.v + 0x88):yyyy} | sum1-20 {s.b.Take(28).Sum(x => x)} | {string.Join(" ", s.b)}");
    Console.WriteLine("--- highest totals at well-known clubs:");
    foreach (var s in staff.Where(s => new[] { "Manchester City", "Real Madrid C.F.", "F.C. Barcelona", "Liverpool", "Arsenal", "FC Bayern München", "Chelsea", "Paris Saint-Germain" }.Contains(s.club)).OrderByDescending(s => s.b.Take(28).Sum(x => x)).Take(8))
        Console.WriteLine($"{Analysis.Name(m2, s.v)} | {s.club} | sum {s.b.Take(28).Sum(x => x)}");
    return;
}
if (args.Length > 0 && args[0] == "status")
{
    using var g = FmGame.Attach(Report.FindLayout());
    var m2 = g.Memory;
    var ps = g.PlayerPointers().Select(v => (p: g.ReadPlayer(v), con: m2.ReadPtr(v + 0xA8))).Where(t => t.con > 0x10000).ToList();
    foreach (var grp in ps.GroupBy(t => m2.Read<byte>(t.con + 0x54)).OrderBy(x => x.Key))
    {
        var l = grp.ToList();
        Console.WriteLine($"status {grp.Key,3}: n={l.Count,6} avgAge {l.Average(t => 2037 - (t.p.DateOfBirth?.Year ?? 2037)),5:F1} avgCA {l.Average(t => t.p.CurrentAbility),5:F0}  e.g. {string.Join(", ", l.OrderByDescending(t => t.p.CurrentAbility).Take(3).Select(t => t.p.Name + " (" + t.p.Club + ")"))}");
    }
    return;
}
if (args.Length > 0 && args[0] == "pcblock")
{
    using var g = FmGame.Attach(Report.FindLayout());
    var m2 = g.Memory; var gp = m2.Module("game_plugin.dll")!.Value; ulong vt = gp.Base + Explore.Hex(args[1]);
    foreach (var v in g.PersonPointers().Where(v => m2.ReadPtr(v) == vt).Where(v => args.Skip(2).Any(n => Analysis.Name(m2, v).Contains(n))).Take(8))
    {
        foreach (long posOff in new long[] { -0x230, 0x290 })
        {
            ulong pos = (ulong)((long)v + posOff);
            var pr = m2.ReadBytes(pos, 15)!;
            Console.WriteLine($"{Analysis.Name(m2, v),-28} pos@{posOff:+#;-#} [{string.Join(" ", pr)}] height {m2.Read<byte>(pos + 0xDE)} CA {m2.Read<short>(pos + 0x114)} PA {m2.Read<short>(pos + 0x116)} dob {Analysis.FmDate(m2, v + 0x88):yyyy-MM-dd}");
        }
    }
    return;
}
if (args.Length > 0 && args[0] == "findblock")
{
    using var g = FmGame.Attach(Report.FindLayout());
    var m2 = g.Memory; var gp = m2.Module("game_plugin.dll")!.Value; ulong vt = gp.Base + Explore.Hex(args[1]);
    var vs = g.PersonPointers().Where(v => m2.ReadPtr(v) == vt).Take(300).ToList();
    const int R = 0x1000;
    var bufs = vs.Select(v => m2.ReadBytes(v - R, 2 * R)).Where(b => b != null).ToList();
    for (int k = 0; k < 2 * R - 15; k++)
    {
        int ok = 0;
        foreach (var b in bufs)
        {
            bool good = true; int mx = 0;
            for (int j = 0; j < 15 && good; j++) { int x = b![k + j]; if (x < 1 || x > 20) good = false; mx = Math.Max(mx, x); }
            if (good && mx == 20) ok++;
        }
        if (ok > bufs.Count * 0.8) Console.WriteLine($"positions candidate at V{k - R:+#;-#;0} (0x{Math.Abs(k - R):X}) : {ok}/{bufs.Count}");
    }
    return;
}
if (args.Length > 0 && args[0] == "classcheck")
{
    using var g = FmGame.Attach(Report.FindLayout());
    var m2 = g.Memory; var gp = m2.Module("game_plugin.dll")!.Value; ulong vt = gp.Base + Explore.Hex(args[1]);
    var ps = g.PersonPointers().Where(v => m2.ReadPtr(v) == vt).Select(g.ReadPlayer).ToList();
    Console.WriteLine($"{ps.Count}: CA 1-200 {ps.Count(p => p.CurrentAbility is > 0 and <= 200)}, PA>=CA {ps.Count(p => p.PotentialAbility >= p.CurrentAbility)}, height 150-210 {ps.Count(p => p.HeightCm is >= 150 and <= 210)}, has contract {ps.Count(p => p.Club != null)}, female {ps.Count(p => p.IsFemale)}");
    foreach (var p in ps.OrderByDescending(p => p.CurrentAbility).Take(6)) Console.WriteLine($"  {p.Name} {p.DateOfBirth} CA {p.CurrentAbility} PA {p.PotentialAbility} {p.HeightCm}cm {p.Club} pos {string.Join(" ", p.Positions)}");
    return;
}
if (args.Length > 0 && args[0] == "rep")
{
    using var g = FmGame.Attach(Report.FindLayout());
    var ps = g.PlayerPointers().Select(g.ReadPlayer).ToList();
    var pick = ps.Where(p => args.Skip(1).Any(n => (p.Club ?? "") == n)).OrderByDescending(p => p.CurrentAbility).Take(40).ToList();
    foreach (var p in pick)
    {
        var b = g.Memory.ReadBytes(p.Address - 0x60, 0x3C)!;
        var u = Enumerable.Range(0, 0x3C / 2).Select(i => BitConverter.ToUInt16(b, i * 2).ToString().PadLeft(5));
        Console.WriteLine($"{p.Name,-24}{p.Club,-18} CA{p.CurrentAbility,4} {string.Join("", u)}");
    }
    return;
}
if (args.Length > 0 && args[0] == "ofclass")
{
    using var g = FmGame.Attach(Report.FindLayout());
    var m2 = g.Memory; var gp = m2.Module("game_plugin.dll")!.Value; ulong vt = gp.Base + Explore.Hex(args[1]);
    foreach (var v in g.PersonPointers().Where(v => m2.ReadPtr(v) == vt).Take(int.Parse(args[2])))
        Console.WriteLine($"0x{v:X} {Analysis.Name(m2, v)}");
    return;
}
if (args.Length > 0 && args[0] == "classes")
{
    using var g = FmGame.Attach(Report.FindLayout());
    var m2 = g.Memory; var gp = m2.Module("game_plugin.dll")!.Value;
    foreach (var grp in g.PersonPointers().GroupBy(v => m2.ReadPtr(v)).OrderByDescending(x => x.Count()))
    {
        var ex = grp.Take(3).Select(v => Analysis.Name(m2, v));
        Console.WriteLine($"vt rva 0x{grp.Key - gp.Base:X} x{grp.Count()}: {string.Join(" | ", ex)}");
    }
    return;
}
if (args.Length > 0 && args[0] == "uniquestaff")
{
    using var g = FmGame.Attach(Report.FindLayout());
    var m2 = g.Memory; var gp = m2.Module("game_plugin.dll")!.Value;
    var staffVt = gp.Base + 0x4D8DE78;
    var staff = g.PersonPointers().Where(v => m2.ReadPtr(v) == staffVt).Select(v => (v, b: m2.ReadBytes(v, 0x240))).Where(t => t.b != null).ToList();
    Console.WriteLine($"{staff.Count} staff");
    var clubName = (ulong v) => { ulong c = m2.ReadPtr(v + 0x108); return c < 0x10000 ? "-" : g.FmString(m2.ReadPtr(c + 0xC0)); };
    for (int i = 0; i < 0x240; i++) for (int bit = 0; bit < 8; bit++)
    {
        var set = staff.Where(t => (t.b![i] >> bit & 1) == 1).Take(4).ToList();
        if (set.Count is >= 1 and <= 2)
            Console.WriteLine($"+0x{i:X3} bit {bit}: " + string.Join("; ", set.Select(t => $"{Analysis.Name(m2, t.v)} ({clubName(t.v)})")));
    }
    return;
}
if (args.Length > 0 && args[0] == "uniqueclub")
{
    using var g = FmGame.Attach(Report.FindLayout());
    var L = g.Layout; var m2 = g.Memory; ulong target = Explore.Hex(args[1]);
    var clubs = new HashSet<ulong>();
    foreach (var v in g.PlayerPointers())
    {
        ulong con = m2.ReadPtr(v + (ulong)L.Person.Contract.Value); if (con < 0x10000) continue;
        ulong team = m2.ReadPtr(con + (ulong)L.Contract.Team.Value); if (team < 0x10000) continue;
        ulong club = m2.ReadPtr(team + (ulong)L.Team.Club.Value); if (club > 0x10000) clubs.Add(club);
    }
    const int N = 0x600;
    var all = clubs.Select(c => (c, b: m2.ReadBytes(c, N))).Where(t => t.b != null).ToList();
    var tb = m2.ReadBytes(target, N)!;
    Console.WriteLine($"{all.Count} clubs");
    for (int i = 0; i < N; i++) for (int bit = 0; bit < 8; bit++)
    {
        int tv = tb[i] >> bit & 1;
        int same = all.Count(t => (t.b![i] >> bit & 1) == tv);
        if (same <= 3) Console.WriteLine($"+0x{i:X3} bit {bit} = {tv}; only {same} clubs share it");
    }
    return;
}
if (args.Length > 0 && args[0] == "humans")
{
    using var g = FmGame.Attach(Report.FindLayout());
    var m2 = g.Memory; ulong club = Explore.Hex(args[1]);
    var gp = m2.Module("game_plugin.dll")!.Value;
    bool InMod(ulong x) => x >= gp.Base && x < gp.Base + (ulong)gp.Size;
    // 1) people whose first 0x240 bytes contain a pointer to the club and that are not players
    var persons = g.PersonPointers();
    var hits = new List<ulong>();
    foreach (var v in persons)
    {
        if (g.IsPlayer(v)) continue;
        var b = m2.ReadBytes(v, 0x240); if (b == null) continue;
        for (int i = 0; i < 0x240; i += 8) if (BitConverter.ToUInt64(b, i) == club) { hits.Add(v); Console.WriteLine($"person 0x{v:X} vt rva 0x{m2.ReadPtr(v) - gp.Base:X} {Analysis.Name(m2, v)} club@+0x{i:X}"); break; }
    }
    // 2) static module pointers to any of those people
    if (hits.Count > 0)
        foreach (var (at, val) in m2.ScanPointersToAny(hits.ToHashSet(), privateOnly: false).Where(h => InMod(h.At)))
            Console.WriteLine($"STATIC rva 0x{at - gp.Base:X} -> 0x{val:X}");
    return;
}
if (args.Length > 0 && args[0] == "clubdump")
{
    using var g = FmGame.Attach(Report.FindLayout());
    var L = g.Layout; var m2 = g.Memory;
    var wanted = args.Skip(1).ToArray();
    var seen = new Dictionary<string, ulong>();
    foreach (var v in g.PlayerPointers())
    {
        ulong con = m2.ReadPtr(v + (ulong)L.Person.Contract.Value); if (con < 0x10000) continue;
        ulong team = m2.ReadPtr(con + (ulong)L.Contract.Team.Value); if (team < 0x10000) continue;
        ulong club = m2.ReadPtr(team + (ulong)L.Team.Club.Value); if (club < 0x10000) continue;
        var name = g.FmString(m2.ReadPtr(club + (ulong)L.Club.Name.Value)) ?? "";
        foreach (var w in wanted) if (name == w && !seen.ContainsKey(w)) seen[w] = club;
        if (seen.Count == wanted.Length) break;
    }
    var bufs = wanted.Where(seen.ContainsKey).Select(w => (w, m2.ReadBytes(seen[w], 0x400)!)).ToList();
    foreach (var (w, _) in bufs) Console.WriteLine($"{w} @0x{seen[w]:X}");
    for (int i = 0; i < 0x400; i += 2)
    {
        var vals = bufs.Select(b => BitConverter.ToUInt16(b.Item2, i)).ToArray();
        if (vals.All(x => x <= 10000) && vals.Distinct().Count() >= bufs.Count - 1 && vals.Max() > 500)
            Console.WriteLine($"+0x{i:X3} u16: " + string.Join(" ", vals));
    }
    return;
}
if (args.Length > 0 && args[0] == "clubs")
{
    using var g = FmGame.Attach(Report.FindLayout());
    var ps = g.PlayerPointers().Select(g.ReadPlayer).ToList();
    foreach (var grp in ps.GroupBy(p => p.Competition ?? "-").OrderByDescending(x => x.Count()).Where(x => args.Length < 2 || x.Key.Contains(args[1], StringComparison.OrdinalIgnoreCase)).Take(40))
        Console.WriteLine($"{grp.Count(),6} {grp.Key}");
    return;
}
if (args.Length > 0 && args[0] == "gender")
{
    using var g = FmGame.Attach(Report.FindLayout());
    var ps = g.PlayerPointers().Select(g.ReadPlayer).ToList();
    bool W(Player p) => (p.Competition ?? "").Contains("Wom", StringComparison.OrdinalIgnoreCase) || (p.Competition ?? "").Contains("Femin", StringComparison.OrdinalIgnoreCase);
    var women = ps.Where(W).ToList(); var men = ps.Where(p => !W(p) && p.Competition != null).ToList();
    Console.WriteLine($"{women.Count} women, {men.Count} men");
    foreach (var w in women.Take(8)) Console.WriteLine($"  {w.Name} {w.Club} {w.DateOfBirth}");
    const int lo = -0x288, hi = 0x140;
    byte[] Rd(Player p) => g.Memory.ReadBytes(p.Address - 0x288, hi - lo)!;
    var wb = women.Select(Rd).ToList(); var mb = men.Select(Rd).ToList();
    for (int i = 0; i < hi - lo; i++)
    {
        var wv = wb.GroupBy(b => b[i]).OrderByDescending(x => x.Count()).First();
        if (wv.Count() < women.Count * 0.9) continue;
        double menSame = mb.Count(b => b[i] == wv.Key) / (double)mb.Count;
        if (menSame < 0.05) Console.WriteLine($"offset {i + lo:X} (signed {i + lo}) women={wv.Key} ({wv.Count()}/{women.Count}) men-same={menSame:P2}");
    }
    // bit-level
    for (int i = 0; i < hi - lo; i++) for (int bit = 0; bit < 8; bit++)
    {
        double wf = wb.Count(b => (b[i] >> bit & 1) == 1) / (double)wb.Count, mf = mb.Count(b => (b[i] >> bit & 1) == 1) / (double)mb.Count;
        if (Math.Abs(wf - mf) > 0.9) Console.WriteLine($"bit offset {i + lo} bit {bit}: women {wf:P0} men {mf:P1}");
    }
    return;
}
if (args.Length == 0 || args[0] == "report")
{
    Report.Run(args.Length > 1 ? args[1..] : ["Haaland", "Bellingham", "Lamine Yamal", "Saka", "Verbruggen"]);
    return;
}

// ---- exploration commands used to reverse the layout (scan-based, slow) ----
using var m = ProcessMemory.Attach("fm");
if (Environment.GetEnvironmentVariable("FM_BASE") is string fb) Analysis.Base = Explore.Hex(fb);
var cmd = args.Length > 0 ? args[0] : "help";
switch (cmd)
{
    case "find": Explore.Find(m, args[1], args.Length > 2 ? int.Parse(args[2]) : 50); break;
    case "refs": Explore.Refs(m, Explore.Hex(args[1]), args.Length > 2 ? int.Parse(args[2]) : 0, 50); break;
    case "refsany":
        {
            var set = new HashSet<ulong>();
            int back = int.Parse(args[1]);
            foreach (var h in args.Skip(2)) { var t = Explore.Hex(h); for (int k = 0; k <= back; k++) set.Add(t - (ulong)k); }
            foreach (var (at, v) in m.ScanPointersToAny(set, privateOnly: false))
                Console.WriteLine($"0x{at:X} -> 0x{v:X}");
            break;
        }
    case "count":
        {
            var set = new HashSet<ulong> { Explore.Hex(args[1]) };
            var hits = m.ScanPointersToAny(set, privateOnly: true, max: 2_000_000);
            Console.WriteLine($"{hits.Count} qwords == 0x{args[1]}");
            foreach (var h in hits.Take(int.Parse(args.Length > 2 ? args[2] : "10"))) Console.WriteLine($"  0x{h.At:X}");
            break;
        }
    case "u32":
        {
            var pat = BitConverter.GetBytes(uint.Parse(args[1]));
            foreach (var a in m.Scan(pat, null, privateOnly: true, alignment: 4, max: 200)) Console.WriteLine($"0x{a:X}");
            break;
        }
    case "follow":
        {
            ulong o = Explore.Hex(args[1]); int len = Convert.ToInt32(args[2], 16); int dl = args.Length > 3 ? int.Parse(args[3]) : 128;
            for (int off = 0; off < len; off += 8)
            {
                ulong q = m.ReadPtr(o + (ulong)off);
                if (q < 0x10000000000 || q > 0x7FF000000000) continue; // heap only
                Console.WriteLine($"=== +0x{off:X} -> 0x{q:X}");
                Explore.Dump(m, q, dl);
            }
            break;
        }
    case "sample": Analysis.Sample(m, Explore.Hex(args[1]), int.Parse(args[2])); break;
    case "byname": Analysis.ByName(m, args[1]); break;
    case "stats": Analysis.Stats(m, Convert.ToInt32(args[1], 16)); break;
    case "obj":
        foreach (var h in args.Skip(1))
        {
            var o = Explore.Hex(h);
            Console.WriteLine($"0x{o:X} vt=0x{m.ReadPtr(o):X} id={m.Read<uint>(o + 0xC)} {Analysis.Name(m, o)} | {Analysis.FmDate(m, o + 0x88):yyyy-MM-dd}");
        }
        break;
    case "who":
        {
            var name = System.Text.Encoding.UTF8.GetBytes(args[1]);
            var pat = BitConverter.GetBytes((uint)name.Length).Concat(name).Append((byte)0).ToArray();
            var strs = m.Scan(pat, null, privateOnly: false, alignment: 4).ToHashSet();
            Console.WriteLine($"{strs.Count} FMStr hits");
            var entries = m.ScanPointersToAny(strs, privateOnly: false).Select(h => h.At).ToHashSet();
            Console.WriteLine($"{entries.Count} entries");
            foreach (var (at, v) in m.ScanPointersToAny(entries, privateOnly: false))
            {
                foreach (int back in new[] { 0x58, 0x28 })
                {
                    ulong o = at - (ulong)back; ulong vt = m.ReadPtr(o);
                    if (vt >> 40 == 0x7F)
                        Console.WriteLine($"holder 0x{at:X} back 0x{back:X}: obj 0x{o:X} vt 0x{vt:X} {Analysis.Name(m, at - 0x58)}");
                }
            }
            break;
        }
    case "strings": Analysis.Strings(m, Explore.Hex(args[1]), int.Parse(args[2]), Convert.ToInt32(args[3], 16)); break;
    case "fieldscan": Analysis.FieldScan(m, Convert.ToInt32(args[1], 16), args.Skip(2).Select(Explore.Hex).ToArray()); break;
    case "corr": Analysis.Corr(m, 0x3C8, Convert.ToInt32(args[1], 16), Convert.ToInt32(args[2], 16)); break;
    case "attrcorr": Analysis.AttrCorr(m); break;
    case "arrays":
        {
            ulong vt = Explore.Hex(args[1]);
            foreach (var line in File.ReadAllLines(args[2]))
            {
                ulong at = Explore.Hex(line.Split(' ')[0]);
                ulong lo = at, hi = at;
                var gp = m.Module("game_plugin.dll")!.Value;
                bool IsObj(ulong a) { ulong t = m.ReadPtr(a); if (t < 0x10000000000) return false; ulong v = m.ReadPtr(t); return v >= gp.Base && v < gp.Base + (ulong)gp.Size; }
                while (IsObj(lo - 8)) lo -= 8;
                while (IsObj(hi + 8)) hi += 8;
                int players = 0; for (ulong a = lo; a <= hi; a += 8) if (m.ReadPtr(m.ReadPtr(a)) == vt) players++;
                long run = (long)((hi - lo) / 8 + 1);
                if (run >= 1000) Console.WriteLine($"holder 0x{at:X}: run of {run} obj ptrs ({players} players) from 0x{lo:X}; header qwords before: 0x{m.ReadPtr(lo - 8):X} 0x{m.ReadPtr(lo - 16):X} 0x{m.ReadPtr(lo - 24):X}");
            }
            break;
        }
    case "dates":
        {
            var gp = m.Module("game_plugin.dll")!.Value; ushort yr = ushort.Parse(args[1]);
            var hist = new Dictionary<int, int>();
            foreach (var r in m.Regions(false).Where(r => r.Base >= gp.Base && r.Base < gp.Base + (ulong)gp.Size && (r.Protect & 0x04) != 0))
            {
                var b = m.ReadBytes(r.Base, (int)r.Size); if (b == null) continue;
                for (int i = 0; i + 4 <= b.Length; i += 4)
                    if (BitConverter.ToUInt16(b, i + 2) == yr) { int d = BitConverter.ToUInt16(b, i); Console.WriteLine($"rva 0x{r.Base + (ulong)i - gp.Base:X} day 0x{d:X4} ({d & 0x1FF})"); }
            }
            break;
        }
    case "ptr": Console.WriteLine($"{m.ReadPtr(Explore.Hex(args[1]) + Explore.Hex(args[2])):X}"); break;
    case "range":
        {
            ulong a = Explore.Hex(args[1]); int len = Convert.ToInt32(args[2], 16); long lo = long.Parse(args[3]), hi = long.Parse(args[4]);
            var b = m.ReadBytes(a, len)!;
            for (int i = 0; i + 4 <= len; i++) { uint v = BitConverter.ToUInt32(b, i); if (v >= lo && v <= hi) Console.WriteLine($"u32 @+0x{i:X} = {v}"); }
            for (int i = 0; i + 8 <= len; i += 4) { long v = BitConverter.ToInt64(b, i); if (v >= lo && v <= hi) Console.WriteLine($"i64 @+0x{i:X} = {v}"); }
            break;
        }
    case "dump": Explore.Dump(m, Explore.Hex(args[1]), args.Length > 2 ? Convert.ToInt32(args[2], args[2].StartsWith("0x") ? 16 : 10) : 256); break;
    case "modules":
        foreach (System.Diagnostics.ProcessModule mod in m.Process.Modules)
            Console.WriteLine($"0x{(ulong)mod.BaseAddress:X} {mod.ModuleMemorySize,10} {mod.ModuleName}");
        break;
    default:
        Console.WriteLine("usage: find <text> [max] | refs <hex> [back] | dump <hex> [len] | modules");
        break;
}




























































