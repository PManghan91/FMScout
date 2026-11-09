using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace FM27Scout;

public sealed class AttrBrush : IValueConverter
{
    public static readonly SolidColorBrush Low = Freeze(0x80, 0x86, 0x96), Mid = Freeze(0xD9, 0xA4, 0x41), Good = Freeze(0x9C, 0xD3, 0x6B), Top = Freeze(0x2E, 0xD5, 0x73);
    static SolidColorBrush Freeze(byte r, byte g, byte b) { var x = new SolidColorBrush(Color.FromRgb(r, g, b)); x.Freeze(); return x; }
    public static SolidColorBrush For(int v) => v >= 16 ? Top : v >= 11 ? Good : v >= 6 ? Mid : Low;
    public object Convert(object value, Type t, object p, CultureInfo c) => value is int v ? For(v) : Low;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class ScoreBrush : IValueConverter
{
    public static SolidColorBrush For(double v) => v >= 80 ? AttrBrush.Top : v >= 65 ? AttrBrush.Good : v >= 50 ? AttrBrush.Mid : AttrBrush.Low;
    public object Convert(object value, Type t, object p, CultureInfo c) => value is double v ? For(v) : AttrBrush.Low;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class MoneyText : IValueConverter
{
    public static string Format(long v) => v >= 1_000_000 ? $"£{v / 1e6:0.#}M" : v >= 1_000 ? $"£{v / 1e3:0}K" : $"£{v}";
    public object Convert(object value, Type t, object p, CultureInfo c) => value is long v ? (v == 0 ? "-" : Format(v)) : "";
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}
