using System.Globalization;
using System.IO.Compression;
using System.Text;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.Objects;
using BenchmarkDotNet.Attributes;

namespace AdCodicem.Pdf.Benchmarks;

/// <summary>
/// Measures what decoding a Flate content stream costs where a guard hides the end of its data: its <c>/Length</c> object
/// runs past <see cref="PdfReaderLimits.MaxObjectLength"/>, and a space and an end-of-line lie between the data and its
/// <c>endstream</c>, which only the hidden length says are the data's or not (#290).
/// </summary>
/// <remarks>
/// Clean zlib data is decoded once. Raw deflate, which carries no checksum, and zlib data whose checksum is short are
/// decoded as taken to tell whether their deflate body ends in them, then again to report what they meet: twice. Zlib data
/// whose checksum disagrees is decoded a third time, up to the run, to tell a checksum the run reaches into from one that
/// disagrees in the data itself; the others were decoded that third time too, for an answer nothing read. The document is
/// opened, and the stream parsed, outside the measurement.
/// </remarks>
[MemoryDiagnoser]
public class HiddenEndFlateBenchmarks
{
    private const int DecodedLength = 4 * 1024 * 1024;

    private PdfDocument? _document;
    private PdfStream _stream = null!;

    [Params("zlib", "raw deflate", "short checksum", "wrong checksum")]
    public string Data { get; set; } = "zlib";

    [GlobalSetup]
    public void Setup()
    {
        var content = new byte[DecodedLength];

        for (var i = 0; i < content.Length; i++)
        {
            content[i] = (byte)('a' + (i * 7 % 23));
        }

        var data = Data switch
        {
            "raw deflate" => Compress(content, raw: true),
            "short checksum" => Compress(content, raw: false)[..^2],
            "wrong checksum" => WrongChecksum(Compress(content, raw: false)),
            _ => Compress(content, raw: false),
        };

        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = 1024 } };
        _document = PdfDocument.Open(HiddenEnd(data), options);
        _stream = (PdfStream)_document.GetObject(new PdfObjectId(4))!;
        _ = _stream.GetRawBytes();
    }

    [GlobalCleanup]
    public void Cleanup() => _document?.Dispose();

    [Benchmark(Description = "Decode Flate data whose end a guard hides, a space and an end-of-line before its endstream")]
    public int Decode() => _stream.Decode(new PdfDiagnostics()).Length;

    private static byte[] Compress(byte[] content, bool raw)
    {
        using var output = new MemoryStream();

        using (Stream deflate = raw
            ? new DeflateStream(output, CompressionLevel.Fastest, leaveOpen: true)
            : new ZLibStream(output, CompressionLevel.Fastest, leaveOpen: true))
        {
            deflate.Write(content);
        }

        return output.ToArray();
    }

    private static byte[] WrongChecksum(byte[] zlib)
    {
        zlib[^1] ^= 0x55;
        return zlib;
    }

    /// <summary>
    /// Writes a file whose content stream 4 holds <paramref name="data"/> under Flate, its <c>/Length</c> object 20 padded
    /// past 1 KB, a space and an end-of-line between the data and its <c>endstream</c>, which object 5 after it proves its own.
    /// </summary>
    private static byte[] HiddenEnd(byte[] data)
    {
        var text = new StringBuilder("%PDF-1.7\n");
        var offsets = new SortedDictionary<int, int>();

        void Object(int number, string body)
        {
            offsets[number] = text.Length;
            text.Append(CultureInfo.InvariantCulture, $"{number} 0 obj\n{body}\nendobj\n");
        }

        Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        Object(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        Object(20, new string(' ', 2000) + data.Length.ToString(CultureInfo.InvariantCulture));
        Object(4, "<< /Length 20 0 R /Filter /FlateDecode >>\nstream\n" + Encoding.Latin1.GetString(data) + " \nendstream");
        Object(5, "(five)");

        var table = text.Length;
        text.Append("xref\n0 21\n0000000000 65535 f \n");

        for (var number = 1; number < 21; number++)
        {
            text.Append(offsets.TryGetValue(number, out var offset)
                ? string.Create(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n")
                : "0000000000 00000 f \n");
        }

        text.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size 21 /Root 1 0 R >>\nstartxref\n{table}\n%%EOF\n");
        return Encoding.Latin1.GetBytes(text.ToString());
    }
}
