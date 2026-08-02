using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Krate.Core;

public static partial class Text
{
    /// <summary>Strips accents: é → e, ç → c. Keeps everything else intact.</summary>
    public static string Deaccent(string s)
    {
        var decomposed = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>Splits an identifier written in any convention into its words.</summary>
    static IEnumerable<string> Words(string s) =>
        SplitPattern().Split(Deaccent(s))
            .Where(w => w.Length > 0)
            .Select(w => w.ToLowerInvariant());

    // Breaks on separators and on camelCase humps (also handles HTTPServer → http, server).
    [GeneratedRegex(@"[^\p{L}\p{N}]+|(?<=\p{Ll})(?=\p{Lu})|(?<=\p{L})(?=\p{N})|(?<=\p{Lu})(?=\p{Lu}\p{Ll})")]
    private static partial Regex SplitPattern();

    /// <summary>Every naming convention at once — you rarely want just one.</summary>
    public static string Naming(string s)
    {
        var words = Words(s).ToArray();
        if (words.Length == 0) throw new ArgumentException(Strings.Get("Error_NeedText"));
        var pascal = string.Concat(words.Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
        return string.Join('\n',
            $"camelCase    {char.ToLowerInvariant(pascal[0]) + pascal[1..]}",
            $"PascalCase   {pascal}",
            $"snake_case   {string.Join('_', words)}",
            $"kebab-case   {string.Join('-', words)}",
            $"CONSTANT     {string.Join('_', words).ToUpperInvariant()}");
    }

    /// <summary>URL slug: accents flattened, punctuation dropped, words joined by hyphens.</summary>
    public static string Slug(string s) => string.Join('-', Words(s));

    /// <summary>Fixes text pasted from a PDF or a Word document: collapsed spaces,
    /// trimmed lines, no runs of blank lines.</summary>
    public static string Clean(string s)
    {
        var lines = s.Replace("\r\n", "\n").Split('\n')
            .Select(l => SpacesPattern().Replace(l, " ").Trim());
        var output = new List<string>();
        foreach (var line in lines)
            if (line.Length > 0 || (output.Count > 0 && output[^1].Length > 0)) output.Add(line);
        while (output.Count > 0 && output[^1].Length == 0) output.RemoveAt(output.Count - 1);
        return string.Join('\n', output);
    }

    [GeneratedRegex(@"[^\S\n]+")] private static partial Regex SpacesPattern();

    /// <summary>Builds a table of contents from Markdown headings, with GitHub-style anchor links.</summary>
    public static string Toc(string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var items = new List<string>();
        var seen = new Dictionary<string, int>();
        var inCode = false;

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            if (line.StartsWith("```")) { inCode = !inCode; continue; } // '#' inside code is not a heading
            if (inCode) continue;

            var match = TocHeadingPattern().Match(line);
            if (!match.Success) continue;

            var level = match.Groups[1].Value.Length;
            var title = match.Groups[2].Value.Trim();
            var anchor = Anchor(title, seen);
            items.Add($"{new string(' ', (level - 1) * 2)}- [{title}](#{anchor})");
        }
        return items.Count == 0 ? Strings.Get("Text_NoHeadings") : string.Join('\n', items);
    }

    /// <summary>GitHub anchor: lowercase, spaces→hyphens, punctuation dropped, duplicates suffixed -1, -2…</summary>
    static string Anchor(string title, Dictionary<string, int> seen)
    {
        var slug = new string(Deaccent(title).ToLowerInvariant()
            .Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-').ToArray())
            .Replace(' ', '-');
        if (!seen.TryAdd(slug, 0)) return $"{slug}-{++seen[slug]}";
        return slug;
    }

    [GeneratedRegex(@"^(#{1,6})\s+(.+)$")] private static partial Regex TocHeadingPattern();

    /// <summary>Turns CSV or TSV rows (first row = header) into an aligned Markdown table.</summary>
    public static string MarkdownTable(string input)
    {
        var rows = input.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (rows.Length == 0) throw new ArgumentException(Strings.Get("Error_NeedText"));

        // Tab wins if present (values often contain commas); otherwise split on commas.
        var delimiter = rows[0].Contains('\t') ? '\t' : ',';
        var cells = rows.Select(r => r.Split(delimiter).Select(c => c.Trim()).ToArray()).ToArray();
        var columns = cells.Max(r => r.Length);

        var width = new int[columns];
        foreach (var row in cells)
            for (var i = 0; i < row.Length; i++) width[i] = Math.Max(width[i], row[i].Length);
        for (var i = 0; i < columns; i++) width[i] = Math.Max(width[i], 3); // "---" needs room

        string Cell(string[] row, int i) => (i < row.Length ? row[i] : "").PadRight(width[i]);
        string Line(string[] row) => "| " + string.Join(" | ", Enumerable.Range(0, columns).Select(i => Cell(row, i))) + " |";

        var output = new List<string> { Line(cells[0]), "| " + string.Join(" | ", width.Select(w => new string('-', w))) + " |" };
        output.AddRange(cells.Skip(1).Select(Line));
        return string.Join('\n', output);
    }

    static string[] Lines(string s) => s.Replace("\r\n", "\n").Split('\n');

    public static string SortLines(string s) =>
        string.Join('\n', Lines(s).OrderBy(l => l, StringComparer.Create(Strings.Culture, ignoreCase: false)));

    public static string SortByLength(string s) =>
        string.Join('\n', Lines(s).OrderBy(l => l.Length).ThenBy(l => l, StringComparer.Ordinal));

    /// <summary>Removes duplicate lines, keeping the first occurrence and the original order.</summary>
    public static string Dedupe(string s) => string.Join('\n', Lines(s).Distinct());

    public static string ReverseText(string s)
    {
        // Enumerate text elements, not chars: reversing by char breaks emoji and accents.
        var elements = new List<string>();
        var e = StringInfo.GetTextElementEnumerator(s);
        while (e.MoveNext()) elements.Add((string)e.Current);
        elements.Reverse();
        return string.Concat(elements);
    }

    public static string ReverseLines(string s) => string.Join('\n', Lines(s).Reverse());

    // The Unicode combining marks block: stack these on a letter for the "zalgo" glitch look.
    static readonly char[] ZalgoMarks = Enumerable.Range(0x0300, 0x036F - 0x0300 + 1).Select(i => (char)i).ToArray();

    /// <summary>Stacks random combining marks on each character. ponytail: fixed intensity (0–5 marks);
    /// add a level argument if anyone wants to dial it.</summary>
    public static string Zalgo(string s)
    {
        if (s.Length == 0) throw new ArgumentException(Strings.Get("Error_NeedText"));
        var sb = new StringBuilder();
        foreach (var rune in s.EnumerateRunes())
        {
            sb.Append(rune);
            if (rune.Value is '\n' or ' ') continue;
            for (var n = Random.Shared.Next(0, 6); n > 0; n--)
                sb.Append(ZalgoMarks[Random.Shared.Next(ZalgoMarks.Length)]);
        }
        return sb.ToString();
    }

    /// <summary>Word frequency, most frequent first.</summary>
    public static string WordFrequency(string s)
    {
        var counts = Words(s)
            .GroupBy(w => w)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => $"{g.Count(),6}  {g.Key}");
        return string.Join('\n', counts);
    }

    /// <summary>French typography: narrow no-break space before ; : ! ? and inside « ».</summary>
    public static string FrenchTypography(string s)
    {
        const char Nbsp = ' ';
        s = Regex.Replace(s, @"\s*([;:!?%])", $"{Nbsp}$1");
        s = Regex.Replace(s, @"«\s*", $"«{Nbsp}");
        s = Regex.Replace(s, @"\s*»", $"{Nbsp}»");
        s = s.Replace("...", "…");
        // A colon inside a URL is not punctuation.
        return Regex.Replace(s, $@"(\w+){Nbsp}(//)", "$1:$2").Replace($"http{Nbsp}:", "http:").Replace($"https{Nbsp}:", "https:");
    }

    /// <summary>Replaces emails, phone numbers and long digit runs with placeholders,
    /// so a log or a spreadsheet can be shared.</summary>
    public static string Mask(string s)
    {
        s = EmailPattern().Replace(s, "[EMAIL]");
        s = PhonePattern().Replace(s, "[PHONE]");
        return LongNumberPattern().Replace(s, "[NUMBER]");
    }

    [GeneratedRegex(@"[\w.+-]+@[\w-]+\.[\w.-]+")] private static partial Regex EmailPattern();
    // A phone number is a digit run broken up by separators; a bare run of digits is just a number.
    [GeneratedRegex(@"(?<![\w.])\+?\d{1,4}(?:[ .-]\(?\d{1,4}\)?){2,6}(?![\w.])")] private static partial Regex PhonePattern();
    [GeneratedRegex(@"(?<![\w.])\d{6,}(?!\d)")] private static partial Regex LongNumberPattern();

    static readonly string[] LoremWords =
        ("lorem ipsum dolor sit amet consectetur adipiscing elit sed do eiusmod tempor incididunt ut labore et dolore " +
         "magna aliqua enim ad minim veniam quis nostrud exercitation ullamco laboris nisi aliquip ex ea commodo " +
         "consequat duis aute irure in reprehenderit voluptate velit esse cillum eu fugiat nulla pariatur excepteur " +
         "sint occaecat cupidatat non proident sunt culpa qui officia deserunt mollit anim id est laborum").Split(' ');

    /// <summary>Lorem Ipsum: a word count, or "3p" for three paragraphs.</summary>
    public static string Lorem(string input)
    {
        var spec = input.Trim().ToLowerInvariant();
        var paragraphs = spec.EndsWith('p');
        if (paragraphs) spec = spec[..^1].Trim();
        // int.Parse would throw .NET's raw English FormatException at the user.
        int n;
        if (spec.Length == 0) n = paragraphs ? 3 : 50;
        else if (!int.TryParse(spec, NumberStyles.Integer, CultureInfo.InvariantCulture, out n))
            throw new ArgumentException(Strings.Get("Error_OutOfRange", 1, 10000));
        if (n is < 1 or > 10000) throw new ArgumentException(Strings.Get("Error_OutOfRange", 1, 10000));

        return paragraphs
            ? string.Join("\n\n", Enumerable.Range(0, n).Select(i => Paragraph(i * 60)))
            : Sentence(Take(0, n));

        string Paragraph(int offset) => Sentence(Take(offset, 60));
        string[] Take(int offset, int count) => Enumerable.Range(offset, count).Select(i => LoremWords[i % LoremWords.Length]).ToArray();
        string Sentence(string[] words) => char.ToUpperInvariant(words[0][0]) + string.Join(' ', words)[1..] + ".";
    }

    static readonly Dictionary<char, string> MorseTable = new()
    {
        ['a'] = ".-", ['b'] = "-...", ['c'] = "-.-.", ['d'] = "-..", ['e'] = ".", ['f'] = "..-.",
        ['g'] = "--.", ['h'] = "....", ['i'] = "..", ['j'] = ".---", ['k'] = "-.-", ['l'] = ".-..",
        ['m'] = "--", ['n'] = "-.", ['o'] = "---", ['p'] = ".--.", ['q'] = "--.-", ['r'] = ".-.",
        ['s'] = "...", ['t'] = "-", ['u'] = "..-", ['v'] = "...-", ['w'] = ".--", ['x'] = "-..-",
        ['y'] = "-.--", ['z'] = "--..", ['0'] = "-----", ['1'] = ".----", ['2'] = "..---",
        ['3'] = "...--", ['4'] = "....-", ['5'] = ".....", ['6'] = "-....", ['7'] = "--...",
        ['8'] = "---..", ['9'] = "----.", ['.'] = ".-.-.-", [','] = "--..--", ['?'] = "..--..",
        ['\''] = ".----.", ['!'] = "-.-.--", ['/'] = "-..-.", ['('] = "-.--.", [')'] = "-.--.-",
        ['&'] = ".-...", [':'] = "---...", ['='] = "-...-", ['+'] = ".-.-.", ['-'] = "-....-",
        ['"'] = ".-..-.", ['@'] = ".--.-.",
    };

    /// <summary>Text ↔ Morse, direction detected from the input. Words are separated by " / ".</summary>
    public static string Morse(string input)
    {
        var s = input.Trim();
        if (s.Length == 0) return "";
        if (s.All(c => c is '.' or '-' or ' ' or '/' or '\n'))
        {
            var reverse = MorseTable.ToDictionary(kv => kv.Value, kv => kv.Key);
            return string.Join(' ', s.Split('/', StringSplitOptions.TrimEntries).Select(word =>
                string.Concat(word.Split([' ', '\n'], StringSplitOptions.RemoveEmptyEntries)
                    .Select(code => reverse.TryGetValue(code, out var c) ? c : '?'))));
        }
        return string.Join(" / ", Deaccent(s).ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => string.Join(' ', word.Select(c => MorseTable.TryGetValue(c, out var code) ? code : "?"))));
    }

    /// <summary>Line diff between two texts separated by a line containing only "---".</summary>
    // ponytail: LCS table is O(n*m) memory — fine for pasted text, swap in a Myers diff if someone
    // ever feeds it two 100k-line files.
    public static string Diff(string input)
    {
        var halves = Regex.Split(input.Replace("\r\n", "\n"), @"^---\s*$", RegexOptions.Multiline);
        if (halves.Length < 2) throw new ArgumentException(Strings.Get("Error_DiffUsage"));
        var (a, b) = (halves[0].Trim('\n').Split('\n'), halves[1].Trim('\n').Split('\n'));

        var lcs = new int[a.Length + 1, b.Length + 1];
        for (var i = a.Length - 1; i >= 0; i--)
            for (var j = b.Length - 1; j >= 0; j--)
                lcs[i, j] = a[i] == b[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);

        var output = new List<string>();
        for (int x = 0, y = 0; x < a.Length || y < b.Length;)
        {
            if (x < a.Length && y < b.Length && a[x] == b[y]) { output.Add("  " + a[x]); x++; y++; }
            else if (y < b.Length && (x == a.Length || lcs[x, y + 1] >= lcs[x + 1, y])) { output.Add("+ " + b[y]); y++; }
            else { output.Add("- " + a[x]); x++; }
        }
        return output.Count(l => l[0] != ' ') == 0
            ? Strings.Get("Diff_Identical")
            : string.Join('\n', output);
    }
}
