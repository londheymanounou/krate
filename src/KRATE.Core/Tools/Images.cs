using System.Buffers.Binary;
using System.Globalization;

namespace Krate.Core;

/// <summary>Image facts read straight from the file header — no decoder, so it stays in pure Core
/// and costs nothing to run. Reading pixels (convert, resize, EXIF) needs a platform imaging API
/// and lives outside Core.</summary>
public static class Images
{
    /// <summary>Format and dimensions of an image, plus megapixels and reduced aspect ratio.</summary>
    public static string Dimensions(string input)
    {
        var path = input.Trim().Trim('"');
        if (!File.Exists(path)) throw new ArgumentException(Strings.Get("Error_NoFile", path));

        // 64 bytes covers every header we read; JPEG needs more, handled inside Read.
        var (format, width, height) = Read(path);
        var mp = width * (long)height / 1_000_000.0;
        var (rw, rh) = ReduceRatio(width, height);
        var named = RatioName(rw, rh);

        return string.Join('\n',
            $"{Strings.Get("Images_Format")}  {format}",
            $"{Strings.Get("Images_Size")}  {width} × {height} px",
            string.Create(CultureInfo.InvariantCulture, $"{Strings.Get("Images_Pixels")}  {mp:0.##} MP"),
            $"{Strings.Get("Images_Ratio")}  {rw}:{rh}{(named is null ? "" : $" ({named})")}",
            $"{Strings.Get("Images_Orientation")}  {Strings.Get(width > height ? "Images_Landscape" : width < height ? "Images_Portrait" : "Images_Square")}");
    }

    /// <summary>Reads (format, width, height) from the magic bytes. Throws if unrecognised.</summary>
    public static (string Format, int Width, int Height) Read(string path)
    {
        using var stream = File.OpenRead(path);
        var head = new byte[Math.Min(32, stream.Length)];
        stream.ReadExactly(head, 0, head.Length);
        var b = head.AsSpan();

        // PNG: 8-byte signature, then IHDR with big-endian width/height at offset 16.
        if (b.Length >= 24 && b[..8].SequenceEqual(PngSignature))
            return ("PNG", ReadBE32(b[16..]), ReadBE32(b[20..]));

        // GIF: "GIF87a"/"GIF89a", little-endian width/height at offset 6.
        if (b.Length >= 10 && b[..3].SequenceEqual("GIF"u8))
            return ("GIF", BinaryPrimitives.ReadUInt16LittleEndian(b[6..]), BinaryPrimitives.ReadUInt16LittleEndian(b[8..]));

        // BMP: "BM", signed little-endian width/height at offset 18 (height can be negative = top-down).
        if (b.Length >= 26 && b[0] == 'B' && b[1] == 'M')
            return ("BMP", BinaryPrimitives.ReadInt32LittleEndian(b[18..]), Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(b[22..])));

        // WebP: RIFF container tagged "WEBP".
        if (b.Length >= 30 && b[..4].SequenceEqual("RIFF"u8) && b[8..12].SequenceEqual("WEBP"u8))
            return Webp(b);

        // JPEG: FF D8, then walk the segments to the frame header.
        if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xD8)
            return Jpeg(stream);

        throw new ArgumentException(Strings.Get("Error_UnknownImage", Path.GetFileName(path)));
    }

    static (string, int, int) Webp(ReadOnlySpan<byte> b)
    {
        var chunk = b[12..16];
        if (chunk.SequenceEqual("VP8X"u8)) // extended: 24-bit canvas size, stored as value-1
            return ("WebP", 1 + Read24LE(b[24..]), 1 + Read24LE(b[27..]));
        if (chunk.SequenceEqual("VP8L"u8)) // lossless: 14 bits each after the 0x2F signature
        {
            var bits = BinaryPrimitives.ReadUInt32LittleEndian(b[21..]);
            return ("WebP", 1 + (int)(bits & 0x3FFF), 1 + (int)(bits >> 14 & 0x3FFF));
        }
        if (chunk.SequenceEqual("VP8 "u8)) // lossy: 14-bit dimensions after the 0x9D012A start code
            return ("WebP", BinaryPrimitives.ReadUInt16LittleEndian(b[26..]) & 0x3FFF,
                            BinaryPrimitives.ReadUInt16LittleEndian(b[28..]) & 0x3FFF);
        throw new ArgumentException("WebP");
    }

    static (string, int, int) Jpeg(FileStream stream)
    {
        stream.Position = 2;
        Span<byte> marker = stackalloc byte[4];
        while (stream.Read(marker[..2]) == 2)
        {
            if (marker[0] != 0xFF) throw new ArgumentException("JPEG");
            var kind = marker[1];
            // SOF0-3, 5-7, 9-11, 13-15 carry the dimensions; everything else we skip by its length.
            if (kind is >= 0xC0 and <= 0xCF && kind is not (0xC4 or 0xC8 or 0xCC))
            {
                stream.Position += 3; // length(2) + precision(1)
                stream.ReadExactly(marker);
                return ("JPEG", BinaryPrimitives.ReadUInt16BigEndian(marker[2..]), BinaryPrimitives.ReadUInt16BigEndian(marker[..2]));
            }
            if (kind is 0xD8 or 0xD9 or (>= 0xD0 and <= 0xD7)) continue; // markers without a length
            stream.ReadExactly(marker[..2]);
            stream.Position += BinaryPrimitives.ReadUInt16BigEndian(marker[..2]) - 2;
        }
        throw new ArgumentException("JPEG");
    }

    /// <summary>"1920x1080" → its reduced ratio; "16:9 1920" → the matching height.</summary>
    public static string Ratio(string input)
    {
        var s = input.Trim().ToLowerInvariant();
        var parts = s.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);

        // "16:9 1920" — fill in the missing dimension from a known one.
        if (parts.Length == 2 && parts[0].Contains(':') && int.TryParse(parts[1], out var known))
        {
            var ratio = parts[0].Split(':');
            var (rw, rh) = (int.Parse(ratio[0], CultureInfo.InvariantCulture), int.Parse(ratio[1], CultureInfo.InvariantCulture));
            return string.Join('\n',
                $"{known} × {(long)known * rh / rw} px  ({Strings.Get("Images_FromWidth")})",
                $"{(long)known * rw / rh} × {known} px  ({Strings.Get("Images_FromHeight")})");
        }

        // "1920x1080" — reduce it.
        var wh = s.Split(['x', ':', '×'], StringSplitOptions.RemoveEmptyEntries);
        if (wh.Length != 2 || !int.TryParse(wh[0], out var w) || !int.TryParse(wh[1], out var h) || w <= 0 || h <= 0)
            throw new ArgumentException(Strings.Get("Error_RatioUsage"));
        var (a, c) = ReduceRatio(w, h);
        var name = RatioName(a, c);
        return $"{a}:{c}{(name is null ? "" : $"  ({name})")}";
    }

    static (int, int) ReduceRatio(int w, int h)
    {
        var g = (int)Maths.Gcd(w, h);
        return g == 0 ? (w, h) : (w / g, h / g);
    }

    // 16:10 reduces to 8:5, so both spellings map to the same name.
    static string? RatioName(int w, int h) => (w, h) switch
    {
        (16, 9) => "16:9",
        (4, 3) => "4:3",
        (3, 2) => "3:2",
        (8, 5) => "16:10",
        (21, 9) or (7, 3) => "21:9",
        (1, 1) => "1:1",
        (5, 4) => "5:4",
        _ => null,
    };

    static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    static int ReadBE32(ReadOnlySpan<byte> b) => (int)BinaryPrimitives.ReadUInt32BigEndian(b);
    static int Read24LE(ReadOnlySpan<byte> b) => b[0] | b[1] << 8 | b[2] << 16;
}
