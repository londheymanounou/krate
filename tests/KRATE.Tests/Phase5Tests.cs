using System.Globalization;
using Krate.Core;
using Xunit;

public class CalcTests
{
    public CalcTests() => Strings.Culture = CultureInfo.GetCultureInfo("en");

    [Theory]
    [InlineData("2 + 3 * 4", "14")]              // precedence
    [InlineData("(2 + 3) * 4", "20")]            // parentheses
    [InlineData("2 ^ 3 ^ 2", "512")]            // right-associative power
    [InlineData("-2 ^ 2", "-4")]                // unary minus binds looser than power
    [InlineData("10 % 3", "1")]                 // modulo
    [InlineData("2 * -3", "-6")]                // unary after operator
    [InlineData("0.1 + 0.2", "0.3")]            // G15 hides binary float noise
    public void Evaluate_RespectsPrecedence(string expr, string expected) =>
        Assert.Equal(expected, Calc.Evaluate(expr));

    [Theory]
    [InlineData("sqrt(16)", "4")]
    [InlineData("abs(-7)", "7")]
    [InlineData("log(1000)", "3")]
    [InlineData("5!", "120")]
    [InlineData("floor(3.9)", "3")]
    [InlineData("deg(pi)", "180")]              // radians → degrees
    public void Evaluate_HandlesFunctionsAndConstants(string expr, string expected) =>
        Assert.Equal(expected, Calc.Evaluate(expr));

    [Fact]
    public void Evaluate_KnowsPiAndE()
    {
        Assert.StartsWith("3.14159", Calc.Evaluate("pi"));
        Assert.StartsWith("2.71828", Calc.Evaluate("e"));
        Assert.Equal("1", Calc.Evaluate("sin(pi/2)"));
    }

    [Fact]
    public void EvaluateAt_BindsTheVariableX()
    {
        Assert.Equal(9, Calc.EvaluateAt("x^2", 3));
        Assert.Equal(0, Calc.EvaluateAt("sin(x)", 0), 10);
        Assert.Equal(7, Calc.EvaluateAt("2*x + 1", 3));
    }

    [Fact]
    public void Plot_SamplesTheCurve_AndMarksUndefinedPointsNaN()
    {
        var line = Calc.Plot("x", 0, 10, 11);   // y = x
        Assert.Equal(11, line.Length);
        Assert.Equal(0, line[0].X); Assert.Equal(0, line[0].Y);
        Assert.Equal(10, line[10].X); Assert.Equal(10, line[10].Y);

        // 1/x is undefined at x=0 → that sample is NaN, not a wild spike.
        var recip = Calc.Plot("1/x", -1, 1, 3);
        Assert.True(double.IsNaN(recip[1].Y));   // the middle sample sits on 0
        Assert.Throws<ArgumentException>(() => Calc.Plot("x", 0, 10, 1));
    }

    [Theory]
    [InlineData("2 +")]           // dangling operator
    [InlineData("(1 + 2")]        // missing paren
    [InlineData("2 ** 3")]        // not an operator
    [InlineData("nope(2)")]       // unknown function
    [InlineData("")]              // empty
    [InlineData("1/0")]           // infinity
    public void Evaluate_RejectsGarbage(string expr) =>
        Assert.Throws<ArgumentException>(() => Calc.Evaluate(expr));
}

public class ColorMoreTests
{
    public ColorMoreTests() => Strings.Culture = CultureInfo.GetCultureInfo("en");

    [Fact]
    public void Palette_RotatesHue()
    {
        var result = Colors.Palette("#ff0000");           // pure red, hue 0
        Assert.Contains("Base", result);
        Assert.Contains("#FF0000", result);
        Assert.Contains("#00FFFF", result);               // complementary of red is cyan (hue 180)
    }

    [Fact]
    public void Contrast_MatchesKnownWcagValues()
    {
        // Black on white is the maximum, exactly 21:1.
        Assert.Equal(21.0, Colors.ContrastRatio((0, 0, 0), (255, 255, 255)), 2);
        // Same colour is 1:1.
        Assert.Equal(1.0, Colors.ContrastRatio((120, 120, 120), (120, 120, 120)), 3);

        var report = Colors.Contrast("#000000\n#ffffff");
        Assert.Contains("21.00:1", report);
        Assert.Contains("pass", report);
        // A low-contrast pair fails AA normal text.
        Assert.Contains("fail", Colors.Contrast("#777777\n#999999"));
    }

    [Fact]
    public void Temperature_WarmIsRedder_CoolIsBluer()
    {
        var warm = Colors.KelvinToRgb(2000);   // candle
        var cool = Colors.KelvinToRgb(10000);  // overcast sky
        Assert.True(warm.R >= warm.B);          // warm light is red-heavy
        Assert.True(cool.B >= cool.R);          // cool light is blue-heavy
        Assert.Throws<ArgumentException>(() => Colors.Temperature("500"));
    }
}

public class CssTests
{
    public CssTests() => Strings.Culture = CultureInfo.GetCultureInfo("en");

    [Fact]
    public void Units_ConvertThroughPixels()
    {
        var from16 = Css.Units("16px");
        Assert.Contains("1rem", from16);
        Assert.Contains("12pt", from16);        // 16px = 12pt at 96dpi

        Assert.Contains("24px", Css.Units("1.5rem"));
        Assert.Contains("16px", Css.Units("12pt"));
        Assert.Throws<ArgumentException>(() => Css.Units("big"));
    }

    [Fact]
    public void Gradient_BuildsCss_AndDefaultsTheAngle()
    {
        Assert.Equal("background: linear-gradient(90deg, #FF0000, #0000FF);", Css.Gradient("#f00 #00f"));
        Assert.Contains("45deg", Css.Gradient("#f00 #00f 45deg"));
        Assert.Throws<ArgumentException>(() => Css.Gradient("#f00"));
    }
}
