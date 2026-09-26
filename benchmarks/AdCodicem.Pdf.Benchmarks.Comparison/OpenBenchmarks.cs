using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.Objects;
using BenchmarkDotNet.Attributes;
using PdfSharp.Pdf.IO;
using PigDocument = UglyToad.PdfPig.PdfDocument;
using SharpReader = PdfSharp.Pdf.IO.PdfReader;

namespace AdCodicem.Pdf.Benchmarks.Comparison;

/// <summary>
/// Opens a document and asks how many pages it has: what every library must do before anything else, and
/// where a reader that loads the whole file pays for it.
/// </summary>
/// <remarks>
/// Each library opens the same bytes from memory. AdCodicem.Pdf reads the page count from the page tree's
/// root, as a caller does today; the others through their own page-count API.
/// </remarks>
[MemoryDiagnoser]
public class OpenBenchmarks
{
    private byte[] _bytes = [];

    [ParamsSource(nameof(Documents))]
    public string Document { get; set; } = "";

    public static IEnumerable<string> Documents => ComparisonDocuments.Names;

    [GlobalSetup]
    public void Setup() => _bytes = ComparisonDocuments.Load(Document);

    [Benchmark(Baseline = true, Description = "AdCodicem.Pdf")]
    public long AdCodicem()
    {
        using var document = PdfDocument.Open(_bytes);
        return document.Catalog.GetDictionary(PdfName.Pages).GetInteger(PdfName.Count) ?? 0;
    }

    [Benchmark(Description = "PdfPig")]
    public int PdfPig()
    {
        using var document = PigDocument.Open(_bytes);
        return document.NumberOfPages;
    }

    [Benchmark(Description = "PDFsharp")]
    public int PdfSharp()
    {
        using var stream = new MemoryStream(_bytes, writable: false);
        using var document = SharpReader.Open(stream, PdfDocumentOpenMode.Import);
        return document.PageCount;
    }

    [Benchmark(Description = "iText")]
    public int IText()
    {
        using var stream = new MemoryStream(_bytes, writable: false);
        using var document = new iText.Kernel.Pdf.PdfDocument(new iText.Kernel.Pdf.PdfReader(stream));
        return document.GetNumberOfPages();
    }
}
