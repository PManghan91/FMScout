using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using FM27Scout.Model;

namespace FM27Scout;

/// <summary>The wide, FM-style player profile card (its own window; opened by double-click on Shortlist/Squad or from the side panel).</summary>
public partial class MainWindow
{
    Window? _profileWindow;

    void OpenProfileWindow(PlayerRow p)
    {
        if (_profileWindow == null)
        {
            var wa = SystemParameters.WorkArea;
            _profileWindow = new Window
            {
                Owner = this, Width = Math.Min(1400, wa.Width - 40), Height = Math.Min(920, wa.Height - 40),
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = (Brush)FindResource("Bg"),
            };
            _profileWindow.KeyDown += (_, k) => { if (k.Key == Key.Escape) _profileWindow?.Close(); };
            _profileWindow.Closed += (_, _) => _profileWindow = null;
            _profileWindow.Show();
        }
        _profileWindow.Title = $"{p.Name} - FM27 Scout";
        _profileWindow.Content = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = BuildProfileCard(p, _profileWindow),
        };
        _profileWindow.Activate();
    }

    internal FrameworkElement BuildProfileCard(PlayerRow p, Window? host = null)
    {
        var muted = (Brush)FindResource("Muted");
        var fg = (Brush)FindResource("Fg");
        var panel2 = (Brush)FindResource("Panel2");
        TextBlock T(string s, double size = 13, bool bold = false, Brush? brush = null, double wrapWidth = double.NaN) =>
            new() { Text = s, FontSize = size, FontWeight = bold ? FontWeights.Bold : FontWeights.Normal, Foreground = brush ?? fg, TextWrapping = TextWrapping.Wrap, MaxWidth = double.IsNaN(wrapWidth) ? double.PositiveInfinity : wrapWidth };
        Border Card(UIElement child, double width = double.NaN) => new()
        {
            Background = (Brush)FindResource("Panel"), BorderBrush = (Brush)FindResource("Line"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6), Padding = new Thickness(16, 12, 16, 14), Margin = new Thickness(0, 0, 12, 12), Child = child, Width = width,
        };
        Border Badge(string s, Color c) => new()
        {
            Background = new SolidColorBrush(Color.FromArgb(0x40, c.R, c.G, c.B)), BorderBrush = new SolidColorBrush(c), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10), Padding = new Thickness(9, 2, 9, 3), Margin = new Thickness(0, 0, 6, 4),
            Child = new TextBlock { Text = s, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(c) },
        };

        var root = new DockPanel { Margin = new Thickness(18, 14, 6, 6) };

        // ---------------- header
        var header = new DockPanel { Margin = new Thickness(0, 0, 12, 12) };
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
        Button Btn(string text, string tip, Action click)
        {
            var b = new Button { Content = text, Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(6, 0, 0, 6), ToolTip = tip, FontSize = 13 };
            b.Click += (_, _) => click();
            buttons.Children.Add(b);
            return b;
        }
        Button? slBtn = null;
        slBtn = Btn(Shortlist.Contains(p.Uid) ? "★ On shortlist" : "☆ Add to shortlist", "Add or remove from your shortlist", () =>
        {
            Shortlist.Toggle(p.Uid); UpdateShortlistTab();
            slBtn!.Content = Shortlist.Contains(p.Uid) ? "★ On shortlist" : "☆ Add to shortlist";
        });
        Btn(_compare.Contains(p) ? "In comparison" : "Compare", "Add to the Compare tab (up to 4 players)", () =>
        {
            if (!_compare.Contains(p)) { if (_compare.Count >= 4) _compare.RemoveAt(0); _compare.Add(p); }
            RenderCompare(); Tabs.SelectedIndex = 2; Activate();
        });
        var upBtn = Btn("Find upgrade ▾", "Find better players for this role who would join you", () => { });
        var upMenu = new ContextMenu();
        foreach (var item in UpgradeMenuItems(p, () => Activate())) upMenu.Items.Add(item);
        upBtn.ContextMenu = upMenu;
        upBtn.Click += (_, _) => { upMenu.PlacementTarget = upBtn; upMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom; upMenu.IsOpen = true; };
        Btn("Find similar", "Players with the same best role, a similar score and age (transfer-arranged excluded)", () => { FindSimilar(p); Activate(); });
        Btn("Copy name", "Copy the name to the clipboard", () => { CopyNames([p.Name]); StatusText.Text = $"Copied \"{p.Name}\"."; });
        DockPanel.SetDock(buttons, Dock.Right);
        header.Children.Add(buttons);

        var title = new StackPanel();
        var nameRow = new WrapPanel();
        nameRow.Children.Add(T(p.Name, 28, true));
        title.Children.Add(nameRow);
        if (p.FullName != null && p.FullName != p.Name) title.Children.Add(T(p.FullName, 13, brush: muted));
        title.Children.Add(T($"{p.PositionsText}   ·   {p.Age} years old ({p.DateOfBirth:dd/MM/yyyy})   ·   {p.Nationalities}", 14, brush: muted));
        title.Children.Add(T(p.Source.Club == null ? "Free agent" : $"{p.Club}   ·   {p.League}", 16, true));
        var badges = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        if (p.HasArranged) badges.Children.Add(Badge("Transfer arranged " + p.ArrangedText, Color.FromRgb(0xF0, 0x9A, 0x3E)));
        if (Shortlist.Contains(p.Uid)) badges.Children.Add(Badge("★ Shortlisted", Color.FromRgb(0xE8, 0xC5, 0x47)));
        if (p.IsRegen) badges.Children.Add(Badge("Newgen", Color.FromRgb(0x6C, 0xB4, 0xF0)));
        if (p.Source.Club == null) badges.Children.Add(Badge("Free agent", Color.FromRgb(0x5C, 0xC9, 0x7B)));
        if (p.PreContract) badges.Children.Add(Badge("Pre-contract possible", Color.FromRgb(0x5C, 0xC9, 0x7B)));
        if (p.IsBargain) badges.Children.Add(Badge("Bargain: " + p.BargainText, Color.FromRgb(0x5C, 0xC9, 0x7B)));
        if (p.NotForSale) badges.Children.Add(Badge("Not for sale", Color.FromRgb(0xE0, 0x6C, 0x6C)));
        if (!p.CanSign && p.TransferRule.Length > 0) badges.Children.Add(Badge("Can't sign: " + p.TransferRule, Color.FromRgb(0xE0, 0x6C, 0x6C)));
        if (p.WpText.Contains("unlikely")) badges.Children.Add(Badge("WP unlikely", Color.FromRgb(0xE0, 0x6C, 0x6C)));
        if (p.RiskCount > 0) badges.Children.Add(Badge($"{p.RiskCount} risk{(p.RiskCount > 1 ? "s" : "")}", Color.FromRgb(0xF0, 0x9A, 0x3E)));
        if (badges.Children.Count > 0) title.Children.Add(badges);
        header.Children.Add(title);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        // ---------------- three columns
        var body = new WrapPanel();
        root.Children.Add(body);

        // column 1: facts + details
        var c1 = new StackPanel { Width = 356 };
        var facts = new Grid();
        for (int i = 0; i < 3; i++) facts.ColumnDefinitions.Add(new ColumnDefinition());
        int fi = 0;
        void Fact(string k, string v, Brush? b = null)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 8, 10) };
            sp.Children.Add(T(k, 11.5, brush: muted)); sp.Children.Add(T(v, 16, true, b));
            if (fi % 3 == 0) facts.RowDefinitions.Add(new RowDefinition());
            Grid.SetColumn(sp, fi % 3); Grid.SetRow(sp, fi / 3); facts.Children.Add(sp); fi++;
        }
        Fact("Current ability", p.CA.ToString(), ScoreBrush.For(p.CA / 2.0));
        Fact("Potential ability", p.PA.ToString(), ScoreBrush.For(p.PA / 2.0));
        Fact("Value", p.ValueText);
        Fact("Wage", p.Wage > 0 ? MoneyText.Format(p.Wage) + " p/w" : "-");
        Fact("Contract ends", p.ContractExpiry?.ToString("dd/MM/yyyy") ?? "-");
        Fact("Joined", p.Joined?.ToString("dd/MM/yyyy") ?? "-");
        if (p.Fee > 0) Fact("Likely fee", MoneyText.Format((long)p.Fee));
        Fact("Height", $"{p.Height} cm");
        Fact("Feet", $"L {p.LeftFoot} / R {p.RightFoot}");
        Fact("Reputation", $"{p.CurrentRep:N0}");
        Fact("Home / world", $"{p.HomeRep:N0} / {p.WorldRep:N0}");
        if (p.SquadStatusName.Length > 0) Fact("Squad status", p.SquadStatusName);
        if (p.PersonalityLabel.Length > 0) Fact("Personality", p.PersonalityLabel);
        Fact("Development", p.OutlookText);
        if (p.CareerApps > 0) Fact("Career", $"{p.CareerApps} apps · {p.CareerGoals} g");
        if (p.CareerApps > 0) Fact("Goals / game", $"{p.GoalsPerGame:F2}");
        if (p.DeltaCaSeason is int dc) Fact("CA this season", $"{dc:+0;-0;0}", dc > 0 ? AttrBrush.Good : dc < 0 ? AttrBrush.Mid : null);
        if (p.DeltaCaLast is int dl) Fact("CA since last load", $"{dl:+0;-0;0}", dl > 0 ? AttrBrush.Good : dl < 0 ? AttrBrush.Mid : null);
        if (p.IsRegen) Fact("Newgen", p.RegenText);
        c1.Children.Add(facts);
        var details = new StackPanel();
        void Line(string label, string text, Brush? b = null)
        {
            if (text.Length == 0) return;
            var tb = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13, Margin = new Thickness(0, 0, 0, 6), Foreground = b ?? fg };
            tb.Inlines.Add(new System.Windows.Documents.Run(label + "  ") { Foreground = muted, FontSize = 12 });
            tb.Inlines.Add(new System.Windows.Documents.Run(text));
            details.Children.Add(tb);
        }
        if (p.HasArranged) Line("Transfer arranged", $"{p.ArrangedText}" + (p.Arranged!.WeeklyWage > 0 ? $", agreed wage {MoneyText.Format(p.Arranged.WeeklyWage)} p/w" : ""), AttrBrush.Mid);
        Line("Work permit", p.WpText.Length == 0 ? "" : $"{p.WpText}. {p.WpDetail}", p.WpText.Contains("unlikely") ? AttrBrush.Mid : null);
        Line("Transfer rule", p.TransferRule, p.CanSign ? null : AttrBrush.Mid);
        Line("Risks", p.RiskText, AttrBrush.Mid);
        if (p.Source.FavouredClubs.Length > 0) Line("Favoured clubs", string.Join(", ", p.Source.FavouredClubs));
        Line("Personality (hidden 1-20)", string.Join(" · ", Model.Personality.Names.Select((n, i) => i < p.Personality.Length ? $"{n} {p.Personality[i]}" : "").Where(s => s.Length > 0)));
        Line("UID", p.Uid.ToString());
        c1.Children.Add(new Border { Height = 1, Background = (Brush)FindResource("Line"), Margin = new Thickness(0, 2, 0, 10) });
        c1.Children.Add(details);
        body.Children.Add(Card(c1, 390));

        // column 2: attributes
        var c2 = new StackPanel();
        var cols = new Grid();
        for (int i = 0; i < 3; i++) cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(162) });
        StackPanel Block(string blockTitle, string[] names, StackPanel? into = null)
        {
            var sp = into ?? new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
            if (into != null) sp.Children.Add(new Border { Height = 12 });
            sp.Children.Add(T(blockTitle, 13, true, muted));
            foreach (var n in names)
            {
                int i = Attr.Index(n); if (i < 0) continue;
                var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
                var v = new Border
                {
                    Width = 30, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
                    Child = new TextBlock { Text = p.A[i].ToString(), FontSize = 14, FontWeight = FontWeights.Bold, Foreground = AttrBrush.For(p.A[i]), HorizontalAlignment = HorizontalAlignment.Center },
                };
                DockPanel.SetDock(v, Dock.Right);
                row.Children.Add(v);
                row.Children.Add(T(Attr.Pretty(n).Replace(" (Tendency)", ""), 13.5));
                sp.Children.Add(row);
            }
            return sp;
        }
        var left = p.IsGoalkeeper ? Block("Goalkeeping", Attr.Goalkeeping) : Block("Technical", Attr.Technical);
        var mid = Block("Mental", Attr.Mental);
        var right = Block("Physical", Attr.Physical);
        if (p.IsGoalkeeper) Block("Technical", ["FreeKicks", "Penalties", "Technique"], right);
        Block("Hidden", Attr.Hidden, right);
        Grid.SetColumn(mid, 1); Grid.SetColumn(right, 2);
        cols.Children.Add(left); cols.Children.Add(mid); cols.Children.Add(right);
        c2.Children.Add(cols);
        c2.Children.Add(T("1-20 like FM: 16+ green, 11-15 white, 6-10 amber, 1-5 red. Hidden attributes are what FM's scouts can't see.", 11.5, brush: muted, wrapWidth: 470));
        body.Children.Add(Card(c2));

        // column 3: positions pitch + roles + join
        var c3 = new StackPanel { Width = 366 };
        var top = new DockPanel();
        var pitch = Pitch(p);
        DockPanel.SetDock(pitch, Dock.Left);
        top.Children.Add(pitch);
        var legend = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        legend.Children.Add(T("Positions", 13, true, muted));
        legend.Children.Add(T(p.PositionsText, 14, true));
        foreach (var (lbl, c) in new[] { ("Natural (18-20)", PitchColor(19)), ("Accomplished (15-17)", PitchColor(16)), ("Competent (10-14)", PitchColor(12)), ("Unconvincing (5-9)", PitchColor(7)) })
        {
            var r = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            r.Children.Add(new Ellipse { Width = 12, Height = 12, Fill = new SolidColorBrush(c), Margin = new Thickness(0, 0, 6, 0) });
            r.Children.Add(T(lbl, 12, brush: muted));
            legend.Children.Add(r);
        }
        top.Children.Add(legend);
        c3.Children.Add(top);

        c3.Children.Add(new Border { Height = 12 });
        var rh = new DockPanel();
        var key = T("now ▮  potential ▯", 11.5, brush: muted); DockPanel.SetDock(key, Dock.Right); rh.Children.Add(key);
        rh.Children.Add(T("Best roles", 13, true, muted));
        c3.Children.Add(rh);
        foreach (var (r, i) in _roles.Roles.Select((r, i) => (r, i)).OrderByDescending(t => p.RoleScores[t.i]).Take(8))
        {
            double now = p.RoleScores[i], pot = Math.Max(now, p.PotentialRoleScores[i]);
            var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62) });
            row.Children.Add(T(r.Name, 13));
            var bar = new Grid { Height = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 8, 0) };
            bar.Children.Add(new Border { Background = panel2, CornerRadius = new CornerRadius(3) });
            var potBar = new Border { HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1), BorderBrush = ScoreBrush.For(pot), Background = Brushes.Transparent };
            var nowBar = new Border { HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(3), Background = ScoreBrush.For(now) };
            bar.Children.Add(potBar); bar.Children.Add(nowBar);
            bar.SizeChanged += (_, e) => { potBar.Width = e.NewSize.Width * Math.Clamp(pot, 0, 100) / 100; nowBar.Width = e.NewSize.Width * Math.Clamp(now, 0, 100) / 100; };
            Grid.SetColumn(bar, 1); row.Children.Add(bar);
            var val = new TextBlock { FontSize = 13, HorizontalAlignment = HorizontalAlignment.Right };
            val.Inlines.Add(new System.Windows.Documents.Run($"{now:F0}") { FontWeight = FontWeights.Bold, Foreground = ScoreBrush.For(now) });
            val.Inlines.Add(new System.Windows.Documents.Run($" · {pot:F0}") { Foreground = ScoreBrush.For(pot) });
            Grid.SetColumn(val, 2); row.Children.Add(val);
            c3.Children.Add(row);
        }

        if (MyClubCombo.SelectedItem is MyClub my)
        {
            var box = new Border { Background = panel2, CornerRadius = new CornerRadius(5), Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 14, 0, 0) };
            var sp = new StackPanel();
            var head = new DockPanel();
            var lbl = T(p.Join < 0 ? p.JoinLabel : $"{p.JoinLabel}  {p.Join:F0}%", 16, true, p.Join < 0 ? muted : ScoreBrush.For(p.Join + 15));
            DockPanel.SetDock(lbl, Dock.Right); head.Children.Add(lbl);
            head.Children.Add(T($"Would he join {my.Name}?", 14, true));
            sp.Children.Add(head);
            if (p.HasArranged) sp.Children.Add(T($"- Already agreed to join {p.Arranged!.ToClub}; you would have to hijack the deal", 12.5, brush: AttrBrush.Mid));
            foreach (var r in p.JoinReasons) sp.Children.Add(T(r, 12.5, brush: r.StartsWith('-') ? AttrBrush.Mid : r.StartsWith('+') ? AttrBrush.Good : muted));
            if (p.Fee > 0) sp.Children.Add(T($"Likely fee {MoneyText.Format((long)p.Fee)} · value for money {p.Vfm:F1}" + (p.GainOverSquad > 0 ? $" · +{p.GainOverSquad:F0} over your squad" : ""), 12, brush: muted));
            sp.Children.Add(T("Estimate only. FM decides when you approach him.", 11.5, brush: muted));
            box.Child = sp;
            c3.Children.Add(box);
        }
        body.Children.Add(Card(c3, 400));
        return root;
    }

    static Color PitchColor(int v) => v >= 18 ? Color.FromRgb(0x2E, 0xC4, 0x5E) : v >= 15 ? Color.FromRgb(0x9B, 0xD1, 0x3C) : v >= 10 ? Color.FromRgb(0xE3, 0xB3, 0x2E) : Color.FromRgb(0xD0, 0x6A, 0x3A);

    /// <summary>Vertical pitch (attack at the top) with a dot per position, coloured by FM's 1-20 familiarity.</summary>
    FrameworkElement Pitch(PlayerRow p)
    {
        const double W = 190, H = 270;
        var cv = new Canvas { Width = W, Height = H, ClipToBounds = true };
        var turf = new SolidColorBrush(Color.FromRgb(0x1F, 0x4D, 0x2E));
        var chalk = new SolidColorBrush(Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF));
        cv.Children.Add(new Rectangle { Width = W, Height = H, Fill = turf, RadiusX = 6, RadiusY = 6 });
        void R(double x, double y, double w, double h) { var r = new Rectangle { Width = w, Height = h, Stroke = chalk, StrokeThickness = 1 }; Canvas.SetLeft(r, x); Canvas.SetTop(r, y); cv.Children.Add(r); }
        R(6, 6, W - 12, H - 12);
        cv.Children.Add(new Line { X1 = 6, X2 = W - 6, Y1 = H / 2, Y2 = H / 2, Stroke = chalk, StrokeThickness = 1 });
        var circle = new Ellipse { Width = 46, Height = 46, Stroke = chalk, StrokeThickness = 1 }; Canvas.SetLeft(circle, W / 2 - 23); Canvas.SetTop(circle, H / 2 - 23); cv.Children.Add(circle);
        R(W / 2 - 45, 6, 90, 36); R(W / 2 - 45, H - 42, 90, 36);
        var spots = new (string Pos, double X, double Y)[]
        {
            ("ST", .5, .12), ("AML", .15, .27), ("AMC", .5, .29), ("AMR", .85, .27),
            ("ML", .15, .45), ("MC", .5, .47), ("MR", .85, .45),
            ("WBL", .12, .62), ("DM", .5, .63), ("WBR", .88, .62),
            ("DL", .15, .78), ("DC", .5, .79), ("DR", .85, .78), ("GK", .5, .93),
        };
        foreach (var (pos, x, y) in spots)
        {
            int i = Model.Positions.Index(pos);
            int v = i >= 0 && i < p.Positions.Length ? p.Positions[i] : 0;
            double d = v >= 5 ? 22 : 10;
            var dot = new Ellipse
            {
                Width = d, Height = d,
                Fill = v >= 5 ? new SolidColorBrush(PitchColor(v)) : new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF)),
                Stroke = v >= 18 ? Brushes.White : null, StrokeThickness = 1.5,
                ToolTip = $"{pos}: {v}/20",
            };
            Canvas.SetLeft(dot, x * W - d / 2); Canvas.SetTop(dot, y * H - d / 2);
            cv.Children.Add(dot);
            if (v >= 5)
            {
                var t = new TextBlock { Text = pos, FontSize = 9, FontWeight = FontWeights.Bold, Foreground = Brushes.Black, Width = 30, TextAlignment = TextAlignment.Center, IsHitTestVisible = false };
                Canvas.SetLeft(t, x * W - 15); Canvas.SetTop(t, y * H - 6.5);
                cv.Children.Add(t);
            }
        }
        return new Border { CornerRadius = new CornerRadius(6), Child = cv };
    }

    void FindSimilar(PlayerRow p)
    {
        int best = Enumerable.Range(0, p.RoleScores.Length).MaxBy(i => p.RoleScores[i]);
        var role = _roles.Roles[best];
        _upgrade = null;
        WriteFilter(new FilterState
        {
            Role = role.Code, RoleMin = Math.Round(p.RoleScores[best] - 4), AgeMin = p.Age is int a ? a - 3 : null, AgeMax = p.Age is int b ? b + 3 : null,
            HideArranged = true,
        });
        Tabs.SelectedIndex = 0;
        ApplyFilters();
        _sortCol = _cols.FirstOrDefault(c => c.Key == "role") ?? _sortCol; _sortDir = ListSortDirection.Descending; SortView();
        StatusText.Text = $"{_view.Count} players similar to {p.Name}: {role.Name} {p.RoleScores[best] - 4:F0}+ , age {p.Age - 3}-{p.Age + 3}, transfer-arranged excluded.";
    }
}
