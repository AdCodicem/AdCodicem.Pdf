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

    [Theory]
    [InlineData(Largest, LargestShifted)]
    [InlineData("999999", "1000004")]
    public void A_chain_that_names_the_same_offset_outside_the_file_twice_loops(string offset, string named)
    {
        // Past what a long holds, the sum wrapped negative, which the rule took for no loop at all; past the end, the rule
        // said the chain had read a section there.
        var file = Updated("/Prev {offset} /XRefStm {offset}", offset);
        var update = PdfTemplate.OffsetOf(file, "xref\n4 1");
        using var document = PdfDocument.Open(file);

        var structure = document.Reader.Structure;
        structure.LoopOffset.Should().Be(offset == Largest ? long.MaxValue : 1000004);
        structure.LoopWrittenOffset.Should().Be(long.Parse(offset, CultureInfo.InvariantCulture));
        var cycle = document.Diagnostics.Should().ContainSingle(d => d.Code == PdfDiagnosticCodes.XRefChainCycle).Which;
        cycle.Position.Should().Be(update);
        cycle.Message.Should().EndWith($"names offset {named}, outside the file, which the chain has already named.");

        var finding = Single(new PdfValidator().Validate(document), PdfValidationRuleIds.XRefChainLoop);
        finding.Location.Position.Should().Be(update);
        finding.Message.Should().Be(string.Create(
            CultureInfo.InvariantCulture,
            $"The /Prev of the cross-reference section at offset {update} names offset {named}, outside the file, which the chain has already named: the chain loops."));
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
        var limit = document.Diagnostics.Should().ContainSingle(d => d.Code == PdfDiagnosticCodes.LimitXRefSectionCount).Which;
        limit.Position.Should().Be(update);
        limit.Message.Should().StartWith(
            $"The cross-reference chain has more than 1 sections; the older ones were not read: the chain goes on at offset {LargestShifted}, outside the file.");

        var report = new PdfValidator().Validate(document);
        var finding = Single(report, PdfValidationRuleIds.XRefCheckedInPart);
        finding.Location.Position.Should().Be(update);
        finding.Message.Should().Be(
            $"The cross-reference chain goes on at offset {LargestShifted}, outside the file, past the sections PdfReaderLimits.MaxXRefSectionCount lets the reader read: the older sections were not checked.");
        report.Contains(PdfValidationRuleIds.FileSizeWrong).Should().BeFalse();
        report.Contains(PdfValidationRuleIds.PageTreePageOrphaned).Should().BeFalse();
        Codes(file, options).Should().BeEquivalentTo(Codes(Updated("/Prev {offset}", "999999", four, size), options));
    }

    [Theory]
    [InlineData(Largest, LargestShifted)]
    [InlineData("999999", "1000004")]
    public void A_limit_reached_where_the_chain_goes_on_outside_the_file_throws_with_the_section_naming_it(string offset, string named)
    {
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxXRefSectionCount = 1 }, ThrowOnLimit = true };
        var file = Updated("/Prev {offset}", offset);

        var thrown = FluentActions.Invoking(() => PdfDocument.Open(file, options)).Should().Throw<PdfLimitExceededException>().Which;

        thrown.Position.Should().Be(PdfTemplate.OffsetOf(file, "xref\n4 1"));
        thrown.Message.Should().Contain($"the chain goes on at offset {named}, outside the file.");
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

    [Theory]
    [InlineData(Largest, LargestShifted)]
    [InlineData("999999", "1000004")]
    public void An_entry_past_what_a_long_holds_once_the_header_is_added_places_its_object_outside_the_file(string row, string named)
    {
        var file = Shifted(PdfTemplate.Sound.Replace("{row:3}", row + " 00000 n", StringComparison.Ordinal));
        using var document = PdfDocument.Open(file);

        document.GetObject(new PdfObjectId(3)).AsDictionary().IsOfType(PdfName.Page).Should().BeTrue("the rebuild finds it");
        document.Diagnostics.Should().ContainSingle(d => d.Code == PdfDiagnosticCodes.XRefEntryOutOfRange).Which.Message.Should().Be(
            $"The entry of object 3 places it at offset {named}, outside the file.");
        Relocations(document).Should().BeEmpty();
        Single(new PdfValidator().Validate(document), PdfValidationRuleIds.XRefEntryBroken).Message.Should().Be(
            $"The entry of object 3 gives offset {named}, outside the file.");
    }

    [Fact]
    public void An_entry_past_what_a_long_holds_is_not_looked_for_at_the_file_s_start_whatever_precedes_the_header()
    {
        // Six hundred bytes before the header: the wrapped sum, less the search's radius, did not wrap back, and the
        // search read the file's first kilobyte, where it found the object.
        var file = Shifted(PdfTemplate.Sound.Replace("{row:3}", Largest + " 00000 n", StringComparison.Ordinal), junk: 600);
        using var document = PdfDocument.Open(file);

        document.GetObject(new PdfObjectId(3)).AsDictionary().IsOfType(PdfName.Page).Should().BeTrue("the rebuild finds it");
        Relocations(document).Should().BeEmpty();
        document.WasRepaired.Should().BeTrue();
        Codes(file).Should().BeEquivalentTo(
            Codes(Shifted(PdfTemplate.Sound.Replace("{row:3}", "999999 00000 n", StringComparison.Ordinal), junk: 600)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    public void A_negative_entry_places_its_object_outside_the_file_whatever_precedes_the_header(int junk)
    {
        // With nine bytes before the header, -3 counted from it fell inside those bytes, and the entry was judged shifted.
        var file = Shifted(PdfTemplate.Sound.Replace("{row:3}", "-3 00000 n", StringComparison.Ordinal), junk);
        using var document = PdfDocument.Open(file);

        var report = new PdfValidator().Validate(document);

        var finding = Single(report, PdfValidationRuleIds.XRefEntryBroken);
        finding.Severity.Should().Be(PdfValidationSeverity.Error);
        finding.Message.Should().Be("The entry of object 3 gives offset -3, outside the file.");
        report.Contains(PdfValidationRuleIds.XRefEntryShifted).Should().BeFalse();
    }

    [Fact]
    public void An_entry_far_before_the_file_s_start_is_not_looked_for_at_it()
    {
        // The search reaches 512 bytes either side of an entry: nothing of the file lies within reach of this one, whose
        // neighborhood was worked out from 0 until now.
        var file = PdfTemplate.SoundWith("{row:3}", "-9223372036854775000 00000 n");
        using var document = PdfDocument.Open(file);

        document.GetObject(new PdfObjectId(3)).AsDictionary().IsOfType(PdfName.Page).Should().BeTrue("the rebuild finds it");
        Relocations(document).Should().BeEmpty();
        document.Diagnostics.Should().ContainSingle(d => d.Code == PdfDiagnosticCodes.XRefEntryOutOfRange).Which.Message.Should().Be(
            "The entry of object 3 places it at offset -9223372036854775000, outside the file.");
    }

    [Fact]
    public void An_object_a_rebuild_finds_before_the_header_is_read_where_it_was_found()
    {
        // An offset the reader found is no offset the file gives: a negative one, counted from the header, names a byte
        // of the file before it.
        const string Before = "4 0 obj\n(before the header)\nendobj\n";
        var template = PdfTemplate.Sound
            .Replace("/Pages 2 0 R >>", "/Pages 2 0 R /Test 4 0 R >>", StringComparison.Ordinal)
            .Replace("startxref\n{xref:1}", "startxref\n999999", StringComparison.Ordinal);
        var file = Encoding.Latin1.GetBytes(Before).Concat(PdfTemplate.Build(template)).ToArray();
        using var document = PdfDocument.Open(file);

        document.GetObject(new PdfObjectId(4)).Should().BeOfType<PdfString>().Which.ToText().Should().Be("before the header");
        document.Diagnostics.Contains(PdfDiagnosticCodes.XRefEntryOutOfRange).Should().BeFalse();
        document.Reader.Index.TryGet(4, out var entry).Should().BeTrue();
        entry.FoundByReader.Should().BeTrue();
        entry.Offset.Should().Be(-Before.Length);
        document.Reader.PositionOf(entry).Should().Be(0);
    }

    [Fact]
    public void An_object_found_near_its_entry_before_the_header_is_placed_where_it_was_found()
    {
        // The entry gives the header's own offset, 0, where the object is not; it is found within 512 bytes, before the
        // header.
        const string Before = "4 0 obj\n(before the header)\nendobj\n";
        var template = PdfTemplate.Sound
            .Replace("/Pages 2 0 R >>", "/Pages 2 0 R /Test 4 0 R >>", StringComparison.Ordinal)
            .Replace("0 4\n", "0 5\n", StringComparison.Ordinal)
            .Replace("{row:3}\n", "{row:3}\n0000000000 00000 n \n", StringComparison.Ordinal)
            .Replace("/Size 4", "/Size 5", StringComparison.Ordinal);
        var file = Encoding.Latin1.GetBytes(Before).Concat(PdfTemplate.Build(template)).ToArray();
        using var document = PdfDocument.Open(file);

        document.GetObject(new PdfObjectId(4)).Should().BeOfType<PdfString>().Which.ToText().Should().Be("before the header");
        document.Reader.Index.TryGet(4, out var entry).Should().BeTrue();
        entry.FoundByReader.Should().BeTrue();
        document.Reader.PositionOf(entry).Should().Be(0);
        Relocations(document).Should().ContainSingle().Which.Position.Should().Be(0);
    }

    [Fact]
    public void A_section_found_near_where_prev_names_it_before_the_header_is_read_where_it_was_found()
    {
        // The older table, the only one to index object 4, lies in the bytes before the header; the newer one's /Prev names
        // the header's own offset, 0. The table is found within 512 bytes, before the header, as an object is.
        const string Header = "%PDF-1.7\n";
        const string Body = "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n" +
                            "4 0 obj\n(older)\nendobj\n";
        var four = Header.Length + Body.IndexOf("4 0 obj", StringComparison.Ordinal);
        var older = "xref\n0 5\n0000000000 65535 f \n" +
                    string.Create(CultureInfo.InvariantCulture, $"{Header.Length:D10} 00000 n \n") +
                    string.Create(CultureInfo.InvariantCulture, $"{Header.Length + Body.IndexOf("2 0 obj", StringComparison.Ordinal):D10} 00000 n \n") +
                    "0000000000 65535 f \n" +
                    string.Create(CultureInfo.InvariantCulture, $"{four:D10} 00000 n \n") +
                    "trailer\n<< /Size 5 /Root 1 0 R >>\n";
        // The newer table lies further than 512 bytes from what its /Prev names, where it is no candidate.
        var padding = "%" + new string('-', 600) + "\n";
        var newer = Header.Length + Body.Length + padding.Length;
        var file = older + Header + Body + padding +
                   "xref\n0 1\n0000000000 65535 f \ntrailer\n<< /Size 5 /Root 1 0 R /Prev 0 >>\n" +
                   string.Create(CultureInfo.InvariantCulture, $"startxref\n{newer}\n%%EOF\n");
        using var document = PdfDocument.Open(Encoding.Latin1.GetBytes(file));

        var section = document.Reader.Structure.Sections.Should().ContainSingle(s => s.NamedBy == "/Prev").Which;
        section.State.Should().Be(IO.XRef.XRefSectionState.Relocated);
        section.Offset.Should().Be(0);
        document.GetObject(new PdfObjectId(4)).Should().BeOfType<PdfString>().Which.ToText().Should().Be("older");
        document.Diagnostics.Contains(PdfDiagnosticCodes.XRefChainCycle).Should().BeFalse();
    }

    [Theory]
    [InlineData(40)]
    [InlineData(-1000)]
    public void A_negative_entry_does_not_bound_the_search_for_the_endstream_of_a_stream_before_the_header(int intoData)
    {
        // Object 4 lies before the header, its declared length past the parser's window; object 5's row, counted from the
        // header, fell inside 4's data and ended the search there, though the reader holds that row outside the file.
        var data = new string('d', 200);
        var before = $"4 0 obj\n<< /Length 9000 >>\nstream\n{data}\nendstream\nendobj\n";
        var dataStart = before.IndexOf("stream\n", StringComparison.Ordinal) + "stream\n".Length;
        var row = (dataStart + intoData - before.Length).ToString(CultureInfo.InvariantCulture);
        var template = PdfTemplate.Sound
            .Replace("%PDF-1.7\n", "%PDF-1.7\n%" + new string('x', 12000) + "\n", StringComparison.Ordinal)
            .Replace("/Pages 2 0 R >>", "/Pages 2 0 R /Test 4 0 R /Other 5 0 R >>", StringComparison.Ordinal)
            .Replace("0 4\n", "0 6\n", StringComparison.Ordinal)
            .Replace("{row:3}\n", "{row:3}\n0000000000 00000 n \n" + row + " 00000 n \n", StringComparison.Ordinal)
            .Replace("/Size 4", "/Size 6", StringComparison.Ordinal);
        var file = Encoding.Latin1.GetBytes(before).Concat(PdfTemplate.Build(template)).ToArray();
        using var document = PdfDocument.Open(file);

        document.GetObject(new PdfObjectId(4)).Should().BeOfType<PdfStream>().Which.RawLength.Should().Be(200);
        document.Diagnostics.Should().Contain(d =>
            d.Code == PdfDiagnosticCodes.StreamLengthInvalid && d.Message == "The stream declared 9000 bytes but ended after 200.");
    }

    [Theory]
    [InlineData(Largest)]
    [InlineData("999999")]
    public void A_report_on_an_object_its_entry_places_outside_the_file_is_placed_nowhere(string row)
    {
        // Each stream takes its /Length from the next: reading the first nests seventy loads, and the reader stops at the
        // sixty-fifth, object 74, whose entry lies outside the file. The report was placed at the wrapped sum, or past the
        // end.
        var text = new StringBuilder("%PDF-1.7\n");
        var offsets = new SortedDictionary<int, long>();
        offsets[1] = text.Length;
        text.Append("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        offsets[2] = text.Length;
        text.Append("2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n");

        for (var number = 10; number < 80; number++)
        {
            offsets[number] = text.Length;
            var length = number < 79 ? $"{number + 1} 0 R" : "5";
            text.Append(CultureInfo.InvariantCulture, $"{number} 0 obj\n<< /Length {length} >>\nstream\nhello\nendstream\nendobj\n");
        }

        var xref = text.Length;
        text.Append("xref\n0 1\n0000000000 65535 f \n1 2\n");
        text.Append(CultureInfo.InvariantCulture, $"{offsets[1]:D10} 00000 n \n{offsets[2]:D10} 00000 n \n10 70\n");

        for (var number = 10; number < 80; number++)
        {
            text.Append(number == 74 ? row : offsets[number].ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        }

        text.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size 80 /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        using var document = PdfDocument.Open(Shifted(text.ToString()));

        _ = document.GetObject(new PdfObjectId(10));

        document.Diagnostics.Should().ContainSingle(d => d.Code == PdfDiagnosticCodes.SyntaxDepthExceeded).Which.Position.Should().Be(-1);
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

    /// <summary>The objects the reader found near where their entries place them, which share their code with the header's offset.</summary>
    private static IEnumerable<PdfDiagnostic> Relocations(PdfDocument document) =>
        document.Diagnostics.Where(d => d.Code == PdfDiagnosticCodes.XRefOffsetAdjusted && d.Message.StartsWith("Object ", StringComparison.Ordinal));

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
