using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Krate.Core;

/// <summary>CSV ↔ JSON. Both are well-specified enough that a hand-written RFC 4180 reader beats
/// pulling in a CSV library, and it stays AOT-safe (no serializer reflection).</summary>
public static class Data
{
    /// <summary>CSV (first row = headers) → a JSON array of objects.</summary>
    public static string CsvToJson(string csv)
    {
        var rows = ParseCsv(csv);
        if (rows.Count == 0) return "[]";
        var headers = rows[0];

        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartArray();
            foreach (var row in rows.Skip(1))
            {
                w.WriteStartObject();
                for (var i = 0; i < headers.Count; i++)
                {
                    var value = i < row.Count ? row[i] : "";
                    w.WritePropertyName(headers[i]);
                    // Numbers and booleans go in unquoted so the JSON is actually typed, not all-strings.
                    if (bool.TryParse(value, out var b)) w.WriteBooleanValue(b);
                    else if (LooksNumeric(value, out var d)) w.WriteNumberValue(d);
                    else w.WriteStringValue(value);
                }
                w.WriteEndObject();
            }
            w.WriteEndArray();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>A JSON array of objects → CSV. Columns are the union of keys, in first-seen order.</summary>
    public static string JsonToCsv(string json)
    {
        // JsonDocument.Parse throws a raw JsonReaderException whose message is .NET's own English
        // text; "nonsense" produced "Expected the literal 'null'. LineNumber: 0 | ...".
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException) { throw new ArgumentException(Strings.Get("Error_JsonNotArray")); }

        using (doc)
        {
        if (doc.RootElement.ValueKind != JsonValueKind.Array) throw new ArgumentException(Strings.Get("Error_JsonNotArray"));

        var rows = doc.RootElement.EnumerateArray().ToArray();
        var columns = new List<string>();
        foreach (var row in rows)
            if (row.ValueKind == JsonValueKind.Object)
                foreach (var prop in row.EnumerateObject())
                    if (!columns.Contains(prop.Name)) columns.Add(prop.Name);
        if (columns.Count == 0) throw new ArgumentException(Strings.Get("Error_JsonNotArray"));

        // Join with '\n' explicitly — AppendLine would emit \r\n on Windows and change the output.
        var lines = new List<string> { string.Join(',', columns.Select(Escape)) };
        foreach (var row in rows)
            lines.Add(string.Join(',', columns.Select(c =>
                Escape(row.ValueKind == JsonValueKind.Object && row.TryGetProperty(c, out var v) ? Scalar(v) : ""))));
        return string.Join('\n', lines);
        }
    }

    /// <summary>JSON → YAML (block style). One direction: YAML→JSON needs a real YAML parser, which is
    /// library-sized — a naive one that mis-parses is worse than none.</summary>
    // ponytail: emitter over the JSON data model, no dependency. Add YAML→JSON only with a proper parser.
    public static string JsonToYaml(string json)
    {
        JsonDocument doc;
        // Same leak Write and Validate had: JsonDocument.Parse throws .NET's own English message.
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException ex)
        {
            throw new ArgumentException(
                Strings.Get("Json_Invalid", (ex.LineNumber ?? 0) + 1, (ex.BytePositionInLine ?? 0) + 1));
        }

        using var _ = doc;
        var sb = new StringBuilder();
        if (doc.RootElement.ValueKind is JsonValueKind.Object or JsonValueKind.Array && HasChildren(doc.RootElement))
            EmitYaml(doc.RootElement, sb, 0);
        else
            sb.Append(YamlScalar(doc.RootElement)).Append('\n'); // a bare scalar document
        return sb.ToString().TrimEnd('\n');
    }

    static bool HasChildren(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Object => e.EnumerateObject().Any(),
        JsonValueKind.Array => e.EnumerateArray().Any(),
        _ => false,
    };

    static void EmitYaml(JsonElement e, StringBuilder sb, int indent)
    {
        var pad = new string(' ', indent * 2);
        if (e.ValueKind == JsonValueKind.Object)
            foreach (var p in e.EnumerateObject())
            {
                sb.Append(pad).Append(YamlKey(p.Name)).Append(':');
                EmitChild(p.Value, sb, indent);
            }
        else // array
            foreach (var item in e.EnumerateArray())
            {
                sb.Append(pad).Append('-');
                EmitChild(item, sb, indent);
            }
    }

    // A container child goes on its own indented lines; a scalar (or empty container) stays inline.
    static void EmitChild(JsonElement v, StringBuilder sb, int indent)
    {
        if (v.ValueKind is JsonValueKind.Object or JsonValueKind.Array && HasChildren(v))
        {
            sb.Append('\n');
            EmitYaml(v, sb, indent + 1);
        }
        else if (v.ValueKind == JsonValueKind.Object) sb.Append(" {}\n");
        else if (v.ValueKind == JsonValueKind.Array) sb.Append(" []\n");
        else sb.Append(' ').Append(YamlScalar(v)).Append('\n');
    }

    static string YamlScalar(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => YamlString(e.GetString() ?? ""),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null => "null",
        _ => e.GetRawText(), // numbers verbatim
    };

    static string YamlKey(string k) => YamlString(k);

    // Quote when a plain scalar would be misread as something else (a number, bool, null, a flow char,
    // or has edge whitespace / a "key: value" lookalike).
    static string YamlString(string s)
    {
        var needsQuote = s.Length == 0
            || s != s.Trim()
            || "!&*?|>%@`\"'#,-:[]{}".IndexOf(s[0]) >= 0
            || s.Contains(": ") || s.Contains(" #") || s.Contains('\n') || s.Contains('\t')
            || bool.TryParse(s, out _)
            || double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out _)
            || s is "null" or "~" or "yes" or "no" or "on" or "off";
        return needsQuote ? "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"" : s;
    }

    // "007" and "0900" are ids/zips/phone parts — keep them as strings, not the number 7.
    static bool LooksNumeric(string v, out decimal d)
    {
        d = 0;
        if (v.Length > 1 && v[0] == '0' && char.IsDigit(v[1])) return false;
        return decimal.TryParse(v, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out d);
    }

    static string Scalar(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString() ?? "",
        JsonValueKind.Null or JsonValueKind.Undefined => "",
        JsonValueKind.Object or JsonValueKind.Array => e.GetRawText(), // nested value: keep the JSON verbatim in the cell
        _ => e.GetRawText(),
    };

    /// <summary>A field needs quoting if it contains a comma, quote, or line break.</summary>
    static string Escape(string field) =>
        field.AsSpan().IndexOfAny(",\"\n\r") >= 0 ? $"\"{field.Replace("\"", "\"\"")}\"" : field;

    /// <summary>RFC 4180 reader: honours quoted fields, "" escapes, and commas/newlines inside quotes.</summary>
    public static List<List<string>> ParseCsv(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var i = 0;
        var s = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var any = false; // did this row have any content, so a trailing newline doesn't add a blank record

        void EndField() { row.Add(field.ToString()); field.Clear(); }
        void EndRow() { EndField(); rows.Add(row); row = []; any = false; }

        while (i < s.Length)
        {
            var c = s[i];
            if (inQuotes)
            {
                if (c == '"' && i + 1 < s.Length && s[i + 1] == '"') { field.Append('"'); i += 2; }
                else if (c == '"') { inQuotes = false; i++; }
                else { field.Append(c); i++; }
            }
            else switch (c)
            {
                case '"': inQuotes = true; any = true; i++; break;
                case ',': EndField(); any = true; i++; break;
                case '\n': EndRow(); i++; break;
                default: field.Append(c); any = true; i++; break;
            }
        }
        if (any || field.Length > 0 || row.Count > 0) EndRow();
        return rows;
    }
}
