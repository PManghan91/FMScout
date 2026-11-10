using System.IO;
using FM27Scout.Model;
using FMScout.Reader;

namespace FM27Scout.Tests;

public class DateAndAgeTests
{
    static uint Raw(int day, int year, int flags = 0) => (uint)(day | flags << 9 | year << 16);

    [Fact] public void DecodesDayOfYear() => Assert.Equal(new DateOnly(2037, 2, 4), FmGame.DecodeDate(Raw(35, 2037)));
    [Fact] public void IgnoresFlagBits() => Assert.Equal(new DateOnly(2037, 2, 4), FmGame.DecodeDate(Raw(35, 2037, flags: 0x19)));
    [Fact] public void Day366OnlyInLeapYears()
    {
        Assert.Equal(new DateOnly(2040, 12, 31), FmGame.DecodeDate(Raw(366, 2040)));
        Assert.Null(FmGame.DecodeDate(Raw(366, 2037)));
    }
    [Fact] public void Year1900MeansNoDate() => Assert.Null(FmGame.DecodeDate(Raw(1, 1900)));
    [Fact] public void ZeroDayIsInvalid() => Assert.Null(FmGame.DecodeDate(Raw(0, 2037)));

    [Theory]
    [InlineData("2008-02-29", "2037-02-28", 28)]
    [InlineData("2008-02-29", "2037-03-01", 29)]
    [InlineData("2000-07-21", "2037-07-20", 36)]
    [InlineData("2000-07-21", "2037-07-21", 37)]
    public void AgeHandlesBirthdaysAndLeapDays(string dob, string today, int expected)
        => Assert.Equal(expected, Ages.On(DateOnly.Parse(dob), DateOnly.Parse(today)));
}

public class LayoutTests
{
    static string RepoFile(string name)
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, name))) return Path.Combine(d.FullName, name);
        throw new FileNotFoundException(name);
    }

    [Fact]
    public void ShippedLayoutIsComplete()
    {
        var layouts = LayoutFile.Load(RepoFile("fm27-layout.json"));
        Assert.NotEmpty(layouts);
        foreach (var (build, L) in layouts)
        {
            Assert.Matches(@"^\d+\.\d+\.\d+\+\d+$", build);
            Assert.NotEqual(0, L.PersonDb.RootRva.Value);
            Assert.NotEqual(0, L.PlayerVtableRva.Value);
            Assert.NotEqual(0, L.StaffVtableRva.Value);
            Assert.NotEqual(0, L.HumanManagerVtableRva.Value);
            Assert.Equal(54, L.AttributeNames.Length);
            Assert.Equal(15, L.PositionNames.Length);
            Assert.Equal(8, L.PersonalityNames.Length);
            Assert.True(L.Player.BlockStart.Value < 0, "player block sits before the person pointer");
            Assert.InRange(L.Player.CurrentAbility.Value, L.Player.BlockStart.Value, 0x400);
            Assert.NotEmpty(L.Anchors);
            Assert.Equal("built-in", L.Origin);
            Assert.Equal(L.AttributeNames.Length, L.AttributeNames.Distinct().Count());
        }
    }

    [Fact]
    public void HexConverterReadsNegativeAndHex()
    {
        var l = LayoutFile.Parse("{ \"builds\": { \"1.0.0+1\": { \"gameDateRva\": \"0x10\", \"player\": { \"blockStart\": \"-0x288\" } } } }");
        Assert.Equal(0x10, l["1.0.0+1"].GameDateRva.Value);
        Assert.Equal(-0x288, l["1.0.0+1"].Player.BlockStart.Value);
    }

    [Fact]
    public void NewestTemplateSortsByVersionNotInsertionOrder()
    {
        var d = new Dictionary<string, BuildLayout>
        {
            ["27.1.0+2600000"] = new() { Origin = "doctor" },
            ["27.0.0+2493426"] = new() { Origin = "built-in" },
            ["27.0.5+2500000"] = new() { Origin = "doctor" },
        };
        Assert.Same(d["27.1.0+2600000"], GameLoader.NewestTemplate(d));
    }
}

public class QueryTests
{
    static QueryTests()
    {
        var L = LayoutFile.Load(FindLayout()).Values.First();
        Attr.Init(L.AttributeNames);
        Positions.Names = L.PositionNames;
    }

    static string FindLayout()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "fm27-layout.json"))) return Path.Combine(d.FullName, "fm27-layout.json");
        throw new FileNotFoundException();
    }

    static PlayerRow Make(string name, int age, int ca, int pa, uint uid = 1, string? club = "Some FC", DateOnly? expires = null, long value = 1_000_000)
    {
        var dob = new DateOnly(2037 - age, 1, 1);
        var p = new Player(1, uid, name, null, dob, "England", club, "League", null, 1000, expires, value, ca, pa, 180,
            new byte[15], Enumerable.Repeat((byte)50, 54).ToArray(), new byte[8], false, null, 5000, 5000, 3000, 3, 0, 5000, null, 0, 10, 2, [], [], "England");
        return new PlayerRow { Source = p, A = p.Attributes.Select(PlayerRow.ToDisplay).ToArray(), Age = age, RoleScores = [], PotentialRoleScores = [] };
    }

    static Func<PlayerRow, bool> Q(string q) => FilterBuilder.Build(new FilterState { Query = q }, new RoleSet());

    [Fact] public void Range() { var f = Q("age:18-23"); Assert.True(f(Make("a", 20, 100, 120))); Assert.False(f(Make("b", 25, 100, 120))); }
    [Fact] public void Comparison() { var f = Q("ca>=120 pa<150"); Assert.True(f(Make("a", 20, 125, 140))); Assert.False(f(Make("b", 20, 125, 160))); }
    [Fact] public void NegatedWord() { var f = Q("-free"); Assert.True(f(Make("a", 20, 100, 120))); Assert.False(f(Make("b", 20, 100, 120, club: null))); }
    [Fact] public void BareWordMatchesName() => Assert.True(Q("haal")(Make("Erling Haaland", 36, 169, 195)));
    [Fact] public void MoneyValues() { var f = Q("value<=2m"); Assert.True(f(Make("a", 20, 100, 120, value: 1_500_000))); Assert.False(f(Make("b", 20, 100, 120, value: 2_500_000))); }
    [Fact] public void UnknownFieldIsAnError() => Assert.Throws<QueryError>(() => Q("bogus>=1"));
    [Fact] public void PlainYearContract()
    {
        var f = Q("contract<=2037");
        Assert.True(f(Make("a", 25, 100, 110, expires: new DateOnly(2037, 6, 30))));
        Assert.False(f(Make("b", 25, 100, 110, expires: new DateOnly(2038, 6, 30))));
    }
    [Theory]
    [InlineData("25m", 25_000_000)]
    [InlineData("500k", 500_000)]
    [InlineData("£1,200", 1200)]
    public void MoneyParsing(string s, long expected) => Assert.Equal(expected, FilterBuilder.Money(s));

    [Fact]
    public void FmfShortlistMatchesFmLayout()
    {
        uint[] uids = [2002223305, 2002137441, 2002176981];
        var bytes = FM27Scout.Model.FmfShortlist.Build("test", uids);
        Assert.Equal("afe.", System.Text.Encoding.ASCII.GetString(bytes, 2, 4));
        long len = BitConverter.ToInt64(bytes, 9);
        Assert.Equal("fmf.", System.Text.Encoding.ASCII.GetString(bytes, (int)(9 + len) + 2, 4));
        var dir = Path.Combine(Path.GetTempPath(), "fm27scout-test");
        var path = FM27Scout.Model.FmfShortlist.Write("test", uids, dir);
        Assert.True(File.Exists(path));
    }
}