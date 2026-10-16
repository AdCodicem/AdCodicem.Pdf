using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// Holds what the reader notes of a document, and what the validator finds in it, to the document rather than to what was
/// read before: a rebuild of the index changes what the reader serves, and nothing kept from before it may still describe
/// what it served then (#128, #237).
/// </summary>
public class ReadOrderTests
{
    private const string Page = "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << >> >>";

    [Fact]
    public void A_rebuild_forgets_that_an_object_it_no_longer_indexes_lacked_its_endobj()
    {
        // Page 5's header is written "5 0 % lost" then "obj" on the next line, which the parser reads where the row places it
        // and a rebuild's scan does not find; no endobj follows its value. Page 4's row leads nowhere: reading it rebuilds the
        // index, which holds no 5.
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [5 0 R 4 0 R] /Count 2 >>"),
            [5] = file.Position,
        };
        file.Raw("5 0 % lost\nobj\n<< /Type /Page /Parent 2 0 R >>\n(stray)\nendobj\n");
        rows[6] = file.Padding(6);
        rows[4] = rows[6] + 600;
        file.Object(4, Page);
        file.Table(7, rows, "/Size 7 /Root 1 0 R");
        using var document = PdfDocument.Open(file.Bytes);

        document.GetObject(new PdfObjectId(5)).Should().BeOfType<PdfDictionary>();
        document.Reader.IsEndObjMissing(5, out _).Should().BeTrue();
        document.GetObject(new PdfObjectId(4)).Should().BeOfType<PdfDictionary>();

        document.WasRepaired.Should().BeTrue();
        document.GetObject(new PdfObjectId(5)).Should().Be(PdfNull.Instance, "the rebuilt index holds no 5");
        document.Reader.IsEndObjMissing(5, out _).Should().BeFalse("the note was of a definition the rebuilt index no longer holds");
    }

    [Fact]
    public void A_rebuild_forgets_that_a_guard_cut_a_definition_it_replaces()
    {
        // Under a MaxObjectLength of 256 bytes, the chain's outline 5 runs past the guard, which cuts it; a later definition of
        // 5 is short. Object 7's row leads nowhere: reading it rebuilds the index, which serves the later 5, read whole.
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R /Outlines 5 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            [3] = file.Object(3, Page),
            [5] = file.Object(5, "<< /Type /Outlines /Count 0 /Pad (" + new string('y', 400) + ") >>"),
        };
        rows[6] = file.Padding(6);
        rows[7] = rows[6] + 600;
        file.Object(7, "<< /Title (info) >>");
        file.Object(5, "<< /Type /Outlines /Count (zero) >>");
        file.Table(8, rows, "/Size 8 /Info 7 0 R /Root 1 0 R");
        using var document = PdfDocument.Open(
            file.Bytes,
            new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = 256 } });

        document.GetObject(new PdfObjectId(5));
        document.Reader.IsCutAtLimit(5).Should().BeTrue();
        document.GetObject(new PdfObjectId(7)).Should().BeOfType<PdfDictionary>();

        document.WasRepaired.Should().BeTrue();
        document.GetObject(new PdfObjectId(5)).Should().BeOfType<PdfDictionary>()
            .Which.Get(PdfName.Get("Count")).Should().BeOfType<PdfString>("the rebuilt index serves the later 5");
        document.Reader.IsCutAtLimit(5).Should().BeFalse("the guard cut the definition the rebuilt index no longer serves");
    }
}
