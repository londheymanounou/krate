using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Krate.Core;

/// <summary>Markdown → HTML for the common subset (headings, emphasis, code, links, lists, quotes,
/// rules). Not CommonMark-complete — a full parser is a library's job — but it covers what people
/// paste. Everything is HTML-escaped first, so it is safe to render.</summary>
public static partial class Markdown
{
    public static string ToHtml(string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var html = new StringBuilder();
        var listType = (string?)null;   // "ul" or "ol" while inside a list
        var inCode = false;
        var paragraph = new List<string>();

        void FlushParagraph()
        {
            if (paragraph.Count == 0) return;
            html.Append("<p>").Append(Inline(string.Join(' ', paragraph))).AppendLine("</p>");
            paragraph.Clear();
        }
        void CloseList() { if (listType != null) { html.AppendLine($"</{listType}>"); listType = null; } }

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();

            if (line.StartsWith("```"))
            {
                FlushParagraph(); CloseList();
                html.AppendLine(inCode ? "</code></pre>" : "<pre><code>");
                inCode = !inCode;
                continue;
            }
            if (inCode) { html.AppendLine(WebUtility.HtmlEncode(raw)); continue; }

            if (line.Length == 0) { FlushParagraph(); CloseList(); continue; }

            // Heading: one to six # then a space.
            if (HeadingPattern().Match(line) is { Success: true } head)
            {
                FlushParagraph(); CloseList();
                var level = head.Groups[1].Value.Length;
                html.AppendLine($"<h{level}>{Inline(head.Groups[2].Value)}</h{level}>");
                continue;
            }
            // Horizontal rule.
            if (Regex.IsMatch(line, @"^(\*\s*){3,}$|^(-\s*){3,}$|^(_\s*){3,}$"))
            {
                FlushParagraph(); CloseList();
                html.AppendLine("<hr>");
                continue;
            }
            // Blockquote.
            if (line.StartsWith("> "))
            {
                FlushParagraph(); CloseList();
                html.AppendLine($"<blockquote>{Inline(line[2..])}</blockquote>");
                continue;
            }
            // List item: "- ", "* ", or "1. ".
            var ordered = OrderedItemPattern().Match(line);
            var unordered = UnorderedItemPattern().Match(line);
            if (ordered.Success || unordered.Success)
            {
                FlushParagraph();
                var want = ordered.Success ? "ol" : "ul";
                if (listType != want) { CloseList(); html.AppendLine($"<{want}>"); listType = want; }
                var content = (ordered.Success ? ordered : unordered).Groups[1].Value;
                html.AppendLine($"<li>{Inline(content)}</li>");
                continue;
            }

            CloseList();
            paragraph.Add(line);
        }
        FlushParagraph();
        CloseList();
        if (inCode) html.AppendLine("</code></pre>");
        return html.ToString().TrimEnd('\n');
    }

    /// <summary>Inline spans: code first (so its content is not further parsed), then links, then
    /// bold/italic. Text is HTML-escaped up front.</summary>
    static string Inline(string text)
    {
        // Pull code spans out, escape the rest, then restore them escaped — so `**x**` in code stays literal.
        var codes = new List<string>();
        text = CodeSpanPattern().Replace(text, m => { codes.Add(WebUtility.HtmlEncode(m.Groups[1].Value)); return $"\x00{codes.Count - 1}\x00"; });
        text = WebUtility.HtmlEncode(text);

        text = LinkPattern().Replace(text, m => $"<a href=\"{m.Groups[2].Value}\">{m.Groups[1].Value}</a>");
        // ** ** and __ __ are two alternatives, so pick whichever group actually captured.
        text = BoldPattern().Replace(text, m => $"<strong>{Captured(m)}</strong>");
        text = ItalicPattern().Replace(text, m => $"<em>{Captured(m)}</em>");

        return Regex.Replace(text, "\x00(\\d+)\x00", m => $"<code>{codes[int.Parse(m.Groups[1].Value)]}</code>");
    }

    static string Captured(Match m) => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;

    [GeneratedRegex(@"^(#{1,6})\s+(.+)$")] private static partial Regex HeadingPattern();
    [GeneratedRegex(@"^[-*]\s+(.+)$")] private static partial Regex UnorderedItemPattern();
    [GeneratedRegex(@"^\d+\.\s+(.+)$")] private static partial Regex OrderedItemPattern();
    [GeneratedRegex(@"`([^`]+)`")] private static partial Regex CodeSpanPattern();
    [GeneratedRegex(@"\[([^\]]+)\]\(([^)]+)\)")] private static partial Regex LinkPattern();
    [GeneratedRegex(@"\*\*([^*]+)\*\*|__([^_]+)__")] private static partial Regex BoldPattern();
    [GeneratedRegex(@"\*([^*]+)\*|_([^_]+)_")] private static partial Regex ItalicPattern();
}
