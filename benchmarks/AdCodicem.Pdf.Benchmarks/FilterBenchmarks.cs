using System.IO.Compression;
using System.Text;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.Objects;
using BenchmarkDotNet.Attributes;

namespace AdCodicem.Pdf.Benchmarks;

/// <summary>
/// Measures what decoding a Flate content stream costs, whole, cut short, and damaged.
/// </summary>
/// <remarks>
/// A whole stream is what nearly every document holds, and noticing a lost tail or a fault must cost it nothing. A
/// stream that ran out is read a second time, without keeping what it decodes to, to tell a lost checksum from lost
/// data: that costs time on damaged streams only, and should cost no memory beyond the inflater's own. A stream
/// whose checksum is wrong, or that turns corrupt, is read again after the fault to keep what the reading that met
/// it lost: its body as raw deflate, then — for a corrupt one — once more, one byte at a time through the input the
/// fault was met in. Three readings at most, into the same output, on damaged streams only.
/// </remarks>
[MemoryDiagnoser]
public class FilterBenchmarks
{
    /// <summary>The first byte of a last block of the type deflate reserves, and defines no data for: BFINAL 1, BTYPE 3.</summary>
    private const byte UndefinedBlock = 0x07;

    private PdfStream _whole = null!;
    private PdfStream _withoutChecksum = null!;
    private PdfStream _withoutTail = null!;
    private PdfStream _wrongChecksum = null!;
    private PdfStream _corrupt = null!;

    [Params(64 * 1024, 4 * 1024 * 1024)]
    public int DecodedLength { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var content = ContentStream(DecodedLength);
        var compressed = Compress(content);
        _whole = FlateStream(compressed);
        _withoutChecksum = FlateStream(compressed.AsMemory(0, compressed.Length - 4));
        _withoutTail = FlateStream(compressed.AsMemory(0, compressed.Length - 5));

        byte[] wrongChecksum = [.. compressed];
        wrongChecksum[^1] ^= 0xFF;
        _wrongChecksum = FlateStream(wrongChecksum);
        _corrupt = FlateStream(CorruptHalfway(content));
    }

    [Benchmark(Baseline = true, Description = "Decode a whole Flate stream")]
    public int Whole() => _whole.Decode(new PdfDiagnostics()).Length;

    [Benchmark(Description = "Decode a Flate stream that lost its checksum")]
    public int WithoutChecksum() => _withoutChecksum.Decode(new PdfDiagnostics()).Length;

    [Benchmark(Description = "Decode a Flate stream that lost its tail")]
    public int WithoutTail() => _withoutTail.Decode(new PdfDiagnostics()).Length;

    [Benchmark(Description = "Decode a Flate stream whose checksum is wrong")]
    public int WrongChecksum() => _wrongChecksum.Decode(new PdfDiagnostics()).Length;

    [Benchmark(Description = "Decode a Flate stream that turns corrupt halfway")]
    public int Corrupt() => _corrupt.Decode(new PdfDiagnostics()).Length;

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

    /// <summary>The first half of <paramref name="data"/>, compressed and flushed to a byte, then a block of the type deflate reserves.</summary>
    private static byte[] CorruptHalfway(byte[] data)
    {
        using var compressed = new MemoryStream();
        using var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true);
        zlib.Write(data.AsSpan(0, data.Length / 2));
        zlib.Flush();
        return [.. compressed.ToArray(), UndefinedBlock];
    }
}
