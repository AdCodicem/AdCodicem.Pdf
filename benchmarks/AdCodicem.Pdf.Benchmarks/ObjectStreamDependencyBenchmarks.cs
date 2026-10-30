using System.Globalization;
using System.IO.Compression;
using System.Text;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.Validation;
using BenchmarkDotNet.Attributes;

namespace AdCodicem.Pdf.Benchmarks;

/// <summary>
/// Measures the walk of the object streams' dependencies, which validation runs once, on Flate object streams that take
/// their <c>/DecodeParms</c> from one object written directly in the file.
/// </summary>
/// <remarks>
/// The walk reads what decoding each stream reads, where the chain's index places it, outside the reader's cache (#128,
/// #277): the streams are compressed, so that decoding reads their parameters — of a stream with no filter, it reads none.
/// An object many streams lead through is read once a walk, not once a stream, while the walk keeps no more objects than
/// the reader's cache holds (#201). The document is opened outside the measurement.
/// </remarks>
[MemoryDiagnoser]
public class ObjectStreamDependencyBenchmarks
{
    private PdfDocument? _document;

    [Params(100, 1000)]
    public int StreamCount { get; set; }

    [GlobalSetup]
    public void Setup() => _document = PdfDocument.Open(Create(StreamCount, sharedBytes: 16 * 1024));

    [GlobalCleanup]
    public void Cleanup() => _document?.Dispose();

    [Benchmark(Description = "Walk the object streams' dependencies")]
    public int Walk() => ObjectStreamDependencies.Run(_document!).Circular.Count;

    /// <summary>
    /// Writes a file indexed by a cross-reference stream, whose <paramref name="streams"/> Flate object streams each hold one
    /// dictionary and take their <c>/DecodeParms</c> from object 3, a dictionary of about <paramref name="sharedBytes"/>.
    /// </summary>
    private static byte[] Create(int streams, int sharedBytes)
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
            offsets[number] = output.Position;
            Write(string.Create(CultureInfo.InvariantCulture, $"{number} 0 obj\n<< /Type /ObjStm /N 1 /First {header} /Filter /FlateDecode /DecodeParms 3 0 R /Length {data.Length} >>\nstream\n"));
            output.Write(data);
            Write("\nendstream\nendobj\n");
        }

        var xref = First + (2 * streams);
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
            else if (number > First && (number - First) % 2 == 1)
            {
                var stream = number - 1;
                row[0] = 2;
                row[1] = (byte)(stream >> 24);
                row[2] = (byte)(stream >> 16);
                row[3] = (byte)(stream >> 8);
                row[4] = (byte)stream;
            }
        }

        Write(string.Create(CultureInfo.InvariantCulture, $"{xref} 0 obj\n<< /Type /XRef /Size {xref + 1} /W [1 4 2] /Root 1 0 R /Length {rows.Length} >>\nstream\n"));
        output.Write(rows);
        Write(string.Create(CultureInfo.InvariantCulture, $"\nendstream\nendobj\nstartxref\n{xrefOffset}\n%%EOF\n"));
        return output.ToArray();
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
