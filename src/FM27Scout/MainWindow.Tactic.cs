using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using FM27Scout.Model;

namespace FM27Scout;

/// <summary>
/// Squad › Tactic view: the formation on a pitch (starter + backup per slot, scored in that slot's role) and, for the
/// selected slot, squad depth, shortlist comparison and suggestions from the whole database with Paul's criteria.
/// </summary>
public partial class MainWindow
{
    TacticDef _tactic = Tactics.Load();
    int _slot = -1;
    PlayerRow?[] _xi = [];
    List<PlayerRow> _tacticSquad = [];
    bool _tacticBuilt;

    // controls built once
    ComboBox _formationBox = null!, _slotRoleBox = null!;
    Canvas _pitch = null!;
    TextBlock _slotTitle = null!, _tacticNote = null!, _sugNote = null!;
    DataGrid _depthGrid = null!, _slGrid = null!, _sugGrid = null!;
    TextBox _sugFee = null!, _sugWage = null!, _sugAge = null!, _sugJoin = null!, _sugTop = null!;
    CheckBox _sugPot = null!, _sugTrn = null!, _sugWp = null!, _sugUpOnly = null!;
    ComboBox _sugScope = null!;

    /// <summary>A player as a candidate for the selected slot.</summary>
    public sealed class SlotCandidate
    {
        public required PlayerRow P { get; init; }
        public string Name => P.Name;
        public int? Age => P.Age;
        public string Club => P.Source.Club == null ? "Free agent" : P.Club;
        public string Pos => P.PositionsText;
        public double Score { get; init; }
        public double Pot { get; init; }
        public double Gain { get; init; }
        public string GainText => Gain > 0 ? $"+{Gain:F0}" : $"{Gain:F0}";
        public int CA => P.CA;
        public int PA => P.PA;
        public string Join => P.Join < 0 ? P.JoinLabel : $"{P.Join:F0}%";
        public double JoinSort => P.Join;
        public string Fee => P.Club == ClubContext.MyClubName ? "" : double.IsNaN(P.Fee) ? "NFS" : P.Fee <= 0 ? "free" : MoneyText.Format((long)P.Fee);
        public double FeeSort => double.IsNaN(P.Fee) ? double.MaxValue : P.Fee;
        public string Wage => P.WageText;
        public long WageSort => P.Wage;
        public string Wp => P.WpText;
        public string Trn => P.ArrangedText;
        public string Note { get; init; } = "";
    }

    void BuildTacticView()
    {
        if (_tacticBuilt) return;
        _tacticBuilt = true;
        var muted = (Brush)FindResource("Muted");
        var root = new DockPanel { Margin = new Thickness(10) };

        // top bar
        var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        bar.Children.Add(new TextBlock { Text = "Formation", Foreground = muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        _formationBox = new ComboBox { Width = 110, ItemsSource = Tactics.Formations.Keys.ToList(), SelectedItem = _tactic.Formation };
        _formationBox.SelectionChanged += (_, _) =>
        {
            if (_formationBox.SelectedItem is string f && f != _tactic.Formation) { _tactic = Tactics.Build(f, _tactic); Tactics.Save(_tactic); _slot = -1; RefreshTactic(); }
        };
        bar.Children.Add(_formationBox);
        var reset = new Button { Content = "Default roles", Margin = new Thickness(8, 0, 0, 0), ToolTip = "Reset every slot to the standard role for its position" };
        reset.Click += (_, _) => { _tactic = Tactics.Build(_tactic.Formation); Tactics.Save(_tactic); RefreshTactic(); };
        bar.Children.Add(reset);
        _tacticNote = new TextBlock { Foreground = muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0), TextWrapping = TextWrapping.Wrap };
        bar.Children.Add(_tacticNote);
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(bar);

        // pitch (left)
        _pitch = new Canvas { Width = 560, Height = 720 };
        var pitchBox = new Viewbox { Child = _pitch, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 12, 0), MaxWidth = 620 };
        DockPanel.SetDock(pitchBox, Dock.Left);
        root.Children.Add(pitchBox);

        // slot panel (right)
        var right = new DockPanel();
        var head = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        _slotTitle = new TextBlock { FontSize = 16, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), Text = "Click a slot on the pitch" };
        head.Children.Add(_slotTitle);
        head.Children.Add(new TextBlock { Text = "Role", Foreground = muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        _slotRoleBox = new ComboBox { Width = 330, DisplayMemberPath = "Display", IsEnabled = false };
        _slotRoleBox.SelectionChanged += (_, _) =>
        {
            if (_slot < 0 || _slotRoleBox.SelectedItem is not RoleDef r || _tactic.Slots[_slot].Role == r.Code) return;
            _tactic.Slots[_slot].Role = r.Code; Tactics.Save(_tactic); RefreshTactic(keepSlot: true);
        };
        head.Children.Add(_slotRoleBox);
        DockPanel.SetDock(head, Dock.Top);
        right.Children.Add(head);

        var tabs = new TabControl();
        _depthGrid = CandidateGrid(includeClub: false);
        _slGrid = CandidateGrid(includeClub: true);
        _sugGrid = CandidateGrid(includeClub: true);
        tabs.Items.Add(new TabItem { Header = "Squad depth", Content = _depthGrid });
        tabs.Items.Add(new TabItem { Header = "★ Shortlist", Content = _slGrid });

        var sug = new DockPanel();
        var crit = new WrapPanel { Margin = new Thickness(0, 4, 0, 6) };
        TextBox Num(string label, string tip, string init = "", double w = 54)
        {
            crit.Children.Add(new TextBlock { Text = label, Foreground = muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 4, 0) });
            var tb = new TextBox { Width = w, Text = init, ToolTip = tip };
            tb.TextChanged += (_, _) => RefreshSlotLists();
            crit.Children.Add(tb);
            return tb;
        }
        CheckBox Chk(string label, string tip, bool on)
        {
            var cb = new CheckBox { Content = label, IsChecked = on, ToolTip = tip, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            cb.Click += (_, _) => RefreshSlotLists();
            crit.Children.Add(cb);
            return cb;
        }
        crit.Children.Add(new TextBlock { Text = "From", Foreground = muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        _sugScope = new ComboBox { Width = 140, ItemsSource = new[] { "Whole database", "Shortlist only" }, SelectedIndex = 0 };
        _sugScope.SelectionChanged += (_, _) => RefreshSlotLists();
        crit.Children.Add(_sugScope);
        _sugFee = Num("Max fee £M", "Highest likely fee, in £ millions (blank = any)");
        _sugWage = Num("Max wage £K", "Highest weekly wage, in £ thousands (blank = any)");
        _sugAge = Num("Max age", "Oldest age to show (blank = any)", "", 40);
        _sugJoin = Num("Min join %", "Only players at least this likely to join you", "40", 40);
        _sugTop = Num("Top", "How many to show", "50", 40);
        _sugPot = Chk("Rank by potential", "Rank and compare on the role score at his potential instead of now", false);
        _sugUpOnly = Chk("Upgrades only", "Only players who beat your starter in this role", true);
        _sugTrn = Chk("Exclude transfer arranged", "Hide players with an agreed move elsewhere", true);
        _sugWp = Chk("WP OK", "Hide players whose work permit is unlikely", true);
        DockPanel.SetDock(crit, Dock.Top);
        sug.Children.Add(crit);
        _sugNote = new TextBlock { Foreground = muted, Margin = new Thickness(2, 0, 0, 4), TextWrapping = TextWrapping.Wrap };
        DockPanel.SetDock(_sugNote, Dock.Top);
        sug.Children.Add(_sugNote);
        sug.Children.Add(_sugGrid);
        tabs.Items.Add(new TabItem { Header = "Suggestions", Content = sug });
        right.Children.Add(tabs);
        root.Children.Add(right);

        TacticHost.Child = root;
    }

    DataGrid CandidateGrid(bool includeClub)
    {
        var g = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, SelectionMode = DataGridSelectionMode.Extended, HeadersVisibility = DataGridHeadersVisibility.Column };
        void Col(string h, string path, double w = double.NaN, string? sort = null, string? fmt = null)
        {
            var b = new System.Windows.Data.Binding(path); if (fmt != null) b.StringFormat = fmt;
            var c = new DataGridTextColumn { Header = h, Binding = b, SortMemberPath = sort ?? path };
            if (!double.IsNaN(w)) c.Width = w;
            g.Columns.Add(c);
        }
        Col("Name", "Name", 170);
        if (includeClub) Col("Club", "Club", 150);
        Col("Age", "Age"); Col("Pos", "Pos", 110);
        Col("Score", "Score", fmt: "F0"); Col("Pot.", "Pot", fmt: "F0"); Col("vs starter", "GainText", sort: "Gain");
        Col("CA", "CA"); Col("PA", "PA");
        if (includeClub) { Col("Join", "Join", sort: "JoinSort"); Col("Fee", "Fee", sort: "FeeSort"); }
        Col("Wage", "Wage", sort: "WageSort");
        if (includeClub) { Col("WP", "Wp", 120); Col("Transfer arranged", "Trn", 170); }
        Col("", "Note", 160);
        g.MouseDoubleClick += (_, e) => { if (!IsHeader(e) && g.SelectedItem is SlotCandidate c) OpenProfileWindow(c.P); };
        g.PreviewMouseRightButtonDown += (_, e) =>
        {
            var row = ItemsControl.ContainerFromElement(g, (DependencyObject)e.OriginalSource) as DataGridRow;
            if (row == null) { g.ContextMenu = null; return; }
            if (!row.IsSelected) { g.SelectedItems.Clear(); row.IsSelected = true; }
            var sel = g.SelectedItems.OfType<SlotCandidate>().Select(c => c.P).ToList();
            g.ContextMenu = BuildRowMenu(((SlotCandidate)row.Item).P, sel);
        };
        return g;
    }

    void ShowTacticMode(bool on)
    {
        if (on) BuildTacticView();
        TacticHost.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        SquadGrid.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        if (on) RefreshTactic(keepSlot: true);
    }

    /// <summary>Recomputes starters for the selected squad and redraws the pitch and slot lists.</summary>
    void RefreshTactic(bool keepSlot = false)
    {
        if (!_tacticBuilt || TacticHost.Visibility != Visibility.Visible) return;
        _formationBox.SelectedItem = _tactic.Formation;
        _tacticSquad = (SquadGrid.ItemsSource as List<PlayerRow> ?? []).Where(p => p.TeamName == "First team" || SquadAllTeams.IsChecked == true).ToList();
        _xi = Tactics.PickXi(_tactic, _tacticSquad, _roles);
        _tacticNote.Text = _all.Count == 0 ? "Press Load from game first."
            : $"Formation and roles are set here (click a slot to change its role). Starters are picked from your squad by score in each slot's role.";
        if (!keepSlot || _slot >= _tactic.Slots.Count) _slot = -1;
        DrawPitch();
        RefreshSlotLists();
    }

    int RoleIx(TacticSlot s) => Math.Max(0, _roles.IndexOf(s.Role));

    void DrawPitch()
    {
        var c = _pitch; c.Children.Clear();
        const double W = 560, H = 720;
        var chalk = new SolidColorBrush(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF));
        c.Children.Add(new Rectangle { Width = W, Height = H, Fill = new SolidColorBrush(Color.FromRgb(0x1D, 0x47, 0x2B)), RadiusX = 8, RadiusY = 8 });
        void R(double x, double y, double w, double h) { var r = new Rectangle { Width = w, Height = h, Stroke = chalk, StrokeThickness = 1.5 }; Canvas.SetLeft(r, x); Canvas.SetTop(r, y); c.Children.Add(r); }
        R(10, 10, W - 20, H - 20); R(W / 2 - 110, 10, 220, 90); R(W / 2 - 110, H - 100, 220, 90);
        c.Children.Add(new Line { X1 = 10, X2 = W - 10, Y1 = H / 2, Y2 = H / 2, Stroke = chalk, StrokeThickness = 1.5 });
        var ci = new Ellipse { Width = 120, Height = 120, Stroke = chalk, StrokeThickness = 1.5 }; Canvas.SetLeft(ci, W / 2 - 60); Canvas.SetTop(ci, H / 2 - 60); c.Children.Add(ci);

        for (int i = 0; i < _tactic.Slots.Count; i++)
        {
            var s = _tactic.Slots[i]; int ri = RoleIx(s); var role = _roles.Roles[ri];
            var starter = i < _xi.Length ? _xi[i] : null;
            var backup = _tacticSquad.Where(p => p != starter).OrderByDescending(p => p.RoleScores[ri]).FirstOrDefault();
            double sc = starter?.RoleScores[ri] ?? 0;
            var card = new Border
            {
                Width = 128, CornerRadius = new CornerRadius(6), Padding = new Thickness(7, 4, 7, 5), Cursor = Cursors.Hand,
                Background = new SolidColorBrush(Color.FromArgb(0xE8, 0x16, 0x19, 0x21)),
                BorderBrush = i == _slot ? Brushes.Gold : ScoreBrush.For(sc), BorderThickness = new Thickness(i == _slot ? 2.5 : 1.5),
                ToolTip = $"{s.Pos} · {role.Name}\nStarter: {starter?.Name ?? "-"} {sc:F0}\nBackup: {backup?.Name ?? "-"} {(backup != null ? backup.RoleScores[ri].ToString("F0") : "")}",
            };
            var sp = new StackPanel();
            var top = new DockPanel();
            var pos = new TextBlock { Text = s.Pos, FontSize = 11, FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("Muted") };
            DockPanel.SetDock(pos, Dock.Right); top.Children.Add(pos);
            top.Children.Add(new TextBlock { Text = role.Code, FontSize = 11, Foreground = (Brush)FindResource("Muted"), TextTrimming = TextTrimming.CharacterEllipsis });
            sp.Children.Add(top);
            var nm = new DockPanel();
            var v = new TextBlock { Text = starter == null ? "" : $"{sc:F0}", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = ScoreBrush.For(sc), Margin = new Thickness(4, 0, 0, 0) };
            DockPanel.SetDock(v, Dock.Right); nm.Children.Add(v);
            nm.Children.Add(new TextBlock { Text = starter?.Name ?? "(nobody)", FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("Fg"), TextTrimming = TextTrimming.CharacterEllipsis });
            sp.Children.Add(nm);
            if (backup != null)
            {
                var bk = new DockPanel();
                var bv = new TextBlock { Text = $"{backup.RoleScores[ri]:F0}", FontSize = 11, Foreground = ScoreBrush.For(backup.RoleScores[ri]) };
                DockPanel.SetDock(bv, Dock.Right); bk.Children.Add(bv);
                bk.Children.Add(new TextBlock { Text = "↳ " + backup.Name, FontSize = 11, Foreground = (Brush)FindResource("Muted"), TextTrimming = TextTrimming.CharacterEllipsis });
                sp.Children.Add(bk);
            }
            card.Child = sp;
            int idx = i;
            card.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2 && _xi.ElementAtOrDefault(idx) is PlayerRow pr) OpenProfileWindow(pr); else { _slot = idx; DrawPitch(); RefreshSlotLists(); } };
            Canvas.SetLeft(card, Math.Clamp(s.X * W - 64, 4, W - 132)); Canvas.SetTop(card, Math.Clamp(s.Y * H - 30, 4, H - 70));
            c.Children.Add(card);
        }
    }

    void RefreshSlotLists()
    {
        if (!_tacticBuilt) return;
        if (_slot < 0)
        {
            _slotTitle.Text = "Click a slot on the pitch"; _slotRoleBox.IsEnabled = false; _slotRoleBox.ItemsSource = null;
            _depthGrid.ItemsSource = _slGrid.ItemsSource = null;
            _sugGrid.ItemsSource = WeakestSlotSuggestions(); _sugNote.Text = "No slot selected: showing the best pick for each slot, weakest slots first. Click a slot to see more for it.";
            return;
        }
        var s = _tactic.Slots[_slot]; int ri = RoleIx(s);
        var starter = _xi.ElementAtOrDefault(_slot);
        bool usePot = _sugPot.IsChecked == true;
        double baseScore = starter == null ? 0 : usePot ? starter.PotentialRoleScores[ri] : starter.RoleScores[ri];
        _slotTitle.Text = $"{s.Pos}: {starter?.Name ?? "nobody"}" + (starter != null ? $" ({starter.RoleScores[ri]:F0})" : "");
        // role picker: roles for this position first
        var roleList = _roles.Roles.OrderBy(r => r.Positions.Contains(s.Pos, StringComparer.OrdinalIgnoreCase) ? 0 : 1).ToList();
        _slotRoleBox.IsEnabled = true;
        if (!ReferenceEquals(_slotRoleBox.Tag, s)) { _slotRoleBox.Tag = s; _slotRoleBox.ItemsSource = roleList; }
        _slotRoleBox.SelectedItem = _roles.Roles[ri];

        SlotCandidate C(PlayerRow p, string note = "")
        {
            double sc = p.RoleScores[ri], pot = p.PotentialRoleScores[ri];
            return new SlotCandidate { P = p, Score = sc, Pot = pot, Gain = (usePot ? pot : sc) - baseScore, Note = note };
        }
        _depthGrid.ItemsSource = _tacticSquad.OrderByDescending(p => p.RoleScores[ri])
            .Select(p => C(p, p == starter ? "starter" : _xi.Contains(p) ? $"starts at {_tactic.Slots[Array.IndexOf(_xi, p)].Pos}" : "")).ToList();
        _slGrid.ItemsSource = _all.Where(p => Shortlist.Contains(p.Uid)).OrderByDescending(p => usePot ? p.PotentialRoleScores[ri] : p.RoleScores[ri]).Select(p => C(p)).ToList();

        var list = Suggest(ri, baseScore, usePot).Take(TopN()).Select(p => C(p)).ToList();
        _sugGrid.ItemsSource = list;
        _sugNote.Text = $"{list.Count} suggestion(s) for {s.Pos} as {_roles.Roles[ri].Name}" + (starter != null ? $", compared with {starter.Name} ({baseScore:F0}{(usePot ? " at potential" : "")})" : "") + ".";
    }

    int TopN() => int.TryParse(_sugTop.Text, out var n) && n > 0 ? Math.Min(n, 500) : 50;

    IEnumerable<PlayerRow> Suggest(int ri, double baseScore, bool usePot)
    {
        double? D(TextBox t) => double.TryParse(t.Text.Trim(), out var v) ? v : null;
        double? maxFee = D(_sugFee) * 1e6, maxWage = D(_sugWage) * 1e3, maxAge = D(_sugAge), minJoin = D(_sugJoin);
        var my = ClubContext.MyClubName;
        IEnumerable<PlayerRow> pool = _sugScope.SelectedIndex == 1 ? _all.Where(p => Shortlist.Contains(p.Uid)) : _all;
        return pool.Where(p => p.Club != my && p.CanSign
                && (maxFee == null || (!double.IsNaN(p.Fee) && p.Fee <= maxFee))
                && (maxWage == null || Math.Max(p.Wage, p.TypicalWage) <= maxWage)
                && (maxAge == null || (p.Age ?? 99) <= maxAge)
                && (minJoin == null || p.Join >= minJoin)
                && (_sugTrn.IsChecked != true || !p.HasArranged)
                && (_sugWp.IsChecked != true || !p.WpText.Contains("unlikely"))
                && (_sugUpOnly.IsChecked != true || (usePot ? p.PotentialRoleScores[ri] : p.RoleScores[ri]) > baseScore))
            .OrderByDescending(p => usePot ? p.PotentialRoleScores[ri] : p.RoleScores[ri]);
    }

    /// <summary>Best suggestion per slot, slots with the weakest starters first.</summary>
    List<SlotCandidate> WeakestSlotSuggestions()
    {
        var res = new List<SlotCandidate>();
        bool usePot = _sugPot.IsChecked == true;
        var order = Enumerable.Range(0, _tactic.Slots.Count).OrderBy(i => _xi.ElementAtOrDefault(i)?.RoleScores[RoleIx(_tactic.Slots[i])] ?? 0);
        var taken = new HashSet<PlayerRow>();
        foreach (var i in order)
        {
            var s = _tactic.Slots[i]; int ri = RoleIx(s); var st = _xi.ElementAtOrDefault(i);
            double b = st == null ? 0 : usePot ? st.PotentialRoleScores[ri] : st.RoleScores[ri];
            var pick = Suggest(ri, b, usePot).FirstOrDefault(p => !taken.Contains(p));
            if (pick == null) continue;
            taken.Add(pick);
            res.Add(new SlotCandidate { P = pick, Score = pick.RoleScores[ri], Pot = pick.PotentialRoleScores[ri], Gain = (usePot ? pick.PotentialRoleScores[ri] : pick.RoleScores[ri]) - b,
                Note = $"{s.Pos} {_roles.Roles[ri].Code} (now {st?.Name ?? "nobody"} {b:F0})" });
        }
        return res;
    }

    internal FrameworkElement? SelfTestTactic()
    {
        SquadModeTactic.IsChecked = true; ShowTacticMode(true);
        _slot = 8; DrawPitch(); RefreshSlotLists();
        return TacticHost;
    }
}
