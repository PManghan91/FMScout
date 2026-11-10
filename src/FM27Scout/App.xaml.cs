using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FM27Scout.Model;

namespace FM27Scout;

public partial class App : Application
{
    void App_Startup(object sender, StartupEventArgs e)
    {
        // never crash with a raw exception: show a friendly dialog (details for bug reports)
        DispatcherUnhandledException += (_, ex) =>
        {
            ex.Handled = true;
            try { ErrorDialog.Show(MainWindow ?? new Window(), "FM27 Scout – unexpected error", "Something went wrong. FM27 Scout will try to carry on; if it misbehaves, restart it.", ex.Exception.ToString()); } catch { }
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
        {
            try { File.WriteAllText(Path.Combine(AppPaths.Data, "crash.txt"), ex.ExceptionObject?.ToString()); } catch { }
        };
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, ex) => ex.SetObserved();
        if (e.Args.Length > 0 && e.Args[0] == "--selftest")
        {
            var outDir = e.Args.Length > 1 ? e.Args[1] : Path.GetTempPath();
            int code = 0;
            try { SelfTest(outDir); }
            catch (Exception ex) { File.WriteAllText(Path.Combine(outDir, "selftest.txt"), ex.ToString()); code = 1; }
            Shutdown(code);
            return;
        }
        new MainWindow().Show();
    }

    /// <summary>
    /// Headless check (no window is shown): loads from the game, runs some queries and renders the UI to PNG.
    /// </summary>
    static void SelfTest(string outDir)
    {
        var log = new StringWriter();
        var w = new MainWindow();
        var res = w.SelfTestLoad();
        log.WriteLine($"build {res.Build} date {res.GameDate} people {res.PeopleInDb} players {res.Players.Count} women excluded {res.ExcludedWomen} in {res.Millis} ms");

        foreach (var q in new[] { "", "age<=21 pa>=170", "pos:ST poa>=75", "league:\"Premier League\" value<=20m age<=23", "gk ca>=160", "free pa>=140", "pot.bpcb>=80 age<=20", "haaland", "join>=70 ca>=120", "pos:ST join>=50 ca>=110", "free join>=60 ca>=100", "regen age<=19 pa>=150", "bargain ca>=110 join>=50", "real age<=17", "bad>>query" })
        {
            var r = w.SelfTestQuery(q);
            log.WriteLine($"query [{q}] -> {r}");
        }
        log.WriteLine(w.SelfTestSample("Haaland"));
        log.WriteLine(w.SelfTestSample("Lamine Yamal"));
        log.WriteLine(w.SelfTestJoin());
        log.WriteLine(w.SelfTestPotential());
        log.WriteLine(w.SelfTestRegens());
        log.WriteLine(w.SelfTestWp());
        log.WriteLine(w.SelfTestUpgrade());
        log.WriteLine(w.SelfTestQa());
        log.WriteLine($"arranged transfers: {w.SelfTestArranged()}");
        foreach (var q in new[] { "trn", "trn ca>=120", "caron allen", "shortlist -trn", "trn Harvey Elliott" }) log.WriteLine($"query [{q}] -> {w.SelfTestQuery(q)}");

        w.SelfTestQuery("age<=23 pa>=160");
        w.SelfTestSelect(0);
        Render(w, Path.Combine(outDir, "selftest-players.png"), 0);
        Render(w, Path.Combine(outDir, "selftest-squad.png"), 1);
        Render(w, Path.Combine(outDir, "selftest-compare.png"), 2);
        Render(w, Path.Combine(outDir, "selftest-clubs.png"), 3);
        Render(w, Path.Combine(outDir, "selftest-staff.png"), 4);
        Render(w, Path.Combine(outDir, "selftest-shortlist.png"), 5);
        foreach (var n in new[] { "Caron Allen", "Haaland" })
            if (w.SelfTestProfileCard(n) is FrameworkElement card)
            {
                card.Measure(new Size(card.Width, double.PositiveInfinity)); card.Arrange(new Rect(card.DesiredSize)); card.UpdateLayout();
                var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)card.ActualWidth, (int)card.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                bmp.Render(card);
                var enc = new System.Windows.Media.Imaging.PngBitmapEncoder(); enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
                using var fs = File.Create(Path.Combine(outDir, $"selftest-profile-{n.Replace(" ", "")}.png")); enc.Save(fs);
            }
        RenderMenu(w.SelfTestColumnsMenu(), Path.Combine(outDir, "selftest-menu.png"));
        File.WriteAllText(Path.Combine(outDir, "selftest.txt"), log.ToString());
    }

    /// <summary>Renders a context menu's items and its first submenu's items off-screen, using the app's menu styles.</summary>
    static void RenderMenu(System.Windows.Controls.ContextMenu menu, string path)
    {
        System.Windows.Controls.Border Box(IEnumerable<object> items)
        {
            var sp = new System.Windows.Controls.StackPanel();
            foreach (var it in items.ToList())
            {
                if (it is System.Windows.Controls.MenuItem mi)
                {
                    var copy = new System.Windows.Controls.MenuItem { Header = mi.Header, IsCheckable = mi.IsCheckable, IsChecked = mi.IsChecked };
                    if (mi.HasItems) copy.Items.Add(new System.Windows.Controls.MenuItem { Header = "x" });
                    sp.Children.Add(copy);
                }
                else sp.Children.Add(new System.Windows.Controls.Separator());
            }
            return new System.Windows.Controls.Border { Background = (Brush)Current.FindResource("Panel2"), BorderBrush = (Brush)Current.FindResource("Line"), BorderThickness = new Thickness(1), Padding = new Thickness(2), Margin = new Thickness(10), Child = sp, VerticalAlignment = VerticalAlignment.Top };
        }
        var top = menu.Items.Cast<object>();
        var sub = menu.Items.OfType<System.Windows.Controls.MenuItem>().First(m => m.HasItems);
        var b1 = Box(top); var b2 = Box(sub.Items.Cast<object>());
        // fake hover on the first submenu item to show the highlight colour
        ((System.Windows.Controls.MenuItem)((System.Windows.Controls.StackPanel)b2.Child).Children[0]).IsChecked = true; // shows the tick style
        var panel = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Background = (Brush)Current.FindResource("Bg") };
        panel.Children.Add(b1); panel.Children.Add(b2);
        var size = new Size(700, 420);
        panel.Measure(size); panel.Arrange(new Rect(size)); panel.UpdateLayout();
        var bmp = new RenderTargetBitmap(700, 420, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(panel);
        var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path); enc.Save(fs);
    }
    static System.Windows.Controls.Border? _host;
    static void Render(MainWindow w, string path, int tab)
    {
        if (_host == null)
        {
            var root = (FrameworkElement)w.Content;
            w.Content = null;
            _host = new System.Windows.Controls.Border { Background = w.Background, Child = root };
        }
        var host = _host;
        w.SelectTab(tab);
        var size = new Size(1600, 900);
        for (int i = 0; i < 4; i++)
        {
            host.Measure(size); host.Arrange(new Rect(size)); host.UpdateLayout();
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
        }
        var bmp = new RenderTargetBitmap(1600, 900, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(host);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        enc.Save(fs);
    }
}














