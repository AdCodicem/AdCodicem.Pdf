using System.Globalization;
using System.Text;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.IO.XRef;
using AdCodicem.Pdf.Objects;
using AdCodicem.Pdf.Validation;
using AdCodicem.Pdf.Validation.Rules;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// The <c>file</c> family of M02's second slice: the header, <c>startxref</c>, the trailer, its <c>/Root</c> and each
/// section's <c>/Size</c> — each rule on a file that breaks it, a sound file, and a file that is unusual and legal.
/// </summary>
public class FileRuleTests
{
    [Fact]
    public void A_sound_file_has_no_finding()
    {
        Validate(PdfTemplate.Build(PdfTemplate.Sound)).Findings.Should().BeEmpty();
    }

    [Theory]
    [InlineData("%PDF1.7")]
    [InlineData("%PFD-1.7")]
    [InlineData("1.7")]
    public void A_file_without_a_header_is_reported_as_a_warning_at_its_start(string header)
    {
        var report = Validate(PdfTemplate.SoundWith("%PDF-1.7", header));

        var finding = Single(report, PdfValidationRuleIds.FileHeaderMissing);
        finding.Severity.Should().Be(PdfValidationSeverity.Warning, "the reader reads the file without its header (ADR 45)");
        finding.Location.Position.Should().Be(0);
        finding.Message.Should().StartWith("The file does not begin with a PDF header: no %PDF- in its first ");
        report.HasErrors.Should().BeFalse();
    }

    [Fact]
    public void A_header_past_the_first_1024_bytes_is_missing_where_readers_look_for_it()
    {
        var sound = PdfTemplate.Build(PdfTemplate.Sound);
        var file = Encoding.Latin1.GetBytes(new string('%', 1500) + "\n").Concat(sound).ToArray();

        var finding = Single(Validate(file), PdfValidationRuleIds.FileHeaderMissing);

        finding.Location.Position.Should().Be(1501);
        finding.Message.Should().Be("The PDF header starts 1501 bytes into the file, past the first 1024 bytes where readers look for it.");
    }

    [Fact]
    public void Bytes_before_the_header_are_reported_once_and_nothing_else_when_offsets_count_from_it()
    {
        // A mail gateway's line before a sound file: every offset counts from the header, as readers read them.
        var file = Encoding.Latin1.GetBytes("HTTP/1.1 200 OK\r\n\r\n").Concat(PdfTemplate.Build(PdfTemplate.Sound)).ToArray();

        var report = Validate(file);

        var finding = report.Findings.Should().ContainSingle().Which;
        finding.RuleId.Should().Be(PdfValidationRuleIds.FileHeaderOffset);
        finding.Severity.Should().Be(PdfValidationSeverity.Warning);
        finding.Location.Position.Should().Be(0);
        finding.Message.Should().Be("The PDF header starts 19 bytes into the file instead of at its first byte.");
    }

    [Theory]
    [InlineData("%PDF-1.8", "The header names version 1.8, which is not a version of PDF: 1.0 to 1.7, or 2.0.")]
    [InlineData("%PDF-2.1", "The header names version 2.1, which is not a version of PDF: 1.0 to 1.7, or 2.0.")]
    [InlineData("%PDF-2.2", "The header names version 2.2, which is not a version of PDF: 1.0 to 1.7, or 2.0.")]
    [InlineData("%PDF-2.3", "The header names version 2.3, which is not a version of PDF: 1.0 to 1.7, or 2.0.")]
    [InlineData("%PDF-2.4", "The header names version 2.4, which is not a version of PDF: 1.0 to 1.7, or 2.0.")]
    [InlineData("%PDF-2.5", "The header names version 2.5, which is not a version of PDF: 1.0 to 1.7, or 2.0.")]
    [InlineData("%PDF-2.6", "The header names version 2.6, which is not a version of PDF: 1.0 to 1.7, or 2.0.")]
    [InlineData("%PDF-3.0", "The header names version 3.0, which is not a version of PDF: 1.0 to 1.7, or 2.0.")]
    [InlineData("%PDF-14", "The header names version 14, which is not a version of PDF: 1.0 to 1.7, or 2.0.")]
    [InlineData("%PDF-", "The header names no version after %PDF-.")]
    public void A_header_that_names_no_version_of_pdf_is_a_warning(string header, string message)
    {
        var finding = Single(Validate(PdfTemplate.SoundWith("%PDF-1.7", header)), PdfValidationRuleIds.FileHeaderVersionInvalid);

        finding.Severity.Should().Be(PdfValidationSeverity.Warning);
        finding.Location.Position.Should().Be(0);
        finding.Message.Should().Be(message);
    }

    [Theory]
    [InlineData("%PDF-1.0")]
    [InlineData("%PDF-1.1")]
    [InlineData("%PDF-1.2")]
    [InlineData("%PDF-1.3")]
    [InlineData("%PDF-1.5")]
    [InlineData("%PDF-1.6")]
    [InlineData("%PDF-1.7\r")]
    [InlineData("%PDF-2.0")]
    [InlineData("%PDF-1.4\n%âãÏÓ")]
    public void Every_version_of_pdf_is_accepted_whatever_follows_it_on_its_line(string header)
    {
        Validate(PdfTemplate.SoundWith("%PDF-1.7", header)).Findings.Should().BeEmpty();
    }

    [Fact]
    public void A_file_without_startxref_is_an_error_at_its_end()
    {
        var file = PdfTemplate.SoundWith("startxref\n{xref:1}\n", string.Empty);

        var finding = Single(Validate(file), PdfValidationRuleIds.FileStartXRefMissing);

        finding.Severity.Should().Be(PdfValidationSeverity.Error, "the reader rebuilds the index by scanning");
        finding.Location.Position.Should().Be(file.Length);
        finding.Message.Should().Be(string.Create(
            CultureInfo.InvariantCulture,
            $"The file does not give the offset of its cross-reference section: no startxref in its last {file.Length} bytes."));
    }

    [Fact]
    public void A_startxref_without_an_offset_is_located_at_the_keyword()
    {
        var file = PdfTemplate.SoundWith("startxref\n{xref:1}\n", "startxref\n");

        var finding = Single(Validate(file), PdfValidationRuleIds.FileStartXRefMissing);

        var keyword = PdfTemplate.OffsetOf(file, "startxref");
        finding.Location.Position.Should().Be(keyword);
        finding.Message.Should().Be($"The startxref at offset {keyword} is not followed by an offset.");
    }

    [Theory]
    [InlineData(40, "holds neither the xref keyword nor an object")]
    [InlineData(100_000, "lies outside the file")]
    public void A_startxref_that_names_no_section_is_an_error_at_the_keyword(int shift, string what)
    {
        var sound = PdfTemplate.Build(PdfTemplate.Sound);
        var xref = PdfTemplate.OffsetOf(sound, "xref\n");
        var file = Replace(sound, $"startxref\n{xref}\n", $"startxref\n{xref + shift}\n");

        var report = Validate(file);

        var finding = Single(report, PdfValidationRuleIds.FileStartXRefWrong);
        finding.Severity.Should().Be(PdfValidationSeverity.Error);
        finding.Location.Position.Should().Be(PdfTemplate.OffsetOf(file, "startxref"));
        finding.Message.Should().Be($"startxref gives offset {xref + shift}, which {what}.");
        report.Contains(PdfValidationRuleIds.XRefEntryBroken).Should().BeFalse("no entry of an index the chain never gave is probed");
    }

    [Fact]
    public void A_startxref_that_names_an_ordinary_object_says_which()
    {
        var file = PdfTemplate.SoundWith("startxref\n{xref:1}\n", "startxref\n{off:2}\n");

        Single(Validate(file), PdfValidationRuleIds.FileStartXRefWrong).Message
            .Should().EndWith("which holds object 2, which is not a cross-reference stream.");
    }

    [Fact]
    public void A_first_section_not_found_for_no_stated_reason_is_said_to_hold_none()
    {
        // The reader says why every section it does not find is not there; the rule, given a record no file produces
        // that says nothing, still writes a sentence.
        var file = PdfTemplate.Build(PdfTemplate.Sound);
        using var document = PdfDocument.Open(file);
        document.Reader.Structure.Sections[0].State = XRefSectionState.NotFound;
        var context = new ValidationContext(document, capacity: 16);

        new StartXRefWrongRule().Check(context);

        var finding = context.ToReport(ValidationProfile.Structural).Findings.Should().ContainSingle().Which;
        finding.Location.Position.Should().Be(PdfTemplate.OffsetOf(file, "startxref"));
        finding.Message.Should().Be($"startxref gives offset {PdfTemplate.OffsetOf(file, "xref\n")}, which holds no cross-reference section.");
    }

    [Fact]
    public void A_startxref_that_names_a_cross_reference_stream_is_sound()
    {
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .BuildWithXRefStream(rootNumber: 1);

        Validate(file).Findings.Should().BeEmpty();
    }

    [Fact]
    public void Rows_that_run_into_a_dictionary_without_the_trailer_keyword_miss_their_trailer()
    {
        var file = PdfTemplate.SoundWith("trailer\n<<", "<<");

        var report = Validate(file);

        var finding = Single(report, PdfValidationRuleIds.FileTrailerMissing);
        finding.Severity.Should().Be(PdfValidationSeverity.Error);
        var dictionary = PdfTemplate.OffsetOf(file, "<< /Size");
        finding.Location.Position.Should().Be(dictionary);
        finding.Message.Should().Be(
            $"The cross-reference table at offset {PdfTemplate.OffsetOf(file, "xref\n")} is not followed by a trailer: a dictionary follows its rows at offset {dictionary} without the trailer keyword.");
        report.Contains(PdfValidationRuleIds.XRefSectionMalformed).Should().BeFalse("the rows themselves are sound");
    }

    [Fact]
    public void A_table_the_file_ends_in_misses_its_trailer()
    {
        // startxref names a table written after it, which the file ends in before any trailer.
        var file = PdfTemplate.Build("""
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
            startxref
            {xref:1}
            %%EOF
            xref
            0 4
            {free}
            {row:1}
            {row:2}
            {row:3}

            """);

        var finding = Single(Validate(file), PdfValidationRuleIds.FileTrailerMissing);

        finding.Location.Position.Should().Be(file.Length);
        finding.Message.Should().Be(
            $"The cross-reference table at offset {PdfTemplate.OffsetOf(file, "\nxref\n") + 1} is not followed by a trailer: the file ends first.");
    }

    [Fact]
    public void A_trailer_keyword_followed_by_no_dictionary_is_malformed_and_the_catalog_found_without_a_rebuild()
    {
        var file = PdfTemplate.SoundWith("<< /Size 4 /Root 1 0 R >>", "/Size 4 /Root 1 0 R >>");
        using var document = PdfDocument.Open(file);

        var report = new PdfValidator().Validate(document);

        var finding = Single(report, PdfValidationRuleIds.FileTrailerMalformed);
        finding.Severity.Should().Be(PdfValidationSeverity.Error);
        finding.Message.Should().Be($"The trailer keyword at offset {PdfTemplate.OffsetOf(file, "trailer")} is not followed by a dictionary.");
        report.Contains(PdfValidationRuleIds.FileRootInvalid).Should().BeFalse("a trailer that could not be read has no /Root to judge");
        document.WasRepaired.Should().BeFalse("the index is sound, and the catalog is among its objects");
        document.Diagnostics.Contains(PdfDiagnosticCodes.TrailerRootRecovered).Should().BeTrue();
    }

    [Theory]
    [InlineData("<< /Size 4 /Root 1 0 R\n")]
    [InlineData("<< /Size 4 /Root 1 0 R 7 >>")]
    public void A_trailer_read_despite_syntax_errors_is_malformed(string trailer)
    {
        var file = PdfTemplate.SoundWith("<< /Size 4 /Root 1 0 R >>\n", trailer);

        var finding = Single(Validate(file), PdfValidationRuleIds.FileTrailerMalformed);

        finding.Message.Should().Be(
            $"The trailer at offset {PdfTemplate.OffsetOf(file, "trailer")} is not a well-formed dictionary: the reader read it despite syntax errors.");
    }

    [Theory]
    [InlineData(400, true)]
    [InlineData(300, false)]
    public void A_trailer_holding_a_number_beyond_what_a_real_can_hold_is_malformed(int zeros, bool malformed)
    {
        // A one and 400 zeros reads as null, a syntax fault that loses the entry; a one and 300 zeros, an integer past a
        // long, reads as the real nearest to it, which is no fault.
        var file = PdfTemplate.SoundWith("<< /Size 4 /Root 1 0 R >>", "<< /Size 4 /Root 1 0 R /Big 1" + new string('0', zeros) + " >>");
        using var document = PdfDocument.Open(file);

        var report = new PdfValidator().Validate(document);

        document.Diagnostics.Where(entry => entry.Code == PdfDiagnosticCodes.SyntaxNumberOutOfRange).Select(entry => entry.Position)
            .Should().Equal(malformed ? [PdfTemplate.OffsetOf(file, "/Big 1") + 5] : Array.Empty<long>());

        if (malformed)
        {
            Single(report, PdfValidationRuleIds.FileTrailerMalformed).Message.Should().Be(
                $"The trailer at offset {PdfTemplate.OffsetOf(file, "trailer")} is not a well-formed dictionary: the reader read it despite syntax errors.");
        }
        else
        {
            report.Findings.Should().BeEmpty();
        }
    }

    [Theory]
    [InlineData(4)]
    [InlineData(3000)]
    public void A_trailer_the_file_ends_inside_is_malformed_whichever_window_reads_it(int rows)
    {
        // The trailer's /ID opens a string the file never closes. Behind four rows, the table's window holds the end of the
        // file; behind three thousand, the trailer runs past that window and is read again through a window of its own,
        // which reaches the end of the file: either way the string is reported, and the trailer judged malformed.
        var table = new StringBuilder($"xref\n0 {rows}\n{{free}}\n{{row:1}}\n{{row:2}}\n{{row:3}}\n");
        for (var row = 4; row < rows; row++)
        {
            table.Append("0000000000 65535 f \n");
        }

        var file = PdfTemplate.Build(
            PdfTemplate.Sound[..PdfTemplate.Sound.IndexOf("xref\n", StringComparison.Ordinal)] + table +
            $"trailer\n<< /Size {rows} /Root 1 0 R /ID [(abc" + new string('x', 10_000) + "\nstartxref\n{xref:1}\n%%EOF\n");
        using var document = PdfDocument.Open(file);

        var report = Validate(file);

        document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.SyntaxTruncatedObject)
            .Which.Position.Should().Be(PdfTemplate.OffsetOf(file, "(abc"));
        Single(report, PdfValidationRuleIds.FileTrailerMalformed).Message.Should().Be(
            $"The trailer at offset {PdfTemplate.OffsetOf(file, "trailer")} is not a well-formed dictionary: the reader read it despite syntax errors.");
    }

    [Fact]
    public void A_trailer_whose_syntax_errors_overflow_the_diagnostics_is_still_malformed()
    {
        // Two stray values, and room for one diagnostic: the second error is dropped, and still counts.
        var file = PdfTemplate.SoundWith("<< /Size 4 /Root 1 0 R >>", "<< /Size 4 7 /Root 1 0 R 8 >>");
        using var document = PdfDocument.Open(file, new PdfReaderOptions { DiagnosticCapacity = 1 });

        Single(new PdfValidator().Validate(document), PdfValidationRuleIds.FileTrailerMalformed).Message.Should().Be(
            $"The trailer at offset {PdfTemplate.OffsetOf(file, "trailer")} is not a well-formed dictionary: the reader read it despite syntax errors.");
    }

    [Fact]
    public void Comments_inside_a_trailer_are_white_space()
    {
        var file = PdfTemplate.SoundWith("<< /Size 4 /Root 1 0 R >>", "<< /Size 4 % the count\n/Root 1 0 R % the catalog\n>>");

        Validate(file).Findings.Should().BeEmpty();
    }

    [Fact]
    public void A_trailer_without_root_is_an_error_naming_the_catalog_the_reader_took()
    {
        var file = PdfTemplate.SoundWith("<< /Size 4 /Root 1 0 R >>", "<< /Size 4 >>");
        using var document = PdfDocument.Open(file);

        var finding = Single(new PdfValidator().Validate(document), PdfValidationRuleIds.FileRootInvalid);

        finding.Severity.Should().Be(PdfValidationSeverity.Error, "the catalog the reader found is its choice, not the file's");
        finding.Location.Position.Should().Be(PdfTemplate.OffsetOf(file, "trailer"));
        finding.Message.Should().Be("The trailer has no /Root. The reader took object 1, which is one, for the catalog.");
        document.WasRepaired.Should().BeFalse();
        document.Catalog.Should().NotBeNull();
    }

    [Theory]
    [InlineData("/Root 1", "The trailer's /Root is a number, not a reference to the document catalog.")]
    [InlineData("/Root /Catalog", "The trailer's /Root is a name, not a reference to the document catalog.")]
    [InlineData("/Root 3 0 R", "The trailer's /Root names object 3 0, which is a dictionary of /Type /Page, not a document catalog.")]
    [InlineData("/Root 9 0 R", "The trailer's /Root names object 9 0, which the file does not hold.")]
    [InlineData("/Root 0 0 R", "The trailer's /Root names object 0 0, which the file does not hold.")]
    [InlineData("/Root null", "The trailer has no /Root.")]
    [InlineData("/Root 1.5", "The trailer's /Root is a number, not a reference to the document catalog.")]
    [InlineData("/Root (the catalog)", "The trailer's /Root is a string, not a reference to the document catalog.")]
    [InlineData("/Root [1 0 R]", "The trailer's /Root is an array, not a reference to the document catalog.")]
    [InlineData("/Root << /Type /Outlines >>", "The trailer's /Root is a dictionary written in the trailer, not a reference to the document catalog.")]
    [InlineData("/Root true", "The trailer's /Root is a boolean, not a reference to the document catalog.")]
    public void A_root_that_leads_to_no_catalog_says_what_it_leads_to(string root, string message)
    {
        var file = PdfTemplate.SoundWith("/Root 1 0 R", root);

        var finding = Single(Validate(file), PdfValidationRuleIds.FileRootInvalid);

        finding.Message.Should().Be(message + " The reader took object 1, which is one, for the catalog.");
    }

    [Fact]
    public void A_root_whose_type_holds_control_characters_quotes_it_escaped()
    {
        // A line feed, an escape, a null character and a C1 control in /Type reach the finding as #xx (#159).
        var file = PdfTemplate.Build(PdfTemplate.Sound
            .Replace("/Root 1 0 R", "/Root 3 0 R", StringComparison.Ordinal)
            .Replace("<< /Type /Page ", "<< /Type /X#0Aforged#1B#00z#9B ", StringComparison.Ordinal));

        Single(Validate(file), PdfValidationRuleIds.FileRootInvalid).Message.Should().StartWith(
            "The trailer's /Root names object 3 0, which is a dictionary of /Type /X#0Aforged#1B#00z#9B, not a document catalog.");
    }

    [Fact]
    public void A_root_naming_object_0_is_located_at_the_object_it_names_in_a_trailer_read_whole()
    {
        // Read as two integers and a stray keyword before #117, /Root 0 0 R left the trailer malformed and the finding at
        // the trailer; it is a reference, located as a reference to any object the file lacks is.
        var report = Validate(PdfTemplate.SoundWith("/Root 1 0 R", "/Root 0 0 R"));

        Single(report, PdfValidationRuleIds.FileRootInvalid).Location.Object.Should().Be(new PdfObjectId(0));
        report.Contains(PdfValidationRuleIds.FileTrailerMalformed).Should().BeFalse();
    }

    [Fact]
    public void A_root_written_as_a_stream_in_the_trailer_is_named_a_stream()
    {
        // A stream is an indirect object (ISO 32000-1, 7.3.8.1), but the parser takes a dictionary followed by stream
        // for one wherever it is written, the trailer included.
        var file = PdfTemplate.SoundWith("/Root 1 0 R", "/Root << /Length 0 >>\nstream\n\nendstream");

        Single(Validate(file), PdfValidationRuleIds.FileRootInvalid).Message.Should().Be(
            "The trailer's /Root is a stream written in the trailer, not a reference to the document catalog. The reader took object 1, which is one, for the catalog.");
    }

    [Fact]
    public void A_file_whose_root_names_no_catalog_and_holds_none_says_so()
    {
        var file = PdfTemplate.SoundWith("<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Outlines >>");

        Single(Validate(file), PdfValidationRuleIds.FileRootInvalid).Message
            .Should().Be("The trailer's /Root names object 1 0, which is a dictionary of /Type /Outlines, not a document catalog. No object of the file is a catalog.");
    }

    [Fact]
    public void A_rebuild_the_catalog_search_sets_off_leaves_the_search_to_it()
    {
        // /Root names nothing. Looking among the indexed objects, the reader meets object 1 far from where its entry
        // says, and rebuilds the index, which finds no catalog either: the search ends there.
        var file = PdfTemplate.Build(PdfTemplate.Spread
            .Replace("<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Outlines >>", StringComparison.Ordinal)
            .Replace("{row:1}", "{row:3}", StringComparison.Ordinal)
            .Replace("/Root 1 0 R", "/Root 9 0 R", StringComparison.Ordinal));
        using var document = PdfDocument.Open(file);

        var report = new PdfValidator().Validate(document);

        document.WasRepaired.Should().BeTrue();
        document.Diagnostics.Contains(PdfDiagnosticCodes.TrailerRootRecovered).Should().BeFalse();
        Single(report, PdfValidationRuleIds.FileRootInvalid).Message.Should().Be(
            "The trailer's /Root names object 9 0, which the file does not hold. No object of the file is a catalog.");
        Single(report, PdfValidationRuleIds.XRefEntryBroken).Location.Object.Should().Be(new PdfObjectId(1));
    }

    [Fact]
    public void A_root_invalid_in_a_cross_reference_stream_is_reported_at_the_stream()
    {
        var file = Replace(
            new TestPdfBuilder()
                .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
                .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
                .BuildWithXRefStream(rootNumber: 1),
            "/Root 1 0 R",
            "/Root (cat)");

        var finding = Single(Validate(file), PdfValidationRuleIds.FileRootInvalid);

        finding.Location.Position.Should().Be(PdfTemplate.OffsetOf(file, "\n4 0 obj") + 1);
        finding.Message.Should().Be(
            "The trailer's /Root is a string, not a reference to the document catalog. The reader took object 1, which is one, for the catalog.");
    }

    [Fact]
    public void A_missing_root_with_no_section_to_locate_the_trailer_is_reported_at_the_document()
    {
        // A trailer the chain gave comes with the section that holds it; the rule, given a structure no file produces
        // — a chain read without a section, the index rebuilt from a file without startxref —, locates the finding at
        // the document rather than at no offset.
        using var document = PdfDocument.Open(PdfTemplate.SoundWith("startxref\n{xref:1}\n", string.Empty));
        var structure = document.Reader.Structure;
        structure.Sections.Should().BeEmpty();
        structure.ChainRead = true;
        structure.TrailerRead = true;
        structure.RootUsable = false;
        var context = new ValidationContext(document, capacity: 16);

        new RootInvalidRule().Check(context);

        var finding = context.ToReport(ValidationProfile.Structural).Findings.Should().ContainSingle().Which;
        finding.Location.IsDocument.Should().BeTrue();
        finding.Message.Should().Be("The trailer has no /Root. No object of the file is a catalog.");
    }

    [Theory]
    [InlineData("42", "is a number, not a document catalog")]
    [InlineData("<< /Kind /Catalog >>", "is a dictionary that is not a document catalog")]
    [InlineData("<< /Length 0 >>\nstream\n\nendstream", "is a stream, not a document catalog")]
    public void A_root_that_names_something_other_than_a_catalog_says_what(string body, string described)
    {
        var file = PdfTemplate.SoundWith("<< /Type /Catalog /Pages 2 0 R >>", body);

        Single(Validate(file), PdfValidationRuleIds.FileRootInvalid).Message
            .Should().Be($"The trailer's /Root names object 1 0, which {described}. No object of the file is a catalog.");
    }

    [Fact]
    public void A_catalog_without_type_that_holds_the_page_tree_is_not_this_rule_s()
    {
        // Readers take a dictionary with /Pages for the catalog; a missing /Type is the object-shape rules' (slice 3).
        Validate(PdfTemplate.SoundWith("<< /Type /Catalog /Pages 2 0 R >>", "<< /Pages 2 0 R >>")).Findings
            .Should().ContainSingle().Which.RuleId.Should().Be(PdfValidationRuleIds.ObjectKeyMissing);
    }

    [Theory]
    [InlineData("/Size 5", "gives /Size 5, where the highest object number it and the sections it updates use, 3, makes it 4.")]
    [InlineData("/Size 3", "gives /Size 3, where the highest object number it and the sections it updates use, 3, makes it 4.")]
    [InlineData("/Size /Four", "gives a /Size that is not a count of objects.")]
    [InlineData("/Size -4", "gives a /Size that is not a count of objects.")]
    [InlineData("", "has no /Size.")]
    public void A_size_that_is_not_one_more_than_the_highest_number_is_a_warning(string size, string message)
    {
        var file = PdfTemplate.SoundWith("/Size 4", size);

        var finding = Single(Validate(file), PdfValidationRuleIds.FileSizeWrong);

        finding.Severity.Should().Be(PdfValidationSeverity.Warning);
        finding.Location.Position.Should().Be(PdfTemplate.OffsetOf(file, "trailer"));
        finding.Message.Should().Be($"The trailer of the cross-reference table at offset {PdfTemplate.OffsetOf(file, "xref\n")} {message}");
    }

    [Theory]
    [InlineData("/Size 7", "/Size 6", null)]
    [InlineData("/Size 7", "/Size 7", null)]
    [InlineData("/Size 7", "/Size 9", "gives /Size 9, where the highest object number it and the sections it updates use, 5, makes it 6.")]
    [InlineData("/Size /Seven", "/Size 7", "gives /Size 7, where the highest object number it and the sections it updates use, 5, makes it 6.")]
    public void A_hybrid_file_s_stream_may_give_its_own_count_or_its_table_s(string tableSize, string streamSize, string? message)
    {
        // The table indexes objects 4 and 6, the stream; the stream indexes object 5. Table 17 asks the stream for
        // one more than its highest number, 6, and for its table's /Size, 7 — which a table that gives none cannot.
        var (file, stream) = Hybrid(tableSize, streamSize);

        var atStream = Validate(file).Findings
            .Where(finding => finding.RuleId == PdfValidationRuleIds.FileSizeWrong && finding.Location.Position == stream)
            .Select(finding => finding.Message);

        if (message is null)
        {
            atStream.Should().BeEmpty();
        }
        else
        {
            atStream.Should().ContainSingle().Which.Should().Be($"The cross-reference stream at offset {stream} {message}");
        }
    }

    [Fact]
    public void Free_entries_after_the_last_object_count_in_the_size()
    {
        var file = PdfTemplate.Build(PdfTemplate.Sound
            .Replace("0 4\n", "0 6\n", StringComparison.Ordinal)
            .Replace("{row:3}\n", "{row:3}\n{free}\n{free}\n", StringComparison.Ordinal)
            .Replace("/Size 4", "/Size 6", StringComparison.Ordinal));

        Validate(file).Findings.Should().BeEmpty();
    }

    [Fact]
    public void Each_update_counts_the_objects_of_the_sections_before_it()
    {
        var updated = TestPdfBuilder.AppendIncrementalUpdate(
            PdfTemplate.Build(PdfTemplate.Sound), rootNumber: 1, [(4, "(an update)")]);

        Validate(updated).Findings.Should().BeEmpty();
    }

    [Fact]
    public void An_update_whose_size_leaves_its_new_object_above_it_is_reported_once()
    {
        var updated = Replace(
            TestPdfBuilder.AppendIncrementalUpdate(PdfTemplate.Build(PdfTemplate.Sound), rootNumber: 1, [(4, "(an update)")]),
            "/Size 5",
            "/Size 3");

        var report = Validate(updated);

        var finding = report.Findings.Should().ContainSingle().Which;
        finding.RuleId.Should().Be(PdfValidationRuleIds.XRefObjectPastSize, "the /Size that leaves an object above it is that rule's alone");
        finding.Severity.Should().Be(PdfValidationSeverity.Warning, "the entry, the header and /Root agree against /Size (ADR 45)");
        finding.Location.Object.Should().Be(new PdfObjectId(4));
        finding.Message.Should().Be(
            "The index holds object 4 although the trailer's /Size is 3: the specification makes a conforming reader ignore an object numbered above it.");
    }

    [Fact]
    public void Objects_above_size_are_counted_once_for_the_file()
    {
        var file = PdfTemplate.SoundWith("/Size 4", "/Size 1");

        var finding = Single(Validate(file), PdfValidationRuleIds.XRefObjectPastSize);

        finding.Location.Object.Should().Be(new PdfObjectId(2));
        finding.Message.Should().Be(
            "The index holds 2 objects numbered above the trailer's /Size, 1, the first being object 2: the specification makes a conforming reader ignore them.");
    }

    [Fact]
    public void An_object_numbered_size_itself_is_not_above_it()
    {
        // Table 15 ignores objects whose number is greater than /Size; one equal to it only makes /Size wrong.
        var report = Validate(PdfTemplate.SoundWith("/Size 4", "/Size 3"));

        report.Contains(PdfValidationRuleIds.XRefObjectPastSize).Should().BeFalse();
        report.Contains(PdfValidationRuleIds.FileSizeWrong).Should().BeTrue();
    }

    /// <summary>
    /// The sound document updated as Word saves a file: a classic table indexing object 4 and object 6, the
    /// cross-reference stream its <c>/XRefStm</c> names, which indexes object 5 alone.
    /// </summary>
    private static (byte[] File, long Stream) Hybrid(string tableSize, string streamSize)
    {
        var original = PdfTemplate.Build(PdfTemplate.Sound);
        var file = new List<byte>(original);

        void Append(string text) => file.AddRange(Encoding.Latin1.GetBytes(text));

        var fourth = file.Count;
        Append("4 0 obj\n(in the table)\nendobj\n");
        var fifth = file.Count;
        Append("5 0 obj\n(only in the stream)\nendobj\n");
        var stream = file.Count;
        Append($"6 0 obj\n<< /Type /XRef /W [1 4 2] {streamSize} /Index [5 1] /Length 7 >>\nstream\n");
        file.AddRange([1, (byte)(fifth >> 24), (byte)(fifth >> 16), (byte)(fifth >> 8), (byte)fifth, 0, 0]);
        Append("\nendstream\nendobj\n");
        var table = file.Count;
        Append(string.Create(
            CultureInfo.InvariantCulture,
            $"xref\n4 1\n{fourth:D10} 00000 n \n6 1\n{stream:D10} 00000 n \ntrailer\n<< {tableSize} /Root 1 0 R /Prev {PdfTemplate.OffsetOf(original, "xref\n")} /XRefStm {stream} >>\nstartxref\n{table}\n%%EOF\n"));

        return ([.. file], stream);
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

    private static byte[] Replace(byte[] file, string text, string replacement)
    {
        var content = Encoding.Latin1.GetString(file);
        content.Should().Contain(text);
        return Encoding.Latin1.GetBytes(content.Replace(text, replacement, StringComparison.Ordinal));
    }
}
