using System.Globalization;
using System.Text;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.Validation;
using BenchmarkDotNet.Attributes;

namespace AdCodicem.Pdf.Benchmarks;

/// <summary>
/// Measures the probe of the cross-reference entries, which validation runs once: each entry's header read where it places
/// its object, as a load reads it (#175).
/// </summary>
/// <remarks>
/// The first benchmark probes a sound document, each header at its offset: a first read of 64 bytes decides each. The second
/// probes rows that place their objects in one run of 2 MB of null bytes — white space, as a block a fault zeroed leaves it —
/// past which no header lies: a load from any of them reads the run to its end, and the probe crosses it once, each row
/// after the first reading on from its end. The documents are opened, and their indexes settled, outside the measurement.
/// </remarks>
[MemoryDiagnoser]
public class CrossReferenceProbeBenchmarks
{
    private PdfDocument? _sound;
    private PdfDocument? _blanks;

    [Params(80, 800)]
    public int Rows { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _sound = PdfDocument.Open(SyntheticDocument.Create(Rows, contentBytes: 4096));
        _sound.Reader.SettleIndex();
        _blanks = PdfDocument.Open(RowsIntoBlanks(Rows, 2 * 1024 * 1024));
        _blanks.Reader.SettleIndex();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _sound?.Dispose();
        _blanks?.Dispose();
    }

    [Benchmark(Description = "Probe the entries of a sound document of as many pages as rows")]
    public int ProbeSound() => CrossReferenceProbe.Run(_sound!).Broken.Count;

    [Benchmark(Description = "Probe rows that lead into one run of 2 MB of blanks")]
    public int ProbeBlanks() => CrossReferenceProbe.Run(_blanks!).Broken.Count;

    /// <summary>
    /// Writes a file whose <paramref name="rows"/> rows place their objects in one run of <paramref name="blanks"/> null
    /// bytes, past which no header lies.
    /// </summary>
    private static byte[] RowsIntoBlanks(int rows, int blanks)
    {
        var text = new StringBuilder("%PDF-1.7\n");
        var offsets = new Dictionary<int, long>();

        offsets[1] = text.Length;
        text.Append("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        offsets[2] = text.Length;
        text.Append("2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n");

        var run = text.Length;
        text.Append('\0', blanks).Append("x\n");

        for (var number = 3; number < rows + 3; number++)
        {
            offsets[number] = run + ((long)number * (blanks / (rows + 3)));
        }

        var xref = text.Length;
        text.Append(CultureInfo.InvariantCulture, $"xref\n0 {rows + 3}\n0000000000 65535 f \n");

        for (var number = 1; number < rows + 3; number++)
        {
            text.Append(CultureInfo.InvariantCulture, $"{offsets[number]:D10} 00000 n \n");
        }

        text.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {rows + 3} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.Latin1.GetBytes(text.ToString());
    }
}
