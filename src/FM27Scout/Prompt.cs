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

