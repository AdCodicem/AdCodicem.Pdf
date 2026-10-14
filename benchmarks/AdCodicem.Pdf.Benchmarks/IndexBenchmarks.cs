using System.Globalization;
using System.IO.Compression;
using System.Text;
using AdCodicem.Pdf.Documents;
using BenchmarkDotNet.Attributes;

namespace AdCodicem.Pdf.Benchmarks;

/// <summary>
/// Measures what indexing a document of many objects costs as it opens: one classic table, one cross-reference stream, or
/// a chain of a thousand classic sections that each add their own objects (#164).
/// </summary>
/// <remarks>
/// Each section makes room in the index for the rows it holds before it reads them, rather than letting the map double as
/// it grows: what opening allocates is the map, sized once, and the chain's sections add room by no less than the map
/// holds, so that a thousand of them resize it about as often as growth would. The objects are not read: opening reads
/// the index and the catalog.
/// </remarks>
[MemoryDiagnoser]
public class IndexBenchmarks
{
    private const int Objects = 300_000;

    private byte[] _document = [];

    /// <summary>The shapes of the index the benchmark opens.</summary>
    public enum IndexShape
    {
        /// <summary>One classic table.</summary>
        Table,

        /// <summary>One cross-reference stream, its rows compressed.</summary>
        Stream,

        /// <summary>A thousand classic sections, each adding 300 objects to those the older ones define.</summary>
        Chain,
    }

    [Params(IndexShape.Table, IndexShape.Stream, IndexShape.Chain)]
    public IndexShape Shape { get; set; }

    [GlobalSetup]
    public void Setup() => _document = Build(Shape);

    [Benchmark(Description = "Open an index of 300,000 objects")]
    public int Open()
    {
        using var document = PdfDocument.Open(_document);
        return document.ObjectCount;
    }

    /// <summary>
    /// Builds a catalog, an empty page tree and <see cref="Objects"/> objects in all, each <c>null</c> but those two,
    /// indexed in the <paramref name="shape"/> asked.
    /// </summary>
    private static byte[] Build(IndexShape shape)
    {
        using var buffer = new MemoryStream();
        var offsets = new long[Objects + 1];
        var sections = shape == IndexShape.Chain ? 1000 : 1;
        var perSection = Objects / sections;
        long previous = -1;

        Write(buffer, "%PDF-1.7\n");

        for (var section = 0; section < sections; section++)
        {
            var first = section * perSection;

            for (var number = Math.Max(first, 1); number < first + perSection; number++)
            {
                offsets[number] = buffer.Position;
                Write(buffer, number switch
                {
                    1 => "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n",
                    2 => "2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n",
                    _ => string.Create(CultureInfo.InvariantCulture, $"{number} 0 obj\nnull\nendobj\n"),
                });
            }

            var at = buffer.Position;

            if (shape == IndexShape.Stream)
            {
                WriteStream(buffer, offsets, at);
            }
            else
            {
                Write(buffer, string.Create(CultureInfo.InvariantCulture, $"xref\n{first} {perSection}\n"));

                for (var number = first; number < first + perSection; number++)
                {
                    Write(buffer, number == 0
                        ? "0000000000 65535 f\r\n"
                        : string.Create(CultureInfo.InvariantCulture, $"{offsets[number]:D10} 00000 n\r\n"));
                }

                var prev = previous < 0 ? string.Empty : string.Create(CultureInfo.InvariantCulture, $" /Prev {previous}");
                Write(buffer, string.Create(CultureInfo.InvariantCulture, $"trailer\n<< /Size {first + perSection} /Root 1 0 R{prev} >>\n"));
            }

            previous = at;
        }

        Write(buffer, string.Create(CultureInfo.InvariantCulture, $"startxref\n{previous}\n%%EOF\n"));
        return buffer.ToArray();
    }

    /// <summary>Writes the cross-reference stream, object <see cref="Objects"/>, at <paramref name="at"/>: rows of
    /// <c>/W [1 4 2]</c> under Flate.</summary>
    private static void WriteStream(MemoryStream buffer, long[] offsets, long at)
    {
        offsets[Objects] = at;
        var rows = new byte[(Objects + 1) * 7];

        for (var number = 1; number <= Objects; number++)
        {
            var row = rows.AsSpan(number * 7);
            var offset = (uint)offsets[number];
            row[0] = 1;
            row[1] = (byte)(offset >> 24);
            row[2] = (byte)(offset >> 16);
            row[3] = (byte)(offset >> 8);
            row[4] = (byte)offset;
        }

        using var compressed = new MemoryStream();

        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(rows);
        }

        Write(buffer, string.Create(
            CultureInfo.InvariantCulture,
            $"{Objects} 0 obj\n<< /Type /XRef /Size {Objects + 1} /W [1 4 2] /Root 1 0 R /Filter /FlateDecode /Length {compressed.Length} >>\nstream\n"));
        buffer.Write(compressed.GetBuffer(), 0, (int)compressed.Length);
        Write(buffer, "\nendstream\nendobj\n");
    }

    private static void Write(MemoryStream stream, string text) => stream.Write(Encoding.ASCII.GetBytes(text));
}
