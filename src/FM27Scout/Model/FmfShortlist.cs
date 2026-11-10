using System.IO;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace FM27Scout.Model;

/// <summary>
/// Writes an FM27 shortlist file (.fmf) that FM loads from Documents\Sports Interactive\Football Manager 27\shortlists.
/// Layout (worked out from a file saved by FM27 and from FM's own loader in game_plugin.dll):
///   "afe." section: u8 2, u8 1, "afe.", u16 8, u8 0, u64 sectionLen (from offset 9), u64 0x11 (payload start, from 9), u8 3 (zstd)
///     payload = for each file: u32 16, u32 16, key[16], iv[16], AES-128-CTR(key, iv, zstd(content))
///   "fmf." section: u8 2, u8 1, "fmf.", u16 8, u8 0, zstd(directory index)
///   index: str rootName, u32 nFiles, { str name, str ext, u64 offset, u64 storedSize, u64 rawSize, i64 t1, i64 t2 },
///          u32 nDirs, { str dirName, u32 nFiles, files…, u32 nDirs … }   (str = u32 length + bytes)
///   files: image.img (10 bytes), &lt;name&gt;.slf (the players), _data/details.aom (shortlist metadata)
///   .slf: 03 01 "fls." 02 00 01 "UXF" 00, str name, u32 count, u32 uid × count, 00
/// Zstd frames are written as raw (stored) blocks: valid zstd that any decoder reads, no compression library needed.
/// </summary>
public static class FmfShortlist
{
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "Sports Interactive", "Football Manager 27", "shortlists");

    // timestamps as FM wrote them in the sample (copied verbatim; FM does not appear to check them)
    static readonly byte[] Stamp = Convert.FromHexString("00096E88F1FFFFFF00096E88F1FFFFFF");
    // tail of details.aom from FM's own file (owner name + version/ids), kept verbatim
    static readonly byte[] DetailsTail = Convert.FromHexString("07000000426C69747A4559020001000288D0F007010010010B0D197A37925AA52A");

    public static string Write(string name, IReadOnlyList<uint> uids, string? folder = null)
    {
        folder ??= Folder;
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, SafeFileName(name) + ".fmf");
        File.WriteAllBytes(path, Build(name, uids));
        return path;
    }

    public static string SafeFileName(string name)
    {
        var bad = Path.GetInvalidFileNameChars();
        var s = new string(name.Select(c => bad.Contains(c) ? '_' : c).ToArray()).Trim();
        return s.Length == 0 ? "FM27 Scout shortlist" : s;
    }

    public static byte[] Build(string name, IReadOnlyList<uint> uids)
    {
        // file contents
        var slf = new MemoryStream();
        slf.Write([0x03, 0x01, (byte)'f', (byte)'l', (byte)'s', (byte)'.', 0x02, 0x00, 0x01, (byte)'U', (byte)'X', (byte)'F', 0x00]);
        Str(slf, name); U32(slf, (uint)uids.Count); foreach (var u in uids) U32(slf, u); slf.WriteByte(0);

        var aom = new MemoryStream();
        aom.Write([0x03, 0x01, (byte)'m', (byte)'o', (byte)'a', (byte)'.', 0x04, 0x00]);
        Str(aom, "PLAYER_SHORTLIST_TYPE_HANDLER"); Str(aom, name);
        aom.Write(new byte[10]); aom.Write(Enumerable.Repeat((byte)0xFF, 16).ToArray());
        aom.Write(DetailsTail);

        byte[] img = [0x01, 0, 0, 0, 0, 0, 0, 0, 0, 0];

        // payload: details, image, slf (same order as FM)
        var payload = new MemoryStream();
        (long off, long stored, long raw) Add(byte[] content)
        {
            long off = payload.Position;
            var blob = Encrypt(Zstd(content));
            payload.Write(blob);
            return (off, blob.Length, content.Length);
        }
        var dAom = Add(aom.ToArray()); var dImg = Add(img); var dSlf = Add(slf.ToArray());

        // directory index
        var idx = new MemoryStream();
        void Entry(string n, string ext, (long off, long stored, long raw) e) { Str(idx, n); Str(idx, ext); U64(idx, (ulong)e.off); U64(idx, (ulong)e.stored); U64(idx, (ulong)e.raw); idx.Write(Stamp); }
        Str(idx, name);
        U32(idx, 2); Entry("image", ".img", dImg); Entry(name, ".slf", dSlf);
        U32(idx, 1); Str(idx, "_data"); U32(idx, 1); Entry("details", ".aom", dAom); U32(idx, 0);

        var o = new MemoryStream();
        o.Write([0x02, 0x01, (byte)'a', (byte)'f', (byte)'e', (byte)'.', 0x08, 0x00, 0x00]);
        U64(o, (ulong)(0x11 + payload.Length)); U64(o, 0x11); o.WriteByte(3);
        payload.Position = 0; payload.CopyTo(o);
        o.Write([0x02, 0x01, (byte)'f', (byte)'m', (byte)'f', (byte)'.', 0x08, 0x00, 0x00]);
        o.Write(Zstd(idx.ToArray()));
        return o.ToArray();
    }

    static void Str(Stream s, string v) { var b = Encoding.UTF8.GetBytes(v); U32(s, (uint)b.Length); s.Write(b); }
    static void U32(Stream s, uint v) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(b, v); s.Write(b); }
    static void U64(Stream s, ulong v) { Span<byte> b = stackalloc byte[8]; BinaryPrimitives.WriteUInt64LittleEndian(b, v); s.Write(b); }

    /// <summary>u32 16, u32 16, key, iv, AES-128-CTR (128-bit big-endian counter starting at iv).</summary>
    static byte[] Encrypt(byte[] data)
    {
        var key = RandomNumberGenerator.GetBytes(16); var iv = RandomNumberGenerator.GetBytes(16);
        var ct = Ctr(key, iv, data);
        var o = new MemoryStream();
        U32(o, 16); U32(o, 16); o.Write(key); o.Write(iv); o.Write(ct);
        return o.ToArray();
    }

    internal static byte[] Ctr(byte[] key, byte[] iv, byte[] data)
    {
        using var aes = Aes.Create(); aes.Key = key;
        var counter = (byte[])iv.Clone(); var outp = new byte[data.Length];
        for (int i = 0; i < data.Length; i += 16)
        {
            var ks = aes.EncryptEcb(counter, PaddingMode.None);
            for (int j = 0; j < 16 && i + j < data.Length; j++) outp[i + j] = (byte)(data[i + j] ^ ks[j]);
            for (int k = 15; k >= 0; k--) if (++counter[k] != 0) break;
        }
        return outp;
    }

    /// <summary>A valid zstd frame made of raw (uncompressed) blocks, with the content size in the header.</summary>
    internal static byte[] Zstd(byte[] data)
    {
        var o = new MemoryStream();
        o.Write([0x28, 0xB5, 0x2F, 0xFD]);
        // frame header: single segment, FCS size by length, no checksum, no dictionary
        if (data.Length < 256) { o.WriteByte(0x20); o.WriteByte((byte)data.Length); }
        else if (data.Length < 65536 + 256) { o.WriteByte(0x60); var v = (ushort)(data.Length - 256); o.WriteByte((byte)v); o.WriteByte((byte)(v >> 8)); }
        else { o.WriteByte(0xA0); U32(o, (uint)data.Length); }
        const int MaxBlock = 128 * 1024;
        int pos = 0;
        do
        {
            int n = Math.Min(MaxBlock, data.Length - pos);
            bool last = pos + n >= data.Length;
            uint hdr = (uint)(n << 3) | (last ? 1u : 0u); // type 0 = raw
            o.WriteByte((byte)hdr); o.WriteByte((byte)(hdr >> 8)); o.WriteByte((byte)(hdr >> 16));
            o.Write(data, pos, n);
            pos += n;
        } while (pos < data.Length);
        return o.ToArray();
    }
}
