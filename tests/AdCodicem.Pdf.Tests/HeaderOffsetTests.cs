using System.Globalization;
using System.Text;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.Objects;
using AdCodicem.Pdf.Validation;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// Offsets a file gives, counted from a header that does not start the file, which the header's offset would carry past
/// what a long holds, or which are negative (#125): each is outside the file, as an offset past its end is, and a file
/// that gives one is read and judged as the same file giving 999999 is.
/// </summary>
public sealed class HeaderOffsetTests
{
    /// <summary>The largest offset a file can write, which five bytes before the header carry past what a long holds.</summary>
    private const string Largest = "9223372036854775807";

    /// <summary>Where <see cref="Largest"/> lies once five bytes before the header are added: the sum a long cannot hold.</summary>
    private const string LargestShifted = "9223372036854775812";

    /// <summary>
    /// The sound document and an update that indexes object 4 and whose trailer adds <c>{entries}</c>, in which
    /// <c>{offset}</c> stands for an offset the test gives and <c>{xref:1}</c> for the first table's.
    /// </summary>
    private const string Update = PdfTemplate.Sound + """
        4 0 obj
        {object}
        endobj
        xref
        4 1
        {row:4}
        trailer
        << /Size 5 /Root 1 0 R {entries} >>
        startxref
        {xref:2}
        %%EOF

        """;

    [Theory]
    [InlineData("/Prev {offset}", "/Prev")]
    [InlineData("/Prev {xref:1} /XRefStm {offset}", "/XRefStm")]
    public void A_section_named_past_what_a_long_holds_once_the_header_is_added_lies_outside_the_file(string entries, string naming)
    {
        // The sum wrapped negative, where -1 means that what named the section gave no offset: the reader and the rules
        // read it as one past the end of the file instead, and name the sum exactly.
        var file = Updated(entries, Largest);
        var update = PdfTemplate.OffsetOf(file, "xref\n4 1");
        using var document = PdfDocument.Open(file);

        var section = document.Reader.Structure.Sections.Should().ContainSingle(s => s.NamedBy == naming).Which;
        section.NamedOffset.Should().Be(long.MaxValue);
        section.WrittenOffset.Should().Be(long.MaxValue);
        section.State.Should().Be(IO.XRef.XRefSectionState.NotFound);
        section.Fault.Should().Be("lies outside the file");
        var missing = document.Diagnostics.Should().ContainSingle(d => d.Code == PdfDiagnosticCodes.XRefSectionMissing).Which;
        missing.Position.Should().Be(update);
        missing.Message.Should().StartWith(
            $"The cross-reference section {naming} names at offset {LargestShifted} lies outside the file, which is {file.Length:N0} bytes long;");

        var finding = Single(new PdfValidator().Validate(document), PdfValidationRuleIds.XRefSectionNotFound);
        finding.Location.Position.Should().Be(update);
        finding.Message.Should().Be(
            $"The cross-reference section {naming} names at offset {LargestShifted} is not there, nor within 512 bytes of it: the offset lies outside the file.");
        Codes(file).Should().BeEquivalentTo(Codes(Updated(entries, "999999")));
    }

    [Fact]
    public void A_startxref_past_what_a_long_holds_once_the_header_is_added_lies_outside_the_file()
    {
        var file = Shifted(PdfTemplate.Sound.Replace("startxref\n{xref:1}", "startxref\n" + Largest, StringComparison.Ordinal));
        using var document = PdfDocument.Open(file);

        var first = document.Reader.Structure.Sections[0];
        first.NamedOffset.Should().Be(long.MaxValue);
        first.State.Should().Be(IO.XRef.XRefSectionState.NotFound);
        document.WasRepaired.Should().BeTrue();
        Single(new PdfValidator().Validate(document), PdfValidationRuleIds.FileStartXRefWrong).Message.Should().Be(
            $"startxref gives offset {Largest}, which lies outside the file.");
        Codes(file).Should().BeEquivalentTo(
            Codes(Shifted(PdfTemplate.Sound.Replace("startxref\n{xref:1}", "startxref\n999999", StringComparison.Ordinal))));
    }

    [Fact]
    public void A_chain_that_names_the_same_offset_past_what_a_long_holds_twice_loops()
    {
        // The sum wrapped negative, which the rule took for no loop at all.
        const string Entries = "/Prev {offset} /XRefStm {offset}";
        var file = Updated(Entries, Largest);
        var update = PdfTemplate.OffsetOf(file, "xref\n4 1");
        using var document = PdfDocument.Open(file);

        var structure = document.Reader.Structure;
        structure.LoopOffset.Should().Be(long.MaxValue);
        structure.LoopWrittenOffset.Should().Be(long.MaxValue);
        var cycle = document.Diagnostics.Should().ContainSingle(d => d.Code == PdfDiagnosticCodes.XRefChainCycle).Which;
        cycle.Position.Should().Be(update);
        cycle.Message.Should().EndWith($"names offset {LargestShifted}, outside the file, which the chain has already named.");

        var finding = Single(new PdfValidator().Validate(document), PdfValidationRuleIds.XRefChainLoop);
        finding.Location.Position.Should().Be(update);
        finding.Message.Should().Contain($"names offset {LargestShifted}");
        Codes(file).Should().BeEquivalentTo(Codes(Updated(Entries, "999999")));
    }

    [Fact]
    public void Two_offsets_past_what_a_long_holds_name_two_sections_not_one_loop()
    {
        // Each is outside the file, and neither is the other: the chain compares what the file writes.
        var file = Updated("/XRefStm {offset} /Prev 9223372036854775806", Largest);
        using var document = PdfDocument.Open(file);

        document.Reader.Structure.LoopOffset.Should().Be(-1);
        new PdfValidator().Validate(document).Findings.Count(f => f.RuleId == PdfValidationRuleIds.XRefSectionNotFound).Should().Be(2);
        Codes(file).Should().BeEquivalentTo(Codes(Updated("/XRefStm {offset} /Prev 999998", "999999")));
    }

    [Theory]
    [InlineData("(an update)", "/Size 5")]
    [InlineData("(an update)", "/Size 9")]
    [InlineData("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 1 1] /Resources << >> >>", "/Size 5")]
    public void A_chain_cut_where_it_goes_on_past_what_a_long_holds_is_placed_at_the_section_naming_it(string four, string size)
    {
        // The sum wrapped negative, which the rules took for a chain read to its end: the cut went unsaid, and /Size and
        // the unlisted pages were judged on what it left. A cut outside the file is placed at what named it (#187).
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxXRefSectionCount = 1 } };
        var file = Updated("/Prev {offset}", Largest, four, size);
        var update = PdfTemplate.OffsetOf(file, "xref\n4 1");
        using var document = PdfDocument.Open(file, options);

        var structure = document.Reader.Structure;
        structure.ChainCutAt.Should().Be(long.MaxValue);
        structure.ChainCutWrittenOffset.Should().Be(long.MaxValue);
        structure.ChainCutNamedFrom.Should().Be(update);
        document.Diagnostics.Should().ContainSingle(d => d.Code == PdfDiagnosticCodes.LimitXRefSectionCount).Which.Position.Should().Be(update);

        var report = new PdfValidator().Validate(document);
        var finding = Single(report, PdfValidationRuleIds.XRefCheckedInPart);
        finding.Location.Position.Should().Be(update);
        finding.Message.Should().Be(
            $"The cross-reference chain goes on at offset {LargestShifted}, outside the file, past the sections PdfReaderLimits.MaxXRefSectionCount lets the reader read: the older sections were not checked.");
        report.Contains(PdfValidationRuleIds.FileSizeWrong).Should().BeFalse();
        report.Contains(PdfValidationRuleIds.PageTreePageOrphaned).Should().BeFalse();
        Codes(file, options).Should().BeEquivalentTo(Codes(Updated("/Prev {offset}", "999999", four, size), options));
    }

    [Fact]
    public void A_limit_reached_where_the_chain_goes_on_outside_the_file_throws_with_the_section_naming_it()
    {
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxXRefSectionCount = 1 }, ThrowOnLimit = true };
        var file = Updated("/Prev {offset}", Largest);

        var thrown = FluentActions.Invoking(() => PdfDocument.Open(file, options)).Should().Throw<PdfLimitExceededException>().Which;

        thrown.Position.Should().Be(PdfTemplate.OffsetOf(file, "xref\n4 1"));
    }

    [Fact]
    public void A_prev_written_as_a_reference_to_an_offset_past_what_a_long_holds_is_still_reported_as_a_reference()
    {
        // The section it names wrapped negative, which the rule took for a /Prev that gives no offset, reported elsewhere.
        var file = Shifted(Update
            .Replace("{object}\nendobj\n", "(an update)\nendobj\n9 0 obj\n" + Largest + "\nendobj\n", StringComparison.Ordinal)
            .Replace("4 1\n{row:4}", "4 1\n{row:4}\n9 1\n{row:9}", StringComparison.Ordinal)
            .Replace("{entries}", "/Prev 9 0 R", StringComparison.Ordinal)
            .Replace("/Size 5", "/Size 10", StringComparison.Ordinal));
        using var document = PdfDocument.Open(file);

        Single(new PdfValidator().Validate(document), PdfValidationRuleIds.FileTrailerValueWrong).Message.Should().Contain(
            "its /Prev as the reference 9 0 R, where ISO 32000-1 makes it direct (Table 15)");
    }

    /// <summary>
    /// The update, five bytes before its header, its trailer adding <paramref name="entries"/> with <paramref name="offset"/>
    /// for <c>{offset}</c> and giving <paramref name="size"/>, and object 4 written as <paramref name="four"/>.
    /// </summary>
    private static byte[] Updated(string entries, string offset, string four = "(an update)", string size = "/Size 5") =>
        Shifted(Update
            .Replace("{entries}", entries.Replace("{offset}", offset, StringComparison.Ordinal), StringComparison.Ordinal)
            .Replace("{object}", four, StringComparison.Ordinal)
            .Replace("/Size 5", size, StringComparison.Ordinal));

    /// <summary>Writes <paramref name="template"/>, its offsets counted from its header, after <paramref name="junk"/> bytes that are no part of it.</summary>
    private static byte[] Shifted(string template, int junk = 5)
    {
        var prefix = junk == 0 ? string.Empty : new string('-', junk - 1) + "\n";
        return Encoding.Latin1.GetBytes(prefix).Concat(PdfTemplate.Build(template)).ToArray();
    }

    /// <summary>What the reader reports of the file and what validating it finds, by code, in order.</summary>
    private static (string[] Diagnostics, string[] Findings) Codes(byte[] file, PdfReaderOptions? options = null)
    {
        using var document = PdfDocument.Open(file, options ?? new PdfReaderOptions());
        var report = new PdfValidator().Validate(document);
        return ([.. document.Diagnostics.Select(d => d.Code).Order(StringComparer.Ordinal)],
            [.. report.Findings.Select(f => f.RuleId).Order(StringComparer.Ordinal)]);
    }

    private static PdfValidationFinding Single(PdfValidationReport report, string ruleId)
    {
        report.Findings.Where(finding => finding.RuleId == ruleId).Should().ContainSingle(
            $"one finding of {ruleId}, among: {string.Join("; ", report.Findings)}");
        return report.Findings.First(finding => finding.RuleId == ruleId);
    }
}
