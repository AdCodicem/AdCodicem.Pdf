using System.IO.Compression;
using System.Text;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.IO;
using AdCodicem.Pdf.IO.Filters;
using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// What decoding says about data that stops short or uses what it never defined (T32): a Flate stream that
/// lost its tail or its checksum, and an LZW stream that uses a code it has not defined.
/// </summary>
/// <remarks>
/// The framework's inflater takes the end of its input for the end of the data, so a Flate stream cut short
/// decoded to what was left without a word. What decodes is unchanged: the prefix is kept, as it always was.
/// What changed is that the loss is reported, and told apart from the loss of the checksum alone, which
/// loses nothing of the data.
/// </remarks>
public class FilterDamageTests
{
    private const string TailLost = "A Flate stream ends before its data does; what decoded before the end was kept.";
    private const string ChecksumMissing = "A Flate stream ends before its checksum does; its data decoded whole, unchecked.";
    private const string NotZlib = "A Flate stream was not valid zlib data.";

    /// <summary>The length of zlib's Adler-32 checksum, which ends a zlib stream.</summary>
    private const int ChecksumLength = 4;

    /// <summary>Data compressed with zlib, each chosen to reach a different part of the inflater.</summary>
    public static TheoryData<string, byte[]> Payloads => new()
    {
        // A few bytes: every cut lands in the header, the only block, or the checksum.
        { "a short text", Encoding.ASCII.GetBytes("Hello, PDF filters!Hello, PDF filters!Hello, PDF filters!") },
        // Nothing at all: the only block holds the end-of-block code and nothing else.
        { "nothing", [] },
        // More than one read of the caller's 64 KB buffer, from a few kilobytes of input.
        { "a content stream", ContentStream(3000) },
        // Stored blocks, and more input than the inflater reads from its source at once.
        { "random bytes", RandomBytes(40_000) },
    };

    [Theory]
    [MemberData(nameof(Payloads))]
    public void Reports_a_zlib_stream_cut_at_any_byte_and_keeps_what_decoded_before_the_cut(string payload, byte[] plain)
    {
        // Every cut from one byte to all but the last: a cut before the end of the last block loses data and
        // earns a warning; a cut inside the four bytes of the checksum loses none and earns a repair. The
        // bytes kept are always the start of the data, and all of it once the last block is whole.
        var compressed = Compress(plain, CompressionLevel.Optimal);
        var bodyEnd = compressed.Length - ChecksumLength;
        var failures = new List<string>();

        foreach (var cut in Cuts(compressed.Length))
        {
            var diagnostics = new PdfDiagnostics();
            var decoded = Decode(compressed.AsSpan(0, cut).ToArray(), PdfName.FlateDecode, diagnostics).ToArray();
            var whole = cut >= bodyEnd;
            var expected = whole ? Report(PdfDiagnosticSeverity.Repair, ChecksumMissing) : Report(PdfDiagnosticSeverity.Warning, TailLost);

            if (!plain.AsSpan().StartsWith(decoded) || (whole && decoded.Length != plain.Length) || Describe(diagnostics) != expected)
            {
                failures.Add($"cut at {cut} of {compressed.Length}: {decoded.Length} of {plain.Length} bytes, [{Describe(diagnostics)}]");
            }
        }

        failures.Should().BeEmpty($"{payload} must be reported wherever it is cut");
    }

    [Theory]
    [MemberData(nameof(Payloads))]
    public void Reads_a_whole_zlib_stream_and_whatever_its_length_took_in_after_it_without_a_word(string payload, byte[] plain)
    {
        // A stream's /Length often takes in the end-of-line before endstream, and some take in more. The
        // inflater stops at the end of the checksum, and asks for nothing past it.
        var compressed = Compress(plain, CompressionLevel.Optimal);
        byte[][] tails = [[], [0x0A], [0x0D], [0x0D, 0x0A], [0x20, 0x20], "endstream"u8.ToArray(), [0xFF, 0x00, 0x78, 0xDA]];
        var failures = new List<string>();

        foreach (var tail in tails)
        {
            var diagnostics = new PdfDiagnostics();
            var decoded = Decode([.. compressed, .. tail], PdfName.FlateDecode, diagnostics).ToArray();

            if (!decoded.AsSpan().SequenceEqual(plain) || diagnostics.Count != 0)
            {
                failures.Add($"followed by {Convert.ToHexString(tail)}: {decoded.Length} bytes, [{Describe(diagnostics)}]");
            }
        }

        failures.Should().BeEmpty($"{payload} is whole");
    }

    [Theory]
    [InlineData(CompressionLevel.NoCompression)]
    [InlineData(CompressionLevel.Fastest)]
    [InlineData(CompressionLevel.SmallestSize)]
    public void Tells_a_lost_checksum_from_lost_data_whatever_the_compression(CompressionLevel level)
    {
        // The last byte of the body holds the end of the last block: without it the data is unfinished —
        // whether any byte of output went with it the data cannot say, since the end-of-block code may be all
        // that byte held —; without anything after it, only the checksum is lost.
        var plain = ContentStream(2000);
        var compressed = Compress(plain, level);
        var bodyEnd = compressed.Length - ChecksumLength;
        var withoutChecksum = new PdfDiagnostics();
        var withoutLastByte = new PdfDiagnostics();

        var whole = Decode(compressed.AsSpan(0, bodyEnd).ToArray(), PdfName.FlateDecode, withoutChecksum).ToArray();
        var cut = Decode(compressed.AsSpan(0, bodyEnd - 1).ToArray(), PdfName.FlateDecode, withoutLastByte).ToArray();

        whole.Should().Equal(plain);
        Describe(withoutChecksum).Should().Be(Report(PdfDiagnosticSeverity.Repair, ChecksumMissing));
        plain.AsSpan().StartsWith(cut).Should().BeTrue();
        Describe(withoutLastByte).Should().Be(Report(PdfDiagnosticSeverity.Warning, TailLost));
    }

    [Fact]
    public void Reports_a_raw_deflate_stream_cut_short_beside_its_missing_header()
    {
        // Raw deflate has no checksum, so a cut is always data lost. A single byte is too short to be told
        // from a zlib header, and is read as one.
        var plain = ContentStream(200);
        var compressed = CompressRaw(plain);
        var failures = new List<string>();

        foreach (var cut in Cuts(compressed.Length + 1))
        {
            var diagnostics = new PdfDiagnostics();
            var decoded = Decode(compressed.AsSpan(0, cut).ToArray(), PdfName.FlateDecode, diagnostics).ToArray();
            var expected = cut switch
            {
                1 => Report(PdfDiagnosticSeverity.Warning, TailLost),
                _ when cut == compressed.Length => Report(PdfDiagnosticSeverity.Repair, NotZlib),
                _ => Report(PdfDiagnosticSeverity.Repair, NotZlib) + "; " + Report(PdfDiagnosticSeverity.Warning, TailLost),
            };

            if (!plain.AsSpan().StartsWith(decoded) || Describe(diagnostics) != expected)
            {
                failures.Add($"cut at {cut} of {compressed.Length}: {decoded.Length} bytes, [{Describe(diagnostics)}]");
            }
        }

        failures.Should().BeEmpty();
    }

    [Fact]
    public void Reads_a_whole_raw_deflate_stream_followed_by_other_bytes_as_whole()
    {
        var plain = ContentStream(200);
        var diagnostics = new PdfDiagnostics();

        var decoded = Decode([.. CompressRaw(plain), 0x01, 0x02, 0x03, 0x04], PdfName.FlateDecode, diagnostics);

        decoded.ToArray().Should().Equal(plain);
        Describe(diagnostics).Should().Be(Report(PdfDiagnosticSeverity.Repair, NotZlib));
    }

    [Theory]
    [InlineData(0, ChecksumMissing)]
    [InlineData(1, TailLost)]
    [InlineData(100, TailLost)]
    public void Reports_a_zlib_stream_after_white_space_that_ends_early(int bytesBeforeTheChecksum, string report)
    {
        // A generator that miscounts /Length leaves white space before the header; the stream after it is
        // judged like any other.
        var plain = ContentStream(500);
        var compressed = Compress(plain, CompressionLevel.Optimal);
        var cut = compressed.Length - ChecksumLength - bytesBeforeTheChecksum;
        var diagnostics = new PdfDiagnostics();

        var decoded = Decode([0x0D, 0x0A, .. compressed.AsSpan(0, cut)], PdfName.FlateDecode, diagnostics).ToArray();

        plain.AsSpan().StartsWith(decoded).Should().BeTrue();
        if (bytesBeforeTheChecksum == 0)
        {
            decoded.Length.Should().Be(plain.Length);
        }

        var severity = report == ChecksumMissing ? PdfDiagnosticSeverity.Repair : PdfDiagnosticSeverity.Warning;
        Describe(diagnostics).Should().Be(Report(PdfDiagnosticSeverity.Repair, NotZlib) + "; " + Report(severity, report));
    }

    [Fact]
    public void Reports_a_zlib_stream_whose_checksum_is_wrong_as_corrupt()
    {
        // A whole checksum that disagrees with the data is checked, as it was before: the data is corrupt
        // somewhere, which is not the same report as data that stops short. The framework throws from the
        // read that meets the checksum, so what that read decoded is lost with it (T38); what came before
        // is kept.
        var plain = ContentStream(3000);
        var compressed = Compress(plain, CompressionLevel.Optimal);
        compressed[^1] ^= 0xFF;
        var diagnostics = new PdfDiagnostics();

        var decoded = Decode(compressed, PdfName.FlateDecode, diagnostics).ToArray();

        decoded.Length.Should().BeGreaterThan(0);
        plain.AsSpan().StartsWith(decoded).Should().BeTrue();
        Describe(diagnostics).Should().Be(
            Report(PdfDiagnosticSeverity.Warning, "A Flate stream is corrupt; what decoded before the fault was found was kept."));
    }

    [Fact]
    public void A_cut_stream_that_reaches_the_bound_reports_the_bound_and_one_whose_rest_fits_reports_the_cut()
    {
        // Decoding that stops at the bound never reaches the end of the data, so it cannot say the data was
        // cut: only the reader's limit is reported. A bound exactly as large as what the cut data decodes
        // to holds all of it, and the cut is reported.
        var compressed = Compress(ContentStream(2000), CompressionLevel.Optimal);
        var cut = compressed.AsSpan(0, compressed.Length / 2).ToArray();
        var decodedLength = Decode(cut, PdfName.FlateDecode, new PdfDiagnostics()).Length;
        var belowIt = new PdfDiagnostics();
        var atIt = new PdfDiagnostics();

        var bounded = DecodeWithin(cut, decodedLength - 1, belowIt);
        var fitting = DecodeWithin(cut, decodedLength, atIt);

        bounded.Length.Should().Be(decodedLength - 1);
        belowIt.Select(d => d.Code).Should().Equal(PdfDiagnosticCodes.LimitDecodedStream);
        fitting.Length.Should().Be(decodedLength);
        Describe(atIt).Should().Be(Report(PdfDiagnosticSeverity.Warning, TailLost));
    }

    [Fact]
    public void Telling_a_lost_checksum_from_lost_data_keeps_nothing_it_decodes_the_second_time()
    {
        // A zlib stream that ran out is read again as raw deflate to learn whether its last block was whole.
        // That second reading keeps nothing: decoding 4 MB whose checksum is missing allocates what decoding
        // them whole does, give or take the inflater's own buffers — not a second 4 MB.
        const int Length = 4 * 1024 * 1024;
        var compressed = Compress(new byte[Length], CompressionLevel.Optimal);
        var withoutChecksum = compressed.AsSpan(0, compressed.Length - ChecksumLength).ToArray();
        Decode(compressed, PdfName.FlateDecode, new PdfDiagnostics());
        var diagnostics = new PdfDiagnostics();

        var beforeWhole = GC.GetAllocatedBytesForCurrentThread();
        Decode(compressed, PdfName.FlateDecode, new PdfDiagnostics());
        var whole = GC.GetAllocatedBytesForCurrentThread() - beforeWhole;
        var beforeCut = GC.GetAllocatedBytesForCurrentThread();
        var decoded = Decode(withoutChecksum, PdfName.FlateDecode, diagnostics);
        var cut = GC.GetAllocatedBytesForCurrentThread() - beforeCut;

        (cut - whole).Should().BeLessThan(128 * 1024);
        decoded.Length.Should().Be(Length);
        Describe(diagnostics).Should().Be(Report(PdfDiagnosticSeverity.Repair, ChecksumMissing));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void A_body_is_whole_only_when_it_reaches_the_end_of_its_last_block(int bytesCut, bool whole)
    {
        var plain = ContentStream(500);
        var body = CompressRaw(plain);

        FlateFilter.IsWholeDeflate(body.AsMemory(0, body.Length - bytesCut), plain.Length, new byte[1024])
            .Should().Be(whole);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void A_body_is_not_the_one_read_first_when_it_decodes_to_another_length(int difference)
    {
        // The body read again is the data the zlib reading decoded; decoding to more or to less, it is not.
        var plain = ContentStream(500);

        FlateFilter.IsWholeDeflate(CompressRaw(plain), plain.Length + difference, new byte[64])
            .Should().BeFalse();
    }

    [Fact]
    public void A_body_that_is_not_deflate_is_not_whole()
    {
        FlateFilter.IsWholeDeflate(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }, 0, new byte[64]).Should().BeFalse();
    }

    [Fact]
    public void The_flate_input_says_the_end_was_asked_past_only_when_bytes_were_asked_for_there()
    {
        using var input = new FlateInput("abcde"u8.ToArray());
        var buffer = new byte[3];

        input.Read(buffer, 0, 3).Should().Be(3);
        input.Read(buffer.AsSpan()).Should().Be(2);
        buffer.AsSpan(0, 2).ToArray().Should().Equal("de"u8.ToArray());
        input.ReadPastEnd.Should().BeFalse("the reader took the last byte, and asked for nothing after it");

        input.Read(buffer, 0, 0).Should().Be(0);
        input.ReadPastEnd.Should().BeFalse("asking for nothing is not asking past the end");

        input.Read(buffer, 0, 3).Should().Be(0);
        input.ReadPastEnd.Should().BeTrue();
    }

    [Fact]
    public void The_flate_input_reads_memory_that_no_array_holds_as_it_is()
    {
        // Encoded data can come from a file's own buffer rather than an array; it is read in place.
        using var owner = new UnexposedMemory("abc"u8.ToArray());
        System.Runtime.InteropServices.MemoryMarshal.TryGetArray<byte>(owner.Memory, out _).Should().BeFalse();
        using var input = new FlateInput(owner.Memory);
        var buffer = new byte[8];

        input.Read(buffer.AsSpan()).Should().Be(3);
        buffer.AsSpan(0, 3).ToArray().Should().Equal("abc"u8.ToArray());
        input.CanSeek.Should().BeFalse();
        input.CanWrite.Should().BeFalse();
    }

    /// <summary>Codes after the table was cleared and 'A' and 'B' read: 258 is "AB", and 259 the next to define.</summary>
    [Theory]
    [InlineData(258, "ABABC", -1)]
    [InlineData(259, "ABBBC", -1)]
    [InlineData(260, "AB", 260)]
    [InlineData(511, "AB", 511)]
    public void Decodes_an_lzw_code_up_to_the_next_one_to_define_and_stops_at_any_past_it(int code, string expected, int undefined)
    {
        // Code 259 is the one the encoder was defining when it used it: the previous sequence and its own
        // first byte. Any code past it has no meaning yet; decoding it as if it were 259 made up data.
        var diagnostics = new PdfDiagnostics();

        var decoded = Decode(NineBitCodes(256, 'A', 'B', code, 'C', 257), PdfName.LZWDecode, diagnostics);

        Encoding.ASCII.GetString(decoded.Span).Should().Be(expected);
        Describe(diagnostics).Should().Be(undefined < 0 ? string.Empty : Report(PdfDiagnosticSeverity.Warning, LzwUndefined(undefined)));
    }

    [Theory]
    [InlineData(258)]
    [InlineData(300)]
    public void Stops_at_an_lzw_code_used_with_nothing_before_it_to_define_it_from(int code)
    {
        // The first code of the data, with no clear before it, has no previous sequence: even the next code
        // to define means nothing.
        var diagnostics = new PdfDiagnostics();

        var decoded = Decode(NineBitCodes(code, 'A', 257), PdfName.LZWDecode, diagnostics);

        decoded.Length.Should().Be(0);
        Describe(diagnostics).Should().Be(Report(PdfDiagnosticSeverity.Warning, LzwUndefined(code)));
    }

    [Fact]
    public void Decodes_an_lzw_stream_without_its_end_of_data_code_and_reports_nothing()
    {
        // Without the end-of-data code, data that lost its tail cannot be told from data whose encoder left
        // the code out; qpdf, like this reader, takes both as complete.
        var diagnostics = new PdfDiagnostics();

        var decoded = Decode(NineBitCodes(256, 'A', 'B', 258), PdfName.LZWDecode, diagnostics);

        Encoding.ASCII.GetString(decoded.Span).Should().Be("ABAB");
        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void Decodes_an_lzw_stream_whose_codes_grow_past_nine_bits_without_a_word()
    {
        // Past 511 codes each is read ten bits wide. A decoder that grew its width a code too late or too
        // early would read codes past the next one to define, and report sound data as corrupt.
        var plain = ContentStream(40);
        var encoded = LzwEncode(plain);
        encoded.Length.Should().BeGreaterThan(512 * 9 / 8, "the data must use codes past nine bits");
        var sound = new PdfDiagnostics();

        Decode(encoded, PdfName.LZWDecode, sound).ToArray().Should().Equal(plain);

        sound.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Reports_a_document_s_flate_stream_that_lost_its_tail_where_its_data_starts(bool callerCollects)
    {
        // A producer that wrote half its compressed data, with a /Length that matches what it wrote. The report
        // goes where the caller asks, or to the document when the caller passes nowhere, and names where the
        // data starts.
        var compressed = Compress(ContentStream(100), CompressionLevel.Optimal);
        var hex = Convert.ToHexString(compressed.AsSpan(0, compressed.Length / 2)) + ">";
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .Stream(3, "/Filter [/ASCIIHexDecode /FlateDecode]", hex)
            .BuildClassic(rootNumber: 1);
        var dataStart = file.AsSpan().IndexOf(Encoding.ASCII.GetBytes(hex));

        using var document = PdfDocument.Open(file);
        var collected = new PdfDiagnostics();
        var decoded = document.GetObject(new PdfObjectId(3)).AsStream().Required().Decode(callerCollects ? collected : null);

        decoded.Length.Should().BeGreaterThan(0);
        (callerCollects ? document.Diagnostics : collected).Should().BeEmpty();
        var report = (callerCollects ? collected : document.Diagnostics).Should().ContainSingle().Which;
        report.Code.Should().Be(PdfDiagnosticCodes.FilterFailed);
        report.Severity.Should().Be(PdfDiagnosticSeverity.Warning);
        report.Message.Should().Be(TailLost);
        report.Position.Should().Be(dataStart);
    }

    private static string LzwUndefined(int code) =>
        $"An LZW stream uses code {code}, which it has not defined; what decoded before it was kept.";

    /// <summary>Every cut from one byte to <paramref name="length"/> - 1, or a sample of them that keeps both ends.</summary>
    private static IEnumerable<int> Cuts(int length)
    {
        var step = Math.Max(1, length / 2000);

        for (var cut = 1; cut < length; cut++)
        {
            if (cut <= 64 || cut >= length - 64 || cut % step == 0)
            {
                yield return cut;
            }
        }
    }

    private static string Report(PdfDiagnosticSeverity severity, string message) =>
        $"{severity} {PdfDiagnosticCodes.FilterFailed}: {message}";

    private static string Describe(PdfDiagnostics diagnostics) =>
        string.Join("; ", diagnostics.Select(d => $"{d.Severity} {d.Code}: {d.Message}"));

    private static ReadOnlyMemory<byte> Decode(byte[] data, PdfName filter, PdfDiagnostics diagnostics)
    {
        var dictionary = new PdfDictionary();
        dictionary.Set(PdfName.Filter, filter);
        return new PdfStream(dictionary, PdfStreamData.FromMemory(data)).Decode(diagnostics);
    }

    private static ReadOnlyMemory<byte> DecodeWithin(byte[] data, int maxLength, PdfDiagnostics diagnostics)
    {
        var dictionary = new PdfDictionary();
        dictionary.Set(PdfName.Filter, PdfName.FlateDecode);
        var guard = new PdfLimitGuard(PdfReaderLimits.Default with { MaxDecodedStreamLength = maxLength }, throwOnLimit: false);
        return PdfFilterPipeline.Decode(new PdfStream(dictionary, PdfStreamData.FromMemory(data)), diagnostics, guard);
    }

    private static byte[] ContentStream(int lines)
    {
        var text = new StringBuilder();

        for (var line = 0; line < lines; line++)
        {
            text.Append("BT /F1 12 Tf 72 ").Append(720 - (line % 700)).Append(" Td (Line ").Append(line).Append(" of a content stream) Tj ET\n");
        }

        return Encoding.ASCII.GetBytes(text.ToString());
    }

    private static byte[] RandomBytes(int length)
    {
        var bytes = new byte[length];
        new Random(32).NextBytes(bytes);
        return bytes;
    }

    private static byte[] Compress(byte[] data, CompressionLevel level)
    {
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, level, leaveOpen: true))
        {
            zlib.Write(data);
        }

        // The framework writes nothing at all for nothing; zlib itself writes an empty block and its checksum.
        return compressed.Length == 0 ? [0x78, 0xDA, 0x03, 0x00, 0x00, 0x00, 0x00, 0x01] : compressed.ToArray();
    }

    private static byte[] CompressRaw(byte[] data)
    {
        using var compressed = new MemoryStream();
        using (var deflate = new DeflateStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(data);
        }

        return compressed.ToArray();
    }

    /// <summary>Encodes as the PDF variant of LZW does, with early change: a clear first, the end-of-data code last.</summary>
    private static byte[] LzwEncode(byte[] data)
    {
        var table = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < 256; i++)
        {
            table[((char)i).ToString()] = i;
        }

        var codes = new List<(int Code, int Bits)> { (256, 9) };
        var next = 258;
        var bits = 9;
        var current = string.Empty;

        foreach (var value in data)
        {
            var extended = current + (char)value;

            if (table.ContainsKey(extended))
            {
                current = extended;
                continue;
            }

            codes.Add((table[current], bits));
            table[extended] = next++;

            if (next + 1 > 1 << bits && bits < 12)
            {
                bits++;
            }

            current = ((char)value).ToString();
        }

        codes.Add((table[current], bits));
        codes.Add((257, bits));
        return Pack(codes);
    }

    /// <summary>Packs LZW codes nine bits each, most significant bit first, as the filter reads them.</summary>
    private static byte[] NineBitCodes(params int[] codes) => Pack([.. codes.Select(code => (code, 9))]);

    private static byte[] Pack(List<(int Code, int Bits)> codes)
    {
        var bytes = new List<byte>();
        var buffer = 0;
        var count = 0;

        foreach (var (code, bits) in codes)
        {
            buffer = (buffer << bits) | code;
            count += bits;

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

    /// <summary>Memory that no array is exposed behind, as a file's own buffer may be.</summary>
    private sealed class UnexposedMemory(byte[] contents) : System.Buffers.MemoryManager<byte>
    {
        public override Span<byte> GetSpan() => contents;

        public override System.Buffers.MemoryHandle Pin(int elementIndex = 0) => throw new NotSupportedException();

        public override void Unpin()
        {
        }

        protected override void Dispose(bool disposing)
        {
        }
    }
}
