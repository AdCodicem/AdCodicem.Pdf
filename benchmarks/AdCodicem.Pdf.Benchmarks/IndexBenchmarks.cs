using System.Globalization;
using System.IO.Compression;
using System.Text;
using AdCodicem.Pdf.Documents;
using BenchmarkDotNet.Attributes;

namespace AdCodicem.Pdf.Benchmarks;

/// <summary>
/// Measures what indexing a document of 300,000 objects costs as it opens, by the shape of its index (#164): one classic
/// table, one cross-reference stream, an original and an update that rewrites a tenth of it, a hybrid file as Word writes
/// it, a chain of a thousand like sections, and one table whose objects are numbered three apart (#155).
/// </summary>
/// <remarks>
/// A section makes room in the index for the rows it holds before it reads them, rather than letting the index double as it
/// grows, and for no more than the newest section's <c>/Size</c> numbers once it is read: an older section's rows for
/// numbers a newer one gave are superseded, and add nothing. What opening allocates is the index, sized once for the first
/// four shapes. A chain of many like sections makes room section after section, never by less than the index holds, and
/// for the whole <c>/Size</c> once a doubling comes within a doubling of it. The index finds a number in a slot while
/// numbers run in order, and past them through a map hashed through the process's seed, which numbers three apart reach.
/// The objects are not read: opening reads the index and the catalog.
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

        /// <summary>An original's classic table, and an update's that rewrites 30,000 of its objects.</summary>
        Update,

        /// <summary>
        /// A hybrid file as Word writes it: a table of no rows naming a stream that indexes every object, and an older
        /// table that indexes them all again.
        /// </summary>
        Hybrid,

        /// <summary>A thousand classic sections, each adding 300 objects to those the older ones define.</summary>
        Chain,

        /// <summary>One classic table whose objects are numbered three apart, each row a subsection of its own.</summary>
        Spread,
    }

    [Params(IndexShape.Table, IndexShape.Stream, IndexShape.Update, IndexShape.Hybrid, IndexShape.Chain, IndexShape.Spread)]
    public IndexShape Shape { get; set; }

    [GlobalSetup]
    public void Setup() => _document = Shape switch
    {
        IndexShape.Stream => Streamed(),
        IndexShape.Update => Updated(),
        IndexShape.Hybrid => Hybrid(),
        IndexShape.Chain => Chained(1000),
        IndexShape.Spread => Spread(),
        _ => Chained(1),
    };

    [Benchmark(Description = "Open an index of 300,000 objects")]
    public int Open()
    {
        using var document = PdfDocument.Open(_document);
        return document.ObjectCount;
    }

    /// <summary>
    /// Writes a catalog, an empty page tree and nulls up to <see cref="Objects"/>, and records where each starts.
    /// </summary>
    private static long[] WriteObjects(MemoryStream buffer, int from, int to, long[]? offsets = null)
    {
        offsets ??= new long[Objects + 2];

        for (var number = Math.Max(from, 1); number < to; number++)
        {
            offsets[number] = buffer.Position;
            Write(buffer, number switch
            {
                1 => "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n",
                2 => "2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n",
                _ => string.Create(CultureInfo.InvariantCulture, $"{number} 0 obj\nnull\nendobj\n"),
            });
        }

        return offsets;
    }

    /// <summary>Writes a classic table's rows for objects <paramref name="first"/> to <paramref name="last"/>.</summary>
    private static void WriteRows(MemoryStream buffer, long[] offsets, int first, int last)
    {
        var rows = new StringBuilder();

        for (var number = first; number <= last; number++)
        {
            rows.Append(number == 0
                ? "0000000000 65535 f\r\n"
                : string.Create(CultureInfo.InvariantCulture, $"{offsets[number]:D10} 00000 n\r\n"));
        }

        Write(buffer, rows.ToString());
    }

    /// <summary>A chain of <paramref name="sections"/> classic sections, each written after the objects it adds.</summary>
    private static byte[] Chained(int sections)
    {
        using var buffer = new MemoryStream();
        var offsets = new long[Objects + 2];
        var perSection = Objects / sections;
        long previous = -1;
        Write(buffer, "%PDF-1.7\n");

        for (var section = 0; section < sections; section++)
        {
            var first = section * perSection;
            WriteObjects(buffer, first, first + perSection, offsets);
            var at = buffer.Position;
            Write(buffer, string.Create(CultureInfo.InvariantCulture, $"xref\n{first} {perSection}\n"));
            WriteRows(buffer, offsets, first, first + perSection - 1);
            var prev = previous < 0 ? string.Empty : string.Create(CultureInfo.InvariantCulture, $" /Prev {previous}");
            Write(buffer, string.Create(CultureInfo.InvariantCulture, $"trailer\n<< /Size {first + perSection} /Root 1 0 R{prev} >>\n"));
            previous = at;
        }

        Write(buffer, string.Create(CultureInfo.InvariantCulture, $"startxref\n{previous}\n%%EOF\n"));
        return buffer.ToArray();
    }

    /// <summary>The original's table, then an update rewriting objects 3 to 30,002, a subsection of one row each.</summary>
    private static byte[] Updated()
    {
        using var buffer = new MemoryStream();
        Write(buffer, "%PDF-1.7\n");
        var offsets = WriteObjects(buffer, 0, Objects);
        var original = buffer.Position;
        Write(buffer, string.Create(CultureInfo.InvariantCulture, $"xref\n0 {Objects}\n"));
        WriteRows(buffer, offsets, 0, Objects - 1);
        Write(buffer, string.Create(CultureInfo.InvariantCulture, $"trailer\n<< /Size {Objects} /Root 1 0 R >>\n"));

        var rows = new StringBuilder();

        for (var number = 3; number < 30_003; number++)
        {
            rows.Append(CultureInfo.InvariantCulture, $"{number} 1\n{buffer.Position:D10} 00000 n\r\n");
            Write(buffer, string.Create(CultureInfo.InvariantCulture, $"{number} 0 obj\n(updated)\nendobj\n"));
        }

        var update = buffer.Position;
        Write(buffer, "xref\n" + rows);
        Write(buffer, string.Create(
            CultureInfo.InvariantCulture,
            $"trailer\n<< /Size {Objects} /Root 1 0 R /Prev {original} >>\nstartxref\n{update}\n%%EOF\n"));
        return buffer.ToArray();
    }

    /// <summary>
    /// A catalog, an empty page tree and nulls numbered three apart, from 3 to three times <see cref="Objects"/>, each
    /// indexed by a subsection of one row.
    /// </summary>
    private static byte[] Spread()
    {
        using var buffer = new MemoryStream();
        Write(buffer, "%PDF-1.7\n");
        var offsets = WriteObjects(buffer, 0, 3);
        var rows = new StringBuilder("xref\n0 3\n0000000000 65535 f\r\n");
        rows.Append(CultureInfo.InvariantCulture, $"{offsets[1]:D10} 00000 n\r\n{offsets[2]:D10} 00000 n\r\n");

        for (var k = 1; k < Objects - 2; k++)
        {
            rows.Append(CultureInfo.InvariantCulture, $"{3 * k} 1\n{buffer.Position:D10} 00000 n\r\n");
            Write(buffer, string.Create(CultureInfo.InvariantCulture, $"{3 * k} 0 obj\nnull\nendobj\n"));
        }

        var at = buffer.Position;
        Write(buffer, rows.ToString());
        Write(buffer, string.Create(
            CultureInfo.InvariantCulture,
            $"trailer\n<< /Size {3 * Objects} /Root 1 0 R >>\nstartxref\n{at}\n%%EOF\n"));
        return buffer.ToArray();
    }

    /// <summary>One cross-reference stream, object <see cref="Objects"/>: rows of <c>/W [1 4 2]</c> under Flate.</summary>
    private static byte[] Streamed()
    {
        using var buffer = new MemoryStream();
        Write(buffer, "%PDF-1.7\n");
        var offsets = WriteObjects(buffer, 0, Objects);
        var at = buffer.Position;
        WriteStream(buffer, offsets, at);
        Write(buffer, string.Create(CultureInfo.InvariantCulture, $"startxref\n{at}\n%%EOF\n"));
        return buffer.ToArray();
    }

    /// <summary>
    /// A hybrid file as Word writes it: an older table of every row, then a stream of every row and a table of none that
    /// names it, whose trailer names the older table.
    /// </summary>
    private static byte[] Hybrid()
    {
        using var buffer = new MemoryStream();
        Write(buffer, "%PDF-1.7\n");
        var offsets = WriteObjects(buffer, 0, Objects);
        var stream = buffer.Position;
        WriteStream(buffer, offsets, stream);
        var original = buffer.Position;
        Write(buffer, string.Create(CultureInfo.InvariantCulture, $"xref\n0 {Objects + 1}\n"));
        WriteRows(buffer, offsets, 0, Objects);
        Write(buffer, string.Create(CultureInfo.InvariantCulture, $"trailer\n<< /Size {Objects + 1} /Root 1 0 R >>\n"));
        var newest = buffer.Position;
        Write(buffer, string.Create(
            CultureInfo.InvariantCulture,
            $"xref\n0 0\ntrailer\n<< /Size {Objects + 1} /Root 1 0 R /Prev {original} /XRefStm {stream} >>\nstartxref\n{newest}\n%%EOF\n"));
        return buffer.ToArray();
    }

    /// <summary>Writes the cross-reference stream, object <see cref="Objects"/>, at <paramref name="at"/>.</summary>
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
