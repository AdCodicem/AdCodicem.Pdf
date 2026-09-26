using System.IO.Compression;
using System.Text;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.Objects;
using BenchmarkDotNet.Attributes;

namespace AdCodicem.Pdf.Benchmarks;

/// <summary>
/// Measures what decoding a Flate content stream costs, whole and cut short.
/// </summary>
/// <remarks>
/// A whole stream is what nearly every document holds, and noticing a lost tail must cost it nothing. A stream
/// that ran out is read a second time, without keeping what it decodes to, to tell a lost checksum from lost
/// data: that costs time on damaged streams only, and should cost no memory beyond the inflater's own.
/// </remarks>
[MemoryDiagnoser]
public class FilterBenchmarks
{
    private PdfStream _whole = null!;
    private PdfStream _withoutChecksum = null!;
    private PdfStream _withoutTail = null!;

    [Params(64 * 1024, 4 * 1024 * 1024)]
    public int DecodedLength { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var compressed = Compress(ContentStream(DecodedLength));
        _whole = FlateStream(compressed);
        _withoutChecksum = FlateStream(compressed.AsMemory(0, compressed.Length - 4));
        _withoutTail = FlateStream(compressed.AsMemory(0, compressed.Length - 5));
    }

    [Benchmark(Baseline = true, Description = "Decode a whole Flate stream")]
    public int Whole() => _whole.Decode(new PdfDiagnostics()).Length;

    [Benchmark(Description = "Decode a Flate stream that lost its checksum")]
    public int WithoutChecksum() => _withoutChecksum.Decode(new PdfDiagnostics()).Length;

    [Benchmark(Description = "Decode a Flate stream that lost its tail")]
    public int WithoutTail() => _withoutTail.Decode(new PdfDiagnostics()).Length;

    private static PdfStream FlateStream(ReadOnlyMemory<byte> data)
    {
        var dictionary = new PdfDictionary();
        dictionary.Set(PdfName.Filter, PdfName.FlateDecode);
        return new PdfStream(dictionary, PdfStreamData.FromMemory(data));
    }

    private static byte[] ContentStream(int length)
    {
        var text = new StringBuilder(length + 64);

        for (var line = 0; text.Length < length; line++)
        {
            text.Append("BT /F1 12 Tf 72 ").Append(720 - (line % 700)).Append(" Td (Line ").Append(line).Append(" of a content stream) Tj ET\n");
        }

        return Encoding.ASCII.GetBytes(text.ToString(0, length));
    }

    private static byte[] Compress(byte[] data)
    {
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(data);
        }

        return compressed.ToArray();
    }
}
