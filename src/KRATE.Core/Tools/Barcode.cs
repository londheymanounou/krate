using System.Text;

namespace Krate.Core;

/// <summary>Code 128 (subset B) barcode, rendered as Unicode blocks. The encoding — start code,
/// per-character patterns, mod-103 checksum, stop — is hand-rolled; no barcode library. Verifying it
/// actually scans needs a reader, but the encoding is test-locked.</summary>
public static class Barcode
{
    // The 107 Code 128 bar/space width patterns (index = symbol value), then the 7-module stop.
    static readonly string[] Patterns =
    [
        "212222","222122","222221","121223","121322","131222","122213","122312","132212","221213",
        "221312","231212","112232","122132","122231","113222","123122","123221","223211","221132",
        "221231","213212","223112","312131","311222","321122","321221","312212","322112","322211",
        "212123","212321","232121","111323","131123","131321","112313","132113","132311","211313",
        "231113","231311","112133","112331","132131","113123","113321","133121","313121","211331",
        "231131","213113","213311","213131","311123","311321","331121","312113","312311","332111",
        "314111","221411","431111","111224","111422","121124","121421","141122","141221","112214",
        "112412","122114","122411","142112","142211","241211","221114","413111","241112","134111",
        "111242","121142","121241","114212","124112","124211","411212","421112","421211","212141",
        "214121","412121","111143","111341","131141","114113","114311","411113","411311","113141",
        "114131","311141","411131","211412","211214","211232","2331112",
    ];

    const int StartB = 104, Stop = 106;

    /// <summary>The mod-103 checksum symbol value for the text (Code 128 subset B).</summary>
    public static int Checksum(string text)
    {
        var sum = StartB;
        for (var i = 0; i < text.Length; i++) sum += (text[i] - 32) * (i + 1);
        return sum % 103;
    }

    /// <summary>The full symbol-value sequence: start, data, checksum, stop.</summary>
    public static int[] Symbols(string text)
    {
        var values = new List<int> { StartB };
        values.AddRange(text.Select(c => c - 32));
        values.Add(Checksum(text));
        values.Add(Stop);
        return [.. values];
    }

    /// <summary>Renders the barcode as Unicode block rows (with a quiet zone), scannable in a monospace font.</summary>
    public static string Code128(string text)
    {
        if (text.Length == 0) throw new ArgumentException(Strings.Get("Error_NeedText"));
        if (text.Any(c => c is < ' ' or > '~')) throw new ArgumentException(Strings.Get("Error_BarcodeAscii"));

        var modules = new StringBuilder();
        modules.Append(new string(' ', 10)); // quiet zone
        foreach (var symbol in Symbols(text))
        {
            var bar = true; // every pattern starts with a bar
            foreach (var width in Patterns[symbol])
            {
                modules.Append(new string(bar ? '█' : ' ', width - '0'));
                bar = !bar;
            }
        }
        modules.Append(new string(' ', 10));

        var line = modules.ToString();
        return string.Join('\n', Enumerable.Repeat(line, 4)); // give it height
    }
}
