using System.Globalization;
using System.IO.Compression;
using System.Text;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.Validation;
using BenchmarkDotNet.Attributes;

namespace AdCodicem.Pdf.Benchmarks;

/// <summary>
/// Measures the walk of the object streams' dependencies, which validation runs once, on Flate object streams that take
/// their <c>/DecodeParms</c> from one object written directly in the file, from an object the next stream holds, from
/// streams that each head a nest of streams by <c>/Length</c>, from the first of chains of objects other object streams
/// hold, or from the object of a stream whose standing waits on the next, and on the streams its parameters' values lie in.
/// </summary>
/// <remarks>
/// The walk reads what decoding each stream reads, where the chain's index places it, outside the reader's cache (#128,
/// #277): the streams are compressed, so that decoding reads their parameters — of a stream with no filter, it reads none.
/// An object many streams lead through is read once a walk, not once a stream, while the walk keeps no more objects than
/// the reader's cache holds (#201). Streams that each need the next, the last the first, are all circular: the streams
/// that lead to one another are worked out once, in time linear in the streams, where a search from each stream's need
/// took time in the square of their number (#168). Streams whose 64 parameters, and the values their predictor asks for,
/// are streams each heading a nest of 62 streams by <c>/Length</c> lead the walk through 16,128 streams, more than it keeps
/// objects: the nest of each is taken at once once a parse went through it, where each stream parsed them all again (#201).
/// An object another object stream holds is followed where that stream decodes alike in every read order, which is worked
/// out once a validation for each such stream, and a chain through such objects is then taken at once, as one written in
/// the file is (#312). A stream whose standing needs those of streams not known yet waits for them, and is walked again
/// once they are, from what the walk kept of its dictionary: streams that each wait on the next, and on 192 holders their
/// parameters' values lie in, are pushed on the stack of waiting standings again by each walk that meets them, leaving
/// stale entries the stack is rid of whenever it holds more than 1,024 and twice as many as it kept the last time (#312).
/// The document is opened outside the measurement.
/// </remarks>
[MemoryDiagnoser]
public class ObjectStreamDependencyBenchmarks
{
    /// <summary>How many Flate steps the nests' streams take, each its parameters a stream heading a nest.</summary>
    private const int Steps = 64;

    /// <summary>How many streams each nest holds below the stream heading it.</summary>
    private const int Depth = 62;

    private PdfDocument? _document;

    [Params(100, 1000)]
    public int StreamCount { get; set; }

    /// <summary>
    /// Gets or sets what each stream's <c>/DecodeParms</c> names: <c>shared</c>, one dictionary of 16 KB written in the
    /// file; <c>ring</c>, the object the next stream holds, the last naming the first's; <c>nests</c>, 64 streams whose
    /// predictor asks for their colors, bits and columns, each a stream as well, each of the 256 heading a nest of 62
    /// streams, each taking its <c>/Length</c> from the next; <c>holders</c>, 64 objects, each the first of a chain of 8
    /// objects held by object streams of their own, which need nothing; <c>waits</c>, for one stream, the object the first
    /// of the others holds, each of the others taking its <c>/Length</c> from the object the next holds, and as parameters
    /// 64 predictors whose values are the objects of 192 holders.
    /// </summary>
    [Params("shared", "ring", "nests", "holders", "waits")]
    public string Parameters { get; set; } = "shared";

    [GlobalSetup]
    public void Setup() => _document = PdfDocument.Open(Parameters switch
    {
        "nests" => CreateNests(StreamCount),
        "holders" => CreateHolders(StreamCount),
        "waits" => CreateWaits(StreamCount),
        _ => Create(StreamCount, sharedBytes: 16 * 1024, ring: Parameters == "ring"),
    });

    [GlobalCleanup]
    public void Cleanup() => _document?.Dispose();

    [Benchmark(Description = "Walk the object streams' dependencies")]
    public int Walk() => ObjectStreamDependencies.Run(_document!).Circular.Total;

    /// <summary>
    /// Writes a file indexed by a cross-reference stream, whose <paramref name="streams"/> Flate object streams each hold one
    /// dictionary and take their <c>/DecodeParms</c> from object 3, a dictionary of about <paramref name="sharedBytes"/>, or,
    /// in a <paramref name="ring"/>, from the dictionary the next stream holds.
    /// </summary>
    private static byte[] Create(int streams, int sharedBytes, bool ring)
    {
        using var output = new MemoryStream();
        var offsets = new Dictionary<int, long>();
        void Write(string text) => output.Write(Encoding.ASCII.GetBytes(text));

        void WriteObject(int number, string body)
        {
            offsets[number] = output.Position;
            Write(string.Create(CultureInfo.InvariantCulture, $"{number} 0 obj\n{body}\nendobj\n"));
        }

        Write("%PDF-1.5\n");
        WriteObject(1, "<< /Type /Catalog /Pages 2 0 R >>");
        WriteObject(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        WriteObject(3, "<< /Pad [" + new StringBuilder().Insert(0, "0 ", sharedBytes / 2) + "] >>");

        const int First = 10;
        for (var stream = 0; stream < streams; stream++)
        {
            var number = First + (2 * stream);
            var data = Compress(string.Create(CultureInfo.InvariantCulture, $"{number + 1} 0 << >>"));
            var header = string.Create(CultureInfo.InvariantCulture, $"{number + 1} 0 ").Length;
            var parameters = ring ? First + (2 * ((stream + 1) % streams)) + 1 : 3;
            offsets[number] = output.Position;
            Write(string.Create(CultureInfo.InvariantCulture, $"{number} 0 obj\n<< /Type /ObjStm /N 1 /First {header} /Filter /FlateDecode /DecodeParms {parameters} 0 R /Length {data.Length} >>\nstream\n"));
            output.Write(data);
            Write("\nendstream\nendobj\n");
        }

        Finish(output, offsets, First, First + (2 * streams));
        return output.ToArray();
    }

    /// <summary>
    /// Writes a file indexed by a cross-reference stream, whose <paramref name="streams"/> object streams, numbered from
    /// 20,000, each hold one dictionary and take <c>/Filter 5 0 R</c>, 64 Flate steps, and <c>/DecodeParms 6 0 R</c>, 64
    /// streams of parameters with a predictor of 10 whose <c>/Colors</c>, <c>/BitsPerComponent</c> and <c>/Columns</c> are
    /// streams; each of these 256 streams heads a nest of 62 streams, numbered from 10, each taking its <c>/Length</c> from
    /// the next, the last from a direct 0. The object streams' data is written as it is: the walk reads none of it.
    /// </summary>
    private static byte[] CreateNests(int streams)
    {
        using var output = new MemoryStream();
        var offsets = new Dictionary<int, long>();
        var next = 10;
        void Write(string text) => output.Write(Encoding.ASCII.GetBytes(text));

        void WriteObject(int number, string body)
        {
            offsets[number] = output.Position;
            Write(string.Create(CultureInfo.InvariantCulture, $"{number} 0 obj\n{body}\nendobj\n"));
        }

        // Writes a stream whose dictionary adds `entries`, heading a nest of Depth streams, each taking its /Length from
        // the next; gives its number.
        int Nest(string entries)
        {
            var head = next;
            next += Depth + 1;

            for (var level = 0; level <= Depth; level++)
            {
                var length = level < Depth ? string.Create(CultureInfo.InvariantCulture, $"{head + level + 1} 0 R") : "0";
                WriteObject(head + level, $"<< {(level == 0 ? entries : string.Empty)}/Length {length} >>\nstream\n\nendstream");
            }

            return head;
        }

        Write("%PDF-1.5\n");
        WriteObject(1, "<< /Type /Catalog /Pages 2 0 R >>");
        WriteObject(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        WriteObject(5, "[" + string.Join(' ', Enumerable.Repeat("/FlateDecode", Steps)) + "]");
        var parameters = new StringBuilder("[");

        for (var step = 0; step < Steps; step++)
        {
            var (colors, bits, columns) = (Nest(string.Empty), Nest(string.Empty), Nest(string.Empty));
            var head = Nest(string.Create(CultureInfo.InvariantCulture, $"/Predictor 10 /Colors {colors} 0 R /BitsPerComponent {bits} 0 R /Columns {columns} 0 R "));
            parameters.Append(CultureInfo.InvariantCulture, $"{head} 0 R ");
        }

        WriteObject(6, parameters.Append(']').ToString());

        const int First = 20_000;
        for (var stream = 0; stream < streams; stream++)
        {
            var number = First + (2 * stream);
            var data = string.Create(CultureInfo.InvariantCulture, $"{number + 1} 0 << >>");
            var header = string.Create(CultureInfo.InvariantCulture, $"{number + 1} 0 ").Length;
            WriteObject(number, string.Create(CultureInfo.InvariantCulture, $"<< /Type /ObjStm /N 1 /First {header} /Filter 5 0 R /DecodeParms 6 0 R /Length {data.Length} >>\nstream\n{data}\nendstream"));
        }

        Finish(output, offsets, First, First + (2 * streams));
        return output.ToArray();
    }

    /// <summary>
    /// Writes a file indexed by a cross-reference stream, whose object streams, numbered from 10, each hold one object: 512
    /// of them hold the objects of 64 chains of 8, each naming the next, the last an empty dictionary; the
    /// <paramref name="streams"/> after them each hold one dictionary and take <c>/Filter 5 0 R</c>, 64 Flate steps, and
    /// <c>/DecodeParms 6 0 R</c>, the first object of each chain. The object streams' data is written as it is.
    /// </summary>
    private static byte[] CreateHolders(int streams)
    {
        const int Links = 8;
        const int First = 10;
        using var output = new MemoryStream();
        var offsets = new Dictionary<int, long>();
        void Write(string text) => output.Write(Encoding.ASCII.GetBytes(text));

        void WriteObject(int number, string body)
        {
            offsets[number] = output.Position;
            Write(string.Create(CultureInfo.InvariantCulture, $"{number} 0 obj\n{body}\nendobj\n"));
        }

        void WriteObjectStream(int number, string member, string entries)
        {
            var data = string.Create(CultureInfo.InvariantCulture, $"{number + 1} 0 {member}");
            var header = string.Create(CultureInfo.InvariantCulture, $"{number + 1} 0 ").Length;
            WriteObject(number, string.Create(CultureInfo.InvariantCulture, $"<< /Type /ObjStm /N 1 /First {header} {entries}/Length {data.Length} >>\nstream\n{data}\nendstream"));
        }

        Write("%PDF-1.5\n");
        WriteObject(1, "<< /Type /Catalog /Pages 2 0 R >>");
        WriteObject(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        WriteObject(5, "[" + string.Join(' ', Enumerable.Repeat("/FlateDecode", Steps)) + "]");
        var heads = new StringBuilder("[");

        for (var holder = 0; holder < Steps * Links; holder++)
        {
            var number = First + (2 * holder);

            if (holder % Links == 0)
            {
                heads.Append(CultureInfo.InvariantCulture, $"{number + 1} 0 R ");
            }

            WriteObjectStream(number, (holder % Links) + 1 < Links ? string.Create(CultureInfo.InvariantCulture, $"{number + 3} 0 R") : "<< >>", string.Empty);
        }

        WriteObject(6, heads.Append(']').ToString());
        var judged = First + (2 * Steps * Links);

        for (var stream = 0; stream < streams; stream++)
        {
            WriteObjectStream(judged + (2 * stream), "<< >>", "/Filter 5 0 R /DecodeParms 6 0 R ");
        }

        Finish(output, offsets, First, judged + (2 * streams));
        return output.ToArray();
    }

    /// <summary>
    /// Writes a file indexed by a cross-reference stream, whose object streams, numbered from 10, each hold one object: 10, a
    /// Flate stream, takes its <c>/DecodeParms</c> from the object X1 holds; 192 holders after it each hold <c>1</c> and need
    /// nothing; the <paramref name="streams"/> after them, X1, X2 and on, each take their <c>/Length</c> from the object the
    /// next holds, the last its own length, <c>/Filter 5 0 R</c>, 64 Flate steps, and <c>/DecodeParms 6 0 R</c>, 64
    /// predictors of 12 whose <c>/Colors</c>, <c>/BitsPerComponent</c> and <c>/Columns</c> are each the object of a holder.
    /// Each X's first walk meets the next X and the 192 holders before any is known, and pushes them all on the stack of
    /// waiting standings: each holder waits once for each X, its entries but the last left stale, so that the stack holds
    /// as many as 1,214 entries for 100 streams and 2,532 for 1,000, rid of the stale ones whenever it holds more than 1,024
    /// and twice as many as it kept the last time. The X's data is written as it is: the walk reads none of it.
    /// </summary>
    private static byte[] CreateWaits(int streams)
    {
        const int First = 10;
        const int Holders = 3 * Steps;
        using var output = new MemoryStream();
        var offsets = new Dictionary<int, long>();
        void Write(string text) => output.Write(Encoding.ASCII.GetBytes(text));

        void WriteObject(int number, string body)
        {
            offsets[number] = output.Position;
            Write(string.Create(CultureInfo.InvariantCulture, $"{number} 0 obj\n{body}\nendobj\n"));
        }

        void WriteCompressed(int number, string member, string entries)
        {
            var data = Compress(string.Create(CultureInfo.InvariantCulture, $"{number + 1} 0 {member}"));
            var header = string.Create(CultureInfo.InvariantCulture, $"{number + 1} 0 ").Length;
            offsets[number] = output.Position;
            Write(string.Create(CultureInfo.InvariantCulture, $"{number} 0 obj\n<< /Type /ObjStm /N 1 /First {header} /Filter /FlateDecode {entries}/Length {data.Length} >>\nstream\n"));
            output.Write(data);
            Write("\nendstream\nendobj\n");
        }

        Write("%PDF-1.5\n");
        WriteObject(1, "<< /Type /Catalog /Pages 2 0 R >>");
        WriteObject(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        WriteObject(5, "[" + string.Join(' ', Enumerable.Repeat("/FlateDecode", Steps)) + "]");
        var parameters = new StringBuilder("[");

        for (var step = 0; step < Steps; step++)
        {
            var holder = First + 2 + (6 * step);
            parameters.Append(CultureInfo.InvariantCulture, $"<< /Predictor 12 /Colors {holder + 1} 0 R /BitsPerComponent {holder + 3} 0 R /Columns {holder + 5} 0 R >> ");
        }

        WriteObject(6, parameters.Append(']').ToString());

        for (var holder = 0; holder < Holders; holder++)
        {
            WriteCompressed(First + 2 + (2 * holder), "1", string.Empty);
        }

        var x = First + 2 + (2 * Holders);
        WriteCompressed(First, "<< >>", string.Create(CultureInfo.InvariantCulture, $"/DecodeParms {x + 1} 0 R "));

        for (var stream = 0; stream < streams; stream++)
        {
            var number = x + (2 * stream);
            var data = string.Create(CultureInfo.InvariantCulture, $"{number + 1} 0 5");
            var header = string.Create(CultureInfo.InvariantCulture, $"{number + 1} 0 ").Length;
            var length = stream + 1 < streams ? string.Create(CultureInfo.InvariantCulture, $"{number + 3} 0 R") : data.Length.ToString(CultureInfo.InvariantCulture);
            WriteObject(number, string.Create(CultureInfo.InvariantCulture, $"<< /Type /ObjStm /N 1 /First {header} /Filter 5 0 R /DecodeParms 6 0 R /Length {length} >>\nstream\n{data}\nendstream"));
        }

        Finish(output, offsets, First, x + (2 * streams));
        return output.ToArray();
    }

    /// <summary>
    /// Ends the file in <paramref name="output"/> with cross-reference stream <paramref name="xref"/>, of the objects at
    /// <paramref name="offsets"/> and of the one each object stream numbered from <paramref name="first"/> holds, numbered
    /// after it.
    /// </summary>
    private static void Finish(MemoryStream output, Dictionary<int, long> offsets, int first, int xref)
    {
        var xrefOffset = output.Position;
        offsets[xref] = xrefOffset;
        var rows = new byte[(xref + 1) * 7];

        for (var number = 0; number <= xref; number++)
        {
            var row = rows.AsSpan(number * 7, 7);

            if (offsets.TryGetValue(number, out var offset))
            {
                row[0] = 1;
                row[1] = (byte)(offset >> 24);
                row[2] = (byte)(offset >> 16);
                row[3] = (byte)(offset >> 8);
                row[4] = (byte)offset;
            }
            else if (number > first && (number - first) % 2 == 1)
            {
                var stream = number - 1;
                row[0] = 2;
                row[1] = (byte)(stream >> 24);
                row[2] = (byte)(stream >> 16);
                row[3] = (byte)(stream >> 8);
                row[4] = (byte)stream;
            }
        }

        output.Write(Encoding.ASCII.GetBytes(string.Create(CultureInfo.InvariantCulture, $"{xref} 0 obj\n<< /Type /XRef /Size {xref + 1} /W [1 4 2] /Root 1 0 R /Length {rows.Length} >>\nstream\n")));
        output.Write(rows);
        output.Write(Encoding.ASCII.GetBytes(string.Create(CultureInfo.InvariantCulture, $"\nendstream\nendobj\nstartxref\n{xrefOffset}\n%%EOF\n")));
    }

    /// <summary>Gives <paramref name="text"/> Flate-compressed.</summary>
    private static byte[] Compress(string text)
    {
        using var compressed = new MemoryStream();

        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(Encoding.ASCII.GetBytes(text));
        }

        return compressed.ToArray();
    }
}
