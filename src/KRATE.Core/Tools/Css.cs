using System.Globalization;

namespace Krate.Core;

public static class Css
{
    const double RootPx = 16;    // the CSS default root font size
    const double PxPerPt = 96.0 / 72; // 1pt = 1/72in, 1in = 96px

    /// <summary>"16px", "1.5rem", "12pt" → the same size in px, rem, em and pt.
    /// em and rem both assume the 16px default root, since there is no document context here.</summary>
    public static string Units(string input)
    {
        var s = input.Trim().ToLowerInvariant().Replace(" ", "");
        var digits = s.TakeWhile(c => char.IsDigit(c) || c is '.' or '-').Count();
        if (digits == 0) throw new ArgumentException(Strings.Get("Error_BadCssUnit", input));
        var value = double.Parse(s[..digits], CultureInfo.InvariantCulture);
        var unit = s[digits..];

        // Everything goes through pixels as the common ground.
        var px = unit switch
        {
            "px" or "" => value,
            "rem" or "em" => value * RootPx,
            "pt" => value * PxPerPt,
            "%" => value / 100 * RootPx,
            _ => throw new ArgumentException(Strings.Get("Error_BadCssUnit", input)),
        };

        return string.Join('\n',
            $"{Num(px)}px",
            $"{Num(px / RootPx)}rem",
            $"{Num(px / RootPx)}em",
            $"{Num(px / PxPerPt)}pt");
    }

    /// <summary>"#f00 #00f" (or "#f00 #00f 90deg") → a CSS linear-gradient snippet.</summary>
    public static string Gradient(string input)
    {
        var parts = input.Split([' ', ',', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var angle = "90deg";
        var stops = new List<string>();
        foreach (var p in parts)
        {
            if (p.EndsWith("deg") && double.TryParse(p[..^3], out _)) angle = p;
            else stops.Add(Colors.Describe(p).Split('\n')[0][5..]); // normalise each colour to its hex form
        }
        if (stops.Count < 2) throw new ArgumentException(Strings.Get("Error_NeedTwoColors"));
        return $"background: linear-gradient({angle}, {string.Join(", ", stops)});";
    }

    static string Num(double v) => Math.Round(v, 4).ToString(CultureInfo.InvariantCulture);

    /// <summary>Strips comments and collapses whitespace to shrink CSS.</summary>
    // ponytail: regex over the token stream, not a full CSS parser. Correct for ordinary rules;
    // the known ceiling is spaces inside string values / data: URIs (e.g. content: "a : b") — rare,
    // reach for a real tokenizer only if that bites.
    public static string Minify(string css)
    {
        if (string.IsNullOrWhiteSpace(css)) throw new ArgumentException(Strings.Get("Error_EmptyInput"));
        var s = System.Text.RegularExpressions.Regex.Replace(css, @"/\*.*?\*/", "", System.Text.RegularExpressions.RegexOptions.Singleline);
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\s*([{}:;,>~+])\s*", "$1");
        s = s.Replace(";}", "}"); // the last declaration's semicolon is redundant
        return s.Trim();
    }
}
