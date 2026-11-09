using System.Windows;
using System.Windows.Controls;

namespace FM27Scout;

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
