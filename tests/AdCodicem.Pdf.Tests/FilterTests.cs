using System.Text;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.IO;
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

    [Fact]
    public void Stops_an_lzw_stream_at_a_code_it_has_not_defined()
    {
        // "A", a clear, then code 300: after a clear the table holds only the 258 fixed codes, and there is no
        // previous sequence to extend, so the code means nothing. What came before it is kept; that the
        // stream was corrupt goes unsaid, as a Flate stream's lost tail does (T32).
        var encoded = NineBitCodes(256, 'A', 256, 300, 'B');

        Text(Decode(encoded, PdfName.LZWDecode)).Should().Be("A");
    }

    [Fact]
    public void Decodes_an_empty_predicted_stream_to_nothing_and_reports_nothing()
    {
        // Rows longer than the data are a fault of the parameters, except when there is no data at all.
        var parameters = new PdfDictionary();
        parameters.Set(PdfName.Predictor, PdfInteger.Create(12));
        parameters.Set(PdfName.Columns, PdfInteger.Create(4));
        var diagnostics = new PdfDiagnostics();

        var decoded = Decode(Compress([]), PdfName.FlateDecode, parameters, diagnostics);

        decoded.Length.Should().Be(0);
        diagnostics.Should().BeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Leaves_data_without_a_predictor_as_it_decodes(bool namesPredictorOne)
    {
        // Parameters that name no predictor, or predictor 1, change nothing, whatever else they say: these
        // bytes read as PNG rows would come out changed.
        var parameters = new PdfDictionary();
        if (namesPredictorOne)
        {
            parameters.Set(PdfName.Predictor, PdfInteger.Create(1));
        }

        parameters.Set(PdfName.Columns, PdfInteger.Create(4));
        byte[] data = [2, 1, 2, 3, 4, 2, 1, 2, 3, 4];

        Decode(Compress(data), PdfName.FlateDecode, parameters).ToArray().Should().Equal(data);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    [InlineData(-5)]
    public void The_predictor_transform_leaves_data_alone_without_a_predictor(int predictor)
    {
        byte[] data = [2, 1, 2, 3, 4];

        PredictorTransform.TryApply(data, predictor, colors: 1, bitsPerComponent: 8, columns: 4, out var result).Should().BeTrue();

        result.Should().BeSameAs(data);
    }

    [Theory]
    [InlineData("/Columns 99 0 R")]
    [InlineData("/Predictor 1 /Colors 99 0 R /BitsPerComponent 99 0 R")]
    public void Does_not_read_the_parameters_of_a_predictor_the_stream_does_not_have(string parameters)
    {
        // Without a predictor, /Columns and its kin mean nothing, and object 99 is not in the file: resolving
        // it would rebuild the whole index for a parameter nothing reads. The Flate data travels in hex, so
        // that the file stays text.
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .Stream(
                3,
                $"/Filter [/ASCIIHexDecode /FlateDecode] /DecodeParms [null << {parameters} >>]",
                Convert.ToHexString(Compress("Hello"u8.ToArray())) + ">")
            .BuildClassic(rootNumber: 1);

        using var document = PdfDocument.Open(file);
        var decoded = document.GetObject(new PdfObjectId(3)).AsStream().Required().Decode(document.Diagnostics);

        Text(decoded).Should().Be("Hello");
        document.WasRepaired.Should().BeFalse();
        document.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void Cuts_a_stream_decoded_with_nowhere_to_report_to_its_bound()
    {
        // A stream built in memory, decoded without diagnostics, has no document to report to: it is cut all
        // the same, and nothing is thrown for want of a report.
        var dictionary = new PdfDictionary();
        dictionary.Set(PdfName.Filter, PdfName.ASCIIHexDecode);
        var stream = new PdfStream(dictionary, PdfStreamData.FromMemory("48656C6C6F>"u8.ToArray()));

        var decoded = PdfFilterPipeline.Decode(stream, diagnostics: null, Within(3));

        decoded.ToArray().Should().Equal("Hel"u8.ToArray());
    }

    /// <summary>Encoded data in each decoding filter, and all it decodes to.</summary>
    public static TheoryData<string, byte[], byte[]> Encoded => new()
    {
        { "FlateDecode", FlateZlib, Encoding.ASCII.GetBytes(FlateText) },
        { "FlateDecode", FlateRawDeflate, Encoding.ASCII.GetBytes(FlateText) },
        { "FlateDecode", [0x0A, 0x20, .. FlateZlib], Encoding.ASCII.GetBytes(FlateText) },
        { "FlateDecode", [0x78, 0xDA, 0x03, 0x00, 0x00, 0x00, 0x00, 0x01], [] },
        { "LZWDecode", [0x80, 0x0B, 0x60, 0x50, 0x22, 0x0C, 0x0C, 0x85, 0x01], "-----A---B"u8.ToArray() },
        { "RunLengthDecode", [0x01, 0x48, 0x49, 0xFC, 0x41, 0x80], "HIAAAAA"u8.ToArray() },
        { "ASCII85Decode", "87cURD]i,\"Ebo7~>"u8.ToArray(), "Hello World"u8.ToArray() },
        { "ASCII85Decode", "zz~>"u8.ToArray(), new byte[8] },
        { "ASCIIHexDecode", "48656C6C6F>"u8.ToArray(), "Hello"u8.ToArray() },
        { "ASCIIHexDecode", "4A5>"u8.ToArray(), [0x4A, 0x50] },
        { "RunLengthDecode", [], [] },
        { "RunLengthDecode", [0x80], [] },
        { "LZWDecode", [], [] },
        { "LZWDecode", [0x80, 0x80], [] },
        { "ASCII85Decode", "~>"u8.ToArray(), [] },
        { "ASCIIHexDecode", ">"u8.ToArray(), [] },
    };

    [Theory]
    [MemberData(nameof(Encoded))]
    public void Keeps_exactly_what_the_bound_allows_and_reports_only_a_bound_it_met(
        string filter, byte[] encoded, byte[] decoded)
    {
        // Every bound from one byte to past the whole output: the first bytes up to the bound are kept,
        // and the bound is reported — once, as the reader's limit — only when the data goes past it,
        // beside whatever the data earns when nothing bounds it, such as a repaired zlib header.
        var failures = new List<string>();
        var unbounded = new PdfDiagnostics();
        DecodeWithin(encoded, PdfName.Get(filter), decoded.Length + 1, unbounded);
        var baseline = string.Join(";", unbounded.Select(d => d.Code));
        var atDefault = new PdfDiagnostics();
        var whole = Decode(encoded, PdfName.Get(filter), diagnostics: atDefault).ToArray();

        if (!whole.AsSpan().SequenceEqual(decoded) || string.Join(";", atDefault.Select(d => d.Code)) != baseline)
        {
            failures.Add($"at the default bound: {whole.Length} bytes, [{string.Join(";", atDefault.Select(d => d.Code))}]");
        }

        for (var bound = 1; bound <= decoded.Length + 1; bound++)
        {
            var diagnostics = new PdfDiagnostics();
            var output = DecodeWithin(encoded, PdfName.Get(filter), bound, diagnostics).ToArray();
            var kept = decoded.AsSpan(0, Math.Min(bound, decoded.Length)).ToArray();
            var expected = bound < decoded.Length
                ? string.Join(";", unbounded.Select(d => d.Code).Append(PdfDiagnosticCodes.LimitDecodedStream))
                : baseline;
            var codes = string.Join(";", diagnostics.Select(d => d.Code));

            if (!output.AsSpan().SequenceEqual(kept) || codes != expected)
            {
                failures.Add($"bound {bound}: kept {output.Length} bytes, [{codes}]");
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

    [Theory]
    [InlineData("FlateDecode")]
    [InlineData("RunLengthDecode")]
    public void A_decoder_holds_no_more_than_its_bound_when_its_guess_lands_just_short_of_it(string filter)
    {
        // A 4 MB bound. Flate guesses four times its input and RunLength twice, so an input of 1,048,575
        // or 2,097,150 bytes makes the guess 4 bytes short of the bound. A buffer that doubles when it
        // fills took 8 MB for the rest, then copied the 4 MB out: 16 MB for 4. A guess within a quarter of
        // the bound starts at the bound, and a full buffer is handed back as it is: one array of 4 MB.
        const int Bound = 4 * 1024 * 1024;
        var encoded = filter == "FlateDecode" ? ZerosCompressedTo(1_048_575, 2 * Bound) : RunsOf(2_097_150);
        var diagnostics = new PdfDiagnostics();
        var before = GC.GetAllocatedBytesForCurrentThread();

        var output = DecodeWithin(encoded, PdfName.Get(filter), Bound, diagnostics);

        (GC.GetAllocatedBytesForCurrentThread() - before).Should().BeLessThan(Bound + (Bound / 2));
        output.Length.Should().Be(Bound);
        diagnostics.Contains(PdfDiagnosticCodes.LimitDecodedStream).Should().BeTrue();
    }

    [Fact]
    public void Undoes_a_predictor_over_whole_rows_of_a_bounded_output()
    {
        // Ten PNG-up rows of four bytes, five with their tag byte. However many bytes the bound leaves,
        // the predictor keeps whole rows, and the report says decoding stopped rather than what was kept.
        var predicted = new byte[50];
        for (var row = 0; row < 10; row++)
        {
            predicted[row * 5] = 2;
            for (var column = 1; column < 5; column++)
            {
                predicted[(row * 5) + column] = (byte)column;
            }
        }

        var parameters = new PdfDictionary();
        parameters.Set(PdfName.Predictor, PdfInteger.Create(12));
        parameters.Set(PdfName.Columns, PdfInteger.Create(4));
        var dictionary = new PdfDictionary();
        dictionary.Set(PdfName.Filter, PdfName.FlateDecode);
        dictionary.Set(PdfName.DecodeParms, parameters);
        var stream = new PdfStream(dictionary, PdfStreamData.FromMemory(Compress(predicted)));
        var failures = new List<string>();

        for (var bound = 5; bound <= 50; bound++)
        {
            var diagnostics = new PdfDiagnostics();
            var output = PdfFilterPipeline.Decode(stream, diagnostics, Within(bound)).ToArray();
            var rows = bound / 5;
            var reported = diagnostics.Select(d => d.Code).ToArray();

            if (output.Length != rows * 4 || (rows > 0 && output[^1] != (byte)(4 * rows)) ||
                reported.Length != (bound < 50 ? 1 : 0) ||
                (bound < 50 && !diagnostics[0].Message.Contains("decoding stopped there.", StringComparison.Ordinal)))
            {
                failures.Add($"bound {bound}: {output.Length} bytes, [{string.Join(";", reported)}]");
            }
        }

        failures.Should().BeEmpty();
    }

    [Theory]
    [InlineData(2, 32, 8, 16_777_216)]
    [InlineData(12, 32, 16, 5_000_000)]
    [InlineData(12, 4, 8, 60_000_000)]
    public async Task Leaves_the_data_as_decoded_when_the_predictor_describes_rows_it_cannot_hold(
        int predictor, int colors, int bitsPerComponent, int columns)
    {
        // Parameters from the file: the first row length overflowed to zero and looped for ever, the
        // second to a negative size and threw, the third allocated two rows of 240 MB for twelve bytes.
        var parameters = new PdfDictionary();
        parameters.Set(PdfName.Predictor, PdfInteger.Create(predictor));
        parameters.Set(PdfName.Colors, PdfInteger.Create(colors));
        parameters.Set(PdfName.BitsPerComponent, PdfInteger.Create(bitsPerComponent));
        parameters.Set(PdfName.Columns, PdfInteger.Create(columns));
        var diagnostics = new PdfDiagnostics();
        var cancellation = TestContext.Current.CancellationToken;

        var decode = Task.Run(() => Decode(Compress("twelve bytes"u8.ToArray()), PdfName.FlateDecode, parameters, diagnostics), cancellation);
        var first = await Task.WhenAny(decode, Task.Delay(TimeSpan.FromSeconds(10), cancellation));

        first.Should().BeSameAs(decode, "the predictor must not loop");
        Text(await decode).Should().Be("twelve bytes");
        var report = diagnostics.Should().ContainSingle().Which;
        report.Code.Should().Be(PdfDiagnosticCodes.FilterFailed);
        report.Message.Should().StartWith("The predictor's parameters describe rows longer than the decoded data");
    }

    [Fact]
    public void A_bounded_output_never_grows_past_its_bound_and_hands_back_a_full_buffer_as_it_is()
    {
        var output = new PdfBoundedOutput(estimate: 10, maxLength: 1000);

        for (var i = 0; i < 200; i++)
        {
            output.TryFill((byte)i, 7);
        }

        output.Count.Should().Be(1000);
        output.Capacity.Should().Be(1000);
        output.TryWrite([]).Should().BeTrue("nothing always fits");
        output.TryWrite([1]).Should().BeFalse();
        output.Capacity.Should().Be(1000, "a write that does not fit must not grow the buffer");
        output.ToArray().Should().BeSameAs(output.ToArray(), "a full buffer is handed back without a copy");
    }

    [Theory]
    [InlineData(10, 1000, 10)]
    [InlineData(700, 1000, 700)]
    [InlineData(800, 1000, 1000)]
    [InlineData(5000, 1000, 1000)]
    [InlineData(0, 1000, 0)]
    public void A_bounded_output_starts_at_its_guess_or_at_the_bound_when_the_guess_is_within_a_quarter_of_it(
        long estimate, int bound, int capacity)
    {
        new PdfBoundedOutput(estimate, bound).Capacity.Should().Be(capacity);
    }

    [Fact]
    public void A_bounded_output_goes_straight_to_its_bound_when_a_doubling_lands_within_a_quarter_of_it()
    {
        // 100, 200, 400: the next doubling, 800, is within a quarter of 1,000, so the buffer becomes 1,000
        // at once rather than 800 now and 1,000 a few bytes later.
        var output = new PdfBoundedOutput(estimate: 100, maxLength: 1000);
        var capacities = new List<int>();

        for (var i = 0; i < 1000; i++)
        {
            output.TryFill(0, 1);
            if (capacities.Count == 0 || capacities[^1] != output.Capacity)
            {
                capacities.Add(output.Capacity);
            }
        }

        capacities.Should().Equal(100, 200, 400, 1000);
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

        var output = PdfFilterPipeline.Decode(stream, diagnostics, Within(10_000));

        output.Length.Should().Be(10_000);
        diagnostics.Should().ContainSingle()
            .Which.Message.Should().Be(
                "The /RunLengthDecode data decodes to more than 10,000 bytes; decoding stopped there. " +
                "Raise PdfReaderLimits.MaxDecodedStreamLength to read past it.");
    }

    [Fact]
    public void Tells_a_corrupt_flate_stream_from_one_that_reaches_the_bound()
    {
        var corrupt = new PdfDiagnostics();
        var bounded = new PdfDiagnostics();

        DecodeWithin([0x01, 0x02, 0x03, 0x04, 0x05, 0x06], PdfName.FlateDecode, 1000, corrupt);
        DecodeWithin(FlateZlib, PdfName.FlateDecode, 10, bounded);

        corrupt.Select(d => d.Code).Should().Equal(PdfDiagnosticCodes.FilterFailed);
        bounded.Select(d => d.Code).Should().Equal(PdfDiagnosticCodes.LimitDecodedStream);
    }

    [Fact]
    public void Undoes_a_png_up_predictor()
    {
        byte[] predicted = [0x02, 0x0A, 0x14, 0x1E, 0x28, 0x02, 0x01, 0x02, 0x03, 0x04, 0x02, 0x01, 0x02, 0x03, 0x04];

        PredictorTransform.TryApply(predicted, predictor: 12, colors: 1, bitsPerComponent: 8, columns: 4, out var result)
            .Should().BeTrue();

        result.Should().Equal((byte)10, 20, 30, 40, 11, 22, 33, 44, 12, 24, 36, 48);
    }

    [Fact]
    public void Undoes_a_tiff_predictor()
    {
        byte[] predicted = [10, 5, 5, 5];

        PredictorTransform.TryApply(predicted, predictor: 2, colors: 1, bitsPerComponent: 8, columns: 4, out var result)
            .Should().BeTrue();

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
        return PdfFilterPipeline.Decode(new PdfStream(dictionary, PdfStreamData.FromMemory(data)), diagnostics, Within(maxLength));
    }

    private static PdfLimitGuard Within(int maxLength) =>
        new(PdfReaderLimits.Default with { MaxDecodedStreamLength = maxLength }, throwOnLimit: false);

    /// <summary>Packs LZW codes nine bits each, most significant bit first, as the filter reads them.</summary>
    private static byte[] NineBitCodes(params int[] codes)
    {
        var bytes = new List<byte>();
        var buffer = 0;
        var count = 0;

        foreach (var code in codes)
        {
            buffer = (buffer << 9) | code;
            count += 9;

            while (count >= 8)
            {
                bytes.Add((byte)(buffer >> (count - 8)));
                count -= 8;
                buffer &= (1 << count) - 1;
            }
        }

        if (count > 0)
        {
            bytes.Add((byte)(buffer << (8 - count)));
        }

        return [.. bytes];
    }

    private static byte[] Compress(byte[] data)
    {
        using var compressed = new MemoryStream();
        using (var zlib = new System.IO.Compression.ZLibStream(compressed, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(data);
        }

        return compressed.ToArray();
    }

    /// <summary>A zlib stream of <paramref name="zeros"/> zero bytes, padded with zeros to <paramref name="length"/> bytes.</summary>
    private static byte[] ZerosCompressedTo(int length, int zeros)
    {
        var compressed = Compress(new byte[zeros]);
        var padded = new byte[length];
        compressed.CopyTo(padded, 0);
        return padded;
    }

    /// <summary>RunLength data of <paramref name="length"/> bytes: runs of 128 'A', and the end marker if there is room.</summary>
    private static byte[] RunsOf(int length)
    {
        var data = new byte[length];
        for (var i = 0; i + 1 < length; i += 2)
        {
            data[i] = 0x81;
            data[i + 1] = (byte)'A';
        }

        return data;
    }

    private static string Text(ReadOnlyMemory<byte> data) => Encoding.ASCII.GetString(data.Span);
}
