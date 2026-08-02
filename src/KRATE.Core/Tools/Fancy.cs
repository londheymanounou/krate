using System.Globalization;
using System.Text;

namespace Krate.Core;

/// <summary>Turns plain letters into the Unicode "math alphanumeric" styles used on social media.
/// Pure codepoint arithmetic — the styles are contiguous blocks, so no lookup table is needed.</summary>
public static class Fancy
{
    // The Mathematical Alphanumeric block has holes: seven double-struck capitals and italic small h
    // were encoded earlier in Letterlike Symbols, so those codepoints are reserved here. Without these
    // overrides the affected letters render as tofu. The bold / mono / sans styles are gap-free.
    static readonly Dictionary<char, int> ItalicHoles = new() { ['h'] = 0x210E };
    static readonly Dictionary<char, int> DoubleHoles = new()
    {
        ['C'] = 0x2102, ['H'] = 0x210D, ['N'] = 0x2115, ['P'] = 0x2119, ['Q'] = 0x211A, ['R'] = 0x211D, ['Z'] = 0x2124,
    };

    public static string Convert(string text)
    {
        if (text.Length == 0) throw new ArgumentException(Strings.Get("Error_NeedText"));
        return string.Join('\n',
            $"{Strings.Get("Fancy_Bold"),-12} {Map(text, 0x1D400, 0x1D41A, 0x1D7CE)}",
            $"{Strings.Get("Fancy_Italic"),-12} {Map(text, 0x1D434, 0x1D44E, null, ItalicHoles)}",
            $"{Strings.Get("Fancy_Script"),-12} {Map(text, 0x1D4D0, 0x1D4EA, null)}",       // bold script: gap-free
            $"{Strings.Get("Fancy_Fraktur"),-12} {Map(text, 0x1D56C, 0x1D586, null)}",      // bold fraktur: gap-free
            $"{Strings.Get("Fancy_Mono"),-12} {Map(text, 0x1D670, 0x1D68A, 0x1D7F6)}",
            $"{Strings.Get("Fancy_Double"),-12} {Map(text, 0x1D538, 0x1D552, 0x1D7D8, DoubleHoles)}",
            $"{Strings.Get("Fancy_Circled"),-12} {Circled(text)}",
            $"{Strings.Get("Fancy_Wide"),-12} {Fullwidth(text)}");
    }

    /// <summary>Maps A→upperBase, a→lowerBase, 0→digitBase (when the style has digits), applying any
    /// hole overrides first so reserved codepoints are replaced with the real letter.</summary>
    static string Map(string text, int upperBase, int lowerBase, int? digitBase, Dictionary<char, int>? holes = null)
    {
        var sb = new StringBuilder();
        foreach (var rune in text.EnumerateRunes())
        {
            var c = rune.Value;
            if (holes is not null && c <= char.MaxValue && holes.TryGetValue((char)c, out var cp)) sb.Append(char.ConvertFromUtf32(cp));
            else if (c is >= 'A' and <= 'Z') sb.Append(char.ConvertFromUtf32(upperBase + (c - 'A')));
            else if (c is >= 'a' and <= 'z') sb.Append(char.ConvertFromUtf32(lowerBase + (c - 'a')));
            else if (digitBase is { } d && c is >= '0' and <= '9') sb.Append(char.ConvertFromUtf32(d + (c - '0')));
            else sb.Append(rune);
        }
        return sb.ToString();
    }

    static string Circled(string text)
    {
        var sb = new StringBuilder();
        foreach (var rune in text.EnumerateRunes())
        {
            var c = rune.Value;
            if (c is >= 'A' and <= 'Z') sb.Append(char.ConvertFromUtf32(0x24B6 + (c - 'A')));
            else if (c is >= 'a' and <= 'z') sb.Append(char.ConvertFromUtf32(0x24D0 + (c - 'a')));
            else if (c is >= '1' and <= '9') sb.Append(char.ConvertFromUtf32(0x2460 + (c - '1')));
            else sb.Append(rune);
        }
        return sb.ToString();
    }

    /// <summary>Fullwidth forms — the "ａｅｓｔｈｅｔｉｃ" look. ASCII 0x21–0x7E map to U+FF01+offset.</summary>
    static string Fullwidth(string text)
    {
        var sb = new StringBuilder();
        foreach (var rune in text.EnumerateRunes())
        {
            var c = rune.Value;
            if (c == ' ') sb.Append('　');                                   // ideographic space
            else if (c is >= 0x21 and <= 0x7E) sb.Append(char.ConvertFromUtf32(0xFF01 + (c - 0x21)));
            else sb.Append(rune);
        }
        return sb.ToString();
    }
}
