using System.Globalization;
using System.Text;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.Objects;
using AdCodicem.Pdf.Validation;

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

    [Fact]
    public void A_reference_to_object_0_under_an_object_stream_s_keys_needs_nothing_though_the_table_places_object_0_in_it()
    {
        // The cross-reference stream's first row is rewritten to place object 0 at index 0 of object stream 7: the reader
        // still never reads object 0, so the stream does not need itself.
        var file = ObjectStream("/DecodeParms 0 0 R", "<< /Predictor 1 >>");
        var text = Encoding.Latin1.GetString(file);
        var rows = text.IndexOf("stream\n", text.IndexOf("/Type /XRef", StringComparison.Ordinal), StringComparison.Ordinal) + "stream\n".Length;
        file[rows] = 2;
        file[rows + 4] = 7;
        file[rows + 5] = 0;
        file[rows + 6] = 0;

        using var document = PdfDocument.Open(file);
        document.GetObject(new PdfObjectId(6)).Should().BeOfType<PdfDictionary>();

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

    [Fact]
    public void An_object_stream_whose_count_it_holds_is_circular_and_not_also_broken()
    {
        var file = ObjectStream("/N 5 0 R", "2", compress: false);

        var report = Validate(file);

        Single(report, PdfValidationRuleIds.XRefObjectStreamCircular).Message.Should().Be(
            "Object stream 7 needs object 5, which it holds, to be read: its /N names it, and the object cannot be read before the stream is.");
        report.Contains(PdfValidationRuleIds.XRefObjectStreamBroken).Should().BeFalse("the loop is the fault, reported once");
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
    public void Object_streams_a_rebuild_during_the_walk_no_longer_places_are_still_reported_circular()
    {
        // Reading object stream 30, which is not where its entry says, rebuilds the index once 7 and 8 were read: the
        // rebuild cannot find 7, whose header has a comment in it, and places 8 in object stream 20. The walk's lookups
        // still read the index the rebuild changed under it (#128), but a finding is located where the chain's index,
        // read again, places the stream (#118): 7 and 8 keep the positions their rows give, which they once lost.
        var file = RebuiltAfterTwoObjectStreams();
        using var document = PdfDocument.Open(file);

        var dependencies = ObjectStreamDependencies.Run(document);

        document.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == PdfDiagnosticCodes.XRefRebuilt);
        dependencies.CircularStreams.Should().BeEquivalentTo([7, 8]);
        dependencies.Circular.Select(finding => finding.Location.Object).Should().Equal(new PdfObjectId(7), new PdfObjectId(8));
        dependencies.Circular.Select(finding => finding.Location.Position)
            .Should().Equal(PdfTemplate.OffsetOf(file, "7 0 %x"), PdfTemplate.OffsetOf(file, "8 0 %x"));
        dependencies.Circular.Select(finding => finding.Message).Should().Equal(
            "Object stream 7 needs object 5, which it holds, to be read: its /DecodeParms names it, and the object cannot be read before the stream is.",
            "Object stream 8 needs object 15, which it holds, to be read: its /DecodeParms names it, and the object cannot be read before the stream is.");
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

    [Theory]
    [InlineData("/O#0At#1B 9 0 R", "Object 1 refers to object 9 0 under /O#0At#1B, which the file lacks: the reference reads as null.")]
    [InlineData("/A << /B << /C << /D << /E << /F << /G << /H << /I << /J 9 0 R >> >> >> >> >> >> >> >> >>", "Object 1 refers to object 9 0 under /A/B/C/D (2 steps) /G/H/I/J, which the file lacks: the reference reads as null.")]
    public void A_path_to_a_missing_object_quotes_its_keys_escaped_and_keeps_its_ends(string entry, string message)
    {
        Single(Validate(PdfTemplate.SoundWith("/Type /Catalog", "/Type /Catalog " + entry)), PdfValidationRuleIds.ObjectReferenceMissing)
            .Message.Should().Be(message);
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
    /// Object streams 7 and 8, each needing the first object it holds, their headers written <c>7 0 %x</c> then
    /// <c>obj</c> on the next line — which the reader reads where the index places them, and a rebuild does not find —;
    /// object stream 20, which no entry names and which lists object 8; and object stream 30, whose entry leads nowhere.
    /// The index is a cross-reference stream, object 32.
    /// </summary>
    private static byte[] RebuiltAfterTwoObjectStreams()
    {
        using var file = new MemoryStream();
        var offsets = new Dictionary<int, long>();

        void Write(string text) => file.Write(Encoding.Latin1.GetBytes(text));

        void WriteObjectStream(int number, string header, string objects, string entries)
        {
            offsets[number] = file.Position;
            var data = header + objects;
            Write($"{number} 0 %x\nobj\n<< /Type /ObjStm /N 2 /First {header.Length} {entries} /Length {data.Length} >>\nstream\n{data}\nendstream\nendobj\n");
        }

        Write("%PDF-1.5\n");
        offsets[1] = file.Position;
        Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        offsets[2] = file.Position;
        Write("2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n");
        WriteObjectStream(7, "5 0 6 19 ", "<< /Predictor 1 >> << /Title (six) >>", "/DecodeParms 5 0 R");
        WriteObjectStream(8, "15 0 16 19 ", "<< /Predictor 1 >> << /Title (sixteen) >>", "/DecodeParms 15 0 R");
        Write("20 0 obj\n<< /Type /ObjStm /N 1 /First 4 /Length 9 >>\nstream\n8 0 << >>\nendstream\nendobj\n");

        var xref = file.Position;
        var rows = new List<byte>();

        for (var number = 0; number <= 32; number++)
        {
            var (type, field2, field3) = number switch
            {
                1 or 2 or 7 or 8 => (1, offsets[number], 0),
                5 or 15 => (2, number == 5 ? 7L : 8L, 0),
                6 or 16 => (2, number == 6 ? 7L : 8L, 1),
                30 => (1, 1L, 0),
                31 => (2, 30L, 0),
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
}
