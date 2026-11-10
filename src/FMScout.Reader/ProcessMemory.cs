using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace FMScout.Reader;

public enum AttachProblem { NotRunning, Starting, AccessDenied, Other }
public sealed class AttachException(AttachProblem problem, string message) : Exception(message) { public AttachProblem Problem { get; } = problem; }

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
    private static extern Microsoft.Win32.SafeHandles.SafeProcessHandle OpenProcess(uint access, bool inherit, int pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern unsafe bool ReadProcessMemory(Microsoft.Win32.SafeHandles.SafeProcessHandle h, IntPtr addr, void* buf, nint size, out nint read);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint VirtualQueryEx(Microsoft.Win32.SafeHandles.SafeProcessHandle h, IntPtr addr, out MEMORY_BASIC_INFORMATION mbi, nint len);

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

    private readonly Microsoft.Win32.SafeHandles.SafeProcessHandle _handle;
    private long _failedReads;

    public Process Process { get; }
    /// <summary>Number of reads that failed (unreadable address, process gone). Used to detect half-read loads.</summary>
    public long FailedReads => Interlocked.Read(ref _failedReads);
    public bool HasExited { get { try { return Process.HasExited; } catch { return true; } } }

    private ProcessMemory(Process p, Microsoft.Win32.SafeHandles.SafeProcessHandle handle)
    {
        Process = p;
        _handle = handle;
    }

    /// <summary>
    /// Opens the game read-only. With several processes of that name, the one that has <paramref name="requiredModule"/>
    /// loaded wins (e.g. a stray launcher). Throws AttachException with a user-friendly reason.
    /// </summary>
    public static ProcessMemory Attach(string processName = "fm", string? requiredModule = null)
    {
        var procs = Process.GetProcessesByName(processName);
        if (procs.Length == 0) throw new AttachException(AttachProblem.NotRunning, $"Process '{processName}' not running.");
        Process? chosen = null; bool starting = false;
        foreach (var p in procs.OrderByDescending(p => { try { return p.WorkingSet64; } catch { return 0L; } }))
        {
            if (chosen != null) { p.Dispose(); continue; }
            if (requiredModule == null) { chosen = p; continue; }
            try
            {
                if (p.Modules.Cast<ProcessModule>().Any(m => string.Equals(m.ModuleName, requiredModule, StringComparison.OrdinalIgnoreCase))) chosen = p;
                else p.Dispose();
            }
            catch (Win32Exception) { starting = true; p.Dispose(); } // ERROR_PARTIAL_COPY (299) while FM is still starting
        }
        if (chosen == null)
            throw new AttachException(starting ? AttachProblem.Starting : AttachProblem.NotRunning, "FM is still starting (game engine not loaded yet).");
        var h = OpenProcess(PROCESS_VM_READ | PROCESS_QUERY_INFORMATION, false, chosen.Id);
        if (h.IsInvalid)
        {
            int err = Marshal.GetLastWin32Error();
            h.Dispose(); chosen.Dispose();
            throw new AttachException(err == 5 ? AttachProblem.AccessDenied : AttachProblem.Other, new Win32Exception(err).Message);
        }
        return new ProcessMemory(chosen, h);
    }

    public (ulong Base, int Size)? Module(string name)
    {
        foreach (ProcessModule m in Process.Modules)
            if (string.Equals(m.ModuleName, name, StringComparison.OrdinalIgnoreCase))
                return ((ulong)m.BaseAddress, m.ModuleMemorySize);
        return null;
    }

    /// <summary>Plausible user-mode address (not null, not kernel).</summary>
    public static bool IsPtr(ulong q) => q >= 0x10000 && q < 0x7FFF_FFFF_0000;
    /// <summary>Plausible pointer to a C++ object (8-byte aligned).</summary>
    public static bool IsObjPtr(ulong q) => IsPtr(q) && (q & 7) == 0;

    public unsafe bool TryRead(ulong addr, Span<byte> buffer)
    {
        if (!IsPtr(addr)) return false;
        bool ok;
        fixed (byte* p = buffer)
            ok = ReadProcessMemory(_handle, (IntPtr)addr, p, buffer.Length, out var n) && n == buffer.Length;
        if (!ok) Interlocked.Increment(ref _failedReads);
        return ok;
    }

    public byte[]? ReadBytes(ulong addr, int len)
    {
        var b = new byte[len];
        return TryRead(addr, b) ? b : null;
    }

    /// <summary>Reads a value; returns default (0) if the address is invalid or unreadable (counted in FailedReads).</summary>
    public unsafe T Read<T>(ulong addr) where T : unmanaged
    {
        T v = default;
        if (!IsPtr(addr)) return v;
        if (!ReadProcessMemory(_handle, (IntPtr)addr, &v, sizeof(T), out var n) || n != sizeof(T)) { Interlocked.Increment(ref _failedReads); return default; }
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
        _handle.Dispose();
        Process.Dispose();
    }
}



