using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.Validation;
using BenchmarkDotNet.Attributes;

namespace AdCodicem.Pdf.Benchmarks;

/// <summary>
/// Measures what validating a document costs on top of opening it, under the structural profile.
/// </summary>
/// <remarks>
/// The baseline M2's exit criterion asks for: as the profile gains rules slice by slice, this says what each
/// one costs. The document is opened outside the measurement, so the numbers are validation's alone.
/// </remarks>
[MemoryDiagnoser]
public class ValidationBenchmarks
{
    private readonly PdfValidator _validator = new();
    private PdfDocument? _document;

    [Params(10, 1000)]
    public int PageCount { get; set; }

    [GlobalSetup]
    public void Setup() => _document = PdfDocument.Open(SyntheticDocument.Create(PageCount, contentBytes: 4096));

    [GlobalCleanup]
    public void Cleanup() => _document?.Dispose();

    [Benchmark(Description = "Validate under the structural profile")]
    public int Validate() => _validator.Validate(_document!).Findings.Count;
}
