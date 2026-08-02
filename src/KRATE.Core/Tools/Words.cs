using System.Globalization;
using System.Text;

namespace Krate.Core;

/// <summary>Numbers spelled out. The language is the tool's own setting, not the app's:
/// you often need a French cheque total while running the English interface.</summary>
public static class Words
{
    /// <summary>"1234" or "1234 fr". Falls back to the interface language.</summary>
    public static string Spell(string input)
    {
        var parts = input.Trim().Split([' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) throw new ArgumentException(Strings.Get("Error_NeedNumber"));

        var language = parts.Length > 1 ? parts[^1].ToLowerInvariant() : Strings.Culture.TwoLetterISOLanguageName;
        var digits = string.Concat(parts.Where(p => p.Any(char.IsDigit)));
        if (!long.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            throw new ArgumentException(Strings.Get("Error_NeedNumber"));

        return language switch
        {
            "fr" => French(value),
            "en" => English(value),
            _ => throw new ArgumentException(Strings.Get("Error_UnsupportedLanguage", language)),
        };
    }

    static readonly string[] EnUnits =
        ["zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten",
         "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen"];
    static readonly string[] EnTens =
        ["", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety"];
    static readonly string[] EnScales = ["", " thousand", " million", " billion", " trillion", " quadrillion", " quintillion"];

    public static string English(long value)
    {
        if (value == 0) return EnUnits[0];
        if (value < 0) return "minus " + English(-value);

        var groups = Groups(value);
        var sb = new StringBuilder();
        for (var i = groups.Count - 1; i >= 0; i--)
        {
            if (groups[i] == 0) continue;
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(EnHundreds(groups[i])).Append(EnScales[i]);
        }
        return sb.ToString();
    }

    static string EnHundreds(int n)
    {
        var sb = new StringBuilder();
        if (n >= 100) { sb.Append(EnUnits[n / 100]).Append(" hundred"); n %= 100; if (n > 0) sb.Append(' '); }
        if (n >= 20) { sb.Append(EnTens[n / 10]); if (n % 10 > 0) sb.Append('-').Append(EnUnits[n % 10]); }
        else if (n > 0) sb.Append(EnUnits[n]);
        return sb.ToString();
    }

    static readonly string[] FrUnits =
        ["zéro", "un", "deux", "trois", "quatre", "cinq", "six", "sept", "huit", "neuf", "dix",
         "onze", "douze", "treize", "quatorze", "quinze", "seize", "dix-sept", "dix-huit", "dix-neuf"];
    static readonly string[] FrTens =
        ["", "", "vingt", "trente", "quarante", "cinquante", "soixante", "soixante", "quatre-vingt", "quatre-vingt"];
    static readonly string[] FrScales = ["", "mille", "million", "milliard", "billion", "billiard", "trillion"];

    public static string French(long value)
    {
        if (value == 0) return FrUnits[0];
        if (value < 0) return "moins " + French(-value);

        var groups = Groups(value);
        var parts = new List<string>();
        for (var i = groups.Count - 1; i >= 0; i--)
        {
            if (groups[i] == 0) continue;
            var text = FrHundreds(groups[i]);
            if (i == 1) parts.Add(groups[i] == 1 ? "mille" : text + " mille");      // "mille", never "un mille"
            else if (i > 1) parts.Add($"{text} {FrScales[i]}{(groups[i] > 1 ? "s" : "")}"); // millions are nouns: they take an s
            else parts.Add(text);
        }
        return string.Join(' ', parts);
    }

    static string FrHundreds(int n)
    {
        var sb = new StringBuilder();
        if (n >= 100)
        {
            var hundreds = n / 100;
            sb.Append(hundreds > 1 ? FrUnits[hundreds] + " cent" : "cent");
            n %= 100;
            // "deux cents" but "deux cent un": the s only survives when nothing follows.
            if (hundreds > 1 && n == 0) sb.Append('s');
            if (n > 0) sb.Append(' ');
        }
        if (n >= 20)
        {
            var (tens, units) = (n / 10, n % 10);
            sb.Append(FrTens[tens]);
            // 70 and 90 are built as 60+10 and 80+10, so the unit part runs from 10 to 19.
            if (tens is 7 or 9) units += 10;
            if (tens == 8 && units == 0) sb.Append('s');                    // quatre-vingts
            // "et" only in 21…61 and 71 — never in 81 or 91.
            var et = (tens is >= 2 and <= 6 && units == 1) || (tens == 7 && units == 11);
            if (units > 0) sb.Append(et ? " et " : "-").Append(FrUnits[units]);
        }
        else if (n > 0) sb.Append(FrUnits[n]);
        return sb.ToString();
    }

    /// <summary>Splits a number into groups of three digits, least significant first.</summary>
    static List<int> Groups(long value)
    {
        var groups = new List<int>();
        for (; value > 0; value /= 1000) groups.Add((int)(value % 1000));
        return groups;
    }
}
