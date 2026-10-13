using AdCodicem.Pdf.Documents;
using BenchmarkDotNet.Attributes;

namespace AdCodicem.Pdf.Benchmarks;

/// <summary>
/// Measures what opening a document from a stream the reader copies costs: a file stream, and a stream derived from
/// <see cref="MemoryStream"/>, as a pooled one is.
/// </summary>
/// <remarks>
/// Every stream but a <see cref="MemoryStream"/> itself is copied into memory as the document opens. What the copy
/// allocates, and what the document keeps of it, is what the allocation column shows.
/// </remarks>
[MemoryDiagnoser]
public class StreamOpenBenchmarks
{
    private byte[] _document = [];
    private string _path = string.Empty;

    [Params(1000)]
    public int PageCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _document = SyntheticDocument.Create(PageCount, contentBytes: 4096);
        _path = Path.GetTempFileName();
        File.WriteAllBytes(_path, _document);
    }

    [GlobalCleanup]
    public void Cleanup() => File.Delete(_path);

    [Benchmark(Description = "Open from a file stream")]
    public int OpenFromFileStream()
    {
        using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read);
        using var document = PdfDocument.Open(stream);
        return document.ObjectCount;
    }

    [Benchmark(Description = "Open from a derived memory stream")]
    public int OpenFromDerivedMemoryStream()
    {
        using var stream = new DerivedMemoryStream(_document);
        using var document = PdfDocument.Open(stream);
        return document.ObjectCount;
    }

    /// <summary>A memory stream of a type derived from it, as a pooled one is.</summary>
    private sealed class DerivedMemoryStream(byte[] buffer) : MemoryStream(buffer);
}
