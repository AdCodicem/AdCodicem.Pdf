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
        var dataEnd = Encoding.Latin1.GetString(file).IndexOf("\nendstream", (int)dataStart, StringComparison.Ordinal) - dataStart;

        using var document = PdfDocument.Open(file);
        _ = document.GetObject(new PdfObjectId(3));

        var report = document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.SyntaxTruncatedObject).Which;
        report.Position.Should().Be(dataStart);
        report.Message.Should().Be(string.Create(
            CultureInfo.InvariantCulture,
            $"The object stream's decoded data ended in the middle of an object. It was met in object 3, at byte {dataEnd} of object stream 4's decoded data."));
    }

    [Fact]
    public void Places_a_length_fault_of_a_stream_written_inside_an_object_stream_where_the_object_stream_s_data_starts()
    {
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(3, "<< /Length 99 >>\nstream\nabc\nendstream")
            .BuildWithXRefStream(rootNumber: 1, compressedObjects: [2, 3]);
        var dataStart = ObjectStreamDataStart(file);

        using var document = PdfDocument.Open(file);
        _ = document.GetObject(new PdfObjectId(3));

        var data = Encoding.Latin1.GetString(file).IndexOf("stream\nabc", (int)dataStart, StringComparison.Ordinal) + "stream\n".Length - dataStart;
        var report = document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.StreamLengthInvalid).Which;
        report.Position.Should().Be(dataStart);
        report.Message.Should().EndWith(string.Create(
            CultureInfo.InvariantCulture,
            $" It was met in object 3, at byte {data} of object stream 4's decoded data."));
    }

    [Theory]
    [InlineData("/First 4", "has no /N that gives a count of objects; none of its objects can be read from it.")]
    [InlineData("/N -1 /First 4", "has no /N that gives a count of objects; none of its objects can be read from it.")]
    [InlineData("/N 1", "has no /First that gives where its objects start; none of its objects can be read from it.")]
    [InlineData("/N 1 /First -4", "has no /First that gives where its objects start; none of its objects can be read from it.")]
    [InlineData("/N 9 /First 4", "declares 9 objects in /N, more than a header of the 4 bytes its /First gives can list; none of its objects can be read from it.")]
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
        // Two object streams list -5, a number no object can have (#157): Swedish writes its minus sign as U+2212.
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(5, "<< /Type /ObjStm /N 1 /First 5 /Length 10 >>\nstream\n-5 0 (one)\nendstream")
            .WithObject(7, "<< /Type /ObjStm /N 1 /First 5 /Length 10 >>\nstream\n-5 0 (two)\nendstream")
            .BuildClassic(rootNumber: 1, includeXRef: false);
        var culture = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("sv-SE");
            using var document = PdfDocument.Open(file);

            document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.ObjectRedefined)
                .Which.Message.Should().Be("Rebuilding the index met a second definition of object -5." + KeptRule);
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
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
