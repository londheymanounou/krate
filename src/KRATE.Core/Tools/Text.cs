using System.Globalization;

namespace Krate.Core;

public static partial class Text
{
    // Invariant casing: a tool must give the same answer on a Turkish machine as on a French one.
    public static string Upper(string s) => s.ToUpperInvariant();
    public static string Lower(string s) => s.ToLowerInvariant();
    public static string Title(string s) => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.ToLowerInvariant());

    public static string Invert(string s) => string.Create(s.Length, s, (dst, src) =>
    {
        for (var i = 0; i < src.Length; i++)
            dst[i] = char.IsUpper(src[i]) ? char.ToLowerInvariant(src[i]) : char.ToUpperInvariant(src[i]);
    });

    public const int WordsPerMinute = 200; // ponytail: average adult reading speed, tweak if it feels off.

    public static string Count(string s)
    {
        var words = s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        var lines = s.Length == 0 ? 0 : s.Split('\n').Length;
        var minutes = Math.Ceiling(words / (double)WordsPerMinute);
        return string.Join('\n',
            Strings.Get("Text_Count_Characters", s.Length),
            Strings.Get("Text_Count_CharactersNoSpaces", s.Count(c => !char.IsWhiteSpace(c))),
            Strings.Get("Text_Count_Words", words),
            Strings.Get("Text_Count_Lines", lines),
            Strings.Get("Text_Count_ReadingTime", minutes));
    }
    public static string Inspector(string input)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var c in input)
        {
            if (char.IsControl(c) || char.IsWhiteSpace(c) || c > 127)
                sb.AppendLine($"'{(char.IsControl(c) ? '.' : c)}'  U+{(int)c:X4}  {char.GetUnicodeCategory(c)}");
        }
        if (sb.Length == 0) return "All basic ASCII characters.";
        return sb.ToString().Trim();
    }

    public static string CaseConverter(string input)
    {
        var words = System.Text.RegularExpressions.Regex.Matches(input, @"[A-Z]?[a-z]+|[A-Z]+(?=[A-Z][a-z]|\b)|\d+").Select(m => m.Value.ToLowerInvariant()).ToArray();
        if (words.Length == 0) return input;
        
        var camel = words[0] + string.Join("", words.Skip(1).Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
        var pascal = string.Join("", words.Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
        var snake = string.Join("_", words);
        var kebab = string.Join("-", words);
        var screaming = string.Join("_", words).ToUpperInvariant();
        
        return string.Join('\n', 
            $"camelCase:      {camel}",
            $"PascalCase:     {pascal}",
            $"snake_case:     {snake}",
            $"kebab-case:     {kebab}",
            $"SCREAMING_SNAKE:{screaming}");
    }
}
