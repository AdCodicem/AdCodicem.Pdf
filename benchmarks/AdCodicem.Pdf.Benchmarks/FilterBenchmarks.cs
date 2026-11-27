using System.IO.Compression;
using System.Text;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.Objects;
using BenchmarkDotNet.Attributes;

namespace AdCodicem.Pdf.Benchmarks;

/// <summary>
/// Measures what decoding a Flate content stream costs, whole, cut short, and damaged; what a chain of two filters
/// costs, its parameters read before its data; what a predictor costs after Flate; and what decoding LZW costs, of text,
/// of incompressible bytes and of an image's flat rows (#192).
/// </summary>
/// <remarks>
/// A whole stream is what nearly every document holds, and noticing a lost tail or a fault must cost it nothing. A
/// stream that ran out is read a second time, without keeping what it decodes to, to tell a lost checksum from lost
/// data: that costs time on damaged streams only, and should cost no memory beyond the inflater's own. A stream
/// whose checksum is wrong, or that turns corrupt, is read again after the fault to keep what the reading that met
/// it lost: its body as raw deflate, then — for a corrupt one — once more, one byte at a time through the input the
/// fault was met in. Three readings at most, into the same output, on damaged streams only. The predictors are PNG's on
/// rows of five bytes, each taking the row above, as a cross-reference stream gives them; and TIFF's on rows of
/// 1,024 RGB pixels of eight bits, each sample taking the one before. The LZW streams hold a clear code each time the
/// table fills: the content stream's text, whose codes name entries of a few bytes; random bytes, which LZW expands and
/// whose codes name entries of two; and rows of 2,400 bytes of a page scanned in gray, white but for a few spans of other
/// values, whose codes name entries of up to some hundreds. A decode allocates its output and nothing for each code, the table rented.
/// </remarks>
[MemoryDiagnoser]
public class FilterBenchmarks
{
    /// <summary>The first byte of a last block of the type deflate reserves, and defines no data for: BFINAL 1, BTYPE 3.</summary>
    private const byte UndefinedBlock = 0x07;

    /// <summary>A cross-reference stream's row as producers write it: <c>/W [1 3 1]</c>.</summary>
    private const int CrossReferenceRow = 5;

    /// <summary>A TIFF-predicted row's length: 1,024 pixels of three eight-bit samples.</summary>
    private const int TiffRow = 3 * 1024;

    private PdfStream _whole = null!;
    private PdfStream _withoutChecksum = null!;
    private PdfStream _withoutTail = null!;
    private PdfStream _wrongChecksum = null!;
    private PdfStream _corrupt = null!;
    private PdfStream _chain = null!;
    private PdfStream _pngRows = null!;
    private PdfStream _tiffSamples = null!;
    private PdfStream _lzwText = null!;
    private PdfStream _lzwIncompressible = null!;
    private PdfStream _lzwImage = null!;

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

        // ReportLab's default, the chain the corpus holds most: ASCII85 over Flate, here with the parameters a predictor
        // would take, so that every value a chain's steps read is read before its data.
        var chain = new PdfDictionary();
        chain.Set(PdfName.Filter, new PdfArray([PdfName.ASCII85Decode, PdfName.FlateDecode]));
        var parameters = new PdfDictionary();
        parameters.Set(PdfName.Predictor, PdfInteger.Create(1));
        chain.Set(PdfName.DecodeParms, new PdfArray([PdfNull.Instance, parameters]));
        _chain = new PdfStream(chain, PdfStreamData.FromMemory(Ascii85(compressed)));

        _pngRows = PredictedStream(PngPredicted(DecodedLength / CrossReferenceRow), predictor: 12, colors: 1, columns: CrossReferenceRow);
        _tiffSamples = PredictedStream(TiffPredicted(DecodedLength / TiffRow), predictor: 2, colors: 3, columns: TiffRow / 3);

        _lzwText = LzwStream(LzwEncode(content));
        var incompressible = new byte[DecodedLength];
        new Random(42).NextBytes(incompressible);
        _lzwIncompressible = LzwStream(LzwEncode(incompressible));
        _lzwImage = LzwStream(LzwEncode(ScannedRows(DecodedLength)));
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

    [Benchmark(Description = "Decode a chain of ASCII85 and Flate")]
    public int Chain() => _chain.Decode(new PdfDiagnostics()).Length;

    [Benchmark(Description = "Decode Flate rows a PNG predictor took")]
    public int PngRows() => _pngRows.Decode(new PdfDiagnostics()).Length;

    [Benchmark(Description = "Decode Flate samples a TIFF predictor took")]
    public int TiffSamples() => _tiffSamples.Decode(new PdfDiagnostics()).Length;

    [Benchmark(Description = "Decode an LZW stream of text")]
    public int LzwText() => _lzwText.Decode(new PdfDiagnostics()).Length;

    [Benchmark(Description = "Decode an LZW stream of incompressible bytes")]
    public int LzwIncompressible() => _lzwIncompressible.Decode(new PdfDiagnostics()).Length;

    [Benchmark(Description = "Decode an LZW stream of a scanned page's rows")]
    public int LzwImage() => _lzwImage.Decode(new PdfDiagnostics()).Length;

    private static byte[] Ascii85(byte[] data)
    {
        var encoded = new List<byte>((data.Length * 5 / 4) + 8);
        Span<byte> digits = stackalloc byte[5];

        for (var start = 0; start < data.Length; start += 4)
        {
            var count = Math.Min(4, data.Length - start);
            var tuple = 0u;

            for (var i = 0; i < 4; i++)
            {
                tuple = (tuple << 8) | (i < count ? data[start + i] : 0u);
            }

            for (var i = 4; i >= 0; i--)
            {
                digits[i] = (byte)('!' + (tuple % 85));
                tuple /= 85;
            }

            encoded.AddRange(digits[..(count + 1)]);
        }

        encoded.AddRange("~>"u8);
        return [.. encoded];
    }

    /// <summary>
    /// Encodes <paramref name="data"/> as <c>LZWDecode</c> with <c>EarlyChange</c> 1, writing a clear code each time the
    /// table fills, and the end-of-data code last.
    /// </summary>
    private static byte[] LzwEncode(byte[] data)
    {
        var table = new Dictionary<int, int>();
        var encoded = new List<byte>(data.Length);
        var buffer = 0;
        var count = 0;
        var next = 258;
        var bits = 9;
        var current = -1;

        void Put(int code)
        {
            buffer = (buffer << bits) | code;
            count += bits;

            while (count >= 8)
            {
                encoded.Add((byte)(buffer >> (count - 8)));
                count -= 8;
                buffer &= (1 << count) - 1;
            }
        }

        Put(256);

        foreach (var value in data)
        {
            if (current < 0)
            {
                current = value;
                continue;
            }

            var key = (current << 8) | value;

            if (table.TryGetValue(key, out var code))
            {
                current = code;
                continue;
            }

            Put(current);
            table[key] = next++;

            if (next + 1 > 1 << bits && bits < 12)
            {
                bits++;
            }

            if (next == 4095)
            {
                Put(256);
                table.Clear();
                next = 258;
                bits = 9;
            }

            current = value;
        }

        if (current >= 0)
        {
            Put(current);

            // The decoder defines an entry on the last code too, which may widen the end-of-data code.
            if (++next + 1 > 1 << bits && bits < 12)
            {
                bits++;
            }
        }

        Put(257);

        if (count > 0)
        {
            encoded.Add((byte)(buffer << (8 - count)));
        }

        return [.. encoded];
    }

    private static PdfStream LzwStream(byte[] data)
    {
        var dictionary = new PdfDictionary();
        dictionary.Set(PdfName.Filter, PdfName.LZWDecode);
        return new PdfStream(dictionary, PdfStreamData.FromMemory(data));
    }

    private static PdfStream FlateStream(ReadOnlyMemory<byte> data)
    {
        var dictionary = new PdfDictionary();
        dictionary.Set(PdfName.Filter, PdfName.FlateDecode);
        return new PdfStream(dictionary, PdfStreamData.FromMemory(data));
    }

    private static PdfStream PredictedStream(byte[] predicted, int predictor, int colors, int columns)
    {
        var parameters = new PdfDictionary();
        parameters.Set(PdfName.Predictor, PdfInteger.Create(predictor));
        parameters.Set(PdfName.Colors, PdfInteger.Create(colors));
        parameters.Set(PdfName.Columns, PdfInteger.Create(columns));
        var dictionary = new PdfDictionary();
        dictionary.Set(PdfName.Filter, PdfName.FlateDecode);
        dictionary.Set(PdfName.DecodeParms, parameters);
        return new PdfStream(dictionary, PdfStreamData.FromMemory(Compress(predicted)));
    }

    /// <summary>
    /// Rows of a cross-reference stream — type 1, an offset 200 bytes past the last, generation 0 — each tagged with
    /// PNG's Up filter and written as its difference from the row above.
    /// </summary>
    private static byte[] PngPredicted(int rows)
    {
        var predicted = new byte[rows * (CrossReferenceRow + 1)];
        Span<byte> above = stackalloc byte[CrossReferenceRow];
        Span<byte> row = stackalloc byte[CrossReferenceRow];
        above.Clear();

        for (var index = 0; index < rows; index++)
        {
            var offset = 15 + (index * 200);
            row[0] = 1;
            row[1] = (byte)(offset >> 16);
            row[2] = (byte)(offset >> 8);
            row[3] = (byte)offset;
            row[4] = 0;

            var start = index * (CrossReferenceRow + 1);
            predicted[start] = 2;

            for (var i = 0; i < CrossReferenceRow; i++)
            {
                predicted[start + 1 + i] = (byte)(row[i] - above[i]);
            }

            row.CopyTo(above);
        }

        return predicted;
    }

    /// <summary>Rows of a smooth RGB gradient, each sample written as its difference from the sample of its color before it.</summary>
    private static byte[] TiffPredicted(int rows)
    {
        var predicted = new byte[rows * TiffRow];

        for (var index = 0; index < rows; index++)
        {
            var start = index * TiffRow;

            for (var i = 0; i < TiffRow; i++)
            {
                var sample = (byte)((index + (i / 3) + (i % 3 * 85)) & 0xFF);
                var before = i < 3 ? (byte)0 : (byte)((index + ((i - 3) / 3) + (i % 3 * 85)) & 0xFF);
                predicted[start + i] = (byte)(sample - before);
            }
        }

        return predicted;
    }

    /// <summary>
    /// Rows of 2,400 bytes, a page of 8-bit gray pixels scanned at 300 dpi: white, but for three spans of other values a row.
    /// </summary>
    private static byte[] ScannedRows(int length)
    {
        const int Row = 2400;
        var rows = new byte[length];
        rows.AsSpan().Fill(0xFF);
        var random = new Random(7);

        for (var row = 0; row < length / Row; row++)
        {
            for (var span = 0; span < 3; span++)
            {
                rows.AsSpan((row * Row) + random.Next(Row - 100), random.Next(1, 60)).Fill((byte)random.Next(256));
            }
        }

        return rows;
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
