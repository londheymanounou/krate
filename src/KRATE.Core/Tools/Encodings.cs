using System.Globalization;
using System.Net;
using System.Text;

namespace Krate.Core;

public static class Encodings
{
    public static string Base64Encode(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s));

    /// <summary>Convert.FromBase64String throws a raw, untranslated FormatException whose message
    /// reached the user verbatim in every language. Give it the localized one instead.</summary>
    public static string Base64Decode(string s)
    {
        try { return Encoding.UTF8.GetString(Convert.FromBase64String(s.Trim())); }
        catch (FormatException) { throw new ArgumentException(Strings.Get("Error_BadBase64")); }
    }

    public static string UrlEncode(string s) => Uri.EscapeDataString(s);
    public static string UrlDecode(string s) => Uri.UnescapeDataString(s);

    public static string HtmlEncode(string s) => WebUtility.HtmlEncode(s);
    public static string HtmlDecode(string s) => WebUtility.HtmlDecode(s);

    /// <summary>Shows one number in binary, octal, decimal and hex at once.
    /// Input base is taken from the prefix (0b / 0o / 0x), decimal otherwise.</summary>
    public static string Bases(string s)
    {
        var t = s.Trim().Replace("_", "").Replace(" ", "");
        var negative = t.StartsWith('-');
        var digits = negative ? t[1..] : t;

        // Decimal keeps its own sign, so long.MinValue parses instead of overflowing on negation.
        // Parse failures otherwise surface .NET's own untranslated message to the user.
        long value;
        try
        {
            value = digits.ToLowerInvariant() switch
            {
                ['0', 'b', .. var bits] => Sign(Convert.ToInt64(bits, 2), negative),
                ['0', 'o', .. var oct] => Sign(Convert.ToInt64(oct, 8), negative),
                ['0', 'x', .. var hex] => Sign(Convert.ToInt64(hex, 16), negative),
                _ => long.Parse(t, CultureInfo.InvariantCulture),
            };
        }
        catch (Exception e) when (e is FormatException or OverflowException or ArgumentOutOfRangeException)
        {
            throw new ArgumentException(Strings.Get("Error_NeedNumber"));
        }

        // Sign-and-magnitude, so -255 reads as -0xFF instead of a two's-complement wall of Fs.
        var sign = value < 0 ? "-" : "";
        var magnitude = value < 0 ? (ulong)(-(value + 1)) + 1 : (ulong)value; // safe at long.MinValue
        return string.Join('\n',
            $"BIN  {sign}0b{ToBase(magnitude, 2)}",
            $"OCT  {sign}0o{ToBase(magnitude, 8)}",
            $"DEC  {value.ToString(CultureInfo.InvariantCulture)}",
            $"HEX  {sign}0x{ToBase(magnitude, 16).ToUpperInvariant()}");
    }

    static long Sign(long value, bool negative) => negative ? -value : value;

    static string ToBase(ulong value, int radix)
    {
        if (value == 0) return "0";
        var digits = new StringBuilder();
        for (; value > 0; value /= (ulong)radix)
            digits.Insert(0, "0123456789abcdef"[(int)(value % (ulong)radix)]);
        return digits.ToString();
    }
}
