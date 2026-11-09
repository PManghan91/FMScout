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



















