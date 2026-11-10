namespace FMScout.Reader;

/// <summary>Crash-safe file writes: write to a temp file, then swap it in (keeping a .bak). Corrupt files are moved aside.</summary>
public static class AtomicFile
{
    public static void WriteAllText(string path, string text)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, text);
        if (File.Exists(path)) File.Replace(tmp, path, path + ".bak", ignoreMetadataErrors: true);
        else File.Move(tmp, path);
    }

    /// <summary>Renames an unreadable file to name.broken-yyyyMMddHHmmss so it isn't overwritten and can be inspected.</summary>
    public static string? MoveAside(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var dest = $"{path}.broken-{DateTime.Now:yyyyMMddHHmmss}";
            File.Move(path, dest);
            return dest;
        }
        catch { return null; }
    }
}
