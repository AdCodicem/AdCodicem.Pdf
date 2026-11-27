using System.Buffers;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.IO;
using AdCodicem.Pdf.IO.Filters;
using AdCodicem.Pdf.Objects;
using AdCodicem.Pdf.Validation;
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

    [Theory]
    [InlineData(4, true)]
    [InlineData(5, true)]
    [InlineData(6, false)]
    public void A_group_ascii85_data_that_ends_where_the_file_s_may_not_parts_is_dropped_and_counted_against_the_bound(int maxLength, bool limited)
    {
        // "5sdq," is ABCD, and "77I" the part of the group EF's: data the reader cut, or whose end a guard hides, may end
        // inside the group, whose bytes the missing digits decide. They are not written, but the whole data holds them,
        // and a bound they do not fit within is reached (#224): with no room for them, with room for one of the two, not
        // with room for both.
        var decoded = Ascii85Filter.Decode("5sdq,77I"u8, out var reached, out _, maxLength, cut: true);

        decoded.Should().Equal("ABCD"u8.ToArray());
        reached.Should().Be(limited);
        Ascii85Filter.Decode("5sdq,77I"u8, out _, out _, 6).Should().Equal("ABCDEF"u8.ToArray());
    }

    [Theory]
    [InlineData(2, true)]
    [InlineData(3, false)]
    public void A_digit_asciihex_data_that_ends_where_the_file_s_may_not_leaves_alone_is_dropped_and_counted_against_the_bound(int maxLength, bool limited)
    {
        // The last digit's byte, which the next digit decides, is not written, but the whole data holds it, and a bound it
        // does not fit within is reached (#224).
        var decoded = AsciiHexFilter.Decode("41424"u8, out var reached, out _, maxLength, cut: true);

        decoded.Should().Equal("AB"u8.ToArray());
        reached.Should().Be(limited);
        AsciiHexFilter.Decode("41424"u8, out _, out _, 3).Should().Equal([0x41, 0x42, 0x40]);
    }

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

        PredictorTransform.TryApply(data, predictor, colors: 1, bitsPerComponent: 8, columns: 4, cut: false, out var result, out _).Should().BeTrue();

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

    [Theory]
    [InlineData("RunLengthDecode")]
    [InlineData("LZWDecode")]
    [InlineData("ASCII85Decode")]
    [InlineData("FlateDecode")]
    public void A_filter_s_first_buffer_is_sized_by_its_bound_not_by_its_input(string filter)
    {
        // Each decoder guesses its output from its input, a length the file chose: two to four times
        // several megabytes here. The guess is capped by the bound, a kilobyte.
        var encoded = LargeInput(filter);
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

    [Theory]
    [InlineData("text", 1)]
    [InlineData("text", 0)]
    [InlineData("incompressible", 1)]
    [InlineData("a run filling the table", 1)]
    public void Decodes_lzw_data_back_to_what_was_encoded_across_clear_codes_and_its_longest_entries(string data, int earlyChange)
    {
        // Text and random bytes, a megabyte each, are written with a clear code each time the table fills. A run of one byte
        // fills the table with no clear code: its codes name entries of 2 to 3,839 bytes, the longest a table holds — the
        // last defined by naming it as it is defined —, then name the longest again.
        var plain = LzwData(data);
        var parameters = new PdfDictionary();
        parameters.Set(PdfName.EarlyChange, PdfInteger.Create(earlyChange));
        var diagnostics = new PdfDiagnostics();

        var decoded = Decode(FilterDamageTests.LzwEncode(plain, earlyChange, clearWhenFull: data != "a run filling the table"), PdfName.LZWDecode, parameters, diagnostics);

        decoded.Span.SequenceEqual(plain).Should().BeTrue();
        diagnostics.Should().BeEmpty();
    }

    [Theory]
    [InlineData("text")]
    [InlineData("incompressible")]
    [InlineData("a run filling the table")]
    public void An_lzw_decode_allocates_its_output_and_nothing_for_each_code(string data)
    {
        // Each code names an entry of the table, which the decoder writes out without an array of its own: what a decode
        // allocates is what the bounded output allocates for the same bytes, from the same first guess, its table rented.
        var plain = LzwData(data);
        var encoded = FilterDamageTests.LzwEncode(plain, clearWhenFull: data != "a run filling the table");
        const int Bound = 16 * 1024 * 1024;

        var decoding = Allocations.Least(() => LzwFilter.Decode(encoded, 1, out _, out _, out _, Bound), out var decoded);
        var writing = Allocations.Least(
            () =>
            {
                var output = new PdfBoundedOutput(Math.Max(1024, encoded.Length * 3L), Bound);
                foreach (var value in plain)
                {
                    output.TryWrite([value]);
                }

                return output.ToArray();
            },
            out _);

        decoded.Should().Equal(plain);
        decoding.Should().BeLessThanOrEqualTo(writing);
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

    [Theory]
    [InlineData(2, 32, 8, 16_777_216)]
    [InlineData(12, 32, 16, 5_000_000)]
    [InlineData(12, 4, 8, 60_000_000)]
    [InlineData(12, int.MaxValue, 16, int.MaxValue)]
    public async Task Leaves_the_data_as_decoded_when_the_predictor_describes_rows_it_cannot_hold(
        int predictor, int colors, int bitsPerComponent, int columns)
    {
        // Parameters from the file: the first row length overflowed to zero and looped for ever, the
        // second to a negative size and threw, the third allocated two rows of 240 MB for twelve bytes. The
        // fourth's bits run past what a long holds, /Colors bounded by nothing.
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
                "The /RunLengthDecode data decodes to more than 10000 bytes; decoding stopped there. " +
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
            "The /FlateDecode data decodes to more than 10000 bytes; decoding stopped there. " +
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
                "shared" => [$"The filters before the last decode to more than {bound} bytes together; decoding stopped in the /ASCIIHexDecode data. Raise PdfReaderLimits.MaxDecodedStreamLength to read past it."],
                "its own" => [$"The /ASCIIHexDecode data decodes to more than {bound} bytes; decoding stopped there. Raise PdfReaderLimits.MaxDecodedStreamLength to read past it."],
                _ => [],
            });
    }

    [Theory]
    [InlineData("/ASCIIHexDecode /FlateDecode")]
    [InlineData("/Crypt /ASCIIHexDecode /FlateDecode")]
    [InlineData("/Crypt /Crypt /ASCIIHexDecode /FlateDecode")]
    [InlineData("/ASCIIHexDecode /FlateDecode /Crypt")]
    [InlineData("/ASCIIHexDecode /Crypt /FlateDecode")]
    public void A_crypt_filter_is_neither_charged_to_the_bound_the_filters_share_nor_the_last(string filters)
    {
        // Nine hundred bytes compressed to about as many and hex-encoded, under a bound of a thousand: the hex step
        // passes along what the Flate step decodes alone. A /Crypt filter passes its data along as it is, and holds
        // nothing more: charged, the 1,800 hex digits it passes would leave the hex step nothing, and as the last, it
        // would leave the Flate step what the hex step left of the shared bound.
        var plain = new byte[900];
        new Random(12).NextBytes(plain);
        var hex = Encoding.ASCII.GetBytes(Convert.ToHexString(Compress(plain)) + ">");
        var dictionary = new PdfObjectParser(Encoding.ASCII.GetBytes($"<< /Filter [{filters}] >>")).ParseObject().AsDictionary().Required();
        var diagnostics = new PdfDiagnostics();

        var decoded = PdfFilterPipeline.Decode(new PdfStream(dictionary, PdfStreamData.FromMemory(hex)), diagnostics, Within(1_000));

        decoded.ToArray().Should().Equal(plain);
        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void Reads_the_filters_a_chain_names_through_references_in_their_order_crypt_filters_among_them()
    {
        // The name each reference leads to is kept in the order the chain names them; a /Crypt filter named through one
        // is passed over in its turn, and the hex filters after it read theirs.
        var source = Substitute.For<IPdfObjectSource>();
        source.GetObject(new PdfObjectId(9)).Returns(PdfName.Crypt);
        source.GetObject(new PdfObjectId(10)).Returns(PdfName.ASCIIHexDecode);
        var dictionary = new PdfObjectParser(
            "<< /Filter [9 0 R 10 0 R /Crypt 9 0 R /ASCIIHexDecode 10 0 R 9 0 R] >>"u8.ToArray(),
            source: source).ParseObject().AsDictionary().Required();
        var encoded = "Hi"u8.ToArray();

        for (var layer = 0; layer < 3; layer++)
        {
            encoded = Encoding.ASCII.GetBytes(Convert.ToHexString(encoded) + ">");
        }

        var diagnostics = new PdfDiagnostics();

        var decoded = new PdfStream(dictionary, PdfStreamData.FromMemory(encoded)).Decode(diagnostics);

        Text(decoded).Should().Be("Hi");
        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void Reads_nothing_past_the_most_filters_the_guard_lets_a_stream_pass_through()
    {
        // Three hex layers under a guard of two: the third element, a reference, is not read, and the data comes back as
        // the two decoded it, with the guard reported.
        var source = Substitute.For<IPdfObjectSource>();
        var dictionary = new PdfObjectParser("<< /Filter [/ASCIIHexDecode /ASCIIHexDecode 9 0 R] /DecodeParms [null null 9 0 R] >>"u8.ToArray(), source: source)
            .ParseObject().AsDictionary().Required();
        var encoded = "Hi"u8.ToArray();

        for (var layer = 0; layer < 3; layer++)
        {
            encoded = Encoding.ASCII.GetBytes(Convert.ToHexString(encoded) + ">");
        }

        var guard = new PdfLimitGuard(PdfReaderLimits.Default with { MaxFilterChainLength = 2 }, throwOnLimit: false);
        var diagnostics = new PdfDiagnostics();

        var decoded = PdfFilterPipeline.Decode(new PdfStream(dictionary, PdfStreamData.FromMemory(encoded)), diagnostics, guard);

        Text(decoded).Should().Be("4869>");
        source.ReceivedCalls().Should().BeEmpty();
        var report = diagnostics.Should().ContainSingle().Which;
        report.Code.Should().Be(PdfDiagnosticCodes.LimitFilterChain);
        report.Message.Should().Be(
            "The /Filter entry names more than 2 filters; decoding stopped after the first 2. " +
            "Raise PdfReaderLimits.MaxFilterChainLength to read past it.");
    }

    [Theory]
    [InlineData(63, true)]
    [InlineData(64, false)]
    public void Says_a_stream_is_an_image_only_when_its_image_filter_stands_within_the_guard(int crypts, bool image)
    {
        // The guard counts every filter the array names, /Crypt among them: an image filter past the 64th is never
        // reached, and decoding would stop before it.
        var dictionary = new PdfObjectParser(
            Encoding.ASCII.GetBytes("<< /Filter [" + string.Concat(Enumerable.Repeat("/Crypt ", crypts)) + "/DCTDecode] >>"))
            .ParseObject().AsDictionary().Required();
        var stream = new PdfStream(dictionary, PdfStreamData.FromMemory(new byte[] { 0xFF, 0xD8 }));

        stream.HasImageFilter().Should().Be(image);
    }

    [Theory]
    [InlineData("/Predictor 12 /Columns 4", "0001020304020101", "010203040203", "with 3 bytes of its 5")]
    [InlineData("/Predictor 2 /Columns 4", "0A0505051401 01", "0A0F1419141516", "with 3 bytes of its 4")]
    [InlineData("/Predictor 2 /BitsPerComponent 16 /Columns 2", "00010001000500", "00010002000500", "with 3 bytes of its 4")]
    [InlineData("/Predictor 2 /BitsPerComponent 4 /Columns 3", "123001", "136001", "with 1 byte of its 2")]
    public void Keeps_the_partial_last_row_of_data_the_reader_did_not_cut_undone_as_far_as_it_goes_and_reports_it(
        string parameters, string predicted, string expected, string held)
    {
        // Whole rows, then the part of a last one, as the file wrote them: what a predictor predicts is predicted from bytes
        // before it, and the part is undone as far as it goes — a 16-bit sample it holds one byte of is no sample — as of
        // data a guard cut. The Flate data ended whole: the part is the file's, and reported.
        var dictionary = new PdfObjectParser(Encoding.ASCII.GetBytes($"<< /Filter /FlateDecode /DecodeParms << {parameters} >> >>"))
            .ParseObject().AsDictionary().Required();
        var diagnostics = new PdfDiagnostics();
        var data = Convert.FromHexString(predicted.Replace(" ", string.Empty, StringComparison.Ordinal));

        var decoded = new PdfStream(dictionary, PdfStreamData.FromMemory(Compress(data))).Decode(diagnostics);

        Convert.ToHexString(decoded.Span).Should().Be(expected);
        var report = diagnostics.Should().ContainSingle().Which;
        (report.Code, report.Severity).Should().Be((PdfDiagnosticCodes.FilterFailed, PdfDiagnosticSeverity.Warning));
        report.Message.Should().Be($"The predicted data ends inside a row, {held}; what the row holds was kept, undone as far as it goes.");
    }

    [Fact]
    public void Reports_a_last_png_row_the_data_holds_only_the_filter_type_of_as_keeping_nothing_of_it()
    {
        // A whole row, then the byte that would name the next row's filter type: the next row holds no byte to undo or keep.
        var dictionary = new PdfObjectParser("<< /Filter /FlateDecode /DecodeParms << /Predictor 12 /Columns 4 >> >>"u8.ToArray())
            .ParseObject().AsDictionary().Required();
        var diagnostics = new PdfDiagnostics();

        var decoded = new PdfStream(dictionary, PdfStreamData.FromMemory(Compress(Convert.FromHexString("000102030402")))).Decode(diagnostics);

        Convert.ToHexString(decoded.Span).Should().Be("01020304");
        var report = diagnostics.Should().ContainSingle().Which;
        (report.Code, report.Severity, report.Message).Should().Be((
            PdfDiagnosticCodes.FilterFailed,
            PdfDiagnosticSeverity.Warning,
            "The predicted data ends with the byte that names a row's filter type, and none of the row's bytes; nothing of it was kept."));
    }

    [Theory]
    [InlineData("Flate, its checksum missing")]
    [InlineData("Flate, its checksum wrong")]
    [InlineData("LZW, its end code")]
    [InlineData("LZW, no end code")]
    [InlineData("LZW, inside a code")]
    [InlineData("LZW, a code it has not defined")]
    public void Reports_a_partial_last_row_where_its_filter_decoded_the_data_to_an_end_that_loses_nothing(string data)
    {
        // Two PNG-predicted rows of two bytes, then a third's filter type and first byte. Flate data whose deflate body ends,
        // its checksum missing or wrong, and LZW data that ends at its end code, or at the end of its last code without
        // one, lose nothing: the part of the row is the file's, and reported. LZW data that ends inside a code, or at a code
        // it has not defined, may have lost what follows, which its report says: the part of the row is not reported again.
        byte[] predicted = [0, 1, 2, 2, 1, 1, 2, 1];
        int[] codes = [.. predicted.Select(value => (int)value)];
        byte[] encoded = data switch
        {
            "Flate, its checksum missing" => Compress(predicted)[..^4],
            "Flate, its checksum wrong" => [.. Compress(predicted)[..^1], (byte)(Compress(predicted)[^1] ^ 0xFF)],
            "LZW, its end code" => NineBitCodes([.. codes, 257]),
            "LZW, no end code" => NineBitCodes(codes),
            "LZW, inside a code" => [.. NineBitCodes(codes), 0xFF],
            _ => NineBitCodes([.. codes, 300]),
        };
        var dictionary = Dictionary($"/Filter /{(data.StartsWith("LZW", StringComparison.Ordinal) ? "LZWDecode" : "FlateDecode")} /DecodeParms << /Predictor 12 /Columns 2 >>");
        const string Partial = "The predicted data ends inside a row, with 2 bytes of its 3; what the row holds was kept, undone as far as it goes.";
        (string Code, string Message)[] reports = data switch
        {
            "Flate, its checksum missing" =>
                [(PdfDiagnosticCodes.FilterFailed, "A Flate stream ends before its checksum does; its data decoded whole, unchecked."), (PdfDiagnosticCodes.FilterFailed, Partial)],
            "Flate, its checksum wrong" =>
                [(PdfDiagnosticCodes.FilterChecksumMismatch, "A Flate stream's checksum disagrees with the 8 bytes its data decoded to; all were kept, and some may be wrong."), (PdfDiagnosticCodes.FilterFailed, Partial)],
            "LZW, its end code" => [(PdfDiagnosticCodes.FilterFailed, Partial)],
            "LZW, no end code" =>
                [(PdfDiagnosticCodes.FilterFailed, "An LZW stream ends without its end-of-data code; it decoded to the end of its data."), (PdfDiagnosticCodes.FilterFailed, Partial)],
            "LZW, inside a code" =>
                [(PdfDiagnosticCodes.FilterFailed, "An LZW stream ends without its end-of-data code, inside a code, whose bits were dropped; data may have been lost.")],
            _ => [(PdfDiagnosticCodes.FilterFailed, "An LZW stream uses code 300, which it has not defined; what decoded before it was kept.")],
        };
        var diagnostics = new PdfDiagnostics();

        var decoded = new PdfStream(dictionary, PdfStreamData.FromMemory(encoded)).Decode(diagnostics);

        Convert.ToHexString(decoded.Span).Should().Be("0102020303");
        diagnostics.Select(d => (d.Code, d.Message)).Should().Equal(reports);
    }

    [Theory]
    [InlineData("00010203040501010101", "0102030401010101", "Row 2 of 2 PNG-predicted rows names filter type 5, which PNG does not define; it was read as unfiltered.")]
    [InlineData("0001020304FF010101010902020202", "010203040101010102020202", "2 of 3 PNG-predicted rows name a filter type PNG does not define, the first row 2 type 255; they were read as unfiltered.")]
    public void Reads_a_png_row_whose_filter_type_png_does_not_define_as_unfiltered_and_reports_it(string predicted, string expected, string message)
    {
        // MuPDF, qpdf and PDFium read such a row as it is, rather than abandon the stream; the report counts them. Decoded
        // with nowhere to report to, the stream reads the same.
        var dictionary = new PdfObjectParser("<< /Filter /FlateDecode /DecodeParms << /Predictor 12 /Columns 4 >> >>"u8.ToArray())
            .ParseObject().AsDictionary().Required();
        var diagnostics = new PdfDiagnostics();
        var stream = new PdfStream(dictionary, PdfStreamData.FromMemory(Compress(Convert.FromHexString(predicted))));

        var decoded = stream.Decode(diagnostics);

        Convert.ToHexString(decoded.Span).Should().Be(expected);
        var report = diagnostics.Should().ContainSingle().Which;
        (report.Code, report.Severity, report.Message).Should().Be((PdfDiagnosticCodes.FilterFailed, PdfDiagnosticSeverity.Warning, message));
        Convert.ToHexString(stream.Decode().Span).Should().Be(expected);
    }

    [Theory]
    [InlineData(
        "02",
        "Row 2 of 100 PNG-predicted rows names filter type 5, which PNG does not define; it was read as unfiltered.",
        "Row 2 of the PNG-predicted rows in the part of the data read names filter type 5, which PNG does not define; it was read as unfiltered.")]
    [InlineData(
        "FF",
        "2 of 100 PNG-predicted rows name a filter type PNG does not define, the first row 2 type 5; they were read as unfiltered.",
        "2 PNG-predicted rows in the part of the data read name a filter type PNG does not define, the first row 2 type 5; they were read as unfiltered.")]
    public void Counts_png_rows_whose_filter_type_png_does_not_define_in_the_part_of_the_data_a_guard_let_be_read(
        string thirdTag, string whole, string cut)
    {
        // A hundred rows, the second of type 5 and the third of type 255 or Up, stored in zlib and written in hex. The
        // reader reads 200 hex digits of it under MaxStreamLength; the hex filter stops at 150 bytes under
        // MaxDecodedStreamLength. Either way the rows those types name lie in the part read, which holds fewer rows than
        // the data: the report counts that part's, as it says, and reads the same however much of the data was taken.
        var rows = new byte[100 * 5];
        rows.AsSpan().Fill(1);
        rows[0] = 0;
        rows[5] = 5;
        rows[10] = Convert.FromHexString(thirdTag)[0];

        for (var row = 3; row < 100; row++)
        {
            rows[row * 5] = 2;
        }

        var hex = Convert.ToHexString(Compress(rows, CompressionLevel.NoCompression)) + ">";
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .Stream(3, "/Filter [/ASCIIHexDecode /FlateDecode] /DecodeParms [null << /Predictor 12 /Columns 4 >>]", hex)
            .BuildClassic(rootNumber: 1);

        Reports(PdfReaderLimits.Default).Should().Equal(
            (PdfDiagnosticCodes.FilterFailed, whole));
        Reports(PdfReaderLimits.Default with { MaxStreamLength = 200 }).Should().Equal(
            (PdfDiagnosticCodes.LimitStreamLength, null), (PdfDiagnosticCodes.FilterFailed, cut));
        Reports(PdfReaderLimits.Default with { MaxDecodedStreamLength = 150 }).Should().Equal(
            (PdfDiagnosticCodes.LimitDecodedStream, null), (PdfDiagnosticCodes.FilterFailed, cut));

        (string Code, string? Message)[] Reports(PdfReaderLimits limits)
        {
            using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = limits });
            document.GetObject(new PdfObjectId(3)).AsStream().Required().Decode(document.Diagnostics);
            return [.. document.Diagnostics.Select(d => (d.Code, d.Code == PdfDiagnosticCodes.FilterFailed ? d.Message : null))];
        }
    }

    [Fact]
    public void Reads_a_stream_s_data_from_the_file_once_every_value_its_filters_read_is_loaded()
    {
        // A stream whose Flate step takes its parameters from an object an object stream holds: decoding loads that
        // object stream, and reads the stream's own data from the file only once it has, so that the data is not held
        // while the object stream decodes (#165).
        byte[] rows = [2, 1, 2, 3, 4, 2, 1, 1, 1, 1];
        var hex = Convert.ToHexString(Compress(rows)) + ">";
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .Stream(5, "/Filter [/ASCIIHexDecode /FlateDecode] /DecodeParms [null 12 0 R]", hex)
            .WithObject(12, "<< /Predictor 12 /Columns 4 >>")
            .BuildWithXRefStream(rootNumber: 1, compressedObjects: [12], compressObjectStream: true);
        var source = new RecordingSource(file);
        using var document = PdfDocument.Open(source, PdfReaderOptions.Default, ownsSource: true);
        var stream = document.GetObject(new PdfObjectId(5)).AsStream().Required();
        source.Reads.Clear();

        var decoded = stream.Decode(document.Diagnostics);

        decoded.ToArray().Should().Equal(1, 2, 3, 4, 2, 3, 4, 5);
        document.Diagnostics.Should().BeEmpty();
        source.Reads.Should().HaveCountGreaterThan(1, "the object stream is read from the file too").And.EndWith(stream.Data.Position);
        source.Reads.Count(offset => offset == stream.Data.Position).Should().Be(1);
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
    [InlineData(1, 1, 8, "C0FF", "80AA")]
    [InlineData(1, 1, 5, "C7", "87")]
    [InlineData(1, 1, 9, "C080C080", "80808080")]
    [InlineData(2, 1, 4, "5540", "6C55")]
    [InlineData(4, 2, 2, "1234", "1246")]
    [InlineData(4, 1, 3, "123F", "136F")]
    [InlineData(16, 1, 2, "01000005", "01000105")]
    [InlineData(16, 1, 2, "00FF0001", "00FF0100")]
    [InlineData(16, 1, 2, "FFFF0002", "FFFF0001")]
    [InlineData(16, 2, 2, "0001000200030004", "0001000200040006")]
    public void Undoes_a_tiff_predictor_over_components_of_every_width_pdf_allows(
        int bitsPerComponent, int colors, int columns, string predicted, string expected)
    {
        // ISO 32000-1 (Table 8) allows 1, 2, 4, 8 and 16 bits. Each sample adds the one of its component before it in its
        // row, modulo 2 to its width (TIFF 6.0, section 14): packed from the high bit below a byte, a row rounded up to whole
        // bytes — nine samples of one bit take two —, the bits that pad it to its last byte left alone, and big-endian at 16
        // bits, a carry crossing from the low byte to the high one.
        var dictionary = new PdfObjectParser(Encoding.ASCII.GetBytes(string.Create(
            CultureInfo.InvariantCulture,
            $"<< /Filter /FlateDecode /DecodeParms << /Predictor 2 /BitsPerComponent {bitsPerComponent} /Colors {colors} /Columns {columns} >> >>")))
            .ParseObject().AsDictionary().Required();
        var diagnostics = new PdfDiagnostics();

        var decoded = new PdfStream(dictionary, PdfStreamData.FromMemory(Compress(Convert.FromHexString(predicted)))).Decode(diagnostics);

        Convert.ToHexString(decoded.Span).Should().Be(expected);
        diagnostics.Should().BeEmpty();
    }

    [Theory]
    [InlineData(11, 3, 4, 2, "01102030", "102040")]
    [InlineData(13, 3, 4, 2, "03102030", "102038")]
    [InlineData(11, 5, 2, 4, "010102030405", "0102040609")]
    [InlineData(11, 1, 1, 16, "011020", "1030")]
    [InlineData(12, 40, 8, 1, "{forty}", "{forty undone}")]
    public void Counts_a_png_pixel_s_bytes_rounded_up_whatever_its_components(
        int predictor, int colors, int bitsPerComponent, int columns, string predicted, string expected)
    {
        // A pixel of three 4-bit components is 12 bits, two bytes as PNG counts them, its left neighbour two bytes back; one
        // of five 2-bit components, 10 bits, two bytes too; one of a single bit, a byte. ISO 32000-1 sets no bound on
        // /Colors: forty components, two rows of a pixel each, the second predicted from the first, decode whole (#162).
        var forty = Enumerable.Range(1, 40).Select(value => (byte)value).ToArray();
        var data = predicted == "{forty}" ? [0, .. forty, 2, .. Enumerable.Repeat((byte)1, 40)] : Convert.FromHexString(predicted);
        var output = expected == "{forty undone}" ? Convert.ToHexString([.. forty, .. forty.Select(value => (byte)(value + 1))]) : expected;
        var dictionary = new PdfObjectParser(Encoding.ASCII.GetBytes(string.Create(
            CultureInfo.InvariantCulture,
            $"<< /Filter /FlateDecode /DecodeParms << /Predictor {predictor} /Colors {colors} /BitsPerComponent {bitsPerComponent} /Columns {columns} >> >>")))
            .ParseObject().AsDictionary().Required();
        var diagnostics = new PdfDiagnostics();

        var decoded = new PdfStream(dictionary, PdfStreamData.FromMemory(Compress(data))).Decode(diagnostics);

        Convert.ToHexString(decoded.Span).Should().Be(output);
        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void Undoes_the_part_of_a_png_row_a_guard_cut_whose_pixel_outgrows_an_int_as_bytes_without_a_left_neighbour()
    {
        // A pixel of 2,147,483,647 components of 16 bits is 4,294,967,294 bytes, more than an int holds, in a row longer than
        // any data. MaxStreamLength lets the reader read 100 of the hexadecimal digits: the 42 bytes they hold after the
        // row's filter type, Sub, are undone as far as they go, none of them with a left neighbour (#162).
        byte[] rows = [1, .. Enumerable.Repeat((byte)5, 99)];
        var hex = Convert.ToHexString(Compress(rows, CompressionLevel.NoCompression)) + ">";
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .Stream(3, "/Filter [/ASCIIHexDecode /FlateDecode] /DecodeParms [null << /Predictor 12 /Colors 2147483647 /BitsPerComponent 16 >>]", hex)
            .BuildClassic(rootNumber: 1);
        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxStreamLength = 100 } });

        var decoded = document.GetObject(new PdfObjectId(3)).AsStream().Required().Decode(document.Diagnostics);

        decoded.ToArray().Should().Equal(Enumerable.Repeat((byte)5, 42));
        document.Diagnostics.Select(d => d.Code).Should().Equal(PdfDiagnosticCodes.LimitStreamLength);
    }

    [Theory]
    [InlineData("/Predictor 3 /Columns 4", Unpredicted, "The /DecodeParms entry gives /Predictor 3, which ISO 32000-1 does not allow; it was read as 1, no predictor.")]
    [InlineData("/Predictor 9 /Columns 4", Unpredicted, "The /DecodeParms entry gives /Predictor 9, which ISO 32000-1 does not allow; it was read as 1, no predictor.")]
    [InlineData("/Predictor 0 /Columns 4", Unpredicted, "The /DecodeParms entry gives /Predictor 0, which ISO 32000-1 does not allow; it was read as 1, no predictor.")]
    [InlineData("/Predictor -12 /Columns 4", Unpredicted, "The /DecodeParms entry gives /Predictor -12, which ISO 32000-1 does not allow; it was read as 1, no predictor.")]
    [InlineData("/Predictor 16 /Columns 4", Undone, "The /DecodeParms entry gives /Predictor 16, which ISO 32000-1 does not allow; it was read as a PNG predictor.")]
    [InlineData("/Predictor 4294967298 /Columns 4", Undone, "The /DecodeParms entry gives /Predictor 4294967298, which ISO 32000-1 does not allow; it was read as a PNG predictor.")]
    [InlineData("/Predictor 12 /BitsPerComponent 0 /Columns 4", Undone, "The /DecodeParms entry gives /BitsPerComponent 0, which ISO 32000-1 does not allow; it was read as 8.")]
    [InlineData("/Predictor 12 /BitsPerComponent 7 /Columns 4", Undone, "The /DecodeParms entry gives /BitsPerComponent 7, which ISO 32000-1 does not allow; it was read as 8.")]
    [InlineData("/Predictor 12 /BitsPerComponent 32 /Columns 4", Undone, "The /DecodeParms entry gives /BitsPerComponent 32, which ISO 32000-1 does not allow; it was read as 8.")]
    [InlineData("/Predictor 12 /BitsPerComponent 4294967304 /Columns 4", Undone, "The /DecodeParms entry gives /BitsPerComponent 4294967304, which ISO 32000-1 does not allow; it was read as 8.")]
    [InlineData("/Predictor 12 /Colors 0 /Columns 4", Undone, "The /DecodeParms entry gives /Colors 0, which ISO 32000-1 does not allow; it was read as 1.")]
    [InlineData("/Predictor 12 /Colors -3 /Columns 4", Undone, "The /DecodeParms entry gives /Colors -3, which ISO 32000-1 does not allow; it was read as 1.")]
    [InlineData("/Predictor 12 /Columns -4", ColumnOfOne, "The /DecodeParms entry gives /Columns -4, which ISO 32000-1 does not allow; it was read as 1.")]
    [InlineData("/Predictor 12 /Columns -4294967300", ColumnOfOne, "The /DecodeParms entry gives /Columns -4294967300, which ISO 32000-1 does not allow; it was read as 1.")]
    [InlineData("/Predictor 12.5 /Columns 4", Unpredicted, "The /DecodeParms entry gives /Predictor as a real number, where ISO 32000-1 wants an integer; it was read as absent, which gives 1, no predictor.")]
    [InlineData("/Predictor /Twelve /Columns 4", Unpredicted, "The /DecodeParms entry gives /Predictor as a name, where ISO 32000-1 wants an integer; it was read as absent, which gives 1, no predictor.")]
    [InlineData("/Predictor 12 /Columns /Four", ColumnOfOne, "The /DecodeParms entry gives /Columns as a name, where ISO 32000-1 wants an integer; it was read as absent, which gives 1.")]
    [InlineData("/Predictor 12 /BitsPerComponent true /Columns 4", Undone, "The /DecodeParms entry gives /BitsPerComponent as a boolean, where ISO 32000-1 wants an integer; it was read as absent, which gives 8.")]
    [InlineData("/Predictor 12 /Colors 0 /Columns -4", ColumnOfOne, "The /DecodeParms entry gives /Colors 0, which ISO 32000-1 does not allow; it was read as 1.|The /DecodeParms entry gives /Columns -4, which ISO 32000-1 does not allow; it was read as 1.")]
    [InlineData("/Predictor 7 /Colors 0 /Columns -4", Unpredicted, "The /DecodeParms entry gives /Predictor 7, which ISO 32000-1 does not allow; it was read as 1, no predictor.")]
    [InlineData("/Predictor 12.0 /Columns 4.0", Undone, "")]
    [InlineData("/Predictor 15 /Colors 1 /BitsPerComponent 8 /Columns 4", Undone, "")]
    public void Reads_a_predictor_s_parameter_pdf_does_not_allow_as_other_readers_do_and_reports_each_value_it_replaced(
        string parameters, string expected, string reports)
    {
        // ISO 32000-1 (Table 8) allows a /Predictor of 1, 2 or 10 to 15, a /BitsPerComponent of 1, 2, 4, 8 or 16, and a
        // /Colors and /Columns of 1 or more. Each value past it is read as PDFium, MuPDF and qpdf read it, and reported: a
        // predictor read as none leaves the other values unread, and a value past what an int holds is the int nearest it.
        var dictionary = Dictionary($"/Filter /FlateDecode /DecodeParms << {parameters} >>");

        AssertDecodes(dictionary, expected, reports);
    }

    [Theory]
    [InlineData("/Filter /FlateDecode /DecodeParms 5", Unpredicted, "The /DecodeParms entry is an integer, neither a dictionary nor an array; the filter decodes with no parameters.")]
    [InlineData("/Filter [/ASCIIHexDecode /FlateDecode] /DecodeParms (x)", Unpredicted, "The /DecodeParms entry is a string, neither a dictionary nor an array; the filters decode with no parameters.")]
    [InlineData("/Filter [/FlateDecode] /DecodeParms [5]", Unpredicted, "Element 0 of the /DecodeParms array is an integer, neither a dictionary nor null; the /FlateDecode filter it is for decodes with no parameters.")]
    [InlineData("/Filter [/ASCIIHexDecode /FlateDecode] /DecodeParms [null]", Unpredicted, "The /DecodeParms array holds no element for the /FlateDecode filter at element 1 of the /Filter entry; it decodes with no parameters.")]
    [InlineData("/Filter /FlateDecode /DecodeParms []", Unpredicted, "The /DecodeParms array holds no element for the /FlateDecode filter; it decodes with no parameters.")]
    [InlineData("/Filter [/FlateDecode /Crypt] /DecodeParms [null]", Unpredicted, "The /DecodeParms array holds no element for the /Crypt filter at element 1 of the /Filter entry; it decodes with no parameters.")]
    [InlineData("/Filter [/FlateDecode] /DecodeParms [<< /Predictor 7 /Columns 4 >>]", Unpredicted, "Element 0 of the /DecodeParms array gives /Predictor 7, which ISO 32000-1 does not allow; it was read as 1, no predictor.")]
    [InlineData("/Filter [/FlateDecode /FlateDecode] /DecodeParms << /Predictor 7 >>", Unpredicted, "The /DecodeParms entry gives /Predictor 7, which ISO 32000-1 does not allow; it was read as 1, no predictor.")]
    [InlineData("/Filter [/FlateDecode /LZWDecode] /DecodeParms << /Predictor 7 /EarlyChange 2 >>", Unpredicted, "The /DecodeParms entry gives /Predictor 7, which ISO 32000-1 does not allow; it was read as 1, no predictor.|The /DecodeParms entry gives /EarlyChange 2, which ISO 32000-1 does not allow; it was read as 1.")]
    [InlineData("/Filter [/LZWDecode /FlateDecode] /DecodeParms << /Predictor 7 >>", Unpredicted, "The /DecodeParms entry gives /Predictor 7, which ISO 32000-1 does not allow; it was read as 1, no predictor.")]
    [InlineData("/Filter [/FlateDecode /FlateDecode] /DecodeParms [<< /Predictor 7 >> << /Predictor 9 >>]", Unpredicted, "Element 0 of the /DecodeParms array gives /Predictor 7, which ISO 32000-1 does not allow; it was read as 1, no predictor.|Element 1 of the /DecodeParms array gives /Predictor 9, which ISO 32000-1 does not allow; it was read as 1, no predictor.")]
    [InlineData("/Filter /FlateDecode /DecodeParms [null]", Unpredicted, "")]
    [InlineData("/Filter /FlateDecode /DecodeParms null", Unpredicted, "")]
    [InlineData("/Filter /FlateDecode /DecodeParms 9 0 R", Unpredicted, "")]
    [InlineData("/Filter [/FlateDecode] /DecodeParms [<< /Predictor 12 /Columns 4 >> null]", Undone, "")]
    [InlineData("/Filter /ASCIIHexDecode /DecodeParms 5", Unpredicted, "")]
    public void Reports_a_filter_the_shape_of_the_parameters_leaves_without_any_and_no_other(string entries, string expected, string reports)
    {
        // A /DecodeParms neither a dictionary nor an array, an element neither a dictionary nor null, and an array too short
        // to reach a filter that reads parameters, leave it none, where the file may have meant some. A null element, a
        // /DecodeParms that leads to null, as a reference to an object nothing holds does, an array longer than /Filter, a
        // dictionary that serves every step, and parameters of a filter that reads none, replace nothing; a value a
        // dictionary gives every step is reported once, whichever kind of step reads it first.
        AssertDecodes(Dictionary(entries), expected, reports);
    }

    [Theory]
    [InlineData("/Filter [/Crypt /FlateDecode] /DecodeParms [<< /Type /CryptFilterDecodeParms /Name /StdCF >> null]", "Element 0 of the /DecodeParms array names the crypt filter /StdCF, which no /Encrypt dictionary defines; the /Crypt filter passed its data along as it is.")]
    [InlineData("/Filter /Crypt /DecodeParms << /Name /StdCF >>", "The /DecodeParms entry names the crypt filter /StdCF, which no /Encrypt dictionary defines; the /Crypt filter passed its data along as it is.")]
    [InlineData("/Filter [/Crypt /FlateDecode] /DecodeParms [<< /Name (StdCF) >> null]", "Element 0 of the /DecodeParms array gives /Name as a string, where ISO 32000-1 wants a name; it was read as absent, which gives the identity crypt filter.")]
    [InlineData("/Filter [/Crypt /Crypt /FlateDecode] /DecodeParms << /Name /StdCF >>", "The /DecodeParms entry names the crypt filter /StdCF, which no /Encrypt dictionary defines; the /Crypt filter passed its data along as it is.")]
    [InlineData("/Filter [/Crypt /FlateDecode] /DecodeParms [<< /Name /Identity >> null]", "")]
    [InlineData("/Filter [/Crypt /FlateDecode] /DecodeParms [<< /Name 9 0 R >> null]", "")]
    [InlineData("/Filter [/Crypt /FlateDecode]", "")]
    [InlineData("/Filter [/Crypt] /DecodeParms [<< /Type /CryptFilterDecodeParms >>]", "")]
    public void Passes_the_data_of_a_crypt_filter_along_and_reports_one_that_names_a_crypt_filter_nothing_defines(string entries, string reports)
    {
        // A stream built in memory belongs to no document, and no encryption dictionary defines a crypt filter for it: the
        // identity filter, named or not — a /Name that leads to null, as a reference to an object nothing holds does, names
        // none —, passes its data along in silence, and any other is passed along so, reported once however many /Crypt
        // filters a dictionary of parameters serves.
        AssertDecodes(Dictionary(entries), Unpredicted, reports);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Reports_a_named_crypt_filter_only_in_a_document_whose_trailer_gives_no_encryption(bool encrypted)
    {
        // In a document that gives /Encrypt the name is the security handler's to read, which decryption reads (M16): the
        // data passes along as it is, unreported. In one that gives none, nothing defines the filter. A value the stream's
        // parameters give that ISO 32000-1 does not allow is reported either way.
        const string Crypt = "Element 0 of the /DecodeParms array names the crypt filter /StdCF, which no /Encrypt dictionary defines; the /Crypt filter passed its data along as it is.";
        const string Predictor = "Element 2 of the /DecodeParms array gives /Predictor 7, which ISO 32000-1 does not allow; it was read as 1, no predictor.";
        var hex = Convert.ToHexString(Compress("plain"u8.ToArray())) + ">";
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .Stream(3, "/Filter [/Crypt /ASCIIHexDecode /FlateDecode] /DecodeParms [<< /Name /StdCF >> null << /Predictor 7 >>]", hex)
            .WithObject(9, "<< /Filter /Standard /V 4 /R 4 /Length 128 /O <00> /U <00> /P -4 /CF << /StdCF << /CFM /AESV2 >> >> /StmF /StdCF /StrF /StdCF >>")
            .BuildClassic(rootNumber: 1);
        var text = Encoding.Latin1.GetString(file);
        file = Encoding.Latin1.GetBytes(encrypted ? text.Replace("/Root 1 0 R >>", "/Root 1 0 R /Encrypt 9 0 R >>", StringComparison.Ordinal) : text);

        using var document = PdfDocument.Open(file, PdfReaderOptions.Default with { ThrowOnEncrypted = false });
        var stream = document.GetObject(new PdfObjectId(3)).AsStream().Required();
        var decoded = stream.Decode(document.Diagnostics);

        document.IsEncrypted.Should().Be(encrypted);
        Text(decoded).Should().Be("plain");
        document.Diagnostics.Select(d => (d.Code, d.Message, d.Position)).Should().Equal(
            encrypted
                ? [(PdfDiagnosticCodes.FilterParametersInvalid, Predictor, stream.Data.Position)]
                : [(PdfDiagnosticCodes.FilterParametersInvalid, Crypt, stream.Data.Position), (PdfDiagnosticCodes.FilterParametersInvalid, Predictor, stream.Data.Position)]);
    }

    [Theory]
    [InlineData("/EarlyChange 5", "The /DecodeParms entry gives /EarlyChange 5, which ISO 32000-1 does not allow; it was read as 1.")]
    [InlineData("/EarlyChange /Zero", "The /DecodeParms entry gives /EarlyChange as a name, where ISO 32000-1 wants an integer; it was read as absent, which gives 1.")]
    [InlineData("/EarlyChange 1", "")]
    public void Reads_an_lzw_early_change_other_than_0_as_1_and_reports_one_pdf_does_not_allow(string parameters, string reports)
    {
        // ISO 32000-1 (Table 8) gives /EarlyChange 0 or 1; MuPDF reads any value but 0 as 1. The specification's example,
        // whose codes grow early, decodes the same.
        var dictionary = Dictionary($"/Filter /LZWDecode /DecodeParms << {parameters} >>");
        var diagnostics = new PdfDiagnostics();

        var decoded = new PdfStream(dictionary, PdfStreamData.FromMemory(new byte[] { 0x80, 0x0B, 0x60, 0x50, 0x22, 0x0C, 0x0C, 0x85, 0x01 })).Decode(diagnostics);

        Text(decoded).Should().Be("-----A---B");
        AssertReports(diagnostics, reports);
    }

    [Fact]
    public void Undoes_a_png_up_predictor()
    {
        byte[] predicted = [0x02, 0x0A, 0x14, 0x1E, 0x28, 0x02, 0x01, 0x02, 0x03, 0x04, 0x02, 0x01, 0x02, 0x03, 0x04];

        PredictorTransform.TryApply(predicted, predictor: 12, colors: 1, bitsPerComponent: 8, columns: 4, cut: false, out var result, out _)
            .Should().BeTrue();

        result.Should().Equal((byte)10, 20, 30, 40, 11, 22, 33, 44, 12, 24, 36, 48);
    }

    [Fact]
    public void Undoes_a_tiff_predictor()
    {
        byte[] predicted = [10, 5, 5, 5];

        PredictorTransform.TryApply(predicted, predictor: 2, colors: 1, bitsPerComponent: 8, columns: 4, cut: false, out var result, out _)
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
    [InlineData("/Filter [/ASCIIHexDecode /RunLengthDecode /ASCII85Decode] /DecodeParms [9 0 R 9 0 R 9 0 R]", 0)]
    [InlineData("/Filter [/ASCIIHexDecode /DCTDecode] /DecodeParms [null 9 0 R]", 0)]
    [InlineData("/Filter [/ASCIIHexDecode /DCTDecode 9 0 R] /DecodeParms [null null 9 0 R]", 0)]
    [InlineData("/Filter [/ASCIIHexDecode /MadeUpDecode /FlateDecode] /DecodeParms [null 9 0 R 9 0 R]", 0)]
    [InlineData("/Filter [/ASCIIHexDecode 5 9 0 R] /DecodeParms 9 0 R", 0)]
    [InlineData("/Filter [/ASCIIHexDecode /FlateDecode] /DecodeParms [null 9 0 R]", 1)]
    [InlineData("/Filter [/ASCIIHexDecode /Crypt] /DecodeParms [null 9 0 R]", 1)]
    public void Resolves_nothing_a_step_does_not_read(string entries, int expected)
    {
        // Each value is a reference to an object nothing should ask for: parameters of filters that take none, and
        // what stands at or past an image filter, an unknown one, or an element that names none. The last cases, whose
        // Flate step, and /Crypt step, which reads the crypt filter they name, do read their parameters, show the source
        // is asked when it should be.
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
    [InlineData("/Filter /DCTDecode", true, false)]
    [InlineData("/Filter [/ASCIIHexDecode /DCTDecode]", true, false)]
    [InlineData("/Filter [/ASCIIHexDecode /JPXDecode /FlateDecode]", true, false)]
    [InlineData("/Filter [/FlateDecode /DCTDecode]", true, true)]
    [InlineData("/Filter [/MadeUpDecode /DCTDecode]", false, false)]
    [InlineData("/Filter [5 /DCTDecode]", false, false)]
    [InlineData("/Filter (DCTDecode)", false, false)]
    [InlineData("/Filter [/ASCIIHexDecode /FlateDecode]", false, true)]
    [InlineData("/Filter null", false, false)]
    [InlineData("", false, false)]
    [InlineData("/Filter /DCT", true, false)]
    [InlineData("/Filter [/Fl /CCF]", true, true)]
    [InlineData("/Filter [/Ahx /DCTDecode]", false, false)]
    [InlineData("/Filter [/a85 /DCTDecode]", false, false)]
    [InlineData("/Filter [/lzW /DCTDecode]", false, false)]
    [InlineData("/Filter [/FL /DCTDecode]", false, false)]
    [InlineData("/Filter [/Rl /DCTDecode]", false, false)]
    [InlineData("/Filter [/ccF /DCTDecode]", false, false)]
    [InlineData("/Filter [/dcT /DCTDecode]", false, false)]
    [InlineData("/Filter [/RGB /DCTDecode]", false, false)]
    [InlineData("/Filter [/DP /DCTDecode]", false, false)]
    public void Says_a_stream_is_an_image_only_when_decoding_it_stops_at_an_image_filter(string entries, bool image, bool readsParameters)
    {
        // Decoding stops at the first filter it does not know, or at an element that names none: the data that comes
        // back is not the image a filter after it names. Telling it reads what decoding reads before it, and no more: the
        // parameters of a Flate or LZW step before the chain ends, a value of which a guard cut ends it there (#263). An
        // abbreviation is the filter it abbreviates (#250); a name is compared byte for byte, so one that differs from an
        // abbreviation in a letter's case, or that abbreviates a color space or a key of an inline image, is a filter the
        // library does not know.
        var source = Substitute.For<IPdfObjectSource>();
        var dictionary = new PdfObjectParser(Encoding.ASCII.GetBytes($"<< {entries} /DecodeParms 9 0 R >>"), source: source).ParseObject().AsDictionary().Required();
        var stream = new PdfStream(dictionary, PdfStreamData.FromMemory("00>"u8.ToArray()));

        stream.HasImageFilter().Should().Be(image);
        source.ReceivedCalls().Should().HaveCount(readsParameters ? 1 : 0);
    }

    [Theory]
    [InlineData("/Filter /Fl", "flate", "48656C6C6F", false, "The /Filter entry names /Fl, an abbreviation ISO 32000-1 allows only in an inline image; it was read as /FlateDecode.")]
    [InlineData("/Filter /AHx", "48656C6C6F>", "48656C6C6F", false, "The /Filter entry names /AHx, an abbreviation ISO 32000-1 allows only in an inline image; it was read as /ASCIIHexDecode.")]
    [InlineData("/Filter /A85", "87cURD]i,\"Ebo80~>", "48656C6C6F20576F726C6421", false, "The /Filter entry names /A85, an abbreviation ISO 32000-1 allows only in an inline image; it was read as /ASCII85Decode.")]
    [InlineData("/Filter /LZW", "lzw", "2D2D2D2D2D412D2D2D42", false, "The /Filter entry names /LZW, an abbreviation ISO 32000-1 allows only in an inline image; it was read as /LZWDecode.")]
    [InlineData("/Filter /RL", "run length", "6162637A7A7A", false, "The /Filter entry names /RL, an abbreviation ISO 32000-1 allows only in an inline image; it was read as /RunLengthDecode.")]
    [InlineData("/Filter /CCF", "abc", "616263", true, "The /Filter entry names /CCF, an abbreviation ISO 32000-1 allows only in an inline image; it was read as /CCITTFaxDecode.")]
    [InlineData("/Filter /DCT", "abc", "616263", true, "The /Filter entry names /DCT, an abbreviation ISO 32000-1 allows only in an inline image; it was read as /DCTDecode.")]
    [InlineData("/Filter [/AHx /Fl]", "hex flate", "48656C6C6F", false, "Element 0 of the /Filter entry names /AHx, an abbreviation ISO 32000-1 allows only in an inline image; it was read as /ASCIIHexDecode.|Element 1 of the /Filter entry names /Fl, an abbreviation ISO 32000-1 allows only in an inline image; it was read as /FlateDecode.")]
    [InlineData("/Filter /Fl /DecodeParms << /Predictor 12 /Columns 4 >>", "rows", Undone, false, "The /Filter entry names /Fl, an abbreviation ISO 32000-1 allows only in an inline image; it was read as /FlateDecode.")]
    public void Reads_a_filter_named_by_the_abbreviation_of_an_inline_image_as_the_filter_it_abbreviates(
        string entries, string data, string expected, bool image, string reports)
    {
        // ISO 32000-1 allows the abbreviations only in an inline image's dictionary (Table 94); qpdf, MuPDF, pdf.js and
        // PDFium read them on any stream all the same. Each element so named is reported once, a repair: nothing is lost.
        var bytes = data switch
        {
            "flate" => Compress("Hello"u8.ToArray()),
            "hex flate" => Encoding.ASCII.GetBytes(Convert.ToHexString(Compress("Hello"u8.ToArray())) + ">"),
            "lzw" => [0x80, 0x0B, 0x60, 0x50, 0x22, 0x0C, 0x0C, 0x85, 0x01],
            "run length" => [2, (byte)'a', (byte)'b', (byte)'c', 254, (byte)'z', 128],
            "rows" => Compress(Convert.FromHexString(Unpredicted)),
            _ => Encoding.ASCII.GetBytes(data),
        };
        var stream = new PdfStream(Dictionary(entries), PdfStreamData.FromMemory(bytes));
        var diagnostics = new PdfDiagnostics();

        var decoded = stream.Decode(diagnostics);

        stream.HasImageFilter().Should().Be(image);
        Convert.ToHexString(decoded.Span).Should().Be(expected);
        AssertReports(diagnostics, reports, PdfDiagnosticCodes.FilterNameAbbreviated, PdfDiagnosticSeverity.Repair);
    }

    [Theory]
    [InlineData("/Type /XRef")]
    [InlineData("/Type /ObjStm")]
    public void Reads_a_cross_reference_or_object_stream_whose_filter_abbreviates_flate_whole(string type)
    {
        // The stream's /Filter names /Fl, padded to the length /FlateDecode took, so that no offset moves: the cross-reference
        // stream's rows, and the objects the object stream holds, are read whole, and the file is valid but for the name.
        var objectStream = type == "/Type /ObjStm";
        var builder = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .WithObject(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << >> >>");
        var file = objectStream
            ? builder.BuildWithXRefStream(rootNumber: 1, compressedObjects: [2, 3], compressObjectStream: true)
            : builder.BuildWithXRefStream(rootNumber: 1, compressXRefStream: true);
        var text = Encoding.Latin1.GetString(file);
        var at = text.IndexOf("/Filter /FlateDecode", text.IndexOf(type, StringComparison.Ordinal), StringComparison.Ordinal);
        file = Encoding.Latin1.GetBytes(string.Concat(text.AsSpan(0, at), "/Filter /Fl         ", text.AsSpan(at + 20)));

        using var document = PdfDocument.Open(file);

        document.GetObject(new PdfObjectId(3)).AsDictionary().Required().IsOfType(PdfName.Page).Should().BeTrue();
        document.WasRepaired.Should().BeFalse();
        var report = document.Diagnostics.Should().ContainSingle().Which;
        (report.Code, report.Severity).Should().Be((PdfDiagnosticCodes.FilterNameAbbreviated, PdfDiagnosticSeverity.Repair));
        report.Message.Should().Be("The /Filter entry names /Fl, an abbreviation ISO 32000-1 allows only in an inline image; it was read as /FlateDecode.");
        new PdfValidator().Validate(document).Findings.Should().BeEmpty();
    }

    [Fact]
    public void Returns_raw_data_when_no_filter_is_declared()
    {
        var stream = new PdfStream(new PdfDictionary(), PdfStreamData.FromMemory("plain"u8.ToArray()));

        Text(stream.Decode()).Should().Be("plain");
    }

    [Theory]
    [InlineData("an unsupported filter", "/Foo", "abc", true, "abc")]
    [InlineData("Flate corrupt before its first byte decodes", "/FlateDecode", "xxxxxxx", true, "xxxxxxx")]
    [InlineData("ASCIIHex, then an unsupported filter", "[/ASCIIHexDecode /Foo]", "4142>", true, "AB")]
    [InlineData("ASCIIHex, then Flate corrupt before its first byte decodes", "[/ASCIIHexDecode /FlateDecode]", "78787878787878>", true, "xxxxxxx")]
    [InlineData("ASCIIHex, then an element that names no filter", "[/ASCIIHexDecode 5]", "4142>", true, "AB")]
    [InlineData("a /Filter that is neither a name nor an array", "5", "abc", true, "abc")]
    [InlineData("Flate that decodes a stored block, then meets a block of the reserved type", "/FlateDecode", "{stored}", false, "Hello")]
    [InlineData("ASCIIHex that decodes to nothing, then an unsupported filter", "[/ASCIIHexDecode /Foo]", ">", false, "")]
    [InlineData("LZW whose first code it has not defined", "/LZWDecode", "\u0096\u0000", false, "")]
    [InlineData("ASCIIHex whose bytes encode nothing", "/ASCIIHexDecode", "zzzzzz>", false, "")]
    [InlineData("an image filter", "/DCTDecode", "abc", false, "abc")]
    [InlineData("sound Flate", "/FlateDecode", "{sound}", false, FlateText)]
    [InlineData("no filter", "", "abc", false, "abc")]
    public void Data_is_said_undecoded_only_where_a_filter_the_library_cannot_run_is_left_some_or_flate_data_is_corrupt_before_its_first_byte(
        string shape, string filter, string data, bool undecoded, string decoded)
    {
        // What comes back from a filter the library does not know, from an element of /Filter that names none or a /Filter
        // that is neither a name nor an array, or from Flate data corrupt before its first byte decodes, the one fault of a
        // filter that leaves the data as it was given, is the data as the filters before it left it, not what the stream
        // decodes to: a reader of a cross-reference stream's rows reads none from it (#221). Flate that decoded part of its
        // data before a fault gives that part; LZW whose first code it has not defined, and ASCIIHex whose bytes encode
        // nothing, decode none of their data, and give none, as a filter left no data does; and an image filter leaves the
        // image on purpose: none of them is said undecoded. No guard ended any of these decodings.
        var bytes = data switch
        {
            // A zlib header, a stored block that is not the last, of the five bytes of Hello, then a block of type 3.
            "{stored}" => [0x78, 0x01, 0x00, 0x05, 0x00, 0xFA, 0xFF, .. "Hello"u8, 0x07],
            "{sound}" => FlateZlib,
            _ => Encoding.Latin1.GetBytes(data),
        };
        var dictionary = new PdfObjectParser(Encoding.ASCII.GetBytes(filter.Length == 0 ? "<< >>" : $"<< /Filter {filter} >>"))
            .ParseObject().AsDictionary().Required();
        var diagnostics = new PdfDiagnostics();

        var output = PdfFilterPipeline.Decode(
            new PdfStream(dictionary, PdfStreamData.FromMemory(bytes)),
            diagnostics,
            endHidden: false,
            reachedBefore: false,
            out var leftEncoded,
            out var cut,
            out var said);

        said.Should().Be(undecoded, shape);
        Text(output).Should().Be(decoded, shape);
        leftEncoded.Should().BeFalse(shape);
        cut.Should().BeFalse(shape);
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

    /// <summary>A PNG predictor's two rows of four bytes, the first unfiltered, the second predicted from the first.</summary>
    private const string Unpredicted = "00010203040201010101";

    /// <summary><see cref="Unpredicted"/> undone as rows of four bytes.</summary>
    private const string Undone = "0102030402030405";

    /// <summary><see cref="Unpredicted"/> undone as rows of one byte.</summary>
    private const string ColumnOfOne = "0104060101";

    private static PdfDictionary Dictionary(string entries) =>
        new PdfObjectParser(Encoding.ASCII.GetBytes($"<< {entries} >>")).ParseObject().AsDictionary().Required();

    /// <summary>
    /// Decodes <see cref="Unpredicted"/>, encoded through the Flate, LZW and ASCIIHex filters <paramref name="dictionary"/>
    /// names, and checks what comes back and what is reported under <see cref="PdfDiagnosticCodes.FilterParametersInvalid"/>.
    /// </summary>
    private static void AssertDecodes(PdfDictionary dictionary, string expected, string reports)
    {
        var data = Convert.FromHexString(Unpredicted);
        PdfObject?[] filters = dictionary.GetRaw(PdfName.Filter) is PdfArray array ? [.. array] : [dictionary.GetRaw(PdfName.Filter)];

        for (var index = filters.Length - 1; index >= 0; index--)
        {
            data = ((PdfName)filters[index]!).Value switch
            {
                "FlateDecode" => Compress(data),
                "ASCIIHexDecode" => Encoding.ASCII.GetBytes(Convert.ToHexString(data) + ">"),

                // Each byte its own code, then the end-of-data code: too few codes for the table to widen them past nine bits.
                "LZWDecode" => NineBitCodes([.. data.Select(value => (int)value), 257]),
                _ => data,
            };
        }

        var diagnostics = new PdfDiagnostics();

        var decoded = new PdfStream(dictionary, PdfStreamData.FromMemory(data)).Decode(diagnostics);

        Convert.ToHexString(decoded.Span).Should().Be(expected);
        AssertReports(diagnostics, reports);
    }

    /// <summary>Checks that <paramref name="diagnostics"/> hold the messages <paramref name="reports"/> lists, | between them.</summary>
    private static void AssertReports(
        PdfDiagnostics diagnostics,
        string reports,
        string code = PdfDiagnosticCodes.FilterParametersInvalid,
        PdfDiagnosticSeverity severity = PdfDiagnosticSeverity.Warning)
    {
        string[] messages = reports.Length == 0 ? [] : reports.Split('|');
        diagnostics.Select(d => (d.Code, d.Severity, d.Message)).Should().Equal(messages.Select(message => (code, severity, message)));
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

    /// <summary>
    /// A megabyte of a content stream's text, or of random bytes; or a run of one byte long enough to fill an LZW table
    /// with no clear code, its longest entry named twice more.
    /// </summary>
    private static byte[] LzwData(string data)
    {
        switch (data)
        {
            case "text":
                var text = new StringBuilder();
                for (var line = 0; text.Length < 1 << 20; line++)
                {
                    text.Append(CultureInfo.InvariantCulture, $"BT /F1 12 Tf 72 {720 - (line % 700)} Td (Line {line} of a content stream) Tj ET\n");
                }

                return Encoding.ASCII.GetBytes(text.ToString(0, 1 << 20));
            case "incompressible":
                var random = new byte[1 << 20];
                new Random(42).NextBytes(random);
                return random;
            default:
                // Entries of 2 to 3,839 bytes after the first byte: 3,839 × 3,840 / 2 bytes, then the longest twice more.
                var run = new byte[(3839 * 3840 / 2) + (2 * 3839)];
                run.AsSpan().Fill((byte)'A');
                return run;
        }
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

    private static byte[] Compress(byte[] data, CompressionLevel level = CompressionLevel.Optimal)
    {
        using var compressed = new MemoryStream();
        using (var zlib = new System.IO.Compression.ZLibStream(compressed, level, leaveOpen: true))
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

    /// <summary>
    /// A megabyte or more of input in <paramref name="filter"/>, an expanding filter, decoding to more than a kilobyte.
    /// </summary>
    /// <remarks>
    /// Built by the test that reads it, not handed to it as a theory's data: xUnit serializes a theory's data at every
    /// discovery, filtered runs included, which took some nine seconds for these four and left 252 MiB of character arrays
    /// in the shared pool, whose trim later moved the memory a test of <see cref="TimingCollection"/> measures (#307).
    /// </remarks>
    private static byte[] LargeInput(string filter)
    {
        switch (filter)
        {
            case "RunLengthDecode":
                var runs = RunsOf((1 << 20) + 1);
                runs[^1] = 0x80;
                return runs;
            case "LZWDecode":
                return new byte[1 << 20];
            case "ASCII85Decode":
                return Encoding.ASCII.GetBytes(new string('z', 1 << 20));
            case "FlateDecode":
                var stored = new byte[2 << 20];
                new Random(17).NextBytes(stored);
                using (var compressed = new MemoryStream())
                {
                    using (var zlib = new System.IO.Compression.ZLibStream(compressed, System.IO.Compression.CompressionLevel.NoCompression, leaveOpen: true))
                    {
                        zlib.Write(stored);
                    }

                    return compressed.ToArray();
                }

            default:
                throw new ArgumentOutOfRangeException(nameof(filter), filter, "Not an expanding filter this suite builds a large input for.");
        }
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

    /// <summary>A file in memory that records where each read starts.</summary>
    private sealed class RecordingSource(byte[] data) : PdfFileSource
    {
        /// <summary>Gets where each read started, in order.</summary>
        public List<long> Reads { get; } = [];

        public override long Length => data.Length;

        public override int Read(long offset, Span<byte> buffer)
        {
            Reads.Add(offset);
            var count = (int)Math.Min(buffer.Length, data.Length - offset);
            data.AsSpan((int)offset, count).CopyTo(buffer);
            return count;
        }
    }

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
