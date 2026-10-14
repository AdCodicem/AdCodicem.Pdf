using System.Buffers;
using System.IO.Compression;
using System.Text;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.IO;
using AdCodicem.Pdf.IO.Filters;
using AdCodicem.Pdf.Objects;
using NSubstitute;

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
    public void Keeps_what_it_could_decode_of_a_truncated_flate_stream_and_says_so()
    {
        var diagnostics = new PdfDiagnostics();
        var truncated = FlateZlib.AsSpan(0, 20).ToArray();

        var decoded = Text(Decode(truncated, PdfName.FlateDecode, diagnostics: diagnostics));

        decoded.Should().NotBeEmpty();
        FlateText.Should().StartWith(decoded);
        var report = diagnostics.Should().ContainSingle().Which;
        report.Code.Should().Be(PdfDiagnosticCodes.FilterFailed);
        report.Severity.Should().Be(PdfDiagnosticSeverity.Warning);
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
    public void Ends_a_chain_at_a_flate_step_that_decodes_nothing_and_returns_what_the_filters_before_it_decoded()
    {
        // The hex decodes to bytes no Flate reading can decode: the Flate filters after the first would each fail on
        // them again, and the hex after them decode bytes that are no hex.
        var dictionary = new PdfDictionary();
        dictionary.Set(PdfName.Filter, new PdfArray([PdfName.ASCIIHexDecode, PdfName.FlateDecode, PdfName.FlateDecode, PdfName.ASCIIHexDecode]));
        var diagnostics = new PdfDiagnostics();

        var decoded = new PdfStream(dictionary, PdfStreamData.FromMemory("FFFF>"u8.ToArray())).Decode(diagnostics);

        decoded.ToArray().Should().Equal(0xFF, 0xFF);
        var report = diagnostics.Should().ContainSingle().Which;
        report.Code.Should().Be(PdfDiagnosticCodes.FilterFailed);
        report.Message.Should().Be("A Flate stream could not be decoded.");
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
    public void Skips_the_introducer_some_producers_keep_before_ascii85_data()
    {
        // Python's base64.a85encode(..., adobe=True) writes the "<~" PostScript uses and PDF does not.
        Text(Decode("<~6\"FnCAKYc\"AT2]5FD,5.Bl8$5De+!#AT@~>"u8.ToArray(), PdfName.ASCII85Decode))
            .Should().Be("Adobe keeps the introducer");
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
    public void Stops_an_lzw_stream_at_a_code_it_has_not_defined_and_says_so()
    {
        // "A", a clear, then code 300: after a clear the table holds only the 258 fixed codes, and there is no
        // previous sequence to extend, so the code means nothing. What came before it is kept, and the code
        // is reported (T32).
        var encoded = NineBitCodes(256, 'A', 256, 300, 'B');
        var diagnostics = new PdfDiagnostics();

        Text(Decode(encoded, PdfName.LZWDecode, diagnostics: diagnostics)).Should().Be("A");

        var report = diagnostics.Should().ContainSingle().Which;
        report.Code.Should().Be(PdfDiagnosticCodes.FilterFailed);
        report.Severity.Should().Be(PdfDiagnosticSeverity.Warning);
        report.Message.Should().Be("An LZW stream uses code 300, which it has not defined; what decoded before it was kept.");
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

        PredictorTransform.TryApply(data, predictor, colors: 1, bitsPerComponent: 8, columns: 4, cut: false, out var result).Should().BeTrue();

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
    public void Undoes_a_predictor_over_the_rows_of_a_bounded_output_and_the_part_of_the_last_it_holds()
    {
        // Ten PNG-up rows of four bytes, five with their tag byte. However many bytes the bound leaves, the predictor
        // undoes the whole rows, and of the row the bound cut, what follows its tag byte: each byte is predicted from the
        // one above it alone. The report says decoding stopped, not that the parameters describe rows the data lacks.
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

        for (var bound = 1; bound <= 50; bound++)
        {
            var diagnostics = new PdfDiagnostics();
            var output = PdfFilterPipeline.Decode(stream, diagnostics, Within(bound)).ToArray();
            var expected = new List<byte>();

            for (var at = 0; at < bound; at++)
            {
                if (at % 5 != 0)
                {
                    expected.Add((byte)(((at / 5) + 1) * (at % 5)));
                }
            }

            var reported = diagnostics.Select(d => d.Code).ToArray();

            if (!output.SequenceEqual(expected) ||
                !reported.SequenceEqual(bound < 50 ? [PdfDiagnosticCodes.LimitDecodedStream] : []) ||
                (bound < 50 && !diagnostics[0].Message.Contains("decoding stopped there.", StringComparison.Ordinal)))
            {
                failures.Add($"bound {bound}: {output.Length} bytes, [{string.Join(";", reported)}]");
            }
        }

        failures.Should().BeEmpty();
    }

    [Fact]
    public void Undoes_a_tiff_predictor_over_the_rows_of_a_bounded_output_and_the_part_of_the_last_it_holds()
    {
        // Ten TIFF-predicted rows of four bytes, each byte the difference from the one before it. However many bytes the
        // bound leaves, the whole rows are undone, and so is the part of the row the bound cut: each byte is predicted
        // from the bytes before it in its row alone.
        var predicted = new byte[40];
        Array.Fill(predicted, (byte)1);
        var parameters = new PdfDictionary();
        parameters.Set(PdfName.Predictor, PdfInteger.Create(2));
        parameters.Set(PdfName.Columns, PdfInteger.Create(4));
        var dictionary = new PdfDictionary();
        dictionary.Set(PdfName.Filter, PdfName.FlateDecode);
        dictionary.Set(PdfName.DecodeParms, parameters);
        var stream = new PdfStream(dictionary, PdfStreamData.FromMemory(Compress(predicted)));
        var failures = new List<string>();

        for (var bound = 1; bound <= 40; bound++)
        {
            var diagnostics = new PdfDiagnostics();
            var output = PdfFilterPipeline.Decode(stream, diagnostics, Within(bound)).ToArray();
            var expected = Enumerable.Range(0, bound).Select(at => (byte)((at % 4) + 1)).ToArray();

            if (!output.SequenceEqual(expected) ||
                !diagnostics.Select(d => d.Code).SequenceEqual(bound < 40 ? [PdfDiagnosticCodes.LimitDecodedStream] : []))
            {
                failures.Add($"bound {bound}: [{string.Join(",", output)}]");
            }
        }

        failures.Should().BeEmpty();
    }

    [Fact]
    public void Gives_no_parameters_to_a_filter_an_array_of_them_is_too_short_to_reach()
    {
        // ISO 32000-1 asks for as many parameters as filters; one missing at the Flate step's place is no predictor.
        byte[] rows = [2, 1, 2, 3, 4];
        var dictionary = new PdfDictionary();
        dictionary.Set(PdfName.Filter, new PdfArray([PdfName.ASCIIHexDecode, PdfName.FlateDecode]));
        dictionary.Set(PdfName.DecodeParms, new PdfArray([PdfNull.Instance]));
        var diagnostics = new PdfDiagnostics();
        var hex = Encoding.ASCII.GetBytes(Convert.ToHexString(Compress(rows)) + ">");

        var decoded = new PdfStream(dictionary, PdfStreamData.FromMemory(hex)).Decode(diagnostics);

        decoded.ToArray().Should().Equal(rows);
        diagnostics.Should().BeEmpty();
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
    public void Bounds_a_filter_in_the_middle_of_a_chain_as_well_as_the_last_and_reports_the_first_that_stopped()
    {
        // 15,000 RunLength pairs, 30,000 bytes once inflated: Flate, the first step, stops at the bound, and RunLength
        // decodes what it kept, until it stops at the bound in turn (#158). The stream was cut once, where Flate stopped:
        // that RunLength stops too follows from it (#199).
        var runs = new byte[30_000];
        for (var pair = 0; pair < 15_000; pair++)
        {
            runs[2 * pair] = 0x81;
            runs[(2 * pair) + 1] = (byte)'A';
        }

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
        diagnostics.Select(d => d.Message).Should().Equal(
            "The /FlateDecode data decodes to more than 10,000 bytes; decoding stopped there. " +
            "Raise PdfReaderLimits.MaxDecodedStreamLength to read past it.");
    }

    [Theory]
    [InlineData(604, 100, "")]
    [InlineData(603, 100, "shared")]
    [InlineData(500, 48, "shared")]
    [InlineData(403, 0, "shared")]
    [InlineData(402, 0, "its own")]
    public void Shares_one_bound_among_the_filters_before_the_last(int bound, int kept, string reached)
    {
        // A hundred bytes hex-encoded three times over: the first filter passes along 403 bytes, the second 201, the
        // last decodes them to the hundred. The first two share the bound, 604 bytes for both; the last keeps its own.
        // What the second may pass along is what the first left of it, and when it stops there the last decodes what it
        // kept as data the reader cut, a digit without its pair dropped (#166). A first filter that stops at the bound
        // itself is reported as before.
        var plain = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("0123456789", 10)));
        var encoded = plain;

        for (var layer = 0; layer < 3; layer++)
        {
            encoded = Encoding.ASCII.GetBytes(Convert.ToHexString(encoded) + ">");
        }

        var dictionary = new PdfDictionary();
        dictionary.Set(PdfName.Filter, new PdfArray([PdfName.ASCIIHexDecode, PdfName.ASCIIHexDecode, PdfName.ASCIIHexDecode]));
        var diagnostics = new PdfDiagnostics();

        var decoded = PdfFilterPipeline.Decode(new PdfStream(dictionary, PdfStreamData.FromMemory(encoded)), diagnostics, Within(bound));

        decoded.ToArray().Should().Equal(plain.AsSpan(0, kept).ToArray());
        diagnostics.Select(d => d.Message).Should().Equal(
            reached switch
            {
                "shared" => [$"The filters before the last decode to more than {bound:N0} bytes together; decoding stopped in the /ASCIIHexDecode data. Raise PdfReaderLimits.MaxDecodedStreamLength to read past it."],
                "its own" => [$"The /ASCIIHexDecode data decodes to more than {bound:N0} bytes; decoding stopped there. Raise PdfReaderLimits.MaxDecodedStreamLength to read past it."],
                _ => [],
            });
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

    [Theory]
    [InlineData(LibpngAdaptive, new byte[] { 1, 2, 3, 4 })]
    [InlineData(LibpngUnfiltered, new byte[] { 0 })]
    public void Undoes_every_png_predictor_as_libpng_writes_it(string idat, byte[] filters)
    {
        // Two PNG files of the same 8 by 6 RGB image, written by libpng through ImageMagick 6.9.12: one with its
        // adaptive filtering, which chose Sub, Up, Average and Paeth row by row, one with none. A PNG's IDAT is
        // what /FlateDecode with /Predictor 15 decodes.
        var data = Convert.FromBase64String(idat);
        RowFilters(data, stride: (8 * 3) + 1).Should().Equal(filters, "the vector must exercise these filters");
        var parameters = new PdfDictionary();
        parameters.Set(PdfName.Predictor, PdfInteger.Create(15));
        parameters.Set(PdfName.Colors, PdfInteger.Create(3));
        parameters.Set(PdfName.BitsPerComponent, PdfInteger.Create(8));
        parameters.Set(PdfName.Columns, PdfInteger.Create(8));

        var decoded = Decode(data, PdfName.FlateDecode, parameters);

        decoded.ToArray().Should().Equal(Convert.FromBase64String(LibpngPixels));
    }

    [Theory]
    [InlineData(1, 32)]
    [InlineData(2, 16)]
    [InlineData(4, 8)]
    [InlineData(16, 2)]
    public void Leaves_a_tiff_predictor_over_components_other_than_a_byte_wide_alone(int bitsPerComponent, int columns)
    {
        byte[] predicted = [0x12, 0x34, 0x56, 0x78];

        PredictorTransform.TryApply(predicted, predictor: 2, colors: 1, bitsPerComponent, columns, cut: false, out var result)
            .Should().BeTrue();

        result.Should().Equal((byte)0x12, 0x34, 0x56, 0x78);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(32)]
    public void Takes_a_bits_per_component_pdf_does_not_allow_for_eight(int bitsPerComponent)
    {
        byte[] predicted = [10, 5, 5, 5];

        PredictorTransform.TryApply(predicted, predictor: 2, colors: 1, bitsPerComponent, columns: 4, cut: false, out var result)
            .Should().BeTrue();

        result.Should().Equal((byte)10, 15, 20, 25);
    }

    [Fact]
    public void Undoes_a_png_up_predictor()
    {
        byte[] predicted = [0x02, 0x0A, 0x14, 0x1E, 0x28, 0x02, 0x01, 0x02, 0x03, 0x04, 0x02, 0x01, 0x02, 0x03, 0x04];

        PredictorTransform.TryApply(predicted, predictor: 12, colors: 1, bitsPerComponent: 8, columns: 4, cut: false, out var result)
            .Should().BeTrue();

        result.Should().Equal((byte)10, 20, 30, 40, 11, 22, 33, 44, 12, 24, 36, 48);
    }

    [Fact]
    public void Undoes_a_tiff_predictor()
    {
        byte[] predicted = [10, 5, 5, 5];

        PredictorTransform.TryApply(predicted, predictor: 2, colors: 1, bitsPerComponent: 8, columns: 4, cut: false, out var result)
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
    public void Quotes_a_filter_it_does_not_know_escaped()
    {
        // An escape sequence and C1 controls in a filter's name reach no message as themselves (#159).
        var diagnostics = new PdfDiagnostics();

        Decode([1, 2, 3], PdfName.Get("A\u001B[31mred\u0080\u009F"), diagnostics: diagnostics);

        diagnostics.Should().ContainSingle(d => d.Code == PdfDiagnosticCodes.FilterUnsupported)
            .Which.Message.Should().Be("The filter /A#1B#5B31mred#80#9F is not supported; decoding stopped there.");
    }

    [Fact]
    public void Stops_a_chain_at_the_first_filter_it_does_not_know_and_reports_it_once()
    {
        // The filters after an unknown one would read what it encoded: Flate would find no zlib data there, and the
        // second unknown filter would be reported for data nothing decoded.
        var dictionary = new PdfDictionary();
        dictionary.Set(PdfName.Filter, new PdfArray([PdfName.Get("MadeUpDecode"), PdfName.FlateDecode, PdfName.Get("OtherDecode")]));
        var diagnostics = new PdfDiagnostics();

        var decoded = new PdfStream(dictionary, PdfStreamData.FromMemory("plain"u8.ToArray())).Decode(diagnostics);

        Text(decoded).Should().Be("plain");
        diagnostics.Should().ContainSingle()
            .Which.Message.Should().Be("The filter /MadeUpDecode is not supported; decoding stopped there.");
    }

    [Fact]
    public void Returns_what_the_filters_before_an_unknown_one_decoded()
    {
        var dictionary = new PdfDictionary();
        dictionary.Set(PdfName.Filter, new PdfArray([PdfName.ASCIIHexDecode, PdfName.Get("MadeUpDecode"), PdfName.ASCIIHexDecode]));
        var diagnostics = new PdfDiagnostics();

        var decoded = new PdfStream(dictionary, PdfStreamData.FromMemory("3730363936453631>"u8.ToArray())).Decode(diagnostics);

        // One hex decoding undone, the second left: the data is what the first filter gave.
        Text(decoded).Should().Be("70696E61");
        diagnostics.Should().ContainSingle().Which.Code.Should().Be(PdfDiagnosticCodes.FilterUnsupported);
    }

    [Theory]
    [InlineData("7", "an integer")]
    [InlineData("1.5", "a real number")]
    [InlineData("(FlateDecode)", "a string")]
    [InlineData("true", "a boolean")]
    [InlineData("<< /FlateDecode 1 >>", "a dictionary")]
    [InlineData("4 0 R", "an integer")]
    public void Leaves_the_data_as_it_is_under_a_filter_entry_that_is_neither_a_name_nor_an_array_and_says_where(
        string filter, string kind)
    {
        // Where the stream's data starts, as every other report on its filters is placed, and what the entry holds in
        // words that quote nothing of the file (#209).
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .Stream(3, $"/Filter {filter}", "plain")
            .WithObject(4, "7")
            .BuildClassic(rootNumber: 1);

        using var document = PdfDocument.Open(file);
        var stream = document.GetObject(new PdfObjectId(3)).AsStream().Required();
        var decoded = stream.Decode(document.Diagnostics);

        Text(decoded).Should().Be("plain");
        var report = document.Diagnostics.Should().ContainSingle().Which;
        report.Code.Should().Be(PdfDiagnosticCodes.FilterUnsupported);
        report.Message.Should().Be($"The /Filter entry is {kind}, neither a name nor an array; the data was left as it is.");
        report.Position.Should().Be(stream.Data.Position).And.BePositive();
    }

    [Theory]
    [InlineData("null")]
    [InlineData("4 0 R")]
    [InlineData("99 0 R")]
    public void Reads_a_filter_entry_that_is_null_as_one_the_stream_does_not_have(string filter)
    {
        // Null, an object that holds null, and an object the file lacks (7.3.10): the data is not encoded.
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .Stream(3, $"/Filter {filter}", "plain")
            .WithObject(4, "null")
            .BuildClassic(rootNumber: 1);

        using var document = PdfDocument.Open(file);
        var decoded = document.GetObject(new PdfObjectId(3)).AsStream().Required().Decode(document.Diagnostics);

        Text(decoded).Should().Be("plain");
        document.Diagnostics.Should().BeEmpty();
    }

    [Theory]
    [InlineData("5", "an integer")]
    [InlineData("(x)", "a string")]
    [InlineData("[/FlateDecode]", "an array")]
    [InlineData("null", "null")]
    [InlineData("99 0 R", "null")]
    public void Ends_a_chain_at_an_element_that_names_no_filter_and_returns_what_the_filters_before_it_decoded(
        string element, string kind)
    {
        // The filters after the element would decode what it encoded, if it encoded anything: Flate would find no zlib
        // data in what the hex gave, and report damage the file does not have.
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .Stream(3, $"/Filter [/ASCIIHexDecode {element} /FlateDecode /ASCIIHexDecode]", "706C61696E>")
            .BuildClassic(rootNumber: 1);

        using var document = PdfDocument.Open(file);
        var stream = document.GetObject(new PdfObjectId(3)).AsStream().Required();
        var decoded = stream.Decode(document.Diagnostics);

        Text(decoded).Should().Be("plain");
        var report = document.Diagnostics.Should().ContainSingle().Which;
        report.Code.Should().Be(PdfDiagnosticCodes.FilterUnsupported);
        report.Message.Should().Be($"Element 1 of the /Filter entry is {kind}, which names no filter; decoding stopped there.");
        report.Position.Should().Be(stream.Data.Position).And.BePositive();
    }

    [Fact]
    public void Reports_no_damage_for_data_an_element_that_names_no_filter_stands_before()
    {
        var dictionary = new PdfDictionary();
        dictionary.Set(PdfName.Filter, new PdfArray([PdfInteger.Create(5), PdfName.FlateDecode]));
        var diagnostics = new PdfDiagnostics();

        var decoded = new PdfStream(dictionary, PdfStreamData.FromMemory("plain"u8.ToArray())).Decode(diagnostics);

        Text(decoded).Should().Be("plain");
        diagnostics.Select(d => d.Code).Should().Equal(PdfDiagnosticCodes.FilterUnsupported);
    }

    [Fact]
    public void Stops_a_chain_of_filters_at_an_image_filter()
    {
        // The JPEG is hex-encoded: decoding undoes the hex and leaves the JPEG to whoever renders it.
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0];
        var dictionary = new PdfDictionary();
        dictionary.Set(PdfName.Filter, new PdfArray([PdfName.ASCIIHexDecode, PdfName.DCTDecode]));
        var stream = new PdfStream(dictionary, PdfStreamData.FromMemory(Encoding.ASCII.GetBytes(Convert.ToHexString(jpeg) + ">")));

        stream.Decode().ToArray().Should().Equal(jpeg);
    }

    [Theory]
    [InlineData("the second filter")]
    [InlineData("the second step's parameters")]
    [InlineData("/Predictor")]
    [InlineData("/Colors")]
    [InlineData("/BitsPerComponent")]
    [InlineData("/Columns")]
    [InlineData("/EarlyChange")]
    public void Resolves_every_value_a_step_reads_before_any_step_reads_the_data(string where)
    {
        // A chain of two steps whose second takes one value through a reference, to a source that keeps nothing, as a
        // reader whose cache let the object go: each resolution is a load. Every load comes before any step looks at
        // the data, so that a load that decodes an object stream runs while this stream holds nothing decoded (#165).
        var lzw = where == "/EarlyChange";
        byte[] rows = [0, (byte)'H', (byte)'e', (byte)'l', (byte)'l', (byte)'o', 0, (byte)'w', (byte)'o', (byte)'r', (byte)'l', (byte)'d'];
        byte[] inner = lzw ? [0x80, 0x0B, 0x60, 0x50, 0x22, 0x0C, 0x0C, 0x85, 0x01] : Compress(rows);
        using var memory = new LookCountingMemory(Compress(inner));
        var loads = new List<int>();
        var second = lzw ? PdfName.LZWDecode : PdfName.FlateDecode;
        var parameters = new PdfDictionary();
        PdfObject value = where switch
        {
            "the second filter" => second,
            "the second step's parameters" => parameters,
            "/Predictor" => PdfInteger.Create(12),
            "/Columns" => PdfInteger.Create(5),
            "/BitsPerComponent" => PdfInteger.Create(8),
            _ => PdfInteger.Create(1),
        };
        var source = Substitute.For<IPdfObjectSource>();
        source.GetObject(Arg.Any<PdfObjectId>()).Returns(_ =>
        {
            loads.Add(memory.Looks);
            return value;
        });
        var reference = new PdfReference(new PdfObjectId(9), source);

        if (lzw)
        {
            parameters.Set(PdfName.EarlyChange, reference);
        }
        else
        {
            parameters.Set(PdfName.Predictor, where == "/Predictor" ? reference : PdfInteger.Create(12));
            parameters.Set(PdfName.Columns, where == "/Columns" ? reference : PdfInteger.Create(5));
            parameters.Set(PdfName.Colors, where == "/Colors" ? reference : PdfInteger.Create(1));
            parameters.Set(PdfName.BitsPerComponent, where == "/BitsPerComponent" ? reference : PdfInteger.Create(8));
        }

        var dictionary = new PdfDictionary();
        dictionary.Set(PdfName.Filter, new PdfArray([PdfName.FlateDecode, where == "the second filter" ? reference : second]));
        dictionary.Set(PdfName.DecodeParms, new PdfArray([PdfNull.Instance, where == "the second step's parameters" ? reference : parameters]));
        var diagnostics = new PdfDiagnostics();
        var stream = new PdfStream(dictionary, PdfStreamData.FromMemory(memory.Memory));
        var unread = memory.Looks;

        var decoded = PdfFilterPipeline.Decode(stream, diagnostics);

        Text(decoded).Should().Be(lzw ? "-----A---B" : "Helloworld");
        diagnostics.Should().BeEmpty();
        loads.Should().ContainSingle().Which.Should().Be(unread, "the value is loaded before the data is read");
    }

    [Theory]
    [InlineData("/Filter /ASCIIHexDecode /DecodeParms 9 0 R", 0)]
    [InlineData("/Filter [/ASCIIHexDecode /RunLengthDecode /ASCII85Decode /Crypt] /DecodeParms [9 0 R 9 0 R 9 0 R 9 0 R]", 0)]
    [InlineData("/Filter [/ASCIIHexDecode /DCTDecode] /DecodeParms [null 9 0 R]", 0)]
    [InlineData("/Filter [/ASCIIHexDecode /DCTDecode 9 0 R] /DecodeParms [null null 9 0 R]", 0)]
    [InlineData("/Filter [/ASCIIHexDecode /MadeUpDecode /FlateDecode] /DecodeParms [null 9 0 R 9 0 R]", 0)]
    [InlineData("/Filter [/ASCIIHexDecode 5 9 0 R] /DecodeParms 9 0 R", 0)]
    [InlineData("/Filter [/ASCIIHexDecode /FlateDecode] /DecodeParms [null 9 0 R]", 1)]
    public void Resolves_nothing_a_step_does_not_read(string entries, int expected)
    {
        // Each value is a reference to an object nothing should ask for: parameters of filters that take none, and
        // what stands at or past an image filter, an unknown one, or an element that names none. The last case, whose
        // Flate step does read its parameters, shows the source is asked when it should be.
        var asked = 0;
        var source = Substitute.For<IPdfObjectSource>();
        source.GetObject(Arg.Any<PdfObjectId>()).Returns(_ =>
        {
            asked++;
            return PdfNull.Instance;
        });
        var dictionary = new PdfObjectParser(Encoding.ASCII.GetBytes($"<< {entries} >>"), source: source).ParseObject().AsDictionary().Required();
        var stream = new PdfStream(dictionary, PdfStreamData.FromMemory(Encoding.ASCII.GetBytes(Convert.ToHexString(Compress("Hello"u8.ToArray())) + ">")));

        stream.Decode(new PdfDiagnostics());

        asked.Should().Be(expected);
    }

    [Fact]
    public void Undoes_the_predictor_an_array_of_parameters_gives_a_single_filter()
    {
        // ISO 32000-1 (7.3.8.2) pairs an array of parameters with an array of filters; beside a single name, its first
        // element is the filter's, as qpdf reads it, rather than no parameters at all and the rows left predicted.
        byte[] rows = [2, 1, 2, 3, 4, 2, 1, 1, 1, 1];
        var parameters = new PdfDictionary();
        parameters.Set(PdfName.Predictor, PdfInteger.Create(12));
        parameters.Set(PdfName.Columns, PdfInteger.Create(4));
        var dictionary = new PdfDictionary();
        dictionary.Set(PdfName.Filter, PdfName.FlateDecode);
        dictionary.Set(PdfName.DecodeParms, new PdfArray([parameters]));
        var diagnostics = new PdfDiagnostics();

        var decoded = new PdfStream(dictionary, PdfStreamData.FromMemory(Compress(rows))).Decode(diagnostics);

        decoded.ToArray().Should().Equal(1, 2, 3, 4, 2, 3, 4, 5);
        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void Gives_a_dictionary_of_parameters_beside_a_chain_to_each_of_its_steps()
    {
        // A shape ISO 32000-1 does not allow, which qpdf reads too: the hex step takes no parameters, and the Flate step
        // undoes the predictor the dictionary describes.
        byte[] rows = [2, 1, 2, 3, 4, 2, 1, 1, 1, 1];
        var parameters = new PdfDictionary();
        parameters.Set(PdfName.Predictor, PdfInteger.Create(12));
        parameters.Set(PdfName.Columns, PdfInteger.Create(4));
        var dictionary = new PdfDictionary();
        dictionary.Set(PdfName.Filter, new PdfArray([PdfName.ASCIIHexDecode, PdfName.FlateDecode]));
        dictionary.Set(PdfName.DecodeParms, parameters);
        var diagnostics = new PdfDiagnostics();
        var hex = Encoding.ASCII.GetBytes(Convert.ToHexString(Compress(rows)) + ">");

        var decoded = new PdfStream(dictionary, PdfStreamData.FromMemory(hex)).Decode(diagnostics);

        decoded.ToArray().Should().Equal(1, 2, 3, 4, 2, 3, 4, 5);
        diagnostics.Should().BeEmpty();
    }

    [Theory]
    [InlineData("/Filter /DCTDecode", true)]
    [InlineData("/Filter [/ASCIIHexDecode /DCTDecode]", true)]
    [InlineData("/Filter [/ASCIIHexDecode /JPXDecode /FlateDecode]", true)]
    [InlineData("/Filter [/MadeUpDecode /DCTDecode]", false)]
    [InlineData("/Filter [5 /DCTDecode]", false)]
    [InlineData("/Filter (DCTDecode)", false)]
    [InlineData("/Filter [/ASCIIHexDecode /FlateDecode]", false)]
    [InlineData("/Filter null", false)]
    [InlineData("", false)]
    public void Says_a_stream_is_an_image_only_when_decoding_it_stops_at_an_image_filter(string entries, bool image)
    {
        // Decoding stops at the first filter it does not know, or at an element that names none: the data that comes
        // back is not the image a filter after it names. Telling it reads no parameter.
        var source = Substitute.For<IPdfObjectSource>();
        var dictionary = new PdfObjectParser(Encoding.ASCII.GetBytes($"<< {entries} /DecodeParms 9 0 R >>"), source: source).ParseObject().AsDictionary().Required();
        var stream = new PdfStream(dictionary, PdfStreamData.FromMemory("00>"u8.ToArray()));

        stream.HasImageFilter().Should().Be(image);
        source.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public void Returns_raw_data_when_no_filter_is_declared()
    {
        var stream = new PdfStream(new PdfDictionary(), PdfStreamData.FromMemory("plain"u8.ToArray()));

        Text(stream.Decode()).Should().Be("plain");
    }

    /// <summary>The IDAT of libpng's adaptive rendering of <see cref="LibpngPixels"/>.</summary>
    private const string LibpngAdaptive =
        "CNdtzCEOg1AMgGG2IRATvUzlu1Avg6wg2QFIqGyyZPKJCRwCUUeTmacqEDgECMR++yX/o0Jq+zzH8/WeflU3fJe17T9z3CMi58zMRJRSAgB3V9X6gG1jIkREADzgNjWp+VcdEaONdsnNzOxcibCIiEgpxd1VdAefBVSZ";

    /// <summary>The IDAT of libpng's unfiltered rendering of <see cref="LibpngPixels"/>.</summary>
    private const string LibpngUnfiltered =
        "CNcBlgBp/wAANWqPxPkeU4it4hc8cabLADVaj8TpHlMA8SZbU4i9teofF0yBea7j2xBFPXKnn9QJAOIXTBdMgUyBtoG267brIOsgVSBVilWKvwDTCD3bEEXjGE3rIFXzKF37MGUDOG0LQHUAxPkun9QJeq/kVYq/MGWaC0B15htQwfYrALXqH2OYzRFGe7/0KW2i1xtQhcn+M3es4bM7RMU=";

    /// <summary>
    /// An 8 by 6 RGB image, sample (x, y, c) being ((x·97 + y·31)·7919 + c·53 + x·y·211) mod 256: values that
    /// leave every PNG filter something to predict.
    /// </summary>
    private const string LibpngPixels =
        "ADVqj8T5HlOIreIXPHGmywA1Wo/E6R5T8SZbU4i9teofF0yBea7j2xBFPXKnn9QJ4hdMF0yBTIG2gbbrtusg6yBVIFWKVYq/0wg92xBF4xhN6yBV8yhd+zBlAzhtC0B1xPkun9QJeq/kVYq/MGWaC0B15htQwfYrteofY5jNEUZ7v/QpbaLXG1CFyf4zd6zh";

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

    /// <summary>Reads which filter bytes the PNG rows start with, in order, after inflating the data.</summary>
    private static byte[] RowFilters(byte[] zlib, int stride)
    {
        using var inflated = new MemoryStream();
        using (var inflater = new System.IO.Compression.ZLibStream(new MemoryStream(zlib), System.IO.Compression.CompressionMode.Decompress))
        {
            inflater.CopyTo(inflated);
        }

        var rows = inflated.ToArray();
        return [.. Enumerable.Range(0, rows.Length / stride).Select(row => rows[row * stride]).Distinct().Order()];
    }

    private static string Text(ReadOnlyMemory<byte> data) => Encoding.ASCII.GetString(data.Span);

    /// <summary>Data that counts how often a decoder looks at it.</summary>
    private sealed class LookCountingMemory(byte[] contents) : MemoryManager<byte>
    {
        /// <summary>Gets how many times the data was looked at.</summary>
        public int Looks { get; private set; }

        public override Span<byte> GetSpan()
        {
            Looks++;
            return contents;
        }

        public override MemoryHandle Pin(int elementIndex = 0) => throw new NotSupportedException();

        public override void Unpin()
        {
        }

        protected override void Dispose(bool disposing)
        {
        }
    }
}
