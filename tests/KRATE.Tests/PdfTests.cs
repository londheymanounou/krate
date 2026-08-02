using System.Globalization;
using Krate.Core;
using PdfSharp.Pdf;
using Xunit;

public class PdfToolTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "krate-pdf-" + Guid.NewGuid().ToString("N")[..8]);

    public PdfToolTests()
    {
        Strings.Culture = CultureInfo.GetCultureInfo("en");
        Directory.CreateDirectory(_dir);
    }
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    string MakePdf(string name, int pages)
    {
        var path = Path.Combine(_dir, name);
        using var doc = new PdfDocument();
        for (var i = 0; i < pages; i++) doc.AddPage();
        doc.Save(path);
        return path;
    }

    static int PageCount(string path)
    {
        using var doc = PdfSharp.Pdf.IO.PdfReader.Open(path, PdfSharp.Pdf.IO.PdfDocumentOpenMode.InformationOnly);
        return doc.PageCount;
    }

    [Fact]
    public void Merge_CombinesAllPagesInOrder()
    {
        var a = MakePdf("a.pdf", 2);
        var b = MakePdf("b.pdf", 3);
        var result = Pdf.Merge($"{a}\n{b}");
        Assert.Contains("5 pages", result);
        Assert.Equal(5, PageCount(Path.Combine(_dir, "merged.pdf")));
        Assert.Throws<ArgumentException>(() => Pdf.Merge(a)); // needs at least two
    }

    [Fact]
    public void Split_ProducesOneFilePerPage_AndRefusesNonPdf()
    {
        var src = MakePdf("doc.pdf", 3);
        Pdf.Split(src);
        Assert.True(File.Exists(Path.Combine(_dir, "doc_p01.pdf")));
        Assert.True(File.Exists(Path.Combine(_dir, "doc_p03.pdf")));
        Assert.Equal(1, PageCount(Path.Combine(_dir, "doc_p02.pdf")));

        File.WriteAllText(Path.Combine(_dir, "x.txt"), "");
        Assert.Throws<ArgumentException>(() => Pdf.Split(Path.Combine(_dir, "x.txt")));
    }
}
