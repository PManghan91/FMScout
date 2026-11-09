using System.Globalization;
using System.Text;
using FMScout.Reader;

namespace FMScout.Probe;

/// <summary>Interactive-style exploration commands used to reverse the layout against the live game.</summary>
static class Explore
{
    public static ulong Hex(string s) => ulong.Parse(s.StartsWith("0x") ? s[2..] : s, NumberStyles.HexNumber);

    public static void Find(ProcessMemory m, string text, int max)
    {
        foreach (var (enc, name) in new[] { (Encoding.UTF8, "utf8"), (Encoding.Unicode, "utf16") })
        {
            var pat = enc.GetBytes(text);
            int n = 0;
            foreach (var a in m.Scan(pat, privateOnly: false, max: max))
            {
                var ctx = m.ReadBytes(a - 16, 64 + 16) ?? [];
                Console.WriteLine($"{name} 0x{a:X}  {Printable(ctx)}");
                n++;
            }
            Console.WriteLine($"-- {name}: {n} hits");
        }
    }

    public static void Refs(ProcessMemory m, ulong target, int back, int max)
    {
        // pointers to target - k (k=0..back step 8 is too coarse for strings; use step 1 only for k<=back)
        for (int k = 0; k <= back; k += (back > 64 ? 8 : 1))
        {
            foreach (var a in m.ScanPointerTo(target - (ulong)k, privateOnly: false, max: max))
                Console.WriteLine($"ptr to target-{k}: at 0x{a:X}");
        }
    }

    public static void Dump(ProcessMemory m, ulong addr, int len)
    {
        var b = m.ReadBytes(addr, len);
        if (b == null) { Console.WriteLine("unreadable"); return; }
        for (int i = 0; i < len; i += 16)
        {
            var row = b.AsSpan(i, Math.Min(16, len - i));
            var hex = string.Join(" ", row.ToArray().Select(x => x.ToString("X2")));
            string ann = "";
            if (row.Length >= 16)
            {
                ulong q0 = BitConverter.ToUInt64(row), q1 = BitConverter.ToUInt64(row[8..]);
                ann = $"{Ann(m, q0)} | {Ann(m, q1)}";
            }
            Console.WriteLine($"+{i:X4} {hex,-47}  {Printable(row.ToArray())}  {ann}");
        }
    }

    static string Ann(ProcessMemory m, ulong q)
    {
        if (q < 0x10000 || q > 0x7FFF_FFFF_FFFF) return "";
        var s = m.ReadBytes(q, 24);
        if (s == null) return "";
        uint flen = BitConverter.ToUInt32(s, 0);
        if (flen is > 0 and < 60)
        {
            var fs = m.ReadUtf8(q + 4, (int)flen);
            if (fs != null && fs.Length == flen && fs.All(c => c >= 0x20)) return $"->FMStr\"{fs}\"";
        }
        var u8 = m.ReadUtf8(q, 40);
        if (u8 != null && u8.Length >= 3 && u8.All(c => c >= 0x20 && c < 0x7F || c > 0xA0)) return $"->\"{u8}\"";
        var u16 = m.ReadUtf16(q, 40);
        if (u16 != null && u16.Length >= 3 && u16.All(c => c >= 0x20 && c < 0x2000)) return $"->u\"{u16}\"";
        return "->ptr";
    }

    public static string Printable(byte[] b) => new(b.Select(x => x >= 0x20 && x < 0x7F ? (char)x : '.').ToArray());
}
