using FMScout.Reader;

namespace FMScout.Probe;

static class Analysis
{
    public static ulong Base = 0;
    public static List<ulong> Objects(ProcessMemory m, ulong vtable)
        => m.ScanPointersToAny(new HashSet<ulong> { vtable }, privateOnly: false, max: 5_000_000).Select(h => h.At).ToList();

    public static string? FmString(ProcessMemory m, ulong p)
    {
        if (p < 0x10000) return null;
        uint len = m.Read<uint>(p);
        if (len == 0 || len > 200) return null;
        return m.ReadUtf8(p + 4, (int)len);
    }

    public static DateTime? FmDate(ProcessMemory m, ulong a)
    {
        ushort day = m.Read<ushort>(a), year = m.Read<ushort>(a + 2);
        if (year < 1900 || year > 2100 || day > 366) return null;
        return day == 0 ? null : new DateTime(year, 1, 1).AddDays(day - 1);
    }

    public static string Name(ProcessMemory m, ulong o)
    {
        string? first = FmString(m, m.ReadPtr(m.ReadPtr(o + 0x50)));
        string? last = FmString(m, m.ReadPtr(m.ReadPtr(o + 0x58)));
        string? common = FmString(m, m.ReadPtr(m.ReadPtr(o + 0x48)));
        string? full = FmString(m, m.ReadPtr(o + 0x40));
        return $"{first} {last}" + (common != null ? $" [common:{common}]" : "") + (full != null ? $" [full:{full}]" : "");
    }

    /// <summary>Find objects whose name matches, cache them in a list file for fast reuse.</summary>
    public static void Sample(ProcessMemory m, ulong vt, int n)
    {
        var objs = Objects(m, vt);
        Console.WriteLine($"{objs.Count} objects");
        foreach (var o in objs.Take(n))
            Console.WriteLine($"0x{o:X} {Name(m, o)} | {FmDate(m, o + 0x88):yyyy-MM-dd}");
        File.WriteAllLines(Path.Combine(Path.GetTempPath(), "fm_objs.txt"), objs.Select(o => o.ToString("X")));
    }

    public static List<ulong> Cached() => File.ReadAllLines(Path.Combine(Path.GetTempPath(), "fm_objs.txt")).Select(Explore.Hex).ToList();

    public static void ByName(ProcessMemory m, string needle)
    {
        foreach (var o in Cached())
        {
            var s = Name(m, o);
            if (s != null && s.Contains(needle, StringComparison.OrdinalIgnoreCase))
                Console.WriteLine($"0x{o:X} {s} | {FmDate(m, o + 0x88):yyyy-MM-dd}");
        }
    }

    /// <summary>Recursively walk pointers from an object and print every FM string reachable, with its path.</summary>
    public static void Strings(ProcessMemory m, ulong o, int depth, int size, string path = "", HashSet<ulong>? seen = null)
    {
        seen ??= new();
        if (!seen.Add(o)) return;
        var buf = m.ReadBytes(o, size);
        if (buf == null) return;
        for (int off = 0; off + 8 <= size; off += 8)
        {
            ulong q = BitConverter.ToUInt64(buf, off);
            if (q < 0x10000000000 || q > 0x7FF000000000) continue;
            var s = FmString(m, q);
            if (s != null && s.Length > 1 && s.All(ch => !char.IsControl(ch))) { Console.WriteLine($"{path}+0x{off:X} = \"{s}\""); continue; }
            if (depth > 0) Strings(m, q, depth - 1, size, $"{path}+0x{off:X}->", seen);
        }
    }

    public static void FieldScan(ProcessMemory m, int size, ulong[] stars)
    {
        var objs = Cached();
        var all = new List<byte[]>();
        foreach (var o in objs) { var b = m.ReadBytes(o - Base, size); if (b != null) all.Add(b); }
        var sb = stars.Select(s => m.ReadBytes(s - Base, size)!).ToArray();
        Console.WriteLine($"{all.Count} players read");
        Console.WriteLine("off  | byte: %1-20 %1-100 min max mean | s16: %1-200 %neg | stars(u8) | stars(s16)");
        for (int i = 0; i < size; i++)
        {
            int in20 = 0, in100 = 0, mn = 255, mx = 0; double sum = 0;
            foreach (var b in all) { int v = b[i]; if (v is >= 1 and <= 20) in20++; if (v is >= 1 and <= 100) in100++; mn = Math.Min(mn, v); mx = Math.Max(mx, v); sum += v; }
            int in200 = 0, neg = 0;
            if (i % 2 == 0 && i + 1 < size)
                foreach (var b in all) { short v = BitConverter.ToInt16(b, i); if (v is >= 1 and <= 200) in200++; if (v is < 0 and >= -200) neg++; }
            double n = all.Count;
            string su8 = string.Join(",", sb.Select(b => b[i]));
            string s16 = i % 2 == 0 ? string.Join(",", sb.Select(b => BitConverter.ToInt16(b, i))) : "";
            Console.WriteLine($"{i:X3} | {in20 / n,5:P0} {in100 / n,5:P0} {mn,3} {mx,3} {sum / n,6:F1} | {in200 / n,5:P0} {neg / n,5:P0} | {su8} | {s16}");
        }
    }

    public static void Corr(ProcessMemory m, int size, int aStart, int aEnd)
    {
        var all = new List<byte[]>();
        foreach (var o in Cached()) { var b = m.ReadBytes(o - Base, size); if (b != null) all.Add(b); }
        int n = all.Count;
        var y = all.Select(b => { double s = 0; for (int i = aStart; i < aEnd; i++) s += b[i]; return s / (aEnd - aStart); }).ToArray();
        Console.WriteLine($"attr mean range {y.Min():F1}..{y.Max():F1} avg {y.Average():F1}");
        double Pearson(Func<byte[], double> f)
        {
            var x = all.Select(f).ToArray();
            double mx = x.Average(), my = y.Average(), sxy = 0, sxx = 0, syy = 0;
            for (int i = 0; i < n; i++) { sxy += (x[i] - mx) * (y[i] - my); sxx += (x[i] - mx) * (x[i] - mx); syy += (y[i] - my) * (y[i] - my); }
            return sxx == 0 ? 0 : sxy / Math.Sqrt(sxx * syy);
        }
        var res = new List<(string, double)>();
        for (int i = 0; i < size; i++)
        {
            if (i >= aStart && i < aEnd) continue;
            int ii = i;
            res.Add(($"u8  +0x{i:X3}", Pearson(b => b[ii])));
            if (i % 2 == 0) res.Add(($"s16 +0x{i:X3}", Pearson(b => BitConverter.ToInt16(b, ii))));
        }
        foreach (var (k, r) in res.OrderByDescending(t => Math.Abs(t.Item2)).Take(25)) Console.WriteLine($"{k}  r={r:F3}");
    }

    public static void AttrCorr(ProcessMemory m)
    {
        const int size = 0x3C8, A = 0x15F, N = 54;
        var all = new List<byte[]>();
        foreach (var o in Cached()) { var b = m.ReadBytes(o - Base, size); if (b != null && BitConverter.ToInt16(b, 0x264) is > 0 and <= 200) all.Add(b); }
        int n = all.Count;
        double[] Col(Func<byte[], double> f) => all.Select(f).ToArray();
        double R(double[] x, double[] y)
        {
            double mx = x.Average(), my = y.Average(), sxy = 0, sxx = 0, syy = 0;
            for (int i = 0; i < x.Length; i++) { sxy += (x[i] - mx) * (y[i] - my); sxx += (x[i] - mx) * (x[i] - mx); syy += (y[i] - my) * (y[i] - my); }
            return sxx * syy == 0 ? 0 : sxy / Math.Sqrt(sxx * syy);
        }
        var attrs = Enumerable.Range(0, N).Select(i => Col(b => b[A + i])).ToArray();
        var refs = new (string, double[])[]
        {
            ("height", Col(b => b[0x22E])), ("CA", Col(b => BitConverter.ToInt16(b, 0x264))),
            ("posGK", Col(b => b[0x150])), ("posDC", Col(b => b[0x152])), ("posST", Col(b => b[0x15C])), ("posAMR", Col(b => b[0x15B])),
            ("age", Col(b => 2037 - BitConverter.ToUInt16(b, 0x288 + 0x8A))),
        };
        Console.WriteLine($"{n} players");
        Console.Write("idx off  mean  %x5 ");
        foreach (var (k, _) in refs) Console.Write($"{k,7}");
        Console.WriteLine("  best-partners");
        for (int i = 0; i < N; i++)
        {
            double mean = attrs[i].Average(); double m5 = attrs[i].Count(v => v % 5 == 0) / (double)n;
            Console.Write($"{i,3} {A + i:X3} {mean,5:F1} {m5,4:P0} ");
            foreach (var (_, r) in refs) Console.Write($"{R(attrs[i], r),7:F2}");
            var partners = Enumerable.Range(0, N).Where(j => j != i).Select(j => (j, r: R(attrs[i], attrs[j]))).OrderByDescending(t => t.r).Take(3);
            Console.WriteLine("  " + string.Join(" ", partners.Select(p => $"{p.j}:{p.r:F2}")));
        }
    }

    /// <summary>Per-offset byte statistics across all objects (object size given).</summary>
    public static void Stats(ProcessMemory m, int size)
    {
        var objs = Cached();
        var min = Enumerable.Repeat(255, size).ToArray();
        var max = new int[size];
        var distinct = new HashSet<byte>[size];
        for (int i = 0; i < size; i++) distinct[i] = new();
        var buf = new byte[size];
        foreach (var o in objs)
        {
            if (!m.TryRead(o, buf)) continue;
            for (int i = 0; i < size; i++) { min[i] = Math.Min(min[i], buf[i]); max[i] = Math.Max(max[i], buf[i]); distinct[i].Add(buf[i]); }
        }
        for (int i = 0; i < size; i++)
            Console.WriteLine($"+0x{i:X3} min {min[i],3} max {max[i],3} distinct {distinct[i].Count,3}");
    }
}







