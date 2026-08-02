using System.Globalization;

namespace Krate.Core;

public static class Colors
{
    /// <summary>Accepts "#3af", "#33aaff", "3aafff", "rgb(51, 170, 255)" or "hsl(204, 100%, 60%)".</summary>
    public static (int R, int G, int B) Parse(string input)
    {
        var s = input.Trim().ToLowerInvariant().Replace(" ", "");

        if (s.StartsWith("rgb"))
        {
            var n = Numbers(s);
            return (Clamp(n[0]), Clamp(n[1]), Clamp(n[2]));
        }
        if (s.StartsWith("hsl"))
        {
            var n = Numbers(s);
            return FromHsl(n[0], n[1] / 100, n[2] / 100);
        }

        var hex = s.TrimStart('#');
        if (hex.Length == 3) hex = string.Concat(hex.Select(c => $"{c}{c}")); // #3af → #33aaff
        if (hex.Length != 6 || !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
            throw new ArgumentException(Strings.Get("Error_BadColor", input));
        return (v >> 16 & 0xFF, v >> 8 & 0xFF, v & 0xFF);
    }

    /// <summary>The same color in every notation — that's what the tool is actually for.</summary>
    public static string Describe(string input) => Describe(Parse(input));

    public static string Describe(int rgb) => Describe((rgb >> 16 & 0xFF, rgb >> 8 & 0xFF, rgb & 0xFF));

    public static string Describe((int R, int G, int B) c)
    {
        var (h, s, l) = ToHsl(c);
        return string.Join('\n',
            $"HEX  #{c.R:X2}{c.G:X2}{c.B:X2}",
            $"RGB  rgb({c.R}, {c.G}, {c.B})",
            string.Create(CultureInfo.InvariantCulture, $"HSL  hsl({h:0}, {s * 100:0}%, {l * 100:0}%)"));
    }

    public static (double H, double S, double L) ToHsl((int R, int G, int B) c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 2;
        if (max == min) return (0, 0, l); // grey: hue is undefined, report 0

        var d = max - min;
        var s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        var h = max == r ? (g - b) / d + (g < b ? 6 : 0)
              : max == g ? (b - r) / d + 2
              : (r - g) / d + 4;
        return (h * 60, s, l);
    }

    public static (int R, int G, int B) FromHsl(double h, double s, double l)
    {
        h = (h % 360 + 360) % 360 / 360;
        s = Math.Clamp(s, 0, 1);
        l = Math.Clamp(l, 0, 1);
        var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
        var p = 2 * l - q;
        return (Channel(p, q, h + 1.0 / 3), Channel(p, q, h), Channel(p, q, h - 1.0 / 3));
    }

    static int Channel(double p, double q, double t)
    {
        t = (t % 1 + 1) % 1;
        var v = t < 1.0 / 6 ? p + (q - p) * 6 * t
              : t < 1.0 / 2 ? q
              : t < 2.0 / 3 ? p + (q - p) * (2.0 / 3 - t) * 6
              : p;
        return (int)Math.Round(v * 255);
    }

    /// <summary>Reads the numbers between the parentheses of an rgb()/hsl() notation.
    ///
    /// Both bounds are validated first: "rgb(0" has no closing paren, so LastIndexOf returned -1
    /// and the range went negative, surfacing .NET's raw "length ('-5') must be a non-negative
    /// value. (Parameter 'length')" to the user. A malformed colour is a bad colour, not a
    /// crash.</summary>
    static double[] Numbers(string s)
    {
        var open = s.IndexOf('(');
        var close = s.LastIndexOf(')');
        if (open < 0 || close <= open) throw new ArgumentException(Strings.Get("Error_BadColor", s));
        return s[(open + 1)..close]
            .Split([',', '/'], StringSplitOptions.RemoveEmptyEntries)
            .Select(p => double.TryParse(p.Trim().TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
                ? v
                : throw new ArgumentException(Strings.Get("Error_BadColor", s)))
            .ToArray();
    }

    static int Clamp(double v) => (int)Math.Clamp(Math.Round(v), 0, 255);

    static string Hex((int R, int G, int B) c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    /// <summary>Colour harmonies built by rotating the hue. Input is any colour notation.</summary>
    public static string Palette(string input)
    {
        var (h, s, l) = ToHsl(Parse(input));
        string At(double degrees) => Hex(FromHsl(h + degrees, s, l));
        return string.Join('\n',
            $"{Strings.Get("Color_Base"),-16} {At(0)}",
            $"{Strings.Get("Color_Complementary"),-16} {At(180)}",
            $"{Strings.Get("Color_Triadic"),-16} {At(120)}  {At(240)}",
            $"{Strings.Get("Color_Analogous"),-16} {At(-30)}  {At(30)}",
            $"{Strings.Get("Color_SplitComp"),-16} {At(150)}  {At(210)}",
            $"{Strings.Get("Color_Tetradic"),-16} {At(90)}  {At(180)}  {At(270)}");
    }

    /// <summary>WCAG contrast ratio between two colours (one per line) and which levels it passes.</summary>
    public static string Contrast(string input)
    {
        var lines = input.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length < 2) throw new ArgumentException(Strings.Get("Error_NeedTwoColors"));
        var ratio = ContrastRatio(Parse(lines[0]), Parse(lines[1]));

        string Verdict(double min) => ratio >= min ? Strings.Get("Color_Pass") : Strings.Get("Color_Fail");
        return string.Join('\n',
            string.Create(CultureInfo.InvariantCulture, $"{Strings.Get("Color_Ratio")}  {ratio:0.00}:1"),
            $"AA  ({Strings.Get("Color_NormalText")})   {Verdict(4.5)}",
            $"AA  ({Strings.Get("Color_LargeText")})   {Verdict(3.0)}",
            $"AAA ({Strings.Get("Color_NormalText")})   {Verdict(7.0)}",
            $"AAA ({Strings.Get("Color_LargeText")})   {Verdict(4.5)}");
    }

    /// <summary>WCAG 2.x contrast ratio: (L1 + 0.05) / (L2 + 0.05), lighter over darker.</summary>
    public static double ContrastRatio((int R, int G, int B) a, (int R, int G, int B) b)
    {
        var (la, lb) = (RelativeLuminance(a), RelativeLuminance(b));
        var (hi, lo) = (Math.Max(la, lb), Math.Min(la, lb));
        return (hi + 0.05) / (lo + 0.05);
    }

    static double RelativeLuminance((int R, int G, int B) c)
    {
        // sRGB → linear, per the WCAG definition, then the Rec.709 luminance weights.
        static double Channel(int v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }

    /// <summary>Colour temperature in Kelvin → the approximate RGB of that white point
    /// (candlelight ~1900K, daylight ~6500K). Uses the Tanner Helland approximation.</summary>
    public static string Temperature(string input)
    {
        if (!double.TryParse(input.Trim().TrimEnd('k', 'K'), NumberStyles.Float, CultureInfo.InvariantCulture, out var kelvin))
            throw new ArgumentException(Strings.Get("Error_NeedNumber"));
        if (kelvin is < 1000 or > 40000) throw new ArgumentException(Strings.Get("Error_KelvinRange"));
        var c = KelvinToRgb(kelvin);
        return Describe(c);
    }

    // Standard colour-vision-deficiency simulation matrices (applied in sRGB space).
    static readonly double[] Protanopia = [0.567, 0.433, 0, 0.558, 0.442, 0, 0, 0.242, 0.758];
    static readonly double[] Deuteranopia = [0.625, 0.375, 0, 0.70, 0.30, 0, 0, 0.30, 0.70];
    static readonly double[] Tritanopia = [0.95, 0.05, 0, 0, 0.433, 0.567, 0, 0.475, 0.525];

    /// <summary>Shows how a colour appears under the common colour-blindness types — for checking a
    /// palette stays distinguishable.</summary>
    public static string ColorBlind(string input)
    {
        var c = Parse(input);
        var gray = Clamp(0.299 * c.R + 0.587 * c.G + 0.114 * c.B);
        return string.Join('\n',
            $"{Strings.Get("Cvd_Normal"),-18} {Hex(c)}",
            $"{Strings.Get("Cvd_Protan"),-18} {Hex(Simulate(c, Protanopia))}",
            $"{Strings.Get("Cvd_Deuter"),-18} {Hex(Simulate(c, Deuteranopia))}",
            $"{Strings.Get("Cvd_Tritan"),-18} {Hex(Simulate(c, Tritanopia))}",
            $"{Strings.Get("Cvd_Achroma"),-18} {Hex((gray, gray, gray))}");
    }

    public static (int R, int G, int B) Simulate((int R, int G, int B) c, double[] m) =>
        (Clamp(m[0] * c.R + m[1] * c.G + m[2] * c.B),
         Clamp(m[3] * c.R + m[4] * c.G + m[5] * c.B),
         Clamp(m[6] * c.R + m[7] * c.G + m[8] * c.B));

    public static (int R, int G, int B) SimulateProtanopia((int R, int G, int B) c) => Simulate(c, Protanopia);
    public static (int R, int G, int B) SimulateDeuteranopia((int R, int G, int B) c) => Simulate(c, Deuteranopia);
    public static (int R, int G, int B) SimulateTritanopia((int R, int G, int B) c) => Simulate(c, Tritanopia);

    public static (int R, int G, int B) KelvinToRgb(double kelvin)
    {
        var t = kelvin / 100;
        double r, g, b;

        if (t <= 66) { r = 255; g = 99.4708025861 * Math.Log(t) - 161.1195681661; }
        else { r = 329.698727446 * Math.Pow(t - 60, -0.1332047592); g = 288.1221695283 * Math.Pow(t - 60, -0.0755148492); }

        if (t >= 66) b = 255;
        else if (t <= 19) b = 0;
        else b = 138.5177312231 * Math.Log(t - 10) - 305.0447927307;

        return (Clamp(r), Clamp(g), Clamp(b));
    }
}
