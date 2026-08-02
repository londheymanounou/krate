using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Krate.Core;

public static class Escapes
{
    // JsonEncodedText rather than the serializer: no reflection, no source-gen context to maintain.
    public static string Json(string s) => '"' + JsonEncodedText.Encode(s).ToString() + '"';

    public static string JsonUnescape(string s)
    {
        var t = s.Trim();
        if (!t.StartsWith('"')) t = '"' + t + '"';
        using var doc = JsonDocument.Parse(t);
        return doc.RootElement.GetString() ?? "";
    }

    /// <summary>SQL string literal: doubles the quotes. Escaping is not a substitute for
    /// parameterised queries — this is for pasting a value into a console.</summary>
    public static string Sql(string s) => "'" + s.Replace("'", "''") + "'";

    /// <summary>POSIX shell literal: single quotes, with the standard '"'"' dance inside.</summary>
    public static string Shell(string s) => "'" + s.Replace("'", "'\"'\"'") + "'";

    /// <summary>Decodes a JWT locally: header and payload, dates spelled out.
    /// The signature is NOT verified — that needs the issuer's key.</summary>
    public static string Jwt(string input)
    {
        var parts = input.Trim().Split('.');
        if (parts.Length < 2) throw new ArgumentException(Strings.Get("Error_BadJwt"));

        var header = Krate.Core.Json.Format(FromBase64Url(parts[0]));
        var payload = FromBase64Url(parts[1]);
        var claims = Krate.Core.Json.Format(payload);

        var dates = new List<string>();
        using (var doc = JsonDocument.Parse(payload))
            foreach (var claim in new[] { "iat", "nbf", "exp" })
                if (doc.RootElement.TryGetProperty(claim, out var v) && v.TryGetInt64(out var seconds))
                {
                    var when = DateTimeOffset.FromUnixTimeSeconds(seconds);
                    var note = claim == "exp" ? Strings.Get(when < DateTimeOffset.UtcNow ? "Jwt_Expired" : "Jwt_Valid") : "";
                    dates.Add($"{claim}  {when.UtcDateTime:yyyy-MM-dd HH:mm:ss}Z  {note}".TrimEnd());
                }

        var lines = new List<string> { Strings.Get("Jwt_Header"), header, "", Strings.Get("Jwt_Payload"), claims };
        if (dates.Count > 0) lines.AddRange(["", Strings.Get("Jwt_Dates"), .. dates]);
        lines.AddRange(["", Strings.Get("Jwt_NotVerified")]);
        return string.Join('\n', lines);
    }

    /// <summary>Convert.FromBase64String throws a raw, untranslated FormatException — a malformed
    /// token is a bad token, and the caller already has a message for that.</summary>
    static string FromBase64Url(string s)
    {
        var padded = s.Replace('-', '+').Replace('_', '/').PadRight((s.Length + 3) / 4 * 4, '=');
        try { return Encoding.UTF8.GetString(Convert.FromBase64String(padded)); }
        catch (FormatException) { throw new ArgumentException(Strings.Get("Error_BadJwt")); }
    }

    /// <summary>Plain and scientific notation of the same number, both directions.</summary>
    public static string Scientific(string input)
    {
        if (!double.TryParse(input.Trim().Replace(" ", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            throw new ArgumentException(Strings.Get("Error_NeedNumber"));
        return string.Join('\n',
            string.Create(CultureInfo.InvariantCulture, $"PLAIN       {value:0.##############################}"),
            string.Create(CultureInfo.InvariantCulture, $"SCIENTIFIC  {value:0.##############E+0}"),
            string.Create(CultureInfo.InvariantCulture, $"ENGINEERING {Engineering(value)}"));
    }

    static string Engineering(double v)
    {
        if (v == 0) return "0e0";
        var exponent = (int)Math.Floor(Math.Log10(Math.Abs(v)) / 3) * 3; // exponent is a multiple of 3
        return string.Create(CultureInfo.InvariantCulture, $"{v / Math.Pow(10, exponent):0.##########}e{exponent}");
    }

    public static string ToCrlf(string s) => s.Replace("\r\n", "\n").Replace("\n", "\r\n");
    public static string ToLf(string s) => s.Replace("\r\n", "\n");

    /// <summary>Windows ↔ Unix path separators, direction detected from the input.</summary>
    public static string Path(string s)
    {
        var t = s.Trim();
        return t.Contains('\\') ? t.Replace('\\', '/') : t.Replace('/', '\\');
    }

    /// <summary>Makes a filename Windows-safe: forbidden characters, trailing dots and
    /// reserved device names (CON, NUL…) all handled.</summary>
    public static string Filename(string s)
    {
        var name = new string(s.Trim().Select(c => System.IO.Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray())
            .TrimEnd(' ', '.');
        if (name.Length == 0) return "_";
        var stem = System.IO.Path.GetFileNameWithoutExtension(name);
        string[] reserved = ["CON", "PRN", "AUX", "NUL", .. Enumerable.Range(1, 9).SelectMany(i => new[] { $"COM{i}", $"LPT{i}" })];
        return reserved.Contains(stem, StringComparer.OrdinalIgnoreCase) ? "_" + name : name;
    }
}
