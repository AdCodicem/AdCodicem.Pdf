using System.Globalization;
using System.Text;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// How the reader follows the chain of cross-reference sections when a section is not where the chain names it
/// (T25): looked for nearby and read, or reported, never dropped in silence.
/// </summary>
/// <remarks>
/// Each file is a small document saved once more: an update whose section indexes the catalog and a new
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

    /// <summary>Where the first object starts: after the header line and the comment of binary bytes.</summary>
    private const long FirstObjectOffset = 15;

    private enum Offset
    {
        Section,
        FirstObject,
        PastTheEnd,
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
            Offset.FirstObject => FirstObjectOffset,
            Offset.PastTheEnd => 99_999_999L,
            _ => originalSection + prevError,
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
