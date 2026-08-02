using System.Text;
using System.Text.Json;

namespace Krate.Core;

public static class Json
{
    // JsonDocument/Utf8JsonWriter only: no serializer, no reflection, so this stays AOT-safe.
    static readonly JsonDocumentOptions ReadOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static string Format(string json) => Write(json, indented: true);
    public static string Minify(string json) => Write(json, indented: false);

    static string Write(string json, bool indented)
    {
        JsonDocument doc;
        // JsonDocument.Parse throws a raw JsonException whose message is .NET's own English
        // text — the same leak Validate had. Report the location in the interface language.
        try { doc = JsonDocument.Parse(json, ReadOptions); }
        catch (JsonException ex)
        {
            throw new ArgumentException(
                Strings.Get("Json_Invalid", (ex.LineNumber ?? 0) + 1, (ex.BytePositionInLine ?? 0) + 1));
        }

        using (doc)
        {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = indented }))
            doc.WriteTo(writer);
        return Encoding.UTF8.GetString(buffer.ToArray());
        }
    }

    /// <summary>Says where the problem is — a validator that only says "invalid" is useless.</summary>
    public static string Validate(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json, ReadOptions);
            return Strings.Get("Json_Valid");
        }
        catch (JsonException ex)
        {
            // ex.Message was appended here, but it is .NET's English text in every language and
            // restates the line and position the localized part already gives.
            return Strings.Get("Json_Invalid", (ex.LineNumber ?? 0) + 1, (ex.BytePositionInLine ?? 0) + 1);
        }
    }
}
