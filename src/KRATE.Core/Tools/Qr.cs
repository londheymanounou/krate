using QRCoder;

namespace Krate.Core;

/// <summary>QR codes. QRCoder does the encoding (the one part that must be exactly right and is
/// not worth hand-rolling); we just choose how to render it.</summary>
public static class Qr
{
    /// <summary>QR rendered with Unicode half-blocks — two rows per character, so it stays roughly
    /// square in a monospace font and scans straight off the screen.</summary>
    public static string Unicode(string text)
    {
        if (string.IsNullOrEmpty(text)) throw new ArgumentException(Strings.Get("Error_NeedText"));
        var modules = Matrix(text);
        var sb = new System.Text.StringBuilder();
        // A quiet zone is part of the spec — without the border, many readers refuse the code.
        for (var y = 0; y < modules.Count; y += 2)
        {
            for (var x = 0; x < modules[y].Count; x++)
            {
                var top = modules[y][x];
                var bottom = y + 1 < modules.Count && modules[y + 1][x];
                // Dark module = black; we print on a light textbox, so dark→space, light→block.
                sb.Append((top, bottom) switch
                {
                    (false, false) => '█',
                    (false, true) => '▀',
                    (true, false) => '▄',
                    (true, true) => ' ',
                });
            }
            sb.Append('\n');
        }
        return sb.ToString().TrimEnd('\n');
    }

    /// <summary>A minimal PNG of the QR, for the GUI to show or the user to save.</summary>
    public static byte[] Png(string text, int pixelsPerModule = 10)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        return new PngByteQRCode(data).GetGraphic(pixelsPerModule);
    }

    static List<List<bool>> Matrix(string text)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        return data.ModuleMatrix.Select(row => row.Cast<bool>().ToList()).ToList();
    }
}
