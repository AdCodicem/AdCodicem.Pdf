using System.Globalization;
using System.Text;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.IO.XRef;
using AdCodicem.Pdf.Objects;
using AdCodicem.Pdf.Validation;
using AdCodicem.Pdf.Validation.Rules;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// The <c>xref</c> family of M02's second slice: the sections of the chain, and each entry of the file's own index
/// — each rule on a file that breaks it, a sound file, and a file that is unusual and legal.
/// </summary>
public class CrossReferenceRuleTests
{
    /// <summary>The sound document, saved once more: an update adding object 4, its /Prev naming the first table.</summary>
    private const string Updated = PdfTemplate.Sound + """
        4 0 obj
        (an update)
        endobj
        xref
        4 1
        {row:4}
        trailer
        << /Size 5 /Root 1 0 R /Prev {xref:1} >>
        startxref
        {xref:2}
        %%EOF

        """;

    [Fact]
    public void A_sound_update_and_a_sound_spread_file_have_no_finding()
    {
        Validate(PdfTemplate.Build(Updated)).Findings.Should().BeEmpty();
        Validate(PdfTemplate.Build(PdfTemplate.Spread)).Findings.Should().BeEmpty();
    }

    [Fact]
    public void A_row_that_is_not_an_offset_a_generation_and_n_or_f_makes_the_table_malformed()
    {
        var file = PdfTemplate.SoundWith("{row:2}", "0000000abc 00000 n ");

        var report = Validate(file);

        var finding = Single(report, PdfValidationRuleIds.XRefSectionMalformed);
        finding.Severity.Should().Be(PdfValidationSeverity.Error, "the reader rebuilds the index");
        var table = PdfTemplate.OffsetOf(file, "xref\n");
        finding.Location.Position.Should().Be(table);
        finding.Message.Should().Be(
            $"The cross-reference table at offset {table} cannot be read: the row for object 2, at offset {PdfTemplate.OffsetOf(file, "0000000abc")}, is not an offset, a generation and n or f.");
        report.Contains(PdfValidationRuleIds.XRefEntryBroken).Should().BeFalse("no entry of an index the chain never gave is probed");
    }

    [Theory]
    [InlineData("xref\n0\n")]
    [InlineData("xref\n0 -4\n")]
    [InlineData("xref\n0 50000001\n")]
    [InlineData("xref\n3000000000 4\n")]
    public void A_subsection_header_without_a_first_number_and_a_count_it_can_hold_makes_the_table_malformed(string header)
    {
        var file = PdfTemplate.SoundWith("xref\n0 4\n", header);

        Single(Validate(file), PdfValidationRuleIds.XRefSectionMalformed).Message
            .Should().Contain("does not give a first object number and a count of rows");
    }

    [Theory]
    [InlineData("2147483647 2", true)]
    [InlineData("2147483646 2", false)]
    public void A_subsection_numbering_rows_past_the_largest_object_number_makes_the_table_malformed(string header, bool malformed)
    {
        // A second subsection of rows placing the page again: its last number, first + count - 1, once wrapped past
        // int.MaxValue to a negative object number.
        var file = PdfTemplate.SoundWith("{row:3}\n", "{row:3}\n" + header + "\n{row:3}\n{row:3}\n");

        var findings = Validate(file).Findings.Where(finding => finding.RuleId == PdfValidationRuleIds.XRefSectionMalformed).ToList();

        if (malformed)
        {
            findings.Should().ContainSingle().Which.Message.Should().EndWith(
                $"cannot be read: the subsection header at offset {PdfTemplate.OffsetOf(file, header)} numbers its rows past object 2147483647.");
        }
        else
        {
            findings.Should().BeEmpty();
        }
    }

    [Theory]
    [InlineData("65536")]
    [InlineData("4294967296")]
    [InlineData("-1")]
    public void A_row_giving_an_object_in_use_a_generation_no_object_can_have_is_refused_alone(string generation)
    {
        // 4294967296 once narrowed to 0, and served the page as generation 0 in silence. Only the row is refused: the
        // rows after it are read, and the page is found again by rebuilding the index. The row keeps its twenty bytes.
        var sound = PdfTemplate.Build(PdfTemplate.Sound);
        var page = PdfTemplate.OffsetOf(sound, "\n3 0 obj") + 1;
        var row = string.Create(CultureInfo.InvariantCulture, $"{page:D10} 00000 n ");
        var written = string.Create(CultureInfo.InvariantCulture, $"{page} {generation} n").PadRight(row.Length);
        var file = Replace(sound, row, written);
        using var document = PdfDocument.Open(file);

        Single(new PdfValidator().Validate(document), PdfValidationRuleIds.XRefSectionMalformed).Message.Should().EndWith(
            $"is malformed: the row for object 3, at offset {PdfTemplate.OffsetOf(file, written)}, gives it generation {generation}, outside 0 to 65535.");
        document.GetObject(new PdfObjectId(3)).AsDictionary().IsOfType(PdfName.Page).Should().BeTrue("the rebuild finds the page");
        document.WasRepaired.Should().BeTrue();
    }

    [Fact]
    public void A_refused_row_of_a_table_leaves_the_rows_after_it_read()
    {
        // The page's row is refused; the long string's, object 4, follows it and is read, so asking for object 4 needs no
        // rebuild.
        var sound = PdfTemplate.Build(PdfTemplate.Spread);
        var page = PdfTemplate.OffsetOf(sound, "\n3 0 obj") + 1;
        var row = string.Create(CultureInfo.InvariantCulture, $"{page:D10} 00000 n ");
        using var document = PdfDocument.Open(Replace(sound, row, string.Create(CultureInfo.InvariantCulture, $"{page:D10} 65536 n ")));

        document.GetObject(new PdfObjectId(4)).Should().BeOfType<PdfString>();
        document.WasRepaired.Should().BeFalse();
    }

    [Theory]
    [InlineData("{row:3:65535}", "3 65535 obj")]
    [InlineData("0000000000 65536 f ", "3 0 obj")]
    public void A_row_at_the_edge_of_what_an_entry_can_hold_is_read(string row, string header)
    {
        // Generation 65,535 is the last an object in use may have; a free row may give more.
        var template = PdfTemplate.Sound.Replace("3 0 obj", header, StringComparison.Ordinal);
        var file = PdfTemplate.Build(row.EndsWith('f') ? template.Replace("{free}", row, StringComparison.Ordinal) : template.Replace("{row:3}", row, StringComparison.Ordinal));

        Validate(file).Findings.Where(finding => finding.RuleId.StartsWith("xref.", StringComparison.Ordinal)).Should().BeEmpty();
    }

    [Theory]
    [InlineData("3 65536 obj", "3 65536 obj")]
    [InlineData("3 92233720368547758080 obj", "3 92233720368547758080 obj")]
    [InlineData("3 0000000000 obj", "4294967299 0 obj")]
    public void An_entry_at_a_header_the_parser_refuses_is_broken(string written, string header)
    {
        // Where the entry places it, the page's header gives a number or a generation the parser refuses, so no read can
        // serve the object there. Once, the probe took 65536 for a mismatched generation; a search near the entry found
        // the header past a long "0 bytes after it", and called the entry shifted; and the probe named the header of
        // object 4294967299, which narrowed to an int is 3. The header written first keeps the offsets right.
        var file = Replace(PdfTemplate.SoundWith("3 0 obj", written), written, header);

        Single(Validate(file), PdfValidationRuleIds.XRefEntryBroken).Message.Should().Be(
            $"The entry of object 3 gives offset {PdfTemplate.OffsetOf(file, "\n" + header) + 1}, where there is no object header, and the object is not within 512 bytes of it.");
    }

    [Fact]
    public void A_table_unreadable_past_a_refused_row_is_said_unreadable_for_what_stopped_it()
    {
        // The page's row is refused for its generation, and the rows after it read; the trailer keyword, misspelled, is what
        // makes the table unreadable, and the finding names it, not the row.
        var sound = PdfTemplate.Build(PdfTemplate.Sound);
        var page = PdfTemplate.OffsetOf(sound, "\n3 0 obj") + 1;
        var row = string.Create(CultureInfo.InvariantCulture, $"{page:D10} 00000 n ");
        var file = Replace(Replace(sound, row, string.Create(CultureInfo.InvariantCulture, $"{page:D10} 65536 n ")), "trailer", "trailor");

        Single(Validate(file), PdfValidationRuleIds.XRefSectionMalformed).Message.Should().EndWith(
            $"cannot be read: it holds the keyword trailor at offset {PdfTemplate.OffsetOf(file, "trailor")}, where a subsection or the trailer should start.");
    }

    [Fact]
    public void A_free_row_whose_generation_is_past_65535_is_read_in_silence()
    {
        // Producers give the head of the free list 65536; nothing is ever served under a free row's generation.
        Validate(PdfTemplate.SoundWith("{free}", "0000000000 65536 f ")).Findings.Should().BeEmpty();
    }

    [Theory]
    [InlineData("/Size 6 /W [1 -4 2]", "its /W gives a field a negative width.")]
    [InlineData("/Size 6 /W [1 -4294967300 2]", "its /W gives a field a negative width.")]
    [InlineData("/Size 6 /W [1 4.5 2]", "its /W gives a field a width that is not an integer.")]
    [InlineData("/Size 6 /W [1 null 2]", "its /W gives a field a width that is not an integer.")]
    [InlineData("/Size 6 /W [1 4]", "its /W does not give the widths of three fields.")]
    [InlineData("/Size 6 /W [0 0 0]", "its /W gives rows of no bytes.")]
    [InlineData("/Size 6 /W [1 4 2] /Index [0 -1]", "its /Index gives a subsection a count of rows out of range.")]
    [InlineData("/Size 6 /W [1 4 2] /Index [0 4294967302]", "its /Index gives a subsection a count of rows out of range.")]
    [InlineData("/Size 6 /W [1 4 2] /Index 6", "its /Index is the integer 6, not an array.")]
    [InlineData("/Size 6 /W [1 4 2] /Index /All", "its /Index is the name /All, not an array.")]
    [InlineData("/Size 6 /W [1 4 2] /Index []", "its /Index holds 0 values, not pairs of a first object number and a count of rows.")]
    [InlineData("/Size 6 /W [1 4 2] /Index [0]", "its /Index holds 1 value, not pairs of a first object number and a count of rows.")]
    [InlineData("/Size 6 /W [1 4 2] /Index [0 6 7]", "its /Index holds 3 values, not pairs of a first object number and a count of rows.")]
    [InlineData("/Size 6 /W [1 4 2] /Index [0 (six)]", "its /Index holds a value of type string where an integer belongs.")]
    [InlineData("/Size 6 /W [1 4 2] /Index [/Zero 6]", "its /Index holds the name /Zero where an integer belongs.")]
    [InlineData("/Size 6 /W [1 4 2] /Index [0 null]", "its /Index holds null where an integer belongs.")]
    [InlineData("/Size 6 /W [1 4 2] /Index [92233720368547758080 6]", "its /Index holds the real number 92233720368547760000 where an integer belongs.")]
    [InlineData("/Size 6 /W [1 4 2] /Index [0 1 0 R]", "its /Index holds the reference 1 0 R, which no section read before it places where it can be read.")]
    [InlineData("/Size 6 /W [1 4 2] /Index 1 0 R", "its /Index is the reference 1 0 R, which no section read before it places where it can be read.")]
    [InlineData("/Size 6 /W 1 0 R", "its /W is the reference 1 0 R, which no section read before it places where it can be read.")]
    [InlineData("/Size 6 /W [1 1 0 R 2]", "its /W holds the reference 1 0 R, which no section read before it places where it can be read.")]
    [InlineData("/Size 1 0 R /W [1 4 2]", "its /Size is the reference 1 0 R, which no section read before it places where it can be read.")]
    [InlineData("/Size 6 /W [1 4 2] /Filter 1 0 R", "its /Filter is the reference 1 0 R, which no section read before it places where it can be read.")]
    [InlineData("/Size 6 /W [1 4 2] /Filter /FlateDecode /DecodeParms 1 0 R", "its /DecodeParms is the reference 1 0 R, which no section read before it places where it can be read.")]
    [InlineData("/Size 6 /W [1 4 2] /Filter /FlateDecode /DecodeParms << /Predictor 12 /Columns 1 0 R >>", "its /DecodeParms holds the reference 1 0 R, which no section read before it places where it can be read.")]
    [InlineData("/Size 6 /W [1 4 2] /Filter [/FlateDecode] /DecodeParms [<< /Predictor 12 /Columns 1 0 R >>]", "its /DecodeParms holds the reference 1 0 R, which no section read before it places where it can be read.")]
    [InlineData("/Size 6 /W [1 4 2] /Filter /FlateDecode /DecodeParms << /Predictor 1 0 R >>", "its /DecodeParms holds the reference 1 0 R, which no section read before it places where it can be read.")]
    [InlineData("/Size 6 /W [1 4 2] /Filter /LZWDecode /DecodeParms << /EarlyChange 1 0 R >>", "its /DecodeParms holds the reference 1 0 R, which no section read before it places where it can be read.")]
    [InlineData("/Size 6 /W [1 4 2] /Filter [1 0 R]", "its /Filter holds the reference 1 0 R, which no section read before it places where it can be read.")]
    [InlineData("/Size 6 /W [1 4 2] /Filter [/FlateDecode] /DecodeParms [1 0 R]", "its /DecodeParms holds the reference 1 0 R, which no section read before it places where it can be read.")]
    [InlineData("/Size 1 0 R /W [1 4 2] /Index null", "its /Size is the reference 1 0 R, which no section read before it places where it can be read.")]
    [InlineData("/Size 6 /W [1 4 2] /Index [-5 1]", "its /Index gives a subsection of 1 row from object -5, outside object numbers 0 to 2147483647.")]
    [InlineData("/Size 6 /W [1 4 2] /Index [2147482000 5000]", "its /Index gives a subsection of 5,000 rows from object 2147482000, outside object numbers 0 to 2147483647.")]
    [InlineData("/Size 6 /W [1 4 2] /Index [-5 6]", "its /Index gives a subsection of 6 rows from object -5, outside object numbers 0 to 2147483647.")]
    [InlineData("/Size 6 /W [1 4 2] /Index [4294967296 6]", "its /Index gives a subsection of 6 rows from object 4294967296, outside object numbers 0 to 2147483647.")]
    [InlineData("/Size 6 /W [1 4 2] /Index [2147483647 6]", "its /Index gives a subsection of 6 rows from object 2147483647, outside object numbers 0 to 2147483647.")]
    [InlineData("/Size 6 /W [1 4 2] /Index [0 6 2147483648 0]", "its /Index gives a subsection of 0 rows from object 2147483648, outside object numbers 0 to 2147483647.")]
    [InlineData("/W [1 4 2]", "it has no /Index, and its /Size gives no count of objects.")]
    [InlineData("/Size -6 /W [1 4 2]", "it has no /Index, and its /Size gives no count of objects.")]
    [InlineData("/Size 6.5 /W [1 4 2]", "it has no /Index, and its /Size gives no count of objects.")]
    [InlineData("/Size 92233720368547758086 /W [1 4 2]", "it has no /Index, and its /Size gives no count of objects.")]
    public void A_cross_reference_stream_whose_widths_or_ranges_cannot_be_read_is_malformed(string layout, string fault)
    {
        // Each was once read on: a width or a start narrowed to an int, an element that is not a number read as 0, an odd
        // element dropped, a negative or absent /Size reading no row. The section is malformed, as a classic table whose
        // subsection header cannot be believed, and the index startxref leads to is rebuilt.
        var file = Replace(XRefStreamFile(), "/Size 6 /W [1 4 2]", layout);

        Single(Validate(file), PdfValidationRuleIds.XRefSectionMalformed).Message
            .Should().StartWith("The cross-reference stream at offset ").And.EndWith("cannot be read: " + fault);
    }

    [Theory]
    [InlineData("/Size 6 /W [1.0 4.0 2.0] /Index [0.0 6.0]", false)]
    [InlineData("/Size 6 /W [1 4 2] /Index [0 6 2147483647 0]", false)]
    [InlineData("/Size 6 /W [1 4 2] /Index [0 6 2147483642 0]", false)]
    [InlineData("/Size 6 /W [1 4 2] /Index [2147483642 6]", true)]
    public void A_cross_reference_stream_whose_numbering_holds_is_not_malformed(string layout, bool repaired)
    {
        // Integral reals are read as the integers they equal, as everywhere the reader reads a value, and reported as the
        // file's fault elsewhere (file.trailer-value-wrong); a subsection may end at the largest object number. Rows
        // numbered from 2147483642 index no catalog, which a rebuild then finds.
        using var document = PdfDocument.Open(Replace(XRefStreamFile(), "/Size 6 /W [1 4 2]", layout));

        new PdfValidator().Validate(document).Contains(PdfValidationRuleIds.XRefSectionMalformed).Should().BeFalse();
        document.WasRepaired.Should().Be(repaired);
    }

    [Theory]
    [InlineData("2147483648")]
    [InlineData("9223372036854775807")]
    [InlineData("2635249153387078803")]
    public void A_cross_reference_stream_whose_size_declares_more_rows_than_it_holds_is_malformed(string size)
    {
        // Narrowed to an int, 2147483648 was negative and read no row, which left the index to a rebuild with no word of
        // why. The rows the data holds are read, and the shortfall is the section's fault, found by division: the last
        // size times the row's 7 bytes wraps past 2^64 to 5, fewer than the data holds.
        var file = Replace(XRefStreamFile(), "/Size 6 /W [1 4 2]", "/Size " + size + " /W [1 4 2]");

        Single(Validate(file), PdfValidationRuleIds.XRefSectionMalformed).Message.Should().EndWith(
            string.Create(
                CultureInfo.InvariantCulture, $"is malformed: it holds 6 rows where its /Index and /Size declare {long.Parse(size, CultureInfo.InvariantCulture):N0}."));
    }

    [Theory]
    [InlineData("/Extra", "a name")]
    [InlineData("1.5", "a real number")]
    [InlineData("(rows)", "a string")]
    [InlineData("<41>", "a string")]
    [InlineData("[", "an array delimiter")]
    [InlineData(">>", "the end of a dictionary")]
    [InlineData(")", "a byte that starts no token")]
    [InlineData("key", "the keyword key")]
    [InlineData("k\u001Bey#", "the keyword k#1Bey#23")]
    public void A_table_holding_something_else_where_a_subsection_should_start_says_what(string text, string what)
    {
        var file = PdfTemplate.SoundWith("{row:3}\ntrailer", "{row:3}\n" + text + "\ntrailer");

        Single(Validate(file), PdfValidationRuleIds.XRefSectionMalformed).Message.Should().EndWith(
            $": it holds {what} at offset {PdfTemplate.OffsetOf(file, text + "\ntrailer")}, where a subsection or the trailer should start.");
    }

    [Fact]
    public void A_section_that_cannot_be_read_for_no_stated_reason_is_reported_without_one()
    {
        // The reader says what makes every section it cannot read unreadable; the rule, given a record no file produces
        // that says nothing, ends its sentence where the reason would start.
        var file = PdfTemplate.Build(Updated);
        using var document = PdfDocument.Open(file);
        var table = PdfTemplate.OffsetOf(file, "xref\n");
        document.Reader.Structure.Add(new XRefSectionRecord("/Prev", table, namedFrom: -1) { State = XRefSectionState.Malformed });
        var context = new ValidationContext(document, capacity: 16);

        new SectionMalformedRule().Check(context);

        var finding = context.ToReport(ValidationProfile.Structural).Findings.Should().ContainSingle().Which;
        finding.Location.Position.Should().Be(table);
        finding.Message.Should().Be($"The cross-reference table at offset {table} cannot be read.");
    }

    [Fact]
    public void A_table_a_limit_cut_is_not_malformed_and_is_said_to_be_checked_in_part()
    {
        // Three hundred rows, 6 KB of table, which a bound of 2 KB cuts before the trailer (ADR 34).
        var builder = new TestPdfBuilder().WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>").WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        for (var number = 3; number < 300; number++)
        {
            builder.WithObject(number, "null");
        }

        var file = builder.BuildClassic(rootNumber: 1);
        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxXRefSectionLength = 2048 } });

        var report = new PdfValidator().Validate(document);

        var finding = report.Findings.Should().ContainSingle().Which;
        finding.RuleId.Should().Be(PdfValidationRuleIds.XRefCheckedInPart);
        finding.Severity.Should().Be(PdfValidationSeverity.Information);
        finding.Message.Should().Be(
            $"One of the reader's limits stopped it reading the cross-reference section at offset {PdfTemplate.OffsetOf(file, "xref\n")} whole: what lies past the limit was not checked.");
    }

    [Fact]
    public void A_chain_a_limit_cut_says_the_older_sections_were_not_checked()
    {
        var file = PdfTemplate.Build(Updated);
        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxXRefSectionCount = 1 } });

        var finding = Single(new PdfValidator().Validate(document), PdfValidationRuleIds.XRefCheckedInPart);

        finding.Location.Position.Should().Be(PdfTemplate.OffsetOf(file, "xref\n"));
        finding.Message.Should().Contain("past the sections PdfReaderLimits.MaxXRefSectionCount lets the reader read");
    }

    [Theory]
    [InlineData("99999", "The cross-reference section /Prev names at offset 99999 is not there, nor within 512 bytes of it: the offset lies outside the file.")]
    [InlineData("12 0 R", "The /Prev of the cross-reference section at offset {update} is the reference 12 0 R, which no section read before it places where it can be read.")]
    [InlineData("-1", "The /Prev of the cross-reference section at offset {update} is the integer -1, not an offset.")]
    [InlineData("/Offset", "The /Prev of the cross-reference section at offset {update} is the name /Offset, not an offset.")]
    [InlineData("/Na#0Ame#1B", "The /Prev of the cross-reference section at offset {update} is the name /Na#0Ame#1B, not an offset.")]
    [InlineData("1.5", "The /Prev of the cross-reference section at offset {update} is the real number 1.5, not an offset.")]
    [InlineData("9223372036854775808", "The /Prev of the cross-reference section at offset {update} is the real number 9223372036854776000, not an offset.")]
    [InlineData("0.0000001", "The /Prev of the cross-reference section at offset {update} is the real number 0.0000001, not an offset.")]
    [InlineData("-0.000000000000000000000012", "The /Prev of the cross-reference section at offset {update} is the real number -0.000000000000000000000012, not an offset.")]
    public void A_section_prev_names_where_none_is_is_not_found(string prev, string message)
    {
        var file = PdfTemplate.Build(Updated.Replace("/Prev {xref:1}", "/Prev " + prev, StringComparison.Ordinal));

        var finding = Single(Validate(file), PdfValidationRuleIds.XRefSectionNotFound);

        finding.Severity.Should().Be(PdfValidationSeverity.Error);
        finding.Message.Should().Be(message.Replace("{update}", (PdfTemplate.OffsetOf(file, "\nxref\n", 2) + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal));
    }

    [Fact]
    public void A_section_prev_names_inside_the_file_where_none_is_near_is_not_found()
    {
        // /Prev names the catalog's header, more than half a kilobyte from the first table.
        var file = PdfTemplate.Build(PdfTemplate.Spread + """
            5 0 obj
            (an update)
            endobj
            xref
            5 1
            {row:5}
            trailer
            << /Size 6 /Root 1 0 R /Prev {off:1} >>
            startxref
            {xref:2}
            %%EOF

            """);
        var catalog = PdfTemplate.OffsetOf(file, "1 0 obj");

        var finding = Single(Validate(file), PdfValidationRuleIds.XRefSectionNotFound);

        finding.Location.Position.Should().Be(catalog);
        finding.Message.Should().Be(
            $"The cross-reference section /Prev names at offset {catalog} is not there, nor within 512 bytes of it: the offset holds object 1, which is not a cross-reference stream.");
    }

    [Fact]
    public void A_section_not_found_outside_the_file_and_named_from_nowhere_known_is_reported_at_the_document()
    {
        // The reader knows where every /Prev and /XRefStm is written, and says why each section it does not find is not
        // there; the rule, given a record no file produces that knows neither, still locates and words its finding.
        var file = PdfTemplate.Build(Updated);
        using var document = PdfDocument.Open(file);
        var outside = file.Length + 100L;
        document.Reader.Structure.Add(
            new XRefSectionRecord("/Prev", outside, namedFrom: -1) { WrittenOffset = outside, State = XRefSectionState.NotFound });
        var context = new ValidationContext(document, capacity: 16);

        new SectionNotFoundRule().Check(context);

        var finding = context.ToReport(ValidationProfile.Structural).Findings.Should().ContainSingle().Which;
        finding.Location.IsDocument.Should().BeTrue();
        finding.Message.Should().Be(
            $"The cross-reference section /Prev names at offset {outside} is not there, nor within 512 bytes of it: the offset holds no section.");
    }

    [Theory]
    [InlineData(3, "3 bytes before it")]
    [InlineData(-7, "7 bytes after it")]
    public void A_section_prev_names_a_few_bytes_off_is_shifted(int error, string distance)
    {
        var sound = PdfTemplate.Build(Updated);
        var table = PdfTemplate.OffsetOf(sound, "xref\n");
        var file = Replace(sound, $"/Prev {table}", $"/Prev {table + error}");

        var report = Validate(file);

        var finding = report.Findings.Should().ContainSingle().Which;
        finding.RuleId.Should().Be(PdfValidationRuleIds.XRefSectionShifted);
        finding.Severity.Should().Be(PdfValidationSeverity.Warning);
        finding.Location.Position.Should().Be(table);
        finding.Message.Should().Be($"The cross-reference section /Prev names at offset {table + error} starts {distance}, at offset {table}.");
    }

    [Fact]
    public void A_prev_naming_the_line_feed_before_its_section_is_imprecise_not_shifted()
    {
        var sound = PdfTemplate.Build(Updated);
        var table = PdfTemplate.OffsetOf(sound, "xref\n");
        var file = Replace(sound, $"/Prev {table}", $"/Prev {table - 1}");

        var finding = Validate(file).Findings.Should().ContainSingle().Which;

        finding.RuleId.Should().Be(PdfValidationRuleIds.XRefOffsetImprecise);
        finding.Message.Should().EndWith($"/Prev gives offset {table - 1}, 1 byte before the xref keyword.");
    }

    [Fact]
    public void A_prev_that_names_a_section_already_read_is_a_loop_and_an_error()
    {
        var file = PdfTemplate.Build(Updated.Replace("/Prev {xref:1}", "/Prev {xref:2}", StringComparison.Ordinal));
        using var document = PdfDocument.Open(file);

        var report = new PdfValidator().Validate(document);

        var finding = Single(report, PdfValidationRuleIds.XRefChainLoop);
        finding.Severity.Should().Be(PdfValidationSeverity.Error, "the section the chain should have gone on to is lost");
        var update = PdfTemplate.OffsetOf(file, "\nxref\n", 2) + 1;
        finding.Message.Should().Be($"The /Prev of the cross-reference section at offset {update} names offset {update}, a section the chain has already read: the chain loops.");
        document.Catalog.Should().NotBeNull("what only the lost section indexed is found by rebuilding the index, as for a missing section");
        report.Contains(PdfValidationRuleIds.FileRootInvalid).Should().BeFalse("the catalog /Root names is found, where the file put it");
    }

    [Fact]
    public void A_loop_named_from_no_known_section_is_reported_at_the_document()
    {
        // Only a section the chain has read can name one it read before, so the reader always knows where the loop is
        // written; the rule, given a structure no file produces that does not, locates the finding at the document.
        using var document = PdfDocument.Open(PdfTemplate.Build(Updated));
        var structure = document.Reader.Structure;
        structure.LoopOffset = 9;
        structure.LoopWrittenOffset = 9;
        structure.LoopNamedBy = "/Prev";
        var context = new ValidationContext(document, capacity: 16);

        new ChainLoopRule().Check(context);

        var finding = context.ToReport(ValidationProfile.Structural).Findings.Should().ContainSingle().Which;
        finding.Location.IsDocument.Should().BeTrue();
        finding.Message.Should().Be("A /Prev names offset 9, a section the chain has already read: the chain loops.");
    }

    [Fact]
    public void An_entry_outside_the_file_is_broken()
    {
        var file = PdfTemplate.SoundWith("{row:3}", "9999999999 00000 n ");

        var finding = Single(Validate(file), PdfValidationRuleIds.XRefEntryBroken);

        finding.Severity.Should().Be(PdfValidationSeverity.Error);
        finding.Location.Object.Should().Be(new PdfObjectId(3));
        finding.Message.Should().Be("The entry of object 3 gives offset 9999999999, outside the file.");
    }

    [Fact]
    public void An_entry_naming_another_object_far_from_its_own_is_broken()
    {
        var file = PdfTemplate.Build(PdfTemplate.Spread.Replace("{row:3}", "{row:1}", StringComparison.Ordinal));

        var finding = Single(Validate(file), PdfValidationRuleIds.XRefEntryBroken);

        finding.Location.Object.Should().Be(new PdfObjectId(3));
        finding.Location.Position.Should().Be(PdfTemplate.OffsetOf(file, "1 0 obj"));
        finding.Message.Should().Be(
            $"The entry of object 3 gives offset {PdfTemplate.OffsetOf(file, "1 0 obj")}, where there is the header of object 1, and the object is not within 512 bytes of it.");
    }

    [Theory]
    [InlineData(-5, "no object header", "5 bytes after it")]
    [InlineData(2, "no object header", "2 bytes before it")]
    public void An_entry_a_few_bytes_off_its_object_is_shifted(int error, string found, string distance)
    {
        var file = PdfTemplate.SoundWith("{row:3}", $"{{row:3:0:{error}}}");

        var report = Validate(file);

        var finding = report.Findings.Should().ContainSingle().Which;
        finding.RuleId.Should().Be(PdfValidationRuleIds.XRefEntryShifted);
        finding.Severity.Should().Be(PdfValidationSeverity.Warning);
        var header = PdfTemplate.OffsetOf(file, "3 0 obj");
        finding.Location.Position.Should().Be(header + error);
        finding.Message.Should().Be($"The entry of object 3 gives offset {header + error}, where there is {found}; the object starts {distance}, at offset {header}.");
    }

    [Fact]
    public void An_entry_whose_generation_its_object_is_not_written_with_is_a_warning()
    {
        var file = PdfTemplate.SoundWith("{row:3}", "{row:3:7}");
        using var document = PdfDocument.Open(file);

        var report = new PdfValidator().Validate(document);

        var finding = report.Findings.Should().ContainSingle().Which;
        finding.RuleId.Should().Be(PdfValidationRuleIds.XRefGenerationMismatch);
        finding.Severity.Should().Be(PdfValidationSeverity.Warning, "the reference and the header agree against the entry (ADR 45)");
        finding.Location.Object.Should().Be(new PdfObjectId(3, 7));
        finding.Message.Should().Be("The entry of object 3 gives generation 7, and the object is written as 3 0 obj.");
        document.GetObject(new PdfObjectId(3)).AsDictionary().Should().NotBeNull("the reader reads the object the references name");
    }

    [Fact]
    public void Offsets_naming_the_line_feed_before_their_object_are_reported_once_for_the_file()
    {
        var file = PdfTemplate.Build(PdfTemplate.Sound
            .Replace("{row:1}", "{row:1:0:-1}", StringComparison.Ordinal)
            .Replace("{row:2}", "{row:2:0:-1}", StringComparison.Ordinal)
            .Replace("{row:3}", "{row:3:0:-1}", StringComparison.Ordinal));

        var report = Validate(file);

        var finding = report.Findings.Should().ContainSingle().Which;
        finding.RuleId.Should().Be(PdfValidationRuleIds.XRefOffsetImprecise);
        finding.Severity.Should().Be(PdfValidationSeverity.Warning);
        var first = PdfTemplate.OffsetOf(file, "1 0 obj") - 1;
        finding.Location.Position.Should().Be(first);
        finding.Message.Should().Be(
            $"3 offsets name the white space before what they designate rather than its first byte; the first: the entry of object 1 gives offset {first}, 1 byte before its header.");
    }

    [Fact]
    public void A_startxref_naming_the_line_feed_before_xref_is_imprecise_and_comes_first()
    {
        var sound = PdfTemplate.Build(PdfTemplate.Sound);
        var table = PdfTemplate.OffsetOf(sound, "xref\n");
        var file = Replace(sound, $"startxref\n{table}\n", $"startxref\n{table - 1}\n");

        var finding = Validate(file).Findings.Should().ContainSingle().Which;

        finding.RuleId.Should().Be(PdfValidationRuleIds.XRefOffsetImprecise);
        finding.Message.Should().Be(
            $"An offset names the white space before what it designates rather than its first byte: startxref gives offset {table - 1}, 1 byte before the xref keyword.");
    }

    [Fact]
    public void Objects_in_a_sound_object_stream_have_no_finding()
    {
        Validate(XRefStreamFile()).Findings.Should().BeEmpty();
    }

    [Fact]
    public void An_object_listed_at_another_index_of_its_stream_is_shifted()
    {
        var file = SwapObjectStreamNumbers(XRefStreamFile());

        var report = Validate(file);

        // Read as the header lists them, object 2 is the page and object 3 the page tree: the page tree rules say so.
        report.Findings.Where(finding => finding.RuleId.StartsWith("xref.", StringComparison.Ordinal))
            .Select(finding => (finding.RuleId, finding.Location.Object)).Should().Equal(
                (PdfValidationRuleIds.XRefEntryShifted, new PdfObjectId(2)),
                (PdfValidationRuleIds.XRefEntryShifted, new PdfObjectId(3)));
        report.Findings[0].Message.Should().Be("The entry of object 2 places it at index 0 of object stream 4, whose header lists it at index 1.");
    }

    [Fact]
    public void An_object_its_stream_does_not_list_is_broken()
    {
        var file = Replace(XRefStreamFile(), "stream\n2 0 3 ", "stream\n2 0 7 ");

        var finding = Single(Validate(file), PdfValidationRuleIds.XRefEntryBroken);

        finding.Location.Object.Should().Be(new PdfObjectId(3));
        finding.Message.Should().Be("The entry of object 3 places it at index 1 of object stream 4, whose header does not list it.");
    }

    [Theory]
    [InlineData("/Type /ObjStm", "/Type/XObject", "Object 4, where the index places 2 objects, is a stream that is not of /Type /ObjStm.")]
    [InlineData("/N 2", "/N 9", "Object stream 4, where the index places 2 objects, cannot be read: its /N declares 9 objects, more than its /First of")]
    [InlineData("/N 2", "/X 2", "Object stream 4, where the index places 2 objects, cannot be read: its /N or its /First is missing or negative.")]
    [InlineData("/Type /ObjStm /N 2 /First ", "/Type/ObjStm/N 2/First 999", "Object stream 4, where the index places 2 objects, cannot be read: it decodes to")]
    public void An_object_stream_that_cannot_serve_its_objects_is_reported_once(string text, string replacement, string message)
    {
        replacement.Length.Should().Be(text.Length, "every offset after the object stream stays right");
        var file = Replace(XRefStreamFile(), text, replacement);

        var finding = Single(Validate(file), PdfValidationRuleIds.XRefObjectStreamBroken);

        finding.Severity.Should().Be(PdfValidationSeverity.Error);
        finding.Location.Object.Should().Be(new PdfObjectId(4));
        finding.Message.Should().StartWith(message);
    }

    [Theory]
    [InlineData("x 0 ", "then something else")]
    [InlineData("2 x ", "then something else")]
    [InlineData("92233720368547758082 0 ", "then something else")]
    [InlineData("0 0 ", "then object 0 at offset 0, which no member can be")]
    [InlineData("2147483648 0 ", "then object 2147483648 at offset 0, which no member can be")]
    [InlineData("4294967298 0 ", "then object 4294967298 at offset 0, which no member can be")]
    [InlineData("-2 0 ", "then object -2 at offset 0, which no member can be")]
    [InlineData("2 -5 ", "then object 2 at offset -5, which no member can be")]
    [InlineData("2 2147483648 ", "then object 2 at offset 2147483648, which no member can be")]
    [InlineData("2147483647 0 ", null)]
    [InlineData("2 2147483647 ", null)]
    public void An_object_stream_whose_header_lists_something_other_than_objects_cannot_be_read(string start, string? then)
    {
        // The header starts "2 0 ", object 2 at offset 0; the objects keep their offsets from /First. A number no object
        // can have, or an offset no member can start at, ends the header as a stray token does, as the reader ends it; a
        // number past a long is a real, which is a stray token.
        var file = XRefStreamBuilder().BuildWithXRefStream(
            rootNumber: 1, compressedObjects: [2, 3], objectStreamHeader: header => start + header[4..]);

        var report = Validate(file);

        if (then is null)
        {
            // The largest number and offset a member may have: the header is read, whatever else the entries then get wrong.
            report.Contains(PdfValidationRuleIds.XRefObjectStreamBroken).Should().BeFalse();
            return;
        }

        Single(report, PdfValidationRuleIds.XRefObjectStreamBroken).Message.Should().Be(
            "Object stream 4, where the index places 2 objects, cannot be read: its header lists 0 of the 2 objects its /N declares, " + then + ".");
    }

    [Fact]
    public void An_object_stream_the_index_names_as_an_object_that_is_no_stream_is_reported()
    {
        // The rows of objects 2 and 3 name object 1, the catalog, as their stream.
        var file = Replace(XRefStreamFile(), "\u0002\0\0\0\u0004", "\u0002\0\0\0\u0001");

        var finding = Single(Validate(file), PdfValidationRuleIds.XRefObjectStreamBroken);

        finding.Location.Object.Should().Be(new PdfObjectId(1));
        finding.Message.Should().Be("Object 1, where the index places 2 objects, is not a stream.");
    }

    [Fact]
    public void An_object_stream_whose_entry_is_broken_is_that_entry_s_finding_alone()
    {
        // A long string, object 4, keeps object stream 5 far from the catalog, whose offset its entry now gives.
        var file = XRefStreamBuilder()
            .WithObject(4, "(" + new string('x', 1200) + ")")
            .BuildWithXRefStream(rootNumber: 1, compressedObjects: [2, 3]);
        var catalog = PdfTemplate.OffsetOf(file, "\n1 0 obj") + 1;
        var stream = PdfTemplate.OffsetOf(file, "\n5 0 obj") + 1;
        file = Replace(file, Row(stream), Row(catalog));

        var finding = Validate(file).Findings.Should().ContainSingle().Which;

        finding.RuleId.Should().Be(PdfValidationRuleIds.XRefEntryBroken);
        finding.Location.Object.Should().Be(new PdfObjectId(5));
    }

    [Fact]
    public void An_object_stream_whose_entry_lies_outside_the_file_is_that_entry_s_finding_alone()
    {
        // The index written again with offsets of eight bytes, object stream 4's past the end of the file: its header is
        // looked for where no byte of the file is.
        var file = WithRows([1, 8, 2], number: 4, type: 1, second: 0x1_0000_0000UL, third: 0);

        var finding = Validate(file).Findings.Should().ContainSingle().Which;

        finding.RuleId.Should().Be(PdfValidationRuleIds.XRefEntryBroken);
        finding.Location.Object.Should().Be(new PdfObjectId(4));
        finding.Message.Should().Be("The entry of object 4 gives offset 4294967296, outside the file.");
    }

    [Theory]
    [InlineData(new[] { 1, 8, 2 }, 4, 1UL, 9223372036854775808UL, 0UL, "the row for object 4 gives it offset 9223372036854775808, past any a file can have")]
    [InlineData(new[] { 1, 8, 2 }, 4, 1UL, ulong.MaxValue, 0UL, "the row for object 4 gives it offset 18446744073709551615, past any a file can have")]
    [InlineData(new[] { 1, 9, 2 }, 4, 1UL, ulong.MaxValue, 0UL, "the row for object 4 gives it offset 18446744073709551615, past any a file can have")]
    [InlineData(new[] { 1, 4, 4 }, 4, 1UL, null, 65536UL, "the row for object 4 gives it generation 65536, outside 0 to 65535")]
    [InlineData(new[] { 1, 4, 8 }, 4, 1UL, null, 4294967296UL, "the row for object 4 gives it generation 4294967296, outside 0 to 65535")]
    [InlineData(new[] { 1, 4, 2 }, 3, 2UL, 0UL, 1UL, "the row for object 3 places it in object stream 0, which is no object number")]
    [InlineData(new[] { 1, 4, 2 }, 3, 2UL, 2147483648UL, 1UL, "the row for object 3 places it in object stream 2147483648, which is no object number")]
    [InlineData(new[] { 1, 8, 2 }, 3, 2UL, 4294967300UL, 1UL, "the row for object 3 places it in object stream 4294967300, which is no object number")]
    [InlineData(new[] { 1, 4, 8 }, 3, 2UL, 4UL, 2147483648UL, "the row for object 3 places it at index 2147483648 of object stream 4, past any an object stream can hold")]
    [InlineData(new[] { 1, 4, 9 }, 3, 2UL, 4UL, ulong.MaxValue, "the row for object 3 places it at index 18446744073709551615 of object stream 4, past any an object stream can hold")]
    public void A_cross_reference_stream_row_whose_fields_no_entry_can_hold_is_refused_alone(
        int[] widths, int number, ulong type, ulong? second, ulong third, string fault)
    {
        // Each once narrowed in silence: an offset of 2^63 or more to a negative one, a generation or a stream number to
        // an int that could name another, 4294967300 to object stream 4. The row is refused and the next ones read.
        var file = WithRows(widths, number, type, second, third);
        using var document = PdfDocument.Open(file);

        Single(new PdfValidator().Validate(document), PdfValidationRuleIds.XRefSectionMalformed).Message.Should().EndWith("is malformed: " + fault + ".");
        document.GetObject(new PdfObjectId(number)).Should().NotBeSameAs(PdfNull.Instance, "the rebuild finds the object");
        document.WasRepaired.Should().BeTrue();
    }

    [Fact]
    public void A_refused_row_of_a_cross_reference_stream_leaves_the_rows_after_it_read()
    {
        // Object 3's row is refused; object stream 4's follows it and is read, so object 2, which it holds, is served with
        // no rebuild.
        using var document = PdfDocument.Open(WithRows([1, 4, 2], number: 3, type: 2, second: 0, third: 1));

        document.GetObject(new PdfObjectId(2)).AsDictionary().IsOfType(PdfName.Pages).Should().BeTrue();
        document.WasRepaired.Should().BeFalse();
    }

    [Theory]
    [InlineData(new[] { 1, 4, 4 }, 4, 1UL, null, 65535UL)]
    [InlineData(new[] { 1, 8, 2 }, 4, 1UL, 9223372036854775807UL, 0UL)]
    [InlineData(new[] { 1, 4, 2 }, 3, 2UL, 2147483647UL, 1UL)]
    [InlineData(new[] { 1, 4, 8 }, 3, 2UL, 4UL, 2147483647UL)]
    public void A_cross_reference_stream_row_at_the_edge_of_what_an_entry_can_hold_is_read(
        int[] widths, int number, ulong type, ulong? second, ulong third)
    {
        // The largest generation, offset, object stream number and index an entry holds: each row is read, whatever else
        // the entry then gets wrong.
        Validate(WithRows(widths, number, type, second, third)).Contains(PdfValidationRuleIds.XRefSectionMalformed).Should().BeFalse();
    }

    [Theory]
    [InlineData(0x1_0000_0002UL)]
    [InlineData(3UL)]
    public void A_cross_reference_stream_row_of_a_type_past_2_is_ignored_however_wide_its_type_field(ulong type)
    {
        // Object 3's row keeps its fields, object stream 4 and index 1. Narrowed to an int, 2^32 + 2 was type 2 and
        // served the page; past 2, ISO 32000-1, Table 18, has the row ignored, as a reference to a null object.
        using var document = PdfDocument.Open(WithRows([8, 4, 2], number: 3, type, second: 4, third: 1));

        document.GetObject(new PdfObjectId(3)).Should().BeSameAs(PdfNull.Instance);
        document.GetObject(new PdfObjectId(2)).AsDictionary().IsOfType(PdfName.Pages).Should().BeTrue();
    }

    [Theory]
    [InlineData(new[] { 1, 9, 2 })]
    [InlineData(new[] { 1, 16, 2 })]
    [InlineData(new[] { 9, 4, 2 })]
    [InlineData(new[] { 1, 4, 9 })]
    public void A_cross_reference_stream_whose_fields_are_wider_than_8_bytes_is_read_when_their_leading_bytes_are_zero(int[] widths)
    {
        // ISO 32000-1 bounds no width (Table 17), and every value an entry holds fits in 8 bytes: a ninth byte, and every
        // one after it, is a leading zero. Each was once a section malformed, and the index rebuilt (#216).
        var file = WithRows(widths, number: 1, type: 1, second: null, third: 0);
        using var document = PdfDocument.Open(file);

        new PdfValidator().Validate(document).Findings.Should().BeEmpty();
        document.WasRepaired.Should().BeFalse();
        document.GetObject(new PdfObjectId(3)).AsDictionary().IsOfType(PdfName.Page).Should().BeTrue();
    }

    [Theory]
    [InlineData(new[] { 1, 9, 2 }, 4, 1UL, null, 0UL, 1, "the row for object 4 gives it an offset of more than 64 bits, past any a file can have")]
    [InlineData(new[] { 1, 4, 9 }, 4, 1UL, null, 0UL, 2, "the row for object 4 gives it a generation of more than 64 bits, outside 0 to 65535")]
    [InlineData(new[] { 1, 9, 2 }, 3, 2UL, 4UL, 1UL, 1, "the row for object 3 places it in an object stream whose number takes more than 64 bits, which is no object number")]
    [InlineData(new[] { 1, 4, 9 }, 3, 2UL, 4UL, 1UL, 2, "the row for object 3 places it at an index of more than 64 bits in object stream 4, past any an object stream can hold")]
    public void A_cross_reference_stream_row_whose_wide_field_holds_more_than_64_bits_is_refused_alone(
        int[] widths, int number, ulong type, ulong? second, ulong third, int leading, string fault)
    {
        // A leading byte that is not zero makes a value past any an entry holds: the row is refused, as one past it in 8
        // bytes is, and the rows after it read.
        using var document = PdfDocument.Open(WithRows(widths, number, type, second, third, leading));

        Single(new PdfValidator().Validate(document), PdfValidationRuleIds.XRefSectionMalformed).Message.Should().EndWith("is malformed: " + fault + ".");
        document.GetObject(new PdfObjectId(number)).Should().NotBeSameAs(PdfNull.Instance, "the rebuild finds the object");
    }

    [Fact]
    public void A_cross_reference_stream_row_whose_wide_type_field_holds_more_than_64_bits_is_ignored()
    {
        // A type past 2, however it is written: the row is skipped, as one written in 8 bytes is (#221), not refused.
        using var document = PdfDocument.Open(WithRows([9, 4, 2], number: 3, type: 2, second: 4, third: 1, leading: 0));

        document.GetObject(new PdfObjectId(3)).Should().BeSameAs(PdfNull.Instance);
        document.GetObject(new PdfObjectId(2)).AsDictionary().IsOfType(PdfName.Pages).Should().BeTrue();
        new PdfValidator().Validate(document).Contains(PdfValidationRuleIds.XRefSectionMalformed).Should().BeFalse();
    }

    [Theory]
    [InlineData("/Size 6 /W [1 9 2]", 3)]
    [InlineData("/Size 6 /W [1 20 20]", 1)]
    [InlineData("/Size 6 /W [1 2147483647 2]", 0)]
    [InlineData("/Size 6 /W [1 4294967300 2]", 0)]
    [InlineData("/Size 6 /W [1 9223372036854775807 2]", 0)]
    [InlineData("/Size 6 /W [9223372036854775807 9223372036854775807 9223372036854775807]", 0)]
    public void A_cross_reference_stream_whose_rows_are_wider_than_its_data_holds_fewer_rows_than_it_declares(string layout, int held)
    {
        // The rows were written 7 bytes wide, 42 bytes in all. Rows of 12 bytes read 3 of them, misaligned, and rows of 41
        // one; a row longer than the data is one the data does not hold, whatever its width, which is narrowed only once
        // it is known to be within the data, and the widths' sum is then within a long.
        var file = Replace(XRefStreamFile(), "/Size 6 /W [1 4 2]", layout);

        Single(Validate(file), PdfValidationRuleIds.XRefSectionMalformed).Message.Should().EndWith(
            string.Create(CultureInfo.InvariantCulture, $"is malformed: it holds {held} {(held == 1 ? "row" : "rows")} where its /Index and /Size declare 6."));
    }

    [Fact]
    public void An_object_stream_a_few_bytes_from_its_entry_serves_its_objects()
    {
        var file = XRefStreamFile();
        var stream = PdfTemplate.OffsetOf(file, "\n4 0 obj") + 1;
        file = Replace(file, Row(stream), Row(stream - 3));

        var finding = Validate(file).Findings.Should().ContainSingle().Which;

        finding.RuleId.Should().Be(PdfValidationRuleIds.XRefEntryShifted);
        finding.Location.Object.Should().Be(new PdfObjectId(4));
    }

    [Fact]
    public void An_object_stream_whose_data_is_corrupt_is_broken_rather_than_unchecked()
    {
        // Past the zlib header, bytes no deflate block starts with: the file's fault, not one of the reader's limits.
        var file = XRefStreamBuilder().BuildWithXRefStream(rootNumber: 1, compressedObjects: [2, 3], compressObjectStream: true);
        var text = Encoding.Latin1.GetString(file);
        var data = text.IndexOf("\nstream\n", text.IndexOf("/ObjStm", StringComparison.Ordinal), StringComparison.Ordinal) + "\nstream\n".Length;
        file.AsSpan(data + 2, 8).Fill(0xFF);

        var findings = Validate(file).Findings;

        findings.Select(finding => finding.RuleId).Should().Equal(PdfValidationRuleIds.XRefObjectStreamBroken);
        findings[0].Message.Should().StartWith("Object stream 4, where the index places 2 objects, cannot be read: ");
    }

    [Fact]
    public void An_object_stream_a_limit_cut_before_its_header_ended_is_said_to_be_unchecked()
    {
        // Its data decodes to 107 bytes, the header 9 of them; the reader stops at 4.
        var file = XRefStreamBuilder().BuildWithXRefStream(rootNumber: 1, compressedObjects: [2, 3], compressObjectStream: true);
        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxDecodedStreamLength = 4 } });

        var finding = new PdfValidator().Validate(document).Findings.Should().ContainSingle().Which;

        finding.RuleId.Should().Be(PdfValidationRuleIds.XRefCheckedInPart);
        finding.Location.Object.Should().Be(new PdfObjectId(4));
        finding.Message.Should().Be(
            "One of the reader's limits stopped it reading object stream 4 before its header ended: the 2 objects the index places in it were not checked. Raising the limit the reader reported lets them be.");
    }

    [Fact]
    public void An_object_stream_whose_dictionary_a_limit_cut_is_said_to_be_unchecked()
    {
        // "4 0 obj << /Type /ObjStm /N 2 /First 9 /Length 107 >>" runs past 32 bytes: the reader's limit, not the file's fault.
        var file = XRefStreamFile();
        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = 32 } });

        var findings = new PdfValidator().Validate(document).Findings;

        findings.Select(finding => finding.RuleId).Should().Equal(PdfValidationRuleIds.XRefCheckedInPart);
        findings[0].Location.Object.Should().Be(new PdfObjectId(4));
        findings[0].Message.Should().StartWith("One of the reader's limits stopped it reading object stream 4 before its header ended");
    }

    [Fact]
    public void A_cross_reference_stream_a_limit_cut_is_said_to_be_checked_in_part()
    {
        // Six rows of seven bytes, of which the reader decodes twenty.
        var file = XRefStreamBuilder().BuildWithXRefStream(rootNumber: 1, compressXRefStream: true);
        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxDecodedStreamLength = 20 } });

        var finding = new PdfValidator().Validate(document).Findings.Should().ContainSingle().Which;

        finding.RuleId.Should().Be(PdfValidationRuleIds.XRefCheckedInPart);
        finding.Message.Should().Be(
            $"One of the reader's limits stopped it reading the cross-reference section at offset {PdfTemplate.OffsetOf(file, "\n5 0 obj") + 1} whole: what lies past the limit was not checked.");
    }

    [Fact]
    public void A_startxref_naming_the_line_feed_before_a_cross_reference_stream_is_imprecise()
    {
        var file = XRefStreamFile();
        var stream = PdfTemplate.OffsetOf(file, "\n5 0 obj") + 1;
        file = Replace(file, $"startxref\n{stream}\n", $"startxref\n{stream - 1}\n");

        Single(Validate(file), PdfValidationRuleIds.XRefOffsetImprecise).Message.Should().Be(
            $"An offset names the white space before what it designates rather than its first byte: startxref gives offset {stream - 1}, 1 byte before the header of the cross-reference stream.");
    }

    [Fact]
    public void A_cross_reference_stream_whose_dictionary_is_not_well_formed_is_reported_at_the_stream()
    {
        var file = Replace(XRefStreamFile(), "/W [1 4 2]", "/W [1 4 2] 7");
        var stream = PdfTemplate.OffsetOf(file, "\n5 0 obj") + 1;

        var finding = Single(Validate(file), PdfValidationRuleIds.FileTrailerMalformed);

        finding.Location.Position.Should().Be(stream);
        finding.Message.Should().Be(
            $"The dictionary of the cross-reference stream at offset {stream} is not well formed: the reader read it despite syntax errors.");
    }

    [Fact]
    public void Objects_placed_in_an_object_stream_the_index_does_not_hold_are_reported_with_it()
    {
        // The rows of objects 2 and 3 name object stream 7 rather than 4.
        var file = Replace(XRefStreamFile(), "\u0002\0\0\0\u0004", "\u0002\0\0\0\u0007");

        var finding = Single(Validate(file), PdfValidationRuleIds.XRefObjectStreamBroken);

        finding.Location.Object.Should().Be(new PdfObjectId(7));
        finding.Message.Should().Be("The index places 2 objects in object stream 7, which it does not hold as an object.");
    }

    [Fact]
    public void An_object_placed_in_an_object_stream_itself_packed_is_reported_with_that_stream()
    {
        // The row of object 2 names object 3, which the index places in object stream 4.
        var content = Encoding.Latin1.GetString(XRefStreamFile());
        var row = content.IndexOf("\u0002\0\0\0\u0004", StringComparison.Ordinal);
        var file = Encoding.Latin1.GetBytes(content[..(row + 4)] + "\u0003" + content[(row + 5)..]);

        var finding = Single(Validate(file), PdfValidationRuleIds.XRefObjectStreamBroken);

        finding.Location.Object.Should().Be(new PdfObjectId(3));
        finding.Message.Should().Be(
            "The index places 1 object in object stream 3, which it places in object stream 4 in turn: an object stream cannot be stored in another.");
    }

    [Theory]
    [MemberData(nameof(ChainFiles.Keys), MemberType = typeof(ChainFiles))]
    public void A_cross_reference_stream_value_the_chain_cannot_read_leaves_the_index_as_the_file_wrote_it(string key)
    {
        // #182: the newer table places object 5 outside the file. Loading it while the chain was read rebuilt the index,
        // which the rules then judged as the file's own, finding nothing. Nothing is loaded while the chain is read: the
        // entry is judged as written, and the stream, whose rows cannot be read without a value it cannot read, is malformed;
        // its /Type it can be read without, and its /Length is read once the chain is.
        var report = Validate(ChainFiles.UnderAnUpdate(key, placed: false));

        Single(report, PdfValidationRuleIds.XRefEntryBroken).Message.Should().Be("The entry of object 5 gives offset 99999, outside the file.");

        if (key is "Type" or "Length")
        {
            report.Contains(PdfValidationRuleIds.XRefSectionMalformed).Should().BeFalse();
        }
        else
        {
            Single(report, PdfValidationRuleIds.XRefSectionMalformed).Message.Should().EndWith(
                "the reference 5 0 R, which no section read before it places where it can be read.");
        }
    }

    [Fact]
    public void A_trailer_followed_by_stream_whose_length_the_chain_cannot_read_leaves_the_index_as_the_file_wrote_it()
    {
        // #182: the parser takes the trailer for a stream's dictionary, whose /Length, object 5, the table places outside
        // the file. Loading it as the chain was read rebuilt the index, and the entry went unjudged. The chain reads nothing
        // it cannot read where the index places it: the data is taken up to its endstream, and the entry judged as written.
        var file = PdfTemplate.Build(PdfTemplate.Sound
            .Replace("{row:3}\n", "{row:3}\n5 1\n0000099999 00000 n \n", StringComparison.Ordinal)
            .Replace("<< /Size 4 /Root 1 0 R >>", "<< /Size 6 /Root 1 0 R /Length 5 0 R >>\nstream\nabcd\nendstream", StringComparison.Ordinal));
        using var document = PdfDocument.Open(file);
        var report = new PdfValidator().Validate(document);

        document.WasRepaired.Should().BeFalse();
        Single(report, PdfValidationRuleIds.XRefEntryBroken).Message.Should().Be("The entry of object 5 gives offset 99999, outside the file.");
        report.Contains(PdfValidationRuleIds.FileTrailerMalformed).Should().BeTrue();
    }

    [Theory]
    [InlineData("deep", "its /DecodeParms holds the reference 6 0 R, which no section read before it places where it can be read.")]
    [InlineData("chained", "its /Index holds the reference 6 0 R, which no section read before it places where it can be read.")]
    [InlineData("looping", "its /Index leads from reference to reference without reaching a value.")]
    [InlineData("a stream", "its /Index is the reference 5 0 R, which no section read before it places where it can be read.")]
    public void A_value_the_rows_need_that_the_chain_cannot_follow_to_its_end_makes_the_section_malformed(string shape, string fault)
    {
        // Object 6, which only the stream indexes, cannot be read while the chain is. Each value was once followed three
        // references deep, or not into what a reference leads to, and read as null or decoded with defaults past them:
        // the /Columns of a /DecodeParms element written as a reference, an /Index whose object holds a reference, two
        // objects naming each other. Object 5 the newer table places, but a stream is no value the chain reads, nor one
        // whose /Length needs itself.
        var file = shape switch
        {
            "deep" => ChainFiles.UnderAnUpdate(
                "/Type /XRef /Size 8 /W [1 4 2] /Filter [/FlateDecode] /DecodeParms [5 0 R] /Length {length}",
                "<< /Predictor 12 /Columns 6 0 R >>",
                compressed: true,
                predicted: true,
                placed: true,
                six: "7"),
            "chained" => ChainFiles.UnderAnUpdate(
                "/Type /XRef /Size 8 /W [1 4 2] /Index 5 0 R /Length {length}", "6 0 R", compressed: false, predicted: false, placed: true, six: "[0 8]"),
            "looping" => ChainFiles.UnderAnUpdate(
                "/Type /XRef /Size 8 /W [1 4 2] /Index 5 0 R /Length {length}",
                "10 0 R",
                compressed: false,
                predicted: false,
                placed: true,
                placedToo: new Dictionary<int, string> { [10] = "5 0 R" }),
            _ => ChainFiles.UnderAnUpdate(
                "/Type /XRef /Size 8 /W [1 4 2] /Index 5 0 R /Length {length}",
                "<< /Length 5 0 R >>\nstream\n[0 8]\nendstream",
                compressed: false,
                predicted: false,
                placed: true),
        };

        Single(Validate(file), PdfValidationRuleIds.XRefSectionMalformed).Message.Should().EndWith("cannot be read: " + fault);
    }

    [Theory]
    [InlineData(30, false)]
    [InlineData(31, true)]
    public void A_value_behind_as_many_references_as_a_reference_resolves_through_is_read_and_one_more_is_not(int links, bool malformed)
    {
        // /Index is object 5, which names object 10, which names the next, the last holding the array: 31 objects read in
        // all with 30 links, 32 with 31. PdfReference.Resolve reads null past 31 (#173); the chain reads no further.
        var placed = new Dictionary<int, string>();

        for (var number = 10; number < 10 + links; number++)
        {
            placed[number] = number + 1 < 10 + links ? string.Create(CultureInfo.InvariantCulture, $"{number + 1} 0 R") : "[0 8]";
        }

        var file = ChainFiles.UnderAnUpdate(
            "/Type /XRef /Size 8 /W [1 4 2] /Index 5 0 R /Length {length}", "10 0 R", compressed: false, predicted: false, placed: true, placedToo: placed);
        using var document = PdfDocument.Open(file);
        var report = new PdfValidator().Validate(document);

        document.WasRepaired.Should().Be(malformed);

        if (malformed)
        {
            Single(report, PdfValidationRuleIds.XRefSectionMalformed).Message.Should().EndWith(
                "cannot be read: its /Index leads from reference to reference without reaching a value.");
        }
        else
        {
            report.Contains(PdfValidationRuleIds.XRefSectionMalformed).Should().BeFalse();
        }
    }

    [Theory]
    [InlineData("/Filter /FlateDecode /DecodeParms << /Predictor 12 /Columns 7 /Foo 6 0 R >>", true, true)]
    [InlineData("/Filter /FlateDecode /DecodeParms << /Predictor 12 /Columns 7 /EarlyChange 6 0 R >>", true, true)]
    [InlineData("/Filter /FlateDecode /DecodeParms << /Columns 6 0 R >>", true, false)]
    [InlineData("/Filter [/DCTDecode /FlateDecode] /DecodeParms [null << /Predictor 6 0 R >>]", false, false)]
    [InlineData("/DecodeParms 6 0 R", false, false)]
    [InlineData("/Size 6 0 R /Index [0 8]", false, false)]
    public void A_value_the_rows_are_read_without_leaves_the_section_readable_though_the_chain_cannot_read_it(string entries, bool compressed, bool predicted)
    {
        // Object 6, which only the stream indexes, cannot be read while the chain is; the rows are read without it: an
        // entry no filter reads, an /EarlyChange Flate does not read, the /Columns of no predictor, the parameters of a step
        // past an image filter, where decoding stops, those of data not encoded, a /Size beside an /Index. Each once made
        // the section malformed, and the index rebuilt.
        var file = ChainFiles.UnderAnUpdate(
            "/Type /XRef " + (entries.Contains("/Size", StringComparison.Ordinal) ? string.Empty : "/Size 8 ") + "/W [1 4 2] " + entries + " /Length {length}",
            "0",
            compressed,
            predicted,
            placed: true,
            six: "8");
        using var document = PdfDocument.Open(file);

        document.WasRepaired.Should().BeFalse();
        new PdfValidator().Validate(document).Contains(PdfValidationRuleIds.XRefSectionMalformed).Should().BeFalse();
        document.GetObject(new PdfObjectId(3)).AsDictionary().IsOfType(PdfName.Page).Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(ChainFiles.Keys), MemberType = typeof(ChainFiles))]
    public void A_cross_reference_stream_value_written_as_a_reference_is_read_where_a_newer_section_places_it_and_reported(string key)
    {
        // As qpdf, MuPDF, PDFBox and pdf.js read it. ISO 32000-1 makes the value direct (7.5.8.2) — but the /Length, and,
        // before PDF 2.0, what a /DecodeParms holds —, and a /Size is file.size-wrong's.
        using var document = PdfDocument.Open(ChainFiles.UnderAnUpdate(key, placed: true));
        var report = new PdfValidator().Validate(document);

        document.WasRepaired.Should().BeFalse();
        document.Diagnostics.Should().BeEmpty();
        report.Findings.Select(finding => finding.RuleId).Should().Equal(key switch
        {
            "Length" or "Columns" => [],
            "Size" => [PdfValidationRuleIds.FileSizeWrong],
            _ => new[] { PdfValidationRuleIds.FileTrailerValueWrong },
        });
    }

    [Theory]
    [InlineData("W", "its /W")]
    [InlineData("W element", "element 1 of its /W")]
    [InlineData("Index", "its /Index")]
    [InlineData("Index element", "element 1 of its /Index")]
    [InlineData("Type", "its /Type")]
    [InlineData("Filter", "its /Filter")]
    [InlineData("DecodeParms", "its /DecodeParms")]
    public void A_value_a_cross_reference_stream_writes_as_a_reference_is_named_where_it_is_written(string key, string where)
    {
        var file = ChainFiles.UnderAnUpdate(key, placed: true);
        var stream = PdfTemplate.OffsetOf(file, "\n7 0 obj") + 1;

        var finding = Single(Validate(file), PdfValidationRuleIds.FileTrailerValueWrong);

        finding.Severity.Should().Be(PdfValidationSeverity.Warning);
        finding.Location.Position.Should().Be(stream);
        finding.Message.Should().Be(string.Create(
            CultureInfo.InvariantCulture,
            $"The cross-reference stream at offset {stream} writes {where} as the reference 5 0 R, where ISO 32000-1 makes it direct (7.5.8.2)."));
    }

    [Theory]
    [InlineData("Length", "its /Length")]
    [InlineData("Columns", "/Columns of its /DecodeParms")]
    public void A_cross_reference_stream_value_written_as_a_reference_is_reported_in_a_file_that_declares_pdf_2_0(string key, string where)
    {
        // An erratum of ISO 32000-2 (pdf-issues #246) makes the values of Table 5 direct in a cross-reference stream, and
        // their own elements and entries: a PDF 1.x file may write them as references.
        var file = ChainFiles.UnderAnUpdate(key, placed: true, version: "2.0");

        Single(Validate(file), PdfValidationRuleIds.FileTrailerValueWrong).Message.Should().Contain(
            $"writes {where} as the reference 5 0 R, where ISO 32000-2 makes it direct (7.5.8.2)");
    }

    [Theory]
    [InlineData("1.5", false)]
    [InlineData("2.0", true)]
    public void An_element_of_a_cross_reference_stream_s_decoding_arrays_written_as_a_reference_is_reported_in_a_file_that_declares_pdf_2_0(
        string version, bool reported)
    {
        // An erratum of ISO 32000-2 (pdf-issues #246) makes the elements of the /Filter and /DecodeParms arrays direct; a
        // PDF 1.x file may write them as references.
        var file = ChainFiles.UnderAnUpdate(
            "/Type /XRef /Size 8 /W [1 4 2] /Filter [5 0 R] /DecodeParms [10 0 R] /Length {length}",
            "/FlateDecode",
            compressed: true,
            predicted: true,
            placed: true,
            placedToo: new Dictionary<int, string> { [10] = "<< /Predictor 12 /Columns 7 >>" },
            version: version);
        var stream = PdfTemplate.OffsetOf(file, "\n7 0 obj") + 1;

        Validate(file).Findings.Where(finding => finding.RuleId == PdfValidationRuleIds.FileTrailerValueWrong).Select(finding => finding.Message)
            .Should().Equal(reported
                ? [
                    string.Create(CultureInfo.InvariantCulture, $"The cross-reference stream at offset {stream} writes element 0 of its /Filter as the reference 5 0 R, where ISO 32000-2 makes it direct (7.5.8.2)."),
                    string.Create(CultureInfo.InvariantCulture, $"The cross-reference stream at offset {stream} writes element 0 of its /DecodeParms as the reference 10 0 R, where ISO 32000-2 makes it direct (7.5.8.2)."),
                ]
                : []);
    }

    [Theory]
    [InlineData("2.0", "writes its /Length as the reference 5 0 R, to a real number: ISO 32000-2 makes it direct (7.5.8.2), and Table 5 of ISO 32000-1 an integer.")]
    [InlineData("1.5", "gives its /Length as a real number, where Table 5 of ISO 32000-1 asks for an integer.")]
    public void A_reference_that_leads_to_a_real_is_one_finding_that_says_both(string version, string message)
    {
        // ADR 45: one fault, one finding. A PDF 1.x file may write the /Length as a reference: only the real is its fault.
        var file = ChainFiles.UnderAnUpdate(
            "/Type /XRef /Size 8 /W [1 4 2] /Length 5 0 R", "56.0", compressed: false, predicted: false, placed: true, version: version);

        Single(Validate(file), PdfValidationRuleIds.FileTrailerValueWrong).Message.Should().EndWith(message);
    }

    [Fact]
    public void A_predictor_written_as_a_real_in_an_array_of_parameters_is_reported()
    {
        // #215: read as the integer it equals, and reported as Table 8 asks, whether the parameters stand alone or in an
        // array.
        var file = ChainFiles.UnderAnUpdate(
            "/Type /XRef /Size 8 /W [1 4 2] /Filter [/FlateDecode] /DecodeParms [<< /Predictor 12.0 /Columns 7 >>] /Length {length}",
            "0",
            compressed: true,
            predicted: true,
            placed: true);
        using var document = PdfDocument.Open(file);

        document.WasRepaired.Should().BeFalse();
        Single(new PdfValidator().Validate(document), PdfValidationRuleIds.FileTrailerValueWrong).Message.Should().EndWith(
            "gives /Predictor of element 0 of its /DecodeParms as a real number, where Table 8 of ISO 32000-1 asks for an integer.");
    }

    [Fact]
    public void The_entry_a_deferred_length_found_off_its_object_is_judged_as_the_file_wrote_it()
    {
        // #182: the older stream's /Length names object 10, whose entry lies 2 bytes before its header. The chain once
        // corrected that entry as it read the stream, and the probe judged the corrected one. The /Length is read once the
        // chain is, behind a copy of the index as the file wrote it.
        var file = StreamLengthTests.LengthObjectOffItsEntry(out var tenAt);

        var finding = Validate(file).Findings.Should().ContainSingle(finding => finding.RuleId == PdfValidationRuleIds.XRefEntryShifted
            && finding.Location.Object == new PdfObjectId(10)).Which;

        finding.Message.Should().Contain(string.Create(CultureInfo.InvariantCulture, $"offset {tenAt - 2}"));
    }

    [Theory]
    [InlineData("{offset}", null)]
    [InlineData("6 0 R", null)]
    [InlineData("{offset}.0", "gives its /XRefStm as a real number, where Table 19 of ISO 32000-1 asks for an integer.")]
    public void A_hybrid_file_whose_xrefstm_is_a_reference_or_a_real_is_read_whole(string xrefStm, string? finding)
    {
        // ISO 32000-1 asks no /XRefStm to be direct (Table 19): one written as a reference, read where the table places
        // its object, names the stream, as qpdf, MuPDF, PDFBox and pdf.js read it. It was once no offset, the stream lost
        // and the index rebuilt. A real with no fractional part is read as the integer it equals, and reported (#215).
        using var document = PdfDocument.Open(ChainFiles.HybridNamingItsStream(xrefStm));
        var report = new PdfValidator().Validate(document);

        document.WasRepaired.Should().BeFalse();
        document.Diagnostics.Should().BeEmpty();
        document.GetObject(new PdfObjectId(3)).AsDictionary().IsOfType(PdfName.Page).Should().BeTrue("only the stream indexes the page");

        if (finding is null)
        {
            report.Findings.Should().BeEmpty();
        }
        else
        {
            Single(report, PdfValidationRuleIds.FileTrailerValueWrong).Message.Should().EndWith(finding);
        }
    }

    [Theory]
    [InlineData("9 0 R", "its /Prev as the reference 9 0 R, where ISO 32000-1 makes it direct (Table 15)")]
    [InlineData("{offset}.0", "its /Prev as a real number, where Table 15 of ISO 32000-1 asks for an integer")]
    public void A_prev_written_as_a_reference_or_a_real_is_followed_and_reported(string prev, string what)
    {
        // A reference a section already read places, and a real with no fractional part, as PDFBox and pdf.js read them
        // (#182, #215); the chain goes on, and the form is reported.
        var file = ChainFiles.UpdateNamingItsPrevious(prev);
        using var document = PdfDocument.Open(file);

        document.WasRepaired.Should().BeFalse();
        document.Diagnostics.Should().BeEmpty();
        Single(new PdfValidator().Validate(document), PdfValidationRuleIds.FileTrailerValueWrong).Message.Should().Contain(what);
    }

    [Theory]
    [InlineData("/Size 6 /W [1 4.0 2]", "element 1 of its /W")]
    [InlineData("/Size 6 /W [1 4 2] /Index [0.0 6]", "element 0 of its /Index")]
    [InlineData("/Size 6 /W [1 4 2] /Index [0 6.]", "element 1 of its /Index")]
    public void A_cross_reference_stream_integer_written_as_a_real_is_read_as_the_integer_and_reported(string layout, string what)
    {
        // #215: the reader reads it as the integer it equals, as MuPDF, PDFBox and pdf.js do; qpdf refuses the section.
        var file = Replace(XRefStreamFile(), "/Size 6 /W [1 4 2]", layout);
        using var document = PdfDocument.Open(file);

        document.WasRepaired.Should().BeFalse();
        Single(new PdfValidator().Validate(document), PdfValidationRuleIds.FileTrailerValueWrong).Message.Should().Contain(
            $"gives {what} as a real number, where Table 17 of ISO 32000-1 asks for an integer.");
    }

    [Fact]
    public void A_cross_reference_stream_length_and_predictor_written_as_reals_are_read_and_reported()
    {
        // #215: a /Length, a /Predictor and /Columns written with a fractional part of zero: read as written, each
        // reported once, beside the /DecodeParms the stream writes as a reference.
        using var document = PdfDocument.Open(ChainFiles.UnderAnUpdate("DecodeParms", placed: true, reals: true));
        var report = new PdfValidator().Validate(document);

        document.WasRepaired.Should().BeFalse();
        report.Findings.Where(finding => finding.RuleId == PdfValidationRuleIds.FileTrailerValueWrong).Select(finding => finding.Message)
            .Should().HaveCount(4).And.ContainMatch("*its /DecodeParms as the reference 5 0 R*")
            .And.ContainMatch("*gives its /Length as a real number, where Table 5*")
            .And.ContainMatch("*gives /Predictor of its /DecodeParms as a real number, where Table 8*")
            .And.ContainMatch("*gives /Columns of its /DecodeParms as a real number, where Table 8*");
    }

    [Theory]
    [InlineData("/Size 6.0 /W [1 4 2] /Index [0 6]", "gives its /Size as a real number, where the count of objects is an integer.")]
    [InlineData("/Size 1 0 R /W [1 4 2] /Index [0 6]", "writes its /Size as the reference 1 0 R, where the count of objects is written directly.")]
    public void A_size_written_as_a_real_or_a_reference_is_said_to_be(string layout, string message)
    {
        // Read all the same: /Index numbers the rows. The rule once said such a /Size was no count of objects.
        Single(Validate(Replace(XRefStreamFile(), "/Size 6 /W [1 4 2]", layout)), PdfValidationRuleIds.FileSizeWrong).Message
            .Should().EndWith(message);
    }

    [Theory]
    [InlineData("/N", 16)]
    [InlineData("/First", 16)]
    [InlineData("/Length", 5)]
    public void An_object_stream_integer_written_as_a_real_is_read_as_the_integer_and_reported(string key, int table)
    {
        // #215: each is read, as MuPDF, PDFBox and pdf.js read /N and /First; qpdf and poppler lose them. The space after
        // the integer becomes its decimal point, so that no offset moves.
        var content = Encoding.Latin1.GetString(XRefStreamFile());
        var point = content.IndexOf(key + " ", content.IndexOf("/ObjStm", StringComparison.Ordinal), StringComparison.Ordinal) + key.Length + 1;

        while (char.IsAsciiDigit(content[point]))
        {
            point++;
        }

        using var document = PdfDocument.Open(Encoding.Latin1.GetBytes(content[..point] + "." + content[(point + 1)..]));

        document.GetObject(new PdfObjectId(3)).AsDictionary().IsOfType(PdfName.Page).Should().BeTrue();
        document.WasRepaired.Should().BeFalse();
        var finding = Single(new PdfValidator().Validate(document), PdfValidationRuleIds.XRefObjectStreamValueWrong);
        finding.Severity.Should().Be(PdfValidationSeverity.Warning);
        finding.Location.Object.Should().Be(new PdfObjectId(4));
        finding.Message.Should().Be(string.Create(
            CultureInfo.InvariantCulture,
            $"Object stream 4 gives its {key} as a real number, where Table {table} of ISO 32000-1 asks for an integer; the reader read it as the integer it equals."));
    }

    [Theory]
    [InlineData("3 1 obj")]
    [InlineData("3 0000000000000000000000000000000000000000000000000000000000000000000001 obj")]
    public void A_relocated_entry_whose_generation_its_header_contradicts_is_reported_however_long_the_header(string header)
    {
        // #118: the probe read the header it found nearby again through its own 64 bytes, and one longer than that — zeros
        // leading its generation — gave it none to compare: the reader relocated the object under 3 1, and nothing said
        // that the row gives 3 0. The probe takes the generation the search read, which the reader records.
        var template = PdfTemplate.Sound
            .Replace("3 0 obj", header, StringComparison.Ordinal)
            .Replace("{row:3}", "{row:3:0:4}", StringComparison.Ordinal);

        var finding = Single(Validate(PdfTemplate.Build(template)), PdfValidationRuleIds.XRefGenerationMismatch);

        finding.Location.Object.Should().Be(new PdfObjectId(3));
        finding.Message.Should().Be("The entry of object 3 gives generation 0, and the object is written as 3 1 obj.");
    }

    [Theory]
    [InlineData(-3, false)]
    [InlineData(-3, true)]
    [InlineData(9999, false)]
    [InlineData(9999, true)]
    public void An_object_stream_the_walk_relocates_or_rebuilds_is_located_where_its_row_places_it(int off, bool readFirst)
    {
        // #118, #128: object stream 4 is written 4 1 obj with a real /N, and its row gives generation 0, 3 bytes before its
        // header or outside the file. Loading it in the dependency walk relocated it, or rebuilt the index, under the walk's
        // own index, which then gave the header's generation and offset: the stream was named 4 1 there, 4 0 by the
        // cross-reference rules, and 4 0 again once the stream had been read before validating.
        var sound = XRefStreamFile();
        var four = PdfTemplate.OffsetOf(sound, "\n4 0 obj") + 1;
        var rows = WithRows([1, 4, 2], number: 4, type: 1, second: (ulong)(four + off), third: 0);
        var file = Encoding.Latin1.GetBytes(Encoding.Latin1.GetString(rows)
            .Replace("\n4 0 obj", "\n4 1 obj", StringComparison.Ordinal)
            .Replace("/N 2 /First", "/N 2./First", StringComparison.Ordinal));
        using var document = PdfDocument.Open(file);

        if (readFirst)
        {
            document.GetObject(new PdfObjectId(4)).Should().BeOfType<PdfStream>();
        }

        var finding = Single(new PdfValidator().Validate(document), PdfValidationRuleIds.XRefObjectStreamValueWrong);

        finding.Location.Object.Should().Be(new PdfObjectId(4));
        finding.Location.Position.Should().Be(four + off);
    }

    [Fact]
    public void A_cut_outside_the_file_named_from_no_known_section_is_reported_at_the_document()
    {
        // Only a section the chain has read names one past the sections the reader reads, so the reader always knows where
        // a cut outside the file is written; the rule, given a structure no file produces that does not, locates the
        // finding at the document.
        var file = PdfTemplate.Build(Updated);
        using var document = PdfDocument.Open(file);
        var structure = document.Reader.Structure;
        structure.ChainCutAt = file.Length + 100L;
        structure.ChainCutWrittenOffset = file.Length + 100L;
        var context = new ValidationContext(document, capacity: 16);

        new CheckedInPartRule().Check(context);

        var finding = context.ToReport(ValidationProfile.Structural).Findings.Should().ContainSingle().Which;
        finding.Location.IsDocument.Should().BeTrue();
        finding.Message.Should().StartWith(string.Create(
            CultureInfo.InvariantCulture, $"The cross-reference chain goes on at offset {file.Length + 100}, outside the file,"));
    }

    [Fact]
    public void An_object_stream_a_rebuild_found_is_located_under_its_header_s_generation()
    {
        // #118: startxref names nothing, and the rebuilt index records object stream 4 as its header writes it, 4 2 obj:
        // the chain gave no index for the rules to name it by, and the one the reader reads with gives the header's.
        var content = Encoding.Latin1.GetString(XRefStreamFile())
            .Replace("\n4 0 obj", "\n4 2 obj", StringComparison.Ordinal)
            .Replace("/N 2 /First", "/N 2./First", StringComparison.Ordinal);
        var startxref = content.LastIndexOf("startxref\n", StringComparison.Ordinal) + "startxref\n".Length;
        var file = Encoding.Latin1.GetBytes(content[..startxref] + "999999\n%%EOF\n");

        using var document = PdfDocument.Open(file);

        document.WasRepaired.Should().BeTrue();
        Single(new PdfValidator().Validate(document), PdfValidationRuleIds.XRefObjectStreamValueWrong).Location.Object
            .Should().Be(new PdfObjectId(4, 2));
    }

    [Fact]
    public void An_encrypted_document_s_object_streams_are_said_to_be_unchecked()
    {
        var file = Replace(
            XRefStreamBuilder().WithObject(5, "<< /Filter /Standard /V 1 /R 2 /O <00> /U <00> /P -4 >>").BuildWithXRefStream(rootNumber: 1, compressedObjects: [2, 3]),
            "/Type /XRef",
            "/Type /XRef /Encrypt 5 0 R");
        using var document = PdfDocument.Open(file, new PdfReaderOptions { ThrowOnEncrypted = false });

        var finding = new PdfValidator().Validate(document).Findings.Should().ContainSingle().Which;

        finding.RuleId.Should().Be(PdfValidationRuleIds.XRefCheckedInPart);
        finding.Location.IsDocument.Should().BeTrue();
        finding.Message.Should().Be(
            "The document is encrypted: the 2 objects its index places in object streams were not checked, those streams being readable only once decrypted.");
    }

    [Fact]
    public void What_was_read_before_validating_changes_nothing()
    {
        // Resolving the misplaced object makes the reader correct its own index; the rules judge the file's.
        var file = PdfTemplate.SoundWith("{row:3}", "{row:3:0:-5}");
        using var document = PdfDocument.Open(file);
        var validator = new PdfValidator();

        var before = validator.Validate(document);
        document.GetObject(new PdfObjectId(3)).AsDictionary().Should().NotBeNull();
        document.GetObject(new PdfObjectId(9)).Should().Be(PdfNull.Instance);
        var after = validator.Validate(document);

        after.Findings.Should().Equal(before.Findings);
        after.Findings.Should().ContainSingle().Which.RuleId.Should().Be(PdfValidationRuleIds.XRefEntryShifted);
    }

    [Fact]
    public void A_rebuild_that_happens_after_opening_leaves_the_file_s_index_to_judge()
    {
        // Object 3 is nowhere near its offset: reading it rebuilds the index, and the report stays the same.
        var file = PdfTemplate.Build(PdfTemplate.Spread.Replace("{row:3}", "{row:1}", StringComparison.Ordinal));
        using var document = PdfDocument.Open(file);
        var validator = new PdfValidator();

        var before = validator.Validate(document);
        document.GetObject(new PdfObjectId(3)).AsDictionary().Should().NotBeNull();
        document.WasRepaired.Should().BeTrue();
        var after = validator.Validate(document);

        after.Findings.Should().Equal(before.Findings);
        after.Contains(PdfValidationRuleIds.XRefEntryBroken).Should().BeTrue();
    }

    /// <summary>A document whose index is a cross-reference stream, objects 2 and 3 packed in object stream 4.</summary>
    private static byte[] XRefStreamFile() => XRefStreamBuilder().BuildWithXRefStream(rootNumber: 1, compressedObjects: [2, 3]);

    /// <summary>The catalog, the page tree and the page, objects 1 to 3.</summary>
    private static TestPdfBuilder XRefStreamBuilder() =>
        new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .WithObject(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << >> >>");

    /// <summary>
    /// <see cref="XRefStreamFile"/> with its index written again as object 5, in rows of <paramref name="widths"/> bytes:
    /// object 0 free, the catalog and object stream 4 at their offsets, objects 2 and 3 in object stream 4, and the index
    /// at its own — but for the row of object <paramref name="number"/>, written with the fields given, a
    /// <paramref name="second"/> left out keeping its own, and the first byte of its field <paramref name="leading"/>, one
    /// wider than 8 bytes, set to 1.
    /// </summary>
    private static byte[] WithRows(int[] widths, int number, ulong type, ulong? second, ulong third, int leading = -1)
    {
        var sound = XRefStreamFile();
        var xref = (int)PdfTemplate.OffsetOf(sound, "\n5 0 obj") + 1;
        (ulong Type, ulong Second, ulong Third)[] rows =
        [
            (0, 0, 65535),
            (1, (ulong)PdfTemplate.OffsetOf(sound, "\n1 0 obj") + 1, 0),
            (2, 4, 0),
            (2, 4, 1),
            (1, (ulong)PdfTemplate.OffsetOf(sound, "\n4 0 obj") + 1, 0),
            (1, (ulong)xref, 0),
        ];
        rows[number] = (type, second ?? rows[number].Second, third);
        var data = new List<byte>();

        for (var row = 0; row < rows.Length; row++)
        {
            var (first, middle, last) = rows[row];
            Field(first, widths[0], row == number && leading == 0);
            Field(middle, widths[1], row == number && leading == 1);
            Field(last, widths[2], row == number && leading == 2);
        }

        return
        [
            .. sound.AsSpan(0, xref),
            .. Encoding.Latin1.GetBytes(string.Create(
                CultureInfo.InvariantCulture,
                $"5 0 obj\n<< /Type /XRef /Size 6 /W [{widths[0]} {widths[1]} {widths[2]}] /Root 1 0 R /Length {data.Count} >>\nstream\n")),
            .. data,
            .. Encoding.Latin1.GetBytes(string.Create(CultureInfo.InvariantCulture, $"\nendstream\nendobj\nstartxref\n{xref}\n%%EOF\n")),
        ];

        void Field(ulong value, int width, bool leadingOne)
        {
            // Bytes past the eight a value holds are its leading zeros, unless the first is asked for as 1.
            for (var shift = (width - 1) * 8; shift >= 0; shift -= 8)
            {
                data.Add(shift >= 64 ? (byte)(leadingOne && shift == (width - 1) * 8 ? 1 : 0) : (byte)(value >> shift));
            }
        }
    }

    /// <summary>A cross-reference stream's row, as <see cref="TestPdfBuilder"/> writes it: an object at <paramref name="offset"/>.</summary>
    private static string Row(long offset) =>
        Encoding.Latin1.GetString([1, (byte)(offset >> 24), (byte)(offset >> 16), (byte)(offset >> 8), (byte)offset, 0, 0]);

    /// <summary>Lists object 3 first and object 2 second in the object stream's header, their offsets unchanged.</summary>
    private static byte[] SwapObjectStreamNumbers(byte[] file)
    {
        var text = Encoding.Latin1.GetString(file);
        var start = text.IndexOf("stream\n2 0 3 ", StringComparison.Ordinal) + "stream\n".Length;
        var swapped = "3" + text[(start + 1)..(start + 4)] + "2" + text[(start + 5)..];
        return Encoding.Latin1.GetBytes(text[..start] + swapped);
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
