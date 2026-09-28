using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.Validation;
using BenchmarkDotNet.Attributes;

namespace AdCodicem.Pdf.Benchmarks;

/// <summary>
/// Measures what validating a document costs on top of opening it, under the structural profile.
/// </summary>
/// <remarks>
/// The baseline M02's exit criterion asks for: as the profile gains rules slice by slice, this says what each
/// one costs. The first benchmark validates a document opened outside the measurement, whose objects the reader
/// has cached by then: the numbers are the rules' alone. The second opens the document and validates it, every
/// object read for the first time — what a caller validating a file it has not read pays, from slice 3 on, since
/// the object and page tree rules resolve what the trailer reaches. The third measures the walk the rules generated
/// from the Arlington model share alone — every object and value the trailer reaches typed and checked —, on the
/// cached document and its page tree walked beforehand.
/// </remarks>
[MemoryDiagnoser]
public class ValidationBenchmarks
{
    private readonly PdfValidator _validator = new();
    private PdfDocument? _document;
    private PageTreeWalk? _pages;
    private byte[] _bytes = [];

    [Params(10, 1000)]
    public int PageCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _bytes = SyntheticDocument.Create(PageCount, contentBytes: 4096);
        _document = PdfDocument.Open(_bytes);
        _pages = PageTreeWalk.Run(_document);
    }

    [GlobalCleanup]
    public void Cleanup() => _document?.Dispose();

    [Benchmark(Description = "Validate under the structural profile")]
    public int Validate() => _validator.Validate(_document!).Findings.Count;

    [Benchmark(Description = "Type and check the reachable objects against the Arlington model")]
    public int CheckShapes() => ArlingtonWalk.Run(_document!, _pages!).Checked;

    [Benchmark(Description = "Open and validate under the structural profile")]
    public int OpenAndValidate()
    {
        using var document = PdfDocument.Open(_bytes);
        return _validator.Validate(document).Findings.Count;
    }
}
