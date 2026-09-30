using System.IO.Compression;
using System.Text;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.IO;
using AdCodicem.Pdf.IO.Filters;
using AdCodicem.Pdf.Objects;
using FsCheck;
using FsCheck.Fluent;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// What decoding says about data that stops short, turns corrupt, or uses what it never defined (T32, #56): a
/// Flate stream that lost its tail or its checksum, whose checksum is wrong, or that turns corrupt part of the
/// way, and an LZW stream that uses a code it has not defined.
/// </summary>
/// <remarks>
/// The framework's inflater takes the end of its input for the end of the data, so a Flate stream cut short
/// decoded to what was left without a word. What decodes is unchanged: the prefix is kept, as it always was.
/// What changed is that the loss is reported, and told apart from the loss of the checksum alone, which
/// loses nothing of the data. The same inflater throws from the read that meets a fault, losing what that read
/// decoded — a stream whose checksum alone was wrong lost its end, or was left encoded, and so was one that turned
/// corrupt within the first input the inflater took. Such data is read again, and keeps what decoded before the
/// byte the fault lies in.
/// </remarks>
public class FilterDamageTests
{
    private const string TailLost = "A Flate stream ends before its data does; what decoded before the end was kept.";
    private const string ChecksumMissing = "A Flate stream ends before its checksum does; its data decoded whole, unchecked.";
    private const string NotZlib = "A Flate stream was not valid zlib data.";
    private const string NotDecoded = "A Flate stream could not be decoded.";

    /// <summary>The length of zlib's Adler-32 checksum, which ends a zlib stream.</summary>
    private const int ChecksumLength = 4;

    /// <summary>The first byte of a last block of the type deflate reserves, and defines no data for: BFINAL 1, BTYPE 3.</summary>
    private const byte UndefinedBlock = 0x07;

    /// <summary>The most input the framework's inflater takes at once.</summary>
    private const int InflaterInput = 8 * 1024;

    private const ulong Seed = 0x5EED_0056UL;

    /// <summary>The second half of FsCheck's random state, which must be odd.</summary>
    private const ulong Gamma = 0x9E37_79B9_7F4A_7C15UL;

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
    [InlineData(0xBB)]
    [InlineData(0x20)]
    public void Reports_a_zlib_header_that_asks_for_a_preset_dictionary_instead_of_throwing(int flags)
    {
        // A PDF stream cannot supply a preset dictionary. Its zlib raises an IOException rather than an
        // InvalidDataException for a header that asks for one; it escaped the filter, and a document whose
        // object stream or cross-reference stream began so could not be opened at all.
        var compressed = Compress(ContentStream(50), CompressionLevel.Optimal);
        compressed[1] = (byte)flags;
        var diagnostics = new PdfDiagnostics();

        var decoded = Decode(compressed, PdfName.FlateDecode, diagnostics);

        decoded.ToArray().Should().Equal(compressed, "data nothing could decode is left encoded");
        Describe(diagnostics).Should().Be(Report(PdfDiagnosticSeverity.Warning, NotDecoded));
    }

    [Fact]
    public void Decodes_damaged_data_with_nowhere_to_report_it()
    {
        // A stream built in memory and decoded without diagnostics has no document to report to: it decodes
        // as far as it goes all the same, and nothing is thrown for want of a report.
        var plain = ContentStream(100);
        var compressed = Compress(plain, CompressionLevel.Optimal);
        byte[][] damaged =
        [
            compressed.AsSpan(0, compressed.Length / 2).ToArray(),
            compressed.AsSpan(0, compressed.Length - ChecksumLength).ToArray(),
            NineBitCodes(256, 'A', 300),
        ];

        foreach (var data in damaged)
        {
            var dictionary = new PdfDictionary();
            dictionary.Set(PdfName.Filter, data.Length < 16 ? PdfName.LZWDecode : PdfName.FlateDecode);
            var stream = new PdfStream(dictionary, PdfStreamData.FromMemory(data));

            var decoded = stream.Decode();

            decoded.Length.Should().BeGreaterThan(0);
        }
    }

    [Fact]
    public void Never_takes_raw_deflate_that_ran_out_for_a_zlib_stream_that_lost_its_checksum()
    {
        // Four bytes that are not a zlib header, read as raw deflate: a stored block that ran out. Past its
        // first two bytes lies an empty last block, which a zlib stream's body could be — but raw deflate has
        // no checksum to lose.
        var diagnostics = new PdfDiagnostics();

        Decode([0x00, 0x00, 0x03, 0x00], PdfName.FlateDecode, diagnostics);

        Describe(diagnostics).Should().Be(Report(PdfDiagnosticSeverity.Repair, NotZlib) + "; " + Report(PdfDiagnosticSeverity.Warning, TailLost));
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

    [Theory]
    [MemberData(nameof(Payloads))]
    public void Keeps_all_of_a_zlib_stream_whose_checksum_is_wrong_and_warns_that_some_may_be_wrong(string payload, byte[] plain)
    {
        // A whole checksum that disagrees with whole data: the framework throws from the read that meets it, and
        // what that read decoded was lost with it — all of it for data one read takes, which was left encoded. The
        // body read again as raw deflate reads to its end, so every byte is kept, as other readers keep it; the
        // disagreement is a warning of its own, since the data may be wrong anywhere. Whatever the stream's length
        // took in after the checksum changes nothing.
        var compressed = WithWrongChecksum(Compress(plain, CompressionLevel.Optimal));
        byte[][] tails = [[], [0x0A], [0x0D], [0x0D, 0x0A]];
        var failures = new List<string>();

        foreach (var tail in tails)
        {
            var diagnostics = new PdfDiagnostics();
            var decoded = Decode([.. compressed, .. tail], PdfName.FlateDecode, diagnostics).ToArray();

            if (!decoded.AsSpan().SequenceEqual(plain) || Describe(diagnostics) != ChecksumReport(plain.Length))
            {
                failures.Add($"followed by {Convert.ToHexString(tail)}: {decoded.Length} bytes, [{Describe(diagnostics)}]");
            }
        }

        failures.Should().BeEmpty($"{payload} decoded whole");
    }

    [Fact]
    public void Decodes_a_short_zlib_stream_whose_checksum_is_wrong_rather_than_leaving_it_encoded()
    {
        // The inflater takes the whole of it at once, and decodes it in the one read that meets the checksum: that
        // read lost everything, and the data was left encoded as if nothing could decode it.
        var plain = ContentStream(100);
        var compressed = WithWrongChecksum(Compress(plain, CompressionLevel.Optimal));
        compressed.Length.Should().BeLessThan(InflaterInput);
        var diagnostics = new PdfDiagnostics();

        var decoded = Decode(compressed, PdfName.FlateDecode, diagnostics);

        decoded.ToArray().Should().Equal(plain);
        Describe(diagnostics).Should().Be(ChecksumReport(plain.Length));
    }

    [Fact]
    public void Keeps_every_byte_decoded_before_the_fault_of_a_stream_that_turns_corrupt_and_says_where_it_lies()
    {
        // 131,071 zeros take a few hundred bytes of input and two reads of 64 KB: the read that met the fault had
        // decoded the last 65,535, and lost them. Read again one byte at a time through the input the fault was met
        // in, the stream keeps all of them. The fault lies in the byte after the flushed zeros, which the stream
        // was made to hold: that is the one reported, among all the data.
        var plain = new byte[131_071];
        var data = CorruptAfter(plain, out var faultAt, after: [0x00, 0x00, 0x00]);
        var diagnostics = new PdfDiagnostics();

        var decoded = Decode(data, PdfName.FlateDecode, diagnostics);

        decoded.ToArray().Should().Equal(plain);
        Describe(diagnostics).Should().Be(Report(PdfDiagnosticSeverity.Warning, CorruptAt(faultAt, data.Length, plain.Length)));
    }

    [Fact]
    public void Keeps_what_decoded_before_a_fault_in_the_first_input_the_inflater_took_rather_than_leaving_it_encoded()
    {
        // The whole stream is one input and one read: the read that met the fault lost everything before it, and
        // the data was left encoded as if nothing could decode it.
        var plain = ContentStream(50);
        var data = CorruptAfter(plain, out var faultAt);
        data.Length.Should().BeLessThan(InflaterInput);
        var diagnostics = new PdfDiagnostics();

        var decoded = Decode(data, PdfName.FlateDecode, diagnostics);

        decoded.ToArray().Should().Equal(plain);
        Describe(diagnostics).Should().Be(Report(PdfDiagnosticSeverity.Warning, CorruptAt(faultAt, data.Length, plain.Length)));
    }

    [Fact]
    public void Leaves_encoded_a_stream_that_turns_corrupt_before_anything_decodes()
    {
        // A header, then a first block deflate does not define: nothing decoded before the fault, read any way.
        byte[] data = [0x78, 0x9C, UndefinedBlock, 0x00, 0x00, 0x00];
        var diagnostics = new PdfDiagnostics();

        var decoded = Decode(data, PdfName.FlateDecode, diagnostics);

        decoded.ToArray().Should().Equal(data, "data nothing could decode is left encoded");
        Describe(diagnostics).Should().Be(Report(PdfDiagnosticSeverity.Warning, NotDecoded));
    }

    [Theory]
    [InlineData("raw deflate")]
    [InlineData("zlib after white space")]
    public void Keeps_what_decoded_before_the_fault_of_a_stream_that_is_not_zlib_as_the_file_wrote_it(string form)
    {
        // Raw deflate is read again one byte at a time from the input the fault was met in, as a zlib body is; zlib
        // after white space as zlib is. The byte reported counts from the start of the data the filter was given,
        // the white space included.
        var plain = ContentStream(3000);
        var raw = CorruptAfter(plain, out var rawFault, zlib: false);
        var zlib = CorruptAfter(plain, out var zlibFault);
        var (data, faultAt) = form == "raw deflate" ? (raw, rawFault) : ([0x0D, 0x0A, .. zlib], zlibFault + 2);
        data.Length.Should().BeGreaterThan(InflaterInput, "the fault must lie past the first input the inflater takes");
        var diagnostics = new PdfDiagnostics();

        var decoded = Decode(data, PdfName.FlateDecode, diagnostics);

        decoded.ToArray().Should().Equal(plain);
        Describe(diagnostics).Should().Be(
            Report(PdfDiagnosticSeverity.Repair, NotZlib) + "; " + Report(PdfDiagnosticSeverity.Warning, CorruptAt(faultAt, data.Length, plain.Length)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Reads_zlib_after_white_space_rather_than_the_bytes_raw_deflate_decodes_from_it_before_a_fault(bool corrupt)
    {
        // A line feed reads as the first byte of a block of fixed codes, which decodes a byte from the white space and
        // the header after it before it faults. Once lost with the read that met the fault, that byte is now kept:
        // the zlib stream after the white space reads further, whole or up to a fault of its own, and is taken over
        // it.
        var plain = Encoding.ASCII.GetBytes("Hello, PDF filters!Hello, PDF filters!Hello, PDF filters!");
        var faultAt = 0;
        var zlib = corrupt ? CorruptAfter(plain, out faultAt, level: CompressionLevel.SmallestSize) : Compress(plain, CompressionLevel.SmallestSize);
        var diagnostics = new PdfDiagnostics();

        var decoded = Decode([0x0A, 0x20, .. zlib], PdfName.FlateDecode, diagnostics);

        decoded.ToArray().Should().Equal(plain);
        Describe(diagnostics).Should().Be(
            Report(PdfDiagnosticSeverity.Repair, NotZlib) +
            (corrupt ? "; " + Report(PdfDiagnosticSeverity.Warning, CorruptAt(faultAt + 2, zlib.Length + 2, plain.Length)) : string.Empty));
    }

    [Fact]
    public void Keeps_raw_deflate_that_starts_with_a_line_feed_over_the_few_bytes_a_zlib_header_after_it_passes_for()
    {
        // A block of fixed codes that holds six bytes, then a flush: its first byte is a line feed, and the two after it
        // pass for a zlib header, behind which the bytes decode to four bytes before they fault — long before the raw
        // deflate, which goes on with a content stream and turns corrupt at its end. The reading that goes further is
        // what the data is: the zlib reading after the white space is not taken over it.
        byte[] head = [0x0A, 0x08, 0x5B, 0x72, 0xD2, 0xFF, 0x22, 0x00, 0x00, 0x00, 0xFF, 0xFF];
        byte[] headPlain = [0x50, 0x56, 0xA4, 0xC9, 0x4F, 0xD1];
        var content = ContentStream(3000);
        byte[] data = [.. head, .. CorruptAfter(content, out var contentFault, zlib: false)];
        FlateFilter.HasPlainZlibHeader(data.AsSpan(1)).Should().BeTrue();
        FlateFilter.TryDecode(data.AsMemory(1), out var passedFor, out _, out var passedForEnding, out var passedForFault, out _, int.MaxValue)
            .Should().BeTrue();
        passedForEnding.Should().Be(FlateEnding.Corrupt);
        passedFor.Length.Should().BeLessThan(headPlain.Length);
        passedForFault.Should().BeLessThan(head.Length + 4, "the zlib reading faults within the first bytes after the head");
        var diagnostics = new PdfDiagnostics();

        var decoded = Decode(data, PdfName.FlateDecode, diagnostics);

        decoded.ToArray().Should().Equal([.. headPlain, .. content]);
        Describe(diagnostics).Should().Be(
            Report(PdfDiagnosticSeverity.Repair, NotZlib) + "; " +
            Report(PdfDiagnosticSeverity.Warning, CorruptAt(head.Length + contentFault, data.Length, headPlain.Length + content.Length)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Reads_raw_deflate_that_starts_with_a_byte_of_white_space_as_raw_deflate(bool corrupt)
    {
        // Bytes that do not compress are stored: the first block, not the last, starts with a zero byte, which is
        // white space. Nothing after it reads as zlib, and the data is raw deflate, whole or turned corrupt.
        var plain = RandomBytes(100_000);
        var faultAt = 0;
        var data = corrupt ? CorruptAfter(plain, out faultAt, zlib: false) : CompressRaw(plain);
        data[0].Should().Be(0x00);
        var diagnostics = new PdfDiagnostics();

        var decoded = Decode(data, PdfName.FlateDecode, diagnostics);

        decoded.ToArray().Should().Equal(plain);
        Describe(diagnostics).Should().Be(
            Report(PdfDiagnosticSeverity.Repair, NotZlib) +
            (corrupt ? "; " + Report(PdfDiagnosticSeverity.Warning, CorruptAt(faultAt, data.Length, plain.Length)) : string.Empty));
    }

    [Fact]
    public void Keeps_exactly_what_decoded_before_a_block_deflate_does_not_define_wherever_the_inflater_meets_it()
    {
        // Data of any kind and length, compressed at any level and flushed to a byte boundary, then a block deflate
        // does not define: whatever read and whichever input of the inflater's the fault falls in, the data is kept
        // whole, and the fault is reported at the byte after it — both known from how the stream was made, not from
        // how it was read. Raw deflate, and zlib after white space, alike.
        var cases =
            from length in Gen.Choose(1, 200_000)
            from text in Gen.Elements(true, false)
            from level in Gen.Elements(CompressionLevel.Optimal, CompressionLevel.Fastest, CompressionLevel.NoCompression, CompressionLevel.SmallestSize)
            from form in Gen.Elements("zlib", "raw deflate", "zlib after white space")
            from last in Gen.Elements(true, false)
            from after in Gen.Choose(0, 16)
            select (length, text, level, form, last, after);

        Check.One(Properties(100), Prop.ForAll(cases.ToArbitrary(), c =>
        {
            var plain = c.text ? Text(c.length) : RandomBytes(c.length);
            var data = CorruptAfter(plain, out var faultAt, zlib: c.form != "raw deflate", c.level, new byte[c.after], c.last);
            var expected = Report(PdfDiagnosticSeverity.Warning, CorruptAt(faultAt, data.Length, plain.Length));

            if (c.form == "zlib after white space")
            {
                data = [0x0D, 0x0A, .. data];
                expected = Report(PdfDiagnosticSeverity.Warning, CorruptAt(faultAt + 2, data.Length, plain.Length));
            }

            if (c.form != "zlib")
            {
                expected = Report(PdfDiagnosticSeverity.Repair, NotZlib) + "; " + expected;
            }

            var diagnostics = new PdfDiagnostics();
            var decoded = Decode(data, PdfName.FlateDecode, diagnostics);

            return decoded.Span.SequenceEqual(plain) && Describe(diagnostics) == expected;
        }));
    }

    [Fact]
    public void Keeps_all_of_any_zlib_stream_whose_checksum_is_wrong()
    {
        // Data of any kind and length, compressed at any level, one byte of its checksum wrong, and what a stream's
        // length often takes in after it: all of it is kept, and the disagreement reported.
        var cases =
            from length in Gen.Choose(0, 200_000)
            from text in Gen.Elements(true, false)
            from level in Gen.Elements(CompressionLevel.Optimal, CompressionLevel.Fastest, CompressionLevel.NoCompression, CompressionLevel.SmallestSize)
            from wrong in Gen.Choose(1, ChecksumLength)
            from flip in Gen.Choose(1, 255)
            from tail in Gen.Elements(Array.Empty<byte>(), [0x0A], [0x0D], [0x0D, 0x0A])
            select (length, text, level, wrong, flip, tail);

        Check.One(Properties(100), Prop.ForAll(cases.ToArbitrary(), c =>
        {
            var plain = c.text ? Text(c.length) : RandomBytes(c.length);
            var compressed = Compress(plain, c.level);
            compressed[^c.wrong] ^= (byte)c.flip;
            var diagnostics = new PdfDiagnostics();

            var decoded = Decode([.. compressed, .. c.tail], PdfName.FlateDecode, diagnostics);

            return decoded.Span.SequenceEqual(plain) && Describe(diagnostics) == ChecksumReport(plain.Length);
        }));
    }

    [Theory]
    [InlineData("checksum")]
    [InlineData("corrupt")]
    public void Reading_damaged_data_again_up_to_the_bound_reports_only_the_bound(string damage)
    {
        // The reading that met the fault kept less than the bound, and reading again reaches it. That stops short of
        // the fault, which is never seen: only the reader's guard is reported, as for any data that reaches it.
        var (data, bound) = BoundByReadingAgain(damage);
        var diagnostics = new PdfDiagnostics();

        var decoded = DecodeWithin(data, bound, diagnostics);

        decoded.Length.Should().Be(bound);
        diagnostics.Select(d => d.Code).Should().Equal(PdfDiagnosticCodes.LimitDecodedStream);
    }

    [Theory]
    [InlineData("checksum")]
    [InlineData("corrupt")]
    public void Reading_damaged_data_again_up_to_the_bound_throws_when_the_document_asked_for_it(string damage)
    {
        var (data, bound) = BoundByReadingAgain(damage);
        var diagnostics = new PdfDiagnostics();

        var thrown = FluentActions.Invoking(() => DecodeWithin(data, bound, diagnostics, throwOnLimit: true))
            .Should().Throw<PdfLimitExceededException>().Which;

        thrown.Code.Should().Be(PdfDiagnosticCodes.LimitDecodedStream);
        diagnostics.Should().BeEmpty();
    }

    [Theory]
    [InlineData("sound")]
    [InlineData("checksum")]
    [InlineData("corrupt")]
    public void Reading_damaged_data_again_allocates_no_second_output_and_sound_data_nothing_to_read_it_again_with(string damage)
    {
        // Within a bound equal to what 2 MB of data that does not compress decode to, the output is one array of
        // exactly that size, handed back as it is. Sound data allocates what the least decoding of it does — that
        // array and one inflater's own objects — and the stream it is read from. Data read again after a fault — its
        // body as raw deflate, then one byte at a time — adds what it decodes to the same array: give or take the
        // inflaters' own objects, it allocates what sound data does, neither a second output nor a copy of its input.
        // That sound data is read once the margin cannot show: a second reading costs a few hundred bytes, its input
        // pooled and its inflater's state native. The count of reads below shows it.
        const int Length = 2 * 1024 * 1024;
        var plain = RandomBytes(Length);
        var sound = Compress(plain, CompressionLevel.Optimal);
        var data = damage switch
        {
            "sound" => sound,
            "checksum" => WithWrongChecksum(sound),
            _ => CorruptAfter(plain, out _),
        };
        LeastAllocation(sound, Length);
        DecodeWithin(data, Length, new PdfDiagnostics());
        var diagnostics = new PdfDiagnostics();

        var least = LeastAllocation(sound, Length);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var decoded = DecodeWithin(data, Length, diagnostics);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        decoded.Length.Should().Be(Length);
        diagnostics.Select(d => d.Code).Should().NotContain(PdfDiagnosticCodes.LimitDecodedStream);
        (allocated - least).Should().BeLessThan(damage == "sound" ? 1024 : 16 * 1024);
    }

    [Fact]
    public void Tries_raw_deflate_that_starts_with_white_space_and_turns_corrupt_as_zlib_after_it_only_behind_a_zlib_header()
    {
        // Bytes that do not compress are stored, and raw deflate of them starts with a zero byte, which is white space;
        // turned corrupt, it is read as zlib after the white space only when a zlib header follows, and none does here.
        // Within a bound equal to what the data decodes to, it allocates the output it keeps, and the one the zlib
        // reading from its first byte takes before it meets the header: not a third, which a zlib reading after the
        // white space would take before it met its header.
        const int Length = 2 * 1024 * 1024;
        var plain = RandomBytes(Length);
        var data = CorruptAfter(plain, out _, zlib: false);
        data[0].Should().Be(0x00);
        FlateFilter.HasPlainZlibHeader(data.AsSpan(1)).Should().BeFalse();
        var least = LeastAllocation(Compress(plain, CompressionLevel.Optimal), Length);
        DecodeWithin(data, Length, new PdfDiagnostics());

        var before = GC.GetAllocatedBytesForCurrentThread();
        var decoded = DecodeWithin(data, Length, new PdfDiagnostics());
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        decoded.ToArray().Should().Equal(plain);
        (allocated - least).Should().BeLessThan(Length + (16 * 1024));
    }

    [Fact]
    public void Reads_sound_data_once()
    {
        // The inflater takes its input a piece at a time, and each piece is one look at the memory behind the data:
        // one reading of 2 MB takes 257 pieces, and a second would take as many again. A few looks more are the
        // filter's own, at the data's first bytes.
        var sound = Compress(RandomBytes(2 * 1024 * 1024), CompressionLevel.Optimal);
        using var memory = new UnexposedMemory(sound);

        var decoded = DecodeWithin(memory.Memory, int.MaxValue, new PdfDiagnostics());

        decoded.Length.Should().Be(2 * 1024 * 1024);
        memory.Looks.Should().BeLessThanOrEqualTo(Pieces(sound.Length) + 4);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Reads_again_a_byte_at_a_time_only_the_piece_of_input_the_fault_was_met_in(bool zlib)
    {
        // 2 MB that do not compress, turned corrupt at their end: read again after the fault, the data is handed over
        // at full speed up to the piece the fault was met in, and a byte at a time through that piece alone — never
        // more than one, whatever the data's length. Each reading at full speed takes as many pieces as the first;
        // zlib's body is read again once more than raw deflate.
        var plain = RandomBytes(2 * 1024 * 1024);
        var data = CorruptAfter(plain, out var faultAt, zlib);
        using var memory = new UnexposedMemory(data);

        FlateFilter.TryDecode(memory.Memory, out var decoded, out _, out var ending, out var reported, out _, int.MaxValue)
            .Should().BeTrue();

        decoded.Should().Equal(plain);
        ending.Should().Be(FlateEnding.Corrupt);
        reported.Should().Be(faultAt);
        memory.Looks.Should().BeLessThanOrEqualTo((3 * Pieces(data.Length)) + FlateInput.ChunkLength + 16);
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
        // That second reading keeps nothing, and copies nothing: decoding 2 MB of data that does not compress,
        // whose checksum is missing, allocates what decoding them whole does, give or take the inflater's own
        // objects — not a second output, nor a copy of the 2 MB body.
        const int Length = 2 * 1024 * 1024;
        var compressed = Compress(RandomBytes(Length), CompressionLevel.Optimal);
        var withoutChecksum = compressed.AsSpan(0, compressed.Length - ChecksumLength).ToArray();
        Decode(compressed, PdfName.FlateDecode, new PdfDiagnostics());
        var diagnostics = new PdfDiagnostics();

        var beforeWhole = GC.GetAllocatedBytesForCurrentThread();
        Decode(compressed, PdfName.FlateDecode, new PdfDiagnostics());
        var whole = GC.GetAllocatedBytesForCurrentThread() - beforeWhole;
        var beforeCut = GC.GetAllocatedBytesForCurrentThread();
        var decoded = Decode(withoutChecksum, PdfName.FlateDecode, diagnostics);
        var cut = GC.GetAllocatedBytesForCurrentThread() - beforeCut;

        (cut - whole).Should().BeLessThan(16 * 1024);
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

    [Fact]
    public void The_flate_input_only_reads_forward()
    {
        // The inflater reads its input once, from the start: the stream neither seeks, nor says its length or
        // position, nor takes writes, and flushing it does nothing.
        using var input = new FlateInput("abc"u8.ToArray());

        input.CanRead.Should().BeTrue();
        FluentActions.Invoking(() => input.Length).Should().Throw<NotSupportedException>();
        FluentActions.Invoking(() => input.Position).Should().Throw<NotSupportedException>();
        FluentActions.Invoking(() => input.Position = 1).Should().Throw<NotSupportedException>();
        FluentActions.Invoking(() => input.Seek(0, SeekOrigin.Begin)).Should().Throw<NotSupportedException>();
        FluentActions.Invoking(() => input.SetLength(1)).Should().Throw<NotSupportedException>();
        FluentActions.Invoking(() => input.Write([1], 0, 1)).Should().Throw<NotSupportedException>();
        input.Flush();
        input.ReadByte().Should().Be('a');
    }

    [Fact]
    public void The_flate_input_hands_its_bytes_over_one_at_a_time_from_where_it_is_told_and_says_where_the_last_started()
    {
        using var input = new FlateInput("abcdefgh"u8.ToArray(), slowFrom: 5);
        var buffer = new byte[4];

        input.Read(buffer, 0, 4).Should().Be(4);
        input.LastReadStart.Should().Be(0);
        input.Read(buffer, 0, 4).Should().Be(1, "a read at full speed stops where reading one byte at a time starts");
        input.LastReadStart.Should().Be(4);
        input.Read(buffer, 0, 4).Should().Be(1);
        buffer[0].Should().Be((byte)'f');
        input.LastReadStart.Should().Be(5);
        input.BytesRead.Should().Be(6);

        input.Read(buffer, 0, 4).Should().Be(1);
        input.Read(buffer, 0, 4).Should().Be(1);
        input.Read(buffer, 0, 4).Should().Be(0);
        input.ReadPastEnd.Should().BeTrue();
        input.BytesRead.Should().Be(8);
        input.LastReadStart.Should().Be(7, "asking past the end hands nothing over");
    }

    [Fact]
    public void The_flate_input_hands_over_no_more_than_a_piece_at_once_however_much_is_asked_for()
    {
        // The piece is what the framework's inflater asks for at once; a reader that asked for more would make the
        // part read again a byte at a time after a fault longer, and it is not given more.
        using var input = new FlateInput(new byte[(3 * FlateInput.ChunkLength) + 1]);
        var buffer = new byte[4 * FlateInput.ChunkLength];

        input.Read(buffer.AsSpan()).Should().Be(FlateInput.ChunkLength);
        input.Read(buffer.AsSpan()).Should().Be(FlateInput.ChunkLength);
        input.LastReadStart.Should().Be(FlateInput.ChunkLength);
    }

    [Theory]
    [InlineData(new byte[] { 0x78, 0x9C }, true)]
    [InlineData(new byte[] { 0x78, 0xDA }, true)]
    [InlineData(new byte[] { 0x08, 0x1D }, true)]
    [InlineData(new byte[] { 0x78 }, false)]
    [InlineData(new byte[] { 0x79, 0x18 }, false)]
    [InlineData(new byte[] { 0x88, 0x1C }, false)]
    [InlineData(new byte[] { 0x78, 0x9D }, false)]
    [InlineData(new byte[] { 0x78, 0xBB }, false)]
    public void A_zlib_header_is_read_past_only_with_deflate_a_window_of_at_most_32_KB_a_check_that_holds_and_no_preset_dictionary(
        byte[] header,
        bool plain)
    {
        // Deflate, a window of 256 bytes to 32 KB, a check that holds, and no preset dictionary. A byte alone is
        // refused, and each of the other refused headers fails one of these alone.
        FlateFilter.HasPlainZlibHeader(header).Should().Be(plain);
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

    /// <summary>
    /// Codes after a table that held "AB", "BC" and "CD" was cleared and 'X' read: 258 is again the next to define,
    /// while the table still holds what it defined before the clear.
    /// </summary>
    [Theory]
    [InlineData(258, "ABCDXXXY", -1)]
    [InlineData(259, "ABCDX", 259)]
    [InlineData(260, "ABCDX", 260)]
    public void Decodes_nothing_the_table_held_before_it_was_cleared(int code, string expected, int undefined)
    {
        // A clear starts the table again but leaves its old entries in place: 258 after it is the sequence
        // being defined, "XX", not the "AB" it held before, and 259 and 260 are not defined at all.
        var diagnostics = new PdfDiagnostics();

        var decoded = Decode(NineBitCodes(256, 'A', 'B', 'C', 'D', 256, 'X', code, 'Y', 257), PdfName.LZWDecode, diagnostics);

        Encoding.ASCII.GetString(decoded.Span).Should().Be(expected);
        Describe(diagnostics).Should().Be(undefined < 0 ? string.Empty : Report(PdfDiagnosticSeverity.Warning, LzwUndefined(undefined)));
    }

    [Fact]
    public void Extends_a_longer_sequence_with_its_first_byte_when_its_code_is_the_one_being_defined()
    {
        // 'A', 'B', then 258 ("AB") defines 259 as "BA"; 260 is being defined by that very sequence: "AB" and
        // its own first byte, "ABA" — not its last.
        var diagnostics = new PdfDiagnostics();

        var decoded = Decode(NineBitCodes(256, 'A', 'B', 258, 260, 257), PdfName.LZWDecode, diagnostics);

        Encoding.ASCII.GetString(decoded.Span).Should().Be("ABABABA");
        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void Reads_an_lzw_stream_whose_code_width_grows_on_time_when_it_says_so()
    {
        // /EarlyChange 0 grows the code width one code later than the default. Read with the default, the
        // same data is read at the wrong width from the 511th code on.
        var plain = ContentStream(40);
        var encoded = LzwEncode(plain, earlyChange: 0);
        var parameters = new PdfDictionary();
        parameters.Set(PdfName.EarlyChange, PdfInteger.Create(0));
        var onTime = new PdfDiagnostics();

        var decoded = Decode(encoded, PdfName.LZWDecode, onTime, parameters).ToArray();
        var early = Decode(encoded, PdfName.LZWDecode, new PdfDiagnostics()).ToArray();

        decoded.Should().Equal(plain);
        onTime.Should().BeEmpty();
        early.Should().NotEqual(plain);
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

    /// <summary>Damaged data a producer wrote whole, with a /Length that matches it: each report, its severity and message.</summary>
    public static TheoryData<string, bool> DocumentDamages => new()
    {
        { "tail", false }, { "tail", true },
        { "checksum", false }, { "checksum", true },
        { "wrong checksum", false }, { "wrong checksum", true },
        { "corrupt", false }, { "corrupt", true },
        { "lzw", false }, { "lzw", true },
    };

    [Theory]
    [MemberData(nameof(DocumentDamages))]
    public void Reports_a_document_s_damaged_stream_where_its_data_starts(string damage, bool callerCollects)
    {
        // The report goes where the caller asks, or to the document when the caller passes nowhere, and names
        // where the data starts. The data travels in hex, so that the file stays text.
        var plain = ContentStream(100);
        var compressed = Compress(plain, CompressionLevel.Optimal);
        var corrupt = CorruptAfter(plain, out var faultAt);
        var (filter, data, code, severity, message) = damage switch
        {
            "tail" => ("/FlateDecode", compressed.AsSpan(0, compressed.Length / 2).ToArray(), PdfDiagnosticCodes.FilterFailed, PdfDiagnosticSeverity.Warning, TailLost),
            "checksum" => ("/FlateDecode", compressed.AsSpan(0, compressed.Length - ChecksumLength).ToArray(), PdfDiagnosticCodes.FilterFailed, PdfDiagnosticSeverity.Repair, ChecksumMissing),
            "wrong checksum" => ("/FlateDecode", WithWrongChecksum(compressed), PdfDiagnosticCodes.FilterChecksumMismatch, PdfDiagnosticSeverity.Warning, ChecksumMismatch(plain.Length)),
            "corrupt" => ("/FlateDecode", corrupt, PdfDiagnosticCodes.FilterFailed, PdfDiagnosticSeverity.Warning, CorruptAt(faultAt, corrupt.Length, plain.Length)),
            _ => ("/LZWDecode", NineBitCodes(256, 'A', 'B', 300, 257), PdfDiagnosticCodes.FilterFailed, PdfDiagnosticSeverity.Warning, LzwUndefined(300)),
        };
        var hex = Convert.ToHexString(data) + ">";
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .Stream(3, $"/Filter [/ASCIIHexDecode {filter}]", hex)
            .BuildClassic(rootNumber: 1);
        var dataStart = file.AsSpan().IndexOf(Encoding.ASCII.GetBytes(hex));

        using var document = PdfDocument.Open(file);
        var collected = new PdfDiagnostics();
        var decoded = document.GetObject(new PdfObjectId(3)).AsStream().Required().Decode(callerCollects ? collected : null);

        decoded.Length.Should().BeGreaterThan(0);
        (callerCollects ? document.Diagnostics : collected).Should().BeEmpty();
        var report = (callerCollects ? collected : document.Diagnostics).Should().ContainSingle().Which;
        report.Code.Should().Be(code);
        report.Severity.Should().Be(severity);
        report.Message.Should().Be(message);
        report.Position.Should().Be(dataStart);
    }

    [Theory]
    [InlineData("checksum")]
    [InlineData("corrupt")]
    public void Counts_the_bytes_a_damaged_stream_kept_before_its_predictor_takes_the_tag_off_each_row(string damage)
    {
        // 200 rows of four bytes, each behind the tag of the PNG predictor that changes nothing: 1,000 bytes decode,
        // and the predictor hands 800 on. The report counts what the Flate data decoded to, the tags included.
        var rows = new byte[1000];
        var predicted = new byte[800];
        for (var index = 0; index < predicted.Length; index++)
        {
            predicted[index] = (byte)(index * 7);
            rows[(index / 4 * 5) + 1 + (index % 4)] = predicted[index];
        }

        var faultAt = 0;
        var data = damage == "checksum" ? WithWrongChecksum(Compress(rows, CompressionLevel.Optimal)) : CorruptAfter(rows, out faultAt);
        var parameters = new PdfDictionary();
        parameters.Set(PdfName.Predictor, PdfInteger.Create(12));
        parameters.Set(PdfName.Columns, PdfInteger.Create(4));
        var diagnostics = new PdfDiagnostics();

        var decoded = Decode(data, PdfName.FlateDecode, diagnostics, parameters);

        decoded.ToArray().Should().Equal(predicted);
        Describe(diagnostics).Should().Be(
            damage == "checksum" ? ChecksumReport(1000) : Report(PdfDiagnosticSeverity.Warning, CorruptAt(faultAt, data.Length, 1000)));
    }

    [Fact]
    public void Reports_a_fault_in_the_part_of_a_stream_a_guard_of_the_reader_kept_and_counts_that_part()
    {
        // A /Length far too short sends the reader looking for endstream, past the largest window MaxObjectLength
        // allows, which ends 10,000 bytes into the stream's data, well past the byte its Flate data turns corrupt
        // at. The guard is reported, and so is the fault, which lies in bytes the reader read and is the file's:
        // counted in the data the Flate filter was given, which is what the window kept.
        const int Kept = 10_000;
        var plain = ContentStream(200);
        var hex = Convert.ToHexString(CorruptAfter(plain, out var faultAt, after: RandomBytes(60_000))) + ">";
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(3, $"<< /Filter [/ASCIIHexDecode /FlateDecode] /Length 10 >>\nstream\n{hex}\nendstream")
            .BuildClassic(rootNumber: 1);
        var header = file.AsSpan().IndexOf("3 0 obj"u8);
        var dataStart = file.AsSpan().IndexOf(Encoding.ASCII.GetBytes(hex));
        var options = PdfReaderOptions.Default with
        {
            Limits = PdfReaderLimits.Default with { MaxObjectLength = dataStart + (2 * Kept) - header },
        };
        faultAt.Should().BeLessThan(Kept);

        using var document = PdfDocument.Open(file, options);
        var decoded = document.GetObject(new PdfObjectId(3)).AsStream().Required().Decode(document.Diagnostics);

        decoded.ToArray().Should().Equal(plain);
        document.Diagnostics.Select(d => d.Code).Should().Equal(PdfDiagnosticCodes.LimitObject, PdfDiagnosticCodes.FilterFailed);
        document.Diagnostics.Last().Message.Should().Be(CorruptAt(faultAt, Kept, plain.Length));
    }

    [Fact]
    public void Says_nothing_of_the_tail_a_guard_of_the_reader_cut_off()
    {
        // A /Length far too short sends the reader looking for endstream, which lies past the largest window
        // MaxObjectLength allows: the object is kept as far as the window went, and the guard is reported.
        // That the Flate data then runs out is the reader's doing, not the file's.
        var compressed = Compress(RandomBytes(100_000), CompressionLevel.Optimal);
        var hex = Convert.ToHexString(compressed) + ">";
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(3, $"<< /Filter [/ASCIIHexDecode /FlateDecode] /Length 10 >>\nstream\n{hex}\nendstream")
            .BuildClassic(rootNumber: 1);
        var options = PdfReaderOptions.Default with { Limits = PdfReaderLimits.Default with { MaxObjectLength = 64 * 1024 } };

        using var cut = PdfDocument.Open(file, options);
        using var whole = PdfDocument.Open(file);
        cut.GetObject(new PdfObjectId(3)).AsStream().Required().Decode(cut.Diagnostics);
        whole.GetObject(new PdfObjectId(3)).AsStream().Required().Decode(whole.Diagnostics).Length.Should().Be(100_000);

        cut.Diagnostics.Select(d => d.Code).Should().Equal(PdfDiagnosticCodes.LimitObject);
        whole.Diagnostics.Select(d => d.Code).Should().Equal(PdfDiagnosticCodes.StreamLengthInvalid);
    }

    [Theory]
    [InlineData(-2)]
    [InlineData(100)]
    public void Says_nothing_of_a_guard_s_cut_that_falls_in_the_checksum_or_after_the_data(int bytesPastTheBody)
    {
        // The largest window MaxObjectLength allows ends 2 bytes into the checksum, or in the white space after
        // the whole zlib stream: a checksum the reader cut is no repair of the file's, and data that ended
        // before the cut is whole. Only the guard is reported.
        var compressed = Compress(ContentStream(200), CompressionLevel.Optimal);
        var hex = Convert.ToHexString(compressed);
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(3, $"<< /Filter [/ASCIIHexDecode /FlateDecode] /Length 10 >>\nstream\n{hex}{new string(' ', 400)}>\nendstream")
            .BuildClassic(rootNumber: 1);
        var header = file.AsSpan().IndexOf("3 0 obj"u8);
        var data = file.AsSpan().IndexOf(Encoding.ASCII.GetBytes(hex));
        var bodyEnd = data + (2 * (compressed.Length - ChecksumLength));
        var cut = bytesPastTheBody < 0 ? bodyEnd + (2 * -bytesPastTheBody) : data + hex.Length + bytesPastTheBody;
        var options = PdfReaderOptions.Default with { Limits = PdfReaderLimits.Default with { MaxObjectLength = cut - header } };

        using var document = PdfDocument.Open(file, options);
        var decoded = document.GetObject(new PdfObjectId(3)).AsStream().Required().Decode(document.Diagnostics);

        decoded.Length.Should().Be(ContentStream(200).Length);
        document.Diagnostics.Select(d => d.Code).Should().Equal(PdfDiagnosticCodes.LimitObject);
    }

    [Theory]
    [InlineData(typeof(InvalidDataException), true)]
    [InlineData(typeof(EndOfStreamException), true)]
    [InlineData(typeof(InvalidOperationException), false)]
    public void Takes_only_the_inflater_s_complaints_about_its_data_as_faults(Type exception, bool fault)
    {
        // zlib raises an IOException for what it cannot go on with, such as a preset dictionary; anything else
        // is not the data's doing, and is not caught.
        FlateFilter.IsInflaterFault((Exception)Activator.CreateInstance(exception)!).Should().Be(fault);
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

    private static string CorruptAt(int faultAt, int encoded, int kept) =>
        $"A Flate stream is corrupt at byte {faultAt} of its {encoded}; the {kept} bytes decoded before the fault was found were kept.";

    private static string ChecksumMismatch(int decoded) =>
        $"A Flate stream's checksum disagrees with the {decoded} bytes its data decoded to; all were kept, and some may be wrong.";

    private static string ChecksumReport(int decoded) =>
        Report(PdfDiagnosticSeverity.Warning, ChecksumMismatch(decoded), PdfDiagnosticCodes.FilterChecksumMismatch);

    private static string Report(PdfDiagnosticSeverity severity, string message, string code = PdfDiagnosticCodes.FilterFailed) =>
        $"{severity} {code}: {message}";

    private static Config Properties(int cases) =>
        Config.QuickThrowOnFailure.WithMaxTest(cases).WithReplay(Seed, Gamma).WithQuietOnSuccess(true);

    /// <summary>
    /// Damaged data whose reading that met the fault keeps less than the bound, and reading again reaches it: a
    /// stream the inflater takes whole, whose checksum is wrong, and 131,071 zeros that turn corrupt, of which the
    /// reading that met the fault kept the first 65,536.
    /// </summary>
    private static (byte[] Data, int Bound) BoundByReadingAgain(string damage)
    {
        if (damage == "checksum")
        {
            var plain = ContentStream(500);
            return (WithWrongChecksum(Compress(plain, CompressionLevel.Optimal)), plain.Length - 1);
        }

        return (CorruptAfter(new byte[131_071], out _), 100_000);
    }

    private static string Describe(PdfDiagnostics diagnostics) =>
        string.Join("; ", diagnostics.Select(d => $"{d.Severity} {d.Code}: {d.Message}"));

    private static ReadOnlyMemory<byte> Decode(byte[] data, PdfName filter, PdfDiagnostics diagnostics, PdfDictionary? parameters = null)
    {
        var dictionary = new PdfDictionary();
        dictionary.Set(PdfName.Filter, filter);

        if (parameters is not null)
        {
            dictionary.Set(PdfName.DecodeParms, parameters);
        }

        return new PdfStream(dictionary, PdfStreamData.FromMemory(data)).Decode(diagnostics);
    }

    /// <summary>How many pieces the framework's inflater takes <paramref name="length"/> bytes of input in.</summary>
    private static int Pieces(int length) => (length + FlateInput.ChunkLength - 1) / FlateInput.ChunkLength;

    private static ReadOnlyMemory<byte> DecodeWithin(ReadOnlyMemory<byte> data, int maxLength, PdfDiagnostics diagnostics, bool throwOnLimit = false)
    {
        var dictionary = new PdfDictionary();
        dictionary.Set(PdfName.Filter, PdfName.FlateDecode);
        var guard = new PdfLimitGuard(PdfReaderLimits.Default with { MaxDecodedStreamLength = maxLength }, throwOnLimit);
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

    /// <summary>The first <paramref name="length"/> bytes of a content stream.</summary>
    private static byte[] Text(int length) => ContentStream((length / 50) + 1)[..length];

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

    /// <summary>What decoding zlib data allocates at the least: its output, and one inflater's own objects.</summary>
    private static long LeastAllocation(byte[] compressed, int length)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        var output = new byte[length];

        using (var zlib = new ZLibStream(new MemoryStream(compressed), CompressionMode.Decompress))
        {
            zlib.ReadExactly(output);
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static byte[] WithWrongChecksum(byte[] compressed)
    {
        byte[] wrong = [.. compressed];
        wrong[^1] ^= 0xFF;
        return wrong;
    }

    /// <summary>
    /// Compresses <paramref name="prefix"/>, flushed so that its data ends at a byte, then a block of the type deflate
    /// reserves — the last one, or not —, and <paramref name="after"/>: an inflater decodes the whole prefix, and
    /// meets the fault in the byte after it, the <paramref name="faultAt"/>th of the data.
    /// </summary>
    private static byte[] CorruptAfter(
        byte[] prefix,
        out int faultAt,
        bool zlib = true,
        CompressionLevel level = CompressionLevel.Optimal,
        byte[]? after = null,
        bool last = true)
    {
        using var compressed = new MemoryStream();
        using Stream deflate = zlib
            ? new ZLibStream(compressed, level, leaveOpen: true)
            : new DeflateStream(compressed, level, leaveOpen: true);
        deflate.Write(prefix);
        deflate.Flush();
        var flushed = compressed.ToArray();
        faultAt = flushed.Length + 1;
        return [.. flushed, (byte)(last ? UndefinedBlock : UndefinedBlock - 1), .. after ?? []];
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

    /// <summary>
    /// Encodes as the PDF variant of LZW does: a clear first, the end-of-data code last, and the code width
    /// growing one code early when <paramref name="earlyChange"/> is 1.
    /// </summary>
    private static byte[] LzwEncode(byte[] data, int earlyChange = 1)
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

            if (next + earlyChange > 1 << bits && bits < 12)
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

    /// <summary>Memory that no array is exposed behind, as a file's own buffer may be, and that counts the looks at it.</summary>
    private sealed class UnexposedMemory(byte[] contents) : System.Buffers.MemoryManager<byte>
    {
        /// <summary>Gets how many times the memory was looked at: once each time a span of it was asked for.</summary>
        public int Looks { get; private set; }

        public override Span<byte> GetSpan()
        {
            Looks++;
            return contents;
        }

        public override System.Buffers.MemoryHandle Pin(int elementIndex = 0) => throw new NotSupportedException();

        public override void Unpin()
        {
        }

        protected override void Dispose(bool disposing)
        {
        }
    }
}
