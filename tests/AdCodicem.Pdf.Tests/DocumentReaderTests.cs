using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.IO;
using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.Tests;

public class DocumentReaderTests
{
    [Fact]
    public void Opens_a_document_indexed_by_a_classic_table()
    {
        using var document = PdfDocument.Open(SampleDocument().BuildClassic(rootNumber: 1));

        document.Version.Should().Be("1.7");
        document.WasRepaired.Should().BeFalse();
        document.Catalog.Required().IsOfType(PdfName.Catalog).Should().BeTrue();
        PageContent(document).Should().Be("BT (Bonjour) Tj ET");
    }

    [Theory]
    [InlineData("%PDF-1.7", "1.7")]
    [InlineData("%PDF-2.0", "2.0")]
    [InlineData("%PDF-1.8", "1.8")]
    [InlineData("%PDF-1.4 ", "1.4")]
    [InlineData("%PDF-1.4\r", "1.4")]
    [InlineData("%PDF-1.4\t", "1.4")]
    [InlineData("%PDF-1.4%comment", "1.4")]
    [InlineData("%XYZ-1.7", null)]
    [InlineData("%PDF-x", null)]
    [InlineData("%PDF-", null)]
    [InlineData("%PDF- 1.7", null)]
    [InlineData("%PDF-1.4.5.6", null)]
    [InlineData("%PDF-1.99999999", null)]
    [InlineData("%PDF-1.7abc", null)]
    [InlineData("%PDF-2.0x", null)]
    [InlineData("%PDF-14", null)]
    [InlineData("%PDF-1.", null)]
    [InlineData("%PDF-.5", null)]
    [InlineData("%PDF-01.4", null)]
    public void The_version_is_the_header_s_when_it_names_one_digit_a_period_and_one_digit(string header, string? version)
    {
        using var document = PdfDocument.Open(PdfTemplate.SoundWith("%PDF-1.7", header));

        document.Version.Should().Be(version);
        document.Catalog.Should().NotBeNull("the header names a version or not; the file reads all the same");
    }

    [Fact]
    public void A_version_the_header_search_cuts_is_none()
    {
        // The header's search ends at the 4,096th byte, right after "1.7": what follows it there is not known.
        var file = Encoding.Latin1.GetBytes(new string('%', 4087) + "\n").Concat(PdfTemplate.Build(PdfTemplate.Sound)).ToArray();

        using var document = PdfDocument.Open(file);

        document.Version.Should().BeNull();
        document.Reader.Structure.HeaderVersion.Should().Be("1.7");
        document.Catalog.Should().NotBeNull();
    }

    [Fact]
    public void A_version_the_end_of_the_file_ends_is_one()
    {
        // The header is the file's last bytes: the end of the file ends its version, as white space would.
        var body = PdfTemplate.Build(PdfTemplate.Sound)[9..];
        var file = body.Concat(Encoding.Latin1.GetBytes("%PDF-1.7")).ToArray();

        using var document = PdfDocument.Open(file);

        document.Version.Should().Be("1.7");
        document.Reader.Structure.HeaderVersionCut.Should().BeFalse();
    }

    [Fact]
    public void A_version_that_ends_where_the_header_search_and_the_file_both_end_is_one()
    {
        // 4,096 bytes in all: the version's last digit is the file's last byte and the search window's.
        var body = PdfTemplate.Build(PdfTemplate.Sound)[9..];
        var file = body.Concat(Encoding.Latin1.GetBytes("\n" + new string('%', 4096 - body.Length - 9) + "%PDF-1.7")).ToArray();
        file.Length.Should().Be(4096);

        using var document = PdfDocument.Open(file);

        document.Version.Should().Be("1.7");
    }

    [Theory]
    [InlineData(9, "/1.7", "1.7")]
    [InlineData(9, "(1.7)", "1.4")]
    [InlineData(8, "/1.7", "1.4")]
    public void A_catalog_s_version_written_as_a_reference_is_read_as_the_document_opens(int named, string value, string version)
    {
        var file = new TestPdfBuilder()
            .WithObject(1, $"<< /Type /Catalog /Pages 2 0 R /Version {named} 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .WithObject(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>")
            .WithObject(9, value)
            .BuildClassic(rootNumber: 1);
        "%PDF-1.4"u8.CopyTo(file);

        using var document = PdfDocument.Open(file);

        document.Version.Should().Be(version);
    }

    [Fact]
    public void Without_a_catalog_the_version_is_the_header_s()
    {
        var file = new TestPdfBuilder()
            .WithObject(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 /Version /1.7 >>")
            .WithObject(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>")
            .BuildClassic(rootNumber: 1);
        "%PDF-1.4"u8.CopyTo(file);

        using var document = PdfDocument.Open(file);

        document.Catalog.Should().BeNull("no object is the catalog /Root names");
        document.Version.Should().Be("1.4");
    }

    [Theory]
    [InlineData("%PDF-1.4", "/Version /1.7", "1.7")]
    [InlineData("%PDF-1.3", "/Version /1.6", "1.6")]
    [InlineData("%PDF-1.4", "/Version /2.0", "2.0")]
    [InlineData("%PDF-1.4", "/Version /1.8", "1.8")]
    [InlineData("%PDF-1.7", "/Version /1.4", "1.7")]
    [InlineData("%PDF-1.7", "/Version /1.7", "1.7")]
    [InlineData("%PDF-1.4", "/Version (1.7)", "1.4")]
    [InlineData("%PDF-1.4", "/Version /1.7.1", "1.4")]
    [InlineData("%PDF-1.4", "/Version /17", "1.4")]
    [InlineData("%PDF-1.4", "/Version 1.7", "1.4")]
    [InlineData("%PDF-x", "/Version /1.7", null)]
    [InlineData("%XYZ-1.7", "/Version /1.7", null)]
    public void The_version_is_the_catalog_s_when_it_is_written_as_one_and_later_than_the_header_s(
        string header, string catalog, string? version)
    {
        var file = PdfTemplate.Build(PdfTemplate.Sound
            .Replace("%PDF-1.7", header, StringComparison.Ordinal)
            .Replace("<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Catalog /Pages 2 0 R " + catalog + " >>", StringComparison.Ordinal));

        using var document = PdfDocument.Open(file);

        document.Version.Should().Be(version);
    }

    [Fact]
    public void Opens_a_document_indexed_by_a_cross_reference_stream()
    {
        using var document = PdfDocument.Open(SampleDocument().BuildWithXRefStream(rootNumber: 1));

        document.WasRepaired.Should().BeFalse();
        PageContent(document).Should().Be("BT (Bonjour) Tj ET");
    }

    [Fact]
    public void Reads_objects_packed_into_an_object_stream()
    {
        var bytes = SampleDocument().BuildWithXRefStream(rootNumber: 1, compressedObjects: [1, 2, 3]);

        using var document = PdfDocument.Open(bytes);

        document.WasRepaired.Should().BeFalse();
        document.Catalog.Required().IsOfType(PdfName.Catalog).Should().BeTrue();
        Page(document).GetArray(PdfName.MediaBox).Required().Count.Should().Be(4);
        PageContent(document).Should().Be("BT (Bonjour) Tj ET");
    }

    [Fact]
    public void Finds_objects_whose_recorded_offsets_are_wrong()
    {
        var bytes = SampleDocument().BuildClassic(rootNumber: 1, offsetError: 3);

        using var document = PdfDocument.Open(bytes);

        PageContent(document).Should().Be("BT (Bonjour) Tj ET");
        document.Diagnostics.Contains(PdfDiagnosticCodes.XRefOffsetAdjusted).Should().BeTrue();
    }

    [Theory]
    [InlineData("sv-SE")]
    [InlineData("fa-IR")]
    public void Writes_the_distance_to_an_object_found_before_its_entry_the_same_whatever_the_culture(string name)
    {
        // Every entry points 3 bytes past its object. Swedish writes a minus sign as U+2212, Persian as U+200E U+2212.
        var bytes = SampleDocument().BuildClassic(rootNumber: 1, offsetError: 3);
        var culture = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(name);
            using var document = PdfDocument.Open(bytes);
            _ = PageContent(document);

            document.Diagnostics.Where(entry => entry.Code == PdfDiagnosticCodes.XRefOffsetAdjusted).Should().NotBeEmpty()
                .And.OnlyContain(entry => entry.Message.EndsWith(" was found -3 bytes from where the index said.", StringComparison.Ordinal));
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    [Fact]
    public void Rebuilds_the_index_of_a_file_that_has_none()
    {
        var bytes = SampleDocument().BuildClassic(rootNumber: 1, includeXRef: false);

        using var document = PdfDocument.Open(bytes);

        document.WasRepaired.Should().BeTrue();
        document.Diagnostics.Contains(PdfDiagnosticCodes.XRefRebuilt).Should().BeTrue();
        document.Catalog.Required().IsOfType(PdfName.Catalog).Should().BeTrue();
        PageContent(document).Should().Be("BT (Bonjour) Tj ET");
    }

    [Fact]
    public void Rebuilds_the_index_of_a_file_whose_tail_was_lost()
    {
        var complete = SampleDocument().BuildClassic(rootNumber: 1);
        var truncated = complete.AsSpan(0, complete.Length - 40).ToArray();

        using var document = PdfDocument.Open(truncated);

        document.WasRepaired.Should().BeTrue();
        document.Catalog.Required();
    }

    [Fact]
    public void Rebuilds_the_index_of_a_file_cut_right_after_its_trailer_keyword()
    {
        var complete = SampleDocument().BuildClassic(rootNumber: 1);
        var keyword = complete.AsSpan().LastIndexOf("trailer"u8);
        var truncated = complete.AsSpan(0, keyword + "trailer".Length).ToArray();

        using var document = PdfDocument.Open(truncated);

        document.WasRepaired.Should().BeTrue();
        document.Catalog.Required().IsOfType(PdfName.Catalog).Should().BeTrue();
        PageContent(document).Should().Be("BT (Bonjour) Tj ET");
        document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.SyntaxTruncatedObject)
            .Which.Position.Should().Be(truncated.Length, "the trailer the keyword announces would start where the file ends");
    }

    [Fact]
    public void Prefers_the_newest_definition_after_an_incremental_update()
    {
        var original = SampleDocument().BuildClassic(rootNumber: 1);
        var updated = TestPdfBuilder.AppendIncrementalUpdate(
            original,
            rootNumber: 1,
            [(4, "<< /Length 20 >>\nstream\nBT (Au revoir) Tj ET\nendstream")]);

        using var document = PdfDocument.Open(updated);

        document.WasRepaired.Should().BeFalse();
        PageContent(document).Should().Be("BT (Au revoir) Tj ET");
    }

    [Fact]
    public void Stops_when_the_chain_of_sections_loops()
    {
        var original = SampleDocument().BuildClassic(rootNumber: 1);
        var looping = TestPdfBuilder.AppendIncrementalUpdate(
            original,
            rootNumber: 1,
            [(4, "<< /Length 18 >>\nstream\nBT (Bonjour) Tj ET\nendstream")],
            pointPreviousAtSelf: true);

        using var document = PdfDocument.Open(looping);

        document.Catalog.Required();
        document.Diagnostics.Contains(PdfDiagnosticCodes.XRefChainCycle).Should().BeTrue();
    }

    [Theory]
    [InlineData("0000099999")]
    [InlineData("-000000001")]
    public void Reports_an_entry_outside_the_file_with_no_position_and_its_offset_in_the_message(string offset)
    {
        // No position in the file names an entry the index keeps without where it was read (#187).
        var template = PdfTemplate.Sound
            .Replace("/Type /Catalog /Pages 2 0 R", "/Type /Catalog /Pages 2 0 R /Outlines 4 0 R", StringComparison.Ordinal)
            .Replace("0 4\n", "0 5\n", StringComparison.Ordinal)
            .Replace("{row:3}\n", "{row:3}\n" + offset + " 00000 n \n", StringComparison.Ordinal)
            .Replace("/Size 4", "/Size 5", StringComparison.Ordinal);

        using var document = PdfDocument.Open(PdfTemplate.Build(template));
        document.GetObject(new PdfObjectId(4));

        var report = document.Diagnostics.Should().ContainSingle(d => d.Code == PdfDiagnosticCodes.XRefEntryOutOfRange).Which;
        report.Position.Should().Be(-1);
        report.Message.Should().Be(string.Create(
            CultureInfo.InvariantCulture,
            $"The entry of object 4 places it at offset {long.Parse(offset, CultureInfo.InvariantCulture)}, outside the file."));
    }

    [Fact]
    public void Reads_a_file_that_starts_with_junk_before_the_header()
    {
        var original = SampleDocument().BuildClassic(rootNumber: 1);
        var prefixed = Encoding.ASCII.GetBytes("garbage bytes from a broken download\n").Concat(original).ToArray();

        using var document = PdfDocument.Open(prefixed);

        document.Catalog.Required();
        PageContent(document).Should().Be("BT (Bonjour) Tj ET");
    }

    [Fact]
    public void Refuses_an_encrypted_document_with_a_typed_exception()
    {
        var builder = SampleDocument().WithObject(9, "<< /Filter /Standard /V 1 /R 2 /P -1 >>");
        var bytes = builder.BuildClassic(rootNumber: 1);
        var encrypted = Encoding.Latin1.GetString(bytes)
            .Replace("/Root 1 0 R >>", "/Root 1 0 R /Encrypt 9 0 R >>", StringComparison.Ordinal);

        FluentThrow<PdfEncryptedException>(() => PdfDocument.Open(Encoding.Latin1.GetBytes(encrypted)));
    }

    [Fact]
    public void Refuses_an_empty_input()
    {
        FluentThrow<PdfFormatException>(() => PdfDocument.Open(ReadOnlyMemory<byte>.Empty));
    }

    [Fact]
    public void A_reader_handed_an_empty_source_finds_no_header_and_rebuilds_an_index_that_stays_empty()
    {
        // A document refuses an empty input before any reader sees it; handed one all the same, the reader finds
        // neither a header nor a startxref, and says so.
        var diagnostics = new PdfDiagnostics();
        var reader = new PdfFileReader(
            PdfFileSource.FromMemory(ReadOnlyMemory<byte>.Empty),
            diagnostics,
            new PdfLimitGuard(PdfReaderLimits.Default, throwOnLimit: false),
            cacheCapacity: 64,
            ownsSource: true);

        reader.ObjectCount.Should().Be(0);
        reader.WasRepaired.Should().BeTrue();
        reader.Structure.StartXRefPosition.Should().Be(-1);
        diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Message, diagnostic.Position)).Should().Equal(
            (PdfDiagnosticCodes.HeaderMissing, "The file has no PDF header (%PDF-) in its first 4,096 bytes; its offsets were counted from its first byte.", -1L),
            (PdfDiagnosticCodes.XRefRebuilt, "The cross-reference index was rebuilt by scanning the file.", -1L));
    }

    [Theory]
    [InlineData("%PDF.1.7\n")]
    [InlineData("")]
    public void A_file_without_a_header_is_read_through_its_table_and_its_header_reported_as_a_repair(string header)
    {
        // Nothing is rebuilt for a missing header: the file's offsets count from its first byte, as from a header
        // there (#187).
        using var document = PdfDocument.Open(PdfTemplate.Build(PdfTemplate.Sound.Replace("%PDF-1.7\n", header, StringComparison.Ordinal)));

        document.Catalog.Should().NotBeNull();
        document.WasRepaired.Should().BeFalse();
        var report = document.Diagnostics.Should().ContainSingle().Which;
        report.Code.Should().Be(PdfDiagnosticCodes.HeaderMissing);
        report.Severity.Should().Be(PdfDiagnosticSeverity.Repair);
        report.Position.Should().Be(0);
        document.Diagnostics.Contains(PdfDiagnosticCodes.XRefRebuilt).Should().Be(document.WasRepaired);
    }

    [Fact]
    public void Does_not_read_stream_data_until_it_is_asked_for()
    {
        var payload = new string('A', 500_000);
        var builder = SampleDocument(content: payload);
        var source = new CountingSource(builder.BuildClassic(rootNumber: 1));

        using var document = PdfDocument.Open(source, options: null, ownsSource: false);

        var afterOpen = source.BytesRead;
        afterOpen.Should().BeLessThan(200_000);

        var stream = Page(document).GetStream(PdfName.Contents).Required();
        stream.GetRawBytes().Length.Should().Be(payload.Length);

        source.BytesRead.Should().BeGreaterThan(afterOpen + payload.Length - 1);
    }

    [Fact]
    public void Stream_data_a_file_lost_after_its_dictionary_was_read_is_read_as_far_as_the_file_still_goes()
    {
        // The file is cut five bytes into the page's content once its dictionary is read: the data is those five bytes,
        // not the eighteen its /Length gives with zeros where the rest was.
        var bytes = SampleDocument().BuildClassic(rootNumber: 1);
        var source = new CutShortSource(bytes);
        using var document = PdfDocument.Open(source, options: null, ownsSource: true);
        var stream = Page(document).GetStream(PdfName.Contents).Required();

        source.CutAt((int)PdfTemplate.OffsetOf(bytes, "BT (Bonjour)") + 5);

        Encoding.ASCII.GetString(stream.GetRawBytes().Span).Should().Be("BT (B");
    }

    public static TheoryData<string, string> StreamKinds()
    {
        var kinds = new[]
        {
            "a memory stream written to", "a memory stream that shows its buffer", "a memory stream that hides its buffer",
            "a memory stream whose buffer starts past an origin", "a stream derived from a memory stream", "a buffered stream",
            "a file stream",
        };
        var data = new TheoryData<string, string>();

        foreach (var kind in kinds)
        {
            foreach (var place in new[] { "at its start", "at its end", "partly read", "past its end" })
            {
                data.Add(kind, place);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(StreamKinds))]
    public void Opens_a_seekable_stream_from_its_start_wherever_it_stands(string kind, string place)
    {
        var bytes = SampleDocument().BuildClassic(rootNumber: 1);
        var path = kind == "a file stream" ? Path.GetTempFileName() : null;

        try
        {
            using var stream = SeekableStream(kind, bytes, path);
            stream.Position = place switch
            {
                "at its start" => 0,
                "at its end" => stream.Length,
                "partly read" => 5,
                _ => stream.Length + 100,
            };

            using var document = PdfDocument.Open(stream);

            PageContent(document).Should().Be("BT (Bonjour) Tj ET");
            document.ObjectCount.Should().Be(5, "objects 1 to 4, and the free head of the table");
            document.Diagnostics.Should().BeEmpty("the stream is the file from its first byte");
        }
        finally
        {
            if (path is not null)
            {
                File.Delete(path);
            }
        }
    }

    [Theory]
    [InlineData("a memory stream written to")]
    [InlineData("a memory stream that hides its buffer")]
    [InlineData("a file stream")]
    public void A_seekable_stream_holds_what_comes_before_the_header_whatever_was_read_of_it(string kind)
    {
        var bytes = Encoding.Latin1.GetBytes(new string('x', 100) + "\n").Concat(SampleDocument().BuildClassic(rootNumber: 1)).ToArray();
        var path = kind == "a file stream" ? Path.GetTempFileName() : null;

        try
        {
            using var stream = SeekableStream(kind, bytes, path);
            stream.Position = 101;

            using var document = PdfDocument.Open(stream);

            PageContent(document).Should().Be("BT (Bonjour) Tj ET");
            document.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Position))
                .Should().Equal((PdfDiagnosticCodes.XRefOffsetAdjusted, 101L));
        }
        finally
        {
            if (path is not null)
            {
                File.Delete(path);
            }
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void Opens_a_stream_that_cannot_seek_from_where_it_stands(int prefix)
    {
        var file = SampleDocument().BuildClassic(rootNumber: 1);
        var bytes = Encoding.Latin1.GetBytes(new string('x', prefix)).Concat(file).ToArray();
        using var stream = new ForwardOnlyStream(new MemoryStream(bytes));
        stream.ReadExactly(new byte[prefix]);

        using var document = PdfDocument.Open(stream);

        PageContent(document).Should().Be("BT (Bonjour) Tj ET");
        document.Diagnostics.Should().BeEmpty("what was read of it before is not the file's");
    }

    [Fact]
    public void A_seekable_stream_is_copied_into_an_array_of_its_length()
    {
        using var stream = new DerivedMemoryStream();
        stream.Write(new byte[1_000_001]);
        using (PdfFileSource.FromStream(stream))
        {
            // Warmed up first: the pool's buffer for the copy is not what is measured.
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        using var source = PdfFileSource.FromStream(stream);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        source.Length.Should().Be(1_000_001);
        allocated.Should().BeLessThan(1_250_000, "the copy is sized from the stream's length, not grown by doubling");
    }

    [Fact]
    public void A_seekable_stream_longer_than_an_array_holds_is_copied_as_it_reads()
    {
        // A stream that says it is longer than any array: its copy is not sized from what it says.
        using var stream = new OverlongStream(SampleDocument().BuildClassic(rootNumber: 1));

        using var document = PdfDocument.Open(stream);

        PageContent(document).Should().Be("BT (Bonjour) Tj ET");
    }

    [Fact]
    public void A_stream_that_cannot_seek_and_was_read_to_its_end_is_empty()
    {
        using var stream = new ForwardOnlyStream(new MemoryStream(SampleDocument().BuildClassic(rootNumber: 1)));
        stream.CopyTo(Stream.Null);

        FluentActions.Invoking(() => PdfDocument.Open(stream))
            .Should().Throw<PdfFormatException>().WithMessage("The input is empty.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Only_a_memory_stream_itself_is_read_in_its_buffer(bool derived)
    {
        // A derived memory stream may lend its buffer again once disposed, as a pooled one does: it is copied, and what is
        // written over its buffer after the document opened reaches none of the document's objects.
        var bytes = SampleDocument().BuildClassic(rootNumber: 1);
        using var stream = derived ? new DerivedMemoryStream() : new MemoryStream();
        stream.Write(bytes);

        using var document = PdfDocument.Open(stream);
        var content = Page(document).GetRaw(PdfName.Contents).Should().BeOfType<PdfReference>().Subject;
        stream.GetBuffer().AsSpan(0, bytes.Length).Fill((byte)'z');

        (content.Resolve() is PdfStream).Should().Be(derived, "a document read in the caller's buffer reads what is there now");
    }

    private static Stream SeekableStream(string kind, byte[] bytes, string? path)
    {
        switch (kind)
        {
            case "a memory stream written to":
                var written = new MemoryStream();
                written.Write(bytes);
                return written;

            case "a memory stream that shows its buffer":
                return new MemoryStream(bytes, 0, bytes.Length, writable: false, publiclyVisible: true);

            case "a memory stream that hides its buffer":
                return new MemoryStream(bytes);

            case "a memory stream whose buffer starts past an origin":
                var padded = Encoding.Latin1.GetBytes(new string('x', 37)).Concat(bytes).ToArray();
                return new MemoryStream(padded, 37, bytes.Length, writable: false, publiclyVisible: true);

            case "a stream derived from a memory stream":
                var derived = new DerivedMemoryStream();
                derived.Write(bytes);
                return derived;

            case "a buffered stream":
                return new BufferedStream(new MemoryStream(bytes));

            default:
                File.WriteAllBytes(path!, bytes);
                return new FileStream(path!, FileMode.Open, FileAccess.Read);
        }
    }

    /// <summary>A memory stream of a type derived from it, as a pooled one is.</summary>
    private sealed class DerivedMemoryStream : MemoryStream;

    /// <summary>A memory stream that says it is longer than any array can be.</summary>
    private sealed class OverlongStream(byte[] buffer) : MemoryStream(buffer)
    {
        public override long Length => (long)Array.MaxLength + 1;
    }

    /// <summary>A stream that can be read forward only, as a network response is.</summary>
    private sealed class ForwardOnlyStream(Stream inner) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_source_reads_nothing_outside_itself(bool fromFile)
    {
        var path = Path.GetTempFileName();

        try
        {
            File.WriteAllBytes(path, "%PDF"u8.ToArray());
            using var source = fromFile ? PdfFileSource.FromFile(path) : PdfFileSource.FromMemory("%PDF"u8.ToArray());
            var buffer = new byte[4];

            source.Read(-1, buffer).Should().Be(0);
            source.Read(4, buffer).Should().Be(0);
            source.Read(1, buffer).Should().Be(3);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(-1, 4)]
    [InlineData(4, 4)]
    [InlineData(0, 0)]
    [InlineData(0, -1)]
    public void A_source_gives_an_empty_window_for_a_range_outside_itself_or_of_no_length(long offset, int length)
    {
        using var source = PdfFileSource.FromMemory("%PDF"u8.ToArray());

        using var window = source.GetWindow(offset, length);

        window.Length.Should().Be(0);
        window.Offset.Should().Be(0);
    }

    [Fact]
    public void Disposing_a_document_twice_is_harmless()
    {
        var document = PdfDocument.Open(SampleDocument().BuildClassic(rootNumber: 1));

        document.Dispose();
        FluentActions.Invoking(document.Dispose).Should().NotThrow();
    }

    public static TheoryData<string> Openings() =>
        new("bytes", "a stream it copies", "a memory stream it reads in place", "a path", "a source it does not own");

    [Theory]
    [MemberData(nameof(Openings))]
    public void A_disposed_document_answers_nothing_but_its_diagnostics_nor_does_what_was_taken_from_it(string opening)
    {
        var bytes = HeldObjectsDocument();
        var path = opening == "a path" ? Path.GetTempFileName() : null;

        try
        {
            if (path is not null)
            {
                File.WriteAllBytes(path, bytes);
            }

            using var stream = opening switch
            {
                "a stream it copies" => new MemoryStream(bytes),
                "a memory stream it reads in place" => new MemoryStream(bytes, 0, bytes.Length, writable: false, publiclyVisible: true),
                _ => null,
            };
            using var source = opening == "a source it does not own" ? new CountingSource(bytes) : null;
            var document = opening switch
            {
                "bytes" => PdfDocument.Open(bytes),
                "a path" => PdfDocument.Open(path!),
                "a source it does not own" => PdfDocument.Open(source!, options: null, ownsSource: false),
                _ => PdfDocument.Open(stream!),
            };

            // Taken before: a page, cached; its content stream, cached and its data read; a stream whose data was not
            // read; an object of an object stream read, so cached, and one of the same stream not read. A stream an object
            // stream holds, which ISO 32000-1 forbids, reads as null, as does an array there holding one (#161).
            var page = document.GetObject(new PdfObjectId(3)).AsDictionary().Required();
            var contents = page.GetRaw(PdfName.Contents).Should().BeOfType<PdfReference>().Subject;
            var read = contents.Resolve().Should().BeOfType<PdfStream>().Subject;
            read.GetRawBytes().Length.Should().Be(18);
            var unread = document.GetObject(new PdfObjectId(7)).Should().BeOfType<PdfStream>().Subject;
            var states = page.GetDictionary(PdfName.Get("Resources")).Required().GetDictionary(PdfName.Get("ExtGState")).Required();
            states.GetDictionary(PdfName.Get("A")).Should().NotBeNull();
            var packed = states.GetRaw(PdfName.Get("A")).Should().BeOfType<PdfReference>().Subject;
            var packedUnread = states.GetRaw(PdfName.Get("B")).Should().BeOfType<PdfReference>().Subject;
            document.GetObject(new PdfObjectId(8)).Should().BeSameAs(PdfNull.Instance);
            document.GetObject(new PdfObjectId(9)).Should().BeSameAs(PdfNull.Instance);
            using var numbers = document.ObjectNumbers.GetEnumerator();
            numbers.MoveNext().Should().BeTrue();

            document.Dispose();

            document.Diagnostics.Select(entry => entry.Code).Should().Equal(
                [PdfDiagnosticCodes.ObjectStreamMemberStream, PdfDiagnosticCodes.ObjectStreamMemberStream],
                "the record of the reading keeps answering");
            Disposed(() => document.Trailer);
            Disposed(() => document.Catalog);
            Disposed(() => document.Version);
            Disposed(() => document.WasRepaired);
            Disposed(() => document.ObjectCount);
            Disposed(() => document.ObjectNumbers);
            Disposed(() => document.IsEncrypted);
            Disposed(() => document.GetObject(new PdfObjectId(3)));
            Disposed(() => document.GetObject(new PdfObjectId(2)));
            Disposed(() => numbers.MoveNext());
            Disposed(() => contents.Resolve());
            Disposed(() => packed.Resolve());
            Disposed(() => packedUnread.Resolve());
            Disposed(() => states.GetDictionary(PdfName.Get("B")));
            Disposed(() => page.GetStream(PdfName.Contents));
            Disposed(() => read.GetRawBytes());
            Disposed(() => read.Decode(document.Diagnostics));
            Disposed(() => unread.GetRawBytes());

            read.RawLength.Should().Be(18, "what was handed out stays the caller's");
            read.ToString().Should().Be("<<stream of 18 bytes>>");
            page.ToString().Should().Be("<</Type /Page ... 5 entries>>");
            FluentActions.Invoking(document.Dispose).Should().NotThrow();
            source?.Disposals.Should().Be(0, "a source the document did not own stays the caller's");
        }
        finally
        {
            if (path is not null)
            {
                File.Delete(path);
            }
        }

        static void Disposed(Func<object?> act) =>
            FluentActions.Invoking(act).Should().Throw<ObjectDisposedException>()
                .Which.ObjectName.Should().Be(typeof(PdfDocument).FullName);
    }

    [Fact]
    public void What_was_taken_from_a_document_read_in_the_caller_s_memory_reads_none_of_what_it_holds_once_disposed()
    {
        // The caller reuses its buffer once the document is disposed, as a pool does: a reference or a stream taken
        // before reads neither the bytes the document was read from nor those the caller wrote since.
        var buffer = HeldObjectsDocument();
        var document = PdfDocument.Open(buffer);
        var contents = document.GetObject(new PdfObjectId(3)).AsDictionary().Required().GetRaw(PdfName.Contents)!;
        var unread = document.GetObject(new PdfObjectId(7)).Should().BeOfType<PdfStream>().Subject;

        document.Dispose();
        SampleDocument("BT (Another file) Tj ET").BuildWithXRefStream(rootNumber: 1).CopyTo(buffer, 0);

        FluentActions.Invoking(contents.Resolve).Should().Throw<ObjectDisposedException>();
        FluentActions.Invoking(() => unread.GetRawBytes()).Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void Closing_a_reader_again_does_nothing_more()
    {
        var source = new CountingSource(SampleDocument().BuildClassic(rootNumber: 1));
        var reader = new PdfFileReader(
            source,
            new PdfDiagnostics(),
            new PdfLimitGuard(PdfReaderLimits.Default, throwOnLimit: false),
            cacheCapacity: 64,
            ownsSource: true);

        reader.Close();
        var closed = reader.Source;
        reader.Close();

        source.Disposals.Should().Be(1, "the source the reader owns is released once");
        reader.Source.Should().BeSameAs(closed, "a second close changes nothing the first left");
        FluentActions.Invoking(() => reader.GetObject(new PdfObjectId(1))).Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void Disposing_lets_go_of_the_index_the_object_streams_and_what_was_noted_of_each_object()
    {
        // A stream whose /Length is wrong and an object that lost its endobj, read before: what the reader noted of
        // them, its index and the object streams it decoded go once the document is disposed.
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .WithObject(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R >>")
            .WithObject(4, "<< /Length 99 >>\nstream\nBT (Bonjour) Tj ET\nendstream")
            .WithObject(5, "<< /Type /ExtGState /CA 1 >>")
            .BuildWithXRefStream(rootNumber: 1, compressedObjects: [5]);
        var damaged = Encoding.Latin1.GetString(file).Replace("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R >>\nendobj", "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R >>\n      ", StringComparison.Ordinal);
        var document = PdfDocument.Open(Encoding.Latin1.GetBytes(damaged));
        var reader = document.Reader;

        foreach (var number in document.ObjectNumbers.ToList())
        {
            _ = document.GetObject(new PdfObjectId(number));
        }

        reader.TryGetStreamLengthFault(4, out _).Should().BeTrue("the stream's /Length was found wrong");
        reader.IsEndObjMissing(3, out _).Should().BeTrue("the page lost its endobj");
        reader.ObjectStreamBytes.Should().BePositive();

        document.Dispose();

        reader.TryGetStreamLengthFault(4, out _).Should().BeFalse();
        reader.IsEndObjMissing(3, out _).Should().BeFalse();
        reader.ObjectStreamBytes.Should().Be(0);
        reader.ObjectCount.Should().Be(0);
        reader.ChainIndex.Required().Count.Should().Be(0, "the copy of the chain's index goes as well");
    }

    [Fact]
    public void Disposing_lets_go_of_the_copy_of_the_chain_s_index()
    {
        // Every row three bytes off: each object is found near it, and the reader keeps the chain's index beside its own.
        var document = PdfDocument.Open(SampleDocument().BuildClassic(rootNumber: 1, offsetError: 3));
        var reader = document.Reader;

        foreach (var number in document.ObjectNumbers.ToList())
        {
            _ = document.GetObject(new PdfObjectId(number));
        }

        document.WasRepaired.Should().BeFalse("each object was found near its row");
        reader.ChainIndex.Should().NotBeSameAs(reader.Index, "the reader corrected its own index");

        document.Dispose();

        reader.ChainIndex.Required().Count.Should().Be(0);
    }

    [Fact]
    public void A_reference_s_source_cannot_close_the_document_s_file()
    {
        var path = Path.GetTempFileName();

        try
        {
            File.WriteAllBytes(path, HeldObjectsDocument());
            using var document = PdfDocument.Open(path);
            var page = document.GetObject(new PdfObjectId(3)).AsDictionary().Required();
            var contents = page.GetRaw(PdfName.Contents).Should().BeOfType<PdfReference>().Subject;

            (contents.Source as IDisposable)?.Dispose();

            contents.Source.Should().NotBeAssignableTo<IDisposable>("no holder of a reference may close the document's file");
            contents.Resolve().Should().BeOfType<PdfStream>().Which.GetRawBytes().Length.Should().Be(18);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Disposing_lets_go_of_what_the_document_kept_though_a_reference_taken_from_it_is_held()
    {
        var (held, cached, source) = OpenTakeAndDispose();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        cached.IsAlive.Should().BeFalse("the objects the document cached are let go");
        source.IsAlive.Should().BeFalse("and so is the file it was read from");
        GC.KeepAlive(held);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static (PdfObject Held, WeakReference Cached, WeakReference Source) OpenTakeAndDispose()
    {
        var source = PdfFileSource.FromMemory(HeldObjectsDocument());
        var document = PdfDocument.Open(source, options: null, ownsSource: true);
        var page = document.GetObject(new PdfObjectId(3));
        var taken = (page.AsDictionary().Required().GetRaw(PdfName.Contents)!, new WeakReference(page), new WeakReference(source));

        document.Dispose();
        return taken;
    }

    /// <summary>
    /// A page whose content stream, a stream beside it, two objects of an object stream, and two streams an object stream
    /// holds, as no file should, are what a caller takes from a document before disposing of it.
    /// </summary>
    private static byte[] HeldObjectsDocument() =>
        new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .WithObject(
                3,
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /ExtGState << /A 5 0 R /B 6 0 R >> >> >>")
            .Stream(4, string.Empty, "BT (Bonjour) Tj ET")
            .WithObject(5, "<< /Type /ExtGState /CA 1 >>")
            .WithObject(6, "<< /Type /ExtGState /CA 0.5 >>")
            .Stream(7, string.Empty, "q Q")
            .WithObject(8, "<< /Length 3 >>\nstream\nabc\nendstream")
            .WithObject(9, "[<< /Length 3 >>\nstream\nxyz\nendstream]")
            .BuildWithXRefStream(rootNumber: 1, compressedObjects: [5, 6, 8, 9]);

    [Fact]
    public void Reads_a_cross_reference_stream_that_leaves_out_the_type_field()
    {
        // /W [0 4 2]: with no type field, every row is an object at an offset (ISO 32000-1, Table 17).
        var file = new List<byte>();
        void Append(string text) => file.AddRange(Encoding.Latin1.GetBytes(text));

        Append("%PDF-1.5\n");
        var catalog = file.Count;
        Append("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        var pages = file.Count;
        Append("2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n");
        var stream = file.Count;
        Append("3 0 obj\n<< /Type /XRef /Size 4 /Index [1 3] /W [0 4 2] /Root 1 0 R /Length 18 >>\nstream\n");
        foreach (var offset in new[] { catalog, pages, stream })
        {
            file.AddRange([(byte)(offset >> 24), (byte)(offset >> 16), (byte)(offset >> 8), (byte)offset, 0, 0]);
        }

        Append($"\nendstream\nendobj\nstartxref\n{stream}\n%%EOF\n");

        using var document = PdfDocument.Open(file.ToArray());

        document.WasRepaired.Should().BeFalse();
        document.Catalog.GetDictionary(PdfName.Pages).IsOfType(PdfName.Pages).Should().BeTrue();
    }

    [Fact]
    public void Reads_what_an_object_stream_header_lists_before_it_goes_wrong()
    {
        // The header lists object 2, then 3 at an offset that is not a number.
        var file = Packed(header => header[..6] + "x ");
        using var document = PdfDocument.Open(file);

        document.GetObject(new PdfObjectId(2)).AsDictionary().IsOfType(PdfName.Pages).Should().BeTrue();
        document.GetObject(new PdfObjectId(3)).Should().BeSameAs(PdfNull.Instance);
        var report = document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.ObjectStreamUnreadable).Which;
        report.Severity.Should().Be(PdfDiagnosticSeverity.Warning);
        report.Position.Should().Be(ObjectStreamDataStart(file));
        report.Message.Should().Be(
            "Object stream 4 has a header that holds something other than an object number and an offset after 1 of the 2 objects its /N declares; only the first 1 of its 2 objects can be read from it.");
        document.Diagnostics.Should().NotContain(entry => entry.Code == PdfDiagnosticCodes.SyntaxUnexpectedToken);
    }

    [Theory]
    [InlineData("4294967298")]
    [InlineData("92233720368547758082")]
    [InlineData("2147483648")]
    [InlineData("0")]
    [InlineData("-2")]
    public void Reads_no_member_an_object_stream_header_numbers_as_no_object_can_be(string number)
    {
        // The header lists object 2 under a number no object has: narrowed to an int, 4294967298 was 2, and
        // 92233720368547758082 wrapped to it (#157). A number past a long is a real, which ends the header as any token
        // other than an integer does.
        var file = Packed(header => number + header[1..]);
        using var document = PdfDocument.Open(file);

        document.GetObject(new PdfObjectId(2)).Should().BeSameAs(PdfNull.Instance);
        document.GetObject(new PdfObjectId(3)).Should().BeSameAs(PdfNull.Instance);
        var report = document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.ObjectStreamUnreadable).Which;
        report.Position.Should().Be(ObjectStreamDataStart(file));
        report.Message.Should().Be(number.Length > 19
            ? "Object stream 4 has a header that holds something other than an object number and an offset after 0 of the 2 objects its /N declares; none of its objects can be read from it."
            : $"Object stream 4 has a header that lists object {number} at offset 0, which no member can be, after 0 of the 2 objects its /N declares; none of its objects can be read from it.");
    }

    [Fact]
    public void Reads_an_object_stream_header_that_lists_the_largest_number_a_member_can_have()
    {
        // The header is read, whatever the entries then find in it.
        using var document = PdfDocument.Open(Packed(header => "2147483647 0 " + header[4..]));

        document.GetObject(new PdfObjectId(3)).AsDictionary().IsOfType(PdfName.Page).Should().BeTrue();
        document.Diagnostics.Should().NotContain(entry => entry.Code == PdfDiagnosticCodes.ObjectStreamUnreadable);
    }

    [Theory]
    [InlineData("6 0 7 0 ", "lists object 7 at offset 0, not past the offset of the object before it, 0")]
    [InlineData("6 6 7 0 ", "lists object 7 at offset 0, not past the offset of the object before it, 6")]
    [InlineData("6 0 7 99 ", "lists object 7 at offset 99, past what its {0} bytes of decoded data hold")]
    [InlineData("6 0 7 13 ", "lists object 7 at offset 13, past what its {0} bytes of decoded data hold")]
    public void Ends_an_object_stream_header_at_a_member_that_starts_before_the_one_before_it_ends_or_past_the_data(string header, string pair)
    {
        // The members are "(six)" at offset 0 and "(sevn)" at offset 6, thirteen bytes with their ends of line. Offsets must
        // increase (7.5.7), so that each member is read to the next one's start; an offset that does not, or a member that
        // would start at or past the end of the data, ends the header as a pair no member can have does (#160, #202).
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(6, "(six)")
            .WithObject(7, "(sevn)")
            .BuildWithXRefStream(rootNumber: 1, compressedObjects: [6, 7], objectStreamHeader: _ => header);

        using var document = PdfDocument.Open(file);

        document.GetObject(new PdfObjectId(6)).AsText().Should().Be(header.StartsWith("6 6", StringComparison.Ordinal) ? "sevn" : "six");
        document.GetObject(new PdfObjectId(7)).Should().BeSameAs(PdfNull.Instance);
        var report = document.Diagnostics.Should().ContainSingle().Which;
        report.Code.Should().Be(PdfDiagnosticCodes.ObjectStreamUnreadable);
        report.Position.Should().Be(ObjectStreamDataStart(file));
        report.Message.Should().Be(string.Create(
            CultureInfo.InvariantCulture,
            $"Object stream 8 has a header that {string.Format(CultureInfo.InvariantCulture, pair, header.Length + 13)}, after 1 of the 2 objects its /N declares; only the first 1 of its 2 objects can be read from it."));
    }

    [Theory]
    [InlineData("6 13 7 14 ")]
    [InlineData("6 2147483647 7 0 ")]
    public void Reads_none_of_an_object_stream_whose_first_member_starts_past_its_data(string header)
    {
        // Thirteen bytes of members: the first starts at their end, or past what an offset into them can reach.
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(6, "(six)")
            .WithObject(7, "(sevn)")
            .BuildWithXRefStream(rootNumber: 1, compressedObjects: [6, 7], objectStreamHeader: _ => header);
        var offset = header[2..header.IndexOf(" 7", StringComparison.Ordinal)];

        using var document = PdfDocument.Open(file);

        document.GetObject(new PdfObjectId(6)).Should().BeSameAs(PdfNull.Instance);
        document.GetObject(new PdfObjectId(7)).Should().BeSameAs(PdfNull.Instance);
        document.Diagnostics.Should().ContainSingle().Which.Message.Should().Be(string.Create(
            CultureInfo.InvariantCulture,
            $"Object stream 8 has a header that lists object 6 at offset {offset}, past what its {header.Length + 13} bytes of decoded data hold, after 0 of the 2 objects its /N declares; none of its objects can be read from it."));
    }

    [Theory]
    [InlineData("/Type /XObj  ")]
    [InlineData("/Kind /ObjStm")]
    public void Reads_none_of_the_members_of_a_stream_that_is_not_of_type_object_stream(string type)
    {
        // The validator and the rebuild read no member from such a stream, nor does the reader (#274). The replacement
        // keeps every offset.
        var file = Encoding.Latin1.GetBytes(Encoding.Latin1.GetString(Packed(header => header)).Replace("/Type /ObjStm", type, StringComparison.Ordinal));

        using var document = PdfDocument.Open(file);

        document.GetObject(new PdfObjectId(3)).Should().BeSameAs(PdfNull.Instance);
        var report = document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.ObjectStreamUnreadable).Which;
        report.Position.Should().Be(Encoding.Latin1.GetString(file).IndexOf("stream\n", Encoding.Latin1.GetString(file).IndexOf("/N 2", StringComparison.Ordinal), StringComparison.Ordinal) + "stream\n".Length);
        report.Message.Should().Be("Object stream 4 is a stream that is not of /Type /ObjStm; none of its objects can be read from it.");
    }

    [Fact]
    public void Finds_an_object_its_entry_misplaces_at_the_first_place_its_object_stream_lists_it()
    {
        // Object 6 is listed twice, at indexes 0 and 1, and its entry gives index 5: it is the first that is read.
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(6, "(six)")
            .WithObject(7, "(sevn)")
            .BuildWithXRefStream(rootNumber: 1, compressedObjects: [6, 7], objectStreamHeader: _ => "6 0 6 6 ");
        var text = Encoding.Latin1.GetString(file);
        var rows = text.IndexOf("stream\n", text.IndexOf("/Type /XRef", StringComparison.Ordinal), StringComparison.Ordinal) + "stream\n".Length;
        file[rows + (6 * 7) + 6] = 5;

        using var document = PdfDocument.Open(file);

        document.GetObject(new PdfObjectId(6)).AsText().Should().Be("six");
        document.Diagnostics.Should().ContainSingle().Which.Message.Should().Be(
            "Object 6 is at index 0 of object stream 8, not at index 5, where the cross-reference index places it.");
    }

    [Theory]
    [InlineData(-5L, false)]
    [InlineData(2147483648L, false)]
    [InlineData(4294967296L, true)]
    public void Reads_no_member_an_object_stream_header_places_where_no_member_can_start(long offset, bool fromItsOwn)
    {
        // Object 3's offset, negative, past an int, or its own plus 2^32, which narrowed to an int was its own again.
        var written = 0L;
        var file = Packed(header =>
        {
            var own = long.Parse(header.AsSpan(6, header.Length - 7), CultureInfo.InvariantCulture);
            written = fromItsOwn ? own + offset : offset;
            return string.Create(CultureInfo.InvariantCulture, $"{header[..6]}{written} ");
        });
        using var document = PdfDocument.Open(file);

        document.GetObject(new PdfObjectId(2)).AsDictionary().IsOfType(PdfName.Pages).Should().BeTrue();
        document.GetObject(new PdfObjectId(3)).Should().BeSameAs(PdfNull.Instance);
        document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.ObjectStreamUnreadable).Which.Message.Should().Be(
            string.Create(
                CultureInfo.InvariantCulture,
                $"Object stream 4 has a header that lists object 3 at offset {written}, which no member can be, after 1 of the 2 objects its /N declares; only the first 1 of its 2 objects can be read from it."));
    }

    [Fact]
    public void Reads_an_object_where_its_stream_s_header_lists_it_rather_than_where_the_index_says()
    {
        // The header lists 3 at index 0 and 2 at index 1; the index places 2 at index 0.
        var file = Packed(header => "3" + header[1..4] + "2" + header[5..]);
        using var document = PdfDocument.Open(file);

        document.GetObject(new PdfObjectId(2)).AsDictionary().IsOfType(PdfName.Page).Should().BeTrue();
        var report = document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.ObjectStreamMemberMoved).Which;
        report.Severity.Should().Be(PdfDiagnosticSeverity.Repair);
        report.Position.Should().Be(ObjectStreamDataStart(file));
        report.Message.Should().Be("Object 2 is at index 1 of object stream 4, not at index 0, where the cross-reference index places it.");
        document.Diagnostics.Should().NotContain(entry => entry.Code == PdfDiagnosticCodes.XRefOffsetAdjusted);
    }

    [Fact]
    public void Reads_an_object_whose_index_lies_past_its_stream_s_header_where_the_header_lists_it()
    {
        // The header lists object 3 alone, at index 0, and ends there; the index places 3 at index 1.
        var file = Packed(header => header[4..]);
        using var document = PdfDocument.Open(file);

        document.GetObject(new PdfObjectId(3)).AsDictionary().IsOfType(PdfName.Page).Should().BeTrue();
        document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.ObjectStreamUnreadable)
            .Which.Message.Should().Be(
                "Object stream 4 has a header that ends after 1 of the 2 objects its /N declares; only the first 1 of its 2 objects can be read from it.");
        var moved = document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.ObjectStreamMemberMoved).Which;
        moved.Position.Should().Be(ObjectStreamDataStart(file));
        moved.Message.Should().Be("Object 3 is at index 0 of object stream 4, not at index 1, where the cross-reference index places it.");
    }

    [Fact]
    public void Places_a_fault_met_inside_an_object_stream_member_where_the_stream_s_data_starts()
    {
        // A byte of decoded data is no offset in the file: the report is placed at the stream, the byte in its message.
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .WithObject(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Rotate ) >>")
            .BuildWithXRefStream(rootNumber: 1, compressedObjects: [2, 3]);
        var dataStart = ObjectStreamDataStart(file);
        var stray = Encoding.Latin1.GetString(file).IndexOf(')', (int)dataStart) - dataStart;

        using var document = PdfDocument.Open(file);
        document.GetObject(new PdfObjectId(3)).AsDictionary().IsOfType(PdfName.Page).Should().BeTrue();

        var report = document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.SyntaxUnexpectedToken).Which;
        report.Position.Should().Be(dataStart);
        report.Message.Should().Be(string.Create(
            CultureInfo.InvariantCulture,
            $"A token was found where a value was expected. It was met in object 3, at byte {stray} of object stream 4's decoded data."));
    }

    [Fact]
    public void Says_that_an_object_stream_s_data_rather_than_the_file_ended_in_the_middle_of_a_member()
    {
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(3, "<< /Type /Page /Rotate")
            .BuildWithXRefStream(rootNumber: 1, compressedObjects: [2, 3]);
        var dataStart = ObjectStreamDataStart(file);
        var opens = Encoding.Latin1.GetString(file).IndexOf("<< /Type /Page /Rotate", (int)dataStart, StringComparison.Ordinal) - dataStart;

        using var document = PdfDocument.Open(file);
        _ = document.GetObject(new PdfObjectId(3));

        // The member is a dictionary the data ends inside, after a key: it is placed where the dictionary opens (#119).
        var report = document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.SyntaxTruncatedObject).Which;
        report.Position.Should().Be(dataStart);
        report.Message.Should().Be(string.Create(
            CultureInfo.InvariantCulture,
            $"The object stream's decoded data ended inside a dictionary, which was never closed, before the value of its last key. It was met in object 3, at byte {opens} of object stream 4's decoded data."));
    }

    [Theory]
    [InlineData("<< /Length 99 >>\nstream\nabc\nendstream")]
    [InlineData("[1 << /Length 3 >> stream\nabc\nendstream]")]
    [InlineData("<< /Inner << /Length 3 >> stream\nabc\nendstream >>")]
    public void Reads_an_object_written_as_a_stream_inside_an_object_stream_as_null_and_reports_it_once(string member)
    {
        // An object stream cannot hold a stream (7.5.7): the member, or the member holding one, is no value; its data is
        // not read, and nothing of its length reported. Read again once the cache let it go, it is not reported again (#161).
        var builder = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(3, member);

        for (var number = 10; number < 90; number++)
        {
            builder.WithObject(number, "(filler)");
        }

        var file = builder.BuildWithXRefStream(rootNumber: 1, compressedObjects: [2, 3]);
        var dataStart = ObjectStreamDataStart(file);
        var opens = Encoding.Latin1.GetString(file).IndexOf(member, (int)dataStart, StringComparison.Ordinal) - dataStart;

        using var document = PdfDocument.Open(file, PdfReaderOptions.Default with { ObjectCacheCapacity = 64 });

        for (var round = 0; round < 2; round++)
        {
            document.GetObject(new PdfObjectId(3)).Should().BeSameAs(PdfNull.Instance);

            for (var number = 10; number < 90; number++)
            {
                document.GetObject(new PdfObjectId(number));
            }
        }

        var report = document.Diagnostics.Should().ContainSingle().Which;
        report.Code.Should().Be(PdfDiagnosticCodes.ObjectStreamMemberStream);
        report.Severity.Should().Be(PdfDiagnosticSeverity.Warning);
        report.Position.Should().Be(dataStart);
        report.Message.Should().Be(string.Create(
            CultureInfo.InvariantCulture,
            $"Object 3, at byte {opens} of object stream 90's decoded data, is written as a stream, which an object stream cannot hold; it reads as null."));
    }

    [Theory]
    [InlineData("/First 4", "has no /N that gives a count of objects; none of its objects can be read from it.")]
    [InlineData("/N -1 /First 4", "has no /N that gives a count of objects; none of its objects can be read from it.")]
    [InlineData("/N 1", "has no /First that gives where its objects start; none of its objects can be read from it.")]
    [InlineData("/N 1 /First -4", "has no /First that gives where its objects start; none of its objects can be read from it.")]
    [InlineData("/N 9 /First 4", "has a header that ends after 1 of the 9 objects its /N declares; only the first 1 of its 9 objects can be read from it.")]
    [InlineData("/N 1 /First 40", "gives in /First an offset, 40, past the end of its decoded data, which is 8 bytes long; none of its objects can be read from it.")]
    [InlineData("/N 2 /First 4", "has a header that ends after 1 of the 2 objects its /N declares; only the first 1 of its 2 objects can be read from it.")]
    public void Reports_an_object_stream_whose_dictionary_or_header_cannot_be_believed_where_its_data_starts(string entries, string fault)
    {
        // The index is rebuilt, which reads every object stream the file holds: object stream 5 holds object 6.
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(5, $"<< /Type /ObjStm {entries} /Length 8 >>\nstream\n6 0 <<>>\nendstream")
            .BuildClassic(rootNumber: 1, includeXRef: false);

        using var document = PdfDocument.Open(file);

        var report = document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.ObjectStreamUnreadable).Which;
        report.Severity.Should().Be(PdfDiagnosticSeverity.Warning);
        report.Position.Should().Be(ObjectStreamDataStart(file));
        report.Message.Should().Be("Object stream 5 " + fault);
    }

    [Fact]
    public void Reports_an_object_stream_whose_header_ends_after_an_object_number_with_no_offset()
    {
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(5, "<< /Type /ObjStm /N 2 /First 6 /Length 10 >>\nstream\n6 0 7 <<>>\nendstream")
            .BuildClassic(rootNumber: 1, includeXRef: false);

        using var document = PdfDocument.Open(file);

        document.GetObject(new PdfObjectId(6)).Should().BeOfType<PdfDictionary>();
        document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.ObjectStreamUnreadable).Which.Message.Should().Be(
            "Object stream 5 has a header that ends after 1 of the 2 objects its /N declares; only the first 1 of its 2 objects can be read from it.");
    }

    [Fact]
    public void Leaves_a_first_past_data_a_guard_cut_to_the_guard_s_report()
    {
        // The decoded-stream guard stops the object stream's data before its /First: the stream is not at fault.
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .WithObject(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>")
            .BuildWithXRefStream(rootNumber: 1, compressedObjects: [2, 3], objectStreamHeader: header => header + new string(' ', 200), compressObjectStream: true);
        var options = PdfReaderOptions.Default with { Limits = PdfReaderLimits.Default with { MaxDecodedStreamLength = 100 } };

        using var document = PdfDocument.Open(file, options);
        document.GetObject(new PdfObjectId(2)).Should().BeSameAs(PdfNull.Instance);

        document.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.LimitDecodedStream);
    }

    [Theory]
    [InlineData("6 x <<>>", "/N 1 /First 4")]
    [InlineData("/X <<>>", "/N 1 /First 3")]
    public void Reports_an_object_stream_whose_header_starts_with_a_stray_token_as_serving_none_of_its_objects(string data, string entries)
    {
        // A stray object number is the fault, though the header ends right after it.
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(5, $"<< /Type /ObjStm {entries} /Length {data.Length} >>\nstream\n{data}\nendstream")
            .BuildClassic(rootNumber: 1, includeXRef: false);

        using var document = PdfDocument.Open(file);

        document.GetObject(new PdfObjectId(6)).Should().BeSameAs(PdfNull.Instance);
        document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.ObjectStreamUnreadable).Which.Message.Should().Be(
            "Object stream 5 has a header that holds something other than an object number and an offset after 0 of the 1 objects its /N declares; none of its objects can be read from it.");
    }

    [Theory]
    [InlineData(64)]
    [InlineData(1_000_000)]
    public void Reports_an_object_at_another_index_of_its_stream_once_however_often_it_is_parsed(int cacheCapacity)
    {
        // The header swaps objects 2 and 3: both entries are wrong. Between two readings of them, a hundred other
        // objects push them out of a cache of 64, which parses them again every time.
        var builder = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .WithObject(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>");

        for (var number = 10; number < 110; number++)
        {
            builder.WithObject(number, string.Create(CultureInfo.InvariantCulture, $"({number})"));
        }

        var file = builder.BuildWithXRefStream(rootNumber: 1, compressedObjects: [2, 3], objectStreamHeader: header => "3" + header[1..4] + "2" + header[5..]);
        using var document = PdfDocument.Open(file, PdfReaderOptions.Default with { ObjectCacheCapacity = cacheCapacity });

        for (var round = 0; round < 3; round++)
        {
            document.GetObject(new PdfObjectId(2)).AsDictionary().IsOfType(PdfName.Page).Should().BeTrue();
            document.GetObject(new PdfObjectId(3)).AsDictionary().IsOfType(PdfName.Pages).Should().BeTrue();

            for (var number = 10; number < 110; number++)
            {
                document.GetObject(new PdfObjectId(number)).Should().BeOfType<PdfString>();
            }
        }

        document.Diagnostics.Where(entry => entry.Code == PdfDiagnosticCodes.ObjectStreamMemberMoved).Select(entry => entry.Message).Should().Equal(
            "Object 2 is at index 1 of object stream 110, not at index 0, where the cross-reference index places it.",
            "Object 3 is at index 0 of object stream 110, not at index 1, where the cross-reference index places it.");
    }

    [Fact]
    public void An_object_its_stream_s_header_does_not_list_is_null()
    {
        using var document = PdfDocument.Open(Packed(header => header.Replace("3 ", "7 ", StringComparison.Ordinal)));

        document.GetObject(new PdfObjectId(3)).Should().BeSameAs(PdfNull.Instance);
    }

    [Fact]
    public void An_object_stream_that_declares_no_object_needs_no_first_and_is_not_reported()
    {
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(5, "<< /Type /ObjStm /N 0 /Length 0 >>\nstream\n\nendstream")
            .BuildClassic(rootNumber: 1, includeXRef: false);

        using var document = PdfDocument.Open(file);

        document.Diagnostics.Should().NotContain(entry => entry.Code == PdfDiagnosticCodes.ObjectStreamUnreadable);
    }

    [Fact]
    public void An_object_stream_that_declares_no_object_serves_none_and_is_not_reported()
    {
        var bytes = Packed(header => header);
        var text = Encoding.Latin1.GetString(bytes);
        using var document = PdfDocument.Open(Encoding.Latin1.GetBytes(text.Replace("/N 2", "/N 0", StringComparison.Ordinal)));

        document.GetObject(new PdfObjectId(2)).Should().BeSameAs(PdfNull.Instance);
        document.Diagnostics.Should().BeEmpty();
    }

    private const string KeptRule =
        " Of each number, the definition kept is the last written directly in the file, or, for a number written only inside object streams, the first listed in the object stream read first.";

    [Fact]
    public void Reports_an_object_a_rebuild_finds_defined_twice_in_the_file_once_as_information()
    {
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(4, "(first)")
            .WithObject(4, "(second)")
            .BuildClassic(rootNumber: 1, includeXRef: false);

        using var document = PdfDocument.Open(file);

        document.GetObject(new PdfObjectId(4)).Should().BeOfType<PdfString>().Which.ToText().Should().Be("second");
        var report = document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.ObjectRedefined).Which;
        report.Severity.Should().Be(PdfDiagnosticSeverity.Information);
        report.Position.Should().Be(-1);
        report.Message.Should().Be("Rebuilding the index met a second definition of object 4." + KeptRule);
    }

    [Theory]
    [InlineData("6 0 6 6 ", "(one)\n(two)", "(one)")]
    [InlineData("6 0 ", "(one)", "(one)")]
    public void Reports_an_object_a_rebuild_finds_twice_inside_object_streams_and_keeps_the_first_listed(string header, string body, string kept)
    {
        // Listed twice in one header, or once in each of two object streams: the first listed in the stream read
        // first is kept.
        var data = header + body;
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(5, string.Create(CultureInfo.InvariantCulture, $"<< /Type /ObjStm /N {header.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length / 2} /First {header.Length} /Length {data.Length} >>\nstream\n{data}\nendstream"))
            .WithObject(7, "<< /Type /ObjStm /N 1 /First 4 /Length 9 >>\nstream\n6 0 (two)\nendstream")
            .BuildClassic(rootNumber: 1, includeXRef: false);

        using var document = PdfDocument.Open(file);

        document.GetObject(new PdfObjectId(6)).Should().BeOfType<PdfString>().Which.ToText().Should().Be(kept.Trim('(', ')'));
        var report = document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.ObjectRedefined).Which;
        report.Message.Should().StartWith("Rebuilding the index met ").And.EndWith(KeptRule);
    }

    [Theory]
    [InlineData(new[] { 4, 4, 4 }, "met 2 definitions of object numbers it had already found, of object 4.")]
    [InlineData(new[] { 4, 4, 5, 5 }, "met 2 definitions of object numbers it had already found, of objects 4, 5.")]
    [InlineData(
        new[] { 10, 10, 11, 11, 12, 12, 13, 13, 14, 14, 15, 15, 16, 16, 17, 17, 18, 18, 19, 19, 20, 20 },
        "met 11 definitions of object numbers it had already found, among them those of objects 10, 11, 12, 13, 14, 15, 16, 17, 18, 19.")]
    public void Lists_the_first_numbers_a_rebuild_finds_defined_more_than_once(int[] numbers, string met)
    {
        var builder = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>");

        foreach (var number in numbers)
        {
            builder.WithObject(number, "null");
        }

        using var document = PdfDocument.Open(builder.BuildClassic(rootNumber: 1, includeXRef: false));

        document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.ObjectRedefined)
            .Which.Message.Should().Be("Rebuilding the index " + met + KeptRule);
    }

    [Fact]
    public void Reports_no_redefinition_when_a_rebuild_finds_each_number_once()
    {
        using var document = PdfDocument.Open(SampleDocument().BuildClassic(rootNumber: 1, includeXRef: false));

        document.WasRepaired.Should().BeTrue();
        document.Diagnostics.Should().NotContain(entry => entry.Code == PdfDiagnosticCodes.ObjectRedefined);
    }

    [Fact]
    public void Takes_a_header_the_scan_finds_in_the_overlap_of_two_windows_for_one_definition()
    {
        // The scan reads a megabyte at a time, each window overlapping the last by 64 bytes: object 1's header lies
        // 40 bytes before the first megabyte ends, so both windows find it.
        var file = new TestPdfBuilder()
            .WithObject(9, "(" + new string('x', 1_048_503) + ")")
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .BuildClassic(rootNumber: 1, includeXRef: false);
        file.AsSpan().IndexOf("1 0 obj"u8).Should().Be((1024 * 1024) - 40);

        using var document = PdfDocument.Open(file);

        document.WasRepaired.Should().BeTrue();
        document.Catalog.IsOfType(PdfName.Catalog).Should().BeTrue();
        document.Diagnostics.Should().NotContain(entry => entry.Code == PdfDiagnosticCodes.ObjectRedefined);
    }

    [Fact]
    public void Counts_two_definitions_the_scan_finds_in_the_overlap_of_two_windows_as_one_redefinition()
    {
        // Both headers of object 7 lie in the 64 bytes the first and second megabyte's windows share; object 8 takes the
        // file past the first.
        byte[] Build(int filler) => new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(9, "(" + new string('x', filler) + ")")
            .WithObject(7, "null")
            .WithObject(7, "1")
            .WithObject(8, "(" + new string('y', 200) + ")")
            .BuildClassic(rootNumber: 1, includeXRef: false);
        var probe = Build(1_000_000);
        var file = Build(1_000_000 + ((1024 * 1024) - 61) - probe.AsSpan().IndexOf("7 0 obj"u8));
        file.AsSpan().IndexOf("7 0 obj"u8).Should().Be((1024 * 1024) - 61);

        using var document = PdfDocument.Open(file);

        document.GetObject(new PdfObjectId(7)).AsInteger().Should().Be(1);
        document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.ObjectRedefined)
            .Which.Message.Should().Be("Rebuilding the index met a second definition of object 7." + KeptRule);
    }

    [Fact]
    public void Reads_a_header_that_straddles_the_start_of_a_scan_window_under_its_own_number()
    {
        // "123 0 obj" starts a byte before the second megabyte's window, which object 8 makes the file reach: that window
        // must not read it as object 23.
        byte[] Build(int filler) => new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(23, "(the real object 23)")
            .WithObject(9, "(" + new string('x', filler) + ")")
            .WithObject(123, "(object 123)")
            .WithObject(8, "(" + new string('y', 200) + ")")
            .BuildClassic(rootNumber: 1, includeXRef: false);
        var probe = Build(1_000_000);
        var file = Build(1_000_000 + ((1024 * 1024) - 65) - probe.AsSpan().IndexOf("123 0 obj"u8));
        file.AsSpan().IndexOf("123 0 obj"u8).Should().Be((1024 * 1024) - 65);

        using var document = PdfDocument.Open(file);

        document.GetObject(new PdfObjectId(23)).Should().BeOfType<PdfString>().Which.ToText().Should().Be("the real object 23");
        document.GetObject(new PdfObjectId(123)).Should().BeOfType<PdfString>().Which.ToText().Should().Be("object 123");
        document.Diagnostics.Should().NotContain(entry => entry.Code == PdfDiagnosticCodes.ObjectRedefined);
    }

    [Fact]
    public void Writes_the_numbers_a_rebuild_finds_redefined_whatever_the_culture()
    {
        // One object stream lists object 6 a thousand and two times, each at its own offset: the 1,001 definitions past
        // the first are counted with the invariant culture's comma, not with the non-breaking space Swedish groups digits
        // with.
        var header = string.Concat(Enumerable.Range(0, 1002).Select(index => string.Create(CultureInfo.InvariantCulture, $"6 {2 * index} ")));
        var data = header + string.Concat(Enumerable.Repeat("1 ", 1002));
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(5, $"<< /Type /ObjStm /N 1002 /First {header.Length} /Length {data.Length} >>\nstream\n{data}\nendstream")
            .BuildClassic(rootNumber: 1, includeXRef: false);
        var culture = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("sv-SE");
            using var document = PdfDocument.Open(file);

            document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.ObjectRedefined)
                .Which.Message.Should().Be("Rebuilding the index met 1,001 definitions of object numbers it had already found, of object 6." + KeptRule);
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    [Theory]
    [InlineData(" 65536 n ")]
    [InlineData(" -0001 n ")]
    public void Serves_no_older_revision_of_an_object_whose_newest_row_is_refused(string row)
    {
        // The update's row for object 3 gives a generation no object in use can have. Refused, it must not let the row
        // of the section the update's /Prev names stand for the object's current revision: the object is the index's to
        // find, and the rebuild finds the last definition written.
        var original = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(3, "(first)")
            .BuildClassic(rootNumber: 1);
        var text = Encoding.Latin1.GetString(TestPdfBuilder.AppendIncrementalUpdate(original, rootNumber: 1, [(3, "(second)")]));
        var second = text.LastIndexOf("3 0 obj", StringComparison.Ordinal);
        var written = string.Create(CultureInfo.InvariantCulture, $"{second:D10} 00000 n ");
        var file = Encoding.Latin1.GetBytes(text.Replace(written, string.Create(CultureInfo.InvariantCulture, $"{second:D10}") + row, StringComparison.Ordinal));

        using var document = PdfDocument.Open(file);

        document.GetObject(new PdfObjectId(3)).Should().BeOfType<PdfString>().Which.ToText().Should().Be("second");
    }

    [Fact]
    public void Indexes_no_member_a_rebuild_finds_listed_under_a_number_no_object_can_have()
    {
        // Two object streams list -5: the rebuild once indexed it, and met it twice (#157).
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(5, "<< /Type /ObjStm /N 1 /First 5 /Length 10 >>\nstream\n-5 0 (one)\nendstream")
            .WithObject(7, "<< /Type /ObjStm /N 1 /First 5 /Length 10 >>\nstream\n-5 0 (two)\nendstream")
            .BuildClassic(rootNumber: 1, includeXRef: false);

        using var document = PdfDocument.Open(file);

        document.ObjectNumbers.Should().OnlyContain(number => number > 0);
        document.Diagnostics.Should().NotContain(entry => entry.Code == PdfDiagnosticCodes.ObjectRedefined);
        document.Diagnostics.Where(entry => entry.Code == PdfDiagnosticCodes.ObjectStreamUnreadable).Select(entry => entry.Message)
            .Should().Equal(
                "Object stream 5 has a header that lists object -5 at offset 0, which no member can be, after 0 of the 1 objects its /N declares; none of its objects can be read from it.",
                "Object stream 7 has a header that lists object -5 at offset 0, which no member can be, after 0 of the 1 objects its /N declares; none of its objects can be read from it.");
    }

    [Fact]
    public void Drops_what_parsing_meets_where_a_guard_cut_an_object_stream_s_data()
    {
        // The data is cut right after object 3's /Parent key: what the parser makes of the end is the guard's.
        const string Pages = "<< /Type /Pages /Kids [3 0 R] /Count 1 >>";
        const string Page = "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>";
        var header = string.Create(CultureInfo.InvariantCulture, $"2 0 3 {Pages.Length + 1} ");
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, Pages)
            .WithObject(3, Page)
            .BuildWithXRefStream(rootNumber: 1, compressedObjects: [2, 3], compressObjectStream: true);
        var cut = header.Length + Pages.Length + 1 + "<< /Type /Page /Parent".Length;
        var options = PdfReaderOptions.Default with { Limits = PdfReaderLimits.Default with { MaxDecodedStreamLength = cut } };

        using var document = PdfDocument.Open(file, options);
        document.GetObject(new PdfObjectId(3)).AsDictionary().IsOfType(PdfName.Page).Should().BeTrue();

        document.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.LimitDecodedStream);
    }

    [Fact]
    public void Rebuilds_an_index_whose_objects_lie_past_the_first_megabyte()
    {
        // The scan reads the file a megabyte at a time: the catalog and the page lie in the second.
        var bytes = new TestPdfBuilder()
            .WithObject(9, "(" + new string('x', 1_100_000) + ")")
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .WithObject(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R >>")
            .Stream(4, string.Empty, "BT (Bonjour) Tj ET")
            .BuildClassic(rootNumber: 1, includeXRef: false);

        using var document = PdfDocument.Open(bytes);

        document.WasRepaired.Should().BeTrue();
        PageContent(document).Should().Be("BT (Bonjour) Tj ET");
    }

    [Fact]
    public void An_object_a_rebuild_does_not_find_either_is_null()
    {
        // The last row is malformed, which leaves the index incomplete: asking for an object it lacks rebuilds it.
        using var document = PdfDocument.Open(PdfTemplate.SoundWith("{row:3}", "0000000abc 00000 n "));
        document.WasRepaired.Should().BeFalse();

        document.GetObject(new PdfObjectId(99)).Should().BeSameAs(PdfNull.Instance);

        document.WasRepaired.Should().BeTrue();
    }

    [Fact]
    public void A_reference_the_chain_cannot_read_rebuilds_nothing_while_the_chain_is_read()
    {
        // Two cross-reference streams, each header split by a comment, which a rebuild's scan does not take for an object
        // header. The newer declares rows its data lacks, leaving the index incomplete; the older, which its /Prev names,
        // numbers its rows from object 9, which the index lacks. Resolving it once rebuilt the index before any of its rows
        // was read, and the chain's empty index asked for a rebuild once more (#182). Nothing is loaded while the chain is
        // read: the reference names nothing the chain can read, the older stream is malformed, and the empty index the
        // chain leaves is rebuilt once.
        const string Template = """
            %PDF-1.5
            7 0 % a comment between the generation and the keyword
            obj
            << /Type /XRef /W [1 2 0] /Index [0 2] /Prev 00000 /Length 0 >>
            stream

            endstream
            endobj
            8 0 % another
            obj
            << /Type /XRef /W [1 2 0] /Index [9 0 R 1] /Length 0 >>
            stream

            endstream
            endobj
            startxref
            9
            %%EOF

            """;
        var text = Template.Replace("\r\n", "\n", StringComparison.Ordinal);
        var older = text.IndexOf("8 0 %", StringComparison.Ordinal);
        var file = Encoding.ASCII.GetBytes(text.Replace("/Prev 00000", string.Create(CultureInfo.InvariantCulture, $"/Prev {older:D5}"), StringComparison.Ordinal));
        var diagnostics = new PdfDiagnostics();

        var reader = new PdfFileReader(
            PdfFileSource.FromMemory(file),
            diagnostics,
            new PdfLimitGuard(PdfReaderLimits.Default, throwOnLimit: false),
            cacheCapacity: 64,
            ownsSource: true);

        reader.ObjectCount.Should().Be(0);
        reader.WasRepaired.Should().BeTrue();
        reader.Structure.ChainRead.Should().BeFalse();
        reader.Structure.Sections[1].Fault.Should().Be("its /Index holds the reference 9 0 R, which no section read before it places where it can be read");
        diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(PdfDiagnosticCodes.XRefSectionUnreadable, PdfDiagnosticCodes.XRefRebuilt);
        diagnostics[1].Message.Should().Be("The cross-reference index was rebuilt by scanning the file.");
    }

    [Fact]
    public void Reads_a_cross_reference_stream_whose_length_only_it_indexes_as_its_direct_twin()
    {
        // ISO 32000-1 lets a stream's /Length be indirect, a cross-reference stream's too. Object 4, which only the stream
        // indexes, cannot be read while the chain is: the data is taken up to its endstream, and the length read and checked
        // once the chain is read. Nothing is reported, and object 4 reads what it holds — it was once kept as null, with a
        // /Length said to name an object the file lacks (#182).
        using var document = PdfDocument.Open(ChainFiles.AloneWithIndirectLength());

        document.Diagnostics.Should().BeEmpty();
        document.WasRepaired.Should().BeFalse();
        document.GetObject(new PdfObjectId(4)).AsInteger().Should().Be(42);
        document.GetObject(new PdfObjectId(3)).AsDictionary().IsOfType(PdfName.Page).Should().BeTrue();
    }

    [Theory]
    [InlineData(4, "The stream declared 46 bytes but ended after 42.")]
    [InlineData(-4, "The stream declared 38 bytes but ended after 42.")]
    [InlineData(1, null)]
    public void Checks_a_deferred_length_against_the_data_once_the_chain_is_read(int lengthOff, string? message)
    {
        // Object 4 gives 46 or 38 bytes where the data holds 42: the length is reported once the chain is read, as any
        // stream's is. One that counts the end-of-line before endstream is confirmed by it, as the parser takes any
        // stream's, and says nothing. (The data's last bytes are zeros, which PDF counts as white space: a length that
        // leaves them out is confirmed as well.)
        using var document = PdfDocument.Open(ChainFiles.AloneWithIndirectLength(lengthOff));

        document.WasRepaired.Should().BeFalse();

        if (message is null)
        {
            document.Diagnostics.Should().BeEmpty();
            return;
        }

        var report = document.Diagnostics.Should().ContainSingle().Which;
        report.Code.Should().Be(PdfDiagnosticCodes.StreamLengthInvalid);
        report.Message.Should().Be(message);
    }

    [Theory]
    [InlineData("4 0 obj\n42\n", "4 0 obj\n/F\n", "The /Length of cross-reference stream 5 names object 4 0, which holds the name /F, not a length; the chain took its data up to its endstream, after 42 bytes.")]
    [InlineData("4 0 obj\n42\n", "4 0 obj\n-1\n", "The /Length of cross-reference stream 5 names object 4 0, which holds the integer -1, not a length; the chain took its data up to its endstream, after 42 bytes.")]
    [InlineData("/Length 4 0 R", "/Length 9 0 R", "The /Length of cross-reference stream 5 names object 9 0, which the file lacks; the chain took its data up to its endstream, after 42 bytes.")]
    [InlineData("4 0 obj\n42\n", "4 0 xbj\n42\n", "The stream's /Length names object 4 0, which could not be read; its data ends after 42 bytes.")]
    public void Reports_a_deferred_length_that_gives_no_length_once_the_chain_is_read(string written, string replacement, string message)
    {
        // Object 4 holds a name, or a negative integer; the /Length names an object no section indexes; object 4's header
        // is misspelled, which no search near its entry nor any rebuild then finds — the rebuild its load sets off parses
        // the stream again, and the parser reports the length first: the stream is reported once. Each replacement keeps
        // every offset.
        var file = Encoding.Latin1.GetBytes(
            Encoding.Latin1.GetString(ChainFiles.AloneWithIndirectLength()).Replace(written, replacement, StringComparison.Ordinal));
        using var document = PdfDocument.Open(file);

        document.Diagnostics.Should().ContainSingle(diagnostic => diagnostic.Code == PdfDiagnosticCodes.StreamLengthInvalid)
            .Which.Message.Should().Be(message);
        document.GetObject(new PdfObjectId(3)).AsDictionary().IsOfType(PdfName.Page).Should().BeTrue();
    }

    [Theory]
    [InlineData(false, true, "\n")]
    [InlineData(false, true, "\r\n")]
    [InlineData(false, false, "\r\n")]
    [InlineData(true, false, "\r\n")]
    public void Reads_every_row_of_a_cross_reference_stream_whose_data_a_deferred_length_leaves_to_its_end_of_line(
        bool compressed, bool endingInCarriageReturn, string endOfLine)
    {
        // The data's last byte is a carriage return — object 6's row, free with generation 13 —, or the end-of-line before
        // endstream is one. Taken as part of that end-of-line, the last byte was once lost with the last row, and the
        // length said to disagree with the data. The chain keeps the carriage return; the length, read once the chain is,
        // says how much of it is data, and an end-of-line it leaves out of the data is no fault.
        var file = ChainFiles.AloneWithIndirectLength(compressed: compressed, endingInCarriageReturn: endingInCarriageReturn, endOfLine: endOfLine);
        using var document = PdfDocument.Open(file);

        document.Diagnostics.Should().BeEmpty();
        document.WasRepaired.Should().BeFalse();
        document.Reader.Index.TryGet(6, out var six).Should().Be(endingInCarriageReturn);
        document.GetObject(new PdfObjectId(3)).AsDictionary().IsOfType(PdfName.Page).Should().BeTrue();
    }

    [Fact]
    public void Reports_the_rows_a_deferred_length_shows_the_chain_did_not_read()
    {
        // The rows of objects 6 and 7, of reserved types, spell an endstream after an end-of-line: the chain, which could
        // not read the /Length, took the data up to it. The length, read once the chain is, gives 14 bytes more, which the
        // endstream after them confirms: what the chain left unread is reported. The rows are not read again.
        var file = ChainFiles.AloneWithIndirectLength(spellingEndStream: true);
        using var document = PdfDocument.Open(file);

        var report = document.Diagnostics.Should().ContainSingle().Which;
        report.Code.Should().Be(PdfDiagnosticCodes.StreamLengthInvalid);
        report.Position.Should().Be(PdfTemplate.OffsetOf(file, "stream\n", 1) + "stream\n".Length);
        report.Message.Should().Be(
            "The /Length of cross-reference stream 5 gives 56 bytes, which an endstream confirms; the chain, which could not read it, took the data up to an endstream 42 bytes in, and read no row past it.");
        document.GetObject(new PdfObjectId(3)).AsDictionary().IsOfType(PdfName.Page).Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(ChainFiles.Keys), MemberType = typeof(ChainFiles))]
    public void Changes_nothing_of_the_index_while_the_chain_is_read(string key)
    {
        // #182: a value the older stream refers to, object 5, which the newer table places outside the file. Loading it as
        // the chain was read rebuilt the index, and the rebuilt one was served as the chain's. Whatever reads object 5 now
        // does so once the chain is read — the catalog looked for, the /Length checked —, behind a copy of the index as the
        // file wrote it; a /Type the rows are read without needs nothing read.
        using var document = PdfDocument.Open(ChainFiles.UnderAnUpdate(key, placed: false));

        document.WasRepaired.Should().Be(key != "Type");
        document.Reader.Structure.ChainRead.Should().BeTrue();
        document.Reader.ChainIndex!.TryGet(5, out var written).Should().BeTrue();
        written.Offset.Should().Be(99999);

        if (document.WasRepaired)
        {
            document.Reader.ChainIndex.Should().NotBeSameAs(document.Reader.Index);
        }
    }

    [Fact]
    public void A_value_the_chain_reads_that_a_guard_cuts_leaves_the_section_whole()
    {
        // #182: object 5, the older stream's /Length, runs past MaxObjectLength, padded with white space. Its load, as the
        // chain was read, reached the guard and marked the section cut, which silenced what the rules found of it. The
        // chain leaves it unread; the stream's data is taken up to its endstream, and the guard reached, and reported at
        // object 5, when the length is read once the chain is.
        var file = ChainFiles.UnderAnUpdate("Length", placed: true, padding: 3000);
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = 1024 } };

        using var document = PdfDocument.Open(file, options);

        document.Reader.Structure.Sections.Should().OnlyContain(section => !section.CutByLimit);
        document.Diagnostics.Should().ContainSingle(diagnostic => diagnostic.Code == PdfDiagnosticCodes.LimitObject)
            .Which.Position.Should().Be(PdfTemplate.OffsetOf(file, "\n5 0 obj") + 1);
    }

    [Fact]
    public void A_guard_a_deferred_length_reaches_throws_once_the_chain_is_read_when_asked_to()
    {
        // The guard left the length unread as the chain was read; read once the chain is, it is reached as any load
        // reaches it, and thrown when the caller asked for it.
        var file = ChainFiles.UnderAnUpdate("Length", placed: true, padding: 3000);
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = 1024 }, ThrowOnLimit = true };

        var opening = () => PdfDocument.Open(file, options);

        opening.Should().Throw<PdfLimitExceededException>().Which.LimitName.Should().Be(nameof(PdfReaderLimits.MaxObjectLength));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_cross_reference_stream_whose_dictionary_the_trailer_guard_cuts_before_it_declares_itself_reaches_the_guard(bool throwOnLimit)
    {
        // Where the chain names a section, a dictionary the guard cuts is the guard's to report, whatever the cut part
        // holds — not yet /Type, nor /W. Only a place the section may have been moved to is a guess, left in silence.
        var file = ChainFiles.UnderAnUpdate(
            "/Producer (" + new string('x', 400) + ") /Type /XRef /Size 8 /W [1 4 2] /Length {length}", "0", compressed: false, predicted: false, placed: true);
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxTrailerLength = 256 }, ThrowOnLimit = throwOnLimit };

        if (throwOnLimit)
        {
            var opening = () => PdfDocument.Open(file, options);
            opening.Should().Throw<PdfLimitExceededException>().Which.LimitName.Should().Be(nameof(PdfReaderLimits.MaxTrailerLength));
            return;
        }

        using var document = PdfDocument.Open(file, options);

        document.Diagnostics.Should().ContainSingle(diagnostic => diagnostic.Code == PdfDiagnosticCodes.LimitTrailer)
            .Which.Position.Should().Be(PdfTemplate.OffsetOf(file, "\n7 0 obj") + 1);
    }

    [Theory]
    [InlineData("<< /A 1 /B >>", 64 * 1024)]
    [InlineData("<< /Producer (an object longer than the trailer guard allows, which a place the section was moved to is only a guess at; it is read as itself when it is asked for, under the guard that bounds objects) >>", 128)]
    public void A_place_a_section_may_have_moved_to_that_holds_another_object_leaves_nothing_of_its_reading(string six, int maxTrailerLength)
    {
        // /Prev names a place 2 bytes into object 6, just before the older stream: object 6, nearer, is tried first, and is
        // no section — a dictionary with a key short of a value, or one the trailer guard cuts. Neither its syntax fault
        // nor the guard is reported: it was read as a section, and is read as itself when asked for.
        var built = ChainFiles.UnderAnUpdate("/Type /XRef /Size 8 /W [1 4 2] /Length {length}", "0", compressed: false, predicted: false, placed: true, six: six);
        var stream = PdfTemplate.OffsetOf(built, "\n7 0 obj") + 1;
        var named = PdfTemplate.OffsetOf(built, "\n6 0 obj") + 3;
        var prev = string.Create(CultureInfo.InvariantCulture, $"/Prev {named}");
        prev.Length.Should().Be(string.Create(CultureInfo.InvariantCulture, $"/Prev {stream}").Length, "no offset moves");
        var file = Encoding.Latin1.GetBytes(Encoding.Latin1.GetString(built).Replace(
            string.Create(CultureInfo.InvariantCulture, $"/Prev {stream}"), prev, StringComparison.Ordinal));
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxTrailerLength = maxTrailerLength } };

        using var document = PdfDocument.Open(file, options);

        document.WasRepaired.Should().BeFalse();
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(PdfDiagnosticCodes.XRefOffsetAdjusted);
        document.GetObject(new PdfObjectId(6)).Should().NotBeSameAs(PdfNull.Instance);
    }

    [Fact]
    public void Reads_a_value_the_chain_refers_to_past_the_window_it_starts_with()
    {
        // Object 5, the older stream's /Index, runs past the 8 KB the reading starts with, padded with white space: the
        // window grows, as a load's does, up to MaxObjectLength.
        using var document = PdfDocument.Open(ChainFiles.UnderAnUpdate("Index", placed: true, padding: 3 * PdfFileReader.InitialObjectWindow));

        document.WasRepaired.Should().BeFalse();
        document.Diagnostics.Should().BeEmpty();
        document.GetObject(new PdfObjectId(3)).AsDictionary().IsOfType(PdfName.Page).Should().BeTrue();
    }

    [Fact]
    public void Finds_an_object_whose_entry_points_past_the_end_of_the_file()
    {
        using var document = PdfDocument.Open(PdfTemplate.SoundWith("{row:3}", "0009999999 00000 n "));

        document.GetObject(new PdfObjectId(3)).AsDictionary().IsOfType(PdfName.Page).Should().BeTrue();
        document.WasRepaired.Should().BeTrue();
    }

    [Fact]
    public void A_rebuild_finds_a_header_with_the_largest_generation_an_object_in_use_may_have()
    {
        const string Objects = """
            1 0 obj
            << /Type /Catalog /Pages 2 65535 R >>
            endobj
            2 65535 obj
            << /Type /Pages /Kids [] /Count 0 >>
            endobj
            %%EOF

            """;

        using var document = PdfDocument.Open(Encoding.ASCII.GetBytes("%PDF-1.7\n" + Objects));

        document.WasRepaired.Should().BeTrue();
        document.GetObject(new PdfObjectId(2)).AsDictionary().IsOfType(PdfName.Pages).Should().BeTrue();
        document.Reader.Index.TryGet(2, out var entry).Should().BeTrue();
        entry.Generation.Should().Be(65535, "the rebuilt entry records the generation its header gives (#118)");
    }

    [Theory]
    [InlineData("3 1 obj", "3 1 R", 1)]
    [InlineData("3 0001 obj", "3 1 R", 1)]
    [InlineData("3\t\r\n2\0\f obj", "3 2 R", 2)]
    [InlineData("3 00000000000000000000002 obj", "3 2 R", 2)]
    public void A_rebuilt_entry_records_the_generation_its_header_gives(string header, string reference, int generation)
    {
        // #118: the scan read the generation's digits to know a header from the obj of an endobj, and recorded every object
        // at generation 0 all the same. It records the generation as the parser reads it, white space and zeros aside.
        var objects = string.Create(
            CultureInfo.InvariantCulture,
            $"1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n2 0 obj\n<< /Type /Pages /Kids [{reference}] /Count 1 >>\nendobj\n{header}\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>\nendobj\n%%EOF\n");
        using var document = PdfDocument.Open(Encoding.Latin1.GetBytes("%PDF-1.7\n" + objects));

        document.WasRepaired.Should().BeTrue();
        document.Reader.Index.TryGet(3, out var entry).Should().BeTrue();
        entry.Generation.Should().Be(generation);
        document.GetObject(new PdfObjectId(3, generation)).AsDictionary().IsOfType(PdfName.Page).Should().BeTrue();
    }

    [Theory]
    [InlineData("4 0 obj", "4 1 obj", 1)]
    [InlineData("4 2 obj", "4 1 obj", 1)]
    public void A_rebuild_keeps_the_last_definition_of_a_number_under_the_generation_its_header_gives(string first, string last, int generation)
    {
        // An update that wrote object 4 again under a new generation, or a later definition under a lower one: the last
        // definition wins, as it did — which one a rebuild keeps is #189's —, and its entry records its own generation.
        var objects = string.Create(
            CultureInfo.InvariantCulture,
            $"1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n{first}\n(older)\nendobj\n{last}\n(newer)\nendobj\n%%EOF\n");
        var file = Encoding.ASCII.GetBytes("%PDF-1.7\n" + objects);
        using var document = PdfDocument.Open(file);

        document.WasRepaired.Should().BeTrue();
        document.Reader.Index.TryGet(4, out var entry).Should().BeTrue();
        entry.Generation.Should().Be(generation);
        entry.Offset.Should().Be(PdfTemplate.OffsetOf(file, last));
        document.GetObject(new PdfObjectId(4, generation)).AsText().Should().Be("newer");
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 0)]
    [InlineData(1, 7)]
    [InlineData(0, 0)]
    [InlineData(0, 1)]
    [InlineData(2, 2)]
    [InlineData(2, 0)]
    public void A_relocated_entry_records_the_generation_its_header_gives_whatever_reference_found_it(int row, int asked)
    {
        // #118: object 3's row lies 4 bytes past its header, written 3 1 obj. The relocation recorded the generation of the
        // reference that asked first, so the index depended on the order objects were asked for, and a finding could name a
        // generation that neither the row nor the header gives. The chain's index keeps the row's, which the rules judge.
        var template = PdfTemplate.Sound
            .Replace("3 0 obj", "3 1 obj", StringComparison.Ordinal)
            .Replace("/Kids [3 0 R]", "/Kids [3 1 R]", StringComparison.Ordinal)
            .Replace("{row:3}", string.Create(CultureInfo.InvariantCulture, $"{{row:3:{row}:4}}"), StringComparison.Ordinal);
        using var document = PdfDocument.Open(PdfTemplate.Build(template));

        document.GetObject(new PdfObjectId(3, asked)).AsDictionary().IsOfType(PdfName.Page).Should().BeTrue();

        document.Diagnostics.Should().ContainSingle(diagnostic => diagnostic.Code == PdfDiagnosticCodes.XRefOffsetAdjusted);
        document.Reader.Index.TryGet(3, out var entry).Should().BeTrue();
        entry.Generation.Should().Be(1);
        document.Reader.ChainIndex!.TryGet(3, out var written).Should().BeTrue();
        written.Generation.Should().Be(row);
    }

    [Theory]
    [InlineData("row")]
    [InlineData("shifted")]
    [InlineData("rebuilt")]
    public void A_catalog_found_by_its_type_is_named_as_its_entry_names_it(string layout)
    {
        // #118: the trailer's /Root names nothing, and the reader finds the catalog, written 1 1 obj, among the objects the
        // index holds — through a row of 1, through a row of 0 a few bytes off, which the catalog's own load relocates, or
        // in a rebuilt index. It once put 1 0 R in the trailer, so that a report named the catalog both 1 1 and 1 0.
        using var document = PdfDocument.Open(CatalogWrittenUnderGeneration1(layout));

        document.Diagnostics.Should().ContainSingle(diagnostic => diagnostic.Code == PdfDiagnosticCodes.TrailerRootRecovered);
        document.WasRepaired.Should().Be(layout == "rebuilt");
        document.Trailer.GetRaw(PdfName.Root).Should().BeOfType<PdfReference>().Which.Id.Should().Be(new PdfObjectId(1, 1));
        document.Catalog.IsOfType(PdfName.Catalog).Should().BeTrue();
    }

    /// <summary>
    /// The sound document with its catalog written <c>1 1 obj</c> and a trailer whose <c>/Root</c> names nothing: the
    /// catalog's row gives generation 1 (<c>row</c>), or 0 a few bytes past its header (<c>shifted</c>), or
    /// <c>startxref</c> names nothing and the index is rebuilt (<c>rebuilt</c>). <paramref name="catalog"/> replaces the
    /// catalog's entries.
    /// </summary>
    internal static byte[] CatalogWrittenUnderGeneration1(string layout, string catalog = "/Type /Catalog /Pages 2 0 R")
    {
        var template = PdfTemplate.Sound
            .Replace("1 0 obj", "1 1 obj", StringComparison.Ordinal)
            .Replace("/Type /Catalog /Pages 2 0 R", catalog, StringComparison.Ordinal)
            .Replace("/Root 1 0 R", "/Root 9 0 R", StringComparison.Ordinal)
            .Replace("{row:1}", layout == "shifted" ? "{row:1:0:4}" : "{row:1:1}", StringComparison.Ordinal);

        return PdfTemplate.Build(layout == "rebuilt" ? template.Replace("startxref\n{xref:1}", "startxref\n999999", StringComparison.Ordinal) : template);
    }

    [Fact]
    public void A_rebuild_finds_a_header_whose_number_and_generation_zeros_lead()
    {
        // Eleven digits each, which the parser reads as 2 and 0: the rebuild's scan judges each run by its value, as the
        // parser does, not by how many digits it has.
        const string Objects = """
            1 0 obj
            << /Type /Catalog /Pages 2 0 R >>
            endobj
            00000000002 00000000000 obj
            << /Type /Pages /Kids [] /Count 0 >>
            endobj
            %%EOF

            """;

        using var document = PdfDocument.Open(Encoding.ASCII.GetBytes("%PDF-1.7\n" + Objects));

        document.WasRepaired.Should().BeTrue();
        document.GetObject(new PdfObjectId(2)).AsDictionary().IsOfType(PdfName.Pages).Should().BeTrue();
    }

    [Fact]
    public void A_rebuild_ignores_object_headers_numbered_zero_or_beyond_what_an_object_number_holds()
    {
        const string Objects = """
            1 0 obj
            << /Type /Catalog /Pages 2 0 R >>
            endobj
            2 0 obj
            << /Type /Pages /Kids [] /Count 0 >>
            endobj
            %%EOF

            """;
        const string Bogus = """
            0 0 obj
            (zero)
            endobj
            9999999999 0 obj
            (too large)
            endobj

            """;

        using var sound = PdfDocument.Open(Encoding.ASCII.GetBytes("%PDF-1.7\n" + Objects));
        using var document = PdfDocument.Open(Encoding.ASCII.GetBytes("%PDF-1.7\n" + Bogus + Objects));

        document.WasRepaired.Should().BeTrue();
        document.ObjectCount.Should().Be(sound.ObjectCount);
        document.Catalog.IsOfType(PdfName.Catalog).Should().BeTrue();
    }

    [Theory]
    [InlineData(LargeIndex.ClassicTable, 300_000, 17_720_000)]
    [InlineData(LargeIndex.CrossReferenceStream, 300_002, 30_960_000)]
    public void Opening_an_index_of_three_hundred_thousand_objects_stays_within_its_memory_budget(
        string shape, int entries, long budget)
    {
        // M01's file of several hundred thousand objects (#193). Opening allocates the reader's map of the index, which the
        // section sizes before its rows are read to 324,449 slots of 52 bytes here, and a cross-reference stream's rows
        // decoded besides. Measured on 2026-10-14, after a warm-up: 16,875,176 bytes for the table, 29,478,272 for the
        // stream, whose rows Flate stores uncompressed so that no runtime's zlib moves the figure — 32,571,368 and
        // 45,174,464 when the map grew by doubling (#164). The budget is 5 % over each. 324,449 is the first prime of
        // .NET's table past 300,000, so a change of a few thousand objects does not move it either.
        var file = LargeIndexFile(shape);
        using (PdfDocument.Open(file))
        {
            // The first opening pays what any first call does once: what the reader's statics and names cost.
        }

        var elapsed = Stopwatch.StartNew();
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        using var document = PdfDocument.Open(file);
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        elapsed.Stop();

        document.ObjectCount.Should().Be(entries, "the file holds what its index says, and the reader read it whole");
        document.WasRepaired.Should().BeFalse();
        document.Diagnostics.Should().BeEmpty();
        allocated.Should().BeLessThan(budget, $"opening {shape} of {entries:N0} entries holds the map of them and little else");

        // A bound, not a throughput: what a regression that allocates nothing, such as a quadratic search, would pass.
        elapsed.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void Opening_a_chain_of_a_thousand_sections_resizes_the_index_no_more_often_than_doubling_it()
    {
        // Each section makes room for its rows, and never by less than the map holds already: room made for each in turn
        // would resize the map at every section, and copy it whole each time past the largest prime .NET keeps in its
        // table. Once a doubling comes within a doubling of what the newest /Size numbers, room is made for it at once.
        // A thousand sections of 300 rows: 30,562,560 bytes allocated, measured twice on 2026-10-14 after a warm-up,
        // where growth allocated 33,196,448, doubling alone 44,621,888, and, on sections of a thousand rows, room made for
        // each in turn 2.6 times what growth did. The budget is 5 % over.
        var file = ChainOfSections(sections: 1000, rows: 300);
        using (PdfDocument.Open(file))
        {
            // The first opening pays what any first call does once.
        }

        var elapsed = Stopwatch.StartNew();
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        using var document = PdfDocument.Open(file);
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        elapsed.Stop();

        document.ObjectCount.Should().Be(300_000);
        document.WasRepaired.Should().BeFalse();
        document.Diagnostics.Should().BeEmpty();
        document.Catalog.IsOfType(PdfName.Catalog).Should().BeTrue();
        allocated.Should().BeLessThan(32_100_000, "the map doubles at most, section after section, and lands on the newest /Size");
        elapsed.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void A_newest_size_far_past_the_room_made_so_far_is_not_made_room_for_at_once()
    {
        // The newest section's /Size claims 100,000 objects; its rows and the older section's hold 2,000. Room is made for
        // the size at once only when a doubling would come within a doubling of it, four times what the map held.
        var text = Encoding.Latin1.GetString(ChainOfSections(sections: 2, rows: 1000));
        var newest = text.LastIndexOf("/Size 2000 ", StringComparison.Ordinal);
        var file = Encoding.Latin1.GetBytes(string.Concat(text.AsSpan(0, newest), "/Size 100000 ", text.AsSpan(newest + "/Size 2000 ".Length)));

        using var document = PdfDocument.Open(file);

        document.ObjectCount.Should().Be(2000);
        document.Reader.Index.Capacity.Should().BeLessThan(4000, "the map doubles from the newest section's 1,103 slots");
    }

    [Fact]
    public void An_update_s_rows_and_the_original_s_make_room_for_the_objects_the_newest_size_numbers()
    {
        // The update rewrites a thousand of the original's 3,000 objects: the original's rows for them supersede nothing,
        // and room made for all its rows on top of the update's doubled the map. The newest /Size numbers every object.
        var file = TestPdfBuilder.AppendIncrementalUpdate(
            ChainOfSections(sections: 1, rows: 3000),
            rootNumber: 1,
            [.. Enumerable.Range(3, 1000).Select(number => (number, "(updated)"))]);

        using var document = PdfDocument.Open(file);

        document.ObjectCount.Should().Be(3000);
        document.Reader.Index.Entries.EnsureCapacity(0).Should().BeLessThan(4000, "3,000 objects take 3,371 slots, and 7,013 doubled");
    }

    [Fact]
    public void A_table_of_many_subsections_makes_room_for_its_rows_once()
    {
        // Three hundred subsections of ten rows each: room made for each in turn doubled the map from 11 slots, to 4,861.
        var file = ChainOfSections(sections: 1, rows: 3000);
        var text = Encoding.Latin1.GetString(file);
        var rows = text.IndexOf("xref\n0 3000\n", StringComparison.Ordinal) + "xref\n".Length;
        var table = new StringBuilder();
        var lines = text[(rows + "0 3000\n".Length)..text.IndexOf("trailer", StringComparison.Ordinal)].Split("\r\n");

        for (var first = 0; first < 3000; first += 10)
        {
            table.Append(CultureInfo.InvariantCulture, $"{first} 10\n");

            for (var number = first; number < first + 10; number++)
            {
                table.Append(lines[number]).Append("\r\n");
            }
        }

        // The table follows the objects, so that the rows written in subsections move no offset.
        var split = string.Concat(text.AsSpan(0, rows), table.ToString(), text.AsSpan(text.IndexOf("trailer", StringComparison.Ordinal)));
        using var document = PdfDocument.Open(Encoding.Latin1.GetBytes(split));

        document.ObjectCount.Should().Be(3000);
        document.Diagnostics.Should().BeEmpty();
        document.Reader.Index.Entries.EnsureCapacity(0).Should().BeLessThan(4000, "3,000 rows take 3,371 slots");
    }

    [Fact]
    public void A_table_read_again_in_a_larger_window_makes_room_for_its_rows_once()
    {
        // The trailer keyword starts four bytes before the first window's end, which cuts it: the rows are read again in a
        // larger window, and room was made for them again on top of those the index held, which doubled the map to 7,013.
        var file = ChainOfSections(sections: 1, rows: 3276);
        var table = PdfTemplate.OffsetOf(file, "xref\n");
        PdfTemplate.OffsetOf(file, "trailer").Should().Be(table + (64 * 1024) - 4);

        using var document = PdfDocument.Open(file);

        document.ObjectCount.Should().Be(3276);
        document.Reader.Index.Entries.EnsureCapacity(0).Should().BeLessThan(4000, "3,276 rows take 3,371 slots");
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r")]
    public void Rows_written_a_byte_short_make_room_for_each_of_them(string end)
    {
        // Rows ended by one byte, as some producers write them: room made for as many 20-byte rows as their bytes hold fell
        // short of the count, and the map doubled past it, to 156,437 slots for 76,000 objects, as it does when it grows.
        var text = Encoding.Latin1.GetString(ChainOfSections(sections: 1, rows: 76_000));
        var rows = text.IndexOf("xref\n", StringComparison.Ordinal);
        var file = Encoding.Latin1.GetBytes(string.Concat(text.AsSpan(0, rows), text[rows..].Replace("\r\n", end, StringComparison.Ordinal)));
        file.Length.Should().Be(text.Length - 76_000);

        using var document = PdfDocument.Open(file);

        document.ObjectCount.Should().Be(76_000);
        document.Reader.Index.Entries.EnsureCapacity(0).Should().BeLessThan(100_000, "76,000 rows take 90,523 slots");
    }

    [Fact]
    public void A_newest_size_an_update_did_not_raise_makes_room_for_the_objects_its_rows_add()
    {
        // The update adds a hundred objects past those the original numbers, and keeps the original's /Size: room made for
        // no more than the /Size fell short of the count, and the map doubled past it.
        var original = ChainOfSections(sections: 1, rows: 62_800);
        var updates = new (int Number, string Body)[100];

        for (var i = 0; i < updates.Length; i++)
        {
            updates[i] = (62_800 + i, "null");
        }

        var file = Encoding.Latin1.GetBytes(Encoding.Latin1.GetString(TestPdfBuilder.AppendIncrementalUpdate(original, rootNumber: 1, updates))
            .Replace("/Size 62900", "/Size 62800", StringComparison.Ordinal));

        using var document = PdfDocument.Open(file);

        document.ObjectCount.Should().Be(62_900);
        document.Reader.Index.Entries.EnsureCapacity(0).Should().BeLessThan(80_000, "62,900 entries take 75,431 slots");
    }

    [Fact]
    public void A_lowered_entry_guard_reads_a_table_in_no_larger_window_than_its_room_needs()
    {
        // The window grew until it held the rows' end before any row was read: for a guard of a thousand entries, a window
        // of 16 MB for a table of 300,000 rows. The rows past the thousandth are searched for the trailer a megabyte at a
        // time.
        var file = ChainOfSections(sections: 1, rows: 300_000);
        using var source = new CountingSource(file);
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxIndexEntries = 1000 } };

        using var document = PdfDocument.Open(source, options, ownsSource: false);

        document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.LimitIndexEntries);
        document.Trailer.GetRaw(PdfName.Root).Should().NotBeNull();
        source.LargestRead.Should().BeLessThanOrEqualTo(1024 * 1024);
    }

    [Fact]
    public void A_subsection_claiming_millions_of_rows_makes_room_for_the_rows_its_bytes_hold()
    {
        // A table at the file's start claims three million rows and holds four, then its trailer; the file goes on for 2 MB.
        // Room was made for as many rows as the bytes to the file's end hold: 100,000.
        const string Header = "%PDF-1.7\n";
        const string Catalog = "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n";
        const string Pages = "2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n";
        string Table(int first) => string.Create(
            CultureInfo.InvariantCulture,
            $"xref\n0 3000000\n0000000000 65535 f\r\n{first:D10} 00000 n\r\n{first + Catalog.Length:D10} 00000 n\r\n{first + Catalog.Length + Pages.Length:D10} 00000 n\r\ntrailer\n<< /Size 4 /Root 1 0 R >>\n");
        var objects = Header.Length + Table(0).Length;
        var text = Header + Table(objects) + Catalog + Pages + $"3 0 obj\n({new string('x', 2 * 1024 * 1024)})\nendobj\n"
            + string.Create(CultureInfo.InvariantCulture, $"startxref\n{Header.Length}\n%%EOF\n");

        using var document = PdfDocument.Open(Encoding.Latin1.GetBytes(text));

        document.Catalog.IsOfType(PdfName.Catalog).Should().BeTrue();
        document.Reader.Index.Entries.EnsureCapacity(0).Should().BeLessThan(64, "the table holds four rows");
    }

    /// <summary>The ways <see cref="LargeIndexFile"/> indexes its objects.</summary>
    private static class LargeIndex
    {
        public const string ClassicTable = "a classic table";
        public const string CrossReferenceStream = "a cross-reference stream";
    }

    /// <summary>A catalog, a page tree of one page, and nulls up to object 299,999, indexed the way the shape says.</summary>
    private static byte[] LargeIndexFile(string shape)
    {
        var builder = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .WithObject(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>");

        for (var number = 4; number < 300_000; number++)
        {
            builder.WithObject(number, "null");
        }

        return shape == LargeIndex.ClassicTable
            ? builder.BuildClassic(rootNumber: 1)
            : builder.BuildWithXRefStream(rootNumber: 1, compressXRefStream: true, compressionLevel: CompressionLevel.NoCompression);
    }

    /// <summary>
    /// A catalog, an empty page tree and nulls, indexed by a chain of <paramref name="sections"/> classic sections of
    /// <paramref name="rows"/> rows, each written after the objects it adds, its /Size counting every object up to them
    /// and its /Prev naming the section before it.
    /// </summary>
    private static byte[] ChainOfSections(int sections, int rows)
    {
        var file = new StringBuilder("%PDF-1.7\n");
        var offsets = new long[sections * rows];
        long previous = -1;

        for (var section = 0; section < sections; section++)
        {
            var first = section * rows;

            for (var number = Math.Max(first, 1); number < first + rows; number++)
            {
                offsets[number] = file.Length;
                file.Append(CultureInfo.InvariantCulture, $"{number} 0 obj\n").Append(number switch
                {
                    1 => "<< /Type /Catalog /Pages 2 0 R >>",
                    2 => "<< /Type /Pages /Kids [] /Count 0 >>",
                    _ => "null",
                }).Append("\nendobj\n");
            }

            var at = file.Length;
            file.Append(CultureInfo.InvariantCulture, $"xref\n{first} {rows}\n");

            for (var number = first; number < first + rows; number++)
            {
                file.Append(number == 0 ? "0000000000 65535 f\r\n" : string.Create(CultureInfo.InvariantCulture, $"{offsets[number]:D10} 00000 n\r\n"));
            }

            file.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {first + rows} /Root 1 0 R");
            file.Append(previous < 0 ? string.Empty : string.Create(CultureInfo.InvariantCulture, $" /Prev {previous}")).Append(" >>\n");
            previous = at;
        }

        file.Append(CultureInfo.InvariantCulture, $"startxref\n{previous}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(file.ToString());
    }

    /// <summary>Where the data of the file's object stream starts.</summary>
    private static long ObjectStreamDataStart(byte[] file)
    {
        var text = Encoding.Latin1.GetString(file);
        var dictionary = text.IndexOf("/Type /ObjStm", StringComparison.Ordinal);
        return text.IndexOf("stream\n", dictionary, StringComparison.Ordinal) + "stream\n".Length;
    }

    /// <summary>The catalog, and the page tree and the page packed in object stream 4, its header rewritten.</summary>
    private static byte[] Packed(Func<string, string> header) =>
        new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .WithObject(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>")
            .BuildWithXRefStream(rootNumber: 1, compressedObjects: [2, 3], objectStreamHeader: header);

    private static TestPdfBuilder SampleDocument(string content = "BT (Bonjour) Tj ET") =>
        new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .WithObject(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R >>")
            .Stream(4, string.Empty, content);

    private static PdfDictionary Page(PdfDocument document) =>
        document.Catalog
            .GetDictionary(PdfName.Pages)
            .Required()
            .GetArray(PdfName.Kids)
            .Required()
            .Resolved(0)
            .AsDictionary()
            .Required();

    private static string PageContent(PdfDocument document)
    {
        var stream = Page(document).GetStream(PdfName.Contents).Required();
        return Encoding.ASCII.GetString(stream.Decode(document.Diagnostics).Span);
    }

    [Theory]
    [InlineData("%\n", false, false)]
    [InlineData("t ", true, false)]
    [InlineData("<a", true, false)]
    [InlineData("%\n", false, true)]
    public void The_search_for_the_trailer_past_rows_left_unread_reads_the_file_once(string hit, bool stray, bool full)
    {
        // The search took one comment, or one candidate past a stray byte, a window at a time, and read a megabyte again for
        // each: hours for a file a little past the default bound, opened from a path. A two-megabyte comment before
        // startxref keeps each window a megabyte long.
        var builder = new TestPdfBuilder();

        for (var number = 1; number <= 10; number++)
        {
            builder.WithObject(number, number switch
            {
                1 => "<< /Type /Catalog /Pages 2 0 R >>",
                2 => "<< /Type /Pages /Kids [] /Count 0 >>",
                _ => "null",
            });
        }

        var text = Encoding.Latin1.GetString(builder.BuildClassic(rootNumber: 1));
        var hits = (stray ? "x " : string.Empty) + string.Concat(Enumerable.Repeat(hit, 10_000)) + "\n";
        text = text
            .Replace("trailer\n", hits + "trailer\n", StringComparison.Ordinal)
            .Replace("startxref", "%" + new string('-', 2 * 1024 * 1024) + "\nstartxref", StringComparison.Ordinal);
        var file = Encoding.Latin1.GetBytes(text);
        var limits = full
            ? PdfReaderLimits.Default with { MaxIndexEntries = 4 }
            : PdfReaderLimits.Default with { MaxXRefSectionLength = 64 };
        using var source = new CountingSource(file);

        using var document = PdfDocument.Open(source, new PdfReaderOptions { Limits = limits }, ownsSource: false);

        document.Trailer.GetRaw(PdfName.Root).Should().NotBeNull();
        source.BytesRead.Should().BeLessThan(2L * file.Length);
    }

    private sealed class CountingSource : PdfFileSource
    {
        private readonly PdfFileSource _inner;

        public CountingSource(byte[] data) => _inner = FromMemory(data);

        public long BytesRead { get; private set; }

        public int LargestRead { get; private set; }

        public int Disposals { get; private set; }

        public override long Length => _inner.Length;

        public override int Read(long offset, Span<byte> buffer)
        {
            var read = _inner.Read(offset, buffer);
            BytesRead += read;
            LargestRead = Math.Max(LargestRead, read);
            return read;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Disposals++;
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
