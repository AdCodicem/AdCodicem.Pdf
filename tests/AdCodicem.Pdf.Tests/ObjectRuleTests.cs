using System.Globalization;
using System.Text;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.IO;
using AdCodicem.Pdf.IO.Filters;
using AdCodicem.Pdf.Objects;
using AdCodicem.Pdf.Validation;
using FsCheck;
using FsCheck.Fluent;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// The <c>object</c> family of M02's third slice — references to objects the file lacks, objects without
/// <c>endobj</c>, names with a null character —, and the object stream that needs itself to be read (#51): each rule on
/// a file that breaks it, a sound file, and a file that is unusual and legal.
/// </summary>
public class ObjectRuleTests
{
    [Fact]
    public void A_reference_to_an_object_the_file_lacks_is_a_warning_at_the_object_holding_it()
    {
        var report = Validate(PdfTemplate.SoundWith("/Resources << >>", "/Resources << /XObject << /Im0 9 0 R >> >>"));

        var finding = report.Findings.Should().ContainSingle().Which;
        finding.RuleId.Should().Be(PdfValidationRuleIds.ObjectReferenceMissing);
        finding.Severity.Should().Be(PdfValidationSeverity.Warning, "the reference reads as null, as the specification says (ADR 45)");
        finding.Location.Object.Should().Be(new PdfObjectId(3));
        finding.Location.PageIndex.Should().Be(0, "the object holding it is the first page");
        finding.Message.Should().Be(
            "Object 3 refers to object 9 0 under /Resources/XObject/Im0, which the file lacks: the reference reads as null.");
        finding.Remedy.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Several_references_to_nothing_in_one_object_are_one_finding_naming_the_first()
    {
        var report = Validate(PdfTemplate.SoundWith("/Resources << >>", "/Resources << >> /Annots [5 0 R 3 0 R 7 0 R] /Thumb 8 0 R"));

        Single(report, PdfValidationRuleIds.ObjectReferenceMissing).Message.Should().Be(
            "Object 3 holds 3 references to objects the file lacks, which read as null; the first refers to object 5 0 under /Annots[0].");
    }

    [Fact]
    public void A_reference_to_a_free_entry_names_an_object_the_file_lacks()
    {
        var template = PdfTemplate.Sound
            .Replace("/Type /Catalog /Pages 2 0 R", "/Type /Catalog /Pages 2 0 R /Outlines 4 0 R", StringComparison.Ordinal)
            .Replace("0 4\n", "0 5\n", StringComparison.Ordinal)
            .Replace("{row:3}\n", "{row:3}\n0000000000 00001 f \n", StringComparison.Ordinal)
            .Replace("/Size 4", "/Size 5", StringComparison.Ordinal);

        Single(Validate(PdfTemplate.Build(template)), PdfValidationRuleIds.ObjectReferenceMissing).Message.Should().Be(
            "Object 1 refers to object 4 0 under /Outlines, which the file lacks: the reference reads as null.");
    }

    [Fact]
    public void A_reference_to_object_0_names_an_object_the_file_lacks_where_it_lies()
    {
        // Object 0 heads the free list and is never in use (ISO 32000-1, 7.5.4); jhove-hul-28's page writes one in its
        // /Contents.
        var report = Validate(PdfTemplate.SoundWith("/Resources << >>", "/Resources << >> /Annots [0 0 R 5 0 R]"));

        var finding = Single(report, PdfValidationRuleIds.ObjectReferenceMissing);
        finding.Location.Object.Should().Be(new PdfObjectId(3));
        finding.Message.Should().Be(
            "Object 3 holds 2 references to objects the file lacks, which read as null; the first refers to object 0 0 under /Annots[0].");

        Single(Validate(PdfTemplate.SoundWith("/Resources << >>", "/Resources << >> /Annots [0 0 R]")), PdfValidationRuleIds.ObjectReferenceMissing)
            .Message.Should().Be("Object 3 refers to object 0 0 under /Annots[0], which the file lacks: the reference reads as null.");
    }

    [Fact]
    public void A_reference_to_object_0_names_an_object_the_file_lacks_though_the_table_holds_entry_0_in_use()
    {
        // A table that writes its first row in use still holds no object 0: the reader never reads one.
        var template = PdfTemplate.Sound
            .Replace("{free}\n", "{row:1}\n", StringComparison.Ordinal)
            .Replace("/Type /Catalog /Pages 2 0 R", "/Type /Catalog /Pages 2 0 R /Outlines 0 0 R", StringComparison.Ordinal);

        using var document = PdfDocument.Open(PdfTemplate.Build(template));
        document.GetObject(new PdfObjectId(0)).Should().BeSameAs(PdfNull.Instance);

        Single(new PdfValidator().Validate(document), PdfValidationRuleIds.ObjectReferenceMissing).Message.Should().Be(
            "Object 1 refers to object 0 0 under /Outlines, which the file lacks: the reference reads as null.");
    }

    [Theory]
    [InlineData("/DecodeParms 0 0 R")]
    [InlineData("/N 0 0 R")]
    [InlineData("/N 3 0 R")]
    [InlineData("/Type 3 0 R")]
    public void A_reference_to_object_0_under_an_object_stream_s_keys_needs_nothing_though_the_table_places_object_0_in_it(string entries)
    {
        // The cross-reference stream's first row is rewritten to place object 0 at index 0 of object stream 7, whose
        // parameters, count or type name object 0, directly or through object 3, a reference to it: the reader still never
        // reads object 0, so the stream does not need itself.
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(3, "0 0 R")
            .WithObject(5, "<< /Predictor 1 >>")
            .WithObject(6, "<< /Title (six) >>")
            .BuildWithXRefStream(rootNumber: 1, compressedObjects: [5, 6], compressObjectStream: true, objectStreamEntries: entries);
        var text = Encoding.Latin1.GetString(file);
        var rows = text.IndexOf("stream\n", text.IndexOf("/Type /XRef", StringComparison.Ordinal), StringComparison.Ordinal) + "stream\n".Length;
        file[rows] = 2;
        file[rows + 4] = 7;
        file[rows + 5] = 0;
        file[rows + 6] = 0;

        using var document = PdfDocument.Open(file);
        document.GetObject(new PdfObjectId(6));
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().NotContain(PdfDiagnosticCodes.StreamSelfReference);

        new PdfValidator().Validate(document).Contains(PdfValidationRuleIds.XRefObjectStreamCircular).Should().BeFalse();
    }

    [Fact]
    public void A_reference_in_the_trailer_is_located_at_the_trailer()
    {
        var file = PdfTemplate.SoundWith("/Size 4 /Root 1 0 R", "/Size 4 /Root 1 0 R /Info 9 0 R");

        var finding = Single(Validate(file), PdfValidationRuleIds.ObjectReferenceMissing);

        finding.Location.Position.Should().Be(PdfTemplate.OffsetOf(file, "trailer"));
        finding.Message.Should().Be("The trailer refers to object 9 0 under /Info, which the file lacks: the reference reads as null.");
    }

    [Fact]
    public void An_object_whose_whole_value_is_a_reference_to_nothing_is_reported_under_its_value()
    {
        var template = PdfTemplate.Sound
            .Replace("/Type /Catalog /Pages 2 0 R", "/Type /Catalog /Pages 2 0 R /Outlines 4 0 R", StringComparison.Ordinal)
            .Replace("xref\n0 4\n", "4 0 obj\n9 0 R\nendobj\nxref\n0 5\n", StringComparison.Ordinal)
            .Replace("{row:3}\n", "{row:3}\n{row:4}\n", StringComparison.Ordinal)
            .Replace("/Size 4", "/Size 5", StringComparison.Ordinal);

        var finding = Single(Validate(PdfTemplate.Build(template)), PdfValidationRuleIds.ObjectReferenceMissing);

        finding.Location.Object.Should().Be(new PdfObjectId(4));
        finding.Message.Should().Be("Object 4 refers to object 9 0 under its value, which the file lacks: the reference reads as null.");
    }

    [Fact]
    public void A_root_naming_nothing_is_the_file_rule_s_alone()
    {
        var report = Validate(PdfTemplate.SoundWith("/Root 1 0 R", "/Root 9 0 R"));

        report.Contains(PdfValidationRuleIds.FileRootInvalid).Should().BeTrue();
        report.Contains(PdfValidationRuleIds.ObjectReferenceMissing).Should().BeFalse("/Root is file.root-invalid's");
    }

    [Fact]
    public void A_root_naming_nothing_in_a_file_with_no_catalog_is_the_file_rule_s_alone()
    {
        // With a catalog in the file, the reader takes it and the trailer's /Root names it: the graph never met the reference
        // to nothing. With none, the trailer keeps /Root 9 0 R, and the graph walks it.
        var report = Validate(PdfTemplate.Build(PdfTemplate.Sound
            .Replace("/Root 1 0 R", "/Root 9 0 R", StringComparison.Ordinal)
            .Replace("/Type /Catalog", "/Type /Outlines", StringComparison.Ordinal)));

        report.Contains(PdfValidationRuleIds.FileRootInvalid).Should().BeTrue();
        report.Contains(PdfValidationRuleIds.ObjectReferenceMissing).Should().BeFalse("/Root is file.root-invalid's");
    }

    [Fact]
    public void A_reference_to_nothing_in_a_kid_written_in_its_node_is_described_where_it_lies()
    {
        // The kids a node lists are page-tree.kid-invalid's, which reports a dictionary written there; the references that
        // dictionary holds are this rule's. Describing them once passed over the node's whole /Kids, and found nothing.
        var finding = Single(
            Validate(PdfTemplate.SoundWith("/Kids [3 0 R]", "/Kids [3 0 R << /A 9 0 R >>]")),
            PdfValidationRuleIds.ObjectReferenceMissing);

        finding.Message.Should().Be("Object 2 refers to object 9 0 under /Kids[1]/A, which the file lacks: the reference reads as null.");
    }

    [Theory]
    [InlineData(8)]
    [InlineData(9)]
    public void A_reference_to_nothing_after_a_kid_to_nothing_is_the_one_described(int foo)
    {
        // A kid naming nothing is page-tree.kid-invalid's, and the walk does not count it: describing the node's one counted
        // reference passes over it, to /Foo. One that names the object the kid names, which the walk met first, through the
        // kid, is counted where it is written all the same.
        var finding = Single(
            Validate(PdfTemplate.SoundWith("/Kids [3 0 R]", $"/Kids [9 0 R 3 0 R] /Foo {foo} 0 R")),
            PdfValidationRuleIds.ObjectReferenceMissing);

        finding.Message.Should().Be($"Object 2 refers to object {foo} 0 under /Foo, which the file lacks: the reference reads as null.");
    }

    [Fact]
    public void References_to_one_object_the_file_lacks_are_counted_wherever_they_are_written_whichever_was_met_first()
    {
        // The catalog names object 9 first; the page names it three times after. Each reference is counted where it is
        // written, in the object that holds it, whatever reference the walk met it through first.
        var report = Validate(PdfTemplate.Build(PdfTemplate.Sound
            .Replace("/Type /Catalog /Pages 2 0 R", "/Type /Catalog /Pages 2 0 R /Outlines 9 0 R", StringComparison.Ordinal)
            .Replace("/Resources << >> >>", "/Resources << >> /Annots [9 0 R 9 0 R] /Thumb 9 0 R >>", StringComparison.Ordinal)));

        report.Findings.Where(finding => finding.RuleId == PdfValidationRuleIds.ObjectReferenceMissing).Select(finding => finding.Message).Should().Equal(
            "Object 1 refers to object 9 0 under /Outlines, which the file lacks: the reference reads as null.",
            "Object 3 holds 3 references to objects the file lacks, which read as null; the first refers to object 9 0 under /Annots[0].");
    }

    [Fact]
    public void An_object_the_index_lacks_until_a_load_rebuilds_it_is_walked_where_the_walk_runs_on_an_unsettled_index()
    {
        // Validation settles the index first, and the graph tells an object the file lacks by the index alone. Run on an index
        // a load can still rebuild — object 4 written in the file, its row lost with the section /Prev names —, the graph
        // asks for the object all the same, as resolving the reference does: the load rebuilds the index and finds it, and
        // the graph walks it, to the reference to nothing it holds.
        var file = PdfTemplate.Build(PdfTemplate.Sound
            .Replace("/Type /Catalog /Pages 2 0 R", "/Type /Catalog /Pages 2 0 R /Foo 4 0 R", StringComparison.Ordinal)
            .Replace("xref\n0 4\n", "4 0 obj\n<< /A 9 0 R >>\nendobj\nxref\n0 4\n", StringComparison.Ordinal)
            .Replace("/Size 4", "/Size 4 /Prev 9999999", StringComparison.Ordinal));
        using var document = PdfDocument.Open(file);
        var pages = PageTreeWalk.Run(document);
        document.Reader.GetPresence(4).Should().Be(ObjectPresence.Missing, "the index the chain gave lacks it");

        var graph = ObjectGraph.Run(document, pages);

        document.WasRepaired.Should().BeTrue();
        graph.MissingReferences.Select(finding => finding.Message).Should().Equal(
            "Object 4 refers to object 9 0 under /A, which the file lacks: the reference reads as null.");
    }

    [Fact]
    public void An_object_a_rebuild_finds_lacking_after_the_walk_claimed_it_is_counted_for_the_reference_that_claimed_it()
    {
        // The update's trailer runs past MaxTrailerLength before its /Prev, so that an object only the original section could
        // index — the page tree, 2, and object 9, which no section indexes — is unknown rather than missing, and claimed
        // when the catalog names it. The catalog names object 5 last, which the update's table places where it is not:
        // reading it first rebuilds the index, which finds 2 and not 9, missing now. Run on an index a load can still
        // rebuild, the walk counts 9 when it reads it, for the catalog that claimed it, and each reference met after, as
        // the page tree's.
        var original = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 /Foo 9 0 R >>")
            .BuildClassic(rootNumber: 1);
        var text = Encoding.Latin1.GetString(TestPdfBuilder.AppendIncrementalUpdate(
            original,
            rootNumber: 1,
            [(1, $"<< /Type /Catalog /Pages 2 0 R /Foo 9 0 R /Bar 5 0 R /Note ({new string('x', 2000)}) >>"), (5, "<< /Five 5 >>")]));
        var five = string.Create(CultureInfo.InvariantCulture, $"{text.LastIndexOf("5 0 obj", StringComparison.Ordinal):D10} 00000 n");
        var trailer = text.LastIndexOf("/Root 1 0 R /Prev", StringComparison.Ordinal);
        var file = Encoding.Latin1.GetBytes(text
            .Insert(trailer + "/Root 1 0 R".Length, $" /Pad ({new string('x', 70 * 1024)})")
            .Replace(five, "0000000010 00000 n", StringComparison.Ordinal));
        using var document = PdfDocument.Open(file);
        var pages = PageTreeWalk.Run(document);
        document.Reader.GetPresence(9).Should().Be(ObjectPresence.Unindexed, "a guard left part of the chain unread");

        var graph = ObjectGraph.Run(document, pages);

        document.WasRepaired.Should().BeTrue();
        document.Reader.GetPresence(9).Should().Be(ObjectPresence.Missing, "the rebuild read the whole file");
        graph.MissingReferences.Select(finding => finding.Message).Should().Equal(
            "Object 1 refers to object 9 0 under /Foo, which the file lacks: the reference reads as null.",
            "Object 2 refers to object 9 0 under /Foo, which the file lacks: the reference reads as null.");
    }

    [Fact]
    public void A_reference_to_a_literal_null_is_sound()
    {
        var template = PdfTemplate.Sound
            .Replace("/Type /Catalog /Pages 2 0 R", "/Type /Catalog /Pages 2 0 R /Outlines 4 0 R", StringComparison.Ordinal)
            .Replace("xref\n0 4\n", "4 0 obj\nnull\nendobj\nxref\n0 5\n", StringComparison.Ordinal)
            .Replace("{row:3}\n", "{row:3}\n{row:4}\n", StringComparison.Ordinal)
            .Replace("/Size 4", "/Size 5", StringComparison.Ordinal);

        Validate(PdfTemplate.Build(template)).Findings.Should().BeEmpty();
    }

    [Fact]
    public void A_reference_nothing_reachable_holds_is_not_judged()
    {
        var file = PdfTemplate.Build(PdfTemplate.Spread.Replace("(" + new string('x', 1200) + ")", "[9 0 R]", StringComparison.Ordinal));

        Validate(file).Findings.Should().BeEmpty();
    }

    [Fact]
    public void A_reference_to_an_object_whose_entry_leads_nowhere_is_the_entry_s_finding()
    {
        var file = PdfTemplate.Build(PdfTemplate.Sound
            .Replace("/Type /Catalog /Pages 2 0 R", "/Type /Catalog /Pages 2 0 R /Outlines 4 0 R", StringComparison.Ordinal)
            .Replace("0 4\n", "0 5\n", StringComparison.Ordinal)
            .Replace("{row:3}\n", "{row:3}\n0000000009 00000 n \n", StringComparison.Ordinal)
            .Replace("/Size 4", "/Size 5", StringComparison.Ordinal));

        var report = Validate(file);

        report.Contains(PdfValidationRuleIds.XRefEntryBroken).Should().BeTrue();
        report.Contains(PdfValidationRuleIds.ObjectReferenceMissing).Should().BeFalse("the index holds the object; its entry is the fault");
    }

    [Fact]
    public void An_object_without_endobj_is_a_warning_at_the_object()
    {
        var file = PdfTemplate.SoundWith("<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj", "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");

        var finding = Validate(file).Findings.Should().ContainSingle().Which;

        finding.RuleId.Should().Be(PdfValidationRuleIds.ObjectEndObjMissing);
        finding.Severity.Should().Be(PdfValidationSeverity.Warning, "the reader reads the value as far as it goes (ADR 45)");
        finding.Location.Object.Should().Be(new PdfObjectId(2));
        finding.Location.Position.Should().Be(PdfTemplate.OffsetOf(file, "2 0 obj"));
        finding.Message.Should().Be("Object 2 does not end with endobj: what follows its value is something else, or the end of the file.");
    }

    [Fact]
    public void An_object_the_file_ends_in_does_not_end_with_endobj()
    {
        var template = PdfTemplate.Sound
            .Replace("/Size 4 /Root 1 0 R", "/Size 5 /Root 1 0 R /Info 4 0 R", StringComparison.Ordinal)
            .Replace("0 4\n", "0 5\n", StringComparison.Ordinal)
            .Replace("{row:3}\n", "{row:3}\n{row:4}\n", StringComparison.Ordinal)
            + "4 0 obj\n<< /Title (cut) >>\n";

        var finding = Validate(PdfTemplate.Build(template)).Findings.Should().ContainSingle().Which;

        finding.RuleId.Should().Be(PdfValidationRuleIds.ObjectEndObjMissing);
        finding.Location.Object.Should().Be(new PdfObjectId(4));
    }

    [Fact]
    public void A_stream_without_endstream_leaves_where_its_object_ends_unjudged()
    {
        // The reader finds no endstream, and takes the stream to run to the end of the file: the endobj after its
        // data is not one it can see, and the missing endstream is the stream rules' to report.
        var template = PdfTemplate.Sound
            .Replace("/Resources << >>", "/Resources << >> /Contents 4 0 R", StringComparison.Ordinal)
            .Replace("xref\n0 4\n", "4 0 obj\n<< /Length 5 >>\nstream\nq Q\n\nendobj\nxref\n0 5\n", StringComparison.Ordinal)
            .Replace("{row:3}\n", "{row:3}\n{row:4}\n", StringComparison.Ordinal)
            .Replace("/Size 4", "/Size 5", StringComparison.Ordinal);

        Validate(PdfTemplate.Build(template)).Contains(PdfValidationRuleIds.ObjectEndObjMissing).Should().BeFalse();
    }

    [Fact]
    public void A_stream_the_file_ends_in_does_not_end_with_endobj()
    {
        var template = PdfTemplate.Sound
            .Replace("/Resources << >>", "/Resources << >> /Contents 4 0 R", StringComparison.Ordinal)
            .Replace("0 4\n", "0 5\n", StringComparison.Ordinal)
            .Replace("{row:3}\n", "{row:3}\n{row:4}\n", StringComparison.Ordinal)
            .Replace("/Size 4", "/Size 5", StringComparison.Ordinal)
            + "4 0 obj\n<< /Length 500 >>\nstream\nq Q\n";

        Single(Validate(PdfTemplate.Build(template)), PdfValidationRuleIds.ObjectEndObjMissing).Location.Object
            .Should().Be(new PdfObjectId(4));
    }

    [Fact]
    public void An_empty_object_has_its_endobj()
    {
        var template = PdfTemplate.Sound
            .Replace("/Type /Catalog /Pages 2 0 R", "/Type /Catalog /Pages 2 0 R /Outlines 4 0 R", StringComparison.Ordinal)
            .Replace("xref\n0 4\n", "4 0 obj endobj\nxref\n0 5\n", StringComparison.Ordinal)
            .Replace("{row:3}\n", "{row:3}\n{row:4}\n", StringComparison.Ordinal)
            .Replace("/Size 4", "/Size 5", StringComparison.Ordinal);

        Validate(PdfTemplate.Build(template)).Contains(PdfValidationRuleIds.ObjectEndObjMissing).Should().BeFalse();
    }

    [Fact]
    public void Endobj_on_the_value_s_own_line_is_sound()
    {
        Validate(PdfTemplate.SoundWith("<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj", "<< /Type /Pages /Kids [3 0 R] /Count 1 >>endobj"))
            .Findings.Should().BeEmpty();
    }

    [Fact]
    public void An_object_nothing_reachable_holds_is_not_judged_for_its_endobj()
    {
        var file = PdfTemplate.Build(PdfTemplate.Spread.Replace(")\nendobj\n3 0 obj", ")\n3 0 obj", StringComparison.Ordinal));

        Validate(file).Findings.Should().BeEmpty();
    }

    [Theory]
    [InlineData("/PageMode /Use#00Outlines", "Object 1 holds the name /Use#00Outlines, under /PageMode, and a name cannot contain a null character.")]
    [InlineData("/Us#00er 1", "Object 1 holds the name /Us#00er, as a key of its dictionary, and a name cannot contain a null character.")]
    public void A_name_with_a_null_character_is_a_warning(string entry, string message)
    {
        var finding = Validate(PdfTemplate.SoundWith("/Type /Catalog", "/Type /Catalog " + entry)).Findings.Should().ContainSingle().Which;

        finding.RuleId.Should().Be(PdfValidationRuleIds.ObjectNameNullCharacter);
        finding.Severity.Should().Be(PdfValidationSeverity.Warning, "the reader keeps the name as written (ADR 45)");
        finding.Location.Object.Should().Be(new PdfObjectId(1));
        finding.Message.Should().Be(message);
    }

    [Fact]
    public void A_rebuilt_object_is_located_under_the_generation_its_header_gives()
    {
        // #118: the rebuilt index recorded every object at generation 0, so the findings on page 3 1 and on object 4 2 named
        // objects 3 0 and 4 0, which the file does not write — while the page tree's findings, from the references, said 3 1.
        const string Objects = """
            1 0 obj
            << /Type /Catalog /Pages 2 0 R >>
            endobj
            2 0 obj
            << /Type /Pages /Kids [3 1 R] /Count 1 >>
            endobj
            3 1 obj
            << /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Annots [9 0 R] /PieceInfo 4 2 R >>
            endobj
            4 2 obj
            << /Bad#00Name 1 >>
            endobj
            %%EOF

            """;
        var report = Validate(Encoding.ASCII.GetBytes("%PDF-1.7\n" + Objects.Replace("\r\n", "\n", StringComparison.Ordinal)));

        Single(report, PdfValidationRuleIds.ObjectReferenceMissing).Location.Object.Should().Be(new PdfObjectId(3, 1));
        Single(report, PdfValidationRuleIds.ObjectNameNullCharacter).Location.Object.Should().Be(new PdfObjectId(4, 2));
    }

    [Theory]
    [InlineData("row")]
    [InlineData("shifted")]
    [InlineData("rebuilt")]
    public void A_catalog_found_by_its_type_is_named_one_way_throughout_a_report(string layout)
    {
        // #118: /Root names nothing, and the reader finds the catalog, written 1 1 obj, among the indexed objects. The graph
        // named it from its entry, 1 1, and the Arlington walk from the reference the reader put in the trailer, 1 0.
        var report = Validate(DocumentReaderTests.CatalogWrittenUnderGeneration1(
            layout, "/Type /Catalog /Pages 2 0 R /Outlines 8 0 R /PageMode 5"));

        Single(report, PdfValidationRuleIds.ObjectReferenceMissing).Location.Object.Should().Be(new PdfObjectId(1, 1));
        Single(report, PdfValidationRuleIds.ObjectValueTypeWrong).Location.Object.Should().Be(new PdfObjectId(1, 1));
    }

    [Fact]
    public void An_object_only_a_rebuild_after_the_chain_finds_is_located_under_the_generation_its_header_gives()
    {
        // #118: the chain is read, and the catalog's row, which leads outside the file, rebuilds the index as the document
        // opens. Object 5, written 5 1 obj, has no row: only the rebuild finds it, and the graph names it from the index the
        // reader reads with, not from the chain's copy, which lacks it.
        var template = PdfTemplate.Sound
            .Replace("{row:1}", "{row:1:0:3000}", StringComparison.Ordinal)
            .Replace("/Type /Catalog /Pages 2 0 R", "/Type /Catalog /Pages 2 0 R /PieceInfo 5 1 R", StringComparison.Ordinal)
            .Replace("endobj\nxref", "endobj\n5 1 obj\n<< /Missing 9 0 R >>\nendobj\nxref", StringComparison.Ordinal);
        using var document = PdfDocument.Open(PdfTemplate.Build(template));
        var report = new PdfValidator().Validate(document);

        document.WasRepaired.Should().BeTrue();
        document.Reader.Structure.ChainRead.Should().BeTrue();
        Single(report, PdfValidationRuleIds.ObjectReferenceMissing).Location.Object.Should().Be(new PdfObjectId(5, 1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void A_relocated_object_is_located_under_its_header_s_generation_beside_the_row_s_mismatch(int row)
    {
        // #118, decision of 2026-10-03: the page's row gives another generation than its header, 3 1 obj, 4 bytes after
        // it. The cross-reference rules name the object as the row does, and the graph as the relocated entry, its header.
        var template = PdfTemplate.Sound
            .Replace("3 0 obj", "3 1 obj", StringComparison.Ordinal)
            .Replace("/Kids [3 0 R]", "/Kids [3 1 R]", StringComparison.Ordinal)
            .Replace("/Resources << >>", "/Resources << >> /Annots [9 0 R]", StringComparison.Ordinal)
            .Replace("{row:3}", string.Create(CultureInfo.InvariantCulture, $"{{row:3:{row}:4}}"), StringComparison.Ordinal);
        var report = Validate(PdfTemplate.Build(template));

        Single(report, PdfValidationRuleIds.XRefGenerationMismatch).Location.Object.Should().Be(new PdfObjectId(3, row));
        Single(report, PdfValidationRuleIds.ObjectReferenceMissing).Location.Object.Should().Be(new PdfObjectId(3, 1));
    }



    [Fact]
    public void Several_names_with_a_null_character_in_one_object_are_one_finding()
    {
        var report = Validate(PdfTemplate.SoundWith("/Type /Catalog", "/Type /Catalog /A#00 /B#00 /C [/D#00]"));

        Single(report, PdfValidationRuleIds.ObjectNameNullCharacter).Message.Should().Be(
            "Object 1 holds 3 names with a null character, which a name cannot contain; the first is /A#00, as a key of its dictionary.");
    }

    [Fact]
    public void Other_escaped_characters_in_a_name_are_sound()
    {
        Validate(PdfTemplate.SoundWith("/Type /Catalog", "/Type /Catalog /PageLabel#20Name /A#23B")).Findings.Should().BeEmpty();
    }

    [Theory]
    [InlineData("/DecodeParms 5 0 R", "<< /Predictor 1 >>", "/DecodeParms")]
    [InlineData("/DecodeParms << /Predictor 5 0 R >>", "1", "/DecodeParms")]
    public void An_object_stream_that_needs_an_object_it_holds_is_an_error(string entries, string parameter, string key)
    {
        var file = ObjectStream(entries, parameter);

        using var document = PdfDocument.Open(file);
        var report = new PdfValidator().Validate(document);

        var finding = report.Findings.Should().ContainSingle().Which;
        finding.RuleId.Should().Be(PdfValidationRuleIds.XRefObjectStreamCircular);
        finding.Severity.Should().Be(PdfValidationSeverity.Error, "the stream is read without a value it needs (ADR 45)");
        finding.Location.Object.Should().Be(new PdfObjectId(7));
        finding.Location.Position.Should().Be(PdfTemplate.OffsetOf(file, "7 0 obj"));
        finding.Message.Should().Be(
            $"Object stream 7 needs object 5, which it holds, to be read: its {key} names it, and the object cannot be read before the stream is.");
        document.Diagnostics.Should().ContainSingle(diagnostic => diagnostic.Code == PdfDiagnosticCodes.StreamSelfReference);
    }

    [Fact]
    public void An_object_stream_that_needs_itself_through_an_object_written_in_the_file_is_circular()
    {
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(3, "<< /Predictor 5 0 R >>")
            .WithObject(5, "1")
            .WithObject(6, "<< /Title (six) >>")
            .BuildWithXRefStream(rootNumber: 1, compressedObjects: [5, 6], compressObjectStream: true, objectStreamEntries: "/DecodeParms 3 0 R");

        Single(Validate(file), PdfValidationRuleIds.XRefObjectStreamCircular).Message.Should().Be(
            "Object stream 7 needs object 5, which it holds, to be read: its /DecodeParms names it, and the object cannot be read before the stream is.");
    }

    [Theory]
    [InlineData("<< /Predictor 5 0 R >>", "/DecodeParms 3 0 R", "/DecodeParms")]
    [InlineData("[5 0 R]", "/Filter 3 0 R", "/Filter")]
    public void A_length_that_names_what_a_filter_or_its_parameters_names_hides_no_loop_through_them(string three, string key, string named)
    {
        // Stream 7's /Length names object 3, as its /DecodeParms, or its /Filter, does: a length reads nothing inside 3, a
        // dictionary or an array, while the filter or its parameters read 5, which 7 holds. Following the length to 3 first
        // must not keep the walk from following the filter or its parameters through it.
        var five = named == "/Filter" ? "/FlateDecode" : "1";
        var header = string.Create(CultureInfo.InvariantCulture, $"5 0 6 {five.Length + 1} ");
        var data = Deflate(header + five + "\n(six)\n");
        var filter = named == "/Filter" ? string.Empty : " /Filter /FlateDecode";
        var written = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, written.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"), 0),
            [2] = (1, written.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"), 0),
            [3] = (1, written.Object(3, three), 0),
            [7] = (1, written.Object(7, string.Create(CultureInfo.InvariantCulture, $"<< /Type /ObjStm /N 2 /First {header.Length} {key}{filter} /Length 3 0 R >>\nstream\n{data}\nendstream")), 0),
            [5] = (2, 7, 0),
            [6] = (2, 7, 1),
        };
        written.CrossReferenceStream(12, 13, rows, "/Root 1 0 R");

        Single(Validate(written.Bytes), PdfValidationRuleIds.XRefObjectStreamCircular).Message.Should().Be(
            $"Object stream 7 needs object 5, which it holds, to be read: its {named} names it, and the object cannot be read before the stream is.");
    }

    [Theory]
    [InlineData("/N", 31, true)]
    [InlineData("/N", 32, false)]
    [InlineData("/Length", 32, true)]
    [InlineData("/Length", 33, false)]
    public void A_chain_of_references_is_followed_as_far_as_a_reference_resolves(string key, int links, bool circular)
    {
        // Stream 7's /N, or /Length, names object 100, the first of a chain of objects each naming the next, the last naming
        // 5, which 7 holds. A reference resolution reads the 32 objects of a chain at most, and a /Length one more, which the
        // parse reads before it resolves what it gives: past that, the reader never reads 5, and needs nothing.
        const string Data = "5 0 6 2 2 (six) ";
        var written = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, written.Object(1, "<< /Type /Catalog /Pages 2 0 R /Foo 6 0 R >>"), 0),
            [2] = (1, written.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"), 0),
        };

        for (var link = 0; link < links; link++)
        {
            rows[100 + link] = (1, written.Object(100 + link, (link + 1 < links ? 101 + link : 5).ToString(CultureInfo.InvariantCulture) + " 0 R"), 0);
        }

        var count = key == "/N" ? "100 0 R" : "2";
        var length = key == "/Length" ? "100 0 R" : Data.Length.ToString(CultureInfo.InvariantCulture);
        rows[7] = (1, written.Object(7, $"<< /Type /ObjStm /N {count} /First 8 /Length {length} >>\nstream\n{Data}\nendstream"), 0);
        rows[5] = (2, 7, 0);
        rows[6] = (2, 7, 1);
        written.CrossReferenceStream(12, 100 + links, rows, "/Root 1 0 R");

        Validate(written.Bytes).Contains(PdfValidationRuleIds.XRefObjectStreamCircular).Should().Be(circular);

        using var document = PdfDocument.Open(written.Bytes);
        document.GetObject(new PdfObjectId(6));
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Contains(PdfDiagnosticCodes.StreamSelfReference).Should().Be(circular);
    }

    [Fact]
    public void Of_two_objects_an_object_stream_holds_and_needs_the_one_the_reader_needs_first_is_named()
    {
        // Object 3, which the stream's /DecodeParms names, is an array of 5 and 6, both in the stream. The one Flate step
        // reads the element at its place, 5, and nothing else of the array: 5 is the object the reader needs, and names
        // (#277). The walk took the last value it met first, and named 6.
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(3, "[5 0 R 6 0 R]")
            .WithObject(5, "1")
            .WithObject(6, "<< /Title (six) >>")
            .BuildWithXRefStream(rootNumber: 1, compressedObjects: [5, 6], compressObjectStream: true, objectStreamEntries: "/DecodeParms 3 0 R");

        Single(Validate(file), PdfValidationRuleIds.XRefObjectStreamCircular).Message.Should().Be(
            "Object stream 7 needs object 5, which it holds, to be read: its /DecodeParms names it, and the object cannot be read before the stream is.");
        ReaderNeeds(file).Should().Be(5);
    }

    [Fact]
    public void An_object_stream_whose_count_it_holds_is_circular_and_not_also_broken()
    {
        var file = ObjectStream("/N 5 0 R", "2", compress: false);

        var report = Validate(file);

        Single(report, PdfValidationRuleIds.XRefObjectStreamCircular).Message.Should().Be(
            "Object stream 7 needs object 5, which it holds, to be read: its /N names it, and the object cannot be read before the stream is.");
        report.Contains(PdfValidationRuleIds.XRefObjectStreamBroken).Should().BeFalse("the loop is the fault, reported once");
    }

    [Theory]
    [InlineData("/Type 5 0 R  ", false)]
    [InlineData("/Type 4 0 R  ", true)]
    public void An_object_stream_whose_type_it_holds_is_circular_and_not_also_broken(string type, bool throughAReference)
    {
        // Stream 7's /Type names object 5, /ObjStm, which the stream holds — directly, or through object 4, written in the
        // file, whose value is a reference to 5: the reader resolves /Type as it decides whether 7 is an object stream
        // (#274), and cannot read 5 before 7. The loop is the fault, as one through /N is.
        var written = Encoding.Latin1.GetString(ObjectStreamWith(throughAReference ? "5 0 R" : "(four)"));
        var file = Encoding.Latin1.GetBytes(written.Replace("/Type /ObjStm", type, StringComparison.Ordinal));

        var report = Validate(file);

        Single(report, PdfValidationRuleIds.XRefObjectStreamCircular).Message.Should().Be(
            "Object stream 7 needs object 5, which it holds, to be read: its /Type names it, and the object cannot be read before the stream is.");
        report.Contains(PdfValidationRuleIds.XRefObjectStreamBroken).Should().BeFalse("the loop is the fault, reported once");
    }

    [Theory]
    [InlineData("/Type [5 0 R]", "(four)")]
    [InlineData("/Type 4 0 R  ", "<< /X 5 0 R >>")]
    public void An_object_stream_whose_type_holds_a_reference_inside_a_value_is_broken_not_circular(string type, string four)
    {
        // Stream 7's /Type is an array holding a reference to 5, or names object 4, a dictionary that does: the reader
        // resolves a /Type as a reference resolves — the object it names, while that is a reference in turn — and reads
        // nothing inside an array or a dictionary, so 5 is never needed. No /Type /ObjStm: the stream is broken.
        var written = Encoding.Latin1.GetString(ObjectStreamWith(four));
        var file = Encoding.Latin1.GetBytes(written.Replace("/Type /ObjStm", type, StringComparison.Ordinal));

        var report = Validate(file);

        Single(report, PdfValidationRuleIds.XRefObjectStreamBroken).Message.Should().EndWith("is a stream that is not of /Type /ObjStm.");
        report.Contains(PdfValidationRuleIds.XRefObjectStreamCircular).Should().BeFalse();
    }

    [Fact]
    public void An_object_stream_whose_count_is_an_array_holding_a_reference_it_holds_is_broken_not_circular()
    {
        // Stream 7's /N is an array that holds a reference to 5, which the stream holds: the reader reads no count from an
        // array, and needs nothing inside it; the stream has no /N, and is broken.
        var report = Validate(ObjectStream("/N [5 0 R]", "2", compress: false));

        Single(report, PdfValidationRuleIds.XRefObjectStreamBroken).Message.Should().EndWith("cannot be read: its /N or its /First is missing or negative.");
        report.Contains(PdfValidationRuleIds.XRefObjectStreamCircular).Should().BeFalse();
    }

    [Fact]
    public void A_stream_of_another_type_whose_length_it_holds_is_broken_not_circular()
    {
        // Stream 7 is not of /Type /ObjStm, and its /Length names object 5, which it would hold. The reader resolves the
        // /Length as it parses the stream, and reports that it needs 5; it then reads the stream's type, and nothing more.
        // The loop is not why its objects are lost — its type is —: the stream is broken, not circular.
        const string Data = "5 0 6 3 17 (six) ";
        var written = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, written.Object(1, "<< /Type /Catalog /Pages 2 0 R /Foo 6 0 R >>"), 0),
            [2] = (1, written.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"), 0),
            [7] = (1, written.Object(7, "<< /Type /XObject /N 2 /First 8 /Length 5 0 R >>\nstream\n" + Data + "\nendstream"), 0),
            [5] = (2, 7, 0),
            [6] = (2, 7, 1),
        };
        written.CrossReferenceStream(12, 13, rows, "/Root 1 0 R");
        var file = written.Bytes;

        var report = Validate(file);

        Single(report, PdfValidationRuleIds.XRefObjectStreamBroken).Message.Should().EndWith("is a stream that is not of /Type /ObjStm.");
        report.Contains(PdfValidationRuleIds.XRefObjectStreamCircular).Should().BeFalse();

        using var document = PdfDocument.Open(file);
        document.GetObject(new PdfObjectId(6)).Should().BeSameAs(PdfNull.Instance);
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Contain(PdfDiagnosticCodes.StreamSelfReference)
            .And.Contain(PdfDiagnosticCodes.ObjectStreamUnreadable);
    }

    [Fact]
    public void An_object_stream_whose_parameters_are_written_in_the_file_is_sound()
    {
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(3, "<< /Predictor 1 >>")
            .WithObject(5, "<< /Title (five) >>")
            .WithObject(6, "<< /Title (six) >>")
            .BuildWithXRefStream(rootNumber: 1, compressedObjects: [5, 6], compressObjectStream: true, objectStreamEntries: "/DecodeParms 3 0 R");

        Validate(file).Findings.Should().BeEmpty();
    }

    [Theory]
    [InlineData(6, 5)]
    [InlineData(5, 6)]
    public void An_object_an_object_stream_needs_is_read_once_the_stream_is_whichever_is_asked_first(int first, int second)
    {
        // #51: the null read while the stream was decoded is not kept.
        using var document = PdfDocument.Open(ObjectStream("/DecodeParms 5 0 R", "<< /Predictor 1 >>"));

        document.GetObject(new PdfObjectId(first)).Should().NotBeOfType<PdfNull>();
        document.GetObject(new PdfObjectId(second)).Should().NotBeOfType<PdfNull>();
        document.GetObject(new PdfObjectId(5)).AsDictionary()!.GetInteger(PdfName.Get("Predictor")).Should().Be(1);
        document.Diagnostics.Should().ContainSingle(diagnostic => diagnostic.Code == PdfDiagnosticCodes.StreamSelfReference)
            .Which.Message.Should().Be(
                "Object stream 7 needs object 5, which it holds, to be read: the object reads as null while the stream is decoded, and the stream is decoded without it.");
    }

    [Fact]
    public void An_object_stream_that_can_never_be_read_loses_its_objects_and_says_so_once()
    {
        using var document = PdfDocument.Open(ObjectStream("/N 5 0 R", "2", compress: false));

        document.GetObject(new PdfObjectId(6)).Should().BeOfType<PdfNull>();
        document.GetObject(new PdfObjectId(5)).Should().BeOfType<PdfNull>();
        document.Diagnostics.Where(diagnostic => diagnostic.Code == PdfDiagnosticCodes.StreamSelfReference).Should().ContainSingle();
    }

    [Fact]
    public void An_object_stream_whose_length_it_holds_is_read_once_its_dictionary_is()
    {
        // The stream's /Length names object 5, which the stream holds: it can only be found by looking for endstream.
        var file = ObjectStream("/Pad (0123456789)", "0", compress: false);
        var text = Encoding.Latin1.GetString(file);
        var dictionary = text.IndexOf("/Pad (0123456789) /Length ", StringComparison.Ordinal);
        var end = text.IndexOf(" >>", dictionary, StringComparison.Ordinal);
        var replaced = "/Length 5 0 R";
        text = text[..dictionary] + replaced.PadRight(end - dictionary) + text[end..];

        using var document = PdfDocument.Open(Encoding.Latin1.GetBytes(text));

        document.GetObject(new PdfObjectId(6)).AsDictionary()!.GetText(PdfName.Get("Title")).Should().Be("six");
        document.GetObject(new PdfObjectId(5)).Should().BeOfType<PdfInteger>("the null read while the stream's dictionary was read is not kept");
        document.Diagnostics.Should().ContainSingle(diagnostic => diagnostic.Code == PdfDiagnosticCodes.StreamSelfReference);
        Single(new PdfValidator().Validate(document), PdfValidationRuleIds.XRefObjectStreamCircular).Message.Should().Be(
            "Object stream 7 needs object 5, which it holds, to be read: its /Length names it, and the object cannot be read before the stream is.");

        // Asked for first, the stream is read without its length, and still serves its objects afterwards.
        using var streamFirst = PdfDocument.Open(Encoding.Latin1.GetBytes(text));
        streamFirst.GetObject(new PdfObjectId(7)).Should().BeOfType<PdfStream>();
        streamFirst.GetObject(new PdfObjectId(6)).AsDictionary()!.GetText(PdfName.Get("Title")).Should().Be("six");
        streamFirst.GetObject(new PdfObjectId(5)).Should().BeOfType<PdfInteger>();
    }

    [Fact]
    public void An_object_stream_that_needs_two_objects_it_holds_is_reported_once()
    {
        using var document = PdfDocument.Open(ObjectStream("/N 5 0 R /First 6 0 R", "2", compress: true));

        document.GetObject(new PdfObjectId(5));
        document.GetObject(new PdfObjectId(6));

        document.Diagnostics.Where(diagnostic => diagnostic.Code == PdfDiagnosticCodes.StreamSelfReference).Should().ContainSingle();
    }

    [Fact]
    public void An_object_stream_stored_in_another_that_needs_an_object_it_holds_is_reported_without_a_position()
    {
        // Object stream 10 is stored in object stream 11, whose /DecodeParms names object 5, which the index places in
        // 10: reading 5 decodes 11, which needs 5. Stream 10 has no offset of its own to report.
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(10, "<< /Type /ObjStm /N 1 /First 4 >>")
            .BuildWithXRefStream(rootNumber: 1, compressedObjects: [10], compressObjectStream: true, objectStreamEntries: "/DecodeParms 5 0 R");

        // The index's row for object 5, free as written, places it at index 0 of object stream 10.
        var text = Encoding.Latin1.GetString(file);
        var rows = text.IndexOf("stream\n", text.IndexOf("/Type /XRef", StringComparison.Ordinal), StringComparison.Ordinal) + "stream\n".Length;
        file[rows + (5 * 7)] = 2;
        file[rows + (5 * 7) + 4] = 10;

        using var document = PdfDocument.Open(file);

        document.GetObject(new PdfObjectId(5)).Should().BeSameAs(PdfNull.Instance);
        var diagnostic = document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.StreamSelfReference).Which;
        diagnostic.Message.Should().Be(
            "Object stream 10 needs object 5, which it holds, to be read: the object reads as null while the stream is decoded, and the stream is decoded without it.");
        diagnostic.Position.Should().Be(-1);
    }

    [Fact]
    public void A_parameter_array_naming_an_object_the_stream_holds_is_circular()
    {
        Single(Validate(ObjectStream("/DecodeParms [5 0 R]", "<< /Predictor 1 >>")), PdfValidationRuleIds.XRefObjectStreamCircular)
            .Message.Should().Be(
                "Object stream 7 needs object 5, which it holds, to be read: its /DecodeParms names it, and the object cannot be read before the stream is.");
    }

    [Fact]
    public void Two_object_streams_that_each_need_an_object_of_the_other_are_both_circular()
    {
        var report = Validate(TwoObjectStreams("/DecodeParms 6 0 R", "/DecodeParms 5 0 R"));

        report.Findings.Where(finding => finding.RuleId == PdfValidationRuleIds.XRefObjectStreamCircular)
            .Select(finding => finding.Message).Should().Equal(
                "Object stream 10 needs object 6, which object stream 11 holds, to be read: its /DecodeParms names it, and reading object stream 11 needs object stream 10 in turn.",
                "Object stream 11 needs object 5, which object stream 10 holds, to be read: its /DecodeParms names it, and reading object stream 10 needs object stream 11 in turn.");
    }

    [Fact]
    public void An_object_stream_that_needs_another_which_needs_nothing_is_sound()
    {
        Validate(TwoObjectStreams("/DecodeParms 6 0 R", string.Empty)).Findings.Should().BeEmpty();
    }

    [Fact]
    public void A_chain_of_object_streams_that_leads_elsewhere_is_not_circular()
    {
        // Stream 11 needs object 7, which the index places in an object stream the file lacks: the chain ends there.
        var report = Validate(TwoObjectStreams("/DecodeParms 6 0 R", "/DecodeParms 7 0 R", elsewhere: true));

        report.Contains(PdfValidationRuleIds.XRefObjectStreamCircular).Should().BeFalse();
        report.Contains(PdfValidationRuleIds.XRefObjectStreamBroken).Should().BeTrue("object 7's stream is not in the file");
    }

    [Fact]
    public void Object_streams_on_a_loop_are_circular_each_naming_its_first_need_that_leads_back_and_the_tail_into_the_loop_is_not()
    {
        // Stream 10 needs 20, which needs 50 and then 30; 30 needs 40, which needs 20; 50 needs nothing. 20, 30 and 40 lead
        // to one another, and each names its need on the next: 20 not its first, on 50, which leads nowhere. 10 leads into
        // the loop, which does not lead back to it.
        var circular = Validate(NeedingStreams([1, 1, 1, 1, 1], [[21], [51, 31], [41], [21], []]))
            .Findings.Where(finding => finding.RuleId == PdfValidationRuleIds.XRefObjectStreamCircular)
            .Select(finding => (finding.Location.Object, finding.Message));

        circular.Should().Equal(
            (new PdfObjectId(20), "Object stream 20 needs object 31, which object stream 30 holds, to be read: its /DecodeParms names it, and reading object stream 30 needs object stream 20 in turn."),
            (new PdfObjectId(30), "Object stream 30 needs object 41, which object stream 40 holds, to be read: its /DecodeParms names it, and reading object stream 40 needs object stream 30 in turn."),
            (new PdfObjectId(40), "Object stream 40 needs object 21, which object stream 20 holds, to be read: its /DecodeParms names it, and reading object stream 20 needs object stream 40 in turn."));
    }

    [Fact]
    public void Whatever_graph_object_streams_make_by_needing_one_another_the_circular_ones_and_what_each_names_are_those_a_search_from_each_need_finds()
    {
        // The walk looked for a way back to each stream with a search from each of its needs, in the order the reader needs
        // them, which cost the square of a chain's length (#168). It finds the streams that lead to one another at once, and
        // a stream is circular, naming the first of its needs that leads back, exactly as the search found. Over graphs of
        // up to eight object streams, each holding up to three objects and needing up to four, any of them its own.
        var graphs =
            from count in Gen.Choose(1, 8)
            from holds in Gen.ArrayOf(Gen.Choose(1, 3), count)
            from lengths in Gen.ArrayOf(Gen.Choose(0, 4), count)
            from picks in Gen.ArrayOf(Gen.Choose(0, holds.Sum() - 1), 4 * count)
            select (holds, lengths, picks);

        Check.One(PropertySettings, Prop.ForAll(graphs.ToArbitrary(), drawn =>
        {
            var (holds, lengths, picks) = drawn;
            var members = new List<(int Stream, int Object)>();

            for (var k = 0; k < holds.Length; k++)
            {
                for (var member = 1; member <= holds[k]; member++)
                {
                    members.Add((k, (10 * (k + 1)) + member));
                }
            }

            var needs = new int[holds.Length][];

            for (var k = 0; k < holds.Length; k++)
            {
                needs[k] = [.. picks.Skip(4 * k).Take(lengths[k]).Select(pick => members[pick].Object)];
            }

            using var document = PdfDocument.Open(NeedingStreams(holds, needs));
            var walk = ObjectStreamDependencies.Run(document);
            var expected = SearchedWayBack(needs, members);

            return walk.CircularStreams.SetEquals(expected.Select(found => found.Stream)) &&
                walk.Circular.Select(finding => finding.Message).SequenceEqual(expected.Select(found => found.Message));
        }));
    }

    [Theory]
    [MemberData(nameof(ValuesDecodingDoesNotRead))]
    public void A_value_decoding_does_not_read_is_needed_by_neither_the_reader_nor_the_walk(string shape)
    {
        // #277's table, and the shapes like it the walk took for loops: decoding reads a filter's names up to the first that
        // ends the chain, of /DecodeParms only what a Flate or LZW step reads — the element at its place, /EarlyChange,
        // /Predictor, and the rest only past a predictor of 1 —, and nothing else. The walk followed both keys whole, and
        // found a loop through what the reader never reads.
        var file = DecodingShapes[shape].Build();

        Validate(file).Contains(PdfValidationRuleIds.XRefObjectStreamCircular).Should().BeFalse();
        ReaderNeeds(file).Should().BeNull();
    }

    [Theory]
    [MemberData(nameof(ValuesDecodingReads))]
    public void A_value_decoding_reads_that_leads_to_an_object_the_stream_holds_is_circular_as_the_reader_finds(string shape, string key)
    {
        // What decoding reads, followed as decoding follows it: through objects written in the file, a parameter written as a
        // stream — outside Table 5 of ISO 32000-1 —, whose /Length its parse resolves, and an object another object stream
        // holds, whose value the reader reads on from. The walk stopped at a stream and at a member (#277).
        var file = DecodingShapes[shape].Build();

        var report = Validate(file);

        Single(report, PdfValidationRuleIds.XRefObjectStreamCircular).Message.Should().Be(HoldsFive(key));
        report.Contains(PdfValidationRuleIds.XRefObjectStreamBroken).Should().BeFalse("the loop is the fault, reported once");
        ReaderNeeds(file).Should().Be(5);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    public void Broken_object_streams_are_kept_whatever_the_room_for_their_rule_to_pass_over_the_circular_ones(int capacity)
    {
        // #168: streams 10, 20 and 30 need, for their /N, the object each holds: circular, and unreadable to the probe; the
        // index places objects in streams 71 to 73, which it lacks. The probe cannot tell the circular ones, which a walk
        // made later finds: it keeps a record of every broken stream, whatever the room, and the rule writes the findings of
        // the others while the report has room for them. Each is counted once, as circular or as broken.
        var file = ValidatorTests.ObjectStreamFaults();
        using var document = PdfDocument.Open(file);
        document.Reader.SettleIndex();

        CrossReferenceProbe.Run(document, room: 0).BrokenObjectStreams.Select(broken => (broken.Stream, broken.Kind)).Should().Equal(
            (10, BrokenObjectStreamKind.Unreadable),
            (20, BrokenObjectStreamKind.Unreadable),
            (30, BrokenObjectStreamKind.Unreadable),
            (71, BrokenObjectStreamKind.NotHeld),
            (72, BrokenObjectStreamKind.NotHeld),
            (73, BrokenObjectStreamKind.NotHeld));

        var report = new PdfValidator(new PdfValidatorOptions { FindingCapacity = capacity }).Validate(document);

        report.ErrorCount.Should().Be(6, "three streams circular and three broken");
        report.Contains(PdfValidationRuleIds.XRefObjectStreamBroken).Should().BeTrue();
        report.Findings.Select(finding => finding.Location.Object?.Number).Should().Equal(new int?[] { 10, 20, 30, 71 }.Take(capacity));
    }

    [Fact]
    public void Objects_referring_to_nothing_past_the_report_s_room_are_counted_and_not_read_again()
    {
        // #168: the graph read each object that refers to nothing again, once the walk was over, to say where in it the first
        // such reference lies, whatever the report's room. Two hundred such objects, under the smallest cache, 64 objects:
        // the walk reads them from the last to the first, which the cache keeps, and the graph describes them from the
        // first. It reads again those it describes, as many as the report has room for, of which those the cache let go
        // are read from the file: none of the first 64, and of a hundred less than half of what describing every one reads.
        const int Objects = 200;
        var file = HostileInputTests.ObjectsHolding(Objects, "/M 9999 0 R");

        var (none, unread) = Walk(room: 0);
        var (hundred, described) = Walk(room: 100);
        var (every, _) = Walk(room: int.MaxValue);

        unread.MissingReferences.Total.Should().Be(Objects);
        unread.MissingReferences.Kept.Should().Be(0);
        described.MissingReferences.Total.Should().Be(Objects);
        described.MissingReferences.Select(finding => finding.Location.Object?.Number).Should().Equal(Enumerable.Range(3, 100).Select(number => (int?)number));
        (hundred - none).Should().BePositive("the objects described the cache let go are read again");
        (hundred - none).Should().BeLessThan((every - none) / 2, "a hundred objects are described, not two hundred");

        (long Read, ObjectGraph Graph) Walk(int room)
        {
            using var source = new StrictCountingSource(file);
            using var document = PdfDocument.Open(source, new PdfReaderOptions { ObjectCacheCapacity = 64 }, ownsSource: false);
            var pages = PageTreeWalk.Run(document);
            var before = source.BytesRead;
            var graph = ObjectGraph.Run(document, pages, room);
            return (source.BytesRead - before, graph);
        }
    }

    [Fact]
    public void The_walk_keeps_as_many_objects_as_the_reader_caches_and_reads_again_one_it_let_go()
    {
        // A hundred Flate object streams take their parameters each from an object of its own, written in the file, whose
        // predictor is object 4, shared; the last stream's predictor is the object it holds. Opened with a cache of 64
        // objects, the walk keeps 64 of what it read, lets go of object 4 long before the last stream, and reads it again
        // for the streams after: the last stream alone is circular, and the walk reads more of the file than under a cache
        // of a thousand objects, which keeps all it read. A walk that let nothing go read the same under both (#201).
        const int Streams = 100;
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"), 0),
            [2] = (1, file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"), 0),
            [4] = (1, file.Object(4, "1"), 0),
        };

        for (var k = 0; k < Streams; k++)
        {
            var (stream, member) = (2000 + (2 * k), 2001 + (2 * k));
            var predictor = k + 1 < Streams ? "4 0 R" : string.Create(CultureInfo.InvariantCulture, $"{member} 0 R");
            rows[1000 + k] = (1, file.Object(1000 + k, $"<< /Predictor {predictor} >>"), 0);
            rows[stream] = (1, FlateObjectStream(file, stream, [(member, "1")], string.Create(CultureInfo.InvariantCulture, $"/DecodeParms {1000 + k} 0 R")), 0);
            rows[member] = (2, stream, 0);
        }

        file.CrossReferenceStream(3000, 3001, rows, "/Root 1 0 R");

        var (letGo, found) = Walk(64);
        var (keptAll, _) = Walk(1000);

        found.Circular.Select(finding => finding.Message).Should().Equal(
            "Object stream 2198 needs object 2199, which it holds, to be read: its /DecodeParms names it, and the object cannot be read before the stream is.");
        letGo.Should().BeGreaterThan(keptAll, "the walk reads object 4 again once it let go of it");

        (long Read, ObjectStreamDependencies Found) Walk(int cache)
        {
            using var source = new StrictCountingSource(file.Bytes);
            using var document = PdfDocument.Open(source, new PdfReaderOptions { ObjectCacheCapacity = cache }, ownsSource: false);
            document.Reader.SettleIndex();
            var before = source.BytesRead;
            var dependencies = ObjectStreamDependencies.Run(document);
            return (source.BytesRead - before, dependencies);
        }
    }

    [Theory]
    [InlineData("met a level lower first, then higher up")]
    [InlineData("taken a level lower, as far as the reader's nesting reaches")]
    [InlineData("taken a level lower, short of the first stream past the reader's nesting")]
    [InlineData("taken, its streams parsed")]
    [InlineData("taken up to a stream the walk parsed already")]
    [InlineData("taken short inside a parse, which gives no nest")]
    [InlineData("taken where the reader's nesting cut it, inside a parse")]
    [InlineData("met past a stream the walk parsed already")]
    [InlineData("met past an object of an object stream")]
    [InlineData("met past a run through objects of object streams")]
    public void A_nest_the_walk_keeps_is_taken_only_where_the_parse_it_stands_for_would_find_what_it_found(string shape)
    {
        // walk:1 of #277's review: the parse of a stream met on a chain parses the stream its /Length loads, a level deeper,
        // and so on to the reader's deepest load. The walk keeps the nest of streams a parse went through when it met nothing
        // its own state answers — no object of an object stream, no stream parsed before —, and takes it at once where the
        // stream is met again at that depth or below it, or at any depth when the parse ended where its chains do; the
        // streams it parsed are parsed, up to one parsed already. Each shape tells a nest taken from a parse followed where
        // either finds an object stream circular and the other does not: met higher up, a parse reaches a stream further, whose
        // /Length names an object of the stream walked; a stream parsed is not parsed again, from higher up either. The
        // verdict is the one the walk gives with no nest kept.
        var (file, circular) = NestShapes[shape];
        using var document = PdfDocument.Open(file());

        ObjectStreamDependencies.Run(document).CircularStreams.Should().BeEquivalentTo(circular);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_nest_whose_parse_ended_where_its_chains_do_is_taken_from_higher_up(bool holdingOneTaken)
    {
        // walk:1 of #277's review: object stream 10 meets stream 400 under its /Length, at the third level, and 12 meets 500
        // under its /N, at the second; each heads a nest of twenty streams leading into a nest of eight, of 100 and of 600,
        // whose last takes its /Length from a direct 2 — nests 6 met first, at the second level, when 400's holds a nest taken
        // whole. 14 meets ten other such nests of eight, which, under the smallest cache, 64 objects, let go of every stream
        // of 400's and 500's, the reader as well. 16 meets 400, or 500, at the second level: 400's parse ended where its chains
        // do, short of the reader's nesting, and from higher up it parses the same streams; its nest is taken at once, as
        // 500's is, the walk reading the stream heading it, which the reader parses with the streams it loads for its
        // /Length. Followed again, the parse would read the twenty streams besides: two thirds more.
        const int Heads = 10;
        var written = Merged(StreamChain(100, 9, "2"), StreamChain(600, 9, "2"));
        written = Merged(written, Merged(StreamChain(400, 20, "100 0 R"), StreamChain(500, 20, "600 0 R")));
        var heads = new StringBuilder();

        for (var head = 0; head < Heads; head++)
        {
            written = Merged(written, StreamChain(200 + (10 * head), 9, "2"));
            heads.Append(CultureInfo.InvariantCulture, $"{200 + (10 * head)} 0 R ");
        }

        var none = Walk("<< >>");
        var lowerFirst = Walk("400 0 R");
        var higherFirst = Walk("500 0 R");

        (lowerFirst - none).Should().BeLessThan((higherFirst - none) * 5 / 4, "the nest is taken at once, as one found at the second level is");

        long Walk(string parameters)
        {
            var file = PlainObjectStreams(
                written,
                new(6, holdingOneTaken ? "/Filter [/FlateDecode /FlateDecode] /DecodeParms [100 0 R 600 0 R]" : string.Empty, [(7, "<< >>")]),
                new(10, string.Empty, [(11, "<< >>")], Length: "400 0 R"),
                new(12, string.Empty, [(13, "<< >>")], Count: "500 0 R"),
                new(14, $"/Filter [{string.Concat(Enumerable.Repeat("/FlateDecode ", Heads))}] /DecodeParms [{heads}]", [(15, "<< >>")]),
                new(16, $"/Filter /FlateDecode /DecodeParms {parameters}", [(17, "<< >>")]));
            using var source = new StrictCountingSource(file);
            using var document = PdfDocument.Open(source, new PdfReaderOptions { ObjectCacheCapacity = 64 }, ownsSource: false);
            document.Reader.SettleIndex();
            var before = source.BytesRead;
            ObjectStreamDependencies.Run(document).CircularStreams.Should().BeEmpty();
            return source.BytesRead - before;
        }
    }

    [Fact]
    public void A_nest_found_again_from_higher_up_takes_the_place_of_the_one_kept_and_lets_go_of_no_other()
    {
        // walk:1 of #277's review: object stream 10 meets stream 100 under its /DecodeParms; 12 meets 200 under its /Length,
        // at the third level, and 62 more under its /DecodeParms, each heading a nest of eight streams; 200 heads a nest of 62
        // streams, which its parse from the third level follows to the reader's deepest load, where it is cut. Opened with the
        // smallest cache, 64 objects, the walk keeps the 64 nests. 14 meets 200 at the second level, where its parse reaches
        // further: the nest found takes the place of the one kept, and the walk lets go of no other — 16 meets 100, or the
        // first of the 62, and takes either nest at once.
        const int Heads = 62;
        var written = Merged(StreamChain(100, 9, "2"), StreamChain(200, 63, "2"));
        var heads = new StringBuilder();

        for (var head = 0; head < Heads; head++)
        {
            written = Merged(written, StreamChain(1000 + (10 * head), 9, "2"));
            heads.Append(CultureInfo.InvariantCulture, $"{1000 + (10 * head)} 0 R ");
        }

        var none = Walk("<< >>");
        var first = Walk("100 0 R");
        var later = Walk("1000 0 R");

        (first - none).Should().BeLessThan((later - none) * 5 / 4, "the walk keeps the first nest it found, as it keeps the later");

        long Walk(string parameters)
        {
            var file = PlainObjectStreams(
                written,
                new(10, "/Filter /FlateDecode /DecodeParms 100 0 R", [(11, "<< >>")]),
                new(12, $"/Filter [{string.Concat(Enumerable.Repeat("/FlateDecode ", Heads))}] /DecodeParms [{heads}]", [(13, "<< >>")], Length: "200 0 R"),
                new(14, "/Filter /FlateDecode /DecodeParms 200 0 R", [(15, "<< >>")]),
                new(16, $"/Filter /FlateDecode /DecodeParms {parameters}", [(17, "<< >>")]));
            using var source = new StrictCountingSource(file);
            using var document = PdfDocument.Open(source, new PdfReaderOptions { ObjectCacheCapacity = 64 }, ownsSource: false);
            document.Reader.SettleIndex();
            var before = source.BytesRead;
            ObjectStreamDependencies.Run(document).CircularStreams.Should().BeEmpty();
            return source.BytesRead - before;
        }
    }

    [Fact]
    public void The_walk_keeps_as_many_nests_as_the_reader_caches_and_parses_again_one_it_let_go()
    {
        // walk:1 of #277's review: object stream 10 takes 64 Flate steps' parameters from 64 streams, each heading a nest of
        // eight streams, each taking its /Length from an object naming the next, the last from a direct 2; 12 two more such.
        // Opened with the smallest cache, 64 objects, the walk keeps 64 nests, and lets go of the first two as it finds the
        // last two. 14 meets the second of them, whose parse it follows again, reading every object of the nest; or the
        // third, whose nest it takes, reading the stream heading it, which the reader parses with what it loads for its
        // /Length: about half as much. Its objects the walk let go of either way.
        const int Heads = 66;
        var written = new Dictionary<int, string>();
        var (first, last) = (new StringBuilder(), new StringBuilder());

        for (var head = 0; head < Heads; head++)
        {
            written = Merged(written, LinkedStreamChain(100 + (20 * head), 9, "2"));
            (head < 64 ? first : last).Append(CultureInfo.InvariantCulture, $"{100 + (20 * head)} 0 R ");
        }

        var none = Walk("<< >>");
        var letGo = Walk("120 0 R");
        var kept = Walk("140 0 R");

        (letGo - none).Should().BeGreaterThan((kept - none) * 3 / 2, "a nest let go is followed again, one kept taken");

        long Walk(string parameters)
        {
            var file = PlainObjectStreams(
                written,
                new(10, $"/Filter [{string.Concat(Enumerable.Repeat("/FlateDecode ", 64))}] /DecodeParms [{first}]", [(11, "<< >>")]),
                new(12, $"/Filter [/FlateDecode /FlateDecode] /DecodeParms [{last}]", [(13, "<< >>")]),
                new(14, $"/Filter /FlateDecode /DecodeParms {parameters}", [(15, "<< >>")]));
            using var source = new StrictCountingSource(file);
            using var document = PdfDocument.Open(source, new PdfReaderOptions { ObjectCacheCapacity = 64 }, ownsSource: false);
            document.Reader.SettleIndex();
            var before = source.BytesRead;
            ObjectStreamDependencies.Run(document).CircularStreams.Should().BeEmpty();
            return source.BytesRead - before;
        }
    }

    [Theory]
    [InlineData("/Length", "/Length 100 0 R, 100 a stream whose /Length names 5")]
    [InlineData("/N", "/N 100 0 R, 100 a stream whose /Length names 5")]
    [InlineData("/First", "/First 100 0 R, 100 a stream whose /Length names 5")]
    [InlineData("/Type", "/Type 100 0 R, 100 a stream whose /Length names 5")]
    [InlineData("/Length", "/Length 100 0 R, 100 naming 101, a stream whose /Length names 5")]
    [InlineData("/Length", "/Length 100 0 R, 100 a stream whose /Length names 101, a stream whose /Length names 5")]
    [InlineData("/Length", "/Length 100 0 R, 100 a member of 9 naming 5")]
    [InlineData("/N", "/N and /First 100 0 R, 100 a stream whose /Length names 5")]
    public void A_stream_or_a_member_on_the_chain_of_a_key_the_reader_resolves_is_followed(string key, string shape)
    {
        // #277's comment: a link of the chain under /Length, /N, /First or /Type that is a stream is parsed, and its parse
        // resolves its /Length, which leads to 5; one another object stream holds is read from it, and its value read on.
        // The reader reports the loop; the walk stopped there, and found the stream broken, or nothing.
        var file = KeyShapes[shape].Build();

        var report = Validate(file);

        Single(report, PdfValidationRuleIds.XRefObjectStreamCircular).Message.Should().Be(HoldsFive(key));
        report.Contains(PdfValidationRuleIds.XRefObjectStreamBroken).Should().BeFalse("the loop is the fault, reported once");
        ReaderNeeds(file).Should().Be(5);
    }

    [Theory]
    [InlineData("0", null, false, false)]
    [InlineData("2.5", null, false, true)]
    [InlineData("2", "-1", false, true)]
    [InlineData("2", "0", true, false)]
    public void An_object_stream_needs_what_its_filters_name_only_when_its_count_and_start_let_it_decode(
        string count, string? first, bool circular, bool broken)
    {
        // The reader reads a stream's /N and /First before its filters, and decodes nothing when they declare no object, or
        // none it can read — a count that is no integer, a start below 0 —: /DecodeParms 5 0 R is never read. With /N 2.5,
        // the walk found a loop, which hid the true fault. A start of 0 is one the reader decodes from.
        var file = new StreamSeven("/Filter /FlateDecode /DecodeParms 5 0 R") { Five = "<< /Predictor 1 >>", Count = count, First = first }.Build();

        var report = Validate(file);

        report.Contains(PdfValidationRuleIds.XRefObjectStreamCircular).Should().Be(circular);
        report.Contains(PdfValidationRuleIds.XRefObjectStreamBroken).Should().Be(broken);
        ReaderNeeds(file).Should().Be(circular ? 5 : null);
    }

    [Theory]
    [InlineData(31, true)]
    [InlineData(32, false)]
    public void A_count_at_the_end_of_a_chain_is_read_only_where_a_reference_takes_its_value(int links, bool circular)
    {
        // Stream 7's /N names the first of a chain of objects, each naming the next, the last 2. A reference takes the value
        // of the 31st object of a chain, and drops that of the 32nd it loads (#173): one link further, the count is null, and
        // the stream is not decoded.
        var file = new StreamSeven("/Filter /FlateDecode /DecodeParms 5 0 R")
        {
            Five = "<< /Predictor 1 >>",
            Count = "100 0 R",
            Written = ReferenceChain(100, links, "2"),
        }.Build();

        Validate(file).Contains(PdfValidationRuleIds.XRefObjectStreamCircular).Should().Be(circular);
        ReaderNeeds(file).Should().Be(circular ? 5 : null);
    }

    [Theory]
    [InlineData("/N 6 0 R, /Filter [/FlateDecode 5 0 R]", 6, "/N")]
    [InlineData("/First 6 0 R, a predictor 5 0 R", 6, "/First")]
    [InlineData("a predictor 5 0 R, of 12, and a /Columns 6 0 R", 5, "/DecodeParms")]
    [InlineData("two Flate steps, /DecodeParms [5 0 R 6 0 R]", 5, "/DecodeParms")]
    public void Of_the_objects_an_object_stream_holds_and_needs_the_one_the_reader_needs_first_is_named(string shape, int named, string key)
    {
        // The reader reads /N and /First before the filters, and a step's parameters in their order; a predictor the stream
        // holds reads as null, which is a predictor of 1, past which nothing more is read. The walk followed /Filter and
        // /DecodeParms before /N and /First, and named another object than the reader does.
        var file = OrderShapes[shape].Build();

        Single(Validate(file), PdfValidationRuleIds.XRefObjectStreamCircular).Message.Should().Be(
            $"Object stream 7 needs object {named}, which it holds, to be read: its {key} names it, and the object cannot be read before the stream is.");
        ReaderNeeds(file).Should().Be(named);
    }

    [Theory]
    [InlineData("/Length", 33, true)]
    [InlineData("/Length", 34, false)]
    [InlineData("/N", 32, true)]
    [InlineData("/N", 33, false)]
    [InlineData("/DecodeParms", 32, true)]
    [InlineData("/DecodeParms", 33, false)]
    [InlineData("the /Length of a stream /N names", 33, true)]
    [InlineData("the /Length of a stream /N names", 34, false)]
    public void An_object_on_a_chain_is_followed_as_far_as_the_reader_loads_the_chain(string key, int at, bool circular)
    {
        // A reference resolves through 32 objects, a /Length through 33; the last is loaded, and its value dropped (#173).
        // Under /Length and /N, the object at the end is a stream whose /Length names 5, which its parse loads; under
        // /DecodeParms, or the /Length of a stream met on the way, it is 5, which the stream holds. One object further, the
        // reader loads nothing of it.
        const string Stream = "<< /Length 5 0 R >>\nstream\nab\nendstream";
        var lengthChain = ReferenceChain(110, at - 1, "5 0 R");
        lengthChain[100] = "<< /Length 110 0 R >>\nstream\nab\nendstream";
        var shape = key switch
        {
            "/Length" => new StreamSeven(string.Empty) { Five = "2", Data = SevenData.Plain, Length = "100 0 R", Written = ReferenceChain(100, at, Stream) },
            "/N" => new StreamSeven(string.Empty) { Five = "2", Data = SevenData.Plain, Count = "100 0 R", Written = ReferenceChain(100, at, Stream) },
            "/DecodeParms" => new StreamSeven("/Filter /FlateDecode /DecodeParms 100 0 R") { Five = "<< /Predictor 1 >>", Written = ReferenceChain(100, at - 1, "5 0 R") },
            _ => new StreamSeven(string.Empty) { Five = "2", Data = SevenData.Plain, Count = "100 0 R", Written = lengthChain },
        };
        var file = shape.Build();

        Validate(file).Contains(PdfValidationRuleIds.XRefObjectStreamCircular).Should().Be(circular);
        ReaderNeeds(file).Should().Be(circular ? 5 : null);
    }

    [Theory]
    [InlineData("/Length", 61, "5", true)]
    [InlineData("/Length", 62, "5", true)]
    [InlineData("/Length", 63, "5", false)]
    [InlineData("/N", 62, "5", true)]
    [InlineData("/N", 63, "5", true)]
    [InlineData("/N", 64, "5", false)]
    [InlineData("/N", 61, "a member of 9", true)]
    [InlineData("/N", 62, "a member of 9", false)]
    [InlineData("/N", 62, "an object", true)]
    [InlineData("/N", 63, "an object", false)]
    [InlineData("/Length and /N", 61, "a member of 9", false)]
    public void Streams_nested_on_a_chain_are_followed_as_deep_as_the_reader_nests_its_loads(string key, int streams, string last, bool circular)
    {
        // The key names the first of a chain of streams, each taking its /Length from the next, the last from 5 — or from
        // object 170, a member of object stream 9 or an object written in the file, whose value is 5 0 R. A read of 6 loads
        // the stream a level below it, its /Length a level further, its /N where the stream was; each nested stream a level
        // deeper; and nothing past 64 levels, so that 170 reads as null at the 65th, a member at the 64th already, its stream
        // loaded a level deeper. 5 itself is reported at any depth, as the stream holds it. A chain of streams /Length met
        // first, a level deeper than /N meets it, is parsed there, and kept: /N reads it again, and loads nothing more.
        var chain = StreamChain(100, streams, last == "5" ? "5 0 R" : "170 0 R");
        Dictionary<int, string> nine = [];

        if (last == "a member of 9")
        {
            nine[170] = "5 0 R";
        }
        else if (last == "an object")
        {
            chain[170] = "5 0 R";
        }

        var file = new StreamSeven(string.Empty)
        {
            Five = "2",
            Data = SevenData.Plain,
            Length = key.StartsWith("/Length", StringComparison.Ordinal) ? "100 0 R" : null,
            Count = key.EndsWith("/N", StringComparison.Ordinal) ? "100 0 R" : "2",
            Written = chain,
            InNine = nine,
        }.Build();

        Validate(file).Contains(PdfValidationRuleIds.XRefObjectStreamCircular).Should().Be(circular);
        ReaderNeeds(file).Should().Be(circular ? 5 : null);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(1, false)]
    [InlineData(31, true)]
    [InlineData(31, false)]
    public void A_value_a_guard_cut_ends_what_the_walk_follows_as_it_ends_decoding(int at, bool guarded)
    {
        // Two Flate steps take their parameters from [100 0 R 5 0 R]; 100 is the first of a chain of references to
        // parameters that run past MaxObjectLength when it is lowered to 128 bytes, the chain's 1st object or its 31st, the
        // last whose value a reference takes, which decoding asks whether a guard cut. Decoding ends at the first step's
        // parameters, which are the guard's, and never reads 5 (#263): the walk found a loop through 5 all the same.
        // Unguarded, the second step reads 5, which the stream holds.
        var file = new StreamSeven("/Filter [/FlateDecode /FlateDecode] /DecodeParms [100 0 R 5 0 R]")
        {
            Five = "<< /Predictor 1 >>",
            Data = SevenData.FlateTwice,
            Written = ReferenceChain(100, at, "<< /Predictor 1 /Pad (" + new string('x', 200) + ") >>"),
        }.Build();
        var options = guarded ? new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = 128 } } : null;

        using var document = PdfDocument.Open(file, options);
        new PdfValidator().Validate(document).Contains(PdfValidationRuleIds.XRefObjectStreamCircular).Should().Be(!guarded);
        ReaderNeeds(file, options).Should().Be(guarded ? null : 5);
    }

    [Theory]
    [InlineData("/Filter 3 0 R, 3 = [5 0 R]", "/Filter")]
    [InlineData("Flate, /DecodeParms 3 0 R, 3 = [5 0 R]", "/DecodeParms")]
    public void An_array_written_in_the_file_is_followed_whole_when_no_guard_bounds_the_chain_of_filters(string shape, string key)
    {
        // What the walk keeps of an array is as many elements as a chain of filters reads, and one more. With no bound on
        // the chain, that bound plus one overflowed, and every such array was kept empty: the loop through its element went
        // unseen, while the reader reported it.
        var file = shape.StartsWith("/Filter", StringComparison.Ordinal)
            ? new StreamSeven("/Filter 3 0 R") { Five = "/FlateDecode", Written = new Dictionary<int, string> { [3] = "[5 0 R]" } }.Build()
            : new StreamSeven("/Filter /FlateDecode /DecodeParms 3 0 R") { Five = "<< /Predictor 1 >>", Written = new Dictionary<int, string> { [3] = "[5 0 R]" } }.Build();
        var unbounded = new PdfReaderOptions { Limits = PdfReaderLimits.Unbounded };

        using var document = PdfDocument.Open(file, unbounded);
        Single(new PdfValidator().Validate(document), PdfValidationRuleIds.XRefObjectStreamCircular).Message.Should().Be(HoldsFive(key));
        ReaderNeeds(file, unbounded).Should().Be(5);
    }

    [Fact]
    public void An_array_written_in_the_file_is_kept_as_far_as_the_guard_on_filters_lets_decoding_read_it()
    {
        // Stream 7's /Filter is object 3, [/FlateDecode 5 0 R /ASCIIHexDecode], and the reader decodes through two filters
        // at most: it reads the second, 5, which the stream holds, and stops at the third. The walk keeps of the array the
        // two elements decoding reads.
        var file = new StreamSeven("/Filter 3 0 R")
        {
            Five = "/FlateDecode",
            Data = SevenData.FlateTwice,
            Written = new() { [3] = "[/FlateDecode 5 0 R /ASCIIHexDecode]" },
        }.Build();
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxFilterChainLength = 2 } };

        using var document = PdfDocument.Open(file, options);
        Single(new PdfValidator().Validate(document), PdfValidationRuleIds.XRefObjectStreamCircular).Message.Should().Be(HoldsFive("/Filter"));
        ReaderNeeds(file, options).Should().Be(5);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Two_object_streams_are_circular_through_their_parameters_only_where_decoding_reads_them(bool nineFiltered)
    {
        // Stream 7 takes its parameters from object 100, which object stream 9 holds; 9 names object 6, which 7 holds, as its
        // /DecodeParms. Compressed, 9 reads them: each stream needs the other, and each names its own first need. With no
        // filter, 9 never reads them, and neither stream needs the other — the walk found both circular.
        var file = new StreamSeven("/Filter /FlateDecode /DecodeParms 100 0 R")
        {
            InNine = new Dictionary<int, string> { [100] = "<< /Predictor 1 >>" },
            NineEntries = nineFiltered ? "/Filter /FlateDecode /DecodeParms 6 0 R" : "/DecodeParms 6 0 R",
        }.Build();

        var circular = Validate(file).Findings.Where(finding => finding.RuleId == PdfValidationRuleIds.XRefObjectStreamCircular).Select(finding => finding.Message);

        circular.Should().Equal(
            nineFiltered
                ?
                [
                    "Object stream 7 needs object 100, which object stream 9 holds, to be read: its /DecodeParms names it, and reading object stream 9 needs object stream 7 in turn.",
                    "Object stream 9 needs object 6, which object stream 7 holds, to be read: its /DecodeParms names it, and reading object stream 7 needs object stream 9 in turn.",
                ]
                : []);
    }

    [Theory]
    [InlineData("the object stream")]
    [InlineData("a stream its /N names")]
    public void A_stream_whose_length_names_the_stream_itself_reads_as_null_there_and_needs_nothing(string stream)
    {
        // The reader refuses to load again an object it is loading: a stream whose /Length names it reads as null there,
        // and its data is found by its endstream. Nothing the stream holds is needed on the way.
        var shape = stream == "the object stream"
            ? new StreamSeven(string.Empty) { Data = SevenData.Plain, Length = "7 0 R" }
            : new StreamSeven(string.Empty) { Data = SevenData.Plain, Count = "100 0 R", Written = StreamChain(100, 1, "100 0 R") };
        var file = shape.Build();

        Validate(file).Contains(PdfValidationRuleIds.XRefObjectStreamCircular).Should().BeFalse();
        ReaderNeeds(file).Should().BeNull();
    }

    [Fact]
    public void An_object_of_a_stream_met_on_a_chain_while_its_length_is_resolved_is_not_needed_by_the_stream_walked()
    {
        // Stream 7's /N names object stream 9, whose /Length names object 170, which 9 holds; 9's /DecodeParms names 6,
        // which 7 holds. Reading 6 parses 9 for 7's /N: 170 cannot be read while 9 is, and reads as null before 9 is decoded
        // — the reader reports 9, not 7, which needs nothing of 9. Only 9 is circular: taking 170 for a need of 7 made 7
        // circular through 9.
        var file = new StreamSeven(string.Empty)
        {
            Data = SevenData.Plain,
            Count = "9 0 R",
            InNine = new() { [170] = "<< /Predictor 1 >>" },
            NineEntries = "/Filter /FlateDecode /DecodeParms 6 0 R",
            NineLength = "170 0 R",
        }.Build();

        Validate(file).Findings.Where(finding => finding.RuleId == PdfValidationRuleIds.XRefObjectStreamCircular)
            .Select(finding => finding.Message).Should().Equal(
                "Object stream 9 needs object 170, which it holds, to be read: its /Length names it, and the object cannot be read before the stream is.");
        ReaderNeeds(file).Should().BeNull();
    }

    [Fact]
    public void Decoding_reads_no_entry_of_a_step_s_parameters_the_walk_does_not_keep()
    {
        // The walk keeps of a parameters dictionary written in the file the entries FilterChain.ParameterKeys lists, and
        // reads nothing else of it: an entry decoding came to read besides would be a need the walk could not see. An LZW
        // step and a Flate step, a predictor past 1, and every entry ISO 32000-1 gives a filter's parameters besides.
        string[] names =
        [
            "EarlyChange", "Predictor", "Colors", "BitsPerComponent", "Columns", "K", "EndOfLine", "EncodedByteAlign", "Rows",
            "EndOfBlock", "BlackIs1", "DamagedRowsBeforeError", "JBIG2Globals", "ColorTransform", "Type", "Name",
        ];
        var parameters = new PdfDictionary();
        var keys = new Dictionary<PdfObject, PdfName>(ReferenceEqualityComparer.Instance);

        // Each value an instance of its own, by which the entry read is known: a predictor of 12, the rest past the
        // integers the library shares instances of.
        for (var index = 0; index < names.Length; index++)
        {
            var value = PdfInteger.Create(names[index] == "Predictor" ? 12 : 1_000_000 + index);
            parameters.Set(PdfName.Get(names[index]), value);
            keys[value] = PdfName.Get(names[index]);
        }

        var dictionary = new PdfDictionary();
        dictionary.Set(PdfName.Filter, new PdfArray([PdfName.LZWDecode, PdfName.FlateDecode]));
        dictionary.Set(PdfName.DecodeParms, parameters);
        var reads = new ParameterReads(keys, []);

        FilterChain.TryFollow(dictionary, maxFilters: 64, ref reads).Should().BeTrue();

        reads.Read.Distinct().Should().BeEquivalentTo(FilterChain.ParameterKeys.ToArray(), "decoding reads every entry the walk keeps, and nothing else");
    }

    [Fact]
    public void A_stream_whose_parameters_lead_back_to_it_through_a_stream_is_circular_and_not_also_broken()
    {
        // #277's sixth shape with a predictor of 12: the stream's parameters are object 3, a stream whose /Predictor is 5,
        // which the stream holds. The reader decodes it with no predictor, 5 reading as null, and reads its objects; the probe
        // decoded it with 5's own value and found its header broken. Once the loop is found, the broken finding, which the
        // loop explains, is not made as well.
        var file = new StreamSeven("/Filter /FlateDecode /DecodeParms 3 0 R")
        {
            Five = "12",
            Written = new Dictionary<int, string> { [3] = "<< /Predictor 5 0 R /Length 2 >>\nstream\nab\nendstream" },
        }.Build();

        var report = Validate(file);

        Single(report, PdfValidationRuleIds.XRefObjectStreamCircular).Message.Should().Be(HoldsFive("/DecodeParms"));
        report.Contains(PdfValidationRuleIds.XRefObjectStreamBroken).Should().BeFalse("the loop is the fault, reported once");
        ReaderNeeds(file).Should().Be(5);

        using var document = PdfDocument.Open(file);
        document.GetObject(new PdfObjectId(6)).Should().BeOfType<PdfDictionary>("the reader decodes the stream without the predictor");
    }

    [Theory]
    [InlineData("nothing")]
    [InlineData("5")]
    [InlineData("6")]
    [InlineData("7")]
    [InlineData("8")]
    [InlineData("5 6 7")]
    [InlineData("7 6 5")]
    [InlineData("6 5 7")]
    [InlineData("a validation")]
    public void Object_streams_that_take_their_parameters_from_one_another_s_objects_are_judged_alike_whatever_was_read_before(string before)
    {
        // #277's review: stream 10 takes its parameters from 6, which 11 holds; 11 from 5, which 10 holds; 12 from 8, which
        // 10 holds. 11's data reads 6 as 7 0 R through the predictor 5 gives, and as 77 0 R without one, so that what the
        // reader serves of 6 depends on which of 10 and 11 it decoded first. The walk read 6 through the reader and followed
        // it to 7, which 12 holds: 12 was found circular after nothing was read, or 6, and not after 5, 7 or 8, though no read
        // of an object 10 holds decodes 12. Neither 10 nor 11 is self-contained, each meeting an object of the other: 6 and 5
        // are needs and read as null, 10 and 11 are circular, and 12, which leads into their loop, is not.
        using var document = PdfDocument.Open(ConvergenceShapes.ObjectStreamsTakingParametersFromOneAnother());

        if (before == "a validation")
        {
            new PdfValidator().Validate(document);
        }
        else if (before != "nothing")
        {
            foreach (var number in before.Split(' '))
            {
                document.GetObject(new PdfObjectId(int.Parse(number, CultureInfo.InvariantCulture)));
            }
        }

        new PdfValidator().Validate(document).Findings.Where(finding => finding.RuleId == PdfValidationRuleIds.XRefObjectStreamCircular)
            .Select(finding => finding.Message).Should().Equal(
                "Object stream 10 needs object 6, which object stream 11 holds, to be read: its /DecodeParms names it, and reading object stream 11 needs object stream 10 in turn.",
                "Object stream 11 needs object 5, which object stream 10 holds, to be read: its /DecodeParms names it, and reading object stream 10 needs object stream 11 in turn.");
    }

    [Theory]
    [InlineData("100, directly")]
    [InlineData("100, through 99, written in the file")]
    [InlineData("100, once stream 3 has read 170")]
    public void A_loop_through_the_value_of_an_object_whose_stream_needs_another_is_left_to_the_reader(string parameters)
    {
        // Stream 7 takes its parameters from 100, which 9 holds; 9 takes its own from 170, which 11 holds, and 11 needs
        // nothing; 100 is 5 0 R, which 7 holds. The reader decodes 9 within 7's decoding, 100 leads it to 5, and it reports
        // that 7 needs 5. 9 is not self-contained, meeting 170: the walk cannot vouch for what 9's decoding gives, and does
        // not follow 100, so it does not find 7 circular. That is the loop the walk leaves unseen, as #277's review decided:
        // filed as debt (#312). It stays unseen when 7 reaches 100 through 99, written in the file, which a run of links
        // would pass; and when an object stream the walk takes first, 3, has read 170, so that the walk knows 11 to be
        // self-contained and holds the run from 170 to 171: the walk of 9, which follows no object of an object stream, still
        // meets 170 there.
        var file = new HandWrittenFile(version: "1.5");
        var throughNinetyNine = parameters.Contains("99", StringComparison.Ordinal);
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"), 0),
            [2] = (1, file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"), 0),
            [7] = (1, FlateObjectStream(file, 7, [(5, "<< /Predictor 1 >>"), (6, "<< /Title (six) >>")], throughNinetyNine ? "/DecodeParms 99 0 R" : "/DecodeParms 100 0 R"), 0),
            [5] = (2, 7, 0),
            [6] = (2, 7, 1),
            [9] = (1, FlateObjectStream(file, 9, [(100, "5 0 R")], "/DecodeParms 170 0 R"), 0),
            [100] = (2, 9, 0),
            [11] = (1, FlateObjectStream(file, 11, [(170, "171 0 R")], string.Empty), 0),
            [170] = (2, 11, 0),
            [171] = (1, file.Object(171, "<< /Predictor 1 >>"), 0),
        };

        if (throughNinetyNine)
        {
            rows[99] = (1, file.Object(99, "100 0 R"), 0);
        }

        if (parameters.EndsWith("170", StringComparison.Ordinal))
        {
            rows[3] = (1, FlateObjectStream(file, 3, [(4, "<< >>")], "/DecodeParms 170 0 R"), 0);
            rows[4] = (2, 3, 0);
        }

        file.CrossReferenceStream(200, 201, rows, "/Root 1 0 R");

        Validate(file.Bytes).Contains(PdfValidationRuleIds.XRefObjectStreamCircular).Should().BeFalse();
        ReaderNeeds(file.Bytes).Should().Be(5);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_object_of_a_stream_the_chain_does_not_place_reads_as_null_whatever_a_rebuild_finds(bool rebuiltFirst)
    {
        // Stream 7 takes its parameters from 100, which the chain places in object stream 9; the chain holds 9 free, though
        // the file writes it, holding 100 as 5 0 R, which 7 holds. The row of stream 30 leads nowhere: reading it rebuilds the
        // index, which finds 9, and the reader then serves 100 from it. The walk judges the chain's index (#128): 9 is no
        // stream there, so 100 is a need of a stream that has no place, and reads as null, before a rebuild or after.
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"), 0),
            [2] = (1, file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"), 0),
            [7] = (1, FlateObjectStream(file, 7, [(5, "<< /Predictor 1 >>"), (6, "<< /Title (six) >>")], "/DecodeParms 100 0 R"), 0),
            [5] = (2, 7, 0),
            [6] = (2, 7, 1),
            [100] = (2, 9, 0),
            [30] = (1, 1, 0),
            [31] = (2, 30, 0),
        };
        FlateObjectStream(file, 9, [(100, "5 0 R")], string.Empty);
        file.CrossReferenceStream(200, 201, rows, "/Root 1 0 R");
        using var document = PdfDocument.Open(file.Bytes);

        if (rebuiltFirst)
        {
            document.GetObject(new PdfObjectId(30));
        }

        new PdfValidator().Validate(document).Contains(PdfValidationRuleIds.XRefObjectStreamCircular).Should().BeFalse();
        document.WasRepaired.Should().BeTrue();
        document.GetObject(new PdfObjectId(100)).Should().BeOfType<PdfReference>("the rebuilt index places 100 in 9, which the reader decodes");
    }

    [Fact]
    public void An_object_a_self_contained_stream_holds_is_read_from_it_though_the_file_writes_its_number_elsewhere()
    {
        // Stream 7 takes its parameters from 100, which self-contained stream 9 holds, << /Predictor 1 >>. The file also
        // writes an object 100 near its start, << /Predictor 5 0 R >>, which no row places. The reader serves 100 from 9, and
        // decoding 7 needs nothing of it: the walk reads 100 as the reader serves it, not where an object of its number lies.
        var file = new HandWrittenFile(version: "1.5");
        file.Object(100, "<< /Predictor 5 0 R >>");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"), 0),
            [2] = (1, file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"), 0),
            [7] = (1, FlateObjectStream(file, 7, [(5, "<< /Predictor 1 >>"), (6, "<< /Title (six) >>")], "/DecodeParms 100 0 R"), 0),
            [5] = (2, 7, 0),
            [6] = (2, 7, 1),
            [9] = (1, FlateObjectStream(file, 9, [(100, "<< /Predictor 1 >>")], string.Empty), 0),
            [100] = (2, 9, 0),
        };
        file.CrossReferenceStream(200, 201, rows, "/Root 1 0 R");

        Validate(file.Bytes).Contains(PdfValidationRuleIds.XRefObjectStreamCircular).Should().BeFalse();
        ReaderNeeds(file.Bytes).Should().BeNull();
    }

    [Theory]
    [InlineData("100 0 R", "Object stream 7 needs object 100, which object stream 9 holds, to be read: its /DecodeParms names it, and reading object stream 9 needs object stream 7 in turn.")]
    [InlineData("9 0 R", "Object stream 7 needs object 5, which it holds, to be read: its /DecodeParms names it, and the object cannot be read before the stream is.")]
    public void A_stream_of_another_type_whose_length_leads_back_makes_the_stream_whose_decoding_parses_it_circular_and_is_broken_itself(string parameters, string message)
    {
        // Stream 7 takes its parameters from 100, which 9 holds, or from 9 itself; 9 is of /Type /XObject and takes its
        // /Length from 5, which 7 holds. Decoding 7 parses 9, which needs 5, and the reader reports that 7 needs 5. The walk
        // dropped what 9's /Length needs, 9 not being an object stream (#274): a parameter naming 9 found the loop, one naming
        // 100 did not. What 9's /Length needs links it to 7, though it does not judge 9, whose objects are lost to its type.
        var file = new StreamSeven("/Filter /FlateDecode /DecodeParms " + parameters)
        {
            InNine = new() { [100] = "<< /Predictor 1 >>" },
            NineType = "/XObject",
            NineLength = "5 0 R",
        }.Build();

        var report = Validate(file);

        Single(report, PdfValidationRuleIds.XRefObjectStreamCircular).Message.Should().Be(message);
        Single(report, PdfValidationRuleIds.XRefObjectStreamBroken).Location.Object.Should().Be(new PdfObjectId(9));
        ReaderNeeds(file).Should().Be(5);
    }

    [Fact]
    public void Object_streams_whose_decoding_goes_through_the_length_of_a_stream_of_another_type_are_circular_and_it_is_broken()
    {
        // Stream 7 holds 5, a PNG predictor of 11 columns, and takes its parameters from 100, which 9 holds; 9 is of /Type
        // /XObject and takes its /Length from 200, which 11 holds; 11 takes its parameters from 5. 11's data reads 200 as 56
        // through the predictor and as 4 without one: read first, 200 is 56; once 6 is read, 4, 11 having been decoded while
        // 7 was. The walk dropped what 9's /Length needs, and found none of the three circular.
        var file = new HandWrittenFile(version: "1.5");
        var data = Deflate("\0" + "200 0 4 56 ");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"), 0),
            [2] = (1, file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"), 0),
            [7] = (1, FlateObjectStream(file, 7, [(5, "<< /Predictor 10 /Columns 11 >>"), (6, "<< /Title (six) >>")], "/DecodeParms 100 0 R"), 0),
            [5] = (2, 7, 0),
            [6] = (2, 7, 1),
            [9] = (1, file.Object(9, "<< /Type /XObject /N 1 /First 6 /Length 200 0 R >>\nstream\n100 0 << /Predictor 1 >>\nendstream"), 0),
            [100] = (2, 9, 0),
            [11] = (1, file.Object(11, $"<< /Type /ObjStm /N 1 /First 7 /Filter /FlateDecode /DecodeParms 5 0 R /Length {data.Length} >>\nstream\n{data}\nendstream"), 0),
            [200] = (2, 11, 0),
        };
        file.CrossReferenceStream(201, 202, rows, "/Root 1 0 R");

        var report = Validate(file.Bytes);

        report.Findings.Where(finding => finding.RuleId == PdfValidationRuleIds.XRefObjectStreamCircular).Select(finding => finding.Message).Should().Equal(
            "Object stream 7 needs object 100, which object stream 9 holds, to be read: its /DecodeParms names it, and reading object stream 9 needs object stream 7 in turn.",
            "Object stream 11 needs object 5, which object stream 7 holds, to be read: its /DecodeParms names it, and reading object stream 7 needs object stream 11 in turn.");
        Single(report, PdfValidationRuleIds.XRefObjectStreamBroken).Location.Object.Should().Be(new PdfObjectId(9));

        using (var fresh = PdfDocument.Open(file.Bytes))
        {
            fresh.GetObject(new PdfObjectId(200)).Should().BeOfType<PdfInteger>().Which.Value.Should().Be(56);
        }

        using var afterSix = PdfDocument.Open(file.Bytes);
        afterSix.GetObject(new PdfObjectId(6));
        afterSix.GetObject(new PdfObjectId(200)).Should().BeOfType<PdfInteger>().Which.Value.Should().Be(4, "the reader keeps what it decoded 11 to while 7 was in flight");
    }

    [Theory]
    [InlineData(61, true)]
    [InlineData(62, false)]
    public void A_run_of_objects_a_stream_holds_is_not_taken_where_the_reader_reads_them_as_null(int streams, bool circular)
    {
        // Stream 7's /N names the first of a chain of streams, each taking its /Length from the next, the last from 180,
        // written in the file, which names 170, which 9 holds and which names 5. Stream 3, which the walk takes first, reads
        // 180 as its parameters, through a chain the walk keeps whole from 180 to 170, 9 being self-contained. Past 61
        // streams, 170 is read at the 64th level, where its stream would be loaded at the 65th: it reads as null, as the
        // reader reads it, though the chain kept from 180 passes it.
        var file = new StreamSeven(string.Empty)
        {
            Five = "2",
            Data = SevenData.Plain,
            Count = "100 0 R",
            Written = Merged(StreamChain(100, streams, "180 0 R"), new() { [180] = "170 0 R" }),
            InNine = new() { [170] = "5 0 R", [171] = "/ObjStm" },
            ThreeEntries = "/Type 171 0 R /Filter /FlateDecode /DecodeParms 180 0 R",
        }.Build();

        Validate(file).Findings.Where(finding => finding.RuleId == PdfValidationRuleIds.XRefObjectStreamCircular).Select(finding => finding.Message)
            .Should().Equal(circular ? [HoldsFive("/N")] : []);
        ReaderNeeds(file).Should().Be(circular ? 5 : null);
    }

    [Theory]
    [InlineData("nothing", 30, false)]
    [InlineData("the broken stream", 30, false)]
    [InlineData("a validation", 30, false)]
    [InlineData("nothing", 3, false)]
    [InlineData("nothing", 30, true)]
    [InlineData("the broken stream", 30, true)]
    public void Object_streams_are_judged_where_the_chain_places_them_whatever_was_read_before(string before, int broken, bool throughObject)
    {
        // An object stream is not where its entry says: reading it rebuilds the index, which cannot find 7, whose header has
        // a comment in it, and places 8 in object stream 20. The rebuild runs before the walk — the caller reads the broken
        // stream, or a validation settles the index — or during it, as the walk reads the broken stream. The walk reads each
        // stream, and each object written directly in the file its keys lead through, where the chain's row places it, the
        // chain's index read again for each lookup (#128): 7 and 8 are circular, at the positions their rows give, whatever
        // was read before the walk. Once, a rebuild before the walk, or during it from a broken stream the walk read first,
        // 3, lost both; and with the broken stream read first, so did an object 7's key leads through, which the rebuild
        // cannot find either.
        var file = RebuiltAfterTwoObjectStreams(broken, throughObject);
        using var document = PdfDocument.Open(file);

        if (before == "the broken stream")
        {
            document.GetObject(new PdfObjectId(broken)).Should().Be(PdfNull.Instance);
        }
        else if (before == "a validation")
        {
            new PdfValidator().Validate(document);
        }

        var dependencies = ObjectStreamDependencies.Run(document);

        document.WasRepaired.Should().BeTrue("the caller, the validation's settling or the walk's own read of the broken stream rebuilt the index");
        dependencies.CircularStreams.Should().BeEquivalentTo([7, 8]);
        dependencies.Circular.Select(finding => finding.Location.Object).Should().Equal(new PdfObjectId(7), new PdfObjectId(8));
        dependencies.Circular.Select(finding => finding.Location.Position)
            .Should().Equal(PdfTemplate.OffsetOf(file, "7 0 %x"), PdfTemplate.OffsetOf(file, "8 0 %x"));
        dependencies.Circular.Select(finding => finding.Message).Should().Equal(
            "Object stream 7 needs object 5, which it holds, to be read: its /DecodeParms names it, and the object cannot be read before the stream is.",
            "Object stream 8 needs object 15, which it holds, to be read: its /DecodeParms names it, and the object cannot be read before the stream is.");
    }

    [Fact]
    public void An_object_stream_a_few_bytes_off_its_row_is_judged_as_the_chain_wrote_it_once_a_rebuild_redefines_it()
    {
        // Object stream 7, which needs object 5 it holds, starts three bytes before where its row says; a later definition of
        // 7 is a string. Reading the broken stream 30 rebuilds the index, which serves the string: the walk reads the
        // stream near where the chain's row places it, as a load finds an object a few bytes off (#128).
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"), 0),
            [2] = (1, file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"), 0),
            [7] = (1, FlateObjectStream(file, 7, [(5, "<< /Predictor 1 >>")], "/DecodeParms 5 0 R") + 3, 0),
            [5] = (2, 7, 0),
            [30] = (1, 9, 0),
            [31] = (2, 30, 0),
        };
        rows[6] = (1, file.Padding(6), 0);
        file.Object(7, "(later)");
        file.CrossReferenceStream(32, 33, rows, "/Root 1 0 R");
        using var document = PdfDocument.Open(file.Bytes);
        document.GetObject(new PdfObjectId(30)).Should().Be(PdfNull.Instance);
        document.WasRepaired.Should().BeTrue();

        var dependencies = ObjectStreamDependencies.Run(document);

        dependencies.Circular.Select(finding => finding.Message).Should().Equal(
            "Object stream 7 needs object 5, which it holds, to be read: its /DecodeParms names it, and the object cannot be read before the stream is.");
    }

    [Theory]
    [InlineData("/Type /Catalog /Names [/Ok /A#00]", "Object 1 holds the name /A#00, under /Names[1], and a name cannot contain a null character.")]
    [InlineData("/Type /Catalog /X << /Y 1 >> /Z [1 2] /Names [/A#00]", "Object 1 holds the name /A#00, under /Names[0], and a name cannot contain a null character.")]
    [InlineData("/Type /Catalog /AP << /D << /Of#00f 1 >> >>", "Object 1 holds the name /Of#00f, as a key under /AP/D, and a name cannot contain a null character.")]
    [InlineData("/Type /Catalog /AP << /D#0A#1B << /Of#00f#9B 1 >> >>", "Object 1 holds the name /Of#00f#9B, as a key under /AP/D#0A#1B, and a name cannot contain a null character.")]
    [InlineData("/Type /Catalog /A << /B << /C << /D << /E << /F << /G << /H << /I << /J#00 1 >> >> >> >> >> >> >> >> >>", "Object 1 holds the name /J#00, as a key under /A/B/C/D (1 step) /F/G/H/I, and a name cannot contain a null character.")]
    public void A_name_with_a_null_character_is_located_inside_its_object(string catalog, string message)
    {
        Single(Validate(PdfTemplate.SoundWith("/Type /Catalog", catalog)), PdfValidationRuleIds.ObjectNameNullCharacter).Message
            .Should().Be(message);
    }

    [Fact]
    public void A_name_with_a_null_character_under_a_prev_written_deeper_in_the_trailer_is_located_there()
    {
        // P1-04: only the trailer's own /Prev and /XRefStm name sections; one written in its /Info is a value as any other.
        var finding = Single(
            Validate(PdfTemplate.SoundWith("<< /Size 4 /Root 1 0 R >>", "<< /Size 4 /Root 1 0 R /Info << /Prev /A#00B >> >>")),
            PdfValidationRuleIds.ObjectNameNullCharacter);

        finding.Message.Should().Be("The trailer holds the name /A#00B, under /Info/Prev, and a name cannot contain a null character.");
    }

    [Theory]
    [InlineData("/O#0At#1B 9 0 R", "Object 1 refers to object 9 0 under /O#0At#1B, which the file lacks: the reference reads as null.")]
    [InlineData("/A << /B << /C << /D << /E << /F << /G << /H << /I << /J 9 0 R >> >> >> >> >> >> >> >> >>", "Object 1 refers to object 9 0 under /A/B/C/D (2 steps) /G/H/I/J, which the file lacks: the reference reads as null.")]
    public void A_path_to_a_missing_object_quotes_its_keys_escaped_and_keeps_its_ends(string entry, string message)
    {
        Single(Validate(PdfTemplate.SoundWith("/Type /Catalog", "/Type /Catalog " + entry)), PdfValidationRuleIds.ObjectReferenceMissing)
            .Message.Should().Be(message);
    }

    [Theory]
    [MemberData(nameof(RebuiltTrailers.Shapes), MemberType = typeof(RebuiltTrailers))]
    public void A_reference_in_a_trailer_only_a_rebuild_finds_is_located_at_the_newest_trailer_it_merged(string shape)
    {
        var (file, trailer) = RebuiltTrailers.Build(shape, "/Info 9 0 R");

        var finding = Single(Validate(file), PdfValidationRuleIds.ObjectReferenceMissing);

        finding.Location.Position.Should().Be(trailer);
        finding.Message.Should().Be("The trailer refers to object 9 0 under /Info, which the file lacks: the reference reads as null.");
    }

    [Fact]
    public void A_name_set_on_a_trailer_the_reader_merged_none_of_is_located_at_the_document()
    {
        // The file has no trailer: the rebuild recovers /Root alone, which nothing locates at the trailer. A value a caller
        // sets on the trailer has no trailer in the file to point at.
        var file = PdfTemplate.SoundWith("trailer\n<< /Size 4 /Root 1 0 R >>\nstartxref\n{xref:1}\n", string.Empty);
        using var document = PdfDocument.Open(file);
        document.Reader.TrailerLocation.Should().Be(-1);
        document.Trailer.Set(PdfName.Get("Kind"), PdfName.Get("B\0"));

        var finding = Single(new PdfValidator().Validate(document), PdfValidationRuleIds.ObjectNameNullCharacter);

        finding.Message.Should().StartWith("The trailer holds the name /B#00");
        finding.Location.IsDocument.Should().BeTrue();
    }

    [Fact]
    public void A_name_with_a_null_character_in_the_trailer_is_found_past_the_keys_naming_sections()
    {
        // The update's trailer gives /Prev and /XRefStm, the keys that name sections, before /Kind: the search passes
        // over them as the walk does, so the name it reports is the one under /Kind, not the one under /XRefStm.
        var file = PdfTemplate.Build(PdfTemplate.Sound + """
            4 0 obj
            (an update)
            endobj
            xref
            4 1
            {row:4}
            trailer
            << /Size 5 /Root 1 0 R /Prev {xref:1} /XRefStm /A#00 /Kind /B#00 >>
            startxref
            {xref:2}
            %%EOF

            """);

        var finding = Single(Validate(file), PdfValidationRuleIds.ObjectNameNullCharacter);

        finding.Location.Position.Should().Be(PdfTemplate.OffsetOf(file, "trailer", occurrence: 2));
        finding.Message.Should().Be("The trailer holds the name /B#00, under /Kind, and a name cannot contain a null character.");
    }

    [Fact]
    public void A_name_with_a_null_character_is_found_in_a_stream_s_dictionary_and_as_an_object_s_whole_value()
    {
        var template = PdfTemplate.Sound
            .Replace("/Resources << >>", "/Resources << >> /Contents 4 0 R /Thumb 5 0 R", StringComparison.Ordinal)
            .Replace("xref\n0 4\n", "4 0 obj\n<< /Length 0 /Fil#00ter /None >>\nstream\n\nendstream\nendobj\n5 0 obj\n/Na#00me\nendobj\nxref\n0 6\n", StringComparison.Ordinal)
            .Replace("{row:3}\n", "{row:3}\n{row:4}\n{row:5}\n", StringComparison.Ordinal)
            .Replace("/Size 4", "/Size 6", StringComparison.Ordinal);

        Validate(PdfTemplate.Build(template)).Findings
            .Where(finding => finding.RuleId == PdfValidationRuleIds.ObjectNameNullCharacter)
            .Select(finding => finding.Message).Should().Equal(
                "Object 4 holds the name /Fil#00ter, as a key of its dictionary, and a name cannot contain a null character.",
                "Object 5 holds the name /Na#00me, as its value, and a name cannot contain a null character.");
    }

    [Fact]
    public void A_reference_to_nothing_in_a_stream_s_dictionary_is_located_in_it()
    {
        var template = PdfTemplate.Sound
            .Replace("/Resources << >>", "/Resources << >> /Contents 4 0 R", StringComparison.Ordinal)
            .Replace("xref\n0 4\n", "4 0 obj\n<< /Length 0 /DecodeParms 9 0 R >>\nstream\n\nendstream\nendobj\nxref\n0 5\n", StringComparison.Ordinal)
            .Replace("{row:3}\n", "{row:3}\n{row:4}\n", StringComparison.Ordinal)
            .Replace("/Size 4", "/Size 5", StringComparison.Ordinal);

        Single(Validate(PdfTemplate.Build(template)), PdfValidationRuleIds.ObjectReferenceMissing).Message.Should().Be(
            "Object 4 refers to object 9 0 under /DecodeParms, which the file lacks: the reference reads as null.");
    }

    /// <summary>Object stream 7, uncompressed, holding object 5, /ObjStm, and 6; object 4 is <paramref name="four"/>, written in the file.</summary>
    private static byte[] ObjectStreamWith(string four) =>
        new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(4, four)
            .WithObject(5, "/ObjStm")
            .WithObject(6, "<< /Title (six) >>")
            .BuildWithXRefStream(rootNumber: 1, compressedObjects: [5, 6], compressObjectStream: false);

    /// <summary>Gives <paramref name="text"/> compressed with Flate, as text written in a file is.</summary>
    private static string Deflate(string text)
    {
        using var compressed = new MemoryStream();

        using (var zlib = new System.IO.Compression.ZLibStream(compressed, System.IO.Compression.CompressionLevel.Optimal))
        {
            zlib.Write(Encoding.Latin1.GetBytes(text));
        }

        return Encoding.Latin1.GetString(compressed.ToArray());
    }

    /// <summary>
    /// Writes object stream <paramref name="number"/> into <paramref name="file"/>, holding <paramref name="members"/>
    /// Flate-compressed, its dictionary holding <paramref name="entries"/> besides its own, and gives where its header starts.
    /// </summary>
    private static long FlateObjectStream(HandWrittenFile file, int number, (int Number, string Body)[] members, string entries)
    {
        var header = new StringBuilder();
        var contents = new StringBuilder();

        foreach (var (member, body) in members)
        {
            header.Append(CultureInfo.InvariantCulture, $"{member} {contents.Length} ");
            contents.Append(body).Append(' ');
        }

        var data = Deflate(header.ToString() + contents);
        return file.Object(
            number,
            string.Create(
                CultureInfo.InvariantCulture,
                $"<< /Type /ObjStm /N {members.Length} /First {header.Length} /Filter /FlateDecode {entries} /Length {data.Length} >>\nstream\n{data}\nendstream"));
    }

    /// <summary>
    /// Gives the object the reader says object stream 7 needs, which it holds, as a document of its own first reads object 6;
    /// null when it says none.
    /// </summary>
    private static int? ReaderNeeds(byte[] file, PdfReaderOptions? options = null)
    {
        const string Needs = "Object stream 7 needs object ";
        using var document = PdfDocument.Open(file, options);
        document.GetObject(new PdfObjectId(6));

        foreach (var diagnostic in document.Diagnostics)
        {
            if (diagnostic.Code == PdfDiagnosticCodes.StreamSelfReference && diagnostic.Message.StartsWith(Needs, StringComparison.Ordinal))
            {
                var end = diagnostic.Message.IndexOf(',', Needs.Length);
                return int.Parse(diagnostic.Message.AsSpan(Needs.Length, end - Needs.Length), CultureInfo.InvariantCulture);
            }
        }

        return null;
    }

    /// <summary>Objects 5 and 6 in object stream 7, whose dictionary gets <paramref name="entries"/>; object 5 is <paramref name="parameter"/>.</summary>
    private static byte[] ObjectStream(string entries, string parameter, bool compress = true) =>
        new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(5, parameter)
            .WithObject(6, "<< /Title (six) >>")
            .BuildWithXRefStream(rootNumber: 1, compressedObjects: [5, 6], compressObjectStream: compress, objectStreamEntries: entries);

    /// <summary>
    /// Object 5 in object stream 10 and object 6 in object stream 11, both Flate-compressed, their dictionaries given
    /// <paramref name="first"/> and <paramref name="second"/>; the index is a cross-reference stream, object 12, and
    /// places object 7, when <paramref name="elsewhere"/> says so, in an object stream 20 the file lacks.
    /// </summary>
    private static byte[] TwoObjectStreams(string first, string second, bool elsewhere = false)
    {
        using var file = new MemoryStream();
        var offsets = new Dictionary<int, long>();

        void Write(string text) => file.Write(Encoding.Latin1.GetBytes(text));

        void WriteStream(int number, string entries, byte[] data)
        {
            offsets[number] = file.Position;
            Write($"{number} 0 obj\n<< {entries} /Length {data.Length} >>\nstream\n");
            file.Write(data);
            Write("\nendstream\nendobj\n");
        }

        static byte[] Compressed(string text)
        {
            using var compressed = new MemoryStream();

            using (var zlib = new System.IO.Compression.ZLibStream(compressed, System.IO.Compression.CompressionLevel.Optimal))
            {
                zlib.Write(Encoding.Latin1.GetBytes(text));
            }

            return compressed.ToArray();
        }

        Write("%PDF-1.5\n");
        offsets[1] = file.Position;
        Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        offsets[2] = file.Position;
        Write("2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n");
        WriteStream(10, $"/Type /ObjStm /N 1 /First 4 /Filter /FlateDecode {first}", Compressed("5 0 << /Predictor 1 >>"));
        WriteStream(11, $"/Type /ObjStm /N 1 /First 4 /Filter /FlateDecode {second}", Compressed("6 0 << /Predictor 1 >>"));

        var xref = file.Position;
        var rows = new List<byte>();

        for (var number = 0; number <= 12; number++)
        {
            var (type, field2, field3) = number switch
            {
                1 or 2 or 10 or 11 => (1, offsets[number], 0),
                5 => (2, 10L, 0),
                6 => (2, 11L, 0),
                7 when elsewhere => (2, 20L, 0),
                12 => (1, xref, 0),
                0 => (0, 0L, 65535),
                _ => (0, 0L, 0),
            };

            rows.AddRange([(byte)type, (byte)(field2 >> 24), (byte)(field2 >> 16), (byte)(field2 >> 8), (byte)field2, (byte)(field3 >> 8), (byte)field3]);
        }

        Write($"12 0 obj\n<< /Type /XRef /Size 13 /W [1 4 2] /Root 1 0 R /Length {rows.Count} >>\nstream\n");
        file.Write(rows.ToArray());
        Write($"\nendstream\nendobj\nstartxref\n{xref}\n%%EOF\n");
        return file.ToArray();
    }

    /// <summary>
    /// Object streams 7 and 8, Flate-compressed, each needing the first object it holds, their headers written <c>7 0 %x</c> then
    /// <c>obj</c> on the next line — which the reader reads where the index places them, and a rebuild does not find —;
    /// object stream 20, which no entry names and which lists object 8; and object stream <paramref name="broken"/>, whose
    /// entry leads nowhere, holding the object numbered after it. The index is a cross-reference stream, object 32.
    /// </summary>
    /// <param name="broken">The number of the object stream whose entry leads nowhere: 30, or 3, which a walk in the order
    /// of numbers reads first.</param>
    /// <param name="throughObject">Whether 7's <c>/DecodeParms</c> leads to object 5 through object 12, written directly in
    /// the file with a header a rebuild does not find either.</param>
    private static byte[] RebuiltAfterTwoObjectStreams(int broken = 30, bool throughObject = false)
    {
        using var file = new MemoryStream();
        var offsets = new Dictionary<int, long>();

        void Write(string text) => file.Write(Encoding.Latin1.GetBytes(text));

        void WriteObjectStream(int number, string header, string objects, string entries)
        {
            offsets[number] = file.Position;
            var data = Deflate(header + objects);
            Write($"{number} 0 %x\nobj\n<< /Type /ObjStm /N 2 /First {header.Length} /Filter /FlateDecode {entries} /Length {data.Length} >>\nstream\n{data}\nendstream\nendobj\n");
        }

        Write("%PDF-1.5\n");
        offsets[1] = file.Position;
        Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        offsets[2] = file.Position;
        Write("2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n");
        WriteObjectStream(7, "5 0 6 19 ", "<< /Predictor 1 >> << /Title (six) >>", throughObject ? "/DecodeParms 12 0 R" : "/DecodeParms 5 0 R");
        WriteObjectStream(8, "15 0 16 19 ", "<< /Predictor 1 >> << /Title (sixteen) >>", "/DecodeParms 15 0 R");
        Write("20 0 obj\n<< /Type /ObjStm /N 1 /First 4 /Length 9 >>\nstream\n8 0 << >>\nendstream\nendobj\n");
        offsets[12] = file.Position;
        Write("12 0 %x\nobj\n[5 0 R]\nendobj\n");

        var xref = file.Position;
        var rows = new List<byte>();

        for (var number = 0; number <= 32; number++)
        {
            var (type, field2, field3) = number switch
            {
                1 or 2 or 7 or 8 or 12 => (1, offsets[number], 0),
                5 or 15 => (2, number == 5 ? 7L : 8L, 0),
                6 or 16 => (2, number == 6 ? 7L : 8L, 1),
                _ when number == broken => (1, 1L, 0),
                _ when number == broken + 1 => (2, broken, 0),
                32 => (1, xref, 0),
                0 => (0, 0L, 65535),
                _ => (0, 0L, 0),
            };

            rows.AddRange([(byte)type, (byte)(field2 >> 24), (byte)(field2 >> 16), (byte)(field2 >> 8), (byte)field2, (byte)(field3 >> 8), (byte)field3]);
        }

        Write($"32 0 obj\n<< /Type /XRef /Size 33 /W [1 4 2] /Root 1 0 R /Length {rows.Count} >>\nstream\n");
        file.Write(rows.ToArray());
        Write($"\nendstream\nendobj\nstartxref\n{xref}\n%%EOF\n");
        return file.ToArray();
    }

    /// <summary>Gives the finding of object stream 7 needing object 5, which it holds, through <paramref name="key"/>.</summary>
    private static string HoldsFive(string key) =>
        $"Object stream 7 needs object 5, which it holds, to be read: its {key} names it, and the object cannot be read before the stream is.";

    /// <summary>
    /// Objects <paramref name="first"/> onward, <paramref name="count"/> of them, each a reference to the next, the last
    /// <paramref name="last"/>.
    /// </summary>
    private static Dictionary<int, string> ReferenceChain(int first, int count, string last)
    {
        var objects = new Dictionary<int, string>();

        for (var index = 0; index < count; index++)
        {
            objects[first + index] = index + 1 < count ? string.Create(CultureInfo.InvariantCulture, $"{first + index + 1} 0 R") : last;
        }

        return objects;
    }

    /// <summary>Gives the objects of <paramref name="first"/> and <paramref name="second"/> together.</summary>
    private static Dictionary<int, string> Merged(Dictionary<int, string> first, Dictionary<int, string> second)
    {
        foreach (var (number, body) in second)
        {
            first[number] = body;
        }

        return first;
    }

    /// <summary>
    /// Streams <paramref name="first"/> onward, <paramref name="count"/> of them, each taking its <c>/Length</c> from the
    /// next, the last from <paramref name="last"/>.
    /// </summary>
    private static Dictionary<int, string> StreamChain(int first, int count, string last)
    {
        var objects = new Dictionary<int, string>();

        for (var index = 0; index < count; index++)
        {
            var length = index + 1 < count ? string.Create(CultureInfo.InvariantCulture, $"{first + index + 1} 0 R") : last;
            objects[first + index] = $"<< /Length {length} >>\nstream\nab\nendstream";
        }

        return objects;
    }

    /// <summary>
    /// Streams <paramref name="first"/>, <paramref name="first"/> + 2 and onward, <paramref name="count"/> of them, each
    /// taking its <c>/Length</c> from the object after it, which names the next stream, the last from
    /// <paramref name="last"/>: a chain loads a run of one link between two streams.
    /// </summary>
    private static Dictionary<int, string> LinkedStreamChain(int first, int count, string last)
    {
        var objects = new Dictionary<int, string>();

        for (var index = 0; index < count; index++)
        {
            var stream = first + (2 * index);
            var length = index + 1 < count ? string.Create(CultureInfo.InvariantCulture, $"{stream + 1} 0 R") : last;
            objects[stream] = $"<< /Length {length} >>\nstream\nab\nendstream";

            if (index + 1 < count)
            {
                objects[stream + 1] = string.Create(CultureInfo.InvariantCulture, $"{stream + 2} 0 R");
            }
        }

        return objects;
    }

    /// <summary>
    /// Writes a file indexed by a cross-reference stream, of the objects <paramref name="written"/> directly in the file and
    /// the object streams <paramref name="streams"/>, whose data is written as it is: the walk of their dependencies reads
    /// none of it.
    /// </summary>
    private static byte[] PlainObjectStreams(Dictionary<int, string> written, params PlainObjectStream[] streams)
    {
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"), 0),
            [2] = (1, file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"), 0),
        };

        foreach (var (number, body) in written)
        {
            rows[number] = (1, file.Object(number, body), 0);
        }

        foreach (var stream in streams)
        {
            var header = new StringBuilder();
            var contents = new StringBuilder();

            for (var index = 0; index < stream.Members.Length; index++)
            {
                header.Append(CultureInfo.InvariantCulture, $"{stream.Members[index].Number} {contents.Length} ");
                contents.Append(stream.Members[index].Body).Append(' ');
                rows[stream.Members[index].Number] = (2, stream.Number, index);
            }

            var data = header.ToString() + contents;
            var length = stream.Length ?? data.Length.ToString(CultureInfo.InvariantCulture);
            rows[stream.Number] = (1, file.Object(stream.Number, $"<< /Type /ObjStm /N {stream.Count ?? stream.Members.Length.ToString(CultureInfo.InvariantCulture)} /First {header.Length} {stream.Entries} /Length {length} >>\nstream\n{data}\nendstream"), 0);
        }

        var size = rows.Keys.Max() + 1;
        file.CrossReferenceStream(size, size + 1, rows, "/Root 1 0 R");
        return file.Bytes;
    }

    /// <summary>
    /// The shapes whose filters and parameters decoding reads or does not, by name: #277's table and comment, and those the
    /// measurement of the walk added.
    /// </summary>
    private static readonly Dictionary<string, StreamSeven> DecodingShapes = new()
    {
        // #277's table, and values decoding does not read that the walk took for needs.
        ["row 1: Flate, /DecodeParms << /Foo 5 0 R >>"] = new("/Filter /FlateDecode /DecodeParms << /Foo 5 0 R >>"),
        ["row 2: Flate, a /Columns 5 0 R past a predictor of 1"] = new("/Filter /FlateDecode /DecodeParms << /Predictor 1 /Columns 5 0 R >>"),
        ["row 3: Flate, /DecodeParms [null 5 0 R]"] = new("/Filter /FlateDecode /DecodeParms [null 5 0 R]") { Five = "<< /Predictor 1 >>" },
        ["row 4: no filter, a /Predictor 5 0 R"] = new("/DecodeParms << /Predictor 5 0 R >>") { Data = SevenData.Plain },
        ["row 5: /Filter [/FlateDecode [5 0 R]]"] = new("/Filter [/FlateDecode [5 0 R]]"),
        ["/Filter [/FlateDecode /Foo 5 0 R]"] = new("/Filter [/FlateDecode /Foo 5 0 R]") { Five = "/FlateDecode" },
        ["/Filter 3 0 R, 3 = [/FlateDecode /Foo 5 0 R]"] = new("/Filter 3 0 R") { Five = "/FlateDecode", Written = new() { [3] = "[/FlateDecode /Foo 5 0 R]" } },
        ["Flate, /DecodeParms 3 0 R, 3 = [null 5 0 R]"] = new("/Filter /FlateDecode /DecodeParms 3 0 R") { Five = "<< /Predictor 1 >>", Written = new() { [3] = "[null 5 0 R]" } },
        ["Flate, /DecodeParms 3 0 R, 3 = [4 0 R], 4 = << /Foo 5 0 R >>"] = new("/Filter /FlateDecode /DecodeParms 3 0 R") { Written = new() { [3] = "[4 0 R]", [4] = "<< /Foo 5 0 R >>" } },
        ["ASCIIHex, which reads no parameters, /DecodeParms 5 0 R"] = new("/Filter /ASCIIHexDecode /DecodeParms 5 0 R") { Five = "<< /Predictor 1 >>", Data = SevenData.Hex },
        ["[/FlateDecode /Foo], /DecodeParms [null 5 0 R]"] = new("/Filter [/FlateDecode /Foo] /DecodeParms [null 5 0 R]") { Five = "<< /Predictor 1 >>" },
        ["[/FlateDecode /JBIG2Decode], /DecodeParms [null << /JBIG2Globals 5 0 R >>]"] = new("/Filter [/FlateDecode /JBIG2Decode] /DecodeParms [null << /JBIG2Globals 5 0 R >>]"),
        ["[/Crypt /FlateDecode], /DecodeParms [<< /Name 5 0 R >> null]"] = new("/Filter [/Crypt /FlateDecode] /DecodeParms [<< /Name 5 0 R >> null]") { Five = "/Identity" },
        ["Flate, a /Columns 5 0 R past a predictor 4 0 R of 1"] = new("/Filter /FlateDecode /DecodeParms << /Predictor 4 0 R /Columns 5 0 R >>") { Written = new() { [4] = "1" } },
        ["Flate, a /Columns 5 0 R past a predictor of 1 object stream 9 holds"] = new("/Filter /FlateDecode /DecodeParms << /Predictor 100 0 R /Columns 5 0 R >>") { InNine = new() { [100] = "1" } },
        ["Flate, parameters object stream 9 holds, << /Foo 5 0 R >>"] = new("/Filter /FlateDecode /DecodeParms 100 0 R") { InNine = new() { [100] = "<< /Foo 5 0 R >>" } },
        ["Flate, a /DecodeParms chain whose 31st object, which 9 holds, names a 32nd"] = new("/Filter /FlateDecode /DecodeParms 100 0 R")
        {
            Written = Merged(ReferenceChain(100, 30, "130 0 R"), ReferenceChain(140, 2, "<< /Predictor 5 0 R >>")),
            InNine = new() { [130] = "140 0 R" },
        },

        // Values decoding reads: the needs the walk had, and those it missed — a stream, a member on the way (#277).
        ["Flate, a /Columns 5 0 R past a predictor of 12"] = new("/Filter /FlateDecode /DecodeParms << /Predictor 12 /Columns 5 0 R >>"),
        ["LZW, an /EarlyChange 5 0 R"] = new("/Filter /LZWDecode /DecodeParms << /EarlyChange 5 0 R >>") { Data = SevenData.Plain },
        ["/Filter 3 0 R, 3 = [5 0 R]"] = new("/Filter 3 0 R") { Five = "/FlateDecode", Written = new() { [3] = "[5 0 R]" } },
        ["Flate, /DecodeParms 3 0 R, 3 = [4 0 R], 4 = << /Predictor 5 0 R >>"] = new("/Filter /FlateDecode /DecodeParms 3 0 R") { Written = new() { [3] = "[4 0 R]", [4] = "<< /Predictor 5 0 R >>" } },
        ["Flate, a /Predictor 4 0 R, 4 = 3 0 R, 3 = 5 0 R"] = new("/Filter /FlateDecode /DecodeParms << /Predictor 4 0 R >>") { Written = new() { [3] = "5 0 R", [4] = "3 0 R" } },
        ["row 6: Flate, parameters a stream whose /Predictor is 5 0 R"] = new("/Filter /FlateDecode /DecodeParms 3 0 R") { Written = new() { [3] = "<< /Predictor 5 0 R /Length 2 >>\nstream\nab\nendstream" } },
        ["row 7: Flate, parameters a stream whose /Length is 5 0 R"] = new("/Filter /FlateDecode /DecodeParms 3 0 R") { Five = "2", Written = new() { [3] = "<< /Length 5 0 R >>\nstream\nab\nendstream" } },
        ["Flate, a /Predictor that is a stream whose /Length is 5 0 R"] = new("/Filter /FlateDecode /DecodeParms << /Predictor 3 0 R >>") { Five = "2", Written = new() { [3] = "<< /Length 5 0 R >>\nstream\nab\nendstream" } },
        ["/Filter 3 0 R, 3 a stream whose /Length is 5 0 R"] = new("/Filter 3 0 R") { Five = "2", Data = SevenData.Plain, Written = new() { [3] = "<< /Length 5 0 R >>\nstream\nab\nendstream" } },
        ["Flate, parameters object stream 9 holds, << /Predictor 5 0 R >>"] = new("/Filter /FlateDecode /DecodeParms 100 0 R") { InNine = new() { [100] = "<< /Predictor 5 0 R >>" } },
        ["Flate, a /Columns 5 0 R past a predictor of 12 object stream 9 holds"] = new("/Filter /FlateDecode /DecodeParms << /Predictor 100 0 R /Columns 5 0 R >>") { InNine = new() { [100] = "12" } },
        ["two Flate steps, the second's parameters a chain to the first's, << /Predictor 5 0 R >>"] = new("/Filter [/FlateDecode /FlateDecode] /DecodeParms [101 0 R 100 0 R]")
        {
            Data = SevenData.FlateTwice,
            Written = new() { [100] = "101 0 R", [101] = "<< /Predictor 5 0 R >>" },
        },
    };

    /// <summary>The names of <see cref="DecodingShapes"/> whose values decoding does not read.</summary>
    public static TheoryData<string> ValuesDecodingDoesNotRead =>
    [
        "row 1: Flate, /DecodeParms << /Foo 5 0 R >>",
        "row 2: Flate, a /Columns 5 0 R past a predictor of 1",
        "row 3: Flate, /DecodeParms [null 5 0 R]",
        "row 4: no filter, a /Predictor 5 0 R",
        "row 5: /Filter [/FlateDecode [5 0 R]]",
        "/Filter [/FlateDecode /Foo 5 0 R]",
        "/Filter 3 0 R, 3 = [/FlateDecode /Foo 5 0 R]",
        "Flate, /DecodeParms 3 0 R, 3 = [null 5 0 R]",
        "Flate, /DecodeParms 3 0 R, 3 = [4 0 R], 4 = << /Foo 5 0 R >>",
        "ASCIIHex, which reads no parameters, /DecodeParms 5 0 R",
        "[/FlateDecode /Foo], /DecodeParms [null 5 0 R]",
        "[/FlateDecode /JBIG2Decode], /DecodeParms [null << /JBIG2Globals 5 0 R >>]",
        "[/Crypt /FlateDecode], /DecodeParms [<< /Name 5 0 R >> null]",
        "Flate, a /Columns 5 0 R past a predictor 4 0 R of 1",
        "Flate, a /Columns 5 0 R past a predictor of 1 object stream 9 holds",
        "Flate, parameters object stream 9 holds, << /Foo 5 0 R >>",
        "Flate, a /DecodeParms chain whose 31st object, which 9 holds, names a 32nd",
    ];

    /// <summary>The names of <see cref="DecodingShapes"/> whose values decoding reads, with the key that leads to 5.</summary>
    public static TheoryData<string, string> ValuesDecodingReads => new()
    {
        { "Flate, a /Columns 5 0 R past a predictor of 12", "/DecodeParms" },
        { "LZW, an /EarlyChange 5 0 R", "/DecodeParms" },
        { "/Filter 3 0 R, 3 = [5 0 R]", "/Filter" },
        { "Flate, /DecodeParms 3 0 R, 3 = [4 0 R], 4 = << /Predictor 5 0 R >>", "/DecodeParms" },
        { "Flate, a /Predictor 4 0 R, 4 = 3 0 R, 3 = 5 0 R", "/DecodeParms" },
        { "row 6: Flate, parameters a stream whose /Predictor is 5 0 R", "/DecodeParms" },
        { "row 7: Flate, parameters a stream whose /Length is 5 0 R", "/DecodeParms" },
        { "Flate, a /Predictor that is a stream whose /Length is 5 0 R", "/DecodeParms" },
        { "/Filter 3 0 R, 3 a stream whose /Length is 5 0 R", "/Filter" },
        { "Flate, parameters object stream 9 holds, << /Predictor 5 0 R >>", "/DecodeParms" },
        { "Flate, a /Columns 5 0 R past a predictor of 12 object stream 9 holds", "/DecodeParms" },
        { "two Flate steps, the second's parameters a chain to the first's, << /Predictor 5 0 R >>", "/DecodeParms" },
    };

    /// <summary>
    /// Nests of streams the walk keeps and takes, or follows again, by name: each file, and the object streams the walk gives
    /// with no nest kept as circular. Object stream 10 walks first; a stream met under /DecodeParms is at the second level, one
    /// under an object stream's /Length at the third, and the streams of a nest each a level below the one before; the reader
    /// loads nothing past the 64th, and an object of the stream walked is a need at any depth.
    /// </summary>
    private static readonly Dictionary<string, (Func<byte[]> File, int[] Circular)> NestShapes = new()
    {
        // 100 heads 101 to 162, the last naming 13, which 12 holds. Under 10's /Length its parse reaches 161; under 12's
        // /DecodeParms, a level higher, it reaches 162 as well, whose /Length is 12's own object.
        ["met a level lower first, then higher up"] = (
            () => PlainObjectStreams(
                StreamChain(100, 63, "13 0 R"),
                new(10, string.Empty, [(11, "<< >>")], Length: "100 0 R"),
                new(12, "/Filter /FlateDecode /DecodeParms 100 0 R", [(13, "<< >>")])),
            [12]),

        // 100 heads 101 to 163, the last naming 13. 10 meets 100 at the second level, which parses 101 to 162; 12 at the
        // third, under its /Length, which parses 101 to 161, then meets 161, parsed, or 162, whose parse reaches 163.
        ["taken a level lower, as far as the reader's nesting reaches"] = (
            () => PlainObjectStreams(
                StreamChain(100, 64, "13 0 R"),
                new(10, "/Filter /FlateDecode /DecodeParms 100 0 R", [(11, "<< >>")]),
                new(12, "/Filter /FlateDecode /DecodeParms 161 0 R", [(13, "<< >>")], Length: "100 0 R")),
            []),
        ["taken a level lower, short of the first stream past the reader's nesting"] = (
            () => PlainObjectStreams(
                StreamChain(100, 64, "13 0 R"),
                new(10, "/Filter /FlateDecode /DecodeParms 100 0 R", [(11, "<< >>")]),
                new(12, "/Filter /FlateDecode /DecodeParms 162 0 R", [(13, "<< >>")], Length: "100 0 R")),
            [12]),

        // Each stream meets 100, which parses 101 to 162, then 101, parsed: met first, 101's parse would reach 163, whose
        // /Length names 13.
        ["taken, its streams parsed"] = (
            () => PlainObjectStreams(
                StreamChain(100, 64, "13 0 R"),
                new(10, "/Filter [/FlateDecode /FlateDecode] /DecodeParms [100 0 R 101 0 R]", [(11, "<< >>")]),
                new(12, "/Filter [/FlateDecode /FlateDecode] /DecodeParms [100 0 R 101 0 R]", [(13, "<< >>")])),
            []),

        // 12 meets 200, whose nest of 57 streams leads to 105 at the 60th level, which parses 106 to 109; then 100, whose
        // parse ends at 105, parsed; then 110, whose parse reaches 172, which names 13.
        ["taken up to a stream the walk parsed already"] = (
            () => PlainObjectStreams(
                Merged(StreamChain(100, 73, "13 0 R"), StreamChain(200, 58, "105 0 R")),
                new(10, "/Filter /FlateDecode /DecodeParms 100 0 R", [(11, "<< >>")]),
                new(12, "/Filter [/FlateDecode /FlateDecode /FlateDecode] /DecodeParms [200 0 R 100 0 R 110 0 R]", [(13, "<< >>")])),
            [12]),

        // 12 parses 105, then 200, whose /Length names 100, whose nest ends at 105: 200's parse met a stream parsed already.
        // 14 parses 200 to 161, and 16 takes its nest, the third level of 101 to 161 included, before it meets 110, parsed.
        ["taken short inside a parse, which gives no nest"] = (
            () => PlainObjectStreams(
                Merged(StreamChain(100, 73, "17 0 R"), new() { [200] = "<< /Length 100 0 R >>\nstream\nab\nendstream" }),
                new(10, "/Filter /FlateDecode /DecodeParms 100 0 R", [(11, "<< >>")]),
                new(12, "/Filter [/FlateDecode /FlateDecode] /DecodeParms [105 0 R 200 0 R]", [(13, "<< >>")]),
                new(14, "/Filter [/FlateDecode /FlateDecode] /DecodeParms [200 0 R 110 0 R]", [(15, "<< >>")]),
                new(16, "/Filter [/FlateDecode /FlateDecode] /DecodeParms [200 0 R 110 0 R]", [(17, "<< >>")])),
            []),

        // 100 heads 101 to 161, the last naming 15. Under 10's /Length, 300 names 21, which self-contained 20 holds as
        // 100 0 R: 100 at the fourth level parses 101 to 160. Under 12's /Length, 200 names 100 at the fourth level, whose
        // nest is taken there, cut by the reader's nesting. Under 14's /DecodeParms, 200's parse reaches 161.
        ["taken where the reader's nesting cut it, inside a parse"] = (
            () => PlainObjectStreams(
                Merged(StreamChain(100, 62, "15 0 R"), new() { [200] = "<< /Length 100 0 R >>\nstream\nab\nendstream", [300] = "<< /Length 21 0 R >>\nstream\nab\nendstream" }),
                new(10, string.Empty, [(11, "<< >>")], Length: "300 0 R"),
                new(12, string.Empty, [(13, "<< >>")], Length: "200 0 R"),
                new(14, "/Filter /FlateDecode /DecodeParms 200 0 R", [(15, "<< >>")]),
                new(20, string.Empty, [(21, "100 0 R")])),
            [14]),

        // 10 meets 111, whose parse reaches 140, which names 13; then 100, whose parse ends at 111, parsed. 12 meets 100,
        // whose parse reaches 13.
        ["met past a stream the walk parsed already"] = (
            () => PlainObjectStreams(
                StreamChain(100, 41, "13 0 R"),
                new(10, "/Filter [/FlateDecode /FlateDecode] /DecodeParms [111 0 R 100 0 R]", [(11, "<< >>")]),
                new(12, "/Filter /FlateDecode /DecodeParms 100 0 R", [(13, "<< >>")])),
            [12]),

        // 100's /Length names 11, which 10 holds: of 12's walk, a need, and of the walk that tells whether 12 is self-contained
        // as 10 meets 13.
        ["met past an object of an object stream"] = (
            () => PlainObjectStreams(
                new() { [100] = "<< /Length 11 0 R >>\nstream\nab\nendstream" },
                new(10, "/Filter /FlateDecode /DecodeParms 13 0 R", [(11, "<< >>")]),
                new(12, "/Filter /FlateDecode /DecodeParms 100 0 R", [(13, "<< /Predictor 1 >>")])),
            [10, 12]),

        // 100's /Length names 21, which self-contained 20 holds as 300 0 R: 12 takes it in a run, once 10 met it. 14 meets 17,
        // which 16 holds as 15 0 R: 16's own walk meets 21, and 16 is not self-contained.
        ["met past a run through objects of object streams"] = (
            () => PlainObjectStreams(
                new() { [100] = "<< /Length 21 0 R >>\nstream\nab\nendstream", [300] = "2" },
                new(10, "/Filter /FlateDecode /DecodeParms 100 0 R", [(11, "<< >>")]),
                new(12, "/Filter /FlateDecode /DecodeParms 100 0 R", [(13, "<< >>")]),
                new(14, "/Filter /FlateDecode /DecodeParms 17 0 R", [(15, "<< >>")]),
                new(16, "/Filter /FlateDecode /DecodeParms 100 0 R", [(17, "15 0 R")]),
                new(20, string.Empty, [(21, "300 0 R")])),
            []),
    };

    /// <summary>#277's comment: a stream or a member on the chain of a key the reader resolves, by name.</summary>
    private static readonly Dictionary<string, StreamSeven> KeyShapes = new()
    {
        ["/Length 100 0 R, 100 a stream whose /Length names 5"] = new(string.Empty) { Five = "2", Data = SevenData.Plain, Length = "100 0 R", Written = StreamChain(100, 1, "5 0 R") },
        ["/N 100 0 R, 100 a stream whose /Length names 5"] = new(string.Empty) { Five = "2", Data = SevenData.Plain, Count = "100 0 R", Written = StreamChain(100, 1, "5 0 R") },
        ["/First 100 0 R, 100 a stream whose /Length names 5"] = new(string.Empty) { Five = "2", Data = SevenData.Plain, First = "100 0 R", Written = StreamChain(100, 1, "5 0 R") },
        ["/Type 100 0 R, 100 a stream whose /Length names 5"] = new(string.Empty) { Five = "2", Data = SevenData.Plain, Type = "100 0 R", Written = StreamChain(100, 1, "5 0 R") },
        ["/Length 100 0 R, 100 naming 101, a stream whose /Length names 5"] = new(string.Empty)
        {
            Five = "2",
            Data = SevenData.Plain,
            Length = "100 0 R",
            Written = new() { [100] = "101 0 R", [101] = "<< /Length 5 0 R >>\nstream\nab\nendstream" },
        },
        ["/Length 100 0 R, 100 a stream whose /Length names 101, a stream whose /Length names 5"] = new(string.Empty) { Five = "2", Data = SevenData.Plain, Length = "100 0 R", Written = StreamChain(100, 2, "5 0 R") },
        ["/Length 100 0 R, 100 a member of 9 naming 5"] = new(string.Empty) { Data = SevenData.Plain, Length = "100 0 R", InNine = new() { [100] = "5 0 R" } },
        ["/N and /First 100 0 R, 100 a stream whose /Length names 5"] = new(string.Empty)
        {
            Five = "2",
            Data = SevenData.Plain,
            Count = "100 0 R",
            First = "100 0 R",
            Written = StreamChain(100, 1, "5 0 R"),
        },
    };

    /// <summary>Shapes where the stream needs two objects it holds, by name.</summary>
    private static readonly Dictionary<string, StreamSeven> OrderShapes = new()
    {
        ["/N 6 0 R, /Filter [/FlateDecode 5 0 R]"] = new("/Filter [/FlateDecode 5 0 R]") { Five = "/FlateDecode", Count = "6 0 R" },
        ["/First 6 0 R, a predictor 5 0 R"] = new("/Filter /FlateDecode /DecodeParms << /Predictor 5 0 R >>") { First = "6 0 R" },
        ["a predictor 5 0 R, of 12, and a /Columns 6 0 R"] = new("/Filter /FlateDecode /DecodeParms << /Predictor 5 0 R /Columns 6 0 R >>") { Five = "12" },
        ["two Flate steps, /DecodeParms [5 0 R 6 0 R]"] = new("/Filter [/FlateDecode /FlateDecode] /DecodeParms [5 0 R 6 0 R]") { Five = "<< /Predictor 1 >>", Data = SevenData.FlateTwice },
    };

    private static PdfValidationFinding Single(PdfValidationReport report, string ruleId)
    {
        report.Findings.Where(finding => finding.RuleId == ruleId).Should().ContainSingle(
            $"one finding of {ruleId}, among: {string.Join("; ", report.Findings)}");
        return report.Findings.First(finding => finding.RuleId == ruleId);
    }

    private static PdfValidationReport Validate(byte[] file)
    {
        using var document = PdfDocument.Open(file);
        return new PdfValidator().Validate(document);
    }

    /// <summary>The cases a property draws, from a fixed seed, so that a failure replays exactly.</summary>
    private static Config PropertySettings =>
        Config.QuickThrowOnFailure.WithMaxTest(200).WithReplay(0x5EED_1729UL, 0x9E37_79B9_7F4A_7C15UL).WithQuietOnSuccess(true);

    /// <summary>
    /// A file of object streams numbered 10, 20 and so on, the <c>k</c>th holding <c>holds[k]</c> objects numbered after
    /// it, each <c>0</c>, and needing the objects <c>needs[k]</c> names, in that order: its <c>/Filter</c> names as many
    /// Flate steps, each of which reads the element of <c>/DecodeParms</c> at its place, and its data is compressed as many
    /// times. The index is a cross-reference stream.
    /// </summary>
    private static byte[] NeedingStreams(int[] holds, int[][] needs)
    {
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"), 0),
            [2] = (1, file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"), 0),
        };

        for (var k = 0; k < holds.Length; k++)
        {
            var number = 10 * (k + 1);
            var header = new StringBuilder();
            var contents = new StringBuilder();

            for (var index = 0; index < holds[k]; index++)
            {
                rows[number + index + 1] = (2, number, index);
                header.Append(CultureInfo.InvariantCulture, $"{number + index + 1} {contents.Length} ");
                contents.Append("0 ");
            }

            var data = header.ToString() + contents;
            var filters = new StringBuilder();
            var parameters = new StringBuilder();

            foreach (var need in needs[k])
            {
                data = Deflate(data);
                filters.Append("/FlateDecode ");
                parameters.Append(CultureInfo.InvariantCulture, $"{need} 0 R ");
            }

            var entries = needs[k].Length == 0 ? string.Empty : $"/Filter [{filters}] /DecodeParms [{parameters}] ";
            rows[number] = (1, file.Object(
                number,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"<< /Type /ObjStm /N {holds[k]} /First {header.Length} {entries}/Length {data.Length} >>\nstream\n{data}\nendstream")), 0);
        }

        var xref = 10 * (holds.Length + 1);
        file.CrossReferenceStream(xref, xref + 1, rows, "/Root 1 0 R");
        return file.Bytes;
    }

    /// <summary>
    /// Gives the circular streams of the file <see cref="NeedingStreams"/> writes and the finding of each, as a search from
    /// each need of each stream finds them: the first need of each stream it needs, in its own order, that names the stream
    /// itself or one from which a search through every stream's needs reaches it.
    /// </summary>
    /// <param name="needs">The objects each stream needs, in order.</param>
    /// <param name="members">The stream holding each object, by its place among the streams.</param>
    private static List<(int Stream, string Message)> SearchedWayBack(int[][] needs, List<(int Stream, int Object)> members)
    {
        var holder = members.ToDictionary(member => member.Object, member => member.Stream);
        var found = new List<(int Stream, string Message)>();

        for (var k = 0; k < needs.Length; k++)
        {
            var seen = new HashSet<int>();

            foreach (var need in needs[k])
            {
                var other = holder[need];

                // Of the objects of one stream, the first is the need: another says nothing more.
                if (!seen.Add(other) || (other != k && !Reaches(other, k)))
                {
                    continue;
                }

                var (stream, held) = (10 * (k + 1), 10 * (other + 1));
                found.Add((stream, other == k
                    ? $"Object stream {stream} needs object {need}, which it holds, to be read: its /DecodeParms names it, and the object cannot be read before the stream is."
                    : $"Object stream {stream} needs object {need}, which object stream {held} holds, to be read: its /DecodeParms names it, and reading object stream {held} needs object stream {stream} in turn."));
                break;
            }
        }

        return found;

        bool Reaches(int from, int target)
        {
            var reached = new HashSet<int> { from };
            var work = new Queue<int>([from]);

            while (work.TryDequeue(out var current))
            {
                foreach (var need in needs[current])
                {
                    if (holder[need] == target)
                    {
                        return true;
                    }

                    if (reached.Add(holder[need]))
                    {
                        work.Enqueue(holder[need]);
                    }
                }
            }

            return false;
        }
    }

    /// <summary>Notes which entries of a step's parameters decoding reads, by the value each holds.</summary>
    /// <param name="keys">The entry each value is written under.</param>
    /// <param name="read">The entries read, in the order decoding reads them.</param>
    private readonly struct ParameterReads(Dictionary<PdfObject, PdfName> keys, List<PdfName> read) : IFilterValueReader
    {
        /// <summary>Gets the entries read, in the order decoding reads them.</summary>
        public List<PdfName> Read { get; } = read;

        /// <inheritdoc/>
        public bool TryRead(PdfName key, PdfObject? written, bool entry, out PdfObject? value)
        {
            if (written is not null && keys.TryGetValue(written, out var name))
            {
                Read.Add(name);
            }

            value = written;
            return true;
        }
    }

    /// <summary>How <see cref="StreamSeven"/> writes its stream's data.</summary>
    private enum SevenData
    {
        /// <summary>As it is.</summary>
        Plain,

        /// <summary>Flate-compressed once.</summary>
        Flate,

        /// <summary>Flate-compressed twice, for two Flate steps.</summary>
        FlateTwice,

        /// <summary>In hexadecimal, ended by <c>&gt;</c>.</summary>
        Hex,
    }

    /// <summary>An object stream <see cref="PlainObjectStreams"/> writes, its data as it is.</summary>
    /// <param name="Number">Its number.</param>
    /// <param name="Entries">What its dictionary holds besides its <c>/Type</c>, <c>/N</c>, <c>/First</c> and <c>/Length</c>.</param>
    /// <param name="Members">The objects it holds.</param>
    /// <param name="Length">Its <c>/Length</c>, the length of its data when null.</param>
    /// <param name="Count">Its <c>/N</c>, how many objects it holds when null.</param>
    private sealed record PlainObjectStream(int Number, string Entries, (int Number, string Body)[] Members, string? Length = null, string? Count = null);

    /// <summary>
    /// A file, indexed by a cross-reference stream, object 200, whose object stream 7 holds objects 5 and 6 — 5 being
    /// <see cref="Five"/> —, its dictionary giving <see cref="Entries"/> besides its own; objects written directly in the
    /// file; members of a second object stream, 9, whose dictionary gives <see cref="NineEntries"/> besides its own; and, when
    /// <see cref="ThreeEntries"/> gives one, an object stream 3, which the walk of the dependencies takes before 7.
    /// </summary>
    /// <param name="Entries">What stream 7's dictionary holds besides its <c>/Type</c>, <c>/N</c>, <c>/First</c> and <c>/Length</c>.</param>
    private sealed record StreamSeven(string Entries)
    {
        /// <summary>Gets object 5's value.</summary>
        public string Five { get; init; } = "1";

        /// <summary>Gets how stream 7's data is written.</summary>
        public SevenData Data { get; init; } = SevenData.Flate;

        /// <summary>Gets stream 7's <c>/Type</c>.</summary>
        public string Type { get; init; } = "/ObjStm";

        /// <summary>Gets stream 7's <c>/N</c>.</summary>
        public string Count { get; init; } = "2";

        /// <summary>Gets stream 7's <c>/First</c>, where its objects start when null.</summary>
        public string? First { get; init; }

        /// <summary>Gets stream 7's <c>/Length</c>, the length of its data when null.</summary>
        public string? Length { get; init; }

        /// <summary>Gets the objects written directly in the file besides the catalog and the page tree.</summary>
        public Dictionary<int, string> Written { get; init; } = [];

        /// <summary>Gets the objects object stream 9 holds; there is no object 9 when there are none.</summary>
        public Dictionary<int, string> InNine { get; init; } = [];

        /// <summary>Gets what stream 9's dictionary holds besides its own entries; its data is Flate-compressed when they name the filter.</summary>
        public string NineEntries { get; init; } = string.Empty;

        /// <summary>Gets stream 9's <c>/Length</c>, the length of its data when null.</summary>
        public string? NineLength { get; init; }

        /// <summary>Gets stream 9's <c>/Type</c>.</summary>
        public string NineType { get; init; } = "/ObjStm";

        /// <summary>
        /// Gets what the dictionary of object stream 3, which holds object 4, an empty dictionary, gives besides its
        /// <c>/N</c>, <c>/First</c> and <c>/Length</c>, its <c>/Type</c> included; there is no object 3 when null. Its data is
        /// Flate-compressed when they name the filter.
        /// </summary>
        public string? ThreeEntries { get; init; }

        /// <summary>Writes the file.</summary>
        public byte[] Build()
        {
            const string Six = "<< /Title (six) >>";
            var header = string.Create(CultureInfo.InvariantCulture, $"5 0 6 {Five.Length + 1} ");
            var plain = header + Five + " " + Six + " ";
            var data = Data switch
            {
                SevenData.Flate => Deflate(plain),
                SevenData.FlateTwice => Deflate(Deflate(plain)),
                SevenData.Hex => Convert.ToHexString(Encoding.Latin1.GetBytes(plain)) + ">",
                _ => plain,
            };

            var file = new HandWrittenFile(version: "1.5");
            var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
            {
                [1] = (1, file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"), 0),
                [2] = (1, file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"), 0),
            };

            foreach (var (number, body) in Written)
            {
                rows[number] = (1, file.Object(number, body), 0);
            }

            var first = First ?? header.Length.ToString(CultureInfo.InvariantCulture);
            var length = Length ?? data.Length.ToString(CultureInfo.InvariantCulture);
            rows[7] = (1, file.Object(7, $"<< /Type {Type} /N {Count} /First {first} {Entries} /Length {length} >>\nstream\n{data}\nendstream"), 0);
            rows[5] = (2, 7, 0);
            rows[6] = (2, 7, 1);

            if (InNine.Count > 0)
            {
                var nineHeader = new StringBuilder();
                var nineBody = new StringBuilder();
                var index = 0;

                foreach (var (number, body) in InNine)
                {
                    nineHeader.Append(CultureInfo.InvariantCulture, $"{number} {nineBody.Length} ");
                    nineBody.Append(body).Append(' ');
                    rows[number] = (2, 9, index++);
                }

                var nineData = nineHeader.ToString() + nineBody;
                nineData = NineEntries.Contains("/FlateDecode", StringComparison.Ordinal) ? Deflate(nineData) : nineData;
                var nineLength = NineLength ?? nineData.Length.ToString(CultureInfo.InvariantCulture);
                rows[9] = (1, file.Object(9, $"<< /Type {NineType} /N {InNine.Count} /First {nineHeader.Length} {NineEntries} /Length {nineLength} >>\nstream\n{nineData}\nendstream"), 0);
            }

            if (ThreeEntries is not null)
            {
                const string Three = "4 0 << >> ";
                var threeData = ThreeEntries.Contains("/FlateDecode", StringComparison.Ordinal) ? Deflate(Three) : Three;
                rows[3] = (1, file.Object(3, $"<< {ThreeEntries} /N 1 /First 4 /Length {threeData.Length} >>\nstream\n{threeData}\nendstream"), 0);
                rows[4] = (2, 3, 0);
            }

            file.CrossReferenceStream(200, 201, rows, "/Root 1 0 R");
            return file.Bytes;
        }
    }
}
