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
