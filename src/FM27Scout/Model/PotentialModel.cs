namespace FM27Scout.Model;

/// <summary>
/// Learns, from the loaded save, how much each attribute "costs" in CA for each position group
/// (least-squares fit CA ~ attributes). When a player develops, CA goes up and the game spends it on
/// the attributes that matter for his position, so potential growth is distributed by these weights
/// rather than evenly. Physical attributes develop less after the mid-twenties.
/// </summary>
public sealed class PotentialModel
{
    public static readonly string[] Groups = ["GK", "DC", "FB", "DM", "MC", "W", "AMC", "ST"];
    static readonly string[] Physical = ["Acceleration", "Agility", "Balance", "JumpingReach", "NaturalFitness", "Pace", "Stamina", "Strength"];

    /// <summary>importance[group][attr], normalised so the average over CA-relevant attributes is 1.</summary>
    public double[][] Importance { get; private set; } = [];
    public double[] FitR2 { get; private set; } = [];
    /// <summary>Fitted CA cost per attribute point (1-20 scale), per group.</summary>
    public double[][] Coef { get; private set; } = [];
    bool[] _isPhysical = [];
    int[] _vars = [];

    public static int GroupOf(byte[] pos)
    {
        int best = 0;
        for (int i = 1; i < pos.Length; i++) if (pos[i] > pos[best]) best = i;
        return Positions.Names[best] switch
        {
            "GK" => 0, "SW" or "DC" => 1, "DL" or "DR" or "WBL" or "WBR" => 2, "DM" => 3, "MC" => 4,
            "ML" or "MR" or "AML" or "AMR" => 5, "AMC" => 6, _ => 7
        };
    }

    public static PotentialModel Fit(IReadOnlyList<PlayerRow> players)
    {
        var m = new PotentialModel();
        int n = Attr.Names.Length;
        m._isPhysical = Attr.Names.Select(a => Physical.Contains(a)).ToArray();
        // CA-relevant: everything except feet and hidden attributes
        var skip = new HashSet<string>(Attr.Hidden.Concat(["LeftFoot", "RightFoot"]));
        m._vars = Enumerable.Range(0, n).Where(i => !skip.Contains(Attr.Names[i])).ToArray();
        int k = m._vars.Length + 1;

        m.Importance = new double[Groups.Length][];
        m.FitR2 = new double[Groups.Length];
        m.Coef = new double[Groups.Length][];
        for (int g = 0; g < Groups.Length; g++)
        {
            var rows = players.Where(p => GroupOf(p.Positions) == g).ToList();
            var imp = Enumerable.Repeat(1.0, n).ToArray();
            var cf = new double[n];
            if (rows.Count > k * 20)
            {
                // normal equations X'X b = X'y with intercept
                var xtx = new double[k, k]; var xty = new double[k];
                var x = new double[k];
                foreach (var p in rows)
                {
                    x[0] = 1;
                    for (int j = 0; j < m._vars.Length; j++) x[j + 1] = p.A[m._vars[j]];
                    for (int a = 0; a < k; a++) { xty[a] += x[a] * p.CA; for (int b = a; b < k; b++) xtx[a, b] += x[a] * x[b]; }
                }
                for (int a = 0; a < k; a++) for (int b = 0; b < a; b++) xtx[a, b] = xtx[b, a];
                for (int a = 0; a < k; a++) xtx[a, a] += 1e-6 * rows.Count; // tiny ridge for stability
                var coef = Solve(xtx, xty);

                // R^2
                double mean = rows.Average(p => (double)p.CA), ssTot = 0, ssRes = 0;
                foreach (var p in rows)
                {
                    double pred = coef[0];
                    for (int j = 0; j < m._vars.Length; j++) pred += coef[j + 1] * p.A[m._vars[j]];
                    ssTot += (p.CA - mean) * (p.CA - mean); ssRes += (p.CA - pred) * (p.CA - pred);
                }
                m.FitR2[g] = 1 - ssRes / ssTot;

                var pos = m._vars.Select((v, j) => (v, c: Math.Max(0, coef[j + 1]))).ToArray();
                double avg = pos.Where(t => t.c > 0).Select(t => t.c).DefaultIfEmpty(1).Average();
                foreach (var (v, c) in pos) { imp[v] = Math.Clamp(c / avg, 0.2, 2.5); cf[v] = c; }
            }
            foreach (var h in skip) { int i = Attr.Index(h); if (i >= 0) imp[i] = 0; } // hidden & feet don't grow with CA
            m.Importance[g] = imp;
            m.Coef[g] = cf;
        }
        return m;
    }

    static double[] Solve(double[,] a, double[] b)
    {
        int n = b.Length;
        var m = (double[,])a.Clone(); var r = (double[])b.Clone();
        for (int c = 0; c < n; c++)
        {
            int piv = c;
            for (int i = c + 1; i < n; i++) if (Math.Abs(m[i, c]) > Math.Abs(m[piv, c])) piv = i;
            if (piv != c) { for (int j = 0; j < n; j++) (m[c, j], m[piv, j]) = (m[piv, j], m[c, j]); (r[c], r[piv]) = (r[piv], r[c]); }
            double d = m[c, c]; if (Math.Abs(d) < 1e-12) continue;
            for (int i = c + 1; i < n; i++)
            {
                double f = m[i, c] / d; if (f == 0) continue;
                for (int j = c; j < n; j++) m[i, j] -= f * m[c, j];
                r[i] -= f * r[c];
            }
        }
        var x = new double[n];
        for (int i = n - 1; i >= 0; i--)
        {
            double s = r[i];
            for (int j = i + 1; j < n; j++) s -= m[i, j] * x[j];
            x[i] = Math.Abs(m[i, i]) < 1e-12 ? 0 : s / m[i, i];
        }
        return x;
    }

    /// <summary>Physical development taper: full until 23, then slows.</summary>
    public static double PhysicalFactor(int? age) => age is not int a ? 1 : a <= 23 ? 1 : a <= 25 ? 0.6 : a <= 27 ? 0.3 : 0.1;

    /// <summary>
    /// Projected 1-20 attributes at potential: grows attributes by CA importance (physicals taper with age) and
    /// calibrates the amount so the fitted CA of the projected player rises by caGain.
    /// </summary>
    public double[] ProjectToCa(PlayerRow p, double caGain)
    {
        int g = GroupOf(p.Positions);
        if (Coef.Length == 0 || caGain <= 0) return p.A.Select(x => (double)x).ToArray();
        double Gain(double[] proj) { double s = 0; for (int i = 0; i < proj.Length; i++) s += Coef[g][i] * (proj[i] - p.A[i]); return s; }
        double lo = 0, hi = 1;
        while (hi < 64 && Gain(Project(p, hi)) < caGain) hi *= 2;
        for (int it = 0; it < 30; it++) { double mid = (lo + hi) / 2; if (Gain(Project(p, mid)) < caGain) lo = mid; else hi = mid; }
        return Project(p, hi);
    }

    /// <summary>Projected 1-20 attributes for a given growth scale.</summary>
    public double[] Project(PlayerRow p, double growth)
    {
        var imp = Importance.Length == 0 ? null : Importance[GroupOf(p.Positions)];
        var res = new double[p.A.Length];
        double phys = PhysicalFactor(p.Age);
        for (int i = 0; i < res.Length; i++)
        {
            double w = imp?[i] ?? 1;
            if (_isPhysical.Length > i && _isPhysical[i]) w *= phys;
            res[i] = Math.Min(20, p.A[i] + (20 - p.A[i]) * Math.Min(1, growth * w));
        }
        return res;
    }
}

