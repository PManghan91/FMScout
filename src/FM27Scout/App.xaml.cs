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

        foreach (var q in new[] { "", "age<=21 pa>=170", "pos:ST poa>=75", "league:\"Premier League\" value<=20m age<=23", "gk ca>=160", "free pa>=140", "pot.bpcb>=80 age<=20", "haaland", "join>=70 ca>=120", "pos:ST join>=50 ca>=110", "free join>=60 ca>=100", "bad>>query" })
        {
            var r = w.SelfTestQuery(q);
            log.WriteLine($"query [{q}] -> {r}");
        }
        log.WriteLine(w.SelfTestSample("Haaland"));
        log.WriteLine(w.SelfTestSample("Lamine Yamal"));
        log.WriteLine(w.SelfTestJoin());

        w.SelfTestQuery("age<=23 pa>=160");
        w.SelfTestSelect(0);
        Render(w, Path.Combine(outDir, "selftest-players.png"));
        File.WriteAllText(Path.Combine(outDir, "selftest.txt"), log.ToString());
    }

    static void Render(Window w, string path)
    {
        var root = (FrameworkElement)w.Content;
        w.Content = null;
        var host = new System.Windows.Controls.Border { Background = w.Background, Child = root };
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

