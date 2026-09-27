namespace AdCodicem.Pdf.Benchmarks.Comparison;

/// <summary>
/// The corpus documents every library is measured on: real files from real producers, chosen to span a
/// one-page invoice to a thousand-page report, and read from memory so that no library pays for the disk.
/// </summary>
internal static class ComparisonDocuments
{
    /// <summary>A short name for each document, as the results tables show it, and its path under <c>tests/corpus</c>.</summary>
    private static readonly Dictionary<string, string> Paths = new(StringComparer.Ordinal)
    {
        ["invoice-1p"] = "documents/invoice/chromium-invoice-fr.pdf",                       // Chromium, object streams
        ["report-3p"] = "documents/report/libreoffice-report-fr.pdf",                       // LibreOffice, classic table
        ["form-10p"] = "vendor/fr-licence-ouverte/pdfmaker-acrobat-cerfa-12156-form.pdf",   // Acrobat, form, linearized
        ["tagged-21p"] = "vendor/pdf-association/indesign13-pdfua1-german-book-chapter.pdf", // InDesign, PDF/UA, 3 updates
        ["journal-1000p"] = "documents/stress/reportlab-journal-1000-pages.pdf",            // ReportLab, 1000 pages
    };

    public static IEnumerable<string> Names => Paths.Keys;

    public static byte[] Load(string name) => File.ReadAllBytes(Path.Combine(CorpusRoot(), Paths[name]));

    private static string CorpusRoot()
    {
        // BenchmarkDotNet runs each benchmark from a generated project under bin/, so the repository is found
        // by walking up to the solution file rather than from a fixed relative path.
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AdCodicem.Pdf.slnx")))
            {
                return Path.Combine(directory.FullName, "tests", "corpus");
            }
        }

        throw new DirectoryNotFoundException("The repository root (AdCodicem.Pdf.slnx) was not found above " + AppContext.BaseDirectory);
    }
}
