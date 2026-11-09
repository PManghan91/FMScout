using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace FMScout.Reader;

/// <summary>
/// Read-only handle to another process's memory. Opens the process with
/// PROCESS_VM_READ | PROCESS_QUERY_INFORMATION only — it is not possible to
/// write to the target through this handle.
/// </summary>
public sealed class ProcessMemory : IDisposable
{
    private const uint PROCESS_VM_READ = 0x0010;
    private const uint PROCESS_QUERY_INFORMATION = 0x0400;

    private const uint MEM_COMMIT = 0x1000;
    private const uint MEM_PRIVATE = 0x20000;
    private const uint PAGE_NOACCESS = 0x01;
    private const uint PAGE_GUARD = 0x100;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr h);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern unsafe bool ReadProcessMemory(IntPtr h, IntPtr addr, void* buf, nint size, out nint read);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint VirtualQueryEx(IntPtr h, IntPtr addr, out MEMORY_BASIC_INFORMATION mbi, nint len);

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORY_BASIC_INFORMATION
    {
        public IntPtr BaseAddress;
        public IntPtr AllocationBase;
        public uint AllocationProtect;
        public ushort PartitionId;
        public IntPtr RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
    }

    private IntPtr _handle;

    public Process Process { get; }

    private ProcessMemory(Process p, IntPtr handle)
    {
        Process = p;
        _handle = handle;
    }

    public static ProcessMemory Attach(string processName = "fm")
    {
        var p = Process.GetProcessesByName(processName).FirstOrDefault()
                ?? throw new InvalidOperationException($"Process '{processName}' not running.");
        var h = OpenProcess(PROCESS_VM_READ | PROCESS_QUERY_INFORMATION, false, p.Id);
        if (h == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenProcess failed");
        return new ProcessMemory(p, h);
    }

    public (ulong Base, int Size)? Module(string name)
    {
        foreach (ProcessModule m in Process.Modules)
            if (string.Equals(m.ModuleName, name, StringComparison.OrdinalIgnoreCase))
                return ((ulong)m.BaseAddress, m.ModuleMemorySize);
        return null;
    }

    public unsafe bool TryRead(ulong addr, Span<byte> buffer)
    {
        if (addr == 0) return false;
        fixed (byte* p = buffer)
            return ReadProcessMemory(_handle, (IntPtr)addr, p, buffer.Length, out var n) && n == buffer.Length;
    }

    public byte[]? ReadBytes(ulong addr, int len)
    {
        var b = new byte[len];
        return TryRead(addr, b) ? b : null;
    }

    public unsafe T Read<T>(ulong addr) where T : unmanaged
    {
        T v = default;
        ReadProcessMemory(_handle, (IntPtr)addr, &v, sizeof(T), out _);
        return v;
    }

    public ulong ReadPtr(ulong addr) => Read<ulong>(addr);

    public string? ReadUtf8(ulong addr, int maxLen = 256)
    {
        var b = ReadBytes(addr, maxLen);
        if (b == null) return null;
        int n = Array.IndexOf(b, (byte)0);
        return Encoding.UTF8.GetString(b, 0, n < 0 ? maxLen : n);
    }

    public string? ReadUtf16(ulong addr, int maxChars = 128)
    {
        var b = ReadBytes(addr, maxChars * 2);
        if (b == null) return null;
        int n = 0;
        while (n + 1 < b.Length && (b[n] | b[n + 1]) != 0) n += 2;
        return Encoding.Unicode.GetString(b, 0, n);
    }

    public record struct Region(ulong Base, ulong Size, uint Protect, uint Type);

    /// <summary>Committed, readable regions. If privateOnly, only heap-style (MEM_PRIVATE) memory.</summary>
    public IEnumerable<Region> Regions(bool privateOnly = false)
    {
        ulong addr = 0;
        while (addr < 0x7FFF_FFFF_FFFF)
        {
            if (VirtualQueryEx(_handle, (IntPtr)addr, out var mbi, Marshal.SizeOf<MEMORY_BASIC_INFORMATION>()) == 0) break;
            ulong b = (ulong)mbi.BaseAddress, s = (ulong)mbi.RegionSize;
            if (mbi.State == MEM_COMMIT && (mbi.Protect & (PAGE_NOACCESS | PAGE_GUARD)) == 0 && mbi.Protect != 0
                && (!privateOnly || mbi.Type == MEM_PRIVATE))
                yield return new Region(b, s, mbi.Protect, mbi.Type);
            addr = b + s;
        }
    }

    /// <summary>
    /// Scan readable memory for a byte pattern (mask: false = wildcard byte).
    /// Reads in chunks with overlap so matches crossing chunk boundaries are found.
    /// </summary>
    public IEnumerable<ulong> Scan(byte[] pattern, bool[]? mask = null, bool privateOnly = true, int alignment = 1, int max = int.MaxValue)
    {
        const int Chunk = 4 << 20;
        var buf = new byte[Chunk + pattern.Length];
        var hits = new List<ulong>();
        int found = 0;
        foreach (var r in Regions(privateOnly))
        {
            for (ulong off = 0; off < r.Size; off += Chunk)
            {
                int len = (int)Math.Min((ulong)buf.Length, r.Size - off);
                if (len < pattern.Length || !TryRead(r.Base + off, buf.AsSpan(0, len))) continue;
                int limit = (int)Math.Min((ulong)Chunk, r.Size - off);
                hits.Clear();
                FindInChunk(buf.AsSpan(0, len), limit, pattern, mask, r.Base + off, alignment, hits);
                foreach (var h in hits)
                {
                    yield return h;
                    if (++found >= max) yield break;
                }
            }
        }
    }

    private static void FindInChunk(ReadOnlySpan<byte> span, int limit, byte[] pattern, bool[]? mask, ulong baseAddr, int alignment, List<ulong> hits)
    {
        if (mask == null)
        {
            int i = 0;
            while (i < limit)
            {
                int idx = span[i..].IndexOf(pattern);
                if (idx < 0) break;
                i += idx;
                if (i >= limit) break;
                ulong a = baseAddr + (ulong)i;
                if (a % (ulong)alignment == 0) hits.Add(a);
                i++;
            }
        }
        else
        {
            for (int i = 0; i + pattern.Length <= span.Length && i < limit; i += alignment)
            {
                bool ok = true;
                for (int j = 0; j < pattern.Length && ok; j++)
                    if (mask[j] && span[i + j] != pattern[j]) ok = false;
                if (ok) hits.Add(baseAddr + (ulong)i);
            }
        }
    }
    /// <summary>One pass over memory: every 8-aligned qword whose value is in targets.</summary>
    public List<(ulong At, ulong Value)> ScanPointersToAny(IReadOnlySet<ulong> targets, bool privateOnly = true, int max = 100000)
    {
        const int Chunk = 4 << 20;
        var buf = new byte[Chunk];
        var res = new List<(ulong, ulong)>();
        ulong lo = targets.Min(), hi = targets.Max();
        foreach (var r in Regions(privateOnly))
        {
            for (ulong off = 0; off < r.Size; off += Chunk)
            {
                int len = (int)Math.Min((ulong)Chunk, r.Size - off);
                if (!TryRead(r.Base + off, buf.AsSpan(0, len))) continue;
                var q = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, ulong>(buf.AsSpan(0, len));
                for (int i = 0; i < q.Length; i++)
                {
                    ulong v = q[i];
                    if (v < lo || v > hi || !targets.Contains(v)) continue;
                    res.Add((r.Base + off + (ulong)i * 8, v));
                    if (res.Count >= max) return res;
                }
            }
        }
        return res;
    }

    public IEnumerable<ulong> ScanPointerTo(ulong target, bool privateOnly = true, int max = int.MaxValue)
        => Scan(BitConverter.GetBytes(target), null, privateOnly, 8, max);

    public void Dispose()
    {
        if (_handle != IntPtr.Zero) { CloseHandle(_handle); _handle = IntPtr.Zero; }
    }
}


