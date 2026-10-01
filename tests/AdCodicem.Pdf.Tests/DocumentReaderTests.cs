using System.Globalization;
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

    [Fact]
    public void Reports_a_loop_in_the_chain_at_the_section_it_loops_back_to_whatever_precedes_the_header()
    {
        // The offset a /Prev gives counts from the header; the report gives a position in the file (#187).
        var original = SampleDocument().BuildClassic(rootNumber: 1);
        var looping = TestPdfBuilder.AppendIncrementalUpdate(
            original,
            rootNumber: 1,
            [(4, "<< /Length 18 >>\nstream\nBT (Bonjour) Tj ET\nendstream")],
            pointPreviousAtSelf: true);
        var junk = Encoding.ASCII.GetBytes(new string('j', 101) + "\n");
        var prefixed = junk.Concat(looping).ToArray();
        var section = Encoding.Latin1.GetString(prefixed).LastIndexOf("\nxref", StringComparison.Ordinal) + 1;

        using var document = PdfDocument.Open(prefixed);

        var cycle = document.Diagnostics.Should().ContainSingle(d => d.Code == PdfDiagnosticCodes.XRefChainCycle).Which;
        cycle.Position.Should().Be(section);
        cycle.Message.Should().EndWith(string.Create(
            CultureInfo.InvariantCulture,
            $"the /Prev of the section at offset {section} names offset {section}, which the chain has already read."));
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
        using var reader = new PdfFileReader(
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

    [Theory]
    [InlineData("a memory stream that shows its buffer")]
    [InlineData("a memory stream that hides its buffer")]
    [InlineData("a stream of another kind")]
    public void Opens_a_document_from_any_stream(string kind)
    {
        var bytes = SampleDocument().BuildClassic(rootNumber: 1);
        using Stream stream = kind switch
        {
            "a memory stream that shows its buffer" => new MemoryStream(bytes, 0, bytes.Length, writable: false, publiclyVisible: true),
            "a memory stream that hides its buffer" => new MemoryStream(bytes),
            _ => new BufferedStream(new MemoryStream(bytes)),
        };

        using var document = PdfDocument.Open(stream);

        PageContent(document).Should().Be("BT (Bonjour) Tj ET");
        document.ObjectCount.Should().Be(5, "objects 1 to 4, and the free head of the table");
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
        using var document = PdfDocument.Open(Packed(header => header[..6] + "x "));

        document.GetObject(new PdfObjectId(2)).AsDictionary().IsOfType(PdfName.Pages).Should().BeTrue();
        document.GetObject(new PdfObjectId(3)).Should().BeSameAs(PdfNull.Instance);
        document.Diagnostics.Should().Contain(entry => entry.Code == PdfDiagnosticCodes.SyntaxUnexpectedToken && entry.Message == "An object stream header is malformed.");
    }

    [Fact]
    public void Reads_an_object_where_its_stream_s_header_lists_it_rather_than_where_the_index_says()
    {
        // The header lists 3 at index 0 and 2 at index 1; the index places 2 at index 0.
        using var document = PdfDocument.Open(Packed(header => "3" + header[1..4] + "2" + header[5..]));

        document.GetObject(new PdfObjectId(2)).AsDictionary().IsOfType(PdfName.Page).Should().BeTrue();
        document.Diagnostics.Should().Contain(entry =>
            entry.Code == PdfDiagnosticCodes.XRefOffsetAdjusted && entry.Message == "Object 2 was at index 1 of its object stream, not 0.");
    }

    [Fact]
    public void An_object_its_stream_s_header_does_not_list_is_null()
    {
        using var document = PdfDocument.Open(Packed(header => header.Replace("3 ", "7 ", StringComparison.Ordinal)));

        document.GetObject(new PdfObjectId(3)).Should().BeSameAs(PdfNull.Instance);
    }

    [Fact]
    public void An_object_stream_whose_count_cannot_be_believed_serves_no_object()
    {
        var bytes = Packed(header => header);
        var text = Encoding.Latin1.GetString(bytes);
        using var document = PdfDocument.Open(Encoding.Latin1.GetBytes(text.Replace("/N 2", "/N 0", StringComparison.Ordinal)));

        document.GetObject(new PdfObjectId(2)).Should().BeSameAs(PdfNull.Instance);
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
    public void An_index_a_rebuild_left_empty_while_the_chain_was_read_is_not_rebuilt_again()
    {
        // The only section is a cross-reference stream whose header a comment splits, which a rebuild's scan does not
        // take for an object header. Its first range declares rows its data lacks, leaving the index incomplete; its
        // second starts at object 9, which the index lacks: resolving it rebuilds the index, and the scan finds
        // nothing. The chain then leaves an empty index, which asks for a rebuild once more — and the one done stands.
        var file = Encoding.ASCII.GetBytes("""
            %PDF-1.5
            7 0 % a comment between the generation and the keyword
            obj
            << /Type /XRef /W [1 2 0] /Index [0 2 9 0 R 1] /Length 0 >>
            stream

            endstream
            endobj
            startxref
            9
            %%EOF

            """);
        var diagnostics = new PdfDiagnostics();

        using var reader = new PdfFileReader(
            PdfFileSource.FromMemory(file),
            diagnostics,
            new PdfLimitGuard(PdfReaderLimits.Default, throwOnLimit: false),
            cacheCapacity: 64,
            ownsSource: true);

        reader.ObjectCount.Should().Be(0);
        reader.WasRepaired.Should().BeTrue();
        reader.Structure.ChainRead.Should().BeFalse();
        diagnostics.Where(diagnostic => diagnostic.Code == PdfDiagnosticCodes.XRefRebuilt).Should().ContainSingle()
            .Which.Message.Should().Be("The cross-reference index was rebuilt by scanning the file.");
    }

    [Fact]
    public void Finds_an_object_whose_entry_points_past_the_end_of_the_file()
    {
        using var document = PdfDocument.Open(PdfTemplate.SoundWith("{row:3}", "0009999999 00000 n "));

        document.GetObject(new PdfObjectId(3)).AsDictionary().IsOfType(PdfName.Page).Should().BeTrue();
        document.WasRepaired.Should().BeTrue();
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

    private sealed class CountingSource : PdfFileSource
    {
        private readonly PdfFileSource _inner;

        public CountingSource(byte[] data) => _inner = FromMemory(data);

        public long BytesRead { get; private set; }

        public override long Length => _inner.Length;

        public override int Read(long offset, Span<byte> buffer)
        {
            var read = _inner.Read(offset, buffer);
            BytesRead += read;
            return read;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
