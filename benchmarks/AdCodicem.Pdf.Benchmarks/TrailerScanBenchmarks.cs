using System.Text;
using AdCodicem.Pdf.Documents;
using BenchmarkDotNet.Attributes;

namespace AdCodicem.Pdf.Benchmarks;

/// <summary>
/// Measures the rebuild's scan for trailers on 12,000 <c>trailer</c> keywords that two long comment lines hold (#49, #171).
/// </summary>
/// <remarks>
/// <para>
/// The file holds a catalog and an empty page tree, then two comment lines of 6,000 <c>trailer %</c> each, each line
/// followed by the <see cref="Tail"/> on a line of its own. It has no <c>startxref</c>, so opening it rebuilds its index.
/// Each keyword lies in the comment its line opens: the scan sweeps the line once and takes none of them for a trailer
/// (#171), where it once parsed past each, reading a 64 KB window at each (#49). With no trailer, the rebuild finds the
/// catalog by searching for it.
/// </para>
/// <para>
/// The diagnostics keep 10 entries. The benchmark holds that comments hide the keywords at a cost linear in the file,
/// whatever value follows the lines.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class TrailerScanBenchmarks
{
    /// <summary>How many <c>trailer</c> keywords each of the two comment lines holds.</summary>
    private const int KeywordsALine = 6000;

    private static readonly PdfReaderOptions Options = PdfReaderOptions.Default with { DiagnosticCapacity = 10 };

    private byte[] _document = [];

    /// <summary>
    /// Gets or sets the value written after each comment line: a reference, <c>null</c> or a dictionary, which no keyword the
    /// scan takes comes before.
    /// </summary>
    [Params("5 0 R", "null", "<< /Size 3 >>")]
    public string Tail { get; set; } = "5 0 R";

    [GlobalSetup]
    public void Setup()
    {
        var text = new StringBuilder("%PDF-1.7\n");
        text.Append("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        text.Append("2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n");

        for (var line = 0; line < 2; line++)
        {
            text.Append('%');

            for (var keyword = 0; keyword < KeywordsALine; keyword++)
            {
                text.Append("trailer %");
            }

            text.Append('\n').Append(Tail).Append('\n');
        }

        _document = Encoding.ASCII.GetBytes(text.ToString());
    }

    [Benchmark(Description = "Rebuild an index past 12,000 trailer keywords")]
    public int Open()
    {
        using var document = PdfDocument.Open(_document, Options);
        return document.Diagnostics.Count;
    }
}
