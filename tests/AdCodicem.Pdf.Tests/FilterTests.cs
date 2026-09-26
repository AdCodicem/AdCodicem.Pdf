using System.Text;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.IO.Filters;
using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.Tests;

public class FilterTests
{
    private const string FlateText = "Hello, PDF filters!Hello, PDF filters!Hello, PDF filters!";

    private static readonly byte[] FlateZlib =
    [
        0x78, 0xDA, 0xF3, 0x48, 0xCD, 0xC9, 0xC9, 0xD7, 0x51, 0x08, 0x70, 0x71, 0x53, 0x48, 0xCB,
        0xCC, 0x29, 0x49, 0x2D, 0x2A, 0x56, 0xF4, 0x20, 0x4A, 0x08, 0x00, 0x23, 0xBC, 0x12, 0xFD,
    ];

    private static readonly byte[] FlateRawDeflate =
    [
        0xF3, 0x48, 0xCD, 0xC9, 0xC9, 0xD7, 0x51, 0x08, 0x70, 0x71, 0x53, 0x48, 0xCB, 0xCC, 0x29,
        0x49, 0x2D, 0x2A, 0x56, 0xF4, 0x20, 0x4A, 0x08, 0x00,
    ];

    [Fact]
    public void Decodes_a_flate_stream()
    {
        Text(Decode(FlateZlib, PdfName.FlateDecode)).Should().Be(FlateText);
    }

    [Fact]
    public void Decodes_a_flate_stream_that_lost_its_zlib_header()
    {
        var diagnostics = new PdfDiagnostics();

        Text(Decode(FlateRawDeflate, PdfName.FlateDecode, diagnostics: diagnostics)).Should().Be(FlateText);

        diagnostics.HasRepairs.Should().BeTrue();
    }

    [Fact]
    public void Keeps_what_it_could_decode_of_a_truncated_flate_stream()
    {
        var diagnostics = new PdfDiagnostics();
        var truncated = FlateZlib.AsSpan(0, 20).ToArray();

        var decoded = Text(Decode(truncated, PdfName.FlateDecode, diagnostics: diagnostics));

        decoded.Should().NotBeEmpty();
        FlateText.Should().StartWith(decoded);
    }

    [Fact]
    public void Decodes_a_flate_stream_that_legitimately_contains_nothing()
    {
        // A validly compressed empty stream — an empty content stream, an empty appearance — decodes to
        // zero bytes. Reading that as a failure would hand back the compressed bytes instead.
        var diagnostics = new PdfDiagnostics();
        byte[] compressedNothing = [0x78, 0xDA, 0x03, 0x00, 0x00, 0x00, 0x00, 0x01];

        Decode(compressedNothing, PdfName.FlateDecode, diagnostics: diagnostics).Length.Should().Be(0);

        diagnostics.Count.Should().Be(0);
    }

    [Fact]
    public void Leaves_data_alone_when_flate_cannot_decode_anything()
    {
        var diagnostics = new PdfDiagnostics();
        byte[] garbage = [0x01, 0x02, 0x03, 0x04, 0x05, 0x06];

        Decode(garbage, PdfName.FlateDecode, diagnostics: diagnostics).ToArray().Should().Equal(garbage);

        diagnostics.Contains(PdfDiagnosticCodes.FilterFailed).Should().BeTrue();
    }

    [Fact]
    public void Decodes_a_hexadecimal_stream()
    {
        Text(Decode("48656C6C6F>"u8.ToArray(), PdfName.ASCIIHexDecode)).Should().Be("Hello");
    }

    [Fact]
    public void Pads_an_odd_hexadecimal_stream()
    {
        Decode("4A5>"u8.ToArray(), PdfName.ASCIIHexDecode).ToArray().Should().Equal((byte)0x4A, 0x50);
    }

    [Fact]
    public void Decodes_an_ascii85_stream()
    {
        Text(Decode("87cURD]i,\"Ebo7~>"u8.ToArray(), PdfName.ASCII85Decode)).Should().Be("Hello World");
    }

    [Fact]
    public void Reads_the_z_shortcut_of_an_ascii85_stream()
    {
        Decode("z~>"u8.ToArray(), PdfName.ASCII85Decode).ToArray().Should().Equal((byte)0, 0, 0, 0);
    }

    [Fact]
    public void Decodes_a_run_length_stream()
    {
        // Two literal bytes, then five copies of 0x41, then the end marker.
        byte[] encoded = [0x01, 0x48, 0x49, 0xFC, 0x41, 0x80];

        Text(Decode(encoded, PdfName.RunLengthDecode)).Should().Be("HIAAAAA");
    }

    [Fact]
    public void Decodes_the_lzw_example_from_the_specification()
    {
        byte[] encoded = [0x80, 0x0B, 0x60, 0x50, 0x22, 0x0C, 0x0C, 0x85, 0x01];

        Text(Decode(encoded, PdfName.LZWDecode)).Should().Be("-----A---B");
    }

    /// <summary>Encoded data in each decoding filter, and all it decodes to.</summary>
    public static TheoryData<string, byte[], byte[]> Encoded => new()
    {
        { "FlateDecode", FlateZlib, Encoding.ASCII.GetBytes(FlateText) },
        { "LZWDecode", [0x80, 0x0B, 0x60, 0x50, 0x22, 0x0C, 0x0C, 0x85, 0x01], "-----A---B"u8.ToArray() },
        { "RunLengthDecode", [0x01, 0x48, 0x49, 0xFC, 0x41, 0x80], "HIAAAAA"u8.ToArray() },
        { "ASCII85Decode", "87cURD]i,\"Ebo7~>"u8.ToArray(), "Hello World"u8.ToArray() },
        { "ASCII85Decode", "zz~>"u8.ToArray(), new byte[8] },
        { "ASCIIHexDecode", "48656C6C6F>"u8.ToArray(), "Hello"u8.ToArray() },
        { "ASCIIHexDecode", "4A5>"u8.ToArray(), [0x4A, 0x50] },
    };

    [Theory]
    [MemberData(nameof(Encoded))]
    public void Keeps_exactly_what_the_bound_allows_and_reports_only_a_bound_it_met(
        string filter, byte[] encoded, byte[] decoded)
    {
        // Every bound from one byte to past the whole output: the first bytes up to the bound are kept,
        // and the bound is reported — once, as the reader's limit — only when the data goes past it.
        var failures = new List<string>();

        for (var bound = 1; bound <= decoded.Length + 1; bound++)
        {
            var diagnostics = new PdfDiagnostics();
            var output = DecodeWithin(encoded, PdfName.Get(filter), bound, diagnostics).ToArray();
            var kept = decoded.AsSpan(0, Math.Min(bound, decoded.Length)).ToArray();
            var reports = bound < decoded.Length ? 1 : 0;

            if (!output.AsSpan().SequenceEqual(kept) || diagnostics.Count != reports ||
                (reports == 1 && !diagnostics.Contains(PdfDiagnosticCodes.FilterLimitExceeded)))
            {
                failures.Add($"bound {bound}: kept {output.Length} bytes, {diagnostics.Count} diagnostics");
            }
        }

        failures.Should().BeEmpty($"/{filter} must keep the first bytes up to its bound");
    }

    /// <summary>A megabyte or more of input in each expanding filter, each decoding to more than a kilobyte.</summary>
    public static TheoryData<string, byte[]> LargeInputs
    {
        get
        {
            var runs = new byte[(1 << 20) + 1];
            for (var i = 0; i + 1 < runs.Length; i += 2)
            {
                runs[i] = 0x81;
                runs[i + 1] = (byte)'A';
            }

            runs[^1] = 0x80;
            var stored = new byte[2 << 20];
            new Random(17).NextBytes(stored);
            using var compressed = new MemoryStream();
            using (var zlib = new System.IO.Compression.ZLibStream(compressed, System.IO.Compression.CompressionLevel.NoCompression, leaveOpen: true))
            {
                zlib.Write(stored);
            }

            return new()
            {
                { "RunLengthDecode", runs },
                { "LZWDecode", new byte[1 << 20] },
                { "ASCII85Decode", Encoding.ASCII.GetBytes(new string('z', 1 << 20)) },
                { "FlateDecode", compressed.ToArray() },
            };
        }
    }

    [Theory]
    [MemberData(nameof(LargeInputs))]
    public void A_filter_s_first_buffer_is_sized_by_its_bound_not_by_its_input(string filter, byte[] encoded)
    {
        // Each decoder guesses its output from its input, a length the file chose: two to four times
        // several megabytes here. The guess is capped by the bound, a kilobyte.
        var diagnostics = new PdfDiagnostics();
        var before = GC.GetAllocatedBytesForCurrentThread();

        var output = DecodeWithin(encoded, PdfName.Get(filter), 1024, diagnostics);

        (GC.GetAllocatedBytesForCurrentThread() - before).Should().BeLessThan(512 * 1024);
        output.Length.Should().Be(1024);
    }

    [Fact]
    public void Bounds_each_filter_of_a_chain_so_that_they_cannot_multiply()
    {
        // A Flate stream holding RunLength data: 2,000 pairs that each decode to 128 bytes. Flate stays
        // within the bound; RunLength, which would multiply it, stops at the bound too.
        var runs = new byte[4001];
        for (var pair = 0; pair < 2000; pair++)
        {
            runs[2 * pair] = 0x81;
            runs[(2 * pair) + 1] = (byte)'A';
        }

        runs[^1] = 0x80;
        using var compressed = new MemoryStream();
        using (var zlib = new System.IO.Compression.ZLibStream(compressed, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(runs);
        }

        var dictionary = new PdfDictionary();
        dictionary.Set(PdfName.Filter, new PdfArray([PdfName.FlateDecode, PdfName.RunLengthDecode]));
        var stream = new PdfStream(dictionary, PdfStreamData.FromMemory(compressed.ToArray()));
        var diagnostics = new PdfDiagnostics();

        var output = PdfFilterPipeline.Decode(stream, diagnostics, maxLength: 10_000);

        output.Length.Should().Be(10_000);
        diagnostics.Should().ContainSingle()
            .Which.Message.Should().StartWith("The /RunLengthDecode data decodes to more than the 10000 bytes");
    }

    [Fact]
    public void Tells_a_corrupt_flate_stream_from_one_that_reaches_the_bound()
    {
        var corrupt = new PdfDiagnostics();
        var bounded = new PdfDiagnostics();

        DecodeWithin([0x01, 0x02, 0x03, 0x04, 0x05, 0x06], PdfName.FlateDecode, 1000, corrupt);
        DecodeWithin(FlateZlib, PdfName.FlateDecode, 10, bounded);

        corrupt.Select(d => d.Code).Should().Equal(PdfDiagnosticCodes.FilterFailed);
        bounded.Select(d => d.Code).Should().Equal(PdfDiagnosticCodes.FilterLimitExceeded);
    }

    [Fact]
    public void Undoes_a_png_up_predictor()
    {
        byte[] predicted = [0x02, 0x0A, 0x14, 0x1E, 0x28, 0x02, 0x01, 0x02, 0x03, 0x04, 0x02, 0x01, 0x02, 0x03, 0x04];

        var result = PredictorTransform.Apply(predicted, predictor: 12, colors: 1, bitsPerComponent: 8, columns: 4);

        result.Should().Equal((byte)10, 20, 30, 40, 11, 22, 33, 44, 12, 24, 36, 48);
    }

    [Fact]
    public void Undoes_a_tiff_predictor()
    {
        byte[] predicted = [10, 5, 5, 5];

        var result = PredictorTransform.Apply(predicted, predictor: 2, colors: 1, bitsPerComponent: 8, columns: 4);

        result.Should().Equal((byte)10, 15, 20, 25);
    }

    [Fact]
    public void Applies_a_chain_of_filters_in_order()
    {
        // The data is flate-encoded first, then hex-encoded, so decoding runs hex then flate.
        var hex = Convert.ToHexString(FlateZlib) + ">";
        var dictionary = new PdfDictionary();
        dictionary.Set(PdfName.Filter, new PdfArray([PdfName.ASCIIHexDecode, PdfName.FlateDecode]));
        var stream = new PdfStream(dictionary, PdfStreamData.FromMemory(Encoding.ASCII.GetBytes(hex)));

        Text(stream.Decode()).Should().Be(FlateText);
    }

    [Fact]
    public void Leaves_an_image_filter_encoded()
    {
        var dictionary = new PdfDictionary();
        dictionary.Set(PdfName.Filter, PdfName.DCTDecode);
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0];
        var stream = new PdfStream(dictionary, PdfStreamData.FromMemory(jpeg));

        stream.HasImageFilter().Should().BeTrue();
        stream.Decode().ToArray().Should().Equal(jpeg);
    }

    [Fact]
    public void Reports_a_filter_it_does_not_know()
    {
        var diagnostics = new PdfDiagnostics();

        Decode([1, 2, 3], PdfName.Get("MadeUpDecode"), diagnostics: diagnostics);

        diagnostics.Contains(PdfDiagnosticCodes.FilterUnsupported).Should().BeTrue();
    }

    [Fact]
    public void Returns_raw_data_when_no_filter_is_declared()
    {
        var stream = new PdfStream(new PdfDictionary(), PdfStreamData.FromMemory("plain"u8.ToArray()));

        Text(stream.Decode()).Should().Be("plain");
    }

    private static ReadOnlyMemory<byte> Decode(
        byte[] data,
        PdfName filter,
        PdfDictionary? parameters = null,
        PdfDiagnostics? diagnostics = null)
    {
        var dictionary = new PdfDictionary();
        dictionary.Set(PdfName.Filter, filter);

        if (parameters is not null)
        {
            dictionary.Set(PdfName.DecodeParms, parameters);
        }

        return new PdfStream(dictionary, PdfStreamData.FromMemory(data)).Decode(diagnostics);
    }

    private static ReadOnlyMemory<byte> DecodeWithin(byte[] data, PdfName filter, int maxLength, PdfDiagnostics diagnostics)
    {
        var dictionary = new PdfDictionary();
        dictionary.Set(PdfName.Filter, filter);
        return PdfFilterPipeline.Decode(new PdfStream(dictionary, PdfStreamData.FromMemory(data)), diagnostics, maxLength);
    }

    private static string Text(ReadOnlyMemory<byte> data) => Encoding.ASCII.GetString(data.Span);
}
