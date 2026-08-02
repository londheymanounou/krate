using System.Globalization;
using Krate.Core;
using Xunit;

public class StandardCalculatorTests
{
    public StandardCalculatorTests() => Strings.Culture = CultureInfo.GetCultureInfo("en");

    static string Run(params string[] keys)
    {
        var c = new StandardCalculator();
        foreach (var k in keys) c.Input(k);
        return c.Display;
    }

    [Fact]
    public void BasicArithmetic()
    {
        Assert.Equal("5", Run("2", "+", "3", "="));
        Assert.Equal("6", Run("2", "*", "3", "="));
        Assert.Equal("2", Run("6", "/", "3", "="));
        Assert.Equal("-1", Run("2", "-", "3", "="));
    }

    [Fact]
    public void ImmediateExecution_ResolvesThePendingOperatorAsYouChain()
    {
        // 2 + 3 shows 5 the moment the next operator is pressed, then continues: 5 + 4 = 9.
        Assert.Equal("5", Run("2", "+", "3", "+"));
        Assert.Equal("9", Run("2", "+", "3", "+", "4", "="));
    }

    [Fact]
    public void UnaryKeys()
    {
        Assert.Equal("3", Run("9", "sqrt"));
        Assert.Equal("25", Run("5", "sqr"));
        Assert.Equal("0.25", Run("4", "1/x"));
        Assert.Equal("-5", Run("5", "+/-"));
        Assert.Equal("0.5", Run("5", "0", "%"));
    }

    [Fact]
    public void Editing()
    {
        Assert.Equal("12", Run("1", "2", "3", "back"));
        Assert.Equal("0", Run("1", "2", "3", "CE"));
        Assert.Equal("7", Run("1", "2", "+", "5", "C", "7"));   // C wipes everything
    }

    [Fact]
    public void DivideByZero_ShowsAnError_AndOnlyClearRecovers()
    {
        var c = new StandardCalculator();
        foreach (var k in new[] { "5", "/", "0", "=" }) c.Input(k);
        Assert.Contains("zero", c.Display, StringComparison.OrdinalIgnoreCase);
        c.Input("9");                       // ignored while in error
        Assert.Contains("zero", c.Display, StringComparison.OrdinalIgnoreCase);
        c.Input("C");
        c.Input("7");
        Assert.Equal("7", c.Display);
    }

    [Fact]
    public void Memory()
    {
        var c = new StandardCalculator();
        foreach (var k in new[] { "5", "MS", "3", "M+" }) c.Input(k); // memory = 5, then +3 = 8
        Assert.Equal(8, c.Memory);
        c.Input("MR");
        Assert.Equal("8", c.Display);
        c.Input("MC");
        Assert.Equal(0, c.Memory);
    }

    [Fact]
    public void Expression_TracksThePendingSum_LikeTheTopLineInWindows()
    {
        var c = new StandardCalculator();
        c.Input("5"); c.Input("+");
        Assert.Equal("5 +", c.Expression);          // faint line shows the pending operator
        c.Input("3"); c.Input("=");
        Assert.Equal("5 + 3 =", c.Expression);
        Assert.Equal("8", c.Display);
        c.Input("2");                                // a new number after "=" clears the line
        Assert.Equal("", c.Expression);
    }

    [Fact]
    public void DecimalPoint_OnlyOnce()
    {
        Assert.Equal("3.14", Run("3", ".", "1", "4"));
        Assert.Equal("0.5", Run(".", "5"));
        Assert.Equal("3.1", Run("3", ".", ".", "1"));   // the second dot is ignored
    }

    [Fact]
    public void Scientific_Functions_HonourAngleModeAndConstants()
    {
        var c = new StandardCalculator();                 // default DEG
        c.Input("3"); c.Input("0"); c.Input("sin");
        Assert.Equal(0.5, double.Parse(c.Display, CultureInfo.InvariantCulture), 10); // sin 30° = 0.5

        var r = new StandardCalculator { AngleMode = StandardCalculator.Angle.Rad };
        foreach (var k in new[] { "0", "sin" }) r.Input(k);
        Assert.Equal("0", r.Display);

        Assert.Equal("8", Run("2", "^", "3", "="));           // power
        Assert.Equal("0.25", Run("4", "1/x"));
        Assert.Equal("2", Run("1", "7", "mod", "5", "="));    // 17 mod 5 = 2
        Assert.Equal("3", Run("2", "7", "cbrt"));             // cube root of 27
        Assert.Equal("120", Run("5", "fact"));

        var pi = new StandardCalculator();
        pi.Input("pi");
        Assert.StartsWith("3.14159", pi.Display);
    }

    [Fact]
    public void History_RecordsCompletedCalculations()
    {
        var c = new StandardCalculator();
        foreach (var k in new[] { "2", "+", "3", "=" }) c.Input(k);
        Assert.Equal("2 + 3 = 5", c.History[^1]);
    }
}

public class ProgrammerCalculatorTests
{
    [Fact]
    public void Shows_AllFourBasesAtOnce()
    {
        var c = new ProgrammerCalculator();
        foreach (var k in new[] { "2", "5", "5" }) c.Input(k);   // decimal 255
        Assert.Equal("FF", c.Hex);
        Assert.Equal("255", c.Dec);
        Assert.Equal("377", c.Oct);
        Assert.Equal("11111111", c.Bin);
    }

    [Fact]
    public void HexEntry_AndBaseSwitch_KeepTheValue()
    {
        var c = new ProgrammerCalculator();
        c.SetBase(16);
        c.Input("F"); c.Input("F");     // FF in hex
        Assert.Equal("255", c.Dec);
        c.SetBase(2);
        Assert.Equal("11111111", c.Display);   // same value, shown in binary
    }

    [Fact]
    public void Bitwise_Operators()
    {
        var c = new ProgrammerCalculator();
        // 12 AND 10 = 8
        foreach (var k in new[] { "1", "2", "and", "1", "0", "=" }) c.Input(k);
        Assert.Equal(8, c.Value);

        var x = new ProgrammerCalculator();
        foreach (var k in new[] { "5", "xor", "3", "=" }) x.Input(k); // 6
        Assert.Equal(6, x.Value);

        var s = new ProgrammerCalculator();
        foreach (var k in new[] { "1", "lsh", "4", "=" }) s.Input(k);  // 1 << 4 = 16
        Assert.Equal(16, s.Value);
    }
}
