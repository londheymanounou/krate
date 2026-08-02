using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Krate.Core;
using Xunit;

public class BulkRenameTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "krate-rename-" + Guid.NewGuid().ToString("N")[..8]);

    public BulkRenameTests()
    {
        Strings.Culture = CultureInfo.GetCultureInfo("en");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "IMG_001.jpg"), "");
        File.WriteAllText(Path.Combine(_dir, "IMG_002.jpg"), "");
        File.WriteAllText(Path.Combine(_dir, "other.txt"), "");
    }
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void RenamePlan_MatchesOnlyChangedNames()
    {
        var plan = Files.RenamePlan(_dir, "IMG", "photo");
        Assert.Equal(2, plan.Count);
        Assert.All(plan, p => Assert.Contains("photo", p.New));
        Assert.DoesNotContain(plan, p => p.Old.EndsWith("other.txt")); // no match, untouched
    }

    [Fact]
    public void BulkRename_PreviewsByDefault_RenamesOnlyWithApply()
    {
        // No "apply" → nothing renamed, just a preview.
        var preview = Files.BulkRename($"{_dir} | IMG | photo");
        Assert.Contains("IMG_001.jpg", preview);
        Assert.Contains("photo_001.jpg", preview);
        Assert.True(File.Exists(Path.Combine(_dir, "IMG_001.jpg")));   // still there

        // With "apply" → actually renamed.
        var done = Files.BulkRename($"{_dir} | IMG | photo | apply");
        Assert.Contains("2", done);
        Assert.False(File.Exists(Path.Combine(_dir, "IMG_001.jpg")));
        Assert.True(File.Exists(Path.Combine(_dir, "photo_001.jpg")));
    }
}

public class CurrencyTests
{
    public CurrencyTests() => Strings.Culture = CultureInfo.GetCultureInfo("en");

    [Fact]
    public void Compute_AppliesTheRate_AndRejectsUnknownCurrency()
    {
        var rates = new Dictionary<string, double> { ["EUR"] = 0.9, ["JPY"] = 150 };
        Assert.Equal(90, Currency.Compute(100, rates, "EUR"));
        Assert.Equal(1500, Currency.Compute(10, rates, "JPY"));
        Assert.Throws<ArgumentException>(() => Currency.Compute(100, rates, "XYZ"));
    }
}

public class ExifTests
{
    public ExifTests() => Strings.Culture = CultureInfo.GetCultureInfo("en");

    // Builds a minimal JPEG with an APP1/Exif block holding one ASCII (Make) and one SHORT (Orientation) tag.
    static byte[] MinimalExifJpeg(string make, ushort orientation)
    {
        var makeBytes = Encoding.ASCII.GetBytes(make + "\0");
        // TIFF: header(8) + IFD count(2) + 2 entries(24) + nextIFD(4) = 38, then Make string appended.
        var tiff = new List<byte>();
        tiff.AddRange("II"u8.ToArray());                       // little-endian
        tiff.AddRange(BitConverter.GetBytes((ushort)42));      // magic
        tiff.AddRange(BitConverter.GetBytes((uint)8));         // IFD0 at offset 8
        tiff.AddRange(BitConverter.GetBytes((ushort)2));       // 2 entries
        // Entry 1: Make (0x010F), ASCII(2), count, offset (string goes after the IFD at offset 38)
        tiff.AddRange(BitConverter.GetBytes((ushort)0x010F));
        tiff.AddRange(BitConverter.GetBytes((ushort)2));
        tiff.AddRange(BitConverter.GetBytes((uint)makeBytes.Length));
        tiff.AddRange(BitConverter.GetBytes((uint)38));
        // Entry 2: Orientation (0x0112), SHORT(3), count 1, inline value
        tiff.AddRange(BitConverter.GetBytes((ushort)0x0112));
        tiff.AddRange(BitConverter.GetBytes((ushort)3));
        tiff.AddRange(BitConverter.GetBytes((uint)1));
        tiff.AddRange(BitConverter.GetBytes((uint)orientation));
        tiff.AddRange(BitConverter.GetBytes((uint)0));         // next IFD = none
        tiff.AddRange(makeBytes);                              // Make string at offset 38

        var app1 = new List<byte> { 0xFF, 0xE1 };
        var payload = new List<byte>();
        payload.AddRange("Exif\0\0"u8.ToArray());
        payload.AddRange(tiff);
        var len = payload.Count + 2;
        app1.Add((byte)(len >> 8)); app1.Add((byte)(len & 0xFF));
        app1.AddRange(payload);

        var jpeg = new List<byte> { 0xFF, 0xD8 };
        jpeg.AddRange(app1);
        jpeg.AddRange([0xFF, 0xD9]); // EOI
        return [.. jpeg];
    }

    [Fact]
    public void Parse_ReadsAsciiAndShortTags()
    {
        var tags = Exif.Parse(MinimalExifJpeg("ACME Cameras", 6));
        Assert.Equal("ACME Cameras", tags["Make"]);
        Assert.Equal("Rotated 90° CW", tags["Orientation"]); // orientation 6
    }

    [Fact]
    public void Parse_ReturnsEmptyForNonJpegOrNoExif()
    {
        Assert.Empty(Exif.Parse(new byte[] { 1, 2, 3, 4 }));                  // not a JPEG
        Assert.Empty(Exif.Parse(new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 }));      // JPEG, no EXIF
    }
}
