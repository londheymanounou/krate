using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Krate.Core;

/// <summary>Reads the common EXIF tags out of a JPEG by parsing the APP1/TIFF structure directly —
/// no imaging library. Camera, lens, exposure and date, plus the pixel size from the file header.</summary>
public static class Exif
{
    public static string Read(string input)
    {
        var path = input.Trim().Trim('"');
        if (!File.Exists(path)) throw new ArgumentException(Strings.Get("Error_NoFile", path));

        var tags = Parse(File.ReadAllBytes(path));
        // Dimensions come from the header reader we already have — always available.
        try { var (_, w, h) = Images.Read(path); tags["Size"] = $"{w} × {h} px"; } catch { }

        if (tags.Count == 0) return Strings.Get("Exif_None");
        string[] order = ["Make", "Model", "LensModel", "Software", "Size", "DateTimeOriginal", "DateTime",
                          "Orientation", "ExposureTime", "FNumber", "ISO", "FocalLength"];
        return string.Join('\n', order.Where(tags.ContainsKey).Select(k => $"{Label(k),-14} {tags[k]}"));
    }

    static string Label(string key) => key switch
    {
        "Make" => "Camera", "Model" => "Model", "LensModel" => "Lens", "Software" => "Software",
        "Size" => "Dimensions", "DateTimeOriginal" => "Taken", "DateTime" => "Modified",
        "Orientation" => "Orientation", "ExposureTime" => "Shutter", "FNumber" => "Aperture",
        "ISO" => "ISO", "FocalLength" => "Focal length", _ => key,
    };

    /// <summary>Parses the EXIF tags from JPEG bytes into a name→value map.</summary>
    public static Dictionary<string, string> Parse(byte[] bytes)
    {
        var result = new Dictionary<string, string>();
        if (bytes.Length < 4 || bytes[0] != 0xFF || bytes[1] != 0xD8) return result; // not a JPEG

        // Walk the segments to the APP1 "Exif" block.
        var i = 2;
        while (i + 4 <= bytes.Length && bytes[i] == 0xFF)
        {
            var marker = bytes[i + 1];
            if (marker is 0xDA or 0xD9) break; // start-of-scan / end
            var len = (bytes[i + 2] << 8) | bytes[i + 3];
            if (marker == 0xE1 && i + 10 <= bytes.Length && Encoding.ASCII.GetString(bytes, i + 4, 4) == "Exif")
            {
                ParseTiff(bytes, i + 10, result); // skip "Exif\0\0"
                break;
            }
            i += 2 + len;
        }
        return result;
    }

    static void ParseTiff(byte[] b, int tiff, Dictionary<string, string> result)
    {
        if (tiff + 8 > b.Length) return;
        var little = b[tiff] == 'I';
        uint U32(int o) => little ? BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(tiff + o)) : BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(tiff + o));

        var ifd0 = (int)U32(4);
        ReadIfd(b, tiff, ifd0, little, result, sub: true);
    }

    static void ReadIfd(byte[] b, int tiff, int ifd, bool little, Dictionary<string, string> result, bool sub)
    {
        if (ifd <= 0 || tiff + ifd + 2 > b.Length) return;
        ushort U16(int abs) => little ? BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(abs)) : BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(abs));
        uint U32(int abs) => little ? BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(abs)) : BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(abs));

        var count = U16(tiff + ifd);
        var pos = tiff + ifd + 2;
        for (var e = 0; e < count && pos + 12 <= b.Length; e++, pos += 12)
        {
            var tag = U16(pos);
            var type = U16(pos + 2);
            var num = U32(pos + 4);
            var size = num * type switch { 1 or 2 or 7 => 1u, 3 => 2u, 4 => 4u, 5 or 10 => 8u, _ => 1u };
            var valuePos = size <= 4 ? pos + 8 : tiff + (int)U32(pos + 8);
            if (valuePos < 0 || valuePos > b.Length) continue;

            // The Exif sub-IFD pointer — follow it for exposure/lens/date tags.
            if (tag == 0x8769 && sub) { ReadIfd(b, tiff, (int)U32(pos + 8), little, result, sub: false); continue; }

            var name = Name(tag);
            if (name is null) continue;
            var value = Format(b, valuePos, type, num, little, tag, U16, U32);
            if (value is not null) result[name] = value;
        }
    }

    static string? Name(int tag) => tag switch
    {
        0x010F => "Make", 0x0110 => "Model", 0x0131 => "Software", 0x0132 => "DateTime", 0x0112 => "Orientation",
        0x829A => "ExposureTime", 0x829D => "FNumber", 0x8827 => "ISO", 0x920A => "FocalLength",
        0x9003 => "DateTimeOriginal", 0xA434 => "LensModel", _ => null,
    };

    static string? Format(byte[] b, int pos, int type, uint num, bool little, int tag, Func<int, ushort> U16, Func<int, uint> U32)
    {
        switch (type)
        {
            case 2: // ASCII
                var end = pos; while (end < b.Length && end < pos + num && b[end] != 0) end++;
                var s = Encoding.ASCII.GetString(b, pos, end - pos).Trim();
                return s.Length == 0 ? null : s;
            case 3: // SHORT
                var sh = U16(pos);
                return tag == 0x0112 ? Orientation(sh) : sh.ToString(CultureInfo.InvariantCulture);
            case 4: // LONG
                return U32(pos).ToString(CultureInfo.InvariantCulture);
            case 5: // RATIONAL
                if (pos + 8 > b.Length) return null;
                double n = U32(pos), d = U32(pos + 4);
                if (d == 0) return null;
                // These were plain interpolated strings, so they formatted with CurrentCulture —
                // the OS language — giving "f/2,8" on a French machine while the SHORT and LONG
                // cases beside them are explicitly invariant. EXIF values are technical data and
                // the whole file means invariant; the culture was simply missing.
                return tag switch
                {
                    0x829A => n / d < 1
                        ? string.Create(CultureInfo.InvariantCulture, $"1/{d / n:0}s")
                        : string.Create(CultureInfo.InvariantCulture, $"{n / d:0.#}s"),   // shutter
                    0x829D => string.Create(CultureInfo.InvariantCulture, $"f/{n / d:0.#}"),   // aperture
                    0x920A => string.Create(CultureInfo.InvariantCulture, $"{n / d:0.#} mm"), // focal length
                    _ => string.Create(CultureInfo.InvariantCulture, $"{n / d:0.##}"),
                };
        }
        return null;
    }

    static string Orientation(int v) => v switch
    {
        1 => "Normal", 3 => "Rotated 180°", 6 => "Rotated 90° CW", 8 => "Rotated 90° CCW",
        2 => "Mirrored", _ => v.ToString(CultureInfo.InvariantCulture),
    };
}
