using System.Text;
using AdCodicem.Pdf.Documents;
using BenchmarkDotNet.Attributes;

namespace AdCodicem.Pdf.Benchmarks;

/// <summary>
/// Measures the rebuild's scan for trailers, which parses what follows each <c>trailer</c> keyword of a file it rebuilds
/// the index of, on 12,000 keywords whose values each lie past a long comment (#49, #171).
/// </summary>
/// <remarks>
/// <para>
/// The file holds a catalog and an empty page tree, then two comment lines of 6,000 <c>trailer %</c> each, each line
/// followed by the <see cref="Tail"/> on a line of its own. It has no <c>startxref</c>, so opening it rebuilds its index.
/// The scan reads a 64 KB window at each keyword (#49), and each keyword's value lies past the rest of its comment line, up
/// to 54 KB of it. The white space and comments after a keyword are skipped once, for the parse to start past them and for
/// a note to name where a reference starts: a parse that started at the keyword would lex them a second time, whatever the
/// value.
/// </para>
/// <para>
/// A reference after a keyword is noted and not followed (#171), and the diagnostics keep 10 entries: a note quotes the
/// keyword's offset and the reference's number only when it is kept, so the thousands dropped format nothing. A dictionary
/// is merged, its parse reading on past it for a <c>stream</c> keyword, over the next comment line where one follows.
/// <c>null</c> is neither noted nor merged. The benchmark holds the scan's constant factor, not its cost in the keywords,
/// which is #49's.
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
    /// Gets or sets the value written after each comment line, and so after each of its keywords: a reference, which the
    /// scan notes; <c>null</c>, which it passes over; or a dictionary, which it merges.
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
