using System.Globalization;
using System.Text;
using AdCodicem.Pdf.Documents;
using BenchmarkDotNet.Attributes;

namespace AdCodicem.Pdf.Benchmarks;

/// <summary>
/// Measures what opening a chain of 1,024 sections costs — as many as the reader reads by default —, read from a file, by
/// the shape of the 1,023 revisions past the original: each a cross-reference stream whose <c>/Prev</c> names the one
/// before, or a hybrid file's table whose <c>/XRefStm</c> names its revision's stream (#330, #332).
/// </summary>
/// <remarks>
/// Once the chain has reached a stream, it tells the section each link names by where the object number of the header there
/// ends, as well as by where it starts: the probe of the white space at the offset a link names, which the chain reads for
/// every link, answers it from the bytes it read, so that a link costs no read more than its own section's. Read from a file,
/// each read is a call to the system: what opening reads shows there, where reading from memory hides it.
/// </remarks>
[MemoryDiagnoser]
public class ChainOpenBenchmarks
{
    private const int Revisions = 1023;

    private string _path = string.Empty;

    /// <summary>The shapes of the revisions the benchmark opens.</summary>
    public enum ChainShape
    {
        /// <summary>Each revision a cross-reference stream whose <c>/Prev</c> names the one before.</summary>
        Stream,

        /// <summary>Each revision a table of no rows whose <c>/XRefStm</c> names its stream and <c>/Prev</c> the table before.</summary>
        Hybrid,
    }

    [Params(ChainShape.Stream, ChainShape.Hybrid)]
    public ChainShape Shape { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _path = Path.GetTempFileName();
        File.WriteAllBytes(_path, Chain(Shape == ChainShape.Hybrid));
    }

    [GlobalCleanup]
    public void Cleanup() => File.Delete(_path);

    [Benchmark(Description = "Open a chain of 1,024 sections from a file")]
    public int Open()
    {
        using var document = PdfDocument.Open(_path);
        return document.ObjectCount;
    }

    /// <summary>
    /// Writes the catalog and an empty page tree under a classic table, then <see cref="Revisions"/> revisions, each adding a
    /// null object and a cross-reference stream that indexes it and itself: the stream's <c>/Prev</c> names the section
    /// before, or, in a hybrid file, a table of no rows names the stream through <c>/XRefStm</c> and the table before through
    /// <c>/Prev</c>.
    /// </summary>
    private static byte[] Chain(bool hybrid)
    {
        var text = new StringBuilder("%PDF-1.5\n");
        var catalog = text.Length;
        text.Append("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        var pages = text.Length;
        text.Append("2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n");
        long previous = text.Length;
        text.Append(CultureInfo.InvariantCulture, $"xref\n0 3\n0000000000 65535 f \n{catalog:D10} 00000 n \n{pages:D10} 00000 n \ntrailer\n<< /Size 3 /Root 1 0 R >>\n");

        for (var revision = 0; revision < Revisions; revision++)
        {
            var number = 3 + (2 * revision);
            var item = text.Length;
            text.Append(CultureInfo.InvariantCulture, $"{number} 0 obj\nnull\nendobj\n");
            var stream = text.Length;
            var rows = Row(item) + Row(stream);
            var prev = hybrid ? string.Empty : string.Create(CultureInfo.InvariantCulture, $" /Prev {previous}");
            text.Append(CultureInfo.InvariantCulture, $"{number + 1} 0 obj\n<< /Type /XRef /Size {number + 2} /W [1 4 2] /Index [{number} 2] /Root 1 0 R{prev} /Length {rows.Length} >>\nstream\n{rows}\nendstream\nendobj\n");

            if (hybrid)
            {
                var table = text.Length;
                text.Append(CultureInfo.InvariantCulture, $"xref\n0 0\ntrailer\n<< /Size {number + 2} /Root 1 0 R /Prev {previous} /XRefStm {stream} >>\n");
                previous = table;
            }
            else
            {
                previous = stream;
            }
        }

        text.Append(CultureInfo.InvariantCulture, $"startxref\n{previous}\n%%EOF\n");
        return Encoding.Latin1.GetBytes(text.ToString());
    }

    /// <summary>Writes a cross-reference stream's row placing an object in use at <paramref name="offset"/>, one byte to a character.</summary>
    private static string Row(long offset) => new(
        [(char)1, (char)((offset >> 24) & 0xFF), (char)((offset >> 16) & 0xFF), (char)((offset >> 8) & 0xFF), (char)(offset & 0xFF), '\0', '\0']);
}
