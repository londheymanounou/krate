using System.Buffers.Binary;
using System.Globalization;
using Krate.Core;
using Xunit;

/// <summary>The header parser is fed hand-built byte arrays, so no real image files or
/// decoder are needed — the magic bytes and offsets are exactly what a real file carries.</summary>
public class ImageTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "krate-img-" + Guid.NewGuid().ToString("N")[..8]);

    public ImageTests()
    {
        Strings.Culture = CultureInfo.GetCultureInfo("en");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    string Write(string name, byte[] bytes)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    static byte[] Png(int w, int h)
    {
        var b = new byte[24];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(b, 0);
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(16), (uint)w);
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(20), (uint)h);
        return b;
    }

    static byte[] Gif(int w, int h)
    {
        var b = new byte[10];
        "GIF89a"u8.CopyTo(b);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(6), (ushort)w);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(8), (ushort)h);
        return b;
    }

    static byte[] Bmp(int w, int h)
    {
        var b = new byte[26];
        b[0] = (byte)'B'; b[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(18), w);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(22), -h); // top-down bitmaps store a negative height
        return b;
    }

    static byte[] Jpeg(int w, int h)
    {
        // SOI, an APP0 segment to skip over, then an SOF0 frame carrying the real dimensions.
        var b = new List<byte> { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x04, 0x11, 0x22, 0xFF, 0xC0, 0x00, 0x11, 0x08 };
        b.AddRange([(byte)(h >> 8), (byte)h, (byte)(w >> 8), (byte)w]);
        b.AddRange(new byte[6]);
        return b.ToArray();
    }

    [Fact]
    public void Reads_EveryFormat()
    {
        Assert.Equal(("PNG", 1920, 1080), Images.Read(Write("a.png", Png(1920, 1080))));
        Assert.Equal(("GIF", 640, 480), Images.Read(Write("a.gif", Gif(640, 480))));
        Assert.Equal(("BMP", 800, 600), Images.Read(Write("a.bmp", Bmp(800, 600))));
        Assert.Equal(("JPEG", 4032, 3024), Images.Read(Write("a.jpg", Jpeg(4032, 3024))));
    }

    [Fact]
    public void Dimensions_ReportsRatioAndOrientation()
    {
        var result = Images.Dimensions(Write("hd.png", Png(1920, 1080)));
        Assert.Contains("1920 × 1080 px", result);
        Assert.Contains("2.07 MP", result);
        Assert.Contains("16:9", result);
        Assert.Contains("landscape", result);

        Assert.Contains("portrait", Images.Dimensions(Write("p.png", Png(1080, 1920))));
        Assert.Contains("square", Images.Dimensions(Write("s.png", Png(512, 512))));
    }

    [Fact]
    public void UnknownData_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => Images.Read(Write("x.bin", new byte[40])));
        Assert.Throws<ArgumentException>(() => Images.Dimensions(Path.Combine(_dir, "missing.png")));
    }

    [Theory]
    [InlineData("1920x1080", "16:9  (16:9)")]
    [InlineData("1920:1200", "8:5  (16:10)")]
    [InlineData("1024x768", "4:3  (4:3)")]
    [InlineData("500x500", "1:1  (1:1)")]
    [InlineData("1234x567", "1234:567")]      // no common name
    public void Ratio_ReducesAndNames(string input, string expected) =>
        Assert.Equal(expected, Images.Ratio(input));

    [Fact]
    public void Ratio_FillsInAMissingDimension()
    {
        var result = Images.Ratio("16:9 1920");
        Assert.Contains("1920 × 1080 px", result);
        Assert.Contains("3413 × 1920 px", result);
    }

    [Fact]
    public void Ratio_RejectsNonsense() => Assert.Throws<ArgumentException>(() => Images.Ratio("wide"));
}
