using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Krate.Core;

/// <summary>PDF merge and split via PDFsharp (import mode — no font rendering, so no font setup needed).</summary>
public static class Pdf
{
    /// <summary>One PDF path per line → a single merged.pdf beside the first, in the given order.</summary>
    public static string Merge(string input)
    {
        var paths = input.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.Trim('"')).ToArray();
        if (paths.Length < 2) throw new ArgumentException(Strings.Get("Error_PdfMergeUsage"));
        foreach (var p in paths)
            if (!File.Exists(p)) throw new ArgumentException(Strings.Get("Error_NoFile", p));

        var outPath = Path.Combine(Path.GetDirectoryName(paths[0])!, "merged.pdf");
        if (File.Exists(outPath)) throw new ArgumentException(Strings.Get("Error_FileExists", outPath));

        using var output = new PdfDocument();
        var pages = 0;
        foreach (var path in paths)
        {
            using var doc = PdfReader.Open(path, PdfDocumentOpenMode.Import);
            for (var i = 0; i < doc.PageCount; i++) { output.AddPage(doc.Pages[i]); pages++; }
        }
        output.Save(outPath);
        return Strings.Get("Pdf_Merged", Path.GetFileName(outPath), pages, paths.Length);
    }

    /// <summary>Splits a PDF into one file per page ("&lt;name&gt;_p01.pdf", …) beside it.</summary>
    public static string Split(string input)
    {
        var path = input.Trim().Trim('"');
        if (!File.Exists(path)) throw new ArgumentException(Strings.Get("Error_NoFile", path));
        if (!path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException(Strings.Get("Error_NotPdf", Path.GetFileName(path)));

        using var doc = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        var stem = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path));
        // Refuse if any target already exists — never overwrite.
        for (var i = 1; i <= doc.PageCount; i++)
            if (File.Exists($"{stem}_p{i:00}.pdf")) throw new ArgumentException(Strings.Get("Error_FileExists", $"{Path.GetFileName(stem)}_p{i:00}.pdf"));

        for (var i = 0; i < doc.PageCount; i++)
        {
            using var single = new PdfDocument();
            single.AddPage(doc.Pages[i]);
            single.Save($"{stem}_p{i + 1:00}.pdf");
        }
        return Strings.Get("Pdf_Split", doc.PageCount, Path.GetFileName(stem));
    }
}
