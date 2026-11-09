using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using FM27Scout.Model;

namespace FM27Scout;

public partial class MainWindow : Window
{
    sealed class ColumnSpec
    {
        public required string Key, Header, Group;
        public required Func<PlayerRow, IComparable?> SortKey;
        public required DataGridColumn Column;
        public bool DefaultVisible;
    }

    RoleSet _roles = null!;
    JoinModel _join = null!;
    List<MyClub> _myClubs = [];
    DateOnly _today = DateOnly.FromDateTime(DateTime.Today);
    List<PlayerRow> _all = [];
    List<PlayerRow> _view = [];
    readonly List<ColumnSpec> _cols = [];
    ColumnSpec? _sortCol;
    ListSortDirection _sortDir = ListSortDirection.Descending;
    List<SavedSearch> _saved = [];
    readonly Dictionary<string, TextBox> _attrBoxes = new();
    readonly List<CheckBox> _posChecks = [];
    DataGridTextColumn? _roleCol, _rolePotCol, _gainCol;
    bool _suppressSaved;

    static readonly string SettingsPath = Path.Combine(AppPaths.Data, "settings.json");

    public MainWindow()
    {
        InitializeComponent();
        var (layouts, _) = GameLoader.LoadLayouts();
        var first = layouts.Values.First();
        Attr.Init(first.AttributeNames);
        Model.Positions.Names = first.PositionNames;
        _roles = RoleSet.LoadUserOrDefault();
        _join = JoinModel.LoadUserOrDefault();

        BuildSidebar();
        BuildColumns();
        LoadSettings();
        RefreshSaved();
        AttachContextMenus();
        RenderCompare();
        QueryBox.TextChanged += (_, _) => QueryHint.Visibility = QueryBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        Closing += (_, _) => SaveSettings();
    }

    // ------------------------------------------------------------------ sidebar
    void BuildSidebar()
    {
        foreach (var p in Model.Positions.Names.Where(n => n != "SW"))
        {
            var cb = new CheckBox { Content = p, Width = 58, Tag = p };
            _posChecks.Add(cb);
            PositionChecks.Children.Add(cb);
        }
        FRole.ItemsSource = new[] { new RoleDef { Name = "(none)", Code = "-" } }.Concat(_roles.Roles).ToList();
        FRole.SelectedIndex = 0;

        void Group(string title, string[] names)
        {
            AttrFilterPanel.Children.Add(new TextBlock { Text = title, Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(0, 8, 0, 2), FontWeight = FontWeights.SemiBold });
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            int row = 0;
            foreach (var n in names)
            {
                if (_attrBoxes.ContainsKey(n)) continue;
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var t = new TextBlock { Text = Attr.Pretty(n), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 0, 1) };
                var tb = new TextBox { Margin = new Thickness(0, 1, 0, 1), Padding = new Thickness(2, 1, 2, 1) };
                Grid.SetRow(t, row); Grid.SetRow(tb, row); Grid.SetColumn(tb, 1);
                g.Children.Add(t); g.Children.Add(tb);
                _attrBoxes[n] = tb;
                row++;
            }
            AttrFilterPanel.Children.Add(g);
        }
        Group("Technical", Attr.Technical);
        Group("Mental", Attr.Mental);
        Group("Physical", Attr.Physical);
        Group("Goalkeeping", Attr.Goalkeeping);
        Group("Hidden", Attr.Hidden);
        Group("Feet", ["LeftFoot", "RightFoot"]);

        foreach (var tb in Sidebar.Children.OfType<Expander>().SelectMany(e => Descendants<TextBox>((DependencyObject)e.Content)))
            tb.KeyDown += (_, e) => { if (e.Key == Key.Enter) ApplyFilters(); };
    }

    static IEnumerable<T> Descendants<T>(DependencyObject d) where T : DependencyObject
    {
        foreach (var c in LogicalTreeHelper.GetChildren(d).OfType<DependencyObject>())
        {
            if (c is T t) yield return t;
            foreach (var x in Descendants<T>(c)) yield return x;
        }
    }

    FilterState ReadFilter()
    {
        int? I(TextBox t) => int.TryParse(t.Text.Trim(), out var v) ? v : null;
        double? D(TextBox t) => double.TryParse(t.Text.Trim(), out var v) ? v : null;
        string? S(TextBox t) => string.IsNullOrWhiteSpace(t.Text) ? null : t.Text.Trim();
        var role = FRole.SelectedItem as RoleDef;
        return new FilterState
        {
            Name = S(FName), AgeMin = I(FAgeMin), AgeMax = I(FAgeMax), CaMin = I(FCaMin), CaMax = I(FCaMax),
            PaMin = I(FPaMin), PaMax = I(FPaMax), Nation = S(FNation), HeightMin = I(FHeight),
            Positions = _posChecks.Where(c => c.IsChecked == true).Select(c => (string)c.Tag).ToList(),
            PositionMin = FPosMin.SelectedIndex switch { 0 => 20, 2 => 10, 3 => 5, _ => 15 },
            Role = role is { Code: not "-" } ? role.Code : null, RoleMin = D(FRoleMin), RolePotentialMin = D(FRolePotMin),
            Club = S(FClub), League = S(FLeague), ValueMax = S(FValueMax), WageMax = S(FWageMax), ContractExpiresBy = I(FContract),
            FreeAgentsOnly = FFree.IsChecked == true, GettableOnly = FGettable.IsChecked == true, ShortlistOnly = FShortlist.IsChecked == true,
            AttributeMin = _attrBoxes.Where(kv => int.TryParse(kv.Value.Text, out var v) && v > 0).ToDictionary(kv => kv.Key, kv => int.Parse(kv.Value.Text)),
            Query = QueryBox.Text.Trim(),
            UpgradeForUid = _upgrade?.UpgradeForUid, UpgradeForName = _upgrade?.UpgradeForName, UpgradeMinScore = _upgrade?.UpgradeMinScore ?? 0,
            UpgradeMinCa = _upgrade?.UpgradeMinCa ?? 0, UpgradeMinPa = _upgrade?.UpgradeMinPa ?? 0, UpgradeClub = _upgrade?.UpgradeClub,
            UpgradeIncScore = _upgrade?.UpgradeIncScore ?? 0, UpgradeIncPot = _upgrade?.UpgradeIncPot ?? 0, UpgradeIncCa = _upgrade?.UpgradeIncCa ?? 0, UpgradeIncPa = _upgrade?.UpgradeIncPa ?? 0,
        };
    }

    void WriteFilter(FilterState f)
    {
        string N(int? v) => v?.ToString() ?? "";
        FName.Text = f.Name ?? ""; FAgeMin.Text = N(f.AgeMin); FAgeMax.Text = N(f.AgeMax); FCaMin.Text = N(f.CaMin); FCaMax.Text = N(f.CaMax);
        FPaMin.Text = N(f.PaMin); FPaMax.Text = N(f.PaMax); FNation.Text = f.Nation ?? ""; FHeight.Text = N(f.HeightMin);
        foreach (var c in _posChecks) c.IsChecked = f.Positions.Contains((string)c.Tag);
        FPosMin.SelectedIndex = f.PositionMin switch { 20 => 0, 10 => 2, 5 => 3, _ => 1 };
        int ri = f.Role == null ? -1 : _roles.IndexOf(f.Role);
        FRole.SelectedIndex = ri + 1;
        FRoleMin.Text = f.RoleMin?.ToString() ?? ""; FRolePotMin.Text = f.RolePotentialMin?.ToString() ?? "";
        FClub.Text = f.Club ?? ""; FLeague.Text = f.League ?? ""; FValueMax.Text = f.ValueMax ?? ""; FWageMax.Text = f.WageMax ?? "";
        FContract.Text = N(f.ContractExpiresBy); FFree.IsChecked = f.FreeAgentsOnly; FGettable.IsChecked = f.GettableOnly; FShortlist.IsChecked = f.ShortlistOnly;
        foreach (var (k, tb) in _attrBoxes) tb.Text = f.AttributeMin.TryGetValue(k, out var v) ? v.ToString() : "";
        QueryBox.Text = f.Query;
    }

    void Apply_Click(object s, RoutedEventArgs e) => ApplyFilters();
    void Reset_Click(object s, RoutedEventArgs e) { _upgrade = null; WriteFilter(new FilterState()); ApplyFilters(); }
    void Query_KeyDown(object s, KeyEventArgs e) { if (e.Key == Key.Enter) ApplyFilters(); }

    void ApplyFilters()
    {
        if (_all.Count == 0) return;
        Func<PlayerRow, bool> pred;
        var f = ReadFilter();
        try { pred = FilterBuilder.Build(f, _roles); }
        catch (Exception ex) when (ex is QueryError or FormatException)
        {
            StatusText.Text = "Query error: " + ex.Message;
            QueryBox.BorderBrush = Brushes.IndianRed;
            return;
        }
        QueryBox.ClearValue(BorderBrushProperty);

        // selected role -> fill the per-row "selected" score used by the Role columns
        int ri = f.Role == null ? -1 : _roles.IndexOf(f.Role);
        foreach (var p in _all)
        {
            p.SelectedRoleScore = ri >= 0 ? p.RoleScores[ri] : 0;
            p.SelectedPotentialScore = ri >= 0 ? p.PotentialRoleScores[ri] : 0;
        }

        if (_upgrade == null) foreach (var p in _all) { p.Gain = 0; p.GainText = ""; }
        _view = _all.Where(pred).ToList();
        if (_gainCol != null) _gainCol.Visibility = _upgrade != null ? Visibility.Visible : Visibility.Collapsed;
        SortView();
        CountText.Text = (_upgrade != null ? $"Upgrades for {_upgrade.UpgradeForName} as {FRole.SelectedItem} (his {_upgrade.UpgradeIncScore:F0}/{_upgrade.UpgradeIncPot:F0}): +3 now with higher CA, or under-23 +5 at potential with higher PA · Join ≥ 50 · Reset to clear · " : "")
                         + $"{_view.Count:N0} of {_all.Count:N0} players";
    }

    void Role_Changed(object s, SelectionChangedEventArgs e)
    {
        if (_roleCol == null) return;
        var r = FRole.SelectedItem as RoleDef;
        bool on = r is { Code: not "-" };
        _roleCol.Header = on ? r!.Code : "Role";
        _rolePotCol!.Header = on ? $"Pot {r!.Code}" : "Pot. role";
        _roleCol.Visibility = _rolePotCol.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        ApplyFilters();
        if (on) { _sortCol = _cols.First(c => c.Key == "role"); _sortDir = ListSortDirection.Descending; SortView(); }
    }

    // ------------------------------------------------------------------ columns & sorting
    void BuildColumns()
    {
        var attrStyle = new Style(typeof(TextBlock));
        attrStyle.Setters.Add(new Setter(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center));
        var scoreStyle = new Style(typeof(TextBlock));
        scoreStyle.Setters.Add(new Setter(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Right));

        DataGridTextColumn Text(string key, string header, string group, string path, Func<PlayerRow, IComparable?> sort,
            bool visible = true, double width = double.NaN, string? fmt = null, IValueConverter? conv = null, Style? style = null, IValueConverter? fg = null)
        {
            var b = new Binding(path) { Mode = BindingMode.OneTime, StringFormat = fmt, Converter = conv };
            var col = new DataGridTextColumn { Header = header, Binding = b, SortMemberPath = key, IsReadOnly = true };
            if (!double.IsNaN(width)) col.Width = width;
            var st = new Style(typeof(TextBlock), style);
            if (fg != null) st.Setters.Add(new Setter(TextBlock.ForegroundProperty, new Binding(path) { Mode = BindingMode.OneTime, Converter = fg }));
            col.ElementStyle = st;
            col.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            _cols.Add(new ColumnSpec { Key = key, Header = header, Group = group, SortKey = sort, Column = col, DefaultVisible = visible });
            PlayerGrid.Columns.Add(col);
            return col;
        }

        Text("name", "Name", "Basics", nameof(PlayerRow.Name), p => p.Name, width: 190);
        _gainCol = Text("gain", "Gain", "Roles", nameof(PlayerRow.GainText), p => p.Gain, visible: false, width: 110);
        Text("age", "Age", "Basics", nameof(PlayerRow.Age), p => p.Age);
        Text("pos", "Position", "Basics", nameof(PlayerRow.PositionsText), p => p.PositionsText, width: 110);
        Text("club", "Club", "Basics", nameof(PlayerRow.Club), p => p.Club, width: 160);
        Text("league", "League", "Basics", nameof(PlayerRow.League), p => p.League, width: 130);
        Text("nation", "Nation", "Basics", nameof(PlayerRow.Nation), p => p.Nation, width: 110);
        Text("ca", "CA", "Basics", nameof(PlayerRow.CA), p => p.CA, style: scoreStyle);
        Text("pa", "PA", "Basics", nameof(PlayerRow.PA), p => p.PA, style: scoreStyle);
        Text("best", "Best role", "Roles", nameof(PlayerRow.BestRole), p => p.BestRole, width: 150);
        Text("bestscore", "Score", "Roles", nameof(PlayerRow.BestRoleScore), p => p.BestRoleScore, fmt: "F0", style: scoreStyle, fg: new ScoreBrush());
        Text("bestpot", "Pot.", "Roles", nameof(PlayerRow.BestPotentialScore), p => p.BestPotentialScore, fmt: "F0", style: scoreStyle, fg: new ScoreBrush());
        _roleCol = Text("role", "Role", "Roles", nameof(PlayerRow.SelectedRoleScore), p => p.SelectedRoleScore, visible: false, fmt: "F0", style: scoreStyle, fg: new ScoreBrush());
        _rolePotCol = Text("rolepot", "Pot. role", "Roles", nameof(PlayerRow.SelectedPotentialScore), p => p.SelectedPotentialScore, visible: false, fmt: "F0", style: scoreStyle, fg: new ScoreBrush());
        Text("join", "Join %", "Would join", nameof(PlayerRow.Join), p => p.Join, fmt: "F0", style: scoreStyle, fg: new ScoreBrush())
            .HeaderStyle = HeaderTip("Estimated chance he would join your club (0-100). See the profile for reasons.");
        Text("joinlabel", "Would join?", "Would join", nameof(PlayerRow.JoinLabel), p => p.Join, width: 95);
        Text("regen", "Newgen", "Basics", nameof(PlayerRow.RegenText), p => p.RegenText, visible: true, width: 58)
            .HeaderStyle = HeaderTip("Generated by the game during your save (not a real-world player).");
        Text("joined", "Joined", "Club & contract", nameof(PlayerRow.Joined), p => p.Joined, visible: false, fmt: "dd/MM/yyyy")
            .HeaderStyle = HeaderTip("Start of his current contract (for youth-intake kids this is the intake date).");
        Text("vfm", "Value for money", "Club & contract", nameof(PlayerRow.Vfm), p => p.Vfm, visible: false, fmt: "F1", style: scoreStyle)
            .HeaderStyle = HeaderTip("Best potential role score per £1M of fee + 2 years of wages (no fee for free agents / contracts ending within 6 months).");
        Text("bargain", "Bargain", "Club & contract", nameof(PlayerRow.BargainText), p => p.IsBargain ? 1 - Math.Min(1, p.ValueRatio) : -1, width: 120)
            .HeaderStyle = HeaderTip("Free agent, contract ending within 6 months, or valued at half (or less) of similar players (same CA band and age).");
        Text("vr", "Value vs typical", "Club & contract", nameof(PlayerRow.ValueRatio), p => p.ValueRatio, visible: false, fmt: "P0", style: scoreStyle)
            .HeaderStyle = HeaderTip("His value divided by the median value of players with similar CA and age. Below 50% = bargain.");
        Text("status", "Squad status", "Club & contract", nameof(PlayerRow.SquadStatusName), p => p.SquadStatusName, visible: false, width: 120);
        Text("rep", "Rep", "Reputation", nameof(PlayerRow.CurrentRep), p => p.CurrentRep, visible: false, style: scoreStyle);
        Text("hrep", "Home rep", "Reputation", nameof(PlayerRow.HomeRep), p => p.HomeRep, visible: false, style: scoreStyle);
        Text("wrep", "World rep", "Reputation", nameof(PlayerRow.WorldRep), p => p.WorldRep, visible: false, style: scoreStyle);
        Text("clubrep", "Club rep", "Reputation", nameof(PlayerRow.ClubRep), p => p.ClubRep, visible: false, style: scoreStyle);
        Text("value", "Value", "Club & contract", nameof(PlayerRow.Value), p => p.Value, conv: new MoneyText(), style: scoreStyle);
        Text("wage", "Wage p/w", "Club & contract", nameof(PlayerRow.Wage), p => p.Wage, conv: new MoneyText(), style: scoreStyle);
        Text("expiry", "Expires", "Club & contract", nameof(PlayerRow.ContractExpiry), p => p.ContractExpiry, fmt: "dd/MM/yyyy");
        Text("height", "Height", "Basics", nameof(PlayerRow.Height), p => p.Height, visible: false);
        Text("foot", "Foot", "Basics", nameof(PlayerRow.Foot), p => p.Foot, visible: false);
        Text("dob", "Born", "Basics", nameof(PlayerRow.DateOfBirth), p => p.DateOfBirth, visible: false, fmt: "dd/MM/yyyy");
        Text("uid", "UID", "Basics", nameof(PlayerRow.Uid), p => p.Uid, visible: false);

        void Attrs(string group, IEnumerable<string> names, bool visible)
        {
            foreach (var n in names)
            {
                int i = Attr.Index(n);
                if (i < 0 || _cols.Any(c => c.Key == "a." + n)) continue;
                Text("a." + n, Abbrev(n), group, $"A[{i}]", p => p.A[i], visible, width: 38, style: attrStyle, fg: new AttrBrush())
                    .HeaderStyle = HeaderTip(Attr.Pretty(n));
            }
        }
        Attrs("Technical", Attr.Technical, true);
        Attrs("Mental", Attr.Mental, true);
        Attrs("Physical", Attr.Physical, true);
        Attrs("Goalkeeping", Attr.Goalkeeping, false);
        Attrs("Hidden", Attr.Hidden, false);
        Attrs("Feet", ["LeftFoot", "RightFoot"], false);

        for (int r = 0; r < _roles.Roles.Length; r++)
        {
            int ri = r;
            var role = _roles.Roles[r];
            Text("r." + role.Code, role.Code, "Role scores", $"RoleScores[{ri}]", p => p.RoleScores[ri], false, fmt: "F0", style: scoreStyle, fg: new ScoreBrush())
                .HeaderStyle = HeaderTip(role.Name);
        }
    }

    Style HeaderTip(string tip)
    {
        var s = new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader), (Style)FindResource(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader)));
        s.Setters.Add(new Setter(ToolTipProperty, tip));
        return s;
    }

    static string Abbrev(string n) => n switch
    {
        "Corners" => "Cor", "Crossing" => "Cro", "Dribbling" => "Dri", "Finishing" => "Fin", "FirstTouch" => "Fir", "FreeKicks" => "Fre",
        "Heading" => "Hea", "LongShots" => "Lon", "LongThrows" => "L Th", "Marking" => "Mar", "Passing" => "Pas", "Penalties" => "Pen",
        "Tackling" => "Tck", "Technique" => "Tec", "Aggression" => "Agg", "Anticipation" => "Ant", "Bravery" => "Bra", "Composure" => "Cmp",
        "Concentration" => "Cnt", "Decisions" => "Dec", "Determination" => "Det", "Flair" => "Fla", "Leadership" => "Ldr", "OffTheBall" => "OtB",
        "Positioning" => "Pos", "Teamwork" => "Tea", "Vision" => "Vis", "WorkRate" => "Wor", "Acceleration" => "Acc", "Agility" => "Agi",
        "Balance" => "Bal", "JumpingReach" => "Jum", "NaturalFitness" => "Nat", "Pace" => "Pac", "Stamina" => "Sta", "Strength" => "Str",
        "AerialReach" => "Aer", "CommandOfArea" => "Cmd", "Communication" => "Com", "Eccentricity" => "Ecc", "Handling" => "Han",
        "Kicking" => "Kic", "OneOnOnes" => "1v1", "Punching" => "Pun", "Reflexes" => "Ref", "RushingOut" => "TRO", "Throwing" => "Thr",
        "Consistency" => "Cons", "Dirtiness" => "Dirt", "ImportantMatches" => "ImpM", "InjuryProneness" => "Inj", "Versatility" => "Vers",
        "LeftFoot" => "L Ft", "RightFoot" => "R Ft", _ => n[..Math.Min(4, n.Length)]
    };

    void Grid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;
        var spec = _cols.FirstOrDefault(c => c.Column == e.Column);
        if (spec == null) return;
        // first click on a numeric column sorts high->low (what you want when scouting)
        if (_sortCol == spec) _sortDir = _sortDir == ListSortDirection.Ascending ? ListSortDirection.Descending : ListSortDirection.Ascending;
        else _sortDir = spec.Key is "name" or "club" or "league" or "nation" or "pos" or "age" or "expiry" or "value" or "wage" ? ListSortDirection.Ascending : ListSortDirection.Descending;
        _sortCol = spec;
        SortView();
    }

    void SortView()
    {
        if (_sortCol != null)
        {
            var key = _sortCol.SortKey;
            var cmp = Comparer<IComparable?>.Create((a, b) => a == null ? (b == null ? 0 : 1) : b == null ? -1 : a.CompareTo(b));
            _view = (_sortDir == ListSortDirection.Ascending ? _view.OrderBy(key, cmp) : _view.OrderByDescending(key, Comparer<IComparable?>.Create((a, b) =>
                a == null ? (b == null ? 0 : -1) : b == null ? 1 : a.CompareTo(b)))).ToList();
            foreach (var c in PlayerGrid.Columns) c.SortDirection = null;
            _sortCol.Column.SortDirection = _sortDir;
        }
        PlayerGrid.ItemsSource = _view;
    }

    void Columns_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        foreach (var g in _cols.Where(c => c.Key is not ("role" or "rolepot")).GroupBy(c => c.Group))
        {
            var sub = new MenuItem { Header = g.Key };
            var all = new MenuItem { Header = "Show all" };
            var none = new MenuItem { Header = "Hide all" };
            all.Click += (_, _) => { foreach (var c in g) c.Column.Visibility = Visibility.Visible; };
            none.Click += (_, _) => { foreach (var c in g.Where(c => c.Key != "name")) c.Column.Visibility = Visibility.Collapsed; };
            sub.Items.Add(all); sub.Items.Add(none); sub.Items.Add(new Separator());
            foreach (var c in g)
            {
                var spec = c;
                var label = spec.Group is "Technical" or "Mental" or "Physical" or "Goalkeeping" or "Hidden" or "Feet" ? Attr.Pretty(spec.Key[2..])
                          : spec.Group == "Role scores" ? _roles.Roles.First(r => "r." + r.Code == spec.Key).Display : spec.Header;
                var mi = new MenuItem { Header = label, IsCheckable = true, IsChecked = spec.Column.Visibility == Visibility.Visible, StaysOpenOnClick = true };
                mi.Click += (_, _) => spec.Column.Visibility = mi.IsChecked ? Visibility.Visible : Visibility.Collapsed;
                sub.Items.Add(mi);
            }
            menu.Items.Add(sub);
        }
        var reset = new MenuItem { Header = "Reset to defaults" };
        reset.Click += (_, _) => { foreach (var c in _cols) if (c.Key is not ("role" or "rolepot")) c.Column.Visibility = c.DefaultVisible ? Visibility.Visible : Visibility.Collapsed; };
        menu.Items.Add(new Separator()); menu.Items.Add(reset);
        menu.PlacementTarget = ColumnsButton;
        menu.IsOpen = true;
    }

    // ------------------------------------------------------------------ loading
    async void Load_Click(object sender, RoutedEventArgs e)
    {
        LoadButton.IsEnabled = false;
        StatusText.Text = "Reading from FM27…";
        var progress = new Progress<string>(s => StatusText.Text = s);
        try
        {
            _roles = RoleSet.LoadUserOrDefault(); // pick up edited weights on every load
            var roles = _roles;
            _join = JoinModel.LoadUserOrDefault();
            var res = await Task.Run(() => GameLoader.Load(roles, progress));
            _all = res.Players;
            AfterLoad(res);
            StatusText.Text = $"FM27 build {res.Build} · game date {res.GameDate:dd/MM/yyyy} · {res.PeopleInDb:N0} people in DB · " +
                              $"{_all.Count:N0} men's players loaded ({res.ExcludedWomen:N0} women's excluded) in {res.Millis / 1000.0:F1}s · your club: {(res.HumanClubs.Count > 0 ? string.Join(", ", res.HumanClubs) : "not detected")} · layout: {res.LayoutSource}";
            if (_sortCol == null) { _sortCol = _cols.First(c => c.Key == "ca"); _sortDir = ListSortDirection.Descending; }
            ApplyFilters();
            BuildClubs();
        }
        catch (UserFacingException ex)
        {
            StatusText.Text = "Not loaded.";
            MessageBox.Show(this, ex.Message, "FM27 Scout", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusText.Text = "Load failed: " + ex.Message;
            MessageBox.Show(this, ex.ToString(), "FM27 Scout – load failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { LoadButton.IsEnabled = true; }
    }

    // ------------------------------------------------------------------ self-test hooks (headless, used by --selftest)
    internal LoadResult SelfTestLoad()
    {
        var res = GameLoader.Load(_roles);
        _all = res.Players;
        AfterLoad(res);
        _sortCol = _cols.First(c => c.Key == "ca"); _sortDir = ListSortDirection.Descending;
        ApplyFilters();
        BuildClubs();
        return res;
    }

    internal string SelfTestQuery(string q)
    {
        QueryBox.Text = q;
        StatusText.Text = "";
        ApplyFilters();
        if (StatusText.Text.StartsWith("Query error")) return StatusText.Text;
        return $"{_view.Count} players; top: " + string.Join(", ", _view.Take(3).Select(p => $"{p.Name} ({p.Age}, CA {p.CA}/PA {p.PA}, {p.PositionsText}, {p.Club}, best {p.BestRole} {p.BestRoleScore:F0}/{p.BestPotentialScore:F0})"));
    }

    internal string SelfTestSample(string name)
    {
        var p = _all.Where(x => x.Name.Contains(name, StringComparison.OrdinalIgnoreCase)).OrderByDescending(x => x.CA).FirstOrDefault();
        if (p == null) return name + ": not found";
        var top = _roles.Roles.Select((r, i) => (r, i)).OrderByDescending(t => p.RoleScores[t.i]).Take(5).Select(t => $"{t.r.Code} {p.RoleScores[t.i]:F0}/{p.PotentialRoleScores[t.i]:F0}");
        return $"{p.Name}: {p.PositionsText} | {p.Club} ({p.League}) | value {MoneyText.Format(p.Value)} wage {MoneyText.Format(p.Wage)} | roles {string.Join(", ", top)}";
    }

    internal string SelfTestJoin()
    {
        var my = MyClubCombo.SelectedItem as MyClub;
        var sb = new System.Text.StringBuilder($"My club: {my} rep {my?.Reputation} top wage {my?.TopWage} squad value {my?.SquadValue}\n");
        foreach (var g in _all.GroupBy(p => p.JoinLabel)) sb.AppendLine($"  {g.Key}: {g.Count():N0}");
        foreach (var n in new[] { "Haaland", "Jacob Greaves" })
        {
            var p = _all.Where(x => x.Name.Contains(n)).OrderByDescending(x => x.CA).FirstOrDefault();
            if (p != null) sb.AppendLine($"  {p.Name} ({p.Club}): {p.JoinLabel} {p.Join:F0} | " + string.Join(" | ", p.JoinReasons));
        }
        foreach (var p in _all.Where(x => x.Join >= 60 && x.CA >= 120).OrderByDescending(x => x.CA).Take(5))
            sb.AppendLine($"  {p.Name} ({p.Age}, {p.Club}, CA {p.CA}, rep {p.CurrentRep}): {p.JoinLabel} {p.Join:F0} | " + string.Join(" | ", p.JoinReasons.Take(5)));
        return sb.ToString();
    }

    internal string SelfTestRegens()
    {
        var sb = new System.Text.StringBuilder($"newgens (uid >= {GameLoader.NewgenUidFrom:N0}) {_all.Count(p => p.IsRegen):N0} of {_all.Count:N0}; check: ");
        foreach (var n in new[] { "Lamine Yamal", "Erling Haaland", "Bart Verbruggen", "Jonas Urbig", "Javier Ortiz", "Ronnie Lede", "Diego Mejía" })
        { var p = _all.FirstOrDefault(x => x.Name == n); sb.Append($"{n}={(p?.IsRegen == true ? "newgen" : "real")} uid {p?.Uid} b.{p?.DateOfBirth?.Year}; "); }
        sb.AppendLine();
        var older = _all.Where(p => p.IsRegen && p.DateOfBirth?.Year <= 2010).Take(3).Select(p => $"{p.Name} b.{p.DateOfBirth?.Year} uid {p.Uid}");
        sb.AppendLine("  newgens born <=2010 (should be none): " + string.Join(", ", older));
        foreach (var g in _all.Where(p => p.Uid >= 1_990_000_000).GroupBy(p => p.Uid / 250_000).OrderBy(g => g.Key))
            sb.AppendLine($"  uid {g.Key * 0.25:0.00}M+: {g.Count(),6}  born min {g.Min(p => p.DateOfBirth?.Year)} median {g.Select(p => p.DateOfBirth?.Year ?? 0).OrderBy(y => y).ElementAt(g.Count() / 2)}");
        sb.AppendLine("  uid < 1990M: " + _all.Count(p => p.Uid < 1_990_000_000) + " born max " + _all.Where(p => p.Uid < 1_990_000_000).Max(p => p.DateOfBirth?.Year));
        sb.AppendLine("  born 2011-12 split: newgen " + _all.Count(p => p.DateOfBirth?.Year is 2011 or 2012 && p.IsRegen) + " / real " + _all.Count(p => p.DateOfBirth?.Year is 2011 or 2012 && !p.IsRegen));
        sb.AppendLine($"  age<=15: {_all.Count(p => p.Age <= 15)} (no club {_all.Count(p => p.Age <= 15 && p.Source.Club == null)}), age 14 or less: {_all.Count(p => p.Age <= 14)}, joined after game date: {_all.Count(p => p.Joined > _today)}, max uid {_all.Max(p => p.Uid):N0}");
        var joined = _all.Where(p => p.IsRegen && p.Age <= 16 && p.Joined != null).GroupBy(p => p.Joined!.Value.Year).OrderByDescending(g => g.Key).Take(3);
        foreach (var g in joined) sb.AppendLine($"  youth (≤16) joined in {g.Key}: {g.Count()}");
        foreach (var p in _all.Where(p => p.IsRegen && p.Age <= 19).OrderByDescending(p => p.PA).Take(5))
            sb.AppendLine($"  top newgen: {p.Name} ({p.Age}, {p.Club}, {p.PositionsText}) CA {p.CA}/PA {p.PA} best {p.BestRole} {p.BestRoleScore:F0}->{p.BestPotentialScore:F0} join {p.Join:F0} joined {p.Joined:dd/MM/yyyy}");
        foreach (var p in _all.Where(p => p.Join >= 50 && p.CA >= 110).OrderByDescending(p => p.Vfm).Take(5))
            sb.AppendLine($"  value for money: {p.Name} ({p.Age}, {p.Club}) CA {p.CA}/PA {p.PA} {p.BestRole} {p.BestPotentialScore:F0} value {MoneyText.Format(p.Value)} wage {MoneyText.Format(p.Wage)} vfm {p.Vfm:F1} {p.BargainText} join {p.Join:F0}");
        return sb.ToString();
    }

    internal string SelfTestPotential()
    {
        var m = _roles.Potential; if (m == null) return "no model";
        var sb = new System.Text.StringBuilder("Potential model fit (R² per group) and top CA-weighted attributes:\n");
        for (int g = 0; g < PotentialModel.Groups.Length; g++)
        {
            var top = m.Importance[g].Select((v, i) => (v, i)).OrderByDescending(t => t.v).Take(6).Select(t => $"{Attr.Names[t.i]} {t.v:0.0}");
            sb.AppendLine($"  {PotentialModel.Groups[g],-4} R²={m.FitR2[g]:0.000}  {string.Join(", ", top)}");
        }
        foreach (var n in new[] { "Víctor Gutiérrez", "Kieran Bird", "Ronnie Lede" })
        {
            var p = _all.FirstOrDefault(x => x.Name == n);
            if (p != null) sb.AppendLine($"  {p.Name} ({p.Age}, CA {p.CA}/PA {p.PA}): best {p.BestRole} {p.BestRoleScore:F0} -> pot {p.BestPotentialScore:F0}");
        }
        return sb.ToString();
    }

    internal string SelfTestUpgrade()
    {
        var sb = new System.Text.StringBuilder();
        var squad = ((SquadGrid.ItemsSource as List<PlayerRow>) ?? []).Where(p => p.TeamName == "First team").ToList();
        sb.AppendLine($"Squad: {squad.Count} players; top: " + string.Join(", ", squad.Take(5).Select(p => $"{p.Name} ({p.PositionsText}, CA {p.CA}, {p.BestRole} {p.BestRoleScore:F0})")));
        foreach (var p in squad.Take(3))
        {
            int ri = Array.IndexOf(p.RoleScores, p.RoleScores.Max());
            FindUpgrade(p, ri);
            sb.AppendLine($"  upgrade for {p.Name} as {_roles.Roles[ri].Name} ({p.RoleScores[ri]:F0}): {_view.Count} found; top: " +
                string.Join(", ", _view.Take(3).Select(u => $"{u.Name} ({u.Age}, {u.Club}, CA {u.CA}/PA {u.PA}) {u.RoleScores[ri]:F0}/{u.PotentialRoleScores[ri]:F0} {u.GainText}, join {u.Join:F0}")));
            if (_compare.Count < 4) { _compare.Add(p); if (_view.Count > 0) _compare.Add(_view[0]); }
        }
        RenderCompare();
        _upgrade = null; WriteFilter(new FilterState()); ApplyFilters();
        return sb.ToString();
    }

    internal void SelectTab(int i) => Tabs.SelectedIndex = i;

    internal void SelfTestSelect(int i) { if (_view.Count > i) { PlayerGrid.SelectedIndex = i; ShowProfile(_view[i]); } }

    // ------------------------------------------------------------------ squad, comparison, upgrades
    FilterState? _upgrade;
    readonly List<PlayerRow> _compare = [];
    const double UpgradeMargin = 3;

    bool _suppressSquad;
    void BuildSquad()
    {
        _suppressSquad = true;
        var my = MyClubCombo.SelectedItem as MyClub;
        if (SquadClubCombo.ItemsSource == null || !ReferenceEquals(SquadClubCombo.ItemsSource, _myClubs)) SquadClubCombo.ItemsSource = _myClubs;
        if (SquadClubCombo.SelectedItem == null || SquadClubCombo.SelectedItem is MyClub s && !_myClubs.Contains(s)) SquadClubCombo.SelectedItem = my;
        _suppressSquad = false;
        ShowSquad();
    }

    void SquadClub_Changed(object sender, SelectionChangedEventArgs e) { if (!_suppressSquad) ShowSquad(); }
    void SquadAll_Click(object sender, RoutedEventArgs e) => ShowSquad();

    void ShowSquad()
    {
        if (SquadClubCombo.SelectedItem is not MyClub club) { SquadGrid.ItemsSource = null; return; }
        var players = _all.Where(p => p.Club == club.Name).ToList();
        // first team = the club's team with the highest average CA; others are reserves / youth
        var teams = players.GroupBy(p => p.Source.TeamAddress).Select(g => (team: g.Key, avg: g.Average(p => p.CA), league: g.First().League)).OrderByDescending(t => t.avg).ToList();
        ulong first = teams.Count > 0 ? teams[0].team : 0;
        foreach (var p in players) p.TeamName = p.Source.TeamAddress == first ? "First team" : $"Reserves/youth ({p.League})";
        var list = players.Where(p => SquadAllTeams.IsChecked == true || p.Source.TeamAddress == first)
            .OrderBy(p => p.Source.TeamAddress == first ? 0 : 1).ThenByDescending(p => p.BestRoleScore).ToList();
        SquadGrid.ItemsSource = list;
        SquadTitle.Text = $"{list.Count} players · reputation {club.Reputation:N0} · top wage {MoneyText.Format(club.TopWage)} p/w" + (club.Name == (MyClubCombo.SelectedItem as MyClub)?.Name ? " · your club" : "");
    }

    void ShowInPlayers(PlayerRow p)
    {
        _upgrade = null;
        WriteFilter(new FilterState { Query = $"uid={p.Uid}" });
        Tabs.SelectedIndex = 0;
        ApplyFilters();
        PlayerGrid.SelectedIndex = 0;
        ShowProfile(p);
    }

    void SquadGrid_DoubleClick(object sender, MouseButtonEventArgs e) { if (SquadGrid.SelectedItem is PlayerRow p) ShowInPlayers(p); }
    void PlayerGrid_DoubleClick(object sender, MouseButtonEventArgs e) { if (PlayerGrid.SelectedItem is PlayerRow p) { Tabs.SelectedIndex = 0; ShowProfile(p); } }

    void SquadGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SquadGrid.SelectedItem is PlayerRow p) ShowProfile(p);
    }

    void AttachContextMenus()
    {
        foreach (var grid in new[] { PlayerGrid, SquadGrid })
        {
            var g = grid;
            g.PreviewMouseRightButtonDown += (s, e) =>
            {
                var row = ItemsControl.ContainerFromElement(g, (DependencyObject)e.OriginalSource) as DataGridRow;
                if (row == null) return;
                row.IsSelected = true;
                g.ContextMenu = BuildRowMenu((PlayerRow)row.Item);
            };
        }
    }

    ContextMenu BuildRowMenu(PlayerRow p)
    {
        var menu = new ContextMenu();
        var cmp = new MenuItem { Header = _compare.Contains(p) ? "Remove from comparison" : "Add to comparison" };
        cmp.Click += (_, _) => { if (!_compare.Remove(p)) { if (_compare.Count >= 4) _compare.RemoveAt(0); _compare.Add(p); } RenderCompare(); StatusText.Text = $"Comparison ({_compare.Count}/4): {string.Join(", ", _compare.Select(x => x.Name))}"; };
        menu.Items.Add(cmp);
        var sl = new MenuItem { Header = Shortlist.Contains(p.Uid) ? "Remove from shortlist" : "Add to shortlist" };
        sl.Click += (_, _) => { Shortlist.Toggle(p.Uid); StatusText.Text = $"Shortlist: {Shortlist.Count} players (sidebar › Shortlist only, or query: shortlist)"; };
        menu.Items.Add(sl);
        var open = new MenuItem { Header = "Open comparison" };
        open.Click += (_, _) => { RenderCompare(); Tabs.SelectedIndex = 2; };
        var prof = new MenuItem { Header = "Open profile" };
        prof.Click += (_, _) => ShowInPlayers(p);
        menu.Items.Add(prof);
        menu.Items.Add(open);
        menu.Items.Add(new Separator());

        var up = new MenuItem { Header = "Find upgrade" };
        foreach (var (r, i) in _roles.Roles.Select((r, i) => (r, i)).OrderByDescending(t => p.RoleScores[t.i]).Take(5))
        {
            int ri = i;
            var mi = new MenuItem { Header = $"as {r.Name}  (his score {p.RoleScores[i]:F0})" };
            mi.Click += (_, _) => FindUpgrade(p, ri);
            up.Items.Add(mi);
        }
        menu.Items.Add(up);
        return menu;
    }

    void FindUpgrade(PlayerRow p, int roleIndex)
    {
        var role = _roles.Roles[roleIndex];
        var my = MyClubCombo.SelectedItem as MyClub;
        _upgrade = new FilterState
        {
            UpgradeForUid = p.Uid, UpgradeForName = p.Name, UpgradeMinScore = Math.Round(p.RoleScores[roleIndex] + UpgradeMargin),
            UpgradeMinCa = p.CA, UpgradeMinPa = p.PA, UpgradeClub = my?.Name,
            UpgradeIncScore = p.RoleScores[roleIndex], UpgradeIncPot = p.PotentialRoleScores[roleIndex], UpgradeIncCa = p.CA, UpgradeIncPa = p.PA,
        };
        WriteFilter(new FilterState { Role = role.Code });
        Tabs.SelectedIndex = 0;
        ApplyFilters();
        _sortCol = _cols.First(c => c.Key == "gain"); _sortDir = ListSortDirection.Descending; SortView();
        StatusText.Text = _view.Count == 0
            ? $"No realistic upgrade for {p.Name} as {role.Name} right now (nobody better who would also join you)."
            : $"{_view.Count} realistic upgrades for {p.Name} as {role.Name}. Pick another role via right-click › Find upgrade, or change the Role in the sidebar.";
    }

    void ClearCompare_Click(object sender, RoutedEventArgs e) { _compare.Clear(); RenderCompare(); }

    void RenderCompare()
    {
        CompareGrid.Children.Clear(); CompareGrid.RowDefinitions.Clear(); CompareGrid.ColumnDefinitions.Clear();
        if (_compare.Count == 0)
        {
            CompareGrid.Children.Add(new TextBlock { Text = "Nothing to compare yet. Right-click players and choose Add to comparison.", Foreground = (Brush)FindResource("Muted") });
            return;
        }
        CompareGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        foreach (var _ in _compare) CompareGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        int row = 0;
        void Row(string label, Func<PlayerRow, string> text, Func<PlayerRow, double>? score = null, bool header = false, bool higherIsBetter = true)
        {
            CompareGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var l = new TextBlock { Text = label, Foreground = header ? (Brush)FindResource("Fg") : (Brush)FindResource("Muted"), FontWeight = header ? FontWeights.Bold : FontWeights.Normal, Margin = new Thickness(0, header ? 10 : 1, 8, 1) };
            Grid.SetRow(l, row); CompareGrid.Children.Add(l);
            double best = score == null ? double.NaN : (higherIsBetter ? _compare.Max(score) : _compare.Min(score));
            for (int i = 0; i < _compare.Count; i++)
            {
                var p = _compare[i];
                bool isBest = score != null && _compare.Count > 1 && score(p) == best;
                var t = new TextBlock { Text = text(p), Margin = new Thickness(0, header ? 10 : 1, 8, 1), FontWeight = header || isBest ? FontWeights.Bold : FontWeights.Normal,
                    Foreground = isBest ? AttrBrush.Top : (Brush)FindResource("Fg"), TextTrimming = TextTrimming.CharacterEllipsis };
                Grid.SetRow(t, row); Grid.SetColumn(t, i + 1); CompareGrid.Children.Add(t);
            }
            row++;
        }
        Row("", p => p.Name, header: true);
        Row("Club", p => p.Club);
        Row("Age", p => p.Age?.ToString() ?? "", p => -(p.Age ?? 99));
        Row("Position", p => p.PositionsText);
        Row("CA", p => p.CA.ToString(), p => p.CA);
        Row("PA", p => p.PA.ToString(), p => p.PA);
        Row("Best role", p => p.BestRole);
        Row("Best score / pot.", p => $"{p.BestRoleScore:F0} / {p.BestPotentialScore:F0}", p => p.BestRoleScore);
        Row("Value", p => p.ValueText, p => -p.Value);
        Row("Wage", p => p.WageText, p => -p.Wage);
        Row("Expires", p => p.ContractExpiry?.ToString("dd/MM/yyyy") ?? "-");
        Row("Join %", p => p.Join < 0 ? "ours" : $"{p.Join:F0} ({p.JoinLabel})", p => p.Join);
        Row("Height", p => $"{p.Height} cm", p => p.Height);
        bool anyGk = _compare.Any(p => p.IsGoalkeeper);
        foreach (var (title, names) in new[] { ("Technical", anyGk && _compare.All(p => p.IsGoalkeeper) ? Attr.Goalkeeping : Attr.Technical), ("Mental", Attr.Mental), ("Physical", Attr.Physical), ("Hidden", Attr.Hidden) })
        {
            Row(title, _ => "", header: true);
            foreach (var n in names)
            {
                int ai = Attr.Index(n); if (ai < 0) continue;
                bool lowerBetter = n is "Dirtiness" or "InjuryProneness";
                Row(Attr.Pretty(n), p => p.A[ai].ToString(), p => p.A[ai], higherIsBetter: !lowerBetter);
            }
        }
        Row("Role scores", _ => "", header: true);
        var roleIdx = _compare.SelectMany(p => _roles.Roles.Select((r, i) => (i, s: p.RoleScores[i]))).GroupBy(t => t.i).OrderByDescending(g => g.Max(t => t.s)).Take(8).Select(g => g.Key);
        foreach (var ri in roleIdx) Row(_roles.Roles[ri].Name, p => $"{p.RoleScores[ri]:F0} / {p.PotentialRoleScores[ri]:F0}", p => p.RoleScores[ri]);
    }

    // ------------------------------------------------------------------ my club / join estimate
    string? _savedMyClub;

    void AfterLoad(LoadResult res)
    {
        _today = res.GameDate ?? _today;
        var clubs = _all.Where(p => p.Source.Club != null).GroupBy(p => p.Club)
            .Select(g => new MyClub(g.Key, g.Max(p => p.ClubRep), g.Max(p => p.Wage), g.Sum(p => p.Value), g.Count())).ToList();
        _myClubs = clubs.Where(c => res.HumanClubs.Contains(c.Name))
            .Concat(clubs.Where(c => !res.HumanClubs.Contains(c.Name)).OrderBy(c => c.Name)).ToList();
        var pick = _myClubs.FirstOrDefault(c => c.Name == _savedMyClub) ?? _myClubs.FirstOrDefault(c => res.HumanClubs.Contains(c.Name)) ?? _myClubs.FirstOrDefault();
        _suppressMyClub = true;
        MyClubCombo.ItemsSource = _myClubs;
        MyClubCombo.SelectedItem = pick;
        _suppressMyClub = false;
        EvaluateJoin();
        BuildSquad();
    }

    bool _suppressMyClub;
    void MyClub_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressMyClub || MyClubCombo.SelectedItem is not MyClub) return;
        _savedMyClub = ((MyClub)MyClubCombo.SelectedItem).Name;
        EvaluateJoin();
        BuildSquad();
        ApplyFilters();
        if (PlayerGrid.SelectedItem is PlayerRow p) ShowProfile(p);
    }

    void EvaluateJoin()
    {
        if (MyClubCombo.SelectedItem is not MyClub my) return;
        foreach (var p in _all) _join.Evaluate(p, my, _today);
    }

    // ------------------------------------------------------------------ profile
    void Grid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PlayerGrid.SelectedItem is PlayerRow p) ShowProfile(p);
    }

    void ShowProfile(PlayerRow p)
    {
        Profile.Children.Clear();
        var muted = (Brush)FindResource("Muted");
        TextBlock T(string s, double size = 13, bool bold = false, Brush? fg = null) =>
            new() { Text = s, FontSize = size, FontWeight = bold ? FontWeights.Bold : FontWeights.Normal, Foreground = fg ?? (Brush)FindResource("Fg"), TextWrapping = TextWrapping.Wrap };

        Profile.Children.Add(T(p.Name, 20, true));
        if (p.FullName != null) Profile.Children.Add(T(p.FullName, 12, fg: muted));
        Profile.Children.Add(T($"{p.PositionsText}   ·   {p.Age} years old ({p.DateOfBirth:dd/MM/yyyy})   ·   {p.Nation}", 12.5, fg: muted));
        Profile.Children.Add(T($"{p.Club}   ·   {p.League}", 13.5, true));

        var facts = new WrapPanel { Margin = new Thickness(0, 10, 0, 6) };
        void Fact(string k, string v)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 18, 6) };
            sp.Children.Add(T(k, 11, fg: muted)); sp.Children.Add(T(v, 15, true));
            facts.Children.Add(sp);
        }
        Fact("CA", p.CA.ToString()); Fact("PA", p.PA.ToString());
        Fact("Value", p.Value > 0 ? MoneyText.Format(p.Value) : "-");
        Fact("Wage", p.Wage > 0 ? MoneyText.Format(p.Wage) + " p/w" : "-");
        Fact("Expires", p.ContractExpiry?.ToString("dd/MM/yyyy") ?? "-");
        Fact("Height", $"{p.Height} cm");
        Fact("Feet", $"L {p.LeftFoot} / R {p.RightFoot}");
        Fact("Reputation", $"{p.CurrentRep:N0}");
        if (p.SquadStatusName.Length > 0) Fact("Status", p.SquadStatusName);
        Profile.Children.Add(facts);

        // attribute columns, FM style
        var cols = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        for (int i = 0; i < 3; i++) cols.ColumnDefinitions.Add(new ColumnDefinition());
        StackPanel Block(string title, string[] names)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
            sp.Children.Add(T(title, 11.5, true, muted));
            foreach (var n in names)
            {
                int i = Attr.Index(n); if (i < 0) continue;
                var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
                var v = T(p.A[i].ToString(), 13, true, AttrBrush.For(p.A[i]));
                DockPanel.SetDock(v, Dock.Right);
                row.Children.Add(v);
                row.Children.Add(T(Attr.Pretty(n).Replace(" (Tendency)", ""), 12));
                sp.Children.Add(row);
            }
            return sp;
        }
        var left = p.IsGoalkeeper ? Block("Goalkeeping", Attr.Goalkeeping) : Block("Technical", Attr.Technical);
        var mid = Block("Mental", Attr.Mental);
        var right = Block("Physical", Attr.Physical);
        right.Children.Add(new Border { Height = 10 });
        var hidden = Block("Hidden", Attr.Hidden);
        foreach (var c in hidden.Children.OfType<UIElement>().ToList()) { hidden.Children.Remove(c); right.Children.Add(c); }
        if (p.IsGoalkeeper)
        {
            right.Children.Add(new Border { Height = 10 });
            var tech = Block("Technical", ["FreeKicks", "Penalties", "Technique"]);
            foreach (var c in tech.Children.OfType<UIElement>().ToList()) { tech.Children.Remove(c); right.Children.Add(c); }
        }
        Grid.SetColumn(mid, 1); Grid.SetColumn(right, 2);
        cols.Children.Add(left); cols.Children.Add(mid); cols.Children.Add(right);
        Profile.Children.Add(cols);

        // would join?
        if (MyClubCombo.SelectedItem is MyClub my)
        {
            var box = new Border { Background = (Brush)FindResource("Panel2"), CornerRadius = new CornerRadius(4), Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 4, 0, 10) };
            var sp = new StackPanel();
            var head = new DockPanel();
            var lbl = T(p.Join < 0 ? p.JoinLabel : $"{p.JoinLabel}  {p.Join:F0}%", 14, true, p.Join < 0 ? muted : ScoreBrush.For(p.Join + 15));
            DockPanel.SetDock(lbl, Dock.Right);
            head.Children.Add(lbl);
            head.Children.Add(T($"Would he join {my.Name}?", 12.5, true));
            sp.Children.Add(head);
            foreach (var r in p.JoinReasons) sp.Children.Add(T(r, 11.5, fg: r.StartsWith('-') ? AttrBrush.Mid : r.StartsWith('+') ? AttrBrush.Good : muted));
            sp.Children.Add(T("Estimate only. FM decides when you approach him.", 10.5, fg: muted));
            box.Child = sp;
            Profile.Children.Add(box);
        }

        // top roles
        Profile.Children.Add(T("Best roles   (score · potential)", 11.5, true, muted));
        foreach (var (r, i) in _roles.Roles.Select((r, i) => (r, i)).OrderByDescending(t => p.RoleScores[t.i]).Take(6))
        {
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            var val = new StackPanel { Orientation = Orientation.Horizontal };
            val.Children.Add(T($"{p.RoleScores[i]:F0}", 13, true, ScoreBrush.For(p.RoleScores[i])));
            val.Children.Add(T($"  ·  {p.PotentialRoleScores[i]:F0}", 13, false, ScoreBrush.For(p.PotentialRoleScores[i])));
            DockPanel.SetDock(val, Dock.Right);
            row.Children.Add(val);
            row.Children.Add(T(r.Name, 12.5));
            Profile.Children.Add(row);
        }

        // positions
        Profile.Children.Add(new Border { Height = 10 });
        Profile.Children.Add(T("Position ratings", 11.5, true, muted));
        var pw = new WrapPanel();
        for (int i = 0; i < p.Positions.Length && i < Model.Positions.Names.Length; i++)
        {
            if (Model.Positions.Names[i] == "SW") continue;
            int v = p.Positions[i];
            pw.Children.Add(new Border
            {
                Width = 52, Margin = new Thickness(0, 2, 4, 2), Padding = new Thickness(4, 2, 4, 2), CornerRadius = new CornerRadius(3),
                Background = v >= 18 ? new SolidColorBrush(Color.FromRgb(0x1E, 0x6B, 0x3E)) : v >= 15 ? new SolidColorBrush(Color.FromRgb(0x3E, 0x6B, 0x1E)) : v >= 10 ? new SolidColorBrush(Color.FromRgb(0x6B, 0x55, 0x1E)) : (Brush)FindResource("Panel2"),
                Child = T($"{Model.Positions.Names[i]} {v}", 11.5)
            });
        }
        Profile.Children.Add(pw);
        Profile.Children.Add(new Border { Height = 10 });
        Profile.Children.Add(T($"Hidden personality values (order not yet mapped): {string.Join(" ", p.Personality)}", 11, fg: muted));
        Profile.Children.Add(T($"UID {p.Uid}", 11, fg: muted));
    }

    // ------------------------------------------------------------------ clubs
    sealed record ClubRow(string Club, string League, int Players, double AvgCa, int BestCa, double AvgPa, double AvgAge, long Value, long Wages, string BestPlayer)
    {
        public string ValueText => MoneyText.Format(Value);
        public string WageText => MoneyText.Format(Wages);
    }
    List<ClubRow> _clubs = [];

    void BuildClubs()
    {
        _clubs = _all.Where(p => p.Source.Club != null).GroupBy(p => (p.Club, p.League))
            .Select(g => new ClubRow(g.Key.Club, g.Key.League, g.Count(), g.Average(p => p.CA), g.Max(p => p.CA), g.Average(p => p.PA),
                g.Average(p => p.Age ?? 0), g.Sum(p => p.Value), g.Sum(p => p.Wage), g.MaxBy(p => p.CA)!.Name))
            .OrderByDescending(c => c.AvgCa).ToList();
        ClubFilter_Changed(this, null!);
    }

    void ClubFilter_Changed(object sender, TextChangedEventArgs e)
    {
        var f = ClubFilter.Text.Trim();
        ClubGrid.ItemsSource = f.Length == 0 ? _clubs : _clubs.Where(c => c.Club.Contains(f, StringComparison.OrdinalIgnoreCase) || c.League.Contains(f, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    void ClubGrid_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ClubGrid.SelectedItem is not ClubRow c) return;
        WriteFilter(new FilterState { Club = c.Club, League = c.League });
        Tabs.SelectedIndex = 0;
        ApplyFilters();
    }

    // ------------------------------------------------------------------ saved searches / settings / roles
    static readonly List<SavedSearch> Presets =
    [
        new() { Name = "★ Wonderkid regens (≤19, PA 150+)", Filter = new FilterState { Query = "regen age<=19 pa>=150" } },
        new() { Name = "★ Best young regens we could get", Filter = new FilterState { Query = "regen age<=21 pa>=130", GettableOnly = true } },
        new() { Name = "★ Bargains we could get", Filter = new FilterState { Query = "bargain ca>=100", GettableOnly = true } },
        new() { Name = "★ Undervalued high potential", Filter = new FilterState { Query = "age<=23 pa>=150 valueratio<=0.6" } },
        new() { Name = "★ Latest youth intakes (newgens joined this year)", Filter = new FilterState { Query = "regen age<=17 joined>=2037 pa>=120" } },
        new() { Name = "★ Value for money we could get", Filter = new FilterState { Query = "ca>=90 vfm>=40", GettableOnly = true } },
        new() { Name = "★ Free agents (CA 100+)", Filter = new FilterState { Query = "free ca>=100" } },
        new() { Name = "★ Contracts ending, gettable", Filter = new FilterState { Query = "ca>=100 contract<=2037", GettableOnly = true } },
    ];

    void RefreshSaved()
    {
        _suppressSaved = true;
        _saved = SavedSearches.Load();
        SavedCombo.ItemsSource = null;
        SavedCombo.ItemsSource = Presets.Concat(_saved).ToList();
        SavedCombo.SelectedIndex = -1;
        _suppressSaved = false;
    }

    void Saved_Selected(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSaved || SavedCombo.SelectedItem is not SavedSearch s) return;
        WriteFilter(s.Filter);
        if (Presets.Contains(s)) { _sortCol = _cols.First(c => c.Key == (s.Filter.Query.Contains("regen") || s.Filter.Query.Contains("pa>=") ? "pa" : "bestscore")); _sortDir = ListSortDirection.Descending; }
        ApplyFilters();
    }

    void SaveSearch_Click(object sender, RoutedEventArgs e)
    {
        var name = Prompt.Ask(this, "Save search", "Name for this search:", (SavedCombo.SelectedItem as SavedSearch)?.Name ?? "");
        if (string.IsNullOrWhiteSpace(name)) return;
        _saved.RemoveAll(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        _saved.Add(new SavedSearch { Name = name.Trim(), Filter = ReadFilter() });
        _saved = _saved.OrderBy(x => x.Name).ToList();
        SavedSearches.Save(_saved);
        RefreshSaved();
        StatusText.Text = $"Saved search \"{name}\".";
    }

    void DeleteSearch_Click(object sender, RoutedEventArgs e)
    {
        if (SavedCombo.SelectedItem is not SavedSearch s || Presets.Contains(s)) return;
        if (MessageBox.Show(this, $"Delete saved search \"{s.Name}\"?", "FM27 Scout", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        _saved.Remove(s);
        SavedSearches.Save(_saved);
        RefreshSaved();
    }

    void Roles_Click(object sender, RoutedEventArgs e)
    {
        var path = _roles.SourcePath ?? Path.Combine(AppPaths.Data, "roles.json");
        MessageBox.Show(this, $"Role weights live in:\n{path}\n\nEdit and save it, then press Load from game to recalculate.\nDelete the file to restore the defaults.", "FM27 Scout – roles");
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { }
    }

    sealed class Settings
    {
        public Dictionary<string, bool> Columns { get; set; } = new();
        public List<string> Order { get; set; } = [];
        public string? MyClub { get; set; }
    }

    void LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return;
            var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsPath));
            if (s == null) return;
            _savedMyClub = s.MyClub;
            foreach (var c in _cols) if (c.Key is not ("role" or "rolepot") && s.Columns.TryGetValue(c.Key, out var vis)) c.Column.Visibility = vis ? Visibility.Visible : Visibility.Collapsed;
            for (int i = 0; i < s.Order.Count; i++)
            {
                var c = _cols.FirstOrDefault(x => x.Key == s.Order[i]);
                if (c != null && i < PlayerGrid.Columns.Count) c.Column.DisplayIndex = i;
            }
        }
        catch { /* ignore bad settings */ }
    }

    void SaveSettings()
    {
        try
        {
            var s = new Settings
            {
                Columns = _cols.Where(c => c.Key is not ("role" or "rolepot")).ToDictionary(c => c.Key, c => c.Column.Visibility == Visibility.Visible),
                Order = _cols.OrderBy(c => c.Column.DisplayIndex).Select(c => c.Key).ToList(),
                MyClub = _savedMyClub,
            };
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
















