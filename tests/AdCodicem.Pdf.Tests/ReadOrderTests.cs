using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.Objects;
using AdCodicem.Pdf.Validation;

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

    [Fact]
    public void A_rebuild_run_after_opening_leaves_where_the_index_filled_as_the_document_opened()
    {
        // The table indexes six numbers, under a MaxIndexEntries of 8; the file also defines ten objects the table does not
        // index, 10 to 19. Object 5's row leads nowhere: reading it rebuilds the index, which fills on the ninth number it
        // meets, 15.
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            [3] = file.Object(3, Page),
            [5] = 9,
        };
        var orphans = new Dictionary<int, long>();

        for (var number = 10; number < 20; number++)
        {
            orphans[number] = file.Object(number, "(orphan)");
        }

        file.Table(6, rows, "/Size 6 /Root 1 0 R");
        using var document = PdfDocument.Open(
            file.Bytes,
            new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxIndexEntries = 8 } });

        document.GetObject(new PdfObjectId(5));

        document.WasRepaired.Should().BeTrue();
        document.Reader.IndexFullAt.Should().Be(orphans[15]);
        document.Reader.Structure.IndexFullAt.Should().Be(-1, "the index had room for every number as the document opened");
        new PdfValidator().Validate(document).Findings
            .Where(finding => finding.RuleId == PdfValidationRuleIds.XRefCheckedInPart)
            .Select(finding => finding.Location.Position).Should().Equal(orphans[15]);
    }

    [Fact]
    public void A_rebuild_run_after_opening_leaves_the_catalog_found_as_the_document_opened()
    {
        // /Root names object 1, no catalog: opening takes catalog 20. Later in the file come catalog 30, and 20 again as an
        // annotation. Object 40's row leads nowhere: reading it rebuilds the index, which serves the later 20, so that the
        // reader takes 30.
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Foo >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            [3] = file.Object(3, Page),
            [20] = file.Object(20, "<< /Type /Catalog /Pages 2 0 R >>"),
        };
        rows[6] = file.Padding(6);
        rows[40] = rows[6] + 600;
        file.Object(40, "<< /Title (info) >>");
        rows[30] = file.Object(30, "<< /Type /Catalog /Pages 2 0 R >>");
        file.Object(20, "<< /Type /Annot /Subtype /Text /Rect [0 0 1 1] >>");
        file.Table(41, rows, "/Size 41 /Info 40 0 R /Root 1 0 R");
        using var document = PdfDocument.Open(file.Bytes);

        document.Reader.CatalogFoundAs.Should().Be(20);
        document.GetObject(new PdfObjectId(40));

        document.WasRepaired.Should().BeTrue();
        document.Reader.CatalogFoundAs.Should().Be(30);
        document.Reader.Structure.CatalogFoundAs.Should().Be(20, "the reader took 20 as the document opened");
        new PdfValidator().Validate(document).Findings.Single(finding => finding.RuleId == PdfValidationRuleIds.FileRootInvalid)
            .Message.Should().Be(
                "The trailer's /Root names object 1 0, which is a dictionary of /Type /Foo, not a document catalog. The reader took object 30, which is one, for the catalog.");
    }

    [Fact]
    public void A_root_a_guard_hid_as_the_document_opened_is_not_judged_once_a_rebuild_finds_it()
    {
        // /Root names object 1, which only the oldest of three sections indexes, and MaxXRefSectionCount 2 leaves that section
        // unread: whether the file holds 1 is unknown. The newest section indexes catalog 7, which the reader takes, and gives
        // object 5 a row that leads nowhere: reading it rebuilds the index, which finds 1.
        var file = PdfTemplate.Build(PdfTemplate.Sound + """
            4 0 obj
            (update)
            endobj
            xref
            4 1
            {row:4}
            trailer
            << /Size 5 /Root 1 0 R /Prev {xref:1} >>
            startxref
            {xref:2}
            %%EOF
            7 0 obj
            << /Type /Catalog >>
            endobj
            xref
            7 1
            {row:7}
            5 1
            0000000009 00000 n 
            trailer
            << /Size 8 /Root 1 0 R /Prev {xref:2} >>
            startxref
            {xref:3}
            %%EOF

            """);
        using var document = PdfDocument.Open(
            file,
            new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxXRefSectionCount = 2 } });

        document.Reader.Structure.RootHiddenByGuard.Should().BeTrue();
        document.GetObject(new PdfObjectId(5));

        document.WasRepaired.Should().BeTrue();
        document.GetObject(new PdfObjectId(1)).Should().BeOfType<PdfDictionary>("the rebuilt index holds 1");
        new PdfValidator().Validate(document).Contains(PdfValidationRuleIds.FileRootInvalid)
            .Should().BeFalse("whether the file held 1 was unknown as the document opened, and the trailer judged is that one's");
    }

    [Fact]
    public void An_object_whose_own_load_rebuilt_the_index_is_read_from_the_rebuilt_index_next()
    {
        // Object 4 is a stream whose /Length is object 5, whose row leads nowhere: reading 4 rebuilds the index, as its length
        // is resolved. The rebuilt index serves a later definition of 4, which is no stream.
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R /Test 4 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            [3] = file.Object(3, Page),
            [4] = file.Object(4, "<< /Length 5 0 R >>\nstream\n0123456789\nendstream"),
            [5] = 9,
        };
        rows[6] = file.Padding(6);
        file.Object(5, "10");
        file.Object(4, "<< /Kind (later) >>");
        file.Table(7, rows, "/Size 7 /Root 1 0 R");
        using var document = PdfDocument.Open(file.Bytes);

        document.GetObject(new PdfObjectId(4)).Should().BeOfType<PdfStream>("it was read where the chain's row places it");

        document.WasRepaired.Should().BeTrue();
        document.GetObject(new PdfObjectId(4)).Should().BeOfType<PdfDictionary>()
            .Which.ContainsKey(PdfName.Get("Kind")).Should().BeTrue("the rebuilt index serves the later definition, as it would had it been rebuilt before");
    }

    [Fact]
    public void An_object_stream_whose_own_load_rebuilt_the_index_is_decoded_from_the_rebuilt_index_next()
    {
        // Object stream 6, which holds object 7, has its /Filter in object 5, whose row leads nowhere: reading 7 decodes 6,
        // which rebuilds the index. The rebuilt index serves a later definition of 6, holding another 7, which the rebuild
        // decodes as it takes in what its object streams hold.
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, file.Object(1, "<< /Type /Catalog /Pages 2 0 R /Test 7 0 R >>"), 0),
            [2] = (1, file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"), 0),
            [3] = (1, file.Object(3, Page), 0),
            [6] = (1, file.ObjectStream(6, [(7, "(chain)")], "/Filter 5 0 R "), 0),
            [5] = (1, 9, 0),
            [7] = (2, 6, 0),
        };
        rows[8] = (1, file.Padding(8), 0);
        file.Object(5, "null");
        file.ObjectStream(6, [(7, "(later)")]);
        file.CrossReferenceStream(9, 10, rows, "/Root 1 0 R");
        using var document = PdfDocument.Open(file.Bytes);

        document.GetObject(new PdfObjectId(7)).Should().BeOfType<PdfString>().Which.ToText().Should().Be("chain");

        document.WasRepaired.Should().BeTrue();
        document.GetObject(new PdfObjectId(7)).Should().BeOfType<PdfString>()
            .Which.ToText().Should().Be("later", "the rebuilt index serves the later stream");
    }
}
