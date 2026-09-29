using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.Objects;
using BenchmarkDotNet.Attributes;

namespace AdCodicem.Pdf.Benchmarks;

/// <summary>
/// Measures what checking a stream's declared length costs, inside the reader's first window and past it (#55).
/// </summary>
/// <remarks>
/// Every page of the synthetic document has a content stream, of 4 KB — inside the 8 KB window the reader parses an
/// object through — or of 16 KB — past it. A stream inside the window is checked against the <c>endstream</c> the
/// window holds; one past it is checked by asking the file for the few bytes after its declared length, which is the
/// cost a valid document pays. A declared length two bytes too long, as Acrobat Distiller 3 wrote them, is not
/// confirmed: the reader looks for the <c>endstream</c> in the window, or searches the file for it past the window,
/// which only a damaged document pays. The streams are parsed, not decoded. The document is opened from memory, where
/// asking the file is a copy, and from a file, where it is a read of its own — the way the library recommends
/// opening a large document.
/// </remarks>
[MemoryDiagnoser]
public class StreamLengthBenchmarks
{
    private byte[] _document = [];
    private string? _path;

    [Params(4096, 16384)]
    public int ContentBytes { get; set; }

    [Params(0, 2)]
    public int LengthError { get; set; }

    [Params(false, true)]
    public bool FromFile { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _document = SyntheticDocument.Create(pageCount: 1000, ContentBytes, LengthError);

        if (FromFile)
        {
            _path = Path.GetTempFileName();
            File.WriteAllBytes(_path, _document);
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (_path is not null)
        {
            File.Delete(_path);
            _path = null;
        }
    }

    [Benchmark(Description = "Index, then parse every page's content stream")]
    public long ParseEveryContentStream()
    {
        using var document = _path is null ? PdfDocument.Open(_document) : PdfDocument.Open(_path);
        var kids = document.Catalog.GetDictionary(PdfName.Pages).GetArray(PdfName.Kids);
        long total = 0;

        for (var i = 0; i < kids?.Count; i++)
        {
            if (kids.Resolved(i).AsDictionary()?.GetStream(PdfName.Contents) is { } contents)
            {
                total += contents.Data.Length;
            }
        }

        return total;
    }
}
