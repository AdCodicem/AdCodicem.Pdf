using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.Objects;
using BenchmarkDotNet.Attributes;
using PdfSharp.Pdf.IO;
using PigDocument = UglyToad.PdfPig.PdfDocument;
using SharpReader = PdfSharp.Pdf.IO.PdfReader;

namespace AdCodicem.Pdf.Benchmarks.Comparison;

/// <summary>
/// Opens a document and decodes the content of every page: the work under any extraction, stamping or
/// rendering, and the measure of what a library holds while it walks a long document.
/// </summary>
/// <remarks>
/// AdCodicem.Pdf, PDFsharp and iText return each page's decoded content streams, and nothing more. PdfPig
/// has no call that stops there: its pages come parsed into operations and letters, which is more work, and
/// its row is reported beside the others for what it is rather than as a like-for-like figure.
/// </remarks>
[MemoryDiagnoser]
public class ContentBenchmarks
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
        var root = document.Catalog.GetDictionary(PdfName.Pages);
        return root is null ? 0 : DecodePages(root, [], depth: 0);
    }

    [Benchmark(Description = "PdfPig (parses operations and letters)")]
    public long PdfPig()
    {
        using var document = PigDocument.Open(_bytes);
        long total = 0;

        for (var number = 1; number <= document.NumberOfPages; number++)
        {
            total += document.GetPage(number).Operations.Count;
        }

        return total;
    }

    [Benchmark(Description = "PDFsharp")]
    public long PdfSharp()
    {
        using var stream = new MemoryStream(_bytes, writable: false);
        using var document = SharpReader.Open(stream, PdfDocumentOpenMode.Import);
        long total = 0;

        foreach (var page in document.Pages)
        {
            for (var index = 0; index < page.Contents.Elements.Count; index++)
            {
                total += page.Contents.Elements.GetDictionary(index)?.Stream?.UnfilteredValue.Length ?? 0;
            }
        }

        return total;
    }

    [Benchmark(Description = "iText")]
    public long IText()
    {
        using var stream = new MemoryStream(_bytes, writable: false);
        using var document = new iText.Kernel.Pdf.PdfDocument(new iText.Kernel.Pdf.PdfReader(stream));
        long total = 0;

        for (var number = 1; number <= document.GetNumberOfPages(); number++)
        {
            total += document.GetPage(number).GetContentBytes().Length;
        }

        return total;
    }

    /// <summary>
    /// Walks the page tree and decodes each page's contents. There is no page API before M06, so the walk is
    /// written here, bounded in depth and guarded against cycles as the reader's own tests are.
    /// </summary>
    private static long DecodePages(PdfDictionary node, HashSet<PdfDictionary> visited, int depth)
    {
        if (depth > 64 || !visited.Add(node))
        {
            return 0;
        }

        if (node.GetArray(PdfName.Kids) is not { } kids)
        {
            return DecodeContents(node);
        }

        long total = 0;

        for (var index = 0; index < kids.Count; index++)
        {
            if (kids.Resolved(index).AsDictionary() is { } kid)
            {
                total += DecodePages(kid, visited, depth + 1);
            }
        }

        return total;
    }

    private static long DecodeContents(PdfDictionary page)
    {
        if (page.GetArray(PdfName.Contents) is { } parts)
        {
            long total = 0;

            for (var index = 0; index < parts.Count; index++)
            {
                total += parts.Resolved(index).AsStream()?.Decode().Length ?? 0;
            }

            return total;
        }

        return page.GetStream(PdfName.Contents)?.Decode().Length ?? 0;
    }
}
