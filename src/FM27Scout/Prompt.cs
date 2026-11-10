using System.Windows;
using System.Windows.Controls;

namespace FM27Scout;

/// <summary>Friendly error dialog: plain message, technical details tucked into an expander.</summary>
public static class ErrorDialog
{
    public static void Show(Window owner, string title, string message, string? details)
    {
        var w = new Window
        {
            Title = title, Owner = owner, Width = 520, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize, Background = (System.Windows.Media.Brush)Application.Current.FindResource("Panel")
        };
        var sp = new StackPanel { Margin = new Thickness(16) };
        sp.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 13.5 });
        if (!string.IsNullOrWhiteSpace(details))
        {
            var box = new TextBox { Text = details, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 260, FontFamily = new System.Windows.Media.FontFamily("Consolas"), FontSize = 11,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            sp.Children.Add(new Expander { Header = "Details (for bug reports)", Content = box, Margin = new Thickness(0, 12, 0, 0) });
        }
        var ok = new Button { Content = "OK", IsDefault = true, IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0), Style = (Style)Application.Current.FindResource("AccentButton") };
        ok.Click += (_, _) => w.Close();
        sp.Children.Add(ok);
        w.Content = sp;
        w.ShowDialog();
    }
}

/// <summary>Find-upgrade thresholds (remembered for the session).</summary>
public static class UpgradeDialog
{
    public static bool Ask(Window owner, FM27Scout.Model.PlayerRow p, FM27Scout.Model.RoleDef role, FM27Scout.Model.FilterState st)
    {
        var w = new Window
        {
            Title = $"Find upgrade for {p.Name}", Owner = owner, Width = 420, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize, Background = (System.Windows.Media.Brush)Application.Current.FindResource("Panel")
        };
        var g = new Grid { Margin = new Thickness(16) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        int row = 0;
        TextBox Field(string label, string value, string tip)
        {
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var l = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 8, 4), ToolTip = tip };
            var t = new TextBox { Text = value, Margin = new Thickness(0, 4, 0, 4), ToolTip = tip };
            Grid.SetRow(l, row); Grid.SetRow(t, row); Grid.SetColumn(t, 1);
            g.Children.Add(l); g.Children.Add(t); row++;
            return t;
        }
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var head = new TextBlock { Text = $"{role.Name}: his score {p.RoleScores[Array.IndexOf(p.RoleScores, p.RoleScores.Max())]:F0}", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8), Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("Muted") };
        Grid.SetColumnSpan(head, 2); g.Children.Add(head); row++;
        var margin = Field("Better by at least (score)", st.UpgradeMargin.ToString("0"), "Role score must be at least this much higher now (under-23s can instead be +5 at potential).");
        var join = Field("Min Join %", st.UpgradeMinJoin.ToString("0"), "Only players who'd realistically come (50 = Possible).");
        var fee = Field("Max fee (e.g. 2m, blank = any)", st.UpgradeMaxFee is double f ? FM27Scout.MoneyText.Format((long)f) : "", "Estimated fee; free agents and pre-contracts count as 0.");
        var wage = Field("Max wage p/w (e.g. 8k)", st.UpgradeMaxWage is double wv ? FM27Scout.MoneyText.Format((long)wv) : "", "");
        var amin = Field("Age from", st.UpgradeAgeMin?.ToString() ?? "", "");
        var amax = Field("Age to", st.UpgradeAgeMax?.ToString() ?? "", "");
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var ok = new Button { Content = "Find upgrades", IsDefault = true, Style = (Style)Application.Current.FindResource("AccentButton") };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        buttons.Children.Add(ok); buttons.Children.Add(cancel);
        Grid.SetRow(buttons, row); Grid.SetColumnSpan(buttons, 2); g.Children.Add(buttons);
        ok.Click += (_, _) =>
        {
            try
            {
                double D(TextBox t, double d) => double.TryParse(t.Text.Trim(), out var v) ? v : d;
                int? I(TextBox t) => int.TryParse(t.Text.Trim(), out var v) ? v : null;
                st.UpgradeMargin = D(margin, 5); st.UpgradeMinJoin = D(join, 50);
                st.UpgradeMaxFee = string.IsNullOrWhiteSpace(fee.Text) ? null : FM27Scout.Model.FilterBuilder.Money(fee.Text);
                st.UpgradeMaxWage = string.IsNullOrWhiteSpace(wage.Text) ? null : FM27Scout.Model.FilterBuilder.Money(wage.Text);
                st.UpgradeAgeMin = I(amin); st.UpgradeAgeMax = I(amax);
                w.DialogResult = true;
            }
            catch (Exception ex) { MessageBox.Show(w, ex.Message, "FM27 Scout"); }
        };
        w.Content = g;
        return w.ShowDialog() == true;
    }
}

/// <summary>Tiny text-input dialog.</summary>
public static class Prompt
{
    public static string? Ask(Window owner, string title, string label, string initial = "")
    {
        var w = new Window
        {
            Title = title, Owner = owner, Width = 360, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize, Background = (System.Windows.Media.Brush)Application.Current.FindResource("Panel")
        };
        var sp = new StackPanel { Margin = new Thickness(14) };
        sp.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 6) });
        var tb = new TextBox { Text = initial };
        sp.Children.Add(tb);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var ok = new Button { Content = "Save", IsDefault = true, Style = (Style)Application.Current.FindResource("AccentButton") };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        ok.Click += (_, _) => w.DialogResult = true;
        buttons.Children.Add(ok); buttons.Children.Add(cancel);
        sp.Children.Add(buttons);
        w.Content = sp;
        w.Loaded += (_, _) => { tb.Focus(); tb.SelectAll(); };
        return w.ShowDialog() == true ? tb.Text : null;
    }
}


