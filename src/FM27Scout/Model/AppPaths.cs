using System.IO;
using System.Reflection;

namespace FM27Scout.Model;

public static class AppPaths
{
    public static string Data
    {
        get
        {
            var d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FM27Scout");
            Directory.CreateDirectory(d);
            return d;
        }
    }

    public static string ExeDir => AppContext.BaseDirectory;
}

public static class Embedded
{
    public static string Read(string name)
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
                      ?? throw new FileNotFoundException($"embedded resource {name} missing");
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }
}
