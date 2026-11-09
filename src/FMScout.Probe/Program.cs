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
































