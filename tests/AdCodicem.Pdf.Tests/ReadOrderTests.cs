using System.Globalization;
using System.IO.Compression;
using System.Text;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.IO;
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

    /// <summary>
    /// The shape of #237: an update whose table indexes objects 1 to 3, whose trailer names object 4 under <c>/Bar</c> and a
    /// <c>/Prev</c> nothing is at; and an older section, which the chain does not reach, indexing 1 to 4, whose trailer names
    /// object 99, which the file lacks, under <c>/Foo</c>.
    /// </summary>
    private const string UpdateWithoutItsPrevious = """
        %PDF-1.7
        1 0 obj
        << /Type /Catalog /Pages 2 0 R >>
        endobj
        2 0 obj
        << /Type /Pages /Kids [3 0 R] /Count 1 >>
        endobj
        3 0 obj
        << /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << >> >>
        endobj
        4 0 obj
        (four)
        endobj
        xref
        0 5
        {free}
        {row:1}
        {row:2}
        {row:3}
        {row:4}
        trailer
        << /Size 5 /Root 1 0 R /Foo 99 0 R /Info << /Title 5 >> >>
        startxref
        {xref:1}
        %%EOF
        xref
        0 4
        {free}
        {row:1}
        {row:2}
        {row:3}
        trailer
        << /Size 5 /Root 1 0 R /Prev 999999 /Bar 4 0 R >>
        startxref
        {xref:2}
        %%EOF

        """;

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
    public void A_member_whose_load_rebuilt_the_index_is_served_as_a_rebuild_run_first_serves_it()
    {
        // Object 5 is in object streams 7 and 10, and the first one listed wins: 7. Stream 7 takes its /Length from object
        // 8, whose row leads nowhere: reading 5 rebuilds the index while 7 is being decoded, which reads 7 as null and places
        // 5 in 10 meanwhile. Finished once no load is in flight, the index places 5 in 7 again; stream 12, which decodes to
        // more than the reader keeps of object streams, lets 7 go, and 7, decoded again, reads as the file wrote it, not as
        // the null the loads in flight read it as.
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"), 0),
            [2] = (1, file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"), 0),
            [5] = (2, 7, 0),
            [6] = (2, 7, 1),
            [13] = (2, 12, 0),
        };
        const string Seven = "5 0 6 13 (from seven) (six)";
        rows[7] = (1, file.Object(7, $"<< /Type /ObjStm /N 2 /First 9 /Length 8 0 R >>\nstream\n{Seven}\nendstream"), 0);
        rows[9] = (1, file.Padding(9), 0);
        rows[8] = (1, rows[9].Field2 + 600, 0);
        file.Object(8, Seven.Length.ToString(CultureInfo.InvariantCulture));
        rows[10] = (1, file.ObjectStream(10, [(5, "(from ten)")]), 0);
        var twelve = new byte[33 * 1024 * 1024];
        twelve.AsSpan().Fill((byte)' ');
        Encoding.ASCII.GetBytes("13 0 (thirteen)").CopyTo(twelve, 0);
        var data = Encoding.Latin1.GetString(Compress(twelve));
        rows[12] = (1, file.Object(12, $"<< /Type /ObjStm /N 1 /First 5 /Filter /FlateDecode /Length {data.Length} >>\nstream\n{data}\nendstream"), 0);
        file.CrossReferenceStream(14, 15, rows, "/Root 1 0 R");
        using var document = PdfDocument.Open(file.Bytes);

        var five = document.GetObject(new PdfObjectId(5));

        document.WasRepaired.Should().BeTrue();
        five.Should().BeOfType<PdfString>().Which.ToText().Should().Be("from seven");
    }

    [Theory]
    [InlineData("a stream its length names soundly")]
    [InlineData("a stream shorter than its length")]
    [InlineData("an index full past an object stream")]
    [InlineData("a number defined twice")]
    public void A_rebuild_a_read_sets_off_reports_what_a_rebuild_run_first_reports(string shape)
    {
        // The rebuild is set off by a load in flight — 5's row leads to the catalog's header — or run first, by settling.
        // Expanded while the load was in flight, the object streams read 5 as null: they reported a sound stream's /Length
        // as unreadable, which kept that stream's true fault from being reported, and filled the index at another object
        // stream. The rebuild expands them once, when no load is in flight.
        var options = shape == "an index full past an object stream"
            ? PdfReaderOptions.Default with { Limits = PdfReaderLimits.Default with { MaxIndexEntries = 10 } }
            : PdfReaderOptions.Default;
        var file = shape switch
        {
            "a stream its length names soundly" => TwoStreamsOfOneLength("abcdefghij"),
            "a stream shorter than its length" => TwoStreamsOfOneLength("abcdefgh"),
            "a number defined twice" => TwoStreamsOfOneLength("abcdefghij", redefined: true),
            _ => TwoObjectStreamsPastAFullIndex(),
        };
        var read = shape == "an index full past an object stream" ? new[] { 6 } : [4, 6];
        using var first = PdfDocument.Open(file, options);
        using var inFlight = PdfDocument.Open(file, options);

        first.Reader.SettleIndex();

        foreach (var number in read)
        {
            first.GetObject(new PdfObjectId(number));
            inFlight.GetObject(new PdfObjectId(number));
        }

        inFlight.WasRepaired.Should().BeTrue();
        inFlight.Diagnostics.Select(diagnostic => diagnostic.ToString())
            .Should().Equal(first.Diagnostics.Select(diagnostic => diagnostic.ToString()));
    }

    [Fact]
    public void A_rebuild_forgets_that_the_read_which_set_it_off_found_no_endobj()
    {
        // Stream 5's header is written "5 0 % lost" then "obj" on the next line, which the parser reads where the row places it
        // and a rebuild's scan does not find; no endobj follows it. Its /Length is object 4, whose row leads nowhere: reading
        // 5 rebuilds the index while 5 is being loaded, and the load notes the missing endobj once the rebuild has begun. The
        // finished index holds no 5, and nothing is noted of it.
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            [3] = file.Object(3, Page),
            [5] = file.Position,
        };
        file.Raw("5 0 % lost\nobj\n<< /Length 4 0 R >>\nstream\n0123456789\nendstream\n");
        rows[6] = file.Padding(6);
        rows[4] = rows[6] + 600;
        file.Object(4, "10");
        file.Table(7, rows, "/Size 7 /Root 1 0 R");
        using var document = PdfDocument.Open(file.Bytes);

        document.GetObject(new PdfObjectId(5)).Should().Be(PdfNull.Instance, "the rebuilt index holds no 5");

        document.WasRepaired.Should().BeTrue();
        document.Reader.IsEndObjMissing(5, out _).Should().BeFalse("the note was of a definition the rebuilt index no longer holds");
    }

    [Fact]
    public void A_rebuild_forgets_that_the_read_which_set_it_off_found_a_wrong_length()
    {
        // As above, stream 5's /Length, object 4, gives 3 bytes of its 10: the load notes the wrong length once the rebuild
        // has begun. The finished index holds no 5, and nothing is noted of it.
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            [3] = file.Object(3, Page),
            [5] = file.Position,
        };
        file.Raw("5 0 % lost\nobj\n<< /Length 4 0 R >>\nstream\n0123456789\nendstream\nendobj\n");
        rows[6] = file.Padding(6);
        rows[4] = rows[6] + 600;
        file.Object(4, "3");
        file.Table(7, rows, "/Size 7 /Root 1 0 R");
        using var document = PdfDocument.Open(file.Bytes);

        document.GetObject(new PdfObjectId(5)).Should().Be(PdfNull.Instance, "the rebuilt index holds no 5");

        document.WasRepaired.Should().BeTrue();
        document.Reader.TryGetStreamLengthFault(5, out _).Should().BeFalse("the note was of a definition the rebuilt index no longer holds");
    }

    [Fact]
    public void A_rebuild_forgets_that_the_read_which_set_it_off_cut_a_member_at_a_limit()
    {
        // Object stream 6 holds 7 as a string of 300 bytes, past a decoded bound of 128, and takes its /Filter from object 5,
        // whose row leads to the catalog's header; a later 6 holds 7 as "later". Reading 7 rebuilds the index while 6 is being
        // loaded, and the load cuts 7 at the bound once the rebuild has begun. The finished index serves the later 7, which no
        // limit cut.
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"), 0),
            [2] = (1, file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"), 0),
            [3] = (1, file.Object(3, Page), 0),
        };
        var hex = Convert.ToHexString(Encoding.ASCII.GetBytes("7 0 (" + new string('a', 300) + ")")) + ">";
        rows[6] = (1, file.Object(6, string.Create(CultureInfo.InvariantCulture, $"<< /Type /ObjStm /N 1 /First 4 /Filter 5 0 R /Length {hex.Length} >>\nstream\n{hex}\nendstream")), 0);
        rows[7] = (2, 6, 0);
        rows[5] = (1, 9, 0);
        rows[8] = (1, file.Padding(8), 0);
        file.Object(5, "/ASCIIHexDecode");
        file.ObjectStream(6, [(7, "(later)")]);
        file.CrossReferenceStream(9, 10, rows, "/Root 1 0 R");
        using var document = PdfDocument.Open(file.Bytes, PdfReaderOptions.Default with { Limits = PdfReaderLimits.Default with { MaxDecodedStreamLength = 128 } });

        document.GetObject(new PdfObjectId(7)).Should().BeOfType<PdfString>().Which.ToText().Should().Be("later");

        document.WasRepaired.Should().BeTrue();
        document.Reader.IsCutAtLimit(7).Should().BeFalse("the note was of a definition the rebuilt index no longer serves");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_rebuild_forgets_that_a_stream_it_no_longer_indexes_gave_a_wrong_length(bool settling)
    {
        // Stream 5's header is written "5 0 % lost" then "obj" on the next line, which the parser reads where the row places it
        // and a rebuild's scan does not find; its /Length is short of its data. Object 4's row leads nowhere: reading it
        // rebuilds the index once its load is done, settling rebuilds it at once, and the rebuilt index holds no 5.
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            [3] = file.Object(3, Page),
            [5] = file.Position,
        };
        file.Raw("5 0 % lost\nobj\n<< /Length 3 >>\nstream\n0123456789\nendstream\nendobj\n");
        rows[6] = file.Padding(6);
        rows[4] = rows[6] + 600;
        file.Object(4, "(four)");
        file.Table(7, rows, "/Size 7 /Root 1 0 R");
        using var document = PdfDocument.Open(file.Bytes);

        document.GetObject(new PdfObjectId(5)).Should().BeOfType<PdfStream>();
        document.Reader.TryGetStreamLengthFault(5, out _).Should().BeTrue();

        if (settling)
        {
            document.Reader.SettleIndex();
        }
        else
        {
            document.GetObject(new PdfObjectId(4));
        }

        document.WasRepaired.Should().BeTrue();
        document.Reader.TryGetStreamLengthFault(5, out _).Should().BeFalse("the note was of a definition the rebuilt index no longer holds");
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
    public void A_catalog_the_rebuilt_index_no_longer_serves_is_not_named_as_the_one_the_reader_took()
    {
        // /Root names object 1, no catalog: opening takes catalog 20. Later in the file, 20 is defined again as an
        // annotation, and no object is a catalog. Object 40's row leads nowhere: the validator rebuilds the index before its
        // first rule, which serves the later 20, and finds no catalog.
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
        file.Object(20, "<< /Type /Annot /Subtype /Text /Rect [0 0 1 1] >>");
        file.Table(41, rows, "/Size 41 /Info 40 0 R /Root 1 0 R");
        using var document = PdfDocument.Open(file.Bytes);

        document.Reader.CatalogFoundAs.Should().Be(20);
        var report = new PdfValidator().Validate(document);

        document.WasRepaired.Should().BeTrue();
        document.Reader.CatalogFoundAs.Should().Be(0);
        document.Reader.Structure.CatalogFoundAs.Should().Be(20, "the reader took 20 as the document opened");
        report.Findings.Single(finding => finding.RuleId == PdfValidationRuleIds.FileRootInvalid).Message.Should().Be(
            "The trailer's /Root names object 1 0, which is a dictionary of /Type /Foo, not a document catalog. No object of the file is a catalog.");
    }

    [Fact]
    public void A_root_a_guard_hid_as_the_document_opened_is_not_judged_once_a_rebuild_finds_it()
    {
        // /Root names object 1, which only the oldest of three sections indexes, and MaxXRefSectionCount 2 leaves that section
        // unread: whether the file holds 1 is unknown. The newest section indexes catalog 7, which the reader does not take
        // for it, and gives object 5 a row that leads nowhere: reading it rebuilds the index, which finds 1.
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
        document.Reader.CatalogFoundAs.Should().Be(0, "object 7 is another object than the one /Root names");
        document.GetObject(new PdfObjectId(5));

        document.WasRepaired.Should().BeTrue();
        document.GetObject(new PdfObjectId(1)).Should().BeOfType<PdfDictionary>("the rebuilt index holds 1");
        new PdfValidator().Validate(document).Contains(PdfValidationRuleIds.FileRootInvalid)
            .Should().BeFalse("whether the file held 1 was unknown as the document opened, and the trailer judged is that one's");
    }

    [Fact]
    public void An_object_whose_own_load_rebuilt_the_index_is_read_as_the_rebuilt_index_serves_it()
    {
        // Object 4 is a stream whose /Length is object 5, whose row leads nowhere: reading 4 rebuilds the index, as its length
        // is resolved. The rebuilt index serves a later definition of 4, which is no stream: the read that set the rebuild off
        // gives it, as every read after it does, and as a read after validating, which rebuilds first, does.
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

        document.GetObject(new PdfObjectId(4)).Should().BeOfType<PdfDictionary>()
            .Which.ContainsKey(PdfName.Get("Kind")).Should().BeTrue("the rebuild is finished before the read gives what it read");

        document.WasRepaired.Should().BeTrue();
        document.GetObject(new PdfObjectId(4)).Should().BeOfType<PdfDictionary>()
            .Which.ContainsKey(PdfName.Get("Kind")).Should().BeTrue("the rebuilt index serves the later definition");
    }

    [Fact]
    public void An_object_stream_whose_own_load_rebuilt_the_index_is_decoded_as_the_rebuilt_index_serves_it()
    {
        // Object stream 6, which holds object 7, has its /Filter in object 5, whose row leads nowhere: reading 7 decodes 6,
        // which rebuilds the index. The rebuilt index serves a later definition of 6, holding another 7, which the rebuild
        // decodes as it takes in what its object streams hold, once the read that set it off no longer holds 6.
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

        document.GetObject(new PdfObjectId(7)).Should().BeOfType<PdfString>()
            .Which.ToText().Should().Be("later", "the rebuild is finished before the read gives what it read");

        document.WasRepaired.Should().BeTrue();
        document.GetObject(new PdfObjectId(7)).Should().BeOfType<PdfString>()
            .Which.ToText().Should().Be("later", "the rebuilt index serves the later stream");
    }

    [Theory]
    [InlineData("its row")]
    [InlineData("its /Length")]
    public void A_catalog_in_an_object_stream_whose_load_rebuilds_the_index_is_served_from_the_rebuilt_index(string leadsNowhere)
    {
        // Object stream 6 holds catalog 1, which /Root names, and object 4, which nothing reads as the document opens; 6's row
        // lies 700 bytes before its header, or its /Length is object 5, whose row leads nowhere. Opening reads 1, which
        // decodes 6, which rebuilds the index while 6 is being loaded: the rebuild could not take in what 6 holds, and the
        // index lost both 1 and 4 for good. It is finished once the load returns, and takes them in.
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [2] = (1, file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"), 0),
            [3] = (1, file.Object(3, Page), 0),
        };
        rows[8] = (1, file.Padding(8), 0);
        const string Catalog = "<< /Type /Catalog /Pages 2 0 R /Extra 4 0 R >> ";
        var header = string.Create(CultureInfo.InvariantCulture, $"1 0 4 {Catalog.Length} ");
        var data = header + Catalog + "(four) ";
        var length = leadsNowhere == "its row" ? data.Length.ToString(CultureInfo.InvariantCulture) : "5 0 R";
        var at = file.Object(
            6,
            string.Create(CultureInfo.InvariantCulture, $"<< /Type /ObjStm /N 2 /First {header.Length} /Length {length} >>\nstream\n{data}\nendstream"));
        rows[6] = (1, leadsNowhere == "its row" ? at - 700 : at, 0);
        rows[1] = (2, 6, 0);
        rows[4] = (2, 6, 1);

        if (leadsNowhere != "its row")
        {
            rows[5] = (1, 9, 0);
            file.Object(5, data.Length.ToString(CultureInfo.InvariantCulture));
        }

        file.CrossReferenceStream(9, 10, rows, "/Root 1 0 R");
        using var document = PdfDocument.Open(file.Bytes);

        document.WasRepaired.Should().BeTrue();
        document.Catalog.Should().NotBeNull();
        document.ObjectNumbers.Should().Contain([1, 4]);
        document.GetObject(new PdfObjectId(1)).Should().BeOfType<PdfDictionary>().Which.IsOfType(PdfName.Catalog).Should().BeTrue();
        document.GetObject(new PdfObjectId(4)).Should().BeOfType<PdfString>().Which.ToText().Should().Be("four");
    }

    [Theory]
    [InlineData("/Length")]
    [InlineData("/Filter")]
    public void A_member_whose_load_rebuilds_the_index_is_read_alike_first_and_after_settling(string key)
    {
        // Object stream 6 holds object 7; its /Length or its /Filter is object 5, whose row leads nowhere. Reading 7 decodes 6,
        // which rebuilds the index while 6, and 5, are being loaded: 7 was read once, then lost to every later read. It is
        // read as the index rebuilt before any read serves it, from the first read on.
        byte[] Build()
        {
            var file = new HandWrittenFile(version: "1.5");
            var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
            {
                [1] = (1, file.Object(1, "<< /Type /Catalog /Pages 2 0 R /Test 7 0 R >>"), 0),
                [2] = (1, file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"), 0),
                [3] = (1, file.Object(3, Page), 0),
            };
            const string Data = "7 0 (member) ";
            var hex = Convert.ToHexString(Encoding.Latin1.GetBytes(Data)) + ">";
            rows[6] = (1, file.Object(
                6,
                key == "/Length"
                    ? "<< /Type /ObjStm /N 1 /First 4 /Length 5 0 R >>\nstream\n" + Data + "\nendstream"
                    : string.Create(CultureInfo.InvariantCulture, $"<< /Type /ObjStm /N 1 /First 4 /Filter 5 0 R /Length {hex.Length} >>\nstream\n{hex}\nendstream")), 0);
            rows[5] = (1, 9, 0);
            rows[7] = (2, 6, 0);
            rows[8] = (1, file.Padding(8), 0);
            file.Object(5, key == "/Length" ? Data.Length.ToString(CultureInfo.InvariantCulture) : "/ASCIIHexDecode");
            file.CrossReferenceStream(9, 10, rows, "/Root 1 0 R");
            return file.Bytes;
        }

        using var settled = PdfDocument.Open(Build());
        settled.Reader.SettleIndex();
        using var document = PdfDocument.Open(Build());

        document.GetObject(new PdfObjectId(7)).Should().BeOfType<PdfString>().Which.ToText().Should().Be("member");
        document.GetObject(new PdfObjectId(7)).Should().BeOfType<PdfString>().Which.ToText().Should().Be("member");
        document.ObjectNumbers.Should().Equal(settled.ObjectNumbers);
        new PdfValidator().Validate(document).Findings.Should().Equal(new PdfValidator().Validate(settled).Findings);
    }

    [Theory]
    [InlineData(1, "nothing")]
    [InlineData(2, "a read")]
    [InlineData(2, "a validation")]
    [InlineData(2, "settling")]
    public void A_rebuild_a_failed_load_set_off_is_finished_before_its_exception_leaves_or_by_what_comes_next(int failures, string next)
    {
        // Object stream 6 holds object 7, and its /Length is object 5, whose row leads nowhere: reading 7 rebuilds the index
        // while 6 is being loaded, then reads 5 where the rebuild found it, which the source fails to give. The read that
        // failed finishes the rebuild before its exception leaves; when the source fails that too, what comes next — a read,
        // a validation, settling — finishes it first: the index serves 7 before 7 is read again.
        var (bytes, five) = MemberWhoseLengthLeadsNowhere();
        using var source = new FailingSource(bytes);
        using var document = PdfDocument.Open(source, options: null, ownsSource: false);
        source.Fail(five, [.. Enumerable.Range(1, failures)]);

        FluentActions.Invoking(() => document.GetObject(new PdfObjectId(7))).Should().Throw<IOException>();

        switch (next)
        {
            case "a read":
                document.GetObject(new PdfObjectId(1)).Should().BeOfType<PdfDictionary>();
                break;

            case "a validation":
                new PdfValidator().Validate(document).Findings.Select(finding => finding.ToString())
                    .Should().Equal(Findings(PdfDocument.Open(bytes), validations: 1), "the rebuild is finished before the first rule");
                break;

            case "settling":
                document.Reader.SettleIndex();
                break;
        }

        document.ObjectNumbers.Should().Contain(7);
        document.GetObject(new PdfObjectId(7)).Should().BeOfType<PdfString>().Which.ToText().Should().Be("member");
    }

    [Fact]
    public void A_rebuild_whose_finish_the_source_fails_is_finished_whole_by_the_next_read()
    {
        // Object stream 6 holds objects 10 and 11; object stream 8 holds 7, and its /Length is object 5, whose row leads
        // nowhere. Reading 7 rebuilds the index while 8 is being loaded; finishing it expands 6, then fails to read 5 for 8.
        // The next read finishes it again from the scan: 6's members are not met a second time, as redefinitions.
        var (bytes, five) = TwoObjectStreamsTheSecondOfWhichLeadsNowhere(unindexed: 0);
        using var source = new FailingSource(bytes);
        using var document = PdfDocument.Open(source, options: null, ownsSource: false);
        source.Fail(five, 2);

        FluentActions.Invoking(() => document.GetObject(new PdfObjectId(7))).Should().Throw<IOException>();
        var seven = document.GetObject(new PdfObjectId(7));

        seven.Should().BeOfType<PdfString>().Which.ToText().Should().Be("member");
        document.ObjectNumbers.Should().Contain([7, 10, 11]);
        document.Diagnostics.Contains(PdfDiagnosticCodes.ObjectRedefined).Should().BeFalse();
    }

    [Fact]
    public void A_rebuild_whose_finish_the_source_fails_throws_the_guard_the_next_finish_reaches()
    {
        // As above, with an index of thirteen numbers, which the scan fills to the brim with five objects written outside it,
        // opened to throw on a limit: the first finish fills the index as it expands 6, then fails; the next, which a read of
        // the catalog sets off, reaches the index's guard again, and throws it.
        var (bytes, five) = TwoObjectStreamsTheSecondOfWhichLeadsNowhere(unindexed: 5);
        using var source = new FailingSource(bytes);
        var options = PdfReaderOptions.Default with { ThrowOnLimit = true, Limits = PdfReaderLimits.Default with { MaxIndexEntries = 13 } };
        using var document = PdfDocument.Open(source, options, ownsSource: false);
        source.Fail(five, 2);

        FluentActions.Invoking(() => document.GetObject(new PdfObjectId(7))).Should().Throw<IOException>();

        FluentActions.Invoking(() => document.GetObject(new PdfObjectId(1))).Should().Throw<PdfLimitExceededException>()
            .Which.LimitName.Should().Be(nameof(PdfReaderLimits.MaxIndexEntries));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    public void A_guard_a_read_reaches_after_setting_off_a_rebuild_leaves_the_next_read_free(int first)
    {
        // Stream 4 takes its /Length from object 5, whose row leads to the catalog's header, and 5 runs past the object guard
        // of a document opened to throw on one. Reading 4 rebuilds the index while 5 is being loaded, and 5 throws there,
        // a load deep; reading 5 first throws a load deep too. Either way the rebuild is finished before the exception
        // leaves: the next read, of the catalog, throws nothing.
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            [3] = file.Object(3, Page),
            [4] = file.Object(4, "<< /Length 5 0 R >>\nstream\n0123456789\nendstream"),
            [5] = 9,
        };
        rows[6] = file.Padding(6, 600);
        rows[7] = file.Padding(7, 600);
        file.Object(5, "10" + new string(' ', 2000));
        file.Table(8, rows, "/Size 8 /Root 1 0 R");
        var options = PdfReaderOptions.Default with { ThrowOnLimit = true, Limits = PdfReaderLimits.Default with { MaxObjectLength = 1024 } };
        using var document = PdfDocument.Open(file.Bytes, options);

        FluentActions.Invoking(() => document.GetObject(new PdfObjectId(first))).Should().Throw<PdfLimitExceededException>()
            .Which.LimitName.Should().Be(nameof(PdfReaderLimits.MaxObjectLength));

        document.WasRepaired.Should().BeTrue();
        document.GetObject(new PdfObjectId(1)).Should().BeOfType<PdfDictionary>();
    }

    [Theory]
    [InlineData("nothing")]
    [InlineData("a validation")]
    [InlineData("the object the update's trailer names")]
    public void Validation_finds_the_same_whatever_was_read_before_it(string before)
    {
        // #237: the update's trailer names a /Prev nothing is at, which leaves the index incomplete; the older trailer, which
        // only a rebuild reads, adds /Foo, naming an object the file lacks. Reading object 4 under the update's /Bar rebuilt
        // the index, which merged /Foo into the trailer: a validation that read it first found /Foo, one whose walk read it
        // after taking the trailer's entries did not.
        var file = PdfTemplate.Build(UpdateWithoutItsPrevious);
        var expected = Findings(PdfDocument.Open(file), validations: 2);
        using var document = PdfDocument.Open(file);

        if (before == "a validation")
        {
            new PdfValidator().Validate(document);
        }
        else if (before != "nothing")
        {
            document.GetObject(new PdfObjectId(4)).Should().BeOfType<PdfString>();
        }

        new PdfValidator().Validate(document).Findings.Select(finding => finding.ToString()).Should().Equal(expected);
        expected.Should().Contain(
            "Warning object.reference-missing at offset 510: The trailer refers to object 99 0 under /Foo, which the file lacks: the reference reads as null.");
    }

    [Theory]
    [InlineData("inside another object")]
    [InlineData("outside the file")]
    public void An_entry_whose_object_a_read_would_not_find_has_the_index_rebuilt_before_the_first_rule(string where)
    {
        // Object 4's row places it in the middle of object 6, more than the neighborhood a read searches away from its header,
        // or past the file's end; nothing refers to it. Reading it would rebuild the index: the validator does so first.
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            [3] = file.Object(3, Page),
        };
        rows[6] = file.Padding(6);
        rows[4] = where == "outside the file" ? 99999 : rows[6] + 600;
        file.Object(4, "(four)");
        file.Table(7, rows, "/Size 7 /Root 1 0 R");
        using var document = PdfDocument.Open(file.Bytes);

        document.WasRepaired.Should().BeFalse();
        var report = new PdfValidator().Validate(document);

        document.WasRepaired.Should().BeTrue();
        var finding = report.Findings.Should().ContainSingle("the rules judge the row as the file wrote it").Which;
        finding.RuleId.Should().Be(PdfValidationRuleIds.XRefEntryBroken);
        finding.Message.Should().Be(where == "outside the file"
            ? "The entry of object 4 gives offset 99999, outside the file."
            : string.Create(CultureInfo.InvariantCulture, $"The entry of object 4 gives offset {rows[4]}, where there is no object header, and the object is not within 512 bytes of it."));
        new PdfValidator().Validate(document).Findings.Should().Equal(report.Findings);
    }

    [Theory]
    [InlineData("a few bytes off")]
    [InlineData("cut by the object guard")]
    [InlineData("after a long comment")]
    [InlineData("after a comment longer than the object guard")]
    public void An_object_a_read_finds_sets_off_no_rebuild_before_the_first_rule(string shape)
    {
        // A read finds object 4 where its row places it, or near: a few bytes off; past the 256 bytes MaxObjectLength lets it
        // read, which cuts its body, not its header; after a comment longer than the first bytes the validator reads of a
        // header; or after one longer than the guard, which the neighborhood search passes over.
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R /Test 4 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            [3] = file.Object(3, Page),
            [4] = file.Position,
        };

        switch (shape)
        {
            case "a few bytes off":
                rows[4] += 3;
                file.Object(4, "(four)");
                break;
            case "cut by the object guard":
                file.Object(4, "(" + new string('y', 400) + ")");
                break;
            default:
                file.Raw("%" + new string('c', shape == "after a long comment" ? 100 : 300) + "\n");
                file.Object(4, "(four)");
                break;
        }

        file.Table(5, rows, "/Size 5 /Root 1 0 R");
        using var document = PdfDocument.Open(
            file.Bytes,
            new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = 256 } });

        new PdfValidator().Validate(document);

        document.WasRepaired.Should().BeFalse();
        document.GetObject(new PdfObjectId(4)).Should().BeOfType<PdfString>();
        document.WasRepaired.Should().BeFalse("a read finds what the validator found a read finds");
    }

    [Theory]
    [InlineData("an obj keyword the first bytes read cut, which runs on", "                                                         4 0 objx\n(not an object)\n")]
    [InlineData("a generation past 65535", "4 70000 obj\n(not an object)\nendobj\n")]
    [InlineData("a negative generation", "4 -1 obj\n(not an object)\nendobj\n")]
    [InlineData("object 0", "0 0 obj\n(not an object)\nendobj\n")]
    [InlineData("a number past the largest", "99999999999 0 obj\n(not an object)\nendobj\n")]
    [InlineData("a keyword that runs on, which the search near the row takes for a header", "4 0 objection\n(not an object)\n")]
    public void A_header_a_read_refuses_has_the_index_rebuilt_by_settling(string refused, string written)
    {
        // Object 4's row places it at bytes a read does not take for its header, and nothing near them is one: a read of 4
        // would rebuild the index, which finds the later 4. Settling does so first.
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            [3] = file.Object(3, Page),
            [4] = file.Position,
        };
        file.Raw(written);
        rows[6] = file.Padding(6);
        file.Object(4, "(four)");
        file.Table(7, rows, "/Size 7 /Root 1 0 R");
        using var document = PdfDocument.Open(file.Bytes);

        document.Reader.SettleIndex();

        document.WasRepaired.Should().BeTrue($"a read refuses {refused}");
        document.GetObject(new PdfObjectId(4)).Should().BeOfType<PdfString>().Which.ToText().Should().Be("four");
    }

    [Fact]
    public void A_header_after_a_comment_longer_than_the_neighborhood_sets_off_no_rebuild()
    {
        // Object 4's row places it at a comment of 2,000 bytes, its header after it: a read finds it there, though nothing near
        // the row is a header, and so does settling, which reads past the comment as a read does.
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            [3] = file.Object(3, Page),
            [6] = file.Padding(6),
            [4] = file.Position,
        };
        file.Raw("%" + new string('c', 1999) + "\n");
        file.Object(4, "(four)");
        file.Table(7, rows, "/Size 7 /Root 1 0 R");
        using var document = PdfDocument.Open(file.Bytes);

        document.Reader.SettleIndex();

        document.WasRepaired.Should().BeFalse();
        document.GetObject(new PdfObjectId(4)).Should().BeOfType<PdfString>().Which.ToText().Should().Be("four");
        document.WasRepaired.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_header_past_the_object_guard_is_settled_as_a_read_takes_it(bool throwOnLimit)
    {
        // Object 4's row places it at a comment of 2,000 bytes, its header after it, under a MaxObjectLength of 1,024 bytes,
        // and nothing near the row is a header. A read there reports the guard and rebuilds the index; opened to throw on a
        // guard, it throws, and rebuilds nothing. Settling does what the read would, short of throwing.
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            [3] = file.Object(3, Page),
            [6] = file.Padding(6),
            [4] = file.Position,
        };
        file.Raw("%" + new string('c', 1999) + "\n");
        file.Object(4, "(four)");
        file.Table(7, rows, "/Size 7 /Root 1 0 R");
        using var document = PdfDocument.Open(
            file.Bytes,
            new PdfReaderOptions { ThrowOnLimit = throwOnLimit, Limits = PdfReaderLimits.Default with { MaxObjectLength = 1024 } });

        document.Reader.SettleIndex();

        if (throwOnLimit)
        {
            document.WasRepaired.Should().BeFalse("a read throws there, and rebuilds nothing");
            FluentActions.Invoking(() => document.GetObject(new PdfObjectId(4))).Should().Throw<PdfLimitExceededException>()
                .Which.LimitName.Should().Be(nameof(PdfReaderLimits.MaxObjectLength));
        }
        else
        {
            document.WasRepaired.Should().BeTrue();
            document.Diagnostics.Where(entry => entry.Code == PdfDiagnosticCodes.LimitObject && entry.Position == rows[4])
                .Select(entry => entry.ToString()).Should().Equal(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Warning limit.object at {rows[4]}: Object 4 runs past 1 KB, its stream data aside; only what lies within it was read. Raise PdfReaderLimits.MaxObjectLength to read past it."));
            document.GetObject(new PdfObjectId(4)).Should().BeOfType<PdfString>().Which.ToText().Should().Be("four");
        }
    }

    [Fact]
    public void A_header_whose_keyword_ends_at_the_object_guard_is_one_a_read_finds()
    {
        // Object 4's row places it at a comment, its header after it, whose obj ends at the 1,024th byte, the object guard: a
        // read takes the header, whole, and reaches the guard in the object's body, rebuilding nothing. Settling takes it too.
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            [3] = file.Object(3, Page),
            [6] = file.Padding(6),
            [4] = file.Position,
        };
        file.Raw("%" + new string('c', 1015) + "\n");
        file.Object(4, "(four)");
        file.Table(7, rows, "/Size 7 /Root 1 0 R");
        using var document = PdfDocument.Open(file.Bytes, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = 1024 } });

        document.Reader.SettleIndex();

        document.WasRepaired.Should().BeFalse("a read finds the header there");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_header_found_near_its_row_past_the_object_guard_is_settled_as_a_read_takes_it(bool throwOnLimit)
    {
        // Object 4's row places it inside object 6's array; 30 bytes on, 4's header runs on past the 256 bytes of the object
        // guard, 400 blanks between its generation and obj. A read finds it there and reaches the guard: it reports it, finds
        // no object, and rebuilds the index; opened to throw on a guard, it throws, and rebuilds nothing. Settling does what
        // the read would, short of throwing.
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            [3] = file.Object(3, Page),
        };
        rows[6] = file.Object(6, "[1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20]\n%\n4 0" + new string(' ', 400) + "obj (four) endobj");
        rows[4] = rows[6] + 12;
        file.Padding(7);
        file.Object(4, "(four)");
        file.Table(7, rows, "/Size 7 /Root 1 0 R");
        using var document = PdfDocument.Open(
            file.Bytes,
            new PdfReaderOptions { ThrowOnLimit = throwOnLimit, Limits = PdfReaderLimits.Default with { MaxObjectLength = 256 } });

        document.Reader.SettleIndex();

        if (throwOnLimit)
        {
            document.WasRepaired.Should().BeFalse("a read throws there, and rebuilds nothing");
            FluentActions.Invoking(() => document.GetObject(new PdfObjectId(4))).Should().Throw<PdfLimitExceededException>()
                .Which.LimitName.Should().Be(nameof(PdfReaderLimits.MaxObjectLength));
        }
        else
        {
            document.WasRepaired.Should().BeTrue();
            document.GetObject(new PdfObjectId(4)).Should().BeOfType<PdfString>().Which.ToText().Should().Be("four");
        }
    }

    [Fact]
    public void A_comment_ending_the_file_where_a_row_leads_is_no_header_the_object_guard_cut()
    {
        // Object 4's row places it at a comment of 128 bytes that ends the file, the object guard's size; 4 is written more
        // than 512 bytes earlier, before six small objects. The window that reads the comment ends where the file does, which
        // cuts nothing: a read finds no header there, nor near, and rebuilds the index, though it was opened to throw on a
        // guard. Settling does so.
        static HandWrittenFile Write(long row, out Dictionary<int, long> rows)
        {
            var file = new HandWrittenFile();
            rows = new Dictionary<int, long>
            {
                [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
                [2] = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
                [3] = file.Object(3, Page),
            };
            file.Object(4, "(four)");

            for (var number = 6; number < 12; number++)
            {
                file.Padding(number, 100);
            }

            rows[4] = row;
            file.Table(7, rows, "/Size 7 /Root 1 0 R");
            return file;
        }

        var row = Write(0, out _).Position;
        var file = Write(row, out _);
        file.Raw("% " + new string('c', 125) + "\n");
        var bytes = file.Bytes;
        bytes.Length.Should().Be((int)row + 128);
        using var document = PdfDocument.Open(bytes, new PdfReaderOptions { ThrowOnLimit = true, Limits = PdfReaderLimits.Default with { MaxObjectLength = 128 } });

        document.Reader.SettleIndex();

        document.WasRepaired.Should().BeTrue();
        document.GetObject(new PdfObjectId(4)).Should().BeOfType<PdfString>().Which.ToText().Should().Be("four");
    }

    [Fact]
    public void A_guard_the_rebuild_before_the_first_rule_reaches_throws_in_the_first_validation_only()
    {
        // Object 5's row leads nowhere, and the file defines ten objects the table does not index: the rebuild the validator
        // runs first fills the index under a MaxIndexEntries of 8, and a document opened to throw on a guard throws, its index
        // whole. The next validation reads it so.
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            [3] = file.Object(3, Page),
            [5] = 9,
        };

        for (var number = 10; number < 20; number++)
        {
            file.Object(number, "(orphan)");
        }

        file.Table(6, rows, "/Size 6 /Root 1 0 R");
        using var document = PdfDocument.Open(
            file.Bytes,
            new PdfReaderOptions { ThrowOnLimit = true, Limits = PdfReaderLimits.Default with { MaxIndexEntries = 8 } });
        var validator = new PdfValidator();

        FluentActions.Invoking(() => validator.Validate(document)).Should().Throw<PdfLimitExceededException>()
            .Which.LimitName.Should().Be(nameof(PdfReaderLimits.MaxIndexEntries));

        document.WasRepaired.Should().BeTrue();
        validator.Validate(document).Contains(PdfValidationRuleIds.XRefCheckedInPart).Should().BeTrue();
    }

    [Fact]
    public void Settling_a_sound_index_allocates_nothing_an_entry()
    {
        // Settling built the message of the object guard for every entry, about 230 bytes each, before it knew whether the
        // guard was reached (#175): what it allocates is the same for a thousand entries as for four thousand.
        static long Settling(int padding)
        {
            var file = new HandWrittenFile();
            var rows = new Dictionary<int, long>
            {
                [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
                [2] = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
                [3] = file.Object(3, Page),
            };

            for (var number = 4; number < padding + 4; number++)
            {
                rows[number] = file.Padding(number, 10);
            }

            file.Table(padding + 4, rows, string.Create(CultureInfo.InvariantCulture, $"/Size {padding + 4} /Root 1 0 R"));
            var bytes = file.Bytes;

            return Allocations.Least(
                () => PdfDocument.Open(bytes),
                document =>
                {
                    document.Reader.SettleIndex();
                    return document.WasRepaired;
                },
                out var settled,
                out var repaired) + (repaired ? 1 : 0) + Dispose(settled);
        }

        static long Dispose(PdfDocument document)
        {
            document.Dispose();
            return 0;
        }

        var thousand = Settling(1_000);
        var fourThousand = Settling(4_000);

        thousand.Should().BeLessThan(512);
        fourThousand.Should().Be(thousand);
    }

    [Fact]
    public void Settling_the_index_of_a_disposed_document_is_refused()
    {
        var document = PdfDocument.Open(PdfTemplate.Build(PdfTemplate.Sound));
        var reader = document.Reader;
        document.Dispose();

        FluentActions.Invoking(reader.SettleIndex).Should().Throw<ObjectDisposedException>();
    }

    [Theory]
    [InlineData("/Bad /A#00B", "/Good 1", "Object 5 holds a name with a null character, which a name cannot contain.")]
    [InlineData("/Ref 99 0 R", "/Ref 3 0 R", "Object 5 refers to an object the file lacks: the reference reads as null.")]
    [InlineData("/Bad /A#00B /Worse /C#00D", "/Good 1", "Object 5 holds 2 names with a null character, which a name cannot contain.")]
    [InlineData("/Ref 99 0 R /Ref2 98 0 R", "/Ref 3 0 R", "Object 5 holds 2 references to objects the file lacks, which read as null.")]
    public void The_object_graph_says_what_it_counted_of_an_object_a_rebuild_redefined_since(string fault, string later, string message)
    {
        // Object 5 holds a fault and object 7, whose row leads nowhere; a later definition of 5 holds no fault. Walked without
        // the validator settling the index first, following 7 rebuilds it while 5 is being walked, and reading 5 again to
        // describe it reads the later 5, which holds no fault: the finding says what the walk counted, without where — not a
        // reference to "object 0 0", nor a crash on the name.
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R /Test 5 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            [3] = file.Object(3, Page),
            [5] = file.Object(5, "<< " + fault + " /Info 7 0 R >>"),
        };
        rows[6] = file.Padding(6);
        rows[7] = rows[6] + 600;
        file.Object(7, "<< /Title (info) >>");
        file.Object(5, "<< " + later + " >>");
        file.Table(8, rows, "/Size 8 /Root 1 0 R");
        using var document = PdfDocument.Open(file.Bytes);

        var graph = ObjectGraph.Run(document, PageTreeWalk.Run(document));

        document.WasRepaired.Should().BeTrue();
        graph.MissingReferences.Concat(graph.NullCharacterNames).Should().ContainSingle().Which.Message.Should().Be(message);
    }

    /// <summary>A file read from memory, some of whose reads starting at one offset fail with an I/O error.</summary>
    private sealed class FailingSource(byte[] data) : PdfFileSource
    {
        private readonly PdfFileSource _inner = FromMemory(data);
        private readonly HashSet<int> _failing = [];
        private long _at = -1;
        private int _reads;

        /// <inheritdoc/>
        public override long Length => _inner.Length;

        /// <summary>Makes the reads starting at <paramref name="at"/> whose ordinals <paramref name="reads"/> gives, from 1, fail.</summary>
        public void Fail(long at, params int[] reads)
        {
            _at = at;
            _reads = 0;
            _failing.Clear();
            _failing.UnionWith(reads);
        }

        /// <inheritdoc/>
        public override int Read(long offset, Span<byte> buffer)
        {
            if (offset == _at && _failing.Contains(++_reads))
            {
                throw new IOException("The source failed to give the bytes asked for.");
            }

            return _inner.Read(offset, buffer);
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// A file whose catalog names object 7, which object stream 6 holds; 6's /Length is object 5, whose row leads to the
    /// catalog's header. Gives the file and where 5 is.
    /// </summary>
    private static (byte[] Bytes, long Five) MemberWhoseLengthLeadsNowhere()
    {
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, file.Object(1, "<< /Type /Catalog /Pages 2 0 R /Test 7 0 R >>"), 0),
            [2] = (1, file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"), 0),
            [3] = (1, file.Object(3, Page), 0),
        };
        const string Data = "7 0 (member) ";
        rows[6] = (1, file.Object(6, "<< /Type /ObjStm /N 1 /First 4 /Length 5 0 R >>\nstream\n" + Data + "\nendstream"), 0);
        rows[5] = (1, 9, 0);
        rows[7] = (2, 6, 0);
        rows[8] = (1, file.Padding(8), 0);
        var five = file.Object(5, Data.Length.ToString(CultureInfo.InvariantCulture));
        file.CrossReferenceStream(9, 10, rows, "/Root 1 0 R");
        return (file.Bytes, five);
    }

    /// <summary>
    /// A file whose catalog names object 7, which object stream 8 holds, after object stream 6, which holds 10 and 11; 8's
    /// /Length is object 5, whose row leads to the catalog's header; <paramref name="unindexed"/> objects are written with no
    /// row. Gives the file and where 5 is.
    /// </summary>
    private static (byte[] Bytes, long Five) TwoObjectStreamsTheSecondOfWhichLeadsNowhere(int unindexed)
    {
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, file.Object(1, "<< /Type /Catalog /Pages 2 0 R /Test 7 0 R >>"), 0),
            [2] = (1, file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"), 0),
            [3] = (1, file.Object(3, Page), 0),
            [6] = (1, file.ObjectStream(6, [(10, "(ten)"), (11, "(eleven)")]), 0),
            [10] = (2, 6, 0),
            [11] = (2, 6, 1),
        };
        const string Data = "7 0 (member) ";
        rows[8] = (1, file.Object(8, "<< /Type /ObjStm /N 1 /First 4 /Length 5 0 R >>\nstream\n" + Data + "\nendstream"), 0);
        rows[7] = (2, 8, 0);
        rows[5] = (1, 9, 0);
        rows[9] = (1, file.Padding(9), 0);
        var five = file.Object(5, Data.Length.ToString(CultureInfo.InvariantCulture));

        for (var number = 20; number < 20 + unindexed; number++)
        {
            file.Object(number, "null");
        }

        file.CrossReferenceStream(12, 13, rows, "/Root 1 0 R");
        return (file.Bytes, five);
    }

    /// <summary>
    /// A file whose streams 4 and 6 both take their /Length from object 5, whose row leads to the catalog's header; 4's data
    /// is 10 bytes long, 6's is <paramref name="six"/>; object 7 is defined again when <paramref name="redefined"/> says so.
    /// </summary>
    private static byte[] TwoStreamsOfOneLength(string six, bool redefined = false)
    {
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            [3] = file.Object(3, Page),
            [4] = file.Object(4, "<< /Length 5 0 R >>\nstream\n0123456789\nendstream"),
            [6] = file.Object(6, "<< /Length 5 0 R >>\nstream\n" + six + "\nendstream"),
            [5] = 9,
        };
        rows[7] = file.Padding(7);
        file.Object(5, "10");

        if (redefined)
        {
            file.Object(7, "(seven again)");
        }

        file.Table(8, rows, "/Size 8 /Root 1 0 R");
        return file.Bytes;
    }

    /// <summary>
    /// A file whose index of ten numbers fills as a rebuild expands object stream 6, which takes its /Length from object 5,
    /// whose row leads to the catalog's header, and then object stream 8.
    /// </summary>
    private static byte[] TwoObjectStreamsPastAFullIndex()
    {
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            [3] = file.Object(3, Page),
        };
        const string Data = "10 0 11 4 12 8 13 12 (a) (b) (c) (d) ";
        rows[6] = file.Object(6, "<< /Type /ObjStm /N 4 /First 20 /Length 5 0 R >>\nstream\n" + Data + "\nendstream");
        rows[8] = file.ObjectStream(8, [(20, "(w)"), (21, "(x)"), (22, "(y)"), (23, "(z)")]);
        rows[7] = file.Padding(7);
        rows[5] = 9;
        file.Object(5, Data.Length.ToString(CultureInfo.InvariantCulture));
        file.Table(10, rows, "/Size 10 /Root 1 0 R");
        return file.Bytes;
    }

    [Theory]
    [InlineData("/N")]
    [InlineData("/Filter")]
    public void An_object_stream_decoded_again_after_a_rebuild_a_load_set_off_reads_what_a_rebuild_run_first_reads(string key)
    {
        // Object stream 10 holds object 11, and takes its /Length from 21, which object stream 20 holds; 20 takes its /N, or its
        // /Filter, from 11, and its /Length from 22, whose row leads nowhere and which only object stream 30 holds. Reading 11
        // decodes 10, then 20, whose /Length rebuilds the index with the three loads in flight; 20 then asks for 11, which is
        // being loaded, and reads it as null. Finished once no load is in flight, the rebuild decodes 20 before 30 gives 22,
        // and does not keep it: 21, read next, decodes 20 again, which reads 11 as the finished index serves it — as a rebuild
        // run first does —, not as the loads in flight read it, which the finish forgets (#257).
        var bytes = LoadsInFlightReadingNull(key);
        using var settled = PdfDocument.Open(bytes);
        settled.Reader.SettleIndex();
        using var document = PdfDocument.Open(bytes);

        document.GetObject(new PdfObjectId(11)).ToString().Should().Be(settled.GetObject(new PdfObjectId(11)).ToString());

        document.WasRepaired.Should().BeTrue();
        var expected = settled.GetObject(new PdfObjectId(21)).Should().BeOfType<PdfInteger>().Subject;
        document.GetObject(new PdfObjectId(21)).Should().BeOfType<PdfInteger>().Which.Value.Should().Be(expected.Value);
    }

    [Theory]
    [InlineData("/N")]
    [InlineData("/Filter")]
    [InlineData("/Length")]
    [InlineData("/Length, with faults")]
    public void A_read_that_sets_a_rebuild_off_while_object_streams_need_each_other_reports_what_a_rebuild_run_first_reports(string key)
    {
        // Object stream 20 takes its /N, its /Filter or its /Length from 11, which object stream 10 holds, and 10 its /Length
        // from 21, which 20 holds. Reading 11 rebuilds the index while 10 and 20 are decoded — 20's /Length names 22, whose
        // row leads nowhere, or 20's own row leads nowhere —, and the loads in flight read 11 as null: they report 20
        // unreadable, or its /Length unreadable, which a rebuild run first does not. The finish withdraws what they
        // reported, and what is read after it reports what a rebuild run first reports, in the same order (#273). With
        // faults, the loads in flight also report what is reported once — a guard, a member a guard cut, a fault of the
        // syntax, of a filter, of a header, a member written as a stream —, which the finish forgets it reported.
        var bytes = key.StartsWith("/Length", StringComparison.Ordinal) ? EachOthersLength(faults: key != "/Length") : LoadsInFlightReadingNull(key);
        var options = PdfReaderOptions.Default with { Limits = PdfReaderLimits.Default with { MaxObjectLength = 4096 } };
        using var settled = PdfDocument.Open(bytes, options);
        settled.Reader.SettleIndex();
        using var document = PdfDocument.Open(bytes, options);

        foreach (var number in new[] { 11, 21 })
        {
            document.GetObject(new PdfObjectId(number)).ToString().Should().Be(settled.GetObject(new PdfObjectId(number)).ToString());
        }

        document.WasRepaired.Should().BeTrue();
        document.Diagnostics.Select(diagnostic => diagnostic.ToString())
            .Should().Equal(settled.Diagnostics.Select(diagnostic => diagnostic.ToString()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_member_fault_of_the_definition_a_rebuild_serves_is_reported_whatever_was_read_before(bool tooLong)
    {
        // Object stream 6 is defined twice, and member 7 of each is written as a stream, or runs past MaxObjectLength. The
        // chain places 6 at the first; 5's row leads into the catalog's header, and reading it rebuilds the index, which
        // serves the second. A member's fault is reported once for each definition of its stream, keyed by where the
        // definition's data starts: 7 read before the rebuild does not keep the served definition's fault from being said.
        var file = new HandWrittenFile();
        var catalog = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        var pages = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        var first = file.ObjectStream(6, [(7, tooLong ? "(" + new string('a', 2000) + ")" : "<< /Length 1 >> stream\nx\nendstream")]);
        file.Padding(9, 200);
        file.ObjectStream(6, [(7, tooLong ? "(" + new string('b', 2000) + ")" : "<< /Length 1 >> stream\ny\nendstream")]);
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, catalog, 0),
            [2] = (1, pages, 0),
            [5] = (1, 9, 0),
            [6] = (1, first, 0),
            [7] = (2, 6, 0),
        };
        file.CrossReferenceStream(20, 21, rows, "/Root 1 0 R");
        var options = tooLong ? new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = 1024 } } : PdfReaderOptions.Default;
        var code = tooLong ? PdfDiagnosticCodes.LimitObject : PdfDiagnosticCodes.ObjectStreamMemberStream;
        using var rebuiltFirst = PdfDocument.Open(file.Bytes, options);
        rebuiltFirst.GetObject(new PdfObjectId(5));
        rebuiltFirst.GetObject(new PdfObjectId(7));
        var served = rebuiltFirst.Diagnostics.Should().ContainSingle(diagnostic => diagnostic.Code == code).Which.Position;
        using var document = PdfDocument.Open(file.Bytes, options);

        document.GetObject(new PdfObjectId(7));
        document.GetObject(new PdfObjectId(5));
        document.GetObject(new PdfObjectId(7));

        served.Should().NotBe(first);
        document.Diagnostics.Where(diagnostic => diagnostic.Code == code).Select(diagnostic => diagnostic.Position).Should().HaveCount(2).And.Contain(served);
    }

    [Fact]
    public void A_finish_the_source_fails_reports_once_finished_again_what_a_rebuild_run_first_reports()
    {
        // Object stream 8 takes its /Length from 5, whose row leads into the catalog's header: reading 7, which 8 holds,
        // rebuilds the index while 8 is loaded. Object 10 is written directly and in object stream 6, so the finish meets it
        // again. The source fails the finish's read of 5: the read throws, the rebuild is left to the next, and what the
        // failed finish met and reported is withdrawn, so that the finish that completes reports what a rebuild run first
        // reports — the redefinition of 10 once.
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, file.Object(1, "<< /Type /Catalog /Pages 2 0 R /Test 7 0 R >>"), 0),
            [2] = (1, file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"), 0),
            [3] = (1, file.Object(3, Page), 0),
            [6] = (1, file.ObjectStream(6, [(10, "(ten)"), (11, "(eleven)")]), 0),
            [10] = (2, 6, 0),
            [11] = (2, 6, 1),
        };
        const string Data = "7 0 (member) ";
        rows[8] = (1, file.Object(8, "<< /Type /ObjStm /N 1 /First 4 /Length 5 0 R >>\nstream\n" + Data + "\nendstream"), 0);
        rows[7] = (2, 8, 0);
        rows[5] = (1, 9, 0);
        rows[9] = (1, file.Padding(9), 0);
        var five = file.Object(5, Data.Length.ToString(CultureInfo.InvariantCulture));
        file.Object(10, "(direct ten)");
        file.CrossReferenceStream(12, 13, rows, "/Root 1 0 R");
        using var settled = PdfDocument.Open(file.Bytes);
        settled.Reader.SettleIndex();
        settled.GetObject(new PdfObjectId(7));
        using var source = new FailingSource(file.Bytes);
        using var document = PdfDocument.Open(source, options: null, ownsSource: false);
        source.Fail(five, 2);

        FluentActions.Invoking(() => document.GetObject(new PdfObjectId(7))).Should().Throw<IOException>();
        document.GetObject(new PdfObjectId(7)).AsText().Should().Be("member");

        document.Diagnostics.Select(diagnostic => diagnostic.ToString())
            .Should().Equal(settled.Diagnostics.Select(diagnostic => diagnostic.ToString()));
    }

    [Fact]
    public void A_finish_the_source_fails_after_it_reported_withdraws_what_it_reported()
    {
        // Object 4 is defined twice; the catalog, written first, is let go by the object cache of 64 before the finish reads
        // it. Reading 7 rebuilds the index while object stream 8 is loaded, its /Length 5 leading nowhere. The finish
        // reports 4's redefinition, then fails to read the catalog: what it reported is withdrawn, and the finish that
        // completes reports it once, as a rebuild run first does.
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>();
        var catalog = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        rows[1] = (1, catalog, 0);
        file.Object(4, "(first)");

        for (var number = 100; number < 180; number++)
        {
            rows[number] = (1, file.Object(number, "(filler)"), 0);
        }

        rows[2] = (1, file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"), 0);
        rows[3] = (1, file.Object(3, Page), 0);
        rows[4] = (1, file.Object(4, "(second)"), 0);
        const string Data = "7 0 (member) ";
        rows[8] = (1, file.Object(8, "<< /Type /ObjStm /N 1 /First 4 /Length 5 0 R >>\nstream\n" + Data + "\nendstream"), 0);
        rows[7] = (2, 8, 0);
        rows[9] = (1, file.Padding(9), 0);
        rows[5] = (1, rows[9].Field2 + 600, 0);
        file.Object(5, Data.Length.ToString(CultureInfo.InvariantCulture));
        file.CrossReferenceStream(200, 201, rows, "/Root 1 0 R");
        var options = PdfReaderOptions.Default with { ObjectCacheCapacity = 64 };
        using var settled = PdfDocument.Open(file.Bytes, options);
        settled.Reader.SettleIndex();
        settled.GetObject(new PdfObjectId(7));
        using var source = new FailingSource(file.Bytes);
        using var document = PdfDocument.Open(source, options, ownsSource: false);
        source.Fail(catalog, 2);

        FluentActions.Invoking(() => document.GetObject(new PdfObjectId(7))).Should().Throw<IOException>();
        document.GetObject(new PdfObjectId(7)).AsText().Should().Be("member");

        document.Diagnostics.Select(diagnostic => diagnostic.ToString())
            .Should().Equal(settled.Diagnostics.Select(diagnostic => diagnostic.ToString()));
    }

    [Fact]
    public void A_read_that_sets_a_rebuild_off_and_nests_too_deep_reports_what_a_rebuild_run_first_reports()
    {
        // Streams 100 to 169 each take their /Length from the next, and 101's row leads nowhere: reading 100 rebuilds the
        // index with 100 and 101 in flight, and the loads in flight reach past the depth the reader follows. The finish
        // withdraws that report, and reading 100 again reports it again, as a rebuild run first does (#273).
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"),
        };

        for (var number = 100; number < 170; number++)
        {
            rows[number] = file.Object(number, string.Create(CultureInfo.InvariantCulture, $"<< /Length {number + 1} 0 R >>\nstream\nx\nendstream"));
        }

        rows[170] = file.Object(170, "1");
        rows[9] = file.Padding(9);
        rows[101] = rows[9] + 600;
        file.Table(171, rows, "/Size 171 /Root 1 0 R");
        using var settled = PdfDocument.Open(file.Bytes);
        settled.Reader.SettleIndex();
        using var document = PdfDocument.Open(file.Bytes);

        document.GetObject(new PdfObjectId(100)).ToString().Should().Be(settled.GetObject(new PdfObjectId(100)).ToString());

        document.WasRepaired.Should().BeTrue();
        document.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == PdfDiagnosticCodes.SyntaxDepthExceeded);
        document.Diagnostics.Select(diagnostic => diagnostic.ToString())
            .Should().Equal(settled.Diagnostics.Select(diagnostic => diagnostic.ToString()));
    }

    [Theory]
    [InlineData(false, 8192)]
    [InlineData(false, 64)]
    [InlineData(true, 8192)]
    [InlineData(true, 64)]
    public void An_object_stream_read_where_the_chain_places_it_notes_nothing_of_a_definition_the_reader_no_longer_serves(
        bool servedLacksEndObj, int cache)
    {
        // The shape of #255: the chain's index places object stream 6, holding 7, before a padding object; a second object
        // stream 6 follows, holding another 7. Object 5's row leads into the catalog's header, so the validator rebuilds
        // the index first, which serves the later 6. One of the two lacks its endobj. The walk of the object streams'
        // dependencies reads the chain's 6 where the chain places it, and the probe reads its header there: neither notes
        // anything of number 6, which the reader serves from the later definition. Only the definition the reader serves is
        // judged, whatever the cache let go between three validations.
        const string Lacking = "6 0 obj\n<< /Type /ObjStm /N 1 /First 4 /Length 13 >>\nstream\n7 0 (chain)\nendstream\n(stray)\nendobj\n";
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, file.Object(1, "<< /Type /Catalog /Pages 2 0 R /Foo 6 0 R /Bar 7 0 R >>"), 0),
            [2] = (1, file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"), 0),
            [3] = (1, file.Object(3, Page), 0),
            [6] = (1, file.Position, 0),
            [7] = (2, 6, 0),
            [5] = (1, 9, 0),
        };

        long served;

        if (servedLacksEndObj)
        {
            file.ObjectStream(6, [(7, "(chain)")]);
            rows[8] = (1, file.Padding(8), 0);
            served = file.Position;
            file.Raw(Lacking.Replace("(chain)", "(later)", StringComparison.Ordinal));
        }
        else
        {
            file.Raw(Lacking);
            rows[8] = (1, file.Padding(8), 0);
            served = file.ObjectStream(6, [(7, "(later)")]);
        }

        for (var number = 100; number < 300; number++)
        {
            rows[number] = (1, file.Object(number, "(filler)"), 0);
        }

        file.CrossReferenceStream(9, 300, rows, "/Root 1 0 R");
        using var document = PdfDocument.Open(file.Bytes, PdfReaderOptions.Default with { ObjectCacheCapacity = cache });

        for (var validation = 0; validation < 3; validation++)
        {
            var findings = new PdfValidator().Validate(document).Findings
                .Where(finding => finding.RuleId == PdfValidationRuleIds.ObjectEndObjMissing).ToList();

            document.GetObject(new PdfObjectId(7)).AsText().Should().Be("later");

            if (servedLacksEndObj)
            {
                findings.Should().ContainSingle().Which.Location.Position.Should().Be(served);
            }
            else
            {
                findings.Should().BeEmpty("the chain's 6, which lacks its endobj, is no definition the reader serves");
            }

            for (var number = 100; number < 300; number++)
            {
                document.GetObject(new PdfObjectId(number));
            }
        }
    }

    /// <summary>
    /// Writes the shape of #257: object streams 10 and 20 each need an object the other holds, 20 through
    /// <paramref name="key"/>, and 20's <c>/Length</c> names object 22, whose row leads nowhere and which only object stream
    /// 30, written after 20, holds.
    /// </summary>
    private static byte[] LoadsInFlightReadingNull(string key)
    {
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"), 0),
            [2] = (1, file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"), 0),
        };
        var ten = "11 0 " + (key == "/N" ? "1" : "/ASCIIHexDecode") + " ";
        rows[10] = (1, file.Object(10, string.Create(CultureInfo.InvariantCulture, $"<< /Type /ObjStm /N 1 /First 5 /Length 21 0 R >>\nstream\n{ten}\nendstream")), 0);
        rows[11] = (2, 10, 0);
        var twenty = string.Create(CultureInfo.InvariantCulture, $"21 0 {ten.Length} ");
        var (entries, data) = key == "/N"
            ? ("/N 11 0 R /First 5", twenty)
            : ("/N 1 /First 5 /Filter 11 0 R", Convert.ToHexString(Encoding.ASCII.GetBytes(twenty)) + ">");
        rows[20] = (1, file.Object(20, $"<< /Type /ObjStm {entries} /Length 22 0 R >>\nstream\n{data}\nendstream"), 0);
        rows[21] = (2, 20, 0);
        rows[30] = (1, file.ObjectStream(30, [(22, data.Length.ToString(CultureInfo.InvariantCulture))]), 0);
        rows[9] = (1, file.Padding(9), 0);
        rows[22] = (1, rows[9].Field2 + 600, 0);
        file.CrossReferenceStream(40, 41, rows, "/Root 1 0 R");
        return file.Bytes;
    }

    /// <summary>
    /// Writes object streams 10 and 20, each of whose <c>/Length</c> is an object the other holds — 10's is 21, 20's is
    /// 11 —, and whose row of 20 leads nowhere. With <paramref name="faults"/>, 10 takes its filters from 12, which runs
    /// past 4 KB, its hexadecimal data holds a byte that encodes nothing, and member 11 decodes past 4 KB; 20 gives /Type
    /// twice, its header lists 1 of its 2 objects, and member 21 is written as a stream.
    /// </summary>
    private static byte[] EachOthersLength(bool faults)
    {
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"), 0),
            [2] = (1, file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"), 0),
        };

        if (faults)
        {
            var ten = Convert.ToHexString(Compress(Encoding.ASCII.GetBytes("11 0 [" + string.Concat(Enumerable.Repeat("0 ", 2100)) + "]")));
            ten = ten[..8] + "G" + ten[8..] + ">";
            rows[12] = (1, file.Object(12, "[/ASCIIHexDecode /FlateDecode]" + new string(' ', 5000)), 0);
            rows[10] = (1, file.Object(10, $"<< /Type /ObjStm /N 1 /First 5 /Filter 12 0 R /Length 21 0 R >>\nstream\n{ten}\nendstream"), 0);
            rows[9] = (1, file.Padding(9), 0);
            file.Object(20, "<< /Type /ObjStm /Type /ObjStm /N 2 /First 5 /Length 11 0 R >>\nstream\n21 0 << /Length 1 >>\nstream\nx\nendstream\nendstream");
        }
        else
        {
            rows[10] = (1, file.Object(10, "<< /Type /ObjStm /N 1 /First 5 /Length 21 0 R >>\nstream\n11 0 6\nendstream"), 0);
            rows[9] = (1, file.Padding(9), 0);
            file.Object(20, "<< /Type /ObjStm /N 1 /First 5 /Length 11 0 R >>\nstream\n21 0 6\nendstream");
        }

        rows[11] = (2, 10, 0);
        rows[20] = (1, rows[9].Field2 + 600, 0);
        rows[21] = (2, 20, 0);
        file.CrossReferenceStream(40, 41, rows, "/Root 1 0 R");
        return file.Bytes;
    }

    /// <summary>Compresses <paramref name="data"/> as <c>/FlateDecode</c> decodes it.</summary>
    private static byte[] Compress(byte[] data)
    {
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            zlib.Write(data);
        }

        return compressed.ToArray();
    }

    /// <summary>Validates <paramref name="document"/> as many times as asked, checks each finds what the first did, and disposes it.</summary>
    private static List<string> Findings(PdfDocument document, int validations)
    {
        using (document)
        {
            var first = new PdfValidator().Validate(document).Findings.Select(finding => finding.ToString()).ToList();

            for (var count = 1; count < validations; count++)
            {
                new PdfValidator().Validate(document).Findings.Select(finding => finding.ToString()).Should().Equal(first);
            }

            return first;
        }
    }
}
