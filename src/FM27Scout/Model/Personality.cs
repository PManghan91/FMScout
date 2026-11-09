namespace FM27Scout.Model;

/// <summary>
/// FM-style personality label from the 8 hidden personality values (+ Determination for players).
/// The byte order (Adaptability, Ambition, Loyalty, Pressure, Professionalism, Sportsmanship, Temperament, Controversy)
/// reproduces all 12 labels seen in the BRR screenshots. The rules below are our own approximation of FM's and are tuned
/// to those 12; other labels (Model Citizen, Ambitious, ...) use the same style of thresholds and are estimates.
/// </summary>
public static class Personality
{
    public const int Adaptability = 0, Ambition = 1, Loyalty = 2, Pressure = 3, Professionalism = 4, Sportsmanship = 5, Temperament = 6, Controversy = 7;
    public static readonly string[] Names = ["Adaptability", "Ambition", "Loyalty", "Pressure", "Professionalism", "Sportsmanship", "Temperament", "Controversy"];

    public static string Label(byte[] p, int? determination)
    {
        if (p.Length < 8) return "";
        int amb = p[Ambition], loy = p[Loyalty], pre = p[Pressure], pro = p[Professionalism], spo = p[Sportsmanship], tem = p[Temperament], con = p[Controversy];
        int det = determination ?? 10;
        if (pro >= 18 && amb >= 12 && loy >= 12 && spo >= 15 && tem >= 15 && con <= 5 && det >= 15) return "Model Citizen";
        if (pro == 20 && tem >= 10) return "Model Professional";
        if (pro >= 15 && amb >= 14 && tem <= 10 && det >= 14) return "Perfectionist";
        if (pro >= 18) return "Professional";
        if (determination != null && det >= 18 && amb >= 15) return "Driven";
        if (amb == 20) return "Very Ambitious";
        if (amb >= 18) return "Ambitious";
        if (loy == 20) return "Very Loyal";
        if (loy >= 18) return "Loyal";
        if (pro >= 16) return "Fairly Professional";
        if (loy >= 15) return "Fairly Loyal";
        if (amb >= 15) return "Fairly Ambitious";
        if (spo >= 18) return "Honest";
        if (tem <= 4) return "Temperamental";
        if (spo <= 4) return "Unsporting";
        if (pre <= 4) return "Spineless";
        if (amb <= 5) return "Unambitious";
        if (pro <= 5) return "Casual";
        return "Balanced";
    }

    /// <summary>Development outlook 1-20 for young players: professionalism, determination and ambition drive training.</summary>
    public static double Outlook(byte[] p, int determination)
        => p.Length < 8 ? 10 : p[Professionalism] * 0.4 + determination * 0.35 + p[Ambition] * 0.25;

    public static string OutlookText(double o) => o >= 16 ? "Excellent" : o >= 13 ? "Good" : o >= 10 ? "Average" : "Poor";
}

