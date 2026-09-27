using System.Globalization;
using System.Text;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// How the reader follows the chain of cross-reference sections when a section is not where the chain names it
/// (T25): looked for nearby and read, or reported, never dropped in silence; and what a reference to an object
/// the index lacks reads as (T27): null, as the specification says, unless the index may have lost it.
/// </summary>
/// <remarks>
/// Most files are a small document saved once more: an update whose section indexes the catalog and a new
/// version of object 4, and names through <c>/Prev</c> the original section, which alone indexes the page tree,
/// the page and object 5.
/// </remarks>
public class CrossReferenceChainTests
{
    [Theory]
    [InlineData(-7)]
    [InlineData(-2)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(12)]
    public void Reads_a_section_that_prev_names_a_few_bytes_off(int error)
    {
        // Twelve bytes past the keyword is where IBM's QMF manual points, inside the first row of the table. One
        // byte before it is the end-of-line the keyword follows, where the table is read as it stands.
        var (file, original) = Updated(prevError: error);

        using var document = PdfDocument.Open(file);

        ReadsWhole(document);
        document.WasRepaired.Should().BeFalse();
        var report = document.Diagnostics.Should().ContainSingle().Which;
        report.Code.Should().Be(PdfDiagnosticCodes.XRefOffsetAdjusted);
        report.Severity.Should().Be(PdfDiagnosticSeverity.Repair);
        report.Position.Should().Be(original);
        report.Message.Should().Be(string.Create(
            CultureInfo.InvariantCulture,
            $"The cross-reference section /Prev names at offset {original + error} was found {-error} bytes from there."));
    }

    [Fact]
    public void Reports_a_section_that_prev_names_where_nothing_is_and_finds_its_objects_when_asked()
    {
        // /Prev names the start of the file's first object, more than half a kilobyte from any section. The
        // section is reported at opening; the index is rebuilt only when an object it alone indexed is asked for.
        var (file, _) = Updated(prevAt: Offset.FirstObject);

        using var document = PdfDocument.Open(file);

        document.WasRepaired.Should().BeFalse("nothing asked yet for what the missing section indexed");
        var report = document.Diagnostics.Should().ContainSingle().Which;
        report.Code.Should().Be(PdfDiagnosticCodes.XRefSectionMissing);
        report.Severity.Should().Be(PdfDiagnosticSeverity.Warning);
        report.Position.Should().Be(FirstObjectOffset);
        report.Message.Should().StartWith(string.Create(
            CultureInfo.InvariantCulture,
            $"The cross-reference section /Prev names at offset {FirstObjectOffset} is not there, nor near it"));

        ReadsWhole(document);
        document.WasRepaired.Should().BeTrue();
    }

    [Fact]
    public void Reports_a_section_that_prev_names_past_the_end_of_the_file()
    {
        var (file, _) = Updated(prevAt: Offset.PastTheEnd);

        using var document = PdfDocument.Open(file);

        document.Diagnostics.Select(d => d.Code).Should().Equal(
            PdfDiagnosticCodes.XRefEntryOutOfRange, PdfDiagnosticCodes.XRefSectionMissing);
        ReadsWhole(document);
    }

    [Fact]
    public void Goes_on_through_prev_past_an_xrefstm_that_names_nothing()
    {
        // A hybrid file's stream indexes what its classic table leaves out. When it is missing, that is reported,
        // and the chain still reaches the original section through /Prev: nothing it indexes needs a rebuild.
        var (file, _) = Updated(xrefStm: XRefStm.Nowhere);

        using var document = PdfDocument.Open(file);

        ReadsWhole(document);
        document.WasRepaired.Should().BeFalse();
        var report = document.Diagnostics.Should().ContainSingle().Which;
        report.Code.Should().Be(PdfDiagnosticCodes.XRefSectionMissing);
        report.Message.Should().StartWith(string.Create(
            CultureInfo.InvariantCulture,
            $"The cross-reference section /XRefStm names at offset {FirstObjectOffset} is not there, nor near it"));
    }

    [Theory]
    [InlineData(-4)]
    [InlineData(6)]
    public void Reads_a_cross_reference_stream_that_xrefstm_names_a_few_bytes_off(int error)
    {
        // Object 6 is indexed by the update's cross-reference stream only.
        var (file, stream) = Updated(xrefStm: XRefStm.Present, xrefStmError: error);

        using var document = PdfDocument.Open(file);

        Text(document, 6).Should().Be("only in the stream");
        document.WasRepaired.Should().BeFalse();
        var report = document.Diagnostics.Should().ContainSingle().Which;
        report.Code.Should().Be(PdfDiagnosticCodes.XRefOffsetAdjusted);
        report.Position.Should().Be(stream);
    }

    [Fact]
    public void Reports_a_prev_that_is_not_an_offset_and_finds_what_it_named_when_asked()
    {
        // tiff2pdf writes /Prev 576066 0 R. A reference names an object, not a section: it is not resolved, which
        // would load an object while the index is still being read, and the section is looked for on demand.
        var (file, _) = Updated(prevAt: Offset.AsReference);
        var update = StartXRef(file);

        using var document = PdfDocument.Open(file);

        document.WasRepaired.Should().BeFalse();
        var report = document.Diagnostics.Should().ContainSingle().Which;
        report.Code.Should().Be(PdfDiagnosticCodes.XRefSectionMissing);
        report.Position.Should().Be(update);
        report.Message.Should().StartWith(string.Create(
            CultureInfo.InvariantCulture,
            $"The /Prev of the cross-reference section at offset {update} is not an offset"));

        ReadsWhole(document);
        document.WasRepaired.Should().BeTrue();
    }

    [Theory]
    [InlineData(9)]
    [InlineData(4)]
    public void Reads_a_reference_to_an_object_the_file_does_not_define_as_null_without_rebuilding(int number)
    {
        // Object 9 lies past /Size; object 4 is a free entry of the table. Each is written after the end of the
        // file, where only a rebuild would find it: reading it as null, with nothing to report, shows that the
        // index was taken at its word (T27).
        var written = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .WithObject(3, $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents {number} 0 R >>")
            .WithObject(5, "(the table's last object)")
            .BuildClassic(rootNumber: 1);
        byte[] file = [.. written, .. Encoding.ASCII.GetBytes($"{number} 0 obj\n(outside the index)\nendobj\n")];

        using var document = PdfDocument.Open(file);
        var page = document.Catalog.GetDictionary(PdfName.Pages)!.GetArray(PdfName.Kids)!.Resolved(0).AsDictionary()!;

        page.GetRaw(PdfName.Contents).Resolved().Should().BeSameAs(PdfNull.Instance);
        document.GetObject(new PdfObjectId(number)).Should().BeSameAs(PdfNull.Instance);
        document.WasRepaired.Should().BeFalse();
        document.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void Looks_for_an_object_the_index_lacks_when_a_guard_stopped_the_chain()
    {
        // Two saves on top of the original, and a chain read no further than two sections: the original section,
        // which alone indexes the page tree, is left unread by the reader's own limit, so what it held is looked
        // for by rebuilding the index when asked for, rather than read as null.
        var (original, _) = Updated();
        var file = TestPdfBuilder.AppendIncrementalUpdate(original, rootNumber: 1, [(1, "<< /Type /Catalog /Pages 2 0 R >>")]);
        var options = PdfReaderOptions.Default with { Limits = PdfReaderLimits.Default with { MaxXRefSectionCount = 2 } };

        using var document = PdfDocument.Open(file, options);

        document.WasRepaired.Should().BeFalse();
        document.Diagnostics.Select(d => d.Code).Should().Equal(PdfDiagnosticCodes.LimitXRefSectionCount);
        ReadsWhole(document);
        document.WasRepaired.Should().BeTrue();
    }

    [Fact]
    public void Looks_for_an_object_the_index_lacks_when_a_guard_stopped_the_table()
    {
        // Tables read no further than 128 bytes: the update's, which names the catalog, fits; the original's six
        // rows do not, and the last, object 5's, is cut. Object 5 is looked for by rebuilding the index when
        // asked for, rather than read as null.
        var (file, _) = Updated();
        var options = PdfReaderOptions.Default with { Limits = PdfReaderLimits.Default with { MaxXRefSectionLength = 128 } };

        using var document = PdfDocument.Open(file, options);

        document.WasRepaired.Should().BeFalse();
        document.Diagnostics.Select(d => d.Code).Should().Equal(PdfDiagnosticCodes.LimitXRefSectionLength);
        ReadsWhole(document);
        document.WasRepaired.Should().BeTrue();
    }

    [Fact]
    public void Looks_for_an_object_a_cross_reference_stream_holds_no_row_for()
    {
        // The stream declares six rows, objects 0 to 5, and its data holds five: object 5, written in the file,
        // has no entry the data could give, so it is looked for by rebuilding the index.
        var file = new List<byte>();
        var offsets = new long[6];

        void Append(string text) => file.AddRange(Encoding.Latin1.GetBytes(text));

        Append("%PDF-1.7\n");
        offsets[1] = file.Count;
        Append("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        offsets[2] = file.Count;
        Append("2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n");
        offsets[3] = file.Count;
        Append("3 0 obj\n(three)\nendobj\n");
        offsets[5] = file.Count;
        Append("5 0 obj\n(five)\nendobj\n");
        offsets[4] = file.Count;
        Append("4 0 obj\n<< /Type /XRef /W [1 4 2] /Size 6 /Root 1 0 R /Length 35 >>\nstream\n");

        for (var number = 0; number < 5; number++)
        {
            var offset = offsets[number];
            file.AddRange([(byte)(number == 0 ? 0 : 1), (byte)(offset >> 24), (byte)(offset >> 16), (byte)(offset >> 8), (byte)offset, 0, 0]);
        }

        Append(string.Create(CultureInfo.InvariantCulture, $"\nendstream\nendobj\nstartxref\n{offsets[4]}\n%%EOF\n"));

        using var document = PdfDocument.Open(file.ToArray());

        Text(document, 3).Should().Be("three");
        document.WasRepaired.Should().BeFalse();
        Text(document, 5).Should().Be("five");
        document.WasRepaired.Should().BeTrue();
    }

    /// <summary>Where the first object starts: after the header line and the comment of binary bytes.</summary>
    private const long FirstObjectOffset = 15;

    private enum Offset
    {
        Section,
        FirstObject,
        PastTheEnd,
        AsReference,
    }

    private enum XRefStm
    {
        None,
        Nowhere,
        Present,
    }

    /// <summary>
    /// Builds the document and its update. Returns the file, and where the section the test is about really
    /// starts: the original section, or the update's cross-reference stream.
    /// </summary>
    private static (byte[] File, long Section) Updated(
        int prevError = 0, Offset prevAt = Offset.Section, XRefStm xrefStm = XRefStm.None, int xrefStmError = 0)
    {
        // Filler keeps the original section more than half a kilobyte from the first object.
        var original = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .WithObject(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>")
            .WithObject(4, "(original)")
            .WithObject(5, $"({new string('x', 1024)})")
            .BuildClassic(rootNumber: 1);
        var originalSection = StartXRef(original);
        original.AsSpan().IndexOf("1 0 obj"u8).Should().Be((int)FirstObjectOffset);

        var file = new List<byte>(original);
        var offsets = new SortedDictionary<int, long>();

        void Append(string text) => file.AddRange(Encoding.Latin1.GetBytes(text));

        offsets[1] = file.Count;
        Append("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        offsets[4] = file.Count;
        Append("4 0 obj\n(updated)\nendobj\n");

        var stream = -1L;
        if (xrefStm == XRefStm.Present)
        {
            var sixth = file.Count;
            Append("6 0 obj\n(only in the stream)\nendobj\n");
            stream = file.Count;
            Append("7 0 obj\n<< /Type /XRef /W [1 4 2] /Size 8 /Index [6 1] /Length 7 >>\nstream\n");
            file.AddRange([1, (byte)(sixth >> 24), (byte)(sixth >> 16), (byte)(sixth >> 8), (byte)sixth, 0, 0]);
            Append("\nendstream\nendobj\n");
        }

        var xref = file.Count;
        Append("xref\n");

        foreach (var (number, offset) in offsets)
        {
            Append(string.Create(CultureInfo.InvariantCulture, $"{number} 1\n{offset:D10} 00000 n\r\n"));
        }

        var prev = prevAt switch
        {
            Offset.FirstObject => FirstObjectOffset.ToString(CultureInfo.InvariantCulture),
            Offset.PastTheEnd => "99999999",
            Offset.AsReference => string.Create(CultureInfo.InvariantCulture, $"{originalSection} 0 R"),
            _ => (originalSection + prevError).ToString(CultureInfo.InvariantCulture),
        };
        var hybrid = xrefStm switch
        {
            XRefStm.Nowhere => string.Create(CultureInfo.InvariantCulture, $" /XRefStm {FirstObjectOffset}"),
            XRefStm.Present => string.Create(CultureInfo.InvariantCulture, $" /XRefStm {stream + xrefStmError}"),
            _ => string.Empty,
        };
        Append(string.Create(
            CultureInfo.InvariantCulture,
            $"trailer\n<< /Size 8 /Root 1 0 R /Prev {prev}{hybrid} >>\nstartxref\n{xref}\n%%EOF\n"));

        return ([.. file], xrefStm == XRefStm.Present ? stream : originalSection);
    }

    /// <summary>Checks the objects each section alone indexes: the update's catalog and object 4, the original's page tree.</summary>
    private static void ReadsWhole(PdfDocument document)
    {
        document.Catalog.GetDictionary(PdfName.Pages)!.GetArray(PdfName.Kids)!.Resolved(0).AsDictionary()!
            .IsOfType(PdfName.Page).Should().BeTrue();
        Text(document, 4).Should().Be("updated");
        Text(document, 5).Should().HaveLength(1024);
    }

    private static string Text(PdfDocument document, int number) =>
        document.GetObject(new PdfObjectId(number)).Should().BeOfType<PdfString>().Subject.ToText();

    private static long StartXRef(byte[] file)
    {
        var text = Encoding.Latin1.GetString(file);
        var start = text.LastIndexOf("startxref", StringComparison.Ordinal) + "startxref".Length;
        return long.Parse(text[start..].Trim().Split('\n')[0], CultureInfo.InvariantCulture);
    }
}
