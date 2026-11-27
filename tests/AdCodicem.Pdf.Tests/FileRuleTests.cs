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
    [InlineData(" ", "1 byte")]
    [InlineData("\n\n", "2 bytes")]
    public void The_bytes_before_the_header_are_counted_alike_by_the_reader_and_the_rule(string before, string count)
    {
        // A count of one in the singular, in the reader's report and in the rule's finding (#241).
        var file = Encoding.Latin1.GetBytes(before).Concat(PdfTemplate.Build(PdfTemplate.Sound)).ToArray();
        using var document = PdfDocument.Open(file);

        document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.XRefOffsetAdjusted).Which.Message.Should().Be(
            $"The PDF header starts {count} into the file; offsets were shifted accordingly.");
        Single(new PdfValidator().Validate(document), PdfValidationRuleIds.FileHeaderOffset).Message.Should().Be(
            $"The PDF header starts {count} into the file instead of at its first byte.");
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
    [InlineData("%PDF- 1.7", "The header names no version after %PDF-.")]
    [InlineData("%PDF-1.7abc", "The header names version 1.7abc, which is not a version of PDF: 1.0 to 1.7, or 2.0.")]
    [InlineData("%PDF-2.0x", "The header names version 2.0x, which is not a version of PDF: 1.0 to 1.7, or 2.0.")]
    [InlineData("%PDF-1.4.5.6", "The header names version 1.4.5.6, which is not a version of PDF: 1.0 to 1.7, or 2.0.")]
    [InlineData("%PDF-1.99999999", "The header names version 1.99999999, which is not a version of PDF: 1.0 to 1.7, or 2.0.")]
    [InlineData("%PDF-1.7\u001B[2J#", "The header names version 1.7#1B#5B2J#23, which is not a version of PDF: 1.0 to 1.7, or 2.0.")]
    public void A_header_that_names_no_version_of_pdf_is_a_warning(string header, string message)
    {
        var finding = Single(Validate(PdfTemplate.SoundWith("%PDF-1.7", header)), PdfValidationRuleIds.FileHeaderVersionInvalid);

        finding.Severity.Should().Be(PdfValidationSeverity.Warning);
        finding.Location.Position.Should().Be(0);
        finding.Message.Should().Be(message);
    }

    [Theory]
    [InlineData(0, 10_000, 4091)]
    [InlineData(0, 4092, 4091)]
    [InlineData(500, 3_800, 3591)]
    public void A_header_s_text_the_header_search_cuts_is_quoted_as_at_least_what_was_read(int before, int length, int read)
    {
        var file = Encoding.Latin1.GetBytes(new string(' ', before))
            .Concat(PdfTemplate.SoundWith("%PDF-1.7", "%PDF-" + new string('K', length)))
            .ToArray();

        var finding = Single(Validate(file), PdfValidationRuleIds.FileHeaderVersionInvalid);

        finding.Message.Should().Be(
            "The header names version " + new string('K', 127) +
            string.Create(CultureInfo.InvariantCulture, $" (the first 127 of at least {read} bytes), which is not a version of PDF: 1.0 to 1.7, or 2.0."));
    }

    [Fact]
    public void A_header_s_long_text_is_quoted_as_a_keyword_bounded_with_its_whole_length()
    {
        var finding = Single(
            Validate(PdfTemplate.SoundWith("%PDF-1.7", "%PDF-1.7" + new string('x', 300))),
            PdfValidationRuleIds.FileHeaderVersionInvalid);

        finding.Message.Should().Be(
            "The header names version 1.7" + new string('x', 124) +
            " (the first 127 of 303 bytes), which is not a version of PDF: 1.0 to 1.7, or 2.0.");
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
    public void A_trailer_keyword_followed_by_no_dictionary_is_malformed_and_its_table_read_in_part_the_catalog_found_among_its_rows()
    {
        // The keyword followed by no dictionary loses the trailer, and with it the older sections it would name: the table is
        // read in part, the catalog found among its rows, and the index rebuilt only once the validation settles it (#322).
        var file = PdfTemplate.SoundWith("<< /Size 4 /Root 1 0 R >>", "/Size 4 /Root 1 0 R >>");
        using var document = PdfDocument.Open(file);

        document.WasRepaired.Should().BeFalse("what the table lost is looked for when it is asked for");
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(
            PdfDiagnosticCodes.XRefSectionUnreadable, PdfDiagnosticCodes.TrailerRootRecovered);
        document.Diagnostics[0].Message.Should().Be(
            $"The cross-reference table startxref names at offset {PdfTemplate.OffsetOf(file, "xref")} was read only in part: its trailer keyword is followed by no dictionary; the rows read were kept, and what only the sections its trailer would name index is looked for by rebuilding the index.");

        var report = new PdfValidator().Validate(document);

        report.Findings.Should().ContainSingle();
        var finding = Single(report, PdfValidationRuleIds.FileTrailerMalformed);
        finding.Severity.Should().Be(PdfValidationSeverity.Error);
        finding.Message.Should().Be($"The trailer keyword at offset {PdfTemplate.OffsetOf(file, "trailer")} is not followed by a dictionary.");
        document.Diagnostics.Count(diagnostic => diagnostic.Code == PdfDiagnosticCodes.TrailerRootRecovered).Should().Be(1);
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
    public void A_trailer_whose_closing_brackets_end_its_table_s_window_is_judged_by_what_they_close()
    {
        // The trailer's last key has no value, and the ">>" after it ends exactly where the table's 64 KB window does. The
        // brackets are whole there, so the trailer is read through that window alone, and their fault is still reported.
        var file = TrailerEndingTheTableWindow(0);
        var padding = PdfTemplate.OffsetOf(file, "xref\n0 3000") + (64 * 1024) - (PdfTemplate.OffsetOf(file, "/K >>") + "/K >>".Length);
        file = TrailerEndingTheTableWindow((int)padding);
        using var document = PdfDocument.Open(file);

        var report = new PdfValidator().Validate(document);

        document.Diagnostics.Should().ContainSingle().Which.Message.Should().Be("A dictionary key had no value.");
        Single(report, PdfValidationRuleIds.FileTrailerMalformed);

        static byte[] TrailerEndingTheTableWindow(int padding)
        {
            var table = new StringBuilder("xref\n0 3000\n{free}\n{row:1}\n{row:2}\n{row:3}\n");
            for (var row = 4; row < 3000; row++)
            {
                table.Append("0000000000 65535 f \n");
            }

            return PdfTemplate.Build(
                PdfTemplate.Sound[..PdfTemplate.Sound.IndexOf("xref\n", StringComparison.Ordinal)] + table +
                "trailer\n<< /Size 3000 /Root 1 0 R /Pad (" + new string('x', padding) + ") /K >>\nstartxref\n{xref:1}\n%%EOF\n");
        }
    }

    [Fact]
    public void A_trailer_its_bound_cuts_after_a_syntax_fault_is_malformed()
    {
        // The trailer runs past its table's window and is read through one of its own, which MaxTrailerLength cuts after a
        // stray token: the token is the file's, kept beside the guard, and the trailer was read despite it.
        var table = new StringBuilder("xref\n0 3000\n{free}\n{row:1}\n{row:2}\n{row:3}\n");
        for (var row = 4; row < 3000; row++)
        {
            table.Append("0000000000 65535 f \n");
        }

        var file = PdfTemplate.Build(
            PdfTemplate.Sound[..PdfTemplate.Sound.IndexOf("xref\n", StringComparison.Ordinal)] + table +
            "trailer\n<< /Size 3000 /Root 1 0 R ) /Pad (" + new string('x', 10_000) + ") >>\nstartxref\n{xref:1}\n%%EOF\n");
        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxTrailerLength = 1024 } });

        var report = new PdfValidator().Validate(document);

        document.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.LimitTrailer, PdfDiagnosticCodes.SyntaxUnexpectedToken);
        report.Findings.Select(finding => finding.RuleId).Should().Contain(PdfValidationRuleIds.FileTrailerMalformed);
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
    [InlineData("/Root 9 0 R", "The trailer's /Root names object 9 0, which read as null through the cross-reference chain.")]
    [InlineData("/Root 0 0 R", "The trailer's /Root names object 0 0, which read as null through the cross-reference chain.")]
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
            .Should().Be("The trailer's /Root names object 1 0, which is a dictionary of /Type /Outlines, not a document catalog. No object the reader could read is a catalog.");
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
            "The trailer's /Root names object 9 0, which read as null through the cross-reference chain. No object the reader could read is a catalog.");
        Single(report, PdfValidationRuleIds.XRefEntryBroken).Location.Object.Should().Be(new PdfObjectId(1));
    }

    [Fact]
    public void A_rebuild_the_catalog_search_sets_off_takes_the_catalog_the_search_was_reading_once()
    {
        // /Root names nothing. Looking among the indexed objects, the reader meets object 1, the catalog, far from where its
        // entry says, and rebuilds the index, whose own search takes it: the search that set the rebuild off ends there,
        // rather than taking the catalog it was reading a second time.
        var file = PdfTemplate.Build(PdfTemplate.Spread
            .Replace("{row:1}", "{row:3}", StringComparison.Ordinal)
            .Replace("/Root 1 0 R", "/Root 9 0 R", StringComparison.Ordinal));
        using var document = PdfDocument.Open(file);

        document.WasRepaired.Should().BeTrue();
        document.Trailer.GetRaw(PdfName.Root).Should().BeOfType<PdfReference>().Which.Id.Should().Be(new PdfObjectId(1));
        document.Diagnostics.Where(diagnostic => diagnostic.Code == PdfDiagnosticCodes.TrailerRootRecovered).Should().ContainSingle();
    }

    private const string Outlines = """
        %PDF-1.7
        1 0 obj
        << /Type /Outlines >>
        endobj
        2 0 obj
        << /Type /Pages /Kids [3 0 R] /Count 1 >>
        endobj
        3 0 obj
        << /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << >> >>
        endobj

        """;

    private const string SoundObjects = """
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

        """;

    private const string RebuiltToOne = " The reader rebuilt the index, through which /Root leads to object 1, which is one.";

    [Theory]
    [InlineData(
        Outlines + "xref\n0 4\n{free}\n{row:1}\n{row:2}\n{row:3}\ntrailer\n<< /Size 4 /Root 1 0 R >>\nstartxref\n{xref:1}\n%%EOF\n1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n",
        0, "The trailer's /Root names object 1 0, which the cross-reference chain read as a dictionary of /Type /Outlines, not a document catalog." + RebuiltToOne)]
    [InlineData(
        SoundObjects + "xref\n0 1\n{free}\ntrailer\n<< /Size 4 /Root 1 0 R >>\nstartxref\n{xref:1}\n%%EOF\n",
        0, "The trailer's /Root names object 1 0, which read as null through the cross-reference chain." + RebuiltToOne)]
    [InlineData(
        SoundObjects + "4 0 obj\n<< /Type /XRef /Size 4 /W [1 4 2] /Root 1 0 R /Filter /ASCIIHexDecode /Length 57 >>\nstream\n03000000000000030000000000000300000000000003000000000000>\nendstream\nendobj\nstartxref\n{off:4}\n%%EOF\n",
        0, "The trailer's /Root names object 1 0, which read as null through the cross-reference chain." + RebuiltToOne)]
    [InlineData(
        Outlines + "5 0 obj\n<<                                                                                                                                                                                                        /Type /Catalog /Pages 2 0 R >>\nendobj\nxref\n0 4\n{free}\n{row:1}\n{row:2}\n{row:3}\n5 1\n{row:5}\ntrailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n{xref:1}\n%%EOF\n",
        128, "The trailer's /Root names object 1 0, which is a dictionary of /Type /Outlines, not a document catalog. No object the reader read whole is a catalog: one of the reader's limits cut some, which its report names.")]
    [InlineData(
        SoundObjects + "4 0 obj\n1 0 R\nendobj\nxref\n0 1\n{free}\ntrailer\n<< /Size 5 /Root 4 0 R >>\nstartxref\n{xref:1}\n%%EOF\n",
        0, "The trailer's /Root names object 4 0, which read as null through the cross-reference chain." + RebuiltToOne)]
    [InlineData(
        Outlines + "5 0 obj\n<<                                                                                                                                                                                                        /Type /Catalog /Pages 2 0 R >>\nendobj\nxref\n0 4\n{free}\n{row:1}\n{row:2}\n{row:3}\ntrailer\n<< /Size 6 /Root 5 0 R >>\nstartxref\n{xref:1}\n%%EOF\n",
        128, "The trailer's /Root names object 5 0, which read as null through the cross-reference chain. The reader rebuilt the index, through which /Root leads to object 5, which one of the reader's limits cut or left out of the index, and looks for no other.")]
    [InlineData(
        Outlines + "5 0 obj\n<<                                                                                                                                                                                                        /Type /Catalog /Pages 2 0 R >>\nendobj\nxref\n0 4\n{free}\n{row:1}\n{row:2}\n{row:3}\n5 1\n{row:5}\ntrailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n{xref:1}\n%%EOF\n5 0 obj\n(short)\nendobj\n",
        128, "The trailer's /Root names object 1 0, which is a dictionary of /Type /Outlines, not a document catalog. No object the reader could read is a catalog.")]
    public void A_root_that_leads_to_no_catalog_ends_with_what_the_reader_knows_of_the_catalog(string template, int maxObjectLength, string message)
    {
        // A later definition of object 1 only a rebuild finds; a chain whose only row frees object 0; a chain whose rows are
        // all of a reserved type; a candidate MaxObjectLength cuts before its /Type; a /Root that names a reference to the
        // catalog; a /Root the chain does not place, which a rebuild finds cut; and a candidate the chain's search meets cut,
        // which the rebuilt index defines again whole. The finding says what the reader knows: the catalog a rebuild made /Root
        // lead to, the cut one it takes as read, or none among the objects it read whole or could read (#132, #260).
        var file = PdfTemplate.Build(template);
        var options = maxObjectLength > 0 ? new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = maxObjectLength } } : null;
        using var document = PdfDocument.Open(file, options);

        Single(new PdfValidator().Validate(document), PdfValidationRuleIds.FileRootInvalid).Message.Should().Be(message);
    }

    [Theory]
    [InlineData("chain", "<< /Type /Catalog /Pages 2 0 R >>", "file.root-direct")]
    [InlineData("chain", "<< /Pages 2 0 R >>", "file.root-direct object.key-missing")]
    [InlineData("chain", "<< /Type /Outlines >>", "file.root-invalid")]
    [InlineData("chain", "1 0 R", "")]
    [InlineData("rebuild", "<< /Type /Catalog /Pages 2 0 R >>", "file.root-direct")]
    public void A_catalog_written_in_the_trailer_is_taken_as_written_and_said_to_be_no_reference(string trailer, string root, string rules)
    {
        // /Root written in the trailer as a dictionary of /Type /Catalog, or one holding /Pages: the reader takes it as the
        // catalog it is, and file.root-direct says it is no reference, in the trailer the chain gave or, the chain giving no
        // index, in the one the rebuild merged; one with no /Type lacks the key the catalog requires. A dictionary that is no
        // catalog is file.root-invalid's, the reader taking object 1 for the catalog; a reference is neither's.
        var file = PdfTemplate.SoundWith("<< /Size 4 /Root 1 0 R >>", $"<< /Size 4 /Root {root} >>");

        if (trailer == "rebuild")
        {
            var text = Encoding.Latin1.GetString(file);
            var startXRef = text.LastIndexOf("startxref\n", StringComparison.Ordinal) + "startxref\n".Length;
            file = Encoding.Latin1.GetBytes(string.Concat(text.AsSpan(0, startXRef), "9", text.AsSpan(text.IndexOf('\n', startXRef))));
        }

        using var document = PdfDocument.Open(file);

        var report = new PdfValidator().Validate(document);

        document.Reader.HasCatalog.Should().BeTrue();
        document.Reader.Structure.ChainRead.Should().Be(trailer == "chain");
        report.Findings.Select(finding => finding.RuleId).Where(id => id != PdfValidationRuleIds.FileStartXRefWrong).Should().Equal(
            rules.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        if (rules.StartsWith(PdfValidationRuleIds.FileRootDirect, StringComparison.Ordinal))
        {
            var finding = Single(report, PdfValidationRuleIds.FileRootDirect);
            finding.Severity.Should().Be(PdfValidationSeverity.Warning);
            finding.Location.Position.Should().Be(PdfTemplate.OffsetOf(file, "trailer"));
            finding.Message.Should().Be("The trailer's /Root is the document catalog written in the trailer, not an indirect reference to it.");
        }
    }

    [Fact]
    public void A_catalog_a_caller_writes_in_a_trailer_the_file_lacks_is_said_to_be_no_reference_of_the_document()
    {
        // The file holds no trailer: the index is rebuilt as it opens, and the search takes catalog 1. A caller writes that
        // catalog in the trailer in place of the reference to it: no trailer of the file holds it, and the finding is the
        // document's.
        var text = Encoding.Latin1.GetString(PdfTemplate.Build(PdfTemplate.Sound));
        var file = Encoding.Latin1.GetBytes(text[..text.IndexOf("xref\n", StringComparison.Ordinal)]);
        using var document = PdfDocument.Open(file);
        document.Trailer.Set(PdfName.Root, document.GetObject(1));

        var finding = Single(new PdfValidator().Validate(document), PdfValidationRuleIds.FileRootDirect);

        document.Reader.Structure.ChainRead.Should().BeFalse();
        document.Reader.TrailerLocation.Should().Be(-1);
        finding.Location.IsDocument.Should().BeTrue();
    }

    [Theory]
    [InlineData("<< /Pad (", "file.root-direct")]
    [InlineData("1 0 R /Pad (", "")]
    public void A_catalog_written_in_a_trailer_a_limit_cut_is_said_to_be_no_reference(string root, string rules)
    {
        // MaxTrailerLength cuts the trailer within the catalog written in it, before its /Type and its /Pages: the reader takes
        // what it read for the catalog, looking for no other (#175), which file.root-invalid does not judge; that it is no
        // reference was read before the cut. A reference before the cut is neither's.
        var file = PdfTemplate.SoundWith(
            "<< /Size 4 /Root 1 0 R >>", $"<< /Size 4 /Root {root}{new string('x', 400)}) /Type /Catalog /Pages 2 0 R >> >>");
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxTrailerLength = 300, MaxXRefSectionLength = 128 } };
        using var document = PdfDocument.Open(file, options);

        var report = new PdfValidator().Validate(document);

        document.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == PdfDiagnosticCodes.LimitTrailer);
        report.Findings.Select(finding => finding.RuleId).Where(id => id is PdfValidationRuleIds.FileRootDirect or PdfValidationRuleIds.FileRootInvalid)
            .Should().Equal(rules.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public void A_root_written_in_a_cross_reference_stream_dictionary_the_end_of_the_file_leaves_open_is_not_judged()
    {
        // A file whose cross-reference stream comes first, as a linearized one's does, cut inside that stream's dictionary
        // after its /Root: no reader takes a dictionary left open for a trailer, and the rebuild merges none, so the finding
        // says no trailer gives /Root — what the reader read, the file holding no catalog either (#381).
        var file = Encoding.Latin1.GetBytes("%PDF-1.7\n5 0 obj\n<< /Type /XRef /Size 6 /W [1 4 2] /Root 1 0 R /Length 70 /Index [0 6");
        using var document = PdfDocument.Open(file);

        var report = new PdfValidator().Validate(document);

        Single(report, PdfValidationRuleIds.FileRootInvalid).Message.Should().Be(
            "No trailer the reader read gives /Root. No object the reader could read is a catalog.");
    }

    [Fact]
    public void A_root_only_an_older_trailer_gives_which_a_rebuild_merges_is_said_to_lead_to_the_catalog()
    {
        // The newest table indexes new copies of objects 2 and 3 and gives no /Root; its /Prev is a reference to a free row, so
        // the chain never reaches the older table, which gives /Root. The rebuild merges the older trailer, and /Root leads to
        // the catalog: the finding says so, not that no object is one (#260).
        var file = new HandWrittenFile();
        var catalog = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        var pages = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        var page = file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << >> >>");
        file.Table(4, new() { [1] = catalog, [2] = pages, [3] = page }, "/Size 4 /Root 1 0 R");
        var newPages = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        var newPage = file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> >>");
        var newest = file.Position;
        file.Raw(string.Create(
            CultureInfo.InvariantCulture,
            $"xref\n0 1\n0000000000 65535 f \n2 2\n{newPages:D10} 00000 n \n{newPage:D10} 00000 n \n21 1\n0000000000 00001 f \ntrailer\n<< /Size 22 /Prev 21 0 R >>\nstartxref\n{newest}\n%%EOF\n"));
        using var document = PdfDocument.Open(file.Bytes);

        Single(new PdfValidator().Validate(document), PdfValidationRuleIds.FileRootInvalid).Message.Should().Be("The trailer has no /Root." + RebuiltToOne);
    }

    [Fact]
    public void A_root_the_chain_places_in_an_object_stream_that_cannot_be_decoded_reads_as_null_through_the_chain_and_the_rebuild_serves_it()
    {
        // An update places the catalog, object 1, in object stream 6, whose /FlateDecode data is no zlib data, so that it
        // holds no object; the older revision writes the catalog directly. /Root read as null through the chain, and the
        // rebuild serves the older definition: the finding says both, guessing no cause for the first (#260).
        var file = new HandWrittenFile();
        var catalog = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        var pages = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        var page = file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << >> >>");
        var oldest = file.Table(4, new() { [1] = catalog, [2] = pages, [3] = page }, "/Size 4 /Root 1 0 R");
        var stream = file.Object(
            6, "<< /Type /ObjStm /N 1 /First 4 /Filter /FlateDecode /Length 30 >>\nstream\nthis is not zlib data at all..\nendstream");
        file.CrossReferenceStream(
            7,
            8,
            new() { [1] = (2, 6, 0), [2] = (1, pages, 0), [3] = (1, page, 0), [6] = (1, stream, 0) },
            string.Create(CultureInfo.InvariantCulture, $"/Root 1 0 R /Prev {oldest}"));
        using var document = PdfDocument.Open(file.Bytes);

        Single(new PdfValidator().Validate(document), PdfValidationRuleIds.FileRootInvalid).Message.Should().Be(
            "The trailer's /Root names object 1 0, which read as null through the cross-reference chain." + RebuiltToOne);
    }

    [Fact]
    public void A_catalog_an_older_trailer_writes_in_itself_which_a_rebuild_merges_is_said_to_be_the_trailer_s()
    {
        // As above, the older trailer writing the catalog in itself rather than referring to it: the rebuild merges it, and
        // the reader takes it as it is (#260).
        var file = new HandWrittenFile();
        var pages = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        var page = file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << >> >>");
        file.Table(4, new() { [2] = pages, [3] = page }, "/Size 4 /Root << /Type /Catalog /Pages 2 0 R >>");
        var newPage = file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> >>");
        var newest = file.Position;
        file.Raw(string.Create(
            CultureInfo.InvariantCulture,
            $"xref\n0 1\n0000000000 65535 f \n3 1\n{newPage:D10} 00000 n \n21 1\n0000000000 00001 f \ntrailer\n<< /Size 22 /Prev 21 0 R >>\nstartxref\n{newest}\n%%EOF\n"));
        using var document = PdfDocument.Open(file.Bytes);

        Single(new PdfValidator().Validate(document), PdfValidationRuleIds.FileRootInvalid).Message.Should().Be(
            "The trailer has no /Root. The reader rebuilt the index, whose trailer gives one.");
    }

    [Fact]
    public void A_document_rebuilt_as_it_opens_whose_last_catalog_the_end_of_the_file_cuts_says_no_object_it_could_read_is_one()
    {
        // An update redefines the catalog and the file ends inside it; startxref names no section. The rebuild keeps the
        // last definition, which gives neither /Type nor /Pages: no catalog is served, though the older one is whole, which
        // the finding does not deny (#132).
        var file = PdfTemplate.Build(SoundObjects + "xref\n0 4\n{free}\n{row:1}\n{row:2}\n{row:3}\ntrailer\n<< /Size 4 /Root 1 0 R >>\nstartxref\n9\n%%EOF\n1 0 obj\n<< /Ty");
        using var document = PdfDocument.Open(file);

        Single(new PdfValidator().Validate(document), PdfValidationRuleIds.FileRootInvalid).Message.Should().Be(
            "The trailer's /Root names object 1 0, which is a dictionary that is not a document catalog. No object the reader could read is a catalog.");
    }

    [Fact]
    public void A_document_rebuilt_as_it_opens_whose_index_a_guard_filled_is_not_judged_for_its_catalog()
    {
        // MaxIndexEntries fills the rebuilt index before the catalog, object 9: whether a catalog lies past the guard is
        // unknown, and xref.checked-in-part says the index was stopped short (#132, ADR 34).
        var template = Outlines +
            string.Concat(Enumerable.Range(4, 5).Select(number => string.Create(CultureInfo.InvariantCulture, $"{number} 0 obj\n<< /F {number} >>\nendobj\n"))) +
            "9 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\nxref\n0 10\n{free}\n" +
            string.Concat(Enumerable.Range(1, 9).Select(number => string.Create(CultureInfo.InvariantCulture, $"{{row:{number}}}\n"))) +
            "trailer\n<< /Size 10 /Root 1 0 R >>\nstartxref\n9\n%%EOF\n";
        using var document = PdfDocument.Open(PdfTemplate.Build(template), new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxIndexEntries = 6 } });

        var report = new PdfValidator().Validate(document);

        document.Reader.Structure.ChainRead.Should().BeFalse();
        report.Contains(PdfValidationRuleIds.FileRootInvalid).Should().BeFalse();
        report.Contains(PdfValidationRuleIds.XRefCheckedInPart).Should().BeTrue();
    }

    [Fact]
    public void A_document_rebuilt_as_it_opens_whose_object_a_guard_cut_is_not_judged_for_its_catalog()
    {
        // MaxObjectLength cuts object 5 before its /Type, and startxref names no section. /Root names an outline: no object
        // the reader read whole is a catalog, but the cut one may be, or an object stream that holds one (#132, ADR 34).
        var template = Outlines + "5 0 obj\n<<" + new string(' ', 200) + "/Type /Catalog /Pages 2 0 R >>\nendobj\n" +
            "xref\n0 4\n{free}\n{row:1}\n{row:2}\n{row:3}\ntrailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n99999\n%%EOF\n";
        using var document = PdfDocument.Open(PdfTemplate.Build(template), CutAt(128));

        var report = new PdfValidator().Validate(document);

        document.Reader.Structure.ChainRead.Should().BeFalse();
        document.Reader.IsCutAtLimit(5).Should().BeTrue();
        document.Reader.RootLeadsPastGuard.Should().BeFalse();
        document.Reader.ObjectsLeftOutByGuard.Should().BeTrue();
        report.Contains(PdfValidationRuleIds.FileRootInvalid).Should().BeFalse(string.Join("; ", report.Findings));
    }

    [Fact]
    public void A_root_the_chain_does_not_place_which_a_rebuild_leaves_out_of_its_full_index_is_said_to_lead_past_the_guard()
    {
        // The chain places objects 1 to 3, not object 13, which /Root names. The rebuild run as the document opens fills its
        // index before object 13: the reader takes /Root as it leads there and looks for no other catalog (#260, ADR 34).
        var template = Outlines +
            string.Concat(Enumerable.Range(4, 9).Select(number => string.Create(CultureInfo.InvariantCulture, $"{number} 0 obj\n<< /F {number} >>\nendobj\n"))) +
            "13 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\nxref\n0 4\n{free}\n{row:1}\n{row:2}\n{row:3}\ntrailer\n<< /Size 14 /Root 13 0 R >>\nstartxref\n{xref:1}\n%%EOF\n";
        using var document = PdfDocument.Open(PdfTemplate.Build(template), new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxIndexEntries = 8 } });

        var report = new PdfValidator().Validate(document);

        document.Reader.Structure.ChainRead.Should().BeTrue();
        document.Reader.IsCutAtLimit(13).Should().BeFalse();
        Single(report, PdfValidationRuleIds.FileRootInvalid).Message.Should().Be(
            "The trailer's /Root names object 13 0, which read as null through the cross-reference chain. The reader rebuilt the index, through which /Root leads to object 13, which one of the reader's limits cut or left out of the index, and looks for no other.");
        report.Contains(PdfValidationRuleIds.XRefCheckedInPart).Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_document_rebuilt_as_it_opens_whose_root_a_guard_cut_is_not_judged_for_its_catalog(bool member)
    {
        // startxref names no section, and the catalog /Root names is cut: MaxObjectLength cuts it before its /Type, or
        // MaxDecodedStreamLength cuts the data of the object stream that holds it before it starts. The reader takes the
        // cut object as read and looks for no other, and its report names the guard (#132, ADR 34).
        byte[] file;
        PdfReaderLimits limits;

        if (member)
        {
            file = new TestPdfBuilder()
                .WithObject(4, "(" + new string('x', 300) + ")")
                .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
                .WithObject(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
                .WithObject(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 1 1] /Resources << >> >>")
                .BuildWithXRefStream(rootNumber: 1, compressedObjects: [4, 1], compressObjectStream: true);
            limits = PdfReaderLimits.Default with { MaxDecodedStreamLength = 100 };
        }
        else
        {
            file = PdfTemplate.Build(
                SoundObjects.Replace("<< /Type /Catalog", "<<" + new string(' ', 200) + "/Type /Catalog", StringComparison.Ordinal) +
                "xref\n0 4\n{free}\n{row:1}\n{row:2}\n{row:3}\ntrailer\n<< /Size 4 /Root 1 0 R >>\nstartxref\n9\n%%EOF\n");
            limits = PdfReaderLimits.Default with { MaxObjectLength = 128 };
        }

        var text = Encoding.Latin1.GetString(file);
        var startXRef = text.LastIndexOf("startxref\n", StringComparison.Ordinal) + "startxref\n".Length;
        file = Encoding.Latin1.GetBytes(string.Concat(text.AsSpan(0, startXRef), "99999", text.AsSpan(text.IndexOf('\n', startXRef))));
        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = limits });

        var report = new PdfValidator().Validate(document);

        document.Reader.Structure.ChainRead.Should().BeFalse();
        document.Reader.IsCutAtLimit(1).Should().BeTrue();
        document.Reader.RootLeadsPastGuard.Should().BeTrue();
        document.Reader.HasCatalog.Should().BeFalse();
        document.Reader.ObjectsLeftOutByGuard.Should().Be(!member, "a regular object a guard cut may be an object stream past the cut; a member is none");
        report.Contains(PdfValidationRuleIds.FileRootInvalid).Should().BeFalse(string.Join("; ", report.Findings));
        document.Diagnostics.Should().Contain(diagnostic => diagnostic.Code.StartsWith("limit.", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("<< /Type /Outlines >>", "/Root 5 0 R", "The trailer's /Root names object 5 0, which reads as null. No object the reader could read is a catalog.")]
    [InlineData("<< /Type /Outlines >>", "", "No trailer the reader read gives /Root. No object the reader could read is a catalog.")]
    [InlineData("1 0 R", "/Root 1 0 R", "The trailer's /Root names object 1 0, which reads as null: its chain of references loops back to object 1 0. No object the reader could read is a catalog.")]
    [InlineData("<< /Type /Catalog /Pages 2 0 R >>", "/Root 1 0 R", null)]
    [InlineData("<< /Type /Catalog /Pages 2 0 R >>", "/Root 5 0 R", null)]
    [InlineData(
        "<< /Type /Outlines >>",
        "/Root << /Type /Outlines >>",
        "The trailer's /Root is a dictionary written in the trailer, not a reference to the document catalog. No object the reader could read is a catalog.")]
    public void A_document_rebuilt_as_it_opens_is_judged_by_its_trailers_root_when_no_object_is_a_catalog(string one, string root, string? message)
    {
        // startxref names no section, and the index is rebuilt as the document opens. Where no object of the file is a catalog,
        // the trailer the rebuild merged is judged; where its /Root leads to one, or a search finds one, the document has a way
        // in, and the rules that judge the chain say what went wrong (#132). A dictionary written in the trailer that is no
        // catalog is no catalog written there either.
        var file = PdfTemplate.SoundWith("<< /Type /Catalog /Pages 2 0 R >>", one);
        file = Encoding.Latin1.GetBytes(Encoding.Latin1.GetString(file)
            .Replace("/Root 1 0 R", root, StringComparison.Ordinal)
            .Replace(string.Create(CultureInfo.InvariantCulture, $"startxref\n{PdfTemplate.OffsetOf(file, "xref\n")}\n"), "startxref\n9\n", StringComparison.Ordinal));
        using var document = PdfDocument.Open(file);

        var report = new PdfValidator().Validate(document);

        document.Reader.Structure.ChainRead.Should().BeFalse();
        report.Contains(PdfValidationRuleIds.FileStartXRefWrong).Should().BeTrue();
        report.Findings.Where(finding => finding.RuleId == PdfValidationRuleIds.FileRootInvalid).Select(finding => finding.Message)
            .Should().Equal(message is null ? [] : [message]);
        report.Contains(PdfValidationRuleIds.FileRootDirect).Should().BeFalse();
    }

    [Theory]
    [InlineData(22)]
    [InlineData(24)]
    public void A_catalog_a_limit_cut_inside_its_type_is_not_judged(int maxObjectLength)
    {
        // MaxObjectLength cuts the catalog inside the name its /Type gives: the parser drops what the window holds of it,
        // /Cata or /Catalo (#279), and the catalog reads with no /Type, which the rule does not take for another type: what
        // the catalog lacks may lie past the cut, and nothing is rebuilt for it (#175).
        var file = PdfTemplate.Build(PdfTemplate.Sound);
        using var document = PdfDocument.Open(file, CutAt(maxObjectLength));

        var report = new PdfValidator().Validate(document);

        report.Findings.Should().BeEmpty("what the catalog lacks may lie past the cut");
        document.Catalog.Required().GetRaw(PdfName.Type).Should().BeNull();
        document.Reader.Structure.RootCutByGuard.Should().BeTrue();
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(PdfDiagnosticCodes.LimitObject);
    }

    [Fact]
    public void A_catalog_a_limit_cut_before_its_type_is_taken_as_read_rather_than_an_older_one()
    {
        // An older catalog, object 5, with a tree of its own, which the reader took for the catalog and walked.
        var file = OlderCatalogBeside(cut: true);
        using var document = PdfDocument.Open(file, CutAt(128));

        var report = new PdfValidator().Validate(document);

        report.Findings.Should().BeEmpty();
        document.WasRepaired.Should().BeFalse();
        document.Reader.CatalogFoundAs.Should().Be(0);
        PageTreeWalk.Run(document).PageCount.Should().Be(0, "the tree lies past the cut");

        // Read whole, object 1 is the catalog, and the older tree's page one no reader shows.
        using var whole = PdfDocument.Open(file);
        new PdfValidator().Validate(whole).Findings.Select(finding => (finding.RuleId, finding.Location.Object)).Should().Equal(
            (PdfValidationRuleIds.PageTreePageOrphaned, new PdfObjectId(7)));
    }

    [Fact]
    public void A_catalog_that_is_none_is_still_judged_beside_one_a_limit_cut_elsewhere()
    {
        // Object 1 is read whole and is no catalog; object 8, which nothing names, is the one a limit cuts.
        var file = PdfTemplate.Build(OlderCatalogBesideTemplate(cut: false)
            .Replace("<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Outlines >>", StringComparison.Ordinal));
        using var document = PdfDocument.Open(file, CutAt(128));

        var finding = Single(new PdfValidator().Validate(document), PdfValidationRuleIds.FileRootInvalid);

        finding.Message.Should().Be(
            "The trailer's /Root names object 1 0, which is a dictionary of /Type /Outlines, not a document catalog. The reader took object 5, which is one, for the catalog.");
        document.Reader.Structure.RootCutByGuard.Should().BeFalse();
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
        finding.Message.Should().Be("The trailer has no /Root." + RebuiltToOne);
    }

    [Theory]
    [InlineData("42", "is a number, not a document catalog")]
    [InlineData("<< /Kind /Catalog >>", "is a dictionary that is not a document catalog")]
    [InlineData("<< /Length 0 >>\nstream\n\nendstream", "is a stream, not a document catalog")]
    public void A_root_that_names_something_other_than_a_catalog_says_what(string body, string described)
    {
        var file = PdfTemplate.SoundWith("<< /Type /Catalog /Pages 2 0 R >>", body);

        Single(Validate(file), PdfValidationRuleIds.FileRootInvalid).Message
            .Should().Be($"The trailer's /Root names object 1 0, which {described}. No object the reader could read is a catalog.");
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
    [InlineData("/Size -1", "gives a /Size that is not a count of objects.")]
    [InlineData("/Size 4.5", "gives a /Size that is not a count of objects.")]
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

    [Theory]
    [InlineData(7, null)]
    [InlineData(6, "gives /Size 6, where the highest object number it and the sections it updates use, 6, makes it 7.")]
    public void A_stream_two_tables_name_counts_for_each_table_s_size(int olderSize, string? message)
    {
        // #156's shape: the update's table indexes objects 4 and 5, the stream its /XRefStm names, which indexes object 6;
        // the newest table copies that /XRefStm. The update's naming left no record, and its /Size was judged without the
        // stream's rows: a right /Size 7 was said to make it 6, and a wrong /Size 6 passed. The stream counts for each table.
        var (file, _, older, _, _) = HybridFiles.SharedStream(olderSize);

        var findings = Validate(file).Findings;

        if (message is null)
        {
            findings.Should().BeEmpty();
        }
        else
        {
            var finding = findings.Should().ContainSingle().Which;
            finding.RuleId.Should().Be(PdfValidationRuleIds.FileSizeWrong);
            finding.Message.Should().Be($"The trailer of the cross-reference table at offset {older} {message}");
        }
    }

    [Theory]
    [InlineData(7, null)]
    [InlineData(5, "gives /Size 5, where the highest object number it and the sections it updates use, 6, makes it 7.")]
    public void A_stream_two_tables_name_is_judged_once_where_the_oldest_names_it(int streamSize, string? message)
    {
        // The newest table copies the update's /XRefStm two revisions late, a table indexing object 8 between: judged where
        // the newest names it, a right /Size 7 was said to make it 9. The stream is of the update's revision, the oldest that
        // names it, and is judged there, once (#156).
        var (file, _, _, stream, _) = HybridFiles.SharedStream(olderSize: 7, streamSize, between: true);

        var findings = Validate(file).Findings.Where(finding => finding.RuleId == PdfValidationRuleIds.FileSizeWrong);

        if (message is null)
        {
            findings.Should().BeEmpty();
        }
        else
        {
            var finding = findings.Should().ContainSingle().Which;
            finding.Location.Position.Should().Be(stream);
            finding.Message.Should().Be($"The cross-reference stream at offset {stream} {message}");
        }
    }

    [Theory]
    [InlineData("0 0\n", null)]
    [InlineData("0 1\n{free}\n", "gives /Size 4, where the highest object number it and the sections it updates use, 0, makes it 1.")]
    public void An_original_table_is_judged_once_it_or_an_older_section_numbers_an_object(string rows, string? message)
    {
        // The first table indexes nothing, or object 0 alone, and gives /Size 4; the update indexes the document. Where no
        // number is used, from the section down, there is no count to judge the /Size by; object 0 is one.
        var file = PdfTemplate.Build(PdfTemplate.Sound
            .Replace("0 4\n{free}\n{row:1}\n{row:2}\n{row:3}\n", rows, StringComparison.Ordinal) + """
            xref
            0 4
            {free}
            {row:1}
            {row:2}
            {row:3}
            trailer
            << /Size 4 /Root 1 0 R /Prev {xref:1} >>
            startxref
            {xref:2}
            %%EOF

            """);

        var findings = Validate(file).Findings.Where(finding => finding.RuleId == PdfValidationRuleIds.FileSizeWrong);

        if (message is null)
        {
            findings.Should().BeEmpty();
        }
        else
        {
            findings.Should().ContainSingle().Which.Message.Should().Be(
                $"The trailer of the cross-reference table at offset {PdfTemplate.OffsetOf(file, "\nxref\n") + 1} {message}");
        }
    }

    [Fact]
    public void A_stream_two_tables_name_may_give_the_size_of_the_oldest_table_that_names_it()
    {
        // The update's table indexes object 8, above what the stream numbers, and gives /Size 9, which the stream gives too:
        // Table 17's other reading, the /Size of the table that names it, read where the oldest names it (#156).
        var (file, _, _, _, _) = HybridFiles.SharedStream(olderSize: 9, streamSize: 9, olderEight: true);

        Validate(file).Findings.Should().BeEmpty();
    }

    [Fact]
    public void A_size_is_not_judged_where_a_table_s_xrefstm_names_no_stream()
    {
        // The table's /XRefStm names the table itself: its stream, which may number objects its /Size counts, is lost, as a
        // section the chain cannot read is, and the /Size is not judged (#156).
        var report = Validate(HybridFiles.NamingItself());

        report.Contains(PdfValidationRuleIds.XRefHybridStreamWrong).Should().BeTrue();
        report.Contains(PdfValidationRuleIds.FileSizeWrong).Should().BeFalse("its /Size 4 counts the page, which no section indexes");
    }

    [Fact]
    public void A_size_is_not_judged_where_the_chain_indexes_nothing()
    {
        // The only table's one row, for object 0, gives a generation of 65,536, which refuses it: the chain indexes nothing,
        // and the index is rebuilt as the document opens. The table's /Size 4 is not judged by the one number its rows use,
        // 0, which would make it 1: the chain gave no index, and the objects the rebuild found number up to 3. The row, its
        // offset written short of ten digits, is not in the 20-byte form either.
        var file = PdfTemplate.SoundWith("0 4\n{free}\n{row:1}\n{row:2}\n{row:3}\n", "0 1\n{off:1} 65536 n\n");
        using var document = PdfDocument.Open(file);

        var report = new PdfValidator().Validate(document);

        document.WasRepaired.Should().BeTrue();
        document.Reader.Structure.Sections.Should().ContainSingle().Which.HighestNumber.Should().Be(0);
        report.Findings.Select(finding => (finding.RuleId, finding.Location.Position)).Should().Equal(
            (PdfValidationRuleIds.XRefSectionMalformed, PdfTemplate.OffsetOf(file, "xref\n")),
            (PdfValidationRuleIds.XRefEntryWidthWrong, PdfTemplate.OffsetOf(file, "xref\n")));
    }

    [Fact]
    public void A_size_is_not_judged_where_the_chain_is_cut_at_the_file_s_first_byte()
    {
        // The only table read gives /Size 9, and its /Prev names offset 0, where the header starts, past the one section
        // MaxXRefSectionCount at 1 lets the reader read: the chain is cut at the file's first byte, and the older sections it
        // leaves unread may number the objects that /Size counts. The table's own rows, which make it 4, do not judge it.
        var file = PdfTemplate.SoundWith("<< /Size 4 /Root 1 0 R >>", "<< /Size 9 /Root 1 0 R /Prev 0 >>");
        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxXRefSectionCount = 1 } });

        var report = new PdfValidator().Validate(document);

        document.Reader.Structure.ChainCutAt.Should().Be(0);
        report.Findings.Select(finding => (finding.RuleId, finding.Location.Position)).Should().Equal(
            (PdfValidationRuleIds.XRefCheckedInPart, 0L));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public void Sections_whose_size_is_wrong_past_the_report_s_room_are_counted_and_not_written(int capacity)
    {
        // #168: one finding a section, as many as the chain has; those past the room are counted, the newest written first.
        const int Sections = 100;
        var (file, tables) = HostileInputTests.SectionsGivingAWrongSize(Sections);
        using var document = PdfDocument.Open(file);
        var profile = new ValidationProfile("size", 1, [new SizeWrongRule()]);

        var report = new PdfValidator(new PdfValidatorOptions { Profile = profile, FindingCapacity = capacity }).Validate(document);

        report.WarningCount.Should().Be(Sections);
        report.SuppressedCount.Should().Be(Sections - capacity);
        report.Findings.Select(finding => finding.Message).Should().Equal(
            tables.Reverse().Take(capacity).Select(table => string.Create(
                CultureInfo.InvariantCulture,
                $"The trailer of the cross-reference table at offset {table} gives /Size 99, where the highest object number it and the sections it updates use, 2, makes it 3.")));
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

    [Theory]
    [InlineData("/Size 0", 1, null, "gives /Size 0, where the highest object number it and the sections it updates use, 3, makes it 4.")]
    [InlineData("/Size 5", 3, "/Size 5", "gives /Size 5, where the highest object number it and the sections it updates use, 3, makes it 4.")]
    public void An_older_section_s_size_is_judged_by_the_objects_it_and_the_older_ones_use(
        string older, int added, string? newest, string message)
    {
        // A count of none is a count, and a wrong one. And the newest trailer's /Size, which leaves object 6 above it, is
        // the error rule's to report there alone: the older section that gives it as well, where its own objects make it 4,
        // is still judged.
        var updates = new (int, string)[added];

        for (var i = 0; i < added; i++)
        {
            updates[i] = (4 + i, "(an update)");
        }

        var original = PdfTemplate.SoundWith("/Size 4", older);
        var file = TestPdfBuilder.AppendIncrementalUpdate(original, rootNumber: 1, updates);
        file = newest is null ? file : Replace(file, "/Size 7", newest);

        var report = Validate(file);

        var finding = Single(report, PdfValidationRuleIds.FileSizeWrong);
        finding.Location.Position.Should().Be(PdfTemplate.OffsetOf(file, "trailer"));
        finding.Message.Should().Be($"The trailer of the cross-reference table at offset {PdfTemplate.OffsetOf(file, "xref\n")} {message}");
        report.Contains(PdfValidationRuleIds.XRefObjectPastSize).Should().Be(newest is not null);
    }

    [Fact]
    public void A_size_of_none_that_leaves_every_object_above_it_is_the_error_rule_s_alone()
    {
        var report = Validate(PdfTemplate.SoundWith("/Size 4", "/Size 0"));

        report.Contains(PdfValidationRuleIds.XRefObjectPastSize).Should().BeTrue();
        report.Contains(PdfValidationRuleIds.FileSizeWrong).Should().BeFalse();
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

    /// <summary>
    /// A sound file whose catalog, object 1, a limit of 128 bytes cuts before its <c>/Type</c> when <paramref name="cut"/>
    /// says so; beside it an older catalog, object 5, with a tree of its own, and object 8, a long string nothing names.
    /// </summary>
    private static byte[] OlderCatalogBeside(bool cut) => PdfTemplate.Build(OlderCatalogBesideTemplate(cut));

    private static string OlderCatalogBesideTemplate(bool cut) => $$"""
        %PDF-1.7
        1 0 obj
        <<{{(cut ? new string(' ', 200) : " ")}}/Type /Catalog /Pages 2 0 R >>
        endobj
        2 0 obj
        << /Type /Pages /Kids [3 0 R] /Count 1 >>
        endobj
        3 0 obj
        << /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << >> >>
        endobj
        5 0 obj
        << /Type /Catalog /Pages 6 0 R >>
        endobj
        6 0 obj
        << /Type /Pages /Kids [7 0 R] /Count 1 >>
        endobj
        7 0 obj
        << /Type /Page /Parent 6 0 R /MediaBox [0 0 1 1] /Resources << >> >>
        endobj
        8 0 obj
        ({{new string('x', 200)}})
        endobj
        xref
        0 9
        {free}
        {row:1}
        {row:2}
        {row:3}
        {free}
        {row:5}
        {row:6}
        {row:7}
        {row:8}
        trailer
        << /Size 9 /Root 1 0 R >>
        startxref
        {xref:1}
        %%EOF

        """;

    private static PdfReaderOptions CutAt(int maxObjectLength) =>
        new() { Limits = PdfReaderLimits.Default with { MaxObjectLength = maxObjectLength } };

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
