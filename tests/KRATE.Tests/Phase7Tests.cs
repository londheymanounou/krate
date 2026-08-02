using System.Globalization;
using System.Numerics;
using Krate.Core;
using Xunit;

public class CombinatoricsTests
{
    public CombinatoricsTests() => Strings.Culture = CultureInfo.GetCultureInfo("en");

    [Fact]
    public void Combinations_And_Permutations_MatchKnownValues()
    {
        Assert.Equal(BigInteger.Parse("13983816"), Maths.Combinations(49, 6));   // lottery odds
        Assert.Equal(new BigInteger(210), Maths.Permutations(7, 3));
        Assert.Equal(BigInteger.One, Maths.Combinations(10, 0));
        Assert.Equal(BigInteger.One, Maths.Combinations(10, 10));
    }

    [Fact]
    public void Combinatorics_StaysExactForHugeInputs()
    {
        // C(100,50) is a 30-digit number — this is the whole point of BigInteger over double.
        Assert.Equal("100891344545564193334812497256", Maths.Combinations(100, 50).ToString());
        Assert.Contains("C(52,5) = 2598960", Maths.Combinatorics("52 5")); // poker hands
    }

    [Fact]
    public void Combinatorics_HandlesFactorialAndRejectsBadInput()
    {
        Assert.Equal("5! = 120", Maths.Combinatorics("5"));
        Assert.Throws<ArgumentException>(() => Maths.Combinatorics("5 9"));    // k > n
        Assert.Throws<ArgumentException>(() => Maths.Combinatorics("-1"));
        Assert.Throws<ArgumentException>(() => Maths.Combinatorics("abc"));
    }
}

public class TransferTests
{
    public TransferTests() => Strings.Culture = CultureInfo.GetCultureInfo("en");

    [Fact]
    public void Time_DistinguishesBitsFromBytes()
    {
        // 1 GB (8 Gbit) over 100 Mbps = 80 s.
        var overBits = Transfer.Time("1GB 100Mbps");
        Assert.Contains("1m 20s", overBits);

        // Same file over 100 MB/s (a byte rate) = 10 s — 8× faster.
        var overBytes = Transfer.Time("1GB 100MB/s");
        Assert.Contains("10s", overBytes);
    }

    [Fact]
    public void ParseBandwidth_HonoursUnitCase()
    {
        Assert.Equal(1e8, Transfer.ParseBandwidth("100Mbps"));       // 100 megabits
        Assert.Equal(8e8, Transfer.ParseBandwidth("100MB/s"));       // 100 megabytes = 800 megabits
        Assert.Equal(1e9, Transfer.ParseBandwidth("1Gbps"));
        Assert.Throws<ArgumentException>(() => Transfer.ParseBandwidth("100kittens"));
    }

    [Fact]
    public void Time_AcceptsEitherOrder_AndRejectsBadInput()
    {
        Assert.Contains("1m 20s", Transfer.Time("100Mbps 1GB"));     // order-independent
        Assert.Throws<ArgumentException>(() => Transfer.Time("1GB"));         // needs two values
        Assert.Throws<ArgumentException>(() => Transfer.Time("1GB 2GB"));     // no bandwidth
    }
}

public class SqlFormatTests
{
    public SqlFormatTests() => Strings.Culture = CultureInfo.GetCultureInfo("en");

    [Fact]
    public void Formats_ClausesOntoSeparateLines_AndUppercasesKeywords()
    {
        var sql = Dev.SqlFormat("select id, name from users where age > 18 order by name");
        var lines = sql.Split('\n');
        Assert.Equal("SELECT id, name", lines[0]);
        Assert.Equal("FROM users", lines[1]);
        Assert.Equal("WHERE age > 18", lines[2]);
        Assert.Equal("ORDER BY name", lines[3]);
    }

    [Fact]
    public void Uppercases_Keywords_ButNotIdentifiers()
    {
        var sql = Dev.SqlFormat("SELECT * from orders o join customers c on o.cid = c.id");
        Assert.Contains("JOIN customers c", sql);
        Assert.Contains("ON o.cid = c.id", sql);
        // "orders" and "customers" are identifiers — left alone.
        Assert.Contains("orders o", sql);
    }

    [Fact]
    public void Rejects_EmptyInput() => Assert.Throws<ArgumentException>(() => Dev.SqlFormat("   "));
}
