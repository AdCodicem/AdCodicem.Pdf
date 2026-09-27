using System.Globalization;
using System.Text;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.Objects;
using AdCodicem.Pdf.Validation;

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
    [InlineData("%PDF-2.4", "The header names version 2.4, which is not a version of PDF: 1.0 to 1.7, or 2.0.")]
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
    [InlineData("%PDF-1.3")]
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
    public void A_root_that_leads_to_no_catalog_says_what_it_leads_to(string root, string message)
    {
        var file = PdfTemplate.SoundWith("/Root 1 0 R", root);

        var finding = Single(Validate(file), PdfValidationRuleIds.FileRootInvalid);

        finding.Message.Should().Be(message + " The reader took object 1, which is one, for the catalog.");
    }

    [Fact]
    public void A_file_whose_root_names_no_catalog_and_holds_none_says_so()
    {
        var file = PdfTemplate.SoundWith("<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Outlines >>");

        Single(Validate(file), PdfValidationRuleIds.FileRootInvalid).Message
            .Should().Be("The trailer's /Root names object 1 0, which is a dictionary of /Type /Outlines, not a document catalog. No object of the file is a catalog.");
    }

    [Fact]
    public void A_catalog_without_type_that_holds_the_page_tree_is_not_this_rule_s()
    {
        // Readers take a dictionary with /Pages for the catalog; a missing /Type is the object-shape rules' (slice 3).
        Validate(PdfTemplate.SoundWith("<< /Type /Catalog /Pages 2 0 R >>", "<< /Pages 2 0 R >>")).Findings.Should().BeEmpty();
    }

    [Theory]
    [InlineData("/Size 5", "gives /Size 5, where the highest object number it and the sections it updates use, 3, makes it 4.")]
    [InlineData("/Size 3", "gives /Size 3, where the highest object number it and the sections it updates use, 3, makes it 4.")]
    [InlineData("/Size /Four", "gives a /Size that is not a count of objects.")]
    [InlineData("", "has no /Size.")]
    public void A_size_that_is_not_one_more_than_the_highest_number_is_a_warning(string size, string message)
    {
        var file = PdfTemplate.SoundWith("/Size 4", size);

        var finding = Single(Validate(file), PdfValidationRuleIds.FileSizeWrong);

        finding.Severity.Should().Be(PdfValidationSeverity.Warning);
        finding.Location.Position.Should().Be(PdfTemplate.OffsetOf(file, "trailer"));
        finding.Message.Should().Be($"The trailer of the cross-reference table at offset {PdfTemplate.OffsetOf(file, "xref\n")} {message}");
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
