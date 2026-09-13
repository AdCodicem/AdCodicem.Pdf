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

        document.Version.ShouldBe("1.7");
        document.WasRepaired.ShouldBeFalse();
        document.Catalog.ShouldNotBeNull().IsOfType(PdfName.Catalog).ShouldBeTrue();
        PageContent(document).ShouldBe("BT (Bonjour) Tj ET");
    }

    [Fact]
    public void Opens_a_document_indexed_by_a_cross_reference_stream()
    {
        using var document = PdfDocument.Open(SampleDocument().BuildWithXRefStream(rootNumber: 1));

        document.WasRepaired.ShouldBeFalse();
        PageContent(document).ShouldBe("BT (Bonjour) Tj ET");
    }

    [Fact]
    public void Reads_objects_packed_into_an_object_stream()
    {
        var bytes = SampleDocument().BuildWithXRefStream(rootNumber: 1, compressedObjects: [1, 2, 3]);

        using var document = PdfDocument.Open(bytes);

        document.WasRepaired.ShouldBeFalse();
        document.Catalog.ShouldNotBeNull().IsOfType(PdfName.Catalog).ShouldBeTrue();
        Page(document).GetArray(PdfName.MediaBox).ShouldNotBeNull().Count.ShouldBe(4);
        PageContent(document).ShouldBe("BT (Bonjour) Tj ET");
    }

    [Fact]
    public void Finds_objects_whose_recorded_offsets_are_wrong()
    {
        var bytes = SampleDocument().BuildClassic(rootNumber: 1, offsetError: 3);

        using var document = PdfDocument.Open(bytes);

        PageContent(document).ShouldBe("BT (Bonjour) Tj ET");
        document.Diagnostics.Contains(PdfDiagnosticCodes.XRefOffsetAdjusted).ShouldBeTrue();
    }

    [Fact]
    public void Rebuilds_the_index_of_a_file_that_has_none()
    {
        var bytes = SampleDocument().BuildClassic(rootNumber: 1, includeXRef: false);

        using var document = PdfDocument.Open(bytes);

        document.WasRepaired.ShouldBeTrue();
        document.Diagnostics.Contains(PdfDiagnosticCodes.XRefRebuilt).ShouldBeTrue();
        document.Catalog.ShouldNotBeNull().IsOfType(PdfName.Catalog).ShouldBeTrue();
        PageContent(document).ShouldBe("BT (Bonjour) Tj ET");
    }

    [Fact]
    public void Rebuilds_the_index_of_a_file_whose_tail_was_lost()
    {
        var complete = SampleDocument().BuildClassic(rootNumber: 1);
        var truncated = complete.AsSpan(0, complete.Length - 40).ToArray();

        using var document = PdfDocument.Open(truncated);

        document.WasRepaired.ShouldBeTrue();
        document.Catalog.ShouldNotBeNull();
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

        document.WasRepaired.ShouldBeFalse();
        PageContent(document).ShouldBe("BT (Au revoir) Tj ET");
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

        document.Catalog.ShouldNotBeNull();
        document.Diagnostics.Contains(PdfDiagnosticCodes.XRefChainCycle).ShouldBeTrue();
    }

    [Fact]
    public void Reads_a_file_that_starts_with_junk_before_the_header()
    {
        var original = SampleDocument().BuildClassic(rootNumber: 1);
        var prefixed = Encoding.ASCII.GetBytes("garbage bytes from a broken download\n").Concat(original).ToArray();

        using var document = PdfDocument.Open(prefixed);

        document.Catalog.ShouldNotBeNull();
        PageContent(document).ShouldBe("BT (Bonjour) Tj ET");
    }

    [Fact]
    public void Refuses_an_encrypted_document_with_a_typed_exception()
    {
        var builder = SampleDocument().Object(9, "<< /Filter /Standard /V 1 /R 2 /P -1 >>");
        var bytes = builder.BuildClassic(rootNumber: 1);
        var encrypted = Encoding.Latin1.GetString(bytes)
            .Replace("/Root 1 0 R >>", "/Root 1 0 R /Encrypt 9 0 R >>", StringComparison.Ordinal);

        Should.Throw<PdfEncryptedException>(() => PdfDocument.Open(Encoding.Latin1.GetBytes(encrypted)));
    }

    [Fact]
    public void Refuses_an_empty_input()
    {
        Should.Throw<PdfFormatException>(() => PdfDocument.Open(ReadOnlyMemory<byte>.Empty));
    }

    [Fact]
    public void Does_not_read_stream_data_until_it_is_asked_for()
    {
        var payload = new string('A', 500_000);
        var builder = SampleDocument(content: payload);
        var source = new CountingSource(builder.BuildClassic(rootNumber: 1));

        using var document = PdfDocument.Open(source, options: null, ownsSource: false);

        var afterOpen = source.BytesRead;
        afterOpen.ShouldBeLessThan(200_000);

        var stream = Page(document).GetStream(PdfName.Contents).ShouldNotBeNull();
        stream.GetRawBytes().Length.ShouldBe(payload.Length);

        source.BytesRead.ShouldBeGreaterThan(afterOpen + payload.Length - 1);
    }

    private static TestPdfBuilder SampleDocument(string content = "BT (Bonjour) Tj ET") =>
        new TestPdfBuilder()
            .Object(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R >>")
            .Stream(4, string.Empty, content);

    private static PdfDictionary Page(PdfDocument document) =>
        document.Catalog
            .GetDictionary(PdfName.Pages)
            .ShouldNotBeNull()
            .GetArray(PdfName.Kids)
            .ShouldNotBeNull()
            .Resolved(0)
            .AsDictionary()
            .ShouldNotBeNull();

    private static string PageContent(PdfDocument document)
    {
        var stream = Page(document).GetStream(PdfName.Contents).ShouldNotBeNull();
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
        }
    }
}
