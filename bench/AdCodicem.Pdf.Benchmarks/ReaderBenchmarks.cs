using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.Objects;
using BenchmarkDotNet.Attributes;

namespace AdCodicem.Pdf.Benchmarks;

/// <summary>
/// Measures what opening a document costs, and what walking every page costs on top.
/// </summary>
/// <remarks>
/// The point of these two numbers side by side is the library's central claim: opening must stay cheap
/// however heavy the content is, because nothing is read until it is asked for.
/// </remarks>
[MemoryDiagnoser]
public class ReaderBenchmarks
{
    private byte[] _document = [];

    [Params(10, 1000)]
    public int PageCount { get; set; }

    [GlobalSetup]
    public void Setup() => _document = SyntheticDocument.Create(PageCount, contentBytes: 4096);

    [Benchmark(Description = "Index the document")]
    public int Open()
    {
        using var document = PdfDocument.Open(_document);
        return document.ObjectCount;
    }

    [Benchmark(Description = "Index, then read every page")]
    public int OpenAndWalkPages()
    {
        using var document = PdfDocument.Open(_document);
        var kids = document.Catalog.GetDictionary(PdfName.Pages).GetArray(PdfName.Kids);
        var total = 0;

        for (var i = 0; i < kids?.Count; i++)
        {
            if (kids.Resolved(i).AsDictionary()?.GetStream(PdfName.Contents) is { } contents)
            {
                total += contents.Decode().Length;
            }
        }

        return total;
    }
}
