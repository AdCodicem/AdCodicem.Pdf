using System.Globalization;
using System.IO.Compression;
using System.Text;
using AdCodicem.Pdf.Diagnostics;
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
    [InlineData("xref\n3000000000 4\n")]
    public void A_subsection_header_without_a_first_number_and_a_count_it_can_hold_makes_the_table_malformed(string header)
    {
        var file = PdfTemplate.SoundWith("xref\n0 4\n", header);

        Single(Validate(file), PdfValidationRuleIds.XRefSectionMalformed).Message
            .Should().Contain("does not give a first object number and a count of rows");
    }

    [Fact]
    public void A_subsection_claiming_more_rows_than_the_table_holds_makes_it_malformed_where_the_rows_end()
    {
        // Fifty million and one rows, which the table does not hold: a count is no fault in itself, however large, since
        // the rows it numbers are read only as far as they are there. The first that is not makes the table malformed.
        var file = PdfTemplate.SoundWith("xref\n0 4\n", "xref\n0 50000001\n");

        Single(Validate(file), PdfValidationRuleIds.XRefSectionMalformed).Message.Should().EndWith(
            $"cannot be read: the row for object 4, at offset {PdfTemplate.OffsetOf(file, "trailer")}, is not an offset, a generation and n or f.");
    }

    [Theory]
    [InlineData("2147483647 2", true)]
    [InlineData("2147483646 2", false)]
    [InlineData("2 9223372036854775807", true)]
    public void A_subsection_numbering_rows_past_the_largest_object_number_makes_the_table_malformed(string header, bool malformed)
    {
        // A second subsection of rows placing the page again: its last number, first + count - 1, once wrapped past
        // int.MaxValue to a negative object number, and, with a count as large as a long, past what a long holds.
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
    [InlineData("/Size 6 /W [1 4 2] /Index [0 4294967302]", "its /Index gives a subsection of 4,294,967,302 rows from object 0, outside object numbers 0 to 2147483647.")]
    [InlineData("/Size 6 /W [1 4 2] /Index 6", "its /Index is the integer 6, not an array.")]
    [InlineData("/Size 6 /W [1 4 2] /Index /All", "its /Index is the name /All, not an array.")]
    [InlineData("/Size 6 /W [1 4 2] /Index []", "its /Index holds 0 values, not pairs of a first object number and a count of rows.")]
    [InlineData("/Size 6 /W [1 4 2] /Index [0]", "its /Index holds 1 value, not pairs of a first object number and a count of rows.")]
    [InlineData("/Size 6 /W [1 4 2] /Index [0 6 7]", "its /Index holds 3 values, not pairs of a first object number and a count of rows.")]
    [InlineData("/Size 6 /W [1 4 2] /Index [0 (six)]", "its /Index holds a value of type string where an integer belongs.")]
    [InlineData("/Size 6 /W [1 4 2] /Index [/Zero 6]", "its /Index holds the name /Zero where an integer belongs.")]
    [InlineData("/Size 6 /W [1 4 2] /Index [0 null]", "its /Index holds null where an integer belongs.")]
    [InlineData("/Size 6 /W [1 4 2] /Index [92233720368547758080 6]", "its /Index holds the real number 92233720368547760000.0 where an integer belongs.")]
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
    [InlineData("/Size 6 /W [1 4 2] /Filter /FlateDecode /DecodeParms [1 0 R]", "its /DecodeParms holds the reference 1 0 R, which no section read before it places where it can be read.")]
    [InlineData("/Size 6 /W [1 4 2] /Filter [/Crypt /FlateDecode] /DecodeParms [null 1 0 R]", "its /DecodeParms holds the reference 1 0 R, which no section read before it places where it can be read.")]
    [InlineData("/Size 1 0 R /W [1 4 2] /Index null", "its /Size is the reference 1 0 R, which no section read before it places where it can be read.")]
    [InlineData("/Size 6 /W [1 4 2] /Index [-5 1]", "its /Index gives a subsection of 1 row from object -5, outside object numbers 0 to 2147483647.")]
    [InlineData("/Size 6 /W [1 4 2] /Index [2147482000 5000]", "its /Index gives a subsection of 5,000 rows from object 2147482000, outside object numbers 0 to 2147483647.")]
    [InlineData("/Size 6 /W [1 4 2] /Index [-5 6]", "its /Index gives a subsection of 6 rows from object -5, outside object numbers 0 to 2147483647.")]
    [InlineData("/Size 6 /W [1 4 2] /Index [4294967296 6]", "its /Index gives a subsection of 6 rows from object 4294967296, outside object numbers 0 to 2147483647.")]
    [InlineData("/Size 6 /W [1 4 2] /Index [2147483647 6]", "its /Index gives a subsection of 6 rows from object 2147483647, outside object numbers 0 to 2147483647.")]
    [InlineData("/Size 6 /W [1 4 2] /Index [0 6 2147483648 0]", "its /Index gives a subsection of 0 rows from object 2147483648, outside object numbers 0 to 2147483647.")]
    [InlineData("/Size 6 /W [1 4 2] /Index [0 6 2 9223372036854775807]", "its /Index gives a subsection of 9,223,372,036,854,775,807 rows from object 2, outside object numbers 0 to 2147483647.")]
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
    [InlineData("/Size 6 /W [1 4 2] /Filter /Crypt /DecodeParms 1 0 R", false)]
    [InlineData("/Size 6 /W [1 4 2] /Filter [/Crypt /DCTDecode 1 0 R] /DecodeParms [1 0 R 1 0 R 1 0 R]", false)]
    public void A_cross_reference_stream_whose_numbering_holds_is_not_malformed(string layout, bool repaired)
    {
        // Integral reals are read as the integers they equal, as everywhere the reader reads a value, and reported as the
        // file's fault elsewhere (file.trailer-value-wrong); a subsection may end at the largest object number. Rows
        // numbered from 2147483642 index no catalog, which a rebuild then finds. Parameters no step reads, and what stands
        // past an image filter, are not followed, as decoding does not resolve them.
        using var document = PdfDocument.Open(Replace(XRefStreamFile(), "/Size 6 /W [1 4 2]", layout));

        new PdfValidator().Validate(document).Contains(PdfValidationRuleIds.XRefSectionMalformed).Should().BeFalse();
        document.WasRepaired.Should().Be(repaired);
    }

    [Fact]
    public void A_cross_reference_stream_s_filters_past_the_guard_are_not_followed()
    {
        // Sixty-four /Crypt filters, then a reference no section places: the check of the section's values stops at the
        // guard before it, as decoding does, so the section is no malformed one. What the filter past the guard would
        // make of the rows is unknown: the section is cut before them, and the chain it leaves with no section is
        // rebuilt, as a guard that leaves none rebuilds it.
        var layout = "/Size 6 /W [1 4 2] /Filter [" + string.Concat(Enumerable.Repeat("/Crypt ", 64)) + "1 0 R]";
        using var document = PdfDocument.Open(Replace(XRefStreamFile(), "/Size 6 /W [1 4 2]", layout));

        new PdfValidator().Validate(document).Contains(PdfValidationRuleIds.XRefSectionMalformed).Should().BeFalse();
        document.Diagnostics.Select(d => d.Code).Should().Equal(PdfDiagnosticCodes.LimitFilterChain, PdfDiagnosticCodes.XRefRebuilt);
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
    [InlineData("", true)]
    [InlineData("0 0\n", false)]
    public void A_table_holding_no_subsection_is_a_warning_and_one_whose_one_subsection_is_0_0_is_sound(string subsections, bool missing)
    {
        // An update whose trailer keyword follows its xref keyword, where ISO 32000-1 (7.5.4) has one or more subsections
        // follow it, had no finding (#222). The reader reads it as indexing nothing, as the lawful 0 0 update, and rebuilds
        // nothing for it: a warning (ADR 45).
        var file = PdfTemplate.Build(PartialFiles.EmptyUpdate(subsections));
        var update = PdfTemplate.OffsetOf(file, "\nxref\n", 2) + 1;
        using var document = PdfDocument.Open(file);

        var report = new PdfValidator().Validate(document);

        document.WasRepaired.Should().BeFalse("the index is whole");

        if (!missing)
        {
            report.Findings.Should().BeEmpty();
            return;
        }

        var finding = report.Findings.Should().ContainSingle().Which;
        finding.RuleId.Should().Be(PdfValidationRuleIds.XRefSubsectionMissing);
        finding.Severity.Should().Be(PdfValidationSeverity.Warning);
        finding.Location.Position.Should().Be(update);
        finding.Message.Should().Be(string.Create(
            CultureInfo.InvariantCulture,
            $"The cross-reference table at offset {update} holds no subsection: its trailer keyword follows its xref keyword."));
    }

    [Theory]
    [InlineData("")]
    [InlineData("t")]
    [InlineData("trai")]
    public void A_table_the_end_of_the_file_cuts_after_a_row_is_missing_its_trailer_and_its_chain_s_size_is_not_judged(string tail)
    {
        // The table /Prev names is cut by the end of the file after object 3's row: its trailer is missing, an error, and the
        // index is rebuilt before the rules run, what that trailer named being lost to the chain (#207). The update's /Size of
        // 5 numbers object 4, which only the sections the lost trailer named index: the chain read in part is not judged for
        // it, as one that lost a section is not, where it was said to make the /Size 4. Cut within the trailer keyword, it is
        // missing its trailer all the same, where it was said to hold the start of the keyword where a subsection should start.
        var file = PdfTemplate.Build(PartialFiles.CutUpdate("3 1\n{row:3}\n" + tail));
        var cut = PdfTemplate.OffsetOf(file, "\nxref\n", 3) + 1;
        using var document = PdfDocument.Open(file);

        var finding = new PdfValidator().Validate(document).Findings.Should().ContainSingle().Which;

        finding.RuleId.Should().Be(PdfValidationRuleIds.FileTrailerMissing);
        finding.Severity.Should().Be(PdfValidationSeverity.Error);
        finding.Location.Position.Should().Be(file.Length);
        finding.Message.Should().Be(string.Create(
            CultureInfo.InvariantCulture, $"The cross-reference table at offset {cut} is not followed by a trailer: the file ends first."));
        document.WasRepaired.Should().BeTrue();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_table_whose_last_row_is_no_row_is_malformed_and_its_chain_s_size_is_not_judged(bool updated)
    {
        // Object 4's row, the table's last, ends in x: the table is malformed, an error, the index rebuilt before the rules
        // run. Its /Size of 5 numbers object 4, whose row it lost: the chain read in part is not judged for it (#210).
        var file = PdfTemplate.Build(PartialFiles.RowEndingInX(updated));
        var table = PdfTemplate.OffsetOf(file, "\nxref\n") + 1;
        var row = PdfTemplate.OffsetOf(file, string.Create(CultureInfo.InvariantCulture, $"{PdfTemplate.OffsetOf(file, "4 0 obj")} 00000 x"));

        var finding = Validate(file).Findings.Should().ContainSingle().Which;

        finding.RuleId.Should().Be(PdfValidationRuleIds.XRefSectionMalformed);
        finding.Location.Position.Should().Be(table);
        finding.Message.Should().Be(string.Create(
            CultureInfo.InvariantCulture,
            $"The cross-reference table at offset {table} is malformed: the row for object 4, at offset {row}, is not an offset, a generation and n or f."));
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
    [InlineData("trai", "the keyword trai")]
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
    [InlineData("9223372036854775808", "The /Prev of the cross-reference section at offset {update} is the real number 9223372036854776000.0, not an offset.")]
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

    [Theory]
    [InlineData(700, 600, PdfValidationRuleIds.XRefSectionNotFound)]
    [InlineData(2000, 600, PdfValidationRuleIds.XRefSectionNotFound)]
    [InlineData(700, 500, PdfValidationRuleIds.XRefSectionShifted)]
    [InlineData(2000, 500, PdfValidationRuleIds.XRefSectionShifted)]
    public void A_section_prev_names_600_bytes_before_it_is_not_found_and_one_500_bytes_before_it_shifted_however_near_the_file_s_start(
        long section, long distance, string ruleId)
    {
        // /Prev names an offset in the file's first 512 bytes, or past them: a section 600 bytes after it is not found, and an
        // error, wherever the offset falls, and one 500 bytes after it shifted, a warning. A window clamped at the file's start
        // once found the first too, near the start alone (#197).
        var named = section - distance;
        var file = NearbyFiles.UpdateNamingItsOriginal(section, named);

        var finding = Single(Validate(file), ruleId);

        if (ruleId == PdfValidationRuleIds.XRefSectionNotFound)
        {
            finding.Severity.Should().Be(PdfValidationSeverity.Error);
            finding.Location.Position.Should().Be(named);
            finding.Message.Should().StartWith($"The cross-reference section /Prev names at offset {named} is not there, nor within 512 bytes of it:");
        }
        else
        {
            finding.Severity.Should().Be(PdfValidationSeverity.Warning);
            finding.Location.Position.Should().Be(section);
            finding.Message.Should().Be($"The cross-reference section /Prev names at offset {named} starts 500 bytes after it, at offset {section}.");
        }
    }

    [Theory]
    [InlineData(0, "")]
    [InlineData(0, "junk\n")]
    [InlineData(1200, "")]
    public void A_table_that_is_there_and_cannot_be_read_beside_the_one_naming_it_is_malformed_and_nothing_else(int gap, string junk)
    {
        // The first table holds a stray name where its trailer should start; the update's table, which names it, lies within
        // 512 bytes of it, or 1,200 bytes on, or the file starts with junk. The update's table was taken for the first moved:
        // the validator judged it twice — a /Size wrong twice —, found a shift and a loop the file does not have, and never
        // the malformed table (#188). The first table is malformed, and that is all.
        var (file, malformed, _) = NearbyFiles.MalformedTableUnderAnUpdate(gap, junk);
        var name = PdfTemplate.OffsetOf(file, "/Bad");

        var report = Validate(file);

        report.Findings.Should().ContainSingle(finding => finding.RuleId != PdfValidationRuleIds.FileHeaderOffset);
        var finding = Single(report, PdfValidationRuleIds.XRefSectionMalformed);
        finding.Location.Position.Should().Be(malformed);
        finding.Message.Should().Be(
            $"The cross-reference table at offset {malformed} cannot be read: it holds a name at offset {name}, where a subsection or the trailer should start.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1500)]
    public void An_xrefstm_naming_an_object_of_another_type_near_its_table_is_not_found(int gap)
    {
        // The hybrid table's /XRefStm names object 4, of /Type /Foo, within 512 bytes of the table, or 1,500 bytes before it.
        // The table itself was taken for the stream moved: a shift, a /Size said wrong, and a page the file lacks, but no error
        // for the stream lost (#188). The stream is not found, an error, and the page is found by rebuilding the index.
        var (file, four, _) = NearbyFiles.HybridNamingAnotherObject(gap);

        var report = Validate(file);

        var finding = report.Findings.Should().ContainSingle().Which;
        finding.RuleId.Should().Be(PdfValidationRuleIds.XRefSectionNotFound);
        finding.Location.Position.Should().Be(four);
        finding.Message.Should().Be(
            $"The cross-reference section /XRefStm names at offset {four} is not there, nor within 512 bytes of it: the offset holds object 4, which is not a cross-reference stream.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1200)]
    public void A_prev_naming_no_section_within_reach_of_the_table_naming_it_is_not_found_and_no_loop(int gap)
    {
        // The only table names a place inside the page tree's dictionary, within 512 bytes of the table or 1,200 bytes before
        // it: once a shift and a loop, the table read twice (#188); now a section not found.
        var (file, named, _) = NearbyFiles.TableNamingNoSection(gap);

        var finding = Validate(file).Findings.Should().ContainSingle().Which;

        finding.RuleId.Should().Be(PdfValidationRuleIds.XRefSectionNotFound);
        finding.Message.Should().Be(
            $"The cross-reference section /Prev names at offset {named} is not there, nor within 512 bytes of it: the offset holds neither the xref keyword nor an object.");
    }

    [Theory]
    [InlineData(69)]
    [InlineData(1269)]
    public void A_prev_naming_a_place_between_two_tables_shifts_to_the_one_the_chain_has_not_read(int after)
    {
        // /Prev names a place 115 bytes past the first table's keyword, and the update's table starts 69 bytes past it, or
        // 1,269: the first table is found, shifted, where once the update's was taken, and a loop found (#188).
        var (file, original, named, _) = NearbyFiles.UpdateNamingBetweenItsTables(115, after);

        var finding = Validate(file).Findings.Should().ContainSingle().Which;

        finding.RuleId.Should().Be(PdfValidationRuleIds.XRefSectionShifted);
        finding.Message.Should().Be($"The cross-reference section /Prev names at offset {named} starts 115 bytes before it, at offset {original}.");
    }

    [Theory]
    [InlineData("/W [1 4 2]", "/W [1 4 /]", "its /W gives a field a width that is not an integer", 0)]
    [InlineData("/W [1 4 2]", "/W [1 4 /]", "its /W gives a field a width that is not an integer", 1200)]
    [InlineData("/Root 1 0 R", "/Index [0 ]", "its /Index holds 1 value, not pairs of a first object number and a count of rows", 0)]
    [InlineData("/Root 1 0 R", "/Index [0 ]", "its /Index holds 1 value, not pairs of a first object number and a count of rows", 1200)]
    [InlineData("/Size 9 ", "/Size/X ", "it has no /Index, and its /Size gives no count of objects", 0)]
    [InlineData("/Size 9 ", "/Size/X ", "it has no /Index, and its /Size gives no count of objects", 1200)]
    public void A_cross_reference_stream_an_update_names_that_cannot_be_read_is_malformed_however_near_the_update(
        string written, string faulty, string fault, int gap)
    {
        // The older of two cross-reference streams has its /W, /Index or /Size written so that no row can be read, in place;
        // the newer, which names it, lies within 512 bytes of it, or 1,200 bytes on. The newer was taken for the older moved:
        // a shift and a loop, and the older never found malformed (#281). Read whole, the file has no finding.
        var sound = ChainFiles.UpdateWithIndirectLength(spellingEndStream: false, gap: gap);
        var older = PdfTemplate.OffsetOf(sound, "7 0 obj");
        var text = Encoding.Latin1.GetString(sound);
        var at = text.IndexOf(written, StringComparison.Ordinal);
        at.Should().BeLessThan((int)PdfTemplate.OffsetOf(sound, "stream\n"), "the older stream's dictionary holds it");
        var file = Encoding.Latin1.GetBytes(string.Concat(text.AsSpan(0, at), faulty, text.AsSpan(at + written.Length)));

        Validate(sound).Findings.Should().BeEmpty();
        var finding = Validate(file).Findings.Should().ContainSingle().Which;

        finding.RuleId.Should().Be(PdfValidationRuleIds.XRefSectionMalformed);
        finding.Location.Position.Should().Be(older);
        finding.Message.Should().Be($"The cross-reference stream at offset {older} cannot be read: {fault}.");
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1200)]
    [InlineData(true, 0)]
    [InlineData(true, 1200)]
    public void A_section_that_is_there_and_cannot_be_read_near_an_older_one_is_malformed_not_shifted(bool streams, int gap)
    {
        // A malformed table or /Type /XRef stream alone indexes object 4, and the section before it, unread, lies within 512
        // bytes of it, or 1,200 bytes more. It was taken for the malformed one moved: a shift, a /Size said wrong, and object 4
        // lost with no error (#281). The section is malformed, an error, and object 4 is found by rebuilding the index.
        var (file, _, second) = NearbyFiles.MalformedUpdateNearItsOriginal(streams, gap);

        var finding = Validate(file).Findings.Should().ContainSingle().Which;

        finding.RuleId.Should().Be(PdfValidationRuleIds.XRefSectionMalformed);
        finding.Location.Position.Should().Be(second);
    }

    [Fact]
    public void A_stream_whose_rows_cannot_be_read_is_malformed_and_the_older_table_its_prev_names_is_judged()
    {
        // The middle of three revisions is a stream of /Type /XRef whose /W gives a negative width, and the oldest table's row
        // for object 3 places it 2 bytes past its header. The chain stopped at the stream, and the oldest table was neither
        // read nor judged (#208). It goes on through the stream's /Prev: the stream is malformed, an error, and the row is
        // found shifted, as in the same file with the stream sound.
        var (sound, _, _, _) = LostStreamFiles.ThreeRevisions(LostStreamFiles.Unreadable.Replace("/W [1 -4 2]", "/W [1 4 2]", StringComparison.Ordinal), shift: 2);
        var (file, _, stream, _) = LostStreamFiles.ThreeRevisions(shift: 2);

        var whole = Validate(sound);
        var report = Validate(file);

        whole.Findings.Select(finding => finding.RuleId).Should().Equal(PdfValidationRuleIds.XRefEntryShifted);
        report.Findings.Select(finding => finding.RuleId).Should().Equal(PdfValidationRuleIds.XRefSectionMalformed, PdfValidationRuleIds.XRefEntryShifted);
        var malformed = report.Findings[0];
        malformed.Severity.Should().Be(PdfValidationSeverity.Error);
        malformed.Location.Position.Should().Be(stream);
        malformed.Message.Should().Be($"The cross-reference stream at offset {stream} cannot be read: its /W gives a field a negative width.");
        report.Findings[1].Message.Should().Be(whole.Findings[0].Message);
    }

    [Theory]
    [InlineData("{oldest}.0", null, "gives its /Prev as a real number, where Table 17 of ISO 32000-1 asks for an integer.")]
    [InlineData("9 0 R", "{oldest}", "writes its /Prev as the reference 9 0 R, where ISO 32000-1 makes it direct (7.5.8.2).")]
    [InlineData("99 0 R", null, null)]
    [InlineData("/X", null, null)]
    public void The_prev_a_stream_whose_rows_cannot_be_read_hands_on_is_judged_as_a_whole_read_judges_it(string prev, string? nine, string? wrong)
    {
        // The stream's /Prev is the one value of its dictionary the chain read: it is judged as the same value in a stream
        // whose rows can be read, a real or a reference reported as written, and one that names no section left to
        // xref.section-not-found (#208). The stream's other values are not judged.
        var oldest = LostStreamFiles.ThreeRevisions().Oldest.ToString(CultureInfo.InvariantCulture);
        var resolved = nine?.Replace("{oldest}", oldest, StringComparison.Ordinal);
        var (sound, _, _, _) = LostStreamFiles.ThreeRevisions(
            LostStreamFiles.Unreadable.Replace("/W [1 -4 2]", "/W [1 4 2]", StringComparison.Ordinal), prev: prev, nine: resolved);
        var (file, _, stream, _) = LostStreamFiles.ThreeRevisions(prev: prev, nine: resolved);

        var whole = Validate(sound);
        var report = Validate(file);

        Single(report, PdfValidationRuleIds.XRefSectionMalformed).Location.Position.Should().Be(stream);

        if (wrong is null)
        {
            report.Contains(PdfValidationRuleIds.FileTrailerValueWrong).Should().BeFalse();
            whole.Contains(PdfValidationRuleIds.FileTrailerValueWrong).Should().BeFalse();
            Single(report, PdfValidationRuleIds.XRefSectionNotFound).Message.Should().Be(Single(whole, PdfValidationRuleIds.XRefSectionNotFound).Message);
            return;
        }

        var finding = Single(report, PdfValidationRuleIds.FileTrailerValueWrong);
        finding.Location.Position.Should().Be(stream);
        finding.Message.Should().Be($"The cross-reference stream at offset {stream} {wrong}");
        finding.Message.Should().Be(Single(whole, PdfValidationRuleIds.FileTrailerValueWrong).Message);
    }

    [Fact]
    public void A_prev_a_guard_cut_that_a_lost_stream_hands_on_leaves_the_older_sections_unchecked()
    {
        // The stream's /Prev is the reference 9 0 R, and MaxObjectLength cuts object 9: the chain ends there for the guard,
        // and the older sections are said unchecked, as for a stream whose rows can be read (#208).
        var oldest = LostStreamFiles.ThreeRevisions().Oldest;
        var (file, _, stream, _) = LostStreamFiles.ThreeRevisions(
            prev: "9 0 R", nine: "%" + new string('-', 2000) + "\n" + oldest.ToString(CultureInfo.InvariantCulture));

        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = 1024 } });
        var report = new PdfValidator().Validate(document);

        document.Reader.Structure.Sections.Should().HaveCount(2);
        var finding = Single(report, PdfValidationRuleIds.XRefCheckedInPart);
        finding.Location.Position.Should().Be(stream);
        finding.Message.Should().Be(
            $"The cross-reference section at offset {stream} names older sections, but its /Prev is the reference 9 0 R, which one of the reader's limits cut: they were not checked.");
        Single(report, PdfValidationRuleIds.XRefSectionMalformed).Location.Position.Should().Be(stream);
    }

    [Theory]
    [InlineData("<< /Type /Font /Subtype /CIDFontType2 /W [0 [500]] /BaseFont /F >>")]
    [InlineData("<< /Type /Font /W [1 -4 2] /Size 8 /Size 8 /Length 3 >>\nstream\nabc\nendstream")]
    public void An_object_of_another_type_the_prev_names_with_no_section_near_is_not_found(string six)
    {
        // /Prev names object 6, a font that gives a /W, and nothing near it reads as a section. It was said to be a
        // cross-reference stream that cannot be read, and the stream's dictionary, given a key twice, a trailer malformed: it
        // says it is a font, and holds no section (#281).
        var (file, at) = NearbyFiles.TableNamingAnObject(six);

        var finding = Validate(file).Findings.Should().ContainSingle().Which;

        finding.RuleId.Should().Be(PdfValidationRuleIds.XRefSectionNotFound);
        finding.Message.Should().Be(
            $"The cross-reference section /Prev names at offset {at} is not there, nor within 512 bytes of it: the offset holds object 6, which is not a cross-reference stream.");
    }

    [Theory]
    [InlineData("<< /Type /Font /Subtype /CIDFontType2 /W [0 [500]] /BaseFont /F >>")]
    [InlineData("<< /Type /Font /W [1 -4 2] /Size 8 /Length 3 >>\nstream\nabc\nendstream")]
    [InlineData("<< /Type /Font /W [1 -4 2] /Size 8 /Size 8 /Length 3 >>\nstream\nabc\nendstream")]
    public void An_object_of_another_type_startxref_names_is_where_startxref_is_wrong(string six)
    {
        // startxref names object 6, a font that gives a /W. The font was called a cross-reference stream that cannot be read,
        // and its dictionary, given a key twice, a trailer malformed, where the same object a /Prev names holds no section: it
        // says it is a font, and startxref names no section (#281).
        var (file, at) = NearbyFiles.StartxrefNamingAnObject(six);
        var keyword = PdfTemplate.OffsetOf(file, "startxref");

        var finding = Validate(file).Findings.Should().ContainSingle(finding => finding.RuleId.StartsWith("xref.", StringComparison.Ordinal) || finding.RuleId.StartsWith("file.", StringComparison.Ordinal)).Which;

        finding.RuleId.Should().Be(PdfValidationRuleIds.FileStartXRefWrong);
        finding.Location.Position.Should().Be(keyword);
        finding.Message.Should().Be($"startxref gives offset {at}, which holds object 6, which is not a cross-reference stream.");
    }

    [Theory]
    [InlineData(false, 0, 1200)]
    [InlineData(false, 0, 0)]
    [InlineData(false, 1200, 1200)]
    [InlineData(true, 0, 1200)]
    [InlineData(true, 0, 0)]
    [InlineData(true, 1200, 1200)]
    public void A_section_that_cannot_be_read_found_nearest_to_where_its_offset_names_none_is_shifted_and_malformed(bool streams, int gap, int after)
    {
        // The third section names the second, which alone indexes object 4 and cannot be read, 7 bytes short of it, where no
        // section starts. The first, within 512 bytes of it, was taken in its place: a shift to the first and a /Size said
        // wrong; or, with nothing else near, the second was not found. It is the section found moved, which cannot be read: a
        // shift and an error, where it is (#281).
        var (file, _, second) = NearbyFiles.MalformedUpdateNearItsOriginal(streams, gap, shortBy: 7, after: after);

        var report = Validate(file);

        report.Findings.Select(finding => finding.RuleId).Should().BeEquivalentTo([PdfValidationRuleIds.XRefSectionMalformed, PdfValidationRuleIds.XRefSectionShifted]);
        Single(report, PdfValidationRuleIds.XRefSectionMalformed).Location.Position.Should().Be(second);
        Single(report, PdfValidationRuleIds.XRefSectionShifted).Message.Should().Be(
            $"The cross-reference section /Prev names at offset {second - 7} starts 7 bytes after it, at offset {second}.");
    }

    [Theory]
    [InlineData("a /Prev", "%xref\n")]
    [InlineData("a /Prev", "% the xref section follows\n")]
    [InlineData("a /Prev", "6 0 obj\n(see xref)\nendobj\n")]
    [InlineData("an /XRefStm", "%xref\n")]
    public void The_word_xref_in_a_comment_or_a_string_near_where_an_offset_names_no_section_is_no_malformed_section(string link, string between)
    {
        // A /Prev, or a hybrid table's /XRefStm, names a place inside an endobj, past which a comment or a string holds the
        // word xref, nearer than the section it means. The word was taken for a table found moved that cannot be read: an
        // error where a comment is, and, for the /XRefStm, a second, the stream said to be named nowhere. The section is
        // found where it lies, and shifted only (#156, #281).
        var file = link == "a /Prev" ? NearbyFiles.UpdateBehindAStrayWord(between).File : NearbyFiles.HybridBehindAStrayWord(between).File;

        var report = Validate(file);

        report.Findings.Select(finding => finding.RuleId).Should().Contain(PdfValidationRuleIds.XRefSectionShifted)
            .And.NotContain([PdfValidationRuleIds.XRefSectionMalformed, PdfValidationRuleIds.XRefHybridStreamWrong]);
    }

    [Theory]
    [InlineData("a comment")]
    [InlineData("a string")]
    public void The_word_xref_on_a_line_that_starts_before_the_bytes_searched_near_an_offset_is_no_malformed_section(string holder)
    {
        // A /Prev names a place inside an endobj, the line before which, 700 letters long, starts before the bytes searched
        // near it, and holds the word xref in a comment or a string, nearer than the section it means. Lexed from where the
        // bytes searched start, the word was taken for a table found moved that cannot be read: an error where a comment
        // is. The line is lexed from its start, and the section found where it lies, shifted only (#281).
        var line = holder == "a comment"
            ? "%" + new string('a', 700) + " xref see the table below\n"
            : "9 0 obj\n(" + new string('a', 700) + " xref see the table below)\nendobj\n";

        var report = Validate(NearbyFiles.UpdateAfterAStrayLine(line).File);

        report.Findings.Select(finding => finding.RuleId).Should().Contain(PdfValidationRuleIds.XRefSectionShifted)
            .And.NotContain(PdfValidationRuleIds.XRefSectionMalformed);
    }

    [Theory]
    [InlineData("a /Prev", "% 8 0 obj << /Type /XRef >>\n")]
    [InlineData("a /Prev", "10 0 obj\n(see 8 0 obj << /Type /XRef >> here)\nendobj\n")]
    [InlineData("a /Prev", "x8 0 obj << /Type /XRef >>\n")]
    [InlineData("an /XRefStm", "% 8 0 obj << /Type /XRef >>\n")]
    public void A_stream_header_in_a_comment_or_a_string_near_where_an_offset_names_no_section_is_no_malformed_section(string link, string between)
    {
        // A /Prev, or a hybrid table's /XRefStm, names a place inside an endobj, past which a comment or a string holds the
        // header of an object of /Type /XRef, or such a header a letter runs into, nearer than the section it means. The
        // header was taken for a stream found moved that cannot be read: an error where a comment is. The section is found
        // where it lies, and shifted only (#156, #281).
        var file = link == "a /Prev" ? NearbyFiles.UpdateBehindAStrayWord(between).File : NearbyFiles.HybridBehindAStrayWord(between).File;

        var report = Validate(file);

        report.Findings.Select(finding => finding.RuleId).Should().Contain(PdfValidationRuleIds.XRefSectionShifted)
            .And.NotContain([PdfValidationRuleIds.XRefSectionMalformed, PdfValidationRuleIds.XRefHybridStreamWrong]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_xrefstm_naming_a_table_that_cannot_be_read_is_wrong_at_each_naming_beside_the_table_s_fault(bool twice)
    {
        // The update's /XRefStm, and the first table's too, name a classic table whose row for object 10 is no row. Only the
        // table was malformed, an error whose remedy would leave the /XRefStm naming a table (#156): each naming is wrong.
        var file = HybridFiles.NamingATableNeverRead(firstNamesIt: twice, ten: "10 1\n(garbage)\n");
        var first = PdfTemplate.OffsetOf(file, "\nxref\n") + 1;
        var table = PdfTemplate.OffsetOf(file, "\nxref\n", 2) + 1;
        var update = PdfTemplate.OffsetOf(file, "\nxref\n", 3) + 1;

        var report = Validate(file);

        Single(report, PdfValidationRuleIds.XRefSectionMalformed).Location.Position.Should().Be(table);
        var wrong = report.Findings.Where(finding => finding.RuleId == PdfValidationRuleIds.XRefHybridStreamWrong).ToList();
        wrong.Select(finding => finding.Location.Position).Should().Equal(twice ? [update, first] : [update]);
        wrong[0].Message.Should().Be(
            $"The /XRefStm of the cross-reference table at offset {update} names offset {table}, a cross-reference table, which cannot be read, where ISO 32000-1 names the cross-reference stream of the table's revision (Table 19): what only that stream indexes is lost.");

        if (twice)
        {
            wrong[1].Message.Should().StartWith(
                $"The /XRefStm of the cross-reference table at offset {first} names offset {table}, the cross-reference table at offset {table}, which cannot be read, where ");
        }
    }

    [Theory]
    [InlineData(true, "{spaces}10 1\n{row:10}\n", "a cross-reference table, which one of the reader's limits cut before its first row")]
    [InlineData(false, "{spaces}10 1\n{row:10}\n", "a cross-reference table, whose rows were read in the stream's place")]
    [InlineData(true, "0 1\n0000000000 65535 f \n{spaces}10 1\n{row:10}\n", "a cross-reference table, whose rows were read in the stream's place")]
    [InlineData(false, "0 0\n", "a cross-reference table, whose rows were read in the stream's place")]
    [InlineData(true, "10 1\njunk junk junk\n{spaces}", "a cross-reference table, whose rows were read in the stream's place")]
    [InlineData(true, "{spaces}10 1\n{row:10}\njunk\n", "a cross-reference table, which one of the reader's limits cut before its first row")]
    public void An_xrefstm_naming_a_table_a_guard_cuts_before_its_first_row_is_wrong_in_words_that_say_none_of_its_rows_was_read(
        bool guarded, string ten, string what)
    {
        // The update's /XRefStm names a classic table whose row for object 10 lies past 300 spaces, which
        // MaxXRefSectionLength = 150 cuts before it. The /XRefStm names a table where a stream belongs, an error whatever the
        // guard hid; the finding said the table's rows were read in the stream's place, as read whole, though none was (#156).
        // A table whose first row, object 0's, the guard lets be read, and one read whole that holds no row, keep the words a
        // whole read gives; so does one whose first row is no row, which the reading reached before the guard's cut, the
        // table's own fault reported beside it. A stray word past the rows the guard left unread changes nothing.
        var file = HybridFiles.NamingATableNeverRead(ten: ten.Replace("{spaces}", new string(' ', 300), StringComparison.Ordinal));
        var table = PdfTemplate.OffsetOf(file, "\nxref\n", 2) + 1;
        var update = PdfTemplate.OffsetOf(file, "\nxref\n", 3) + 1;
        using var document = guarded
            ? PdfDocument.Open(file, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxXRefSectionLength = 150 } })
            : PdfDocument.Open(file);

        var report = new PdfValidator().Validate(document);

        var wrong = Single(report, PdfValidationRuleIds.XRefHybridStreamWrong);
        wrong.Severity.Should().Be(PdfValidationSeverity.Error);
        wrong.Location.Position.Should().Be(update);
        wrong.Message.Should().Be(
            $"The /XRefStm of the cross-reference table at offset {update} names offset {table}, {what}, where ISO 32000-1 names the cross-reference stream of the table's revision (Table 19): what only that stream indexes is lost.");
        report.Contains(PdfValidationRuleIds.XRefCheckedInPart).Should().Be(guarded);
    }

    [Theory]
    [InlineData(0, 1, "what its /Prev names too, past the guard that ended the chain there")]
    [InlineData(2, 1, "what its /Prev names too, past the guard that ended the chain there")]
    [InlineData(0, 2, "the section its /Prev names too, which the chain read through that /Prev")]
    public void An_xrefstm_naming_what_its_table_s_prev_names_where_the_section_count_guard_ends_the_chain_is_wrong_as_the_guard_hides_it(
        int xrefStmShortBy, int maxSections, string what)
    {
        // The newest table names the middle one through /Prev and /XRefStm, at one offset or at the white space before it, and
        // MaxXRefSectionCount ends the chain right at that /Prev, or one section later. Reported before the /Prev was read, the
        // naming said the chain looks for what the /Prev names, and the finding said so too, though the guard kept the chain
        // from looking: it says the guard hides it, beside the guard's own finding (#156, #264).
        var (file, _, middle, newest) = NearbyFiles.ChainNamingTheSpacesBeforeTheMiddle(0, newestXRefStm: xrefStmShortBy);
        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxXRefSectionCount = maxSections } });

        var report = new PdfValidator().Validate(document);

        Single(report, PdfValidationRuleIds.XRefHybridStreamWrong).Message.Should().Be(string.Create(
            CultureInfo.InvariantCulture,
            $"The /XRefStm of the cross-reference table at offset {newest} names offset {middle - xrefStmShortBy}, {what}, where ISO 32000-1 names the cross-reference stream of the table's revision (Table 19): what only that stream indexes is lost."));
        Single(report, PdfValidationRuleIds.XRefCheckedInPart);
        document.Reader.Structure.Sections[1].NamesWhatPreviousReads.Should().Be(maxSections > 1);
    }

    [Fact]
    public void An_xrefstm_naming_what_its_table_s_prev_names_outside_the_file_where_the_section_count_guard_ends_the_chain_says_so()
    {
        // The table names one offset past the file's end through /Prev and /XRefStm, and MaxXRefSectionCount ends the chain at
        // that /Prev: what lies outside the file holds no section whatever the guard hid, which the finding says, as read
        // whole (#156).
        var (file, section) = StoppedSectionFiles.Stopped("/Prev 99999999 /XRefStm 99999999");
        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxXRefSectionCount = 1 } });

        Single(new PdfValidator().Validate(document), PdfValidationRuleIds.XRefHybridStreamWrong).Message.Should().Be(string.Create(
            CultureInfo.InvariantCulture,
            $"The /XRefStm of the cross-reference table at offset {section} names offset 99999999, outside the file, which its /Prev names too, where ISO 32000-1 names the cross-reference stream of the table's revision (Table 19): what only that stream indexes is lost."));
    }

    [Theory]
    [InlineData("/Prev 99999999 /XRefStm 99999999", null, "outside the file, which its /Prev names too")]
    [InlineData("/Prev 99999999 /XRefStm 99999999", "table rows", "outside the file, which its /Prev names too")]
    [InlineData("/Prev {hole} /XRefStm {hole}", null, "what its /Prev names too, which holds no section the chain could read")]
    [InlineData("/Prev {hole} /XRefStm {hole}", "table rows", "the section its /Prev names too, past the guard that stopped the table")]
    [InlineData("/Prev {older} /XRefStm {older}", null, "the section its /Prev names too, which the chain read through that /Prev")]
    [InlineData("/Prev {shifted} /XRefStm {shifted}", null, "the section its /Prev names too, which the chain read through that /Prev")]
    public void An_xrefstm_naming_what_its_table_s_prev_names_is_wrong_in_words_that_say_what_the_prev_found_there(
        string links, string? guard, string what)
    {
        // The table names through /Prev and /XRefStm one offset: outside the file; inside a string of 1,500 bytes, where no
        // section lies within 512 bytes; the older table; or the end of the page's object, 7 bytes before the older table,
        // where the /Prev finds it moved. The finding said the chain reads a section there through that /Prev, whatever it
        // found (#156): it says what the /Prev read once the chain was read, or, past a guard that stopped the table, that
        // the guard hides it (#264).
        var twentyOne = "(" + new string('x', 1500) + ")";
        var (built, _) = StoppedSectionFiles.Stopped(links.Replace("{hole}", "0000000000", StringComparison.Ordinal).Replace("{shifted}", "0000000000", StringComparison.Ordinal), twentyOne: twentyOne);
        var hole = PdfTemplate.OffsetOf(built, "(xxx") + 750;
        var shifted = PdfTemplate.OffsetOf(built, "xref\n") - 7;
        var (file, section) = StoppedSectionFiles.Stopped(
            links
                .Replace("{hole}", hole.ToString("D10", CultureInfo.InvariantCulture), StringComparison.Ordinal)
                .Replace("{shifted}", shifted.ToString("D10", CultureInfo.InvariantCulture), StringComparison.Ordinal),
            twentyOne: twentyOne);
        var named = links.Contains("{hole}", StringComparison.Ordinal) ? hole
            : links.Contains("{shifted}", StringComparison.Ordinal) ? shifted
            : links.Contains("{older}", StringComparison.Ordinal) ? PdfTemplate.OffsetOf(file, "xref\n")
            : 99999999;
        using var document = guard is null ? PdfDocument.Open(file) : PdfDocument.Open(file, StoppedSectionFiles.Guard(guard));

        var finding = Single(new PdfValidator().Validate(document), PdfValidationRuleIds.XRefHybridStreamWrong);

        finding.Location.Position.Should().Be(section);
        finding.Message.Should().Be(
            $"The /XRefStm of the cross-reference table at offset {section} names offset {named}, {what}, where ISO 32000-1 names the cross-reference stream of the table's revision (Table 19): what only that stream indexes is lost.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void An_xrefstm_naming_what_its_table_s_prev_names_where_a_limit_cut_it_is_wrong_in_words_that_say_the_limit_hides_it(int shortBy)
    {
        // The newest table names object 4, a font whose dictionary holds 70,000 bytes before its /Type, through /Prev, and
        // through /XRefStm at its header too, or at the space before it. The /XRefStm names what the /Prev names, wrong
        // whatever MaxTrailerLength hides (#156): the finding said that what the /Prev names holds no section the chain could
        // read, of a dictionary the limit cut before it said what it is. It says the limit hides it (#264).
        var (built, _, four, _, newest) = HybridFiles.NamingAnObjectBesideTheStream(
            "<< /Pad (" + new string('-', 70_000) + ") /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
        var named = PdfTemplate.OffsetOf(built, "8 0 obj\n") + 20;
        var file = Encoding.Latin1.GetBytes(Encoding.Latin1.GetString(built).Replace(
            string.Create(CultureInfo.InvariantCulture, $"/XRefStm {named} >>"),
            string.Create(CultureInfo.InvariantCulture, $"/XRefStm {four - shortBy} >>"),
            StringComparison.Ordinal));
        using var document = PdfDocument.Open(file);

        Single(new PdfValidator().Validate(document), PdfValidationRuleIds.XRefHybridStreamWrong).Message.Should().Be(string.Create(
            CultureInfo.InvariantCulture,
            $"The /XRefStm of the cross-reference table at offset {newest} names offset {four - shortBy}, what its /Prev names too, past the limit that cut it there, where ISO 32000-1 names the cross-reference stream of the table's revision (Table 19): what only that stream indexes is lost."));
    }

    [Fact]
    public void A_prev_naming_a_place_inside_the_stream_its_hybrid_table_names_is_not_found()
    {
        // The hybrid table's /Prev names a place 30 bytes into the stream its /XRefStm names: the stream was taken for the
        // section /Prev names, and the catalog only the first table indexes was said to be missing from the file (#188).
        var (file, _, named) = NearbyFiles.HybridNamingInsideItsStream();

        var finding = Validate(file).Findings.Should().ContainSingle().Which;

        finding.RuleId.Should().Be(PdfValidationRuleIds.XRefSectionNotFound);
        finding.Location.Position.Should().Be(named);
    }

    [Fact]
    public void A_section_found_moved_and_then_named_where_it_lies_is_one_loop()
    {
        // The update names a place among the first table's rows, and the first table, found 40 bytes before it, names itself:
        // a shift and a loop, the first table judged once (#188).
        var (file, original, _) = NearbyFiles.UpdateNamingAroundItsOriginal(40, 0);

        var report = Validate(file);

        report.Findings.Select(finding => finding.RuleId).Should().Equal(PdfValidationRuleIds.XRefSectionShifted, PdfValidationRuleIds.XRefChainLoop);
        Single(report, PdfValidationRuleIds.XRefChainLoop).Message.Should().Be(
            $"The /Prev of the cross-reference section at offset {original} names offset {original}, a section the chain has already read: the chain loops.");
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

    [Theory]
    [InlineData("/Prev 21 0 R", "table rows", PdfValidationRuleIds.XRefSectionNotFound)]
    [InlineData("/Prev 21 0 R", "table index", PdfValidationRuleIds.XRefSectionNotFound)]
    [InlineData("/Prev 21 0 R", "stream rows", PdfValidationRuleIds.XRefSectionNotFound)]
    [InlineData("/Prev 21 0 R", "stream index", PdfValidationRuleIds.XRefSectionNotFound)]
    [InlineData("/Prev 99999999", "table rows", PdfValidationRuleIds.XRefSectionNotFound)]
    [InlineData("/Prev 99999999", "table index", PdfValidationRuleIds.XRefSectionNotFound)]
    [InlineData("/Prev 99999999", "stream rows", PdfValidationRuleIds.XRefSectionNotFound)]
    [InlineData("/Prev 99999999", "stream index", PdfValidationRuleIds.XRefSectionNotFound)]
    [InlineData("/Prev {self}", "table rows", PdfValidationRuleIds.XRefChainLoop)]
    [InlineData("/Prev {self}", "table index", PdfValidationRuleIds.XRefChainLoop)]
    [InlineData("/Prev {self}", "stream rows", PdfValidationRuleIds.XRefChainLoop)]
    [InlineData("/Prev {self}", "stream index", PdfValidationRuleIds.XRefChainLoop)]
    public void A_prev_fault_a_section_a_guard_stops_shows_is_found_as_a_whole_read_finds_it(string prev, string guard, string rule)
    {
        // #264: the section a guard stops — its rows cut by MaxXRefSectionLength or MaxDecodedStreamLength, or filling
        // MaxIndexEntries — names through /Prev a free row the guard let the reader read, an offset outside the file, or
        // itself. The validation finds it as it does in the file read whole, beside the guard's xref.checked-in-part; it
        // found only the guard's, and file.trailer-value-wrong for a /Prev written as a reference, which names no section.
        var (file, _) = StoppedSectionFiles.Stopped(prev, stream: guard.StartsWith("stream", StringComparison.Ordinal));

        var whole = Validate(file);
        using var document = PdfDocument.Open(file, StoppedSectionFiles.Guard(guard));
        var guarded = new PdfValidator().Validate(document);

        Single(guarded, rule).Should().Be(Single(whole, rule));
        guarded.Contains(PdfValidationRuleIds.XRefCheckedInPart).Should().BeTrue();
        guarded.Findings.Where(finding => finding.RuleId != PdfValidationRuleIds.XRefCheckedInPart).Should().Equal(whole.Findings);
    }

    [Theory]
    [InlineData("99999999", 0, "stream rows", PdfValidationRuleIds.XRefSectionNotFound)]
    [InlineData("99999999", 0, "stream index", PdfValidationRuleIds.XRefSectionNotFound)]
    [InlineData("{self}", 0, "stream rows", PdfValidationRuleIds.XRefChainLoop)]
    [InlineData("{self}", 0, "stream index", PdfValidationRuleIds.XRefChainLoop)]
    [InlineData("{stream}", 0, "stream rows", PdfValidationRuleIds.XRefHybridStreamWrong)]
    [InlineData("{stream}", 0, "stream index", PdfValidationRuleIds.XRefHybridStreamWrong)]
    [InlineData("{stream}", -4, "stream rows", PdfValidationRuleIds.XRefHybridStreamWrong)]
    [InlineData("{stream}", -4, "stream index", PdfValidationRuleIds.XRefHybridStreamWrong)]
    public void A_prev_fault_of_a_table_whose_hybrid_stream_a_guard_stops_is_found_as_a_whole_read_finds_it(string prev, int shift, string guard, string rule)
    {
        // #264, with #156: a table read whole names, through /XRefStm, the cross-reference stream a guard stops, and through
        // /Prev an offset outside the file, itself, or the stream — where /XRefStm names it, or a few bytes before it, where
        // the search near the /XRefStm's offset would find it: either way the /Prev reads it, and the /XRefStm names no stream
        // of its revision. The validation finds what it finds in the file read whole, beside the guard's
        // xref.checked-in-part.
        var (file, _) = StoppedSectionFiles.HybridWhoseStreamIsStopped(prev, shift);

        var whole = Validate(file);
        using var document = PdfDocument.Open(file, StoppedSectionFiles.Guard(guard));
        var guarded = new PdfValidator().Validate(document);

        Single(guarded, rule).Should().Be(Single(whole, rule));
        guarded.Findings.Where(finding => finding.RuleId != PdfValidationRuleIds.XRefCheckedInPart).Should().Equal(whole.Findings);
    }

    [Theory]
    [InlineData("/Prev {older} /XRefStm 99999999", "table rows", PdfValidationRuleIds.XRefSectionNotFound)]
    [InlineData("/Prev {older} /XRefStm 99999999", "table index", PdfValidationRuleIds.XRefSectionNotFound)]
    [InlineData("/Prev {older} /XRefStm {self}", "table rows", PdfValidationRuleIds.XRefHybridStreamWrong)]
    [InlineData("/Prev {older} /XRefStm {self}", "table index", PdfValidationRuleIds.XRefHybridStreamWrong)]
    [InlineData("/Prev {older} /XRefStm {newer}", "table rows", PdfValidationRuleIds.XRefHybridStreamWrong)]
    [InlineData("/Prev {older} /XRefStm {newer}", "table index", PdfValidationRuleIds.XRefHybridStreamWrong)]
    [InlineData("/Prev 99999999 /XRefStm 99999999", "table rows", PdfValidationRuleIds.XRefHybridStreamWrong)]
    [InlineData("/Prev 99999999 /XRefStm 99999999", "table index", PdfValidationRuleIds.XRefHybridStreamWrong)]
    public void An_xrefstm_fault_a_table_a_guard_stops_shows_is_found_as_a_whole_read_finds_it(string links, string guard, string rule)
    {
        // #264, with #156: the table a guard stops, under a newer one, names through /XRefStm an offset outside the file, the
        // table itself, the newer table, or what its /Prev names too, outside the file: no cross-reference stream of its
        // revision, whatever the guard hid. The validation finds it as it does in the file read whole, beside the guard's
        // xref.checked-in-part; past the guard, it found nothing of the /XRefStm.
        var (file, _) = StoppedSectionFiles.Stopped(links, newer: "/Prev");

        var whole = Validate(file);
        using var document = PdfDocument.Open(file, StoppedSectionFiles.Guard(guard));
        var guarded = new PdfValidator().Validate(document);

        Single(guarded, rule).Should().Be(Single(whole, rule));
        guarded.Findings.Where(finding => finding.RuleId != PdfValidationRuleIds.XRefCheckedInPart).Should().Equal(whole.Findings);
    }

    [Theory]
    [InlineData("table rows")]
    [InlineData("table index")]
    public void An_xrefstm_naming_what_the_prev_of_a_table_a_guard_stops_names_is_wrong_as_read_whole(string guard)
    {
        // #264, with #156: the table a guard stops names the older table, in the file, through /Prev and /XRefStm both. The
        // /XRefStm names no stream of the table's revision, whatever the guard hid: an error, at the table, as read whole,
        // which says the /Prev read that table, where past the guard it reads nothing.
        var (file, section) = StoppedSectionFiles.Stopped("/Prev {older} /XRefStm {older}");

        var whole = Single(Validate(file), PdfValidationRuleIds.XRefHybridStreamWrong);
        using var document = PdfDocument.Open(file, StoppedSectionFiles.Guard(guard));
        var guarded = Single(new PdfValidator().Validate(document), PdfValidationRuleIds.XRefHybridStreamWrong);

        guarded.Severity.Should().Be(PdfValidationSeverity.Error);
        guarded.Location.Should().Be(whole.Location);
        guarded.Location.Position.Should().Be(section);
        whole.Message.Should().Contain("the section its /Prev names too, which the chain read through that /Prev, where");
        guarded.Message.Should().Be(whole.Message.Replace("which the chain read through that /Prev", "past the guard that stopped the table", StringComparison.Ordinal));
        guarded.Message.Should().NotBe(whole.Message);
    }

    [Fact]
    public void A_loop_through_an_offset_the_chain_only_named_says_no_section_was_read_there()
    {
        // The newest table's /XRefStm names an offset in a comment, more than 512 bytes from anything, and the update's /Prev
        // names it again: the chain loops through an offset where it read no section, and says so. An /XRefStm that names
        // what its own table's /Prev names is not followed, the /Prev reading the section (#156): the loop takes two tables.
        static byte[] Build(string offset) => PdfTemplate.Build((Updated
            .Replace("4 0 obj\n", "%" + new string('-', 1200) + "\n4 0 obj\n", StringComparison.Ordinal)
            .Replace("/Prev {xref:1}", "/Prev {offset}", StringComparison.Ordinal) + """
            xref
            0 1
            {free}
            trailer
            << /Size 5 /Root 1 0 R /Prev {xref:2} /XRefStm {offset} >>
            startxref
            {xref:3}
            %%EOF

            """).Replace("{offset}", offset, StringComparison.Ordinal));
        var hole = PdfTemplate.OffsetOf(Build("0000000000"), "%---") + 600;
        var named = hole.ToString("D10", CultureInfo.InvariantCulture);
        var file = Build(named);
        var update = PdfTemplate.OffsetOf(file, "\nxref\n", 2) + 1;
        using var document = PdfDocument.Open(file);

        document.Reader.Structure.LoopTarget.Should().Be(XRefLoopTarget.Named);
        var cycle = document.Diagnostics.Should().ContainSingle(d => d.Code == PdfDiagnosticCodes.XRefChainCycle).Which;
        cycle.Position.Should().Be(hole);
        cycle.Message.Should().EndWith($"names offset {hole}, which the chain has already named.");
        Single(new PdfValidator().Validate(document), PdfValidationRuleIds.XRefChainLoop).Message.Should().Be(
            $"The /Prev of the cross-reference section at offset {update} names offset {hole}, which the chain has already named: the chain loops.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_chain_that_ends_in_a_loop_has_no_size_judged_the_section_it_lost_may_number_the_objects_of(bool atTheFirstByte)
    {
        // The original's table indexes objects 1 to 5; the update writes object 4 again, and its table, which gives /Size 6,
        // names through /Prev the newest table rather than the original's, which names the update: the chain loops, and the
        // original is never read. The update's /Size counts the original's objects, as it should: the chain is not judged,
        // the section the last /Prev should have named unknown, where both tables were said to give a /Size that 4, the
        // highest number read, makes 5 (#156). In a file with no header, the update starts the file, and its /Prev names
        // offset 0, itself: a loop at the file's first byte, judged as any other.
        byte[] bytes;
        long update;
        long loop;

        if (atTheFirstByte)
        {
            const string Head = "xref\n4 1\n{0:D10} 00000 n \ntrailer\n<< /Size 6 /Root 1 0 R /Prev 0 >>\n";
            var length = string.Format(CultureInfo.InvariantCulture, Head, 0).Length;
            var objects = new StringBuilder();
            var offsets = new long[6];

            foreach (var (number, body) in new[] { (1, "<< /Type /Catalog /Pages 2 0 R >>"), (2, "<< /Type /Pages /Kids [] /Count 0 >>"), (3, "(three)"), (4, "(four)"), (5, "(five)") })
            {
                offsets[number] = length + objects.Length;
                objects.Append(CultureInfo.InvariantCulture, $"{number} 0 obj\n{body}\nendobj\n");
            }

            objects.Append("xref\n0 6\n0000000000 65535 f \n");

            for (var number = 1; number < 6; number++)
            {
                objects.Append(CultureInfo.InvariantCulture, $"{offsets[number]:D10} 00000 n \n");
            }

            objects.Append("trailer\n<< /Size 6 /Root 1 0 R >>\n");
            bytes = Encoding.Latin1.GetBytes(string.Format(CultureInfo.InvariantCulture, Head, offsets[4]) + objects + "startxref\n0\n%%EOF\n");
            update = 0;
            loop = 0;
        }
        else
        {
            var file = new HandWrittenFile();
            var rows = new Dictionary<int, long>
            {
                [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
                [2] = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"),
                [3] = file.Object(3, "(three)"),
                [4] = file.Object(4, "(old four)"),
                [5] = file.Object(5, "(five)"),
            };
            file.Table(6, rows, "/Size 6 /Root 1 0 R");
            var four = file.Object(4, "(new four)");
            update = file.Position;
            string Update(long newest) => string.Create(
                CultureInfo.InvariantCulture, $"xref\n4 1\n{four:D10} 00000 n \ntrailer\n<< /Size 6 /Root 1 0 R /Prev {newest:D10} >>\n");
            loop = update + Update(0).Length;
            file.Raw(Update(loop));
            file.Raw(string.Create(
                CultureInfo.InvariantCulture, $"xref\n0 1\n0000000000 65535 f \ntrailer\n<< /Size 6 /Root 1 0 R /Prev {update} >>\nstartxref\n{loop}\n%%EOF\n"));
            bytes = file.Bytes;
        }

        using var document = PdfDocument.Open(bytes);

        var report = new PdfValidator().Validate(document);

        document.Reader.Structure.LoopOffset.Should().Be(loop);
        Single(report, PdfValidationRuleIds.XRefChainLoop).Location.Position.Should().Be(update);
        report.Contains(PdfValidationRuleIds.FileSizeWrong).Should().BeFalse(string.Join("; ", report.Findings));
    }

    [Fact]
    public void A_section_s_trailer_finding_points_at_the_section_until_a_trailer_is_found()
    {
        // Every rule that locates a finding at a section's trailer judges one the reader found; the record, given what no
        // file produces, points at the section itself.
        var section = new XRefSectionRecord("/Prev", 120, namedFrom: 40);

        section.TrailerLocation.Should().Be(120);
        section.TrailerPosition = 180;
        section.TrailerLocation.Should().Be(180);
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
        structure.LoopTarget = XRefLoopTarget.Read;
        structure.LoopNamedBy = "/Prev";
        var context = new ValidationContext(document, capacity: 16);

        new ChainLoopRule().Check(context);

        var finding = context.ToReport(ValidationProfile.Structural).Findings.Should().ContainSingle().Which;
        finding.Location.IsDocument.Should().BeTrue();
        finding.Message.Should().Be("A /Prev names offset 9, a section the chain has already read: the chain loops.");
    }

    [Theory]
    [InlineData("itself", "the table itself")]
    [InlineData("the table its /Prev reads", "the cross-reference table at offset {named}, which the chain read through /Prev")]
    [InlineData("the newest table", "the cross-reference table at offset {named}, which the chain read through startxref")]
    [InlineData("a table no /Prev names", "a cross-reference table, whose rows were read in the stream's place")]
    [InlineData("what its own /Prev names", "the section its /Prev names too, which the chain read through that /Prev")]
    public void An_xrefstm_that_names_no_cross_reference_stream_of_its_revision_is_an_error(string shape, string what)
    {
        // ISO 32000-1 has a hybrid table's /XRefStm name the cross-reference stream of its revision (Table 19). One that names
        // a classic table, or a section the chain reads through /Prev or startxref, leaves that stream unknown, what only it
        // indexes found, if at all, by rebuilding the index: an error (ADR 45), which nothing reported (#156).
        var (file, naming, named) = shape switch
        {
            "itself" => Tables(HybridFiles.OldestNaming("{xref:1}"), 1, 1),
            "the table its /Prev reads" => Tables(HybridFiles.OldestNaming("{xref:2}"), 1, 2),
            "the newest table" => Tables(HybridFiles.OldestNaming("{xref:3}"), 1, 3),
            "a table no /Prev names" => Tables(HybridFiles.NamingATableNeverRead(), 3, 2),
            _ => Tables(HybridFiles.NamingThePreviousTable(), 3, 2),
        };

        var report = Validate(file);

        var finding = report.Findings.Should().ContainSingle().Which;
        finding.RuleId.Should().Be(PdfValidationRuleIds.XRefHybridStreamWrong);
        finding.Severity.Should().Be(PdfValidationSeverity.Error);
        finding.Location.Position.Should().Be(naming);
        finding.Message.Should().Be(string.Create(
            CultureInfo.InvariantCulture,
            $"The /XRefStm of the cross-reference table at offset {naming} names offset {named}, {what.Replace("{named}", named.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)}, where ISO 32000-1 names the cross-reference stream of the table's revision (Table 19): what only that stream indexes is lost."));
        finding.Remedy.Should().Be("Point /XRefStm at the cross-reference stream of the table's revision, or remove it when the table indexes every object of its revision.");

        static (byte[] File, long Naming, long Named) Tables(byte[] file, int naming, int named) =>
            (file, PdfTemplate.OffsetOf(file, "\nxref\n", naming) + 1, PdfTemplate.OffsetOf(file, "\nxref\n", named) + 1);
    }

    [Fact]
    public void An_xrefstm_naming_the_stream_startxref_names_is_an_error()
    {
        // A stream, but one the chain read through startxref, of a newer revision than the table's (#156).
        var (file, table, stream) = HybridFiles.NamingANewerStream();

        var finding = Single(Validate(file), PdfValidationRuleIds.XRefHybridStreamWrong);

        finding.Location.Position.Should().Be(table);
        finding.Message.Should().Contain(string.Create(
            CultureInfo.InvariantCulture,
            $"names offset {stream}, the cross-reference stream at offset {stream}, which the chain read through startxref, where"));
    }

    [Fact]
    public void An_xrefstm_the_next_table_copies_names_no_wrong_stream()
    {
        // A writer that copies /XRefStm into the next trailer names one stream from two tables: each names a stream, and the
        // file breaks no rule (#156).
        Validate(HybridFiles.SharedStream(olderSize: 7).File).Findings.Should().BeEmpty();
        Validate(HybridFiles.SharedStreamNamedShort().File).Contains(PdfValidationRuleIds.XRefHybridStreamWrong).Should().BeFalse(
            "a copy naming the white space before the stream's header still names the stream, an imprecise offset only");
    }

    [Fact]
    public void An_entry_outside_the_file_is_broken()
    {
        var file = PdfTemplate.SoundWith("{row:3}", "9999999999 00000 n ");

        var finding = Single(Validate(file), PdfValidationRuleIds.XRefEntryBroken);

        finding.Severity.Should().Be(PdfValidationSeverity.Error);
        finding.Location.Object.Should().Be(new PdfObjectId(3));
        finding.Message.Should().Be("The entry of object 3 gives offset 9999999999, outside the file.");
        finding.Remedy.Should().Be("Correct the entry, or rewrite the file's cross-reference index.");
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
    [InlineData(400, 600, PdfValidationRuleIds.XRefEntryBroken)]
    [InlineData(1400, 600, PdfValidationRuleIds.XRefEntryBroken)]
    [InlineData(400, 500, PdfValidationRuleIds.XRefEntryShifted)]
    [InlineData(1400, 500, PdfValidationRuleIds.XRefEntryShifted)]
    public void An_entry_600_bytes_before_its_object_is_broken_and_one_500_bytes_before_it_shifted_however_near_the_file_s_start(
        long row, long distance, string ruleId)
    {
        // The page's entry gives an offset in the file's first 512 bytes, or past them: an object 600 bytes after it is not
        // found near it, an error, wherever the offset falls, and one 500 bytes after it shifted, a warning. A window clamped at
        // the file's start once found the first too, near the start alone (#197).
        var file = NearbyFiles.PageNamedAt(row, row + distance);

        var finding = Single(Validate(file), ruleId);

        finding.Location.Should().Be(PdfValidationLocation.OfObject(new PdfObjectId(3), row));
        finding.Message.Should().Be(ruleId == PdfValidationRuleIds.XRefEntryBroken
            ? $"The entry of object 3 gives offset {row}, where there is no object header, and the object is not within 512 bytes of it."
            : $"The entry of object 3 gives offset {row}, where there is no object header; the object starts {distance} bytes after it, at offset {row + distance}.");
        finding.Severity.Should().Be(ruleId == PdfValidationRuleIds.XRefEntryBroken ? PdfValidationSeverity.Error : PdfValidationSeverity.Warning);
    }

    [Theory]
    [InlineData(-500, 500, PdfValidationRuleIds.XRefEntryBroken, "The entry of object 3 gives offset -500, outside the file.")]
    [InlineData(-511, 600, PdfValidationRuleIds.XRefEntryBroken, "The entry of object 3 gives offset -511, outside the file.")]
    [InlineData(-1, 600, PdfValidationRuleIds.XRefEntryBroken, "The entry of object 3 gives offset -1, outside the file.")]
    [InlineData(
        -1,
        500,
        PdfValidationRuleIds.XRefEntryShifted,
        "The entry of object 3 gives offset -1, outside the file; the object starts 501 bytes after it, at offset 500.")]
    public void An_entry_before_the_file_s_start_is_shifted_only_where_its_object_s_header_ends_within_512_bytes_after_it(
        long row, long at, string ruleId, string message)
    {
        // The probe looks near an entry before the file's start as a load does (#175), within 512 bytes of the offset: an
        // entry of -1 reaches a header 500 bytes into the file, but not one 600 bytes in, and one of -500 or -511 neither,
        // where a window clamped at the file's start reached both (#197).
        var file = NearbyFiles.PageNamedAt(row, at);

        var finding = Single(Validate(file), ruleId);

        finding.Location.Should().Be(PdfValidationLocation.OfObject(new PdfObjectId(3)));
        finding.Message.Should().Be(message);
    }

    [Theory]
    [InlineData(4, 512, PdfValidationRuleIds.XRefEntryShifted)]
    [InlineData(4, 513, PdfValidationRuleIds.XRefEntryBroken)]
    [InlineData(14, 513, PdfValidationRuleIds.XRefEntryBroken)]
    public void An_entry_is_shifted_only_where_its_object_s_header_starts_within_512_bytes_before_it_whatever_number_the_window_s_start_cuts(
        int number, long before, string ruleId)
    {
        // Object 4's entry gives an offset 512 or 513 bytes past the header of object 4, or 513 bytes past object 14's, whose 4
        // the window searched near the entry starts on: the probe takes neither the second nor the third for object 4's header,
        // as a load does not, where it once judged the third shifted (#197).
        const long At = 805;
        var file = NearbyFiles.HeaderNear(number, At, At + before);

        var finding = Single(Validate(file), ruleId);

        finding.Location.Should().Be(PdfValidationLocation.OfObject(new PdfObjectId(4), At + before));
        finding.Message.Should().Be(ruleId == PdfValidationRuleIds.XRefEntryShifted
            ? $"The entry of object 4 gives offset {At + before}, where there is no object header; the object starts 512 bytes before it, at offset {At}."
            : $"The entry of object 4 gives offset {At + before}, where there is no object header, and the object is not within 512 bytes of it.");
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

    [Theory]
    [InlineData("4 % a comment\n0 obj")]
    [InlineData("4 0 % a comment\nobj")]
    [InlineData("4\r% one\r0 % two\robj")]
    public void An_entry_two_bytes_past_a_header_that_holds_comments_is_shifted_not_broken(string header)
    {
        // Object 4's header holds comments between its tokens, and its entry gives the offset two bytes past where it starts:
        // the probe reads the header back from its obj keyword as a load reads it, comments and all, where it once stopped at
        // the comment and judged the entry broken, an error (#261).
        var file = NearbyFiles.FourWrittenAs(header, shift: 2);
        var at = PdfTemplate.OffsetOf(file, header);

        var finding = Validate(file).Findings.Should().ContainSingle().Which;

        finding.RuleId.Should().Be(PdfValidationRuleIds.XRefEntryShifted);
        finding.Severity.Should().Be(PdfValidationSeverity.Warning);
        finding.Location.Should().Be(PdfValidationLocation.OfObject(new PdfObjectId(4), at + 2));
        finding.Message.Should().Be(
            $"The entry of object 4 gives offset {at + 2}, where there is no object header; the object starts 2 bytes before it, at offset {at}.");
    }

    [Fact]
    public void An_entry_naming_the_object_before_its_own_is_shifted_from_that_object_s_header()
    {
        // Object 2's row gives where object 1 starts, and object 2 follows it within reach: the finding names the header the
        // row lands on, however low its number.
        var file = PdfTemplate.SoundWith("{row:2}", "{row:1}");

        var finding = Single(Validate(file), PdfValidationRuleIds.XRefEntryShifted);

        var one = PdfTemplate.OffsetOf(file, "1 0 obj");
        var two = PdfTemplate.OffsetOf(file, "2 0 obj");
        finding.Location.Should().Be(PdfValidationLocation.OfObject(new PdfObjectId(2), one));
        finding.Message.Should().Be(
            $"The entry of object 2 gives offset {one}, where there is the header of object 1; the object starts {two - one} bytes after it, at offset {two}.");
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
    [InlineData("/Type /ObjStm", "/Kind /ObjStm", "Object 4, where the index places 2 objects, is a stream that is not of /Type /ObjStm.")]
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
    [InlineData("/N 2.0", false)]
    [InlineData("/N 5 0 R", false)]
    [InlineData("/DecodeParms 5 0 R", true)]
    public void A_stream_that_is_not_an_object_stream_is_judged_on_its_type_alone(string entries, bool compressed)
    {
        // Stream 4 has no /Type: it serves no object, to the reader as to the probe (#274). Its /N written as a real, or a
        // /N or /DecodeParms naming an object it would hold, is neither a value the reader read nor a need of its data: the
        // stream is judged on its type alone.
        var written = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(5, "2")
            .WithObject(6, "<< /Title (six) >>")
            .BuildWithXRefStream(rootNumber: 1, compressedObjects: [5, 6], compressObjectStream: compressed, objectStreamEntries: entries);
        var file = Replace(written, "/Type /ObjStm", "/Kind /ObjStm");

        var findings = Validate(file).Findings.Where(finding => finding.RuleId.StartsWith("xref.", StringComparison.Ordinal)).ToList();

        findings.Should().ContainSingle().Which.RuleId.Should().Be(PdfValidationRuleIds.XRefObjectStreamBroken);
        findings[0].Message.Should().EndWith("is a stream that is not of /Type /ObjStm.");

        // The reader reads nothing of it past its type either: no /N read, no object needed. Its dictionary repeats /N, the
        // builder's and the row's, which the parse reports.
        using var document = PdfDocument.Open(file);
        document.GetObject(new PdfObjectId(6)).Should().BeSameAs(PdfNull.Instance);
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Where(code => code != PdfDiagnosticCodes.SyntaxKeyRepeated)
            .Should().Equal(PdfDiagnosticCodes.ObjectStreamUnreadable);
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
    [InlineData("2 2147483647 ", "then object 2 at offset 2147483647, past what its {0} bytes of decoded data hold")]
    [InlineData("2147483647 0 ", null)]
    public void An_object_stream_whose_header_lists_something_other_than_objects_cannot_be_read(string start, string? then)
    {
        // The header starts "2 0 ", object 2 at offset 0; the objects keep their offsets from /First. A number no object
        // can have, or an offset no member can start at, ends the header as a stray token does, as the reader ends it; a
        // number past a long is a real, which is a stray token; the largest offset an int holds is a start past the data.
        var file = XRefStreamBuilder().BuildWithXRefStream(
            rootNumber: 1, compressedObjects: [2, 3], objectStreamHeader: header => start + header[4..]);

        var report = Validate(file);

        if (then is null)
        {
            // The largest number a member may have: the header is read, whatever else the entries then get wrong.
            report.Contains(PdfValidationRuleIds.XRefObjectStreamBroken).Should().BeFalse();
            return;
        }

        Single(report, PdfValidationRuleIds.XRefObjectStreamBroken).Message.Should().Be(
            "Object stream 4, where the index places 2 objects, cannot be read: its header lists 0 of the 2 objects its /N declares, " +
            string.Format(CultureInfo.InvariantCulture, then, start.Length + 5 + PackedLength) + ".");
    }

    [Theory]
    [InlineData("2 0 3 0 ", "then object 3 at offset 0, not past the offset of the object before it, 0")]
    [InlineData("2 42 3 0 ", "then object 3 at offset 0, not past the offset of the object before it, 42")]
    [InlineData("2 0 3 999 ", "then object 3 at offset 999, past what its {0} bytes of decoded data hold")]
    [InlineData("2 0 x ", "then something else")]
    public void An_object_stream_whose_header_breaks_after_an_object_says_which_objects_are_lost(string header, string then)
    {
        // The reader serves object 2, listed before the fault, and loses object 3 (#160, #202, #275): the stream's finding
        // says so, and 3's entry has none of its own.
        var file = XRefStreamBuilder().BuildWithXRefStream(rootNumber: 1, compressedObjects: [2, 3], objectStreamHeader: _ => header);

        var finding = Single(Validate(file), PdfValidationRuleIds.XRefObjectStreamBroken);
        Validate(file).Findings.Where(each => each.RuleId.StartsWith("xref.", StringComparison.Ordinal)).Select(each => each.RuleId)
            .Should().Equal(PdfValidationRuleIds.XRefObjectStreamBroken);

        finding.Severity.Should().Be(PdfValidationSeverity.Error);
        finding.Message.Should().Be(string.Create(
            CultureInfo.InvariantCulture,
            $"Object stream 4, where the index places 2 objects, can be read only in part: its header lists 1 of the 2 objects its /N declares, {string.Format(CultureInfo.InvariantCulture, then, header.Length + PackedLength)}; 1 of them is not listed before that, and lost."));
    }

    [Fact]
    public void An_object_stream_whose_first_member_starts_past_its_data_cannot_be_read()
    {
        // The only page of the tree is lost: a start no member can have, as an offset past an int is (#202).
        const string Header = "2 999 3 1000 ";
        var file = XRefStreamBuilder().BuildWithXRefStream(rootNumber: 1, compressedObjects: [2, 3], objectStreamHeader: _ => Header);

        var finding = Single(Validate(file), PdfValidationRuleIds.XRefObjectStreamBroken);

        finding.Message.Should().Be(string.Create(
            CultureInfo.InvariantCulture,
            $"Object stream 4, where the index places 2 objects, cannot be read: its header lists 0 of the 2 objects its /N declares, then object 2 at offset 999, past what its {Header.Length + PackedLength} bytes of decoded data hold."));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_object_stream_that_lists_every_object_its_index_places_before_its_header_breaks_loses_none_and_is_no_finding(bool stray)
    {
        // Its /N declares a third object, and its header ends, or lists something else, after the two the index places in
        // it: both are served, nothing the index places is lost, and the reader's own report says what the header lacks
        // (#275).
        var written = stray
            ? XRefStreamBuilder().BuildWithXRefStream(rootNumber: 1, compressedObjects: [2, 3], objectStreamHeader: header => header + "x ")
            : XRefStreamFile();
        var file = Replace(written, "/N 2", "/N 3");
        using var document = PdfDocument.Open(file);

        var report = new PdfValidator().Validate(document);

        report.Findings.Should().BeEmpty();
        document.Diagnostics.Should().ContainSingle().Which.Code.Should().Be(PdfDiagnosticCodes.ObjectStreamUnreadable);
    }

    [Fact]
    public void An_object_stream_that_loses_the_only_object_its_index_places_says_so_of_it()
    {
        // The index places object 3 alone in the stream, whose header lists object 2 and then something else (#275).
        var written = XRefStreamBuilder().BuildWithXRefStream(rootNumber: 1, compressedObjects: [3], objectStreamHeader: _ => "2 0 x ");
        var file = Replace(written, "/N 1", "/N 2");

        var finding = Single(Validate(file), PdfValidationRuleIds.XRefObjectStreamBroken);

        finding.Location.Object.Should().Be(new PdfObjectId(4));
        finding.Message.Should().Be("Object stream 4, where the index places 1 object, can be read only in part: its header lists 1 of the 2 objects its /N declares, then something else; it is not listed before that, and lost.");
    }

    [Theory]
    [InlineData("", "its header lists 0 of the 1 object its /N declares, then ends", "has a header that ends after 0 of the 1 object its /N declares")]
    [InlineData("x ", "its header lists 0 of the 1 object its /N declares, then something else", "has a header that holds something other than an object number and an offset after 0 of the 1 object its /N declares")]
    [InlineData("0 0 ", "its header lists 0 of the 1 object its /N declares, then object 0 at offset 0, which no member can be", "has a header that lists object 0 at offset 0, which no member can be, after 0 of the 1 object its /N declares")]
    public void An_object_stream_whose_n_declares_one_object_says_so_in_the_singular(string header, string fault, string unreadable)
    {
        // The index places object 3 alone in object stream 4, whose /N declares it alone, and whose header ends before it,
        // lists something else, or an object no member can be: the finding quotes the reader's fault, and the reader's own
        // report words it its way, each with one object in the singular (#241).
        var file = XRefStreamBuilder().BuildWithXRefStream(rootNumber: 1, compressedObjects: [3], objectStreamHeader: _ => header);
        using var document = PdfDocument.Open(file);

        var finding = Single(new PdfValidator().Validate(document), PdfValidationRuleIds.XRefObjectStreamBroken);

        finding.Message.Should().Be("Object stream 4, where the index places 1 object, cannot be read: " + fault + ".");
        document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.ObjectStreamUnreadable).Which.Message.Should().Be(
            "Object stream 4 " + unreadable + "; none of its objects can be read from it.");
    }

    [Fact]
    public void An_object_stream_whose_data_is_one_byte_says_so_in_the_singular()
    {
        // Object 3, empty, is packed alone with no header: the stream's data is the line feed after it, one byte, and its
        // /First, moved from 0 to 5, lies past it (#241).
        var written = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(3, string.Empty)
            .BuildWithXRefStream(rootNumber: 1, compressedObjects: [3], objectStreamHeader: _ => string.Empty);
        var file = Replace(written, "/N 1 /First 0", "/N 1 /First 5");
        using var document = PdfDocument.Open(file);

        var finding = Single(new PdfValidator().Validate(document), PdfValidationRuleIds.XRefObjectStreamBroken);
        document.GetObject(new PdfObjectId(3)).Should().BeSameAs(PdfNull.Instance);

        finding.Message.Should().Be("Object stream 4, where the index places 1 object, cannot be read: it decodes to 1 byte, fewer than its /First of 5.");
        document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.ObjectStreamUnreadable).Which.Message.Should().Be(
            "Object stream 4 gives in /First an offset, 5, past the end of its decoded data, which is 1 byte long; none of its objects can be read from it.");
    }

    [Fact]
    public void An_object_stream_broken_after_an_object_the_index_misplaces_shifts_that_object_and_loses_the_other()
    {
        // The header lists object 2 and then something else; the index places 2 at index 1, past the break, and 3 there too.
        // The reader serves 2 where the header lists it, and loses 3: 2's entry is shifted, and the stream loses one object.
        var written = XRefStreamBuilder().BuildWithXRefStream(rootNumber: 1, compressedObjects: [2, 3], objectStreamHeader: _ => "2 0 x ");
        var file = Replace(written, "\u0002\0\0\0\u0004\0\0", "\u0002\0\0\0\u0004\0\u0001");

        var findings = Validate(file).Findings.Where(finding => finding.RuleId.StartsWith("xref.", StringComparison.Ordinal)).ToList();

        findings.Select(finding => (finding.RuleId, finding.Location.Object)).Should().BeEquivalentTo(
            [(PdfValidationRuleIds.XRefEntryShifted, new PdfObjectId(2)), (PdfValidationRuleIds.XRefObjectStreamBroken, new PdfObjectId(4))]);
        findings.Single(finding => finding.RuleId == PdfValidationRuleIds.XRefObjectStreamBroken).Message.Should().EndWith("; 1 of them is not listed before that, and lost.");
    }

    [Fact]
    public void An_object_written_as_a_stream_before_a_header_breaks_is_its_entry_s_finding_and_not_counted_lost()
    {
        // The header lists object 5, written as a stream, then something else; the index places 6 and 7 after it. 5's entry
        // is broken, and the stream loses 6 and 7, not 5, which its header lists.
        var written = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R /Foo 5 0 R /Bar 6 0 R /Baz 7 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(5, "<< /Length 5 >>stream\nhello\nendstream")
            .WithObject(6, "(six)")
            .WithObject(7, "(seven)")
            .BuildWithXRefStream(rootNumber: 1, compressedObjects: [5, 6, 7], objectStreamHeader: _ => "5 0 x ");

        var findings = Validate(written).Findings.Where(finding => finding.RuleId.StartsWith("xref.", StringComparison.Ordinal)).ToList();

        findings.Should().HaveCount(2);
        findings.Single(finding => finding.RuleId == PdfValidationRuleIds.XRefEntryBroken).Location.Object.Should().Be(new PdfObjectId(5));
        findings.Single(finding => finding.RuleId == PdfValidationRuleIds.XRefObjectStreamBroken).Message.Should().EndWith("; 2 of them are not listed before that, and lost.");
    }

    [Theory]
    [InlineData("<< /Length 5 >>stream\nhello\nendstream")]
    [InlineData("[1 << /Length 5 >> stream\nhello\nendstream]")]
    public void An_object_written_as_a_stream_inside_an_object_stream_is_broken(string written)
    {
        // An object stream cannot hold a stream (7.5.7): the reader reads the object as null, and its entry is broken (#161,
        // #202). The page's /Contents names it.
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .WithObject(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << >> /Contents 4 0 R >>")
            .WithObject(4, written)
            .BuildWithXRefStream(rootNumber: 1, compressedObjects: [2, 3, 4]);

        var finding = Single(Validate(file), PdfValidationRuleIds.XRefEntryBroken);

        finding.Severity.Should().Be(PdfValidationSeverity.Error);
        finding.Location.Object.Should().Be(new PdfObjectId(4));
        finding.Message.Should().Be("The entry of object 4 places it at index 2 of object stream 5, where it is written as a stream, which an object stream cannot hold.");
        finding.Remedy.Should().Be("Write the object outside the object stream, and index it there.");
    }

    [Theory]
    [InlineData(5)]
    [InlineData(0)]
    public void An_object_written_as_a_stream_that_its_entry_misplaces_is_broken_where_its_header_lists_it(int index)
    {
        // Object 4, written as a stream at index 2 of object stream 5, is placed by its entry at an index past the header, or
        // at another member's: the finding names both indexes (#161).
        var written = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .WithObject(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << >> /Contents 4 0 R >>")
            .WithObject(4, "<< /Length 5 >>stream\nhello\nendstream")
            .BuildWithXRefStream(rootNumber: 1, compressedObjects: [2, 3, 4]);
        var file = Replace(written, "\u0002\0\0\0\u0005\0\u0002", "\u0002\0\0\0\u0005\0" + (char)index);

        var findings = Validate(file).Findings.Where(finding => finding.RuleId.StartsWith("xref.", StringComparison.Ordinal)).ToList();

        findings.Should().ContainSingle().Which.RuleId.Should().Be(PdfValidationRuleIds.XRefEntryBroken);
        findings[0].Message.Should().Be(string.Create(
            CultureInfo.InvariantCulture,
            $"The entry of object 4 places it at index {index} of object stream 5, whose header lists it at index 2, where it is written as a stream, which an object stream cannot hold."));
        findings[0].Remedy.Should().Be("Write the object outside the object stream, and index it there.");
    }

    [Theory]
    [InlineData("no index", "the file's own index does not place it")]
    [InlineData("an index that holds it free", "the file's own index does not place it")]
    [InlineData("an index that lacks it", "the file's own index does not place it")]
    [InlineData("an index a guard stopped short of it", "the part of the file's own index the reader read does not place it")]
    public void An_object_written_as_a_stream_that_only_a_rebuilt_index_places_is_broken(string chain, string clause)
    {
        // Object stream 3 holds 4, written as a stream, and 5. The file has no index; or a table that places 1 and 2 alone,
        // 2 outside the file, and holds 4 free or lists no row for it; or that table, then a cross-reference stream before
        // it, which places 4 in 3, and which MaxXRefSectionCount leaves unread. The rebuilt index places 4 in 3, where it
        // reads as null, and no entry the reader read says so but the rebuilt one (#161, #202); where a guard stopped the
        // chain, the entry that places 4 may lie past where it stopped.
        var file = new HandWrittenFile(version: "1.5");
        var one = file.Object(1, "<< /Type /Catalog /Pages 2 0 R /Foo 4 0 R >>");
        var two = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        var three = file.ObjectStream(3, [(4, "<< /Length 3 >> stream\nabc\nendstream"), (5, "<< /B 2 >>")]);
        var options = new PdfReaderOptions();

        switch (chain)
        {
            case "no index":
                file.Raw("trailer\n<< /Root 1 0 R /Size 6 >>\n%%EOF\n");
                break;
            case "an index that holds it free":
                file.Table(6, new Dictionary<int, long> { [1] = one, [2] = 99999 }, "/Root 1 0 R /Size 6");
                break;
            case "an index that lacks it":
                file.Table(3, new Dictionary<int, long> { [1] = one, [2] = 99999 }, "/Root 1 0 R /Size 6");
                break;
            default:
                var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
                {
                    [1] = (1, one, 0),
                    [2] = (1, two, 0),
                    [3] = (1, three, 0),
                    [4] = (2, 3, 0),
                    [5] = (2, 3, 1),
                };
                var older = file.CrossReferenceStream(6, 7, rows, "/Root 1 0 R");
                file.Table(3, new Dictionary<int, long> { [1] = one, [2] = 99999 }, string.Create(CultureInfo.InvariantCulture, $"/Root 1 0 R /Size 7 /Prev {older}"));
                options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxXRefSectionCount = 1 } };
                break;
        }

        using var document = PdfDocument.Open(file.Bytes, options);
        var report = new PdfValidator().Validate(document);

        document.WasRepaired.Should().BeTrue();
        var finding = report.Findings.Should().ContainSingle(each => each.Location.Object == new PdfObjectId(4)).Subject;
        finding.RuleId.Should().Be(PdfValidationRuleIds.XRefEntryBroken);
        finding.Message.Should().Be($"The index the reader rebuilt places object 4 at index 0 of object stream 3, where it is written as a stream, which an object stream cannot hold; {clause}.");
        finding.Remedy.Should().Be("Write the object outside the object stream, and index it there.");
    }

    [Fact]
    public void An_object_a_rebuild_placed_where_the_stream_s_header_now_lists_another_is_not_judged_as_written_there()
    {
        // Object stream 3 takes its filter from object 9, which object stream 8 holds. The rebuild expands 3 before 8, its
        // filter unknown yet: its data read as written, "37 20 30 20 ...", list object 37 at index 0. Read once 9 is known,
        // the data decode to a header that lists object 7 there, written as a stream: object 37 is not written there at all,
        // as a stream or otherwise.
        const string Decoded = "7 0 << /Length 3 >> stream\nabc\nendstream";
        var hex = string.Join(' ', Encoding.Latin1.GetBytes(Decoded).Select(each => each.ToString("X2", CultureInfo.InvariantCulture))) + ">";
        var file = new HandWrittenFile(version: "1.5");
        file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        file.Object(3, string.Create(
            CultureInfo.InvariantCulture,
            $"<< /Type /ObjStm /N 1 /First 4 /Filter 9 0 R /Length {hex.Length} >>\nstream\n{hex}\nendstream"));
        file.ObjectStream(8, [(9, "/ASCIIHexDecode")]);
        file.Raw("trailer\n<< /Root 1 0 R /Size 10 >>\n%%EOF\n");

        using var document = PdfDocument.Open(file.Bytes);
        var report = new PdfValidator().Validate(document);

        document.WasRepaired.Should().BeTrue();
        document.Reader.Index.TryGet(37, out var placed).Should().BeTrue("the rebuild placed 37 as the data read as written list it");
        placed.ObjectStreamNumber.Should().Be(3);
        report.Findings.Should().NotContain(each => each.Location.Object == new PdfObjectId(37));
    }

    [Fact]
    public void An_object_written_as_a_stream_that_the_chain_and_a_rebuilt_index_both_place_is_its_entry_s_finding_alone()
    {
        // The cross-reference stream places 4 in object stream 3, where it is written as a stream, and 6 inside the
        // catalog's header, which sets a rebuild off: the rebuilt index places 4 there too, and the chain's entry is judged,
        // once — the rebuilt index is judged only where the chain's places nothing.
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, file.Object(1, "<< /Type /Catalog /Pages 2 0 R /Foo 4 0 R >>"), 0),
            [2] = (1, file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"), 0),
            [3] = (1, file.ObjectStream(3, [(4, "<< /Length 3 >> stream\nabc\nendstream"), (5, "<< /B 2 >>")]), 0),
            [4] = (2, 3, 0),
            [5] = (2, 3, 1),
        };
        rows[6] = (1, rows[1].Field2, 0);
        file.CrossReferenceStream(7, 8, rows, "/Root 1 0 R");

        using var document = PdfDocument.Open(file.Bytes);
        var report = new PdfValidator().Validate(document);

        document.WasRepaired.Should().BeTrue();
        report.Findings.Should().ContainSingle(each => each.Location.Object == new PdfObjectId(4)).Which.Message.Should().Be(
            "The entry of object 4 places it at index 0 of object stream 3, where it is written as a stream, which an object stream cannot hold.");
    }

    [Theory]
    [InlineData("(upstream)")]
    [InlineData("<< /Note (a stream) /Kind /stream >>")]
    public void An_object_whose_bytes_merely_hold_stream_inside_an_object_stream_is_sound(string written)
    {
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R /Test 4 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .WithObject(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << >> >>")
            .WithObject(4, written)
            .BuildWithXRefStream(rootNumber: 1, compressedObjects: [2, 3, 4]);

        Validate(file).Findings.Should().BeEmpty();
    }

    [Theory]
    [InlineData("/N 2 /First 9", "/N 0 /First 9")]
    [InlineData("/N 2 /First 9", "/N 0 /Fxrst 9")]
    [InlineData("/N 2 /First 9 /Length", "/N 0/First 999/Length")]
    public void An_object_placed_in_an_object_stream_that_declares_none_is_broken(string text, string replacement)
    {
        // An object stream of /N 0 is empty, not damaged, and serves nothing, whatever its /First — missing, or past the
        // 124 bytes its data holds —: the object its entry places there is lost, as the reader reads the stream. Each
        // replacement is as long as what it replaces, so that the file's offsets hold.
        var file = Replace(XRefStreamFile(), text, replacement);

        var findings = Validate(file).Findings.Where(finding => finding.RuleId.StartsWith("xref.", StringComparison.Ordinal)).ToList();

        findings.Select(finding => (finding.RuleId, finding.Location.Object)).Should().Equal(
            (PdfValidationRuleIds.XRefEntryBroken, new PdfObjectId(2)),
            (PdfValidationRuleIds.XRefEntryBroken, new PdfObjectId(3)));
        findings[0].Message.Should().Be("The entry of object 2 places it at index 0 of object stream 4, whose header does not list it.");
    }

    [Fact]
    public void An_object_whose_start_a_damaged_filter_leaves_past_the_data_is_lost()
    {
        // Object stream 4's Flate data is cut short, stored blocks decoding what they hold: its header and the start of object
        // 2 decode, and object 3 starts past what does. The header ends there, as the reader reads it (#202).
        const string Header = "2 0 3 42 ";
        var members = "<< /Type /Pages /Kids [3 0 R] /Count 1 >>\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << >> >>\n";
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.NoCompression, leaveOpen: true))
        {
            zlib.Write(Encoding.ASCII.GetBytes(Header + members));
        }

        var stored = compressed.ToArray()[..(2 + 5 + Header.Length + 30)];
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"), 0),
            [4] = (1, file.Object(4, string.Create(CultureInfo.InvariantCulture, $"<< /Type /ObjStm /N 2 /First {Header.Length} /Filter /FlateDecode /Length {stored.Length} >>\nstream\n{Encoding.Latin1.GetString(stored)}\nendstream")), 0),
            [2] = (2, 4, 0),
            [3] = (2, 4, 1),
        };
        file.CrossReferenceStream(5, 6, rows, "/Root 1 0 R");

        var finding = Single(Validate(file.Bytes), PdfValidationRuleIds.XRefObjectStreamBroken);

        finding.Message.Should().Be(string.Create(
            CultureInfo.InvariantCulture,
            $"Object stream 4, where the index places 2 objects, can be read only in part: its header lists 1 of the 2 objects its /N declares, then object 3 at offset 42, past what its {Header.Length + 30} bytes of decoded data hold; 1 of them is not listed before that, and lost."));
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
    [InlineData(new[] { 8, 4, 2 }, 0x1_0000_0002UL, "type 4294967298")]
    [InlineData(new[] { 8, 4, 2 }, 3UL, "type 3")]
    [InlineData(new[] { 8, 4, 2 }, ulong.MaxValue, "type 18446744073709551615")]
    [InlineData(new[] { 1, 4, 2 }, 255UL, "type 255")]
    [InlineData(new[] { 9, 4, 2 }, 3UL, "type 3")]
    public void A_cross_reference_stream_row_of_a_type_past_2_reads_as_the_null_object_however_wide_its_type_field(
        int[] widths, ulong type, string given)
    {
        // Object 3's row keeps its fields, object stream 4 and index 1. Narrowed to an int, 2^32 + 2 was type 2 and served
        // the page. ISO 32000-1 defines types 0, 1 and 2, and reads any other as a reference to the null object (7.5.8.3):
        // the row is indexed as a free one is, and reported, never refused (#221). A ninth byte that is zero leads the type its
        // last eight give, and eight bytes all set give the largest type 64 bits hold.
        using var document = PdfDocument.Open(WithRows(widths, number: 3, type, second: 4, third: 1));

        document.GetObject(new PdfObjectId(3)).Should().BeSameAs(PdfNull.Instance);
        document.Reader.Index.TryGet(3, out var entry).Should().BeTrue();
        entry.Kind.Should().Be(XRefEntryKind.Free);
        document.GetObject(new PdfObjectId(2)).AsDictionary().IsOfType(PdfName.Pages).Should().BeTrue();
        document.WasRepaired.Should().BeFalse();
        var report = new PdfValidator().Validate(document);
        report.Contains(PdfValidationRuleIds.XRefSectionMalformed).Should().BeFalse();
        Single(report, PdfValidationRuleIds.XRefEntryTypeReserved).Message.Should().StartWith(
            "The cross-reference stream at offset ").And.Contain(" gives 1 row a reserved type: the row for object 3 gives it " + given + ". ");
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
    public void A_cross_reference_stream_row_whose_wide_type_field_holds_more_than_64_bits_reads_as_the_null_object()
    {
        // A type past 2, however it is written: the row is read as a reference to the null object, as one written in 8 bytes
        // is (#221), not refused, and the report says the type takes more than 64 bits, which its last 8 bytes do not tell.
        using var document = PdfDocument.Open(WithRows([9, 4, 2], number: 3, type: 2, second: 4, third: 1, leading: 0));

        document.GetObject(new PdfObjectId(3)).Should().BeSameAs(PdfNull.Instance);
        document.Reader.Index.TryGet(3, out var entry).Should().BeTrue();
        entry.Kind.Should().Be(XRefEntryKind.Free);
        document.GetObject(new PdfObjectId(2)).AsDictionary().IsOfType(PdfName.Pages).Should().BeTrue();
        var report = new PdfValidator().Validate(document);
        report.Contains(PdfValidationRuleIds.XRefSectionMalformed).Should().BeFalse();
        Single(report, PdfValidationRuleIds.XRefEntryTypeReserved).Message.Should().Contain(
            ": the row for object 3 gives it a type of more than 64 bits. ");
        document.Diagnostics.Should().ContainSingle(diagnostic => diagnostic.Code == PdfDiagnosticCodes.XRefEntryReserved)
            .Which.Message.Should().Contain(": the row for object 3 gives it a type of more than 64 bits. ");
    }

    [Theory]
    [InlineData("type 3", 3UL, true, null, true)]
    [InlineData("free", 0UL, true, null, false)]
    [InlineData("no row", null, true, "three", false)]
    [InlineData("type 3, no older definition", 3UL, false, null, true)]
    public void An_update_s_row_of_a_reserved_type_reads_its_object_as_null_over_an_older_one_and_is_reported(
        string shape, ulong? type, bool older, string? expected, bool reported)
    {
        // #221's four shapes: two cross-reference streams, the update's giving object 3 a row of type 3, a free row, or none.
        // ISO 32000-1 reads a type past 2 as a reference to the null object (7.5.8.3): the object reads as null, as for a free
        // row, though the original revision defines it, and the reader and the validator both report the row, once, at the
        // update's stream. A free row and no row are reported nowhere.
        var (file, _, newer) = ReservedRowFiles.Updated(type, older);
        using var document = PdfDocument.Open(file);
        var three = document.GetObject(new PdfObjectId(3));
        var report = new PdfValidator().Validate(document);

        if (expected is null)
        {
            three.Should().BeSameAs(PdfNull.Instance, shape);
        }
        else
        {
            three.Should().BeOfType<PdfString>(shape).Which.ToText().Should().Be(expected);
        }

        document.WasRepaired.Should().BeFalse(shape);

        if (!reported)
        {
            document.Diagnostics.Should().BeEmpty(shape);
            report.Findings.Should().BeEmpty(shape);
            return;
        }

        var diagnostic = document.Diagnostics.Should().ContainSingle(shape).Which;
        diagnostic.Code.Should().Be(PdfDiagnosticCodes.XRefEntryReserved);
        diagnostic.Severity.Should().Be(PdfDiagnosticSeverity.Warning);
        diagnostic.Position.Should().Be(newer);
        diagnostic.Message.Should().Be(string.Create(
            CultureInfo.InvariantCulture,
            $"The cross-reference stream startxref names at offset {newer} gives 1 row a reserved type: the row for object 3 gives it type 3. ISO 32000-1 defines types 0, 1 and 2, and reads any other as a reference to the null object (7.5.8.3): the row was read so, and no older section's row stands for its object."));
        var finding = report.Findings.Should().ContainSingle(shape).Which;
        finding.RuleId.Should().Be(PdfValidationRuleIds.XRefEntryTypeReserved);
        finding.Severity.Should().Be(PdfValidationSeverity.Warning);
        finding.Location.Position.Should().Be(newer);
        finding.Message.Should().Be(string.Create(
            CultureInfo.InvariantCulture,
            $"The cross-reference stream at offset {newer} gives 1 row a reserved type: the row for object 3 gives it type 3. ISO 32000-1 defines types 0, 1 and 2, and reads any other as a reference to the null object (7.5.8.3), as the reader does."));
        finding.Remedy.Should().Be("Rewrite the cross-reference stream with a row of type 0, 1 or 2 for each object it numbers: type 0 for one that is free.");
    }

    [Theory]
    [InlineData(true, "new three")]
    [InlineData(false, "three")]
    public void A_row_of_a_reserved_type_in_an_index_rebuilt_at_opening_is_said_to_be_read_so_only_until_the_rebuild(bool newer, string expected)
    {
        // The update's /Prev names an offset past the end of the file, so the index is rebuilt at opening, the update's row
        // of type 3 read first: object 3 reads as the scan finds it, its last definition, the update's or the original's.
        // The finding says the reader read the row as the null object only until it rebuilt the index (#221).
        var (file, _, newerAt) = ReservedRowFiles.Updated(type: 3, newer: newer, prevShift: 700);
        using var document = PdfDocument.Open(file);

        document.WasRepaired.Should().BeTrue();
        document.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == PdfDiagnosticCodes.XRefEntryReserved);
        document.GetObject(new PdfObjectId(3)).Should().BeOfType<PdfString>().Which.ToText().Should().Be(expected);
        var finding = Single(new PdfValidator().Validate(document), PdfValidationRuleIds.XRefEntryTypeReserved);
        finding.Location.Position.Should().Be(newerAt);
        finding.Message.Should().Be(string.Create(
            CultureInfo.InvariantCulture,
            $"The cross-reference stream at offset {newerAt} gives 1 row a reserved type: the row for object 3 gives it type 3. ISO 32000-1 defines types 0, 1 and 2, and reads any other as a reference to the null object (7.5.8.3), as the reader did until it rebuilt the index by scanning the file; it now reads each object such a row numbers as the rebuild finds it."));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_row_of_a_reserved_type_in_an_index_a_read_after_opening_rebuilt_is_said_to_be_read_so_only_until_the_rebuild(bool lacked)
    {
        // The index stands at opening, object 3's row of type 3 read as the null object. A read after it then has the index
        // rebuilt, before any validation: of object 9, which the update's stream numbers but does not hold, while the index
        // may have lost entries, or of object 4, whose entry places it in the middle of a string, far from any header.
        // Object 3 then reads as the scan finds it, and the finding says the reader read the row so only until it rebuilt
        // the index, as where the rebuild came at opening (#221).
        var (file, stream, asked) = lacked ? Lacking() : Astray();
        using var document = PdfDocument.Open(file);

        document.WasRepaired.Should().BeFalse();
        document.GetObject(new PdfObjectId(3)).Should().BeSameAs(PdfNull.Instance);
        document.GetObject(new PdfObjectId(asked));
        document.WasRepaired.Should().BeTrue();
        document.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == PdfDiagnosticCodes.XRefRebuilt);
        document.GetObject(new PdfObjectId(3)).Should().BeOfType<PdfString>().Which.ToText().Should().Be("three");
        Single(new PdfValidator().Validate(document), PdfValidationRuleIds.XRefEntryTypeReserved).Message.Should().Be(string.Create(
            CultureInfo.InvariantCulture,
            $"The cross-reference stream at offset {stream} gives {(lacked ? "3 rows" : "1 row")} a reserved type: the row for object 3 gives it type 3{(lacked ? ", the first of them" : string.Empty)}. ISO 32000-1 defines types 0, 1 and 2, and reads any other as a reference to the null object (7.5.8.3), as the reader did until it rebuilt the index by scanning the file; it now reads each object such a row numbers as the rebuild finds it."));

        static (byte[] File, long Stream, int Asked) Lacking()
        {
            var (file, _, newer) = ReservedRowFiles.Updated(type: 3, more: 2, missing: 1);
            return (file, newer, 9);
        }

        static (byte[] File, long Stream, int Asked) Astray()
        {
            var (file, stream) = ReservedRowFiles.Astray();
            return (file, stream, 4);
        }
    }

    [Fact]
    public void Each_stream_giving_rows_of_a_reserved_type_is_one_finding_its_first_row_named_and_its_rows_counted()
    {
        // The update gives object 3 and objects 7 to 1,006 rows of type 3, the original object 3 one of type 9: one finding a
        // section, in the chain's order, the update's counting its rows by thousands as the reader's report does.
        var (file, olderAt, newer) = ReservedRowFiles.Updated(type: 3, more: 1000, olderType: 9);

        var findings = Validate(file).Findings;

        findings.Select(finding => finding.RuleId).Should().Equal(PdfValidationRuleIds.XRefEntryTypeReserved, PdfValidationRuleIds.XRefEntryTypeReserved);
        findings[0].Location.Position.Should().Be(newer);
        findings[0].Message.Should().StartWith(string.Create(
            CultureInfo.InvariantCulture,
            $"The cross-reference stream at offset {newer} gives 1,001 rows a reserved type: the row for object 3 gives it type 3, the first of them. "));
        findings[1].Location.Position.Should().Be(olderAt);
        findings[1].Message.Should().StartWith(string.Create(
            CultureInfo.InvariantCulture,
            $"The cross-reference stream at offset {olderAt} gives 1 row a reserved type: the row for object 3 gives it type 9. "));
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
    public void An_object_stream_a_limit_cut_before_its_header_ended_says_its_one_object_was_not_checked()
    {
        // The index places the page alone in the stream, whose header, "3 0 ", the reader stops reading at 2 bytes: one
        // object, in the singular, as are the verb and the pronoun that follow it (#241).
        var file = XRefStreamBuilder().BuildWithXRefStream(rootNumber: 1, compressedObjects: [3], compressObjectStream: true);
        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxDecodedStreamLength = 2 } });

        var finding = Single(new PdfValidator().Validate(document), PdfValidationRuleIds.XRefCheckedInPart);

        finding.Location.Object.Should().Be(new PdfObjectId(4));
        finding.Message.Should().Be(
            "One of the reader's limits stopped it reading object stream 4 before its header ended: the 1 object the index places in it was not checked. Raising the limit the reader reported lets it be.");
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
    public void An_index_the_entry_bound_filled_is_said_once_to_be_checked_in_part()
    {
        // The index fills in the only section: the section is not said to be cut besides.
        var builder = new TestPdfBuilder().WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>").WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        for (var number = 3; number < 300; number++)
        {
            builder.WithObject(number, "null");
        }

        var file = builder.BuildClassic(rootNumber: 1);
        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxIndexEntries = 100 } });

        var finding = new PdfValidator().Validate(document).Findings.Should().ContainSingle().Which;

        finding.RuleId.Should().Be(PdfValidationRuleIds.XRefCheckedInPart);
        finding.Severity.Should().Be(PdfValidationSeverity.Information);
        finding.Location.Position.Should().Be(PdfTemplate.OffsetOf(file, "xref\n"));
        finding.Message.Should().Be(
            $"At offset {PdfTemplate.OffsetOf(file, "xref\n")} the index reached the most entries PdfReaderLimits.MaxIndexEntries lets the reader hold: the objects past them were not indexed or checked.");
    }

    [Fact]
    public void An_object_a_guard_left_out_of_the_index_is_no_reference_the_file_lacks()
    {
        // The update indexes the catalog alone, and the chain is read no further: the page tree it names is past the guard.
        // Whether the file holds it is unknown, so neither the object graph nor the page tree blames the file for it.
        var original = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .WithObject(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>")
            .BuildClassic(rootNumber: 1);
        var file = TestPdfBuilder.AppendIncrementalUpdate(original, rootNumber: 1, [(1, "<< /Type /Catalog /Pages 2 0 R /Lang (en) >>")]);
        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxXRefSectionCount = 1 } });

        var findings = new PdfValidator().Validate(document).Findings;

        document.WasRepaired.Should().BeFalse();
        findings.Should().NotContain(finding => finding.RuleId == PdfValidationRuleIds.ObjectReferenceMissing);
        findings.Should().NotContain(finding => finding.Message.Contains("which the file lacks", StringComparison.Ordinal));
        findings.Should().Contain(finding => finding.RuleId == PdfValidationRuleIds.XRefCheckedInPart);
    }

    [Theory]
    [InlineData(29)]
    [InlineData(32)]
    [InlineData(600)]
    public void A_startxref_naming_white_space_far_before_a_classic_table_is_imprecise_and_the_table_read(int padding)
    {
        // Startxref names the start of white space before the xref keyword. The keyword lay past the first 32 bytes the
        // reader looks at, or across them: the table was not read, the index was rebuilt, and startxref said to name neither
        // the keyword nor an object, where a cross-reference stream is read past however much white space comes first.
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            [3] = file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> >>"),
        };
        file.Raw(new string(' ', padding));
        var table = file.Table(4, rows, "/Size 4 /Root 1 0 R");
        var bytes = Encoding.Latin1.GetBytes(Encoding.Latin1.GetString(file.Bytes)
            .Replace(
                string.Create(CultureInfo.InvariantCulture, $"startxref\n{table}\n"),
                string.Create(CultureInfo.InvariantCulture, $"startxref\n{table - padding}\n"),
                StringComparison.Ordinal));
        using var document = PdfDocument.Open(bytes);

        document.Diagnostics.Should().BeEmpty();
        Single(new PdfValidator().Validate(document), PdfValidationRuleIds.XRefOffsetImprecise).Message.Should().EndWith(
            string.Create(CultureInfo.InvariantCulture, $"startxref gives offset {table - padding}, {padding} bytes before the xref keyword."));
    }

    [Theory(Timeout = 60_000)]
    [InlineData(300, false)]
    [InlineData(70_000, false)]
    [InlineData(300, true)]
    public async Task A_startxref_naming_more_white_space_than_a_trailer_s_window_before_a_classic_table_reaches_the_guard(
        int padding, bool throwOnLimit)
    {
        // The reader reads on past white space for the xref keyword as far as MaxTrailerLength reaches, past which it would
        // read the same window again and again: the guard is reached, the index rebuilt, or the guard thrown.
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            [3] = file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> >>"),
        };
        file.Raw(new string(' ', padding));
        var table = file.Table(4, rows, "/Size 4 /Root 1 0 R");
        var bytes = Encoding.Latin1.GetBytes(Encoding.Latin1.GetString(file.Bytes)
            .Replace(
                string.Create(CultureInfo.InvariantCulture, $"startxref\n{table}\n"),
                string.Create(CultureInfo.InvariantCulture, $"startxref\n{table - padding}\n"),
                StringComparison.Ordinal));
        var limits = padding > PdfReaderLimits.Default.MaxTrailerLength
            ? PdfReaderLimits.Default
            : PdfReaderLimits.Default with { MaxTrailerLength = 256 };
        var options = new PdfReaderOptions { Limits = limits, ThrowOnLimit = throwOnLimit };

        if (throwOnLimit)
        {
            await Task.Run(() => FluentActions.Invoking(() => PdfDocument.Open(bytes, options)).Should().Throw<PdfLimitExceededException>(), TestContext.Current.CancellationToken);
            return;
        }

        using var document = await Task.Run(() => PdfDocument.Open(bytes, options), TestContext.Current.CancellationToken);

        document.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.LimitTrailer, PdfDiagnosticCodes.XRefRebuilt);
        document.GetObject(new PdfObjectId(3)).AsDictionary().Required().IsOfType(PdfName.Page).Should().BeTrue();
    }

    [Fact]
    public void A_startxref_naming_white_space_that_runs_to_the_end_of_the_file_names_no_table()
    {
        // The white space runs out before the reader's window does: there is nothing more to read on to.
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            [3] = file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> >>"),
        };
        var table = file.Table(4, rows, "/Size 4 /Root 1 0 R");
        var text = Encoding.Latin1.GetString(file.Bytes).Replace(
            string.Create(CultureInfo.InvariantCulture, $"startxref\n{table}\n"),
            "startxref\n##########\n",
            StringComparison.Ordinal);
        var bytes = Encoding.Latin1.GetBytes(
            text.Replace("##########", text.Length.ToString("D10", CultureInfo.InvariantCulture), StringComparison.Ordinal) +
            new string(' ', 100));
        using var document = PdfDocument.Open(bytes);

        document.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.XRefRebuilt);
        Single(new PdfValidator().Validate(document), PdfValidationRuleIds.FileStartXRefWrong).Message.Should().EndWith(
            "which holds neither the xref keyword nor an object.");
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
    public void A_trailer_followed_by_stream_whose_length_the_chain_cannot_read_has_its_entry_judged_as_the_file_wrote_it()
    {
        // #182: the parser takes the trailer for a stream's dictionary, whose /Length, object 5, the table places outside
        // the file. Loading it as the chain was read rebuilt the index, and the entry went unjudged. The chain reads nothing
        // it cannot read where the index places it: the data is taken up to its endstream, and the entry judged as written.
        // The validator rebuilds the index before its first rule, as a load of object 5 would (#237): the rules judge the
        // chain's index all the same.
        var file = PdfTemplate.Build(PdfTemplate.Sound
            .Replace("{row:3}\n", "{row:3}\n5 1\n0000099999 00000 n \n", StringComparison.Ordinal)
            .Replace("<< /Size 4 /Root 1 0 R >>", "<< /Size 6 /Root 1 0 R /Length 5 0 R >>\nstream\nabcd\nendstream", StringComparison.Ordinal));
        using var document = PdfDocument.Open(file);
        var report = new PdfValidator().Validate(document);

        document.WasRepaired.Should().BeTrue();
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

    [Theory]
    [InlineData("/W [1 4 5 0 R]", "2.0", "element 2 of its /W")]
    [InlineData("/W [1 4 2] /Index [0 7 7 5 0 R]", "1.0", "element 3 of its /Index")]
    public void An_element_the_reader_reads_through_a_reference_is_judged_through_it(string entries, string five, string what)
    {
        // The reader reads the first three elements of a /W, and every element of an /Index: a reference among them leads to
        // the real it reads as an integer, one finding that says both.
        var file = ChainFiles.UnderAnUpdate(
            "/Type /XRef /Size 8 " + entries + " /Length {length}", five, compressed: false, predicted: false, placed: true);

        Single(Validate(file), PdfValidationRuleIds.FileTrailerValueWrong).Message.Should().EndWith(
            $"writes {what} as the reference 5 0 R, to a real number: ISO 32000-1 makes it direct (7.5.8.2), and Table 17 of ISO 32000-1 an integer.");
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

    [Theory]
    [InlineData("/Filter 5 0 R", "<< /Predictor 12.0 /Columns 7.0 >>")]
    [InlineData("/Filter [5 0 R]", "<< /Predictor 12.0 /Columns 7.0 >>")]
    [InlineData("/Filter << /Predictor 12.0 /Columns 7.0 >>", "0")]
    [InlineData("/Filter [<< /Predictor 12.0 /Columns 7.0 >>]", "0")]
    public void Parameters_a_filter_gives_as_a_dictionary_are_not_judged_by_what_decode_parameters_ask(string filter, string five)
    {
        // A /Filter, or an element of one, that is a dictionary ends decoding, the data left as it is: decoding reads nothing
        // it holds, which Table 8, a /DecodeParms', does not govern. Its reals were reported as Table 8 asks (#263).
        var file = ChainFiles.UnderAnUpdate(
            "/Type /XRef /Size 8 /W [1 4 2] " + filter + " /Length {length}", five, compressed: false, predicted: false, placed: true);

        Validate(file).Findings.Select(finding => finding.Message).Should().NotContain(message => message.Contains("Table 8", StringComparison.Ordinal));
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
    [InlineData("/N")]
    [InlineData("/First")]
    [InlineData("/Length")]
    public void An_object_stream_value_written_as_a_real_with_a_fraction_is_not_said_to_be_read_as_an_integer(string key)
    {
        // As above, but the integer and the space after it become a real of as many bytes with a fractional part, which
        // equals no integer: the reader reads no integer from it, and the finding that says it did is not made.
        var content = Encoding.Latin1.GetString(XRefStreamFile());
        var start = content.IndexOf(key + " ", content.IndexOf("/ObjStm", StringComparison.Ordinal), StringComparison.Ordinal) + key.Length + 1;
        var end = start;

        while (char.IsAsciiDigit(content[end]))
        {
            end++;
        }

        var real = end - start == 1 ? ".5" : content[start..(end - 1)] + ".5";

        Validate(Encoding.Latin1.GetBytes(content[..start] + real + content[(end + 1)..])).Findings
            .Should().NotContain(finding => finding.RuleId == PdfValidationRuleIds.XRefObjectStreamValueWrong);
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
    [InlineData("seventy zeros leading its number")]
    [InlineData("sixty zeros leading its number")]
    [InlineData("a comment between its number and its generation")]
    [InlineData("a comment of eighty bytes between its number and its generation")]
    [InlineData("eighty blanks before its generation")]
    [InlineData("seventy zeros for its generation")]
    public void A_valid_header_longer_than_a_first_read_is_found_where_its_entry_places_it(string shape)
    {
        // #175: the probe read 64 bytes where the entry places object 4, and called entry-shifted, "0 bytes after it", or
        // entry-broken a header any read takes there (ISO 32000-1, 7.3.3, 7.2.3).
        var file = WithFourWritten(LongHeader(shape) + "\n(four)\nendobj\n", out _);
        using var document = PdfDocument.Open(file);

        new PdfValidator().Validate(document).Findings.Should().BeEmpty();
        document.WasRepaired.Should().BeFalse();
    }

    [Theory]
    [InlineData("                                                         4 0 objx\n(not an object)\n")]
    [InlineData("4 0 objection\n(not an object)\n")]
    public void A_header_a_read_refuses_is_an_entry_that_leads_nowhere(string written)
    {
        // #256: the probe took a keyword its 64 bytes cut for obj, 57 bytes after the offset, and the search near the row
        // read "4 0 obj" at the start of "4 0 objection"; a read refuses both, and rebuilds the index.
        var file = WithFourWritten(written, out var row);
        using var document = PdfDocument.Open(file);

        var finding = Single(new PdfValidator().Validate(document), PdfValidationRuleIds.XRefEntryBroken);

        finding.Location.Should().Be(PdfValidationLocation.OfObject(new PdfObjectId(4), row));
        finding.Message.Should().Be(string.Create(
            CultureInfo.InvariantCulture,
            $"The entry of object 4 gives offset {row}, where there is no object header, and the object is not within 512 bytes of it."));
        document.WasRepaired.Should().BeTrue();
    }

    [Theory]
    [InlineData("seventy zeros leading its number")]
    [InlineData("a comment of eighty bytes between its number and its generation")]
    [InlineData("eighty blanks before its generation")]
    [InlineData("seventy zeros for its generation")]
    [InlineData("an obj keyword the first bytes read cut, which runs on")]
    [InlineData("a keyword that runs on, which the search near the row takes for a header")]
    [InlineData("a generation past 65535")]
    [InlineData("object 0")]
    [InlineData("its header three bytes past the row")]
    [InlineData("a comment between its number and its generation, three bytes past the row")]
    public void The_probe_judges_an_entry_as_settling_does(string shape)
    {
        // One decision for both (#256): an entry the probe calls broken is one settling rebuilds the index for, and the probe
        // takes every other where a load takes it.
        var written = shape switch
        {
            "an obj keyword the first bytes read cut, which runs on" => new string(' ', 57) + "4 0 objx\n(not an object)\n",
            "a keyword that runs on, which the search near the row takes for a header" => "4 0 objection\n(not an object)\n",
            "a generation past 65535" => "4 70000 obj\n(not an object)\nendobj\n",
            "object 0" => "0 0 obj\n(not an object)\nendobj\n",
            "its header three bytes past the row" => "   4 0 obj\n(four)\nendobj\n",
            "a comment between its number and its generation, three bytes past the row" => "   4 %c\n0 obj\n(four)\nendobj\n",
            _ => LongHeader(shape) + "\n(four)\nendobj\n",
        };
        var file = WithFourWritten(written, out _);

        using var settled = PdfDocument.Open(file);
        settled.Reader.SettleIndex();
        using var probed = PdfDocument.Open(file);
        var probe = CrossReferenceProbe.Run(probed);

        probe.Broken.Any(finding => finding.Location.Object == new PdfObjectId(4)).Should().Be(settled.WasRepaired);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_header_that_runs_past_the_object_guard_where_its_entry_places_it_is_said_to_be_unchecked(bool throwOnLimit)
    {
        // Two hundred zeros lead object 4's number, a header of 207 bytes; the reader reads no object past 128. A load reaches
        // the guard on it, and looks no further when opened to throw on one.
        var file = WithFourWritten(new string('0', 200) + "4 0 obj\n(four)\nendobj\n", out var row);
        var options = new PdfReaderOptions { ThrowOnLimit = throwOnLimit, Limits = PdfReaderLimits.Default with { MaxObjectLength = 128 } };
        using var document = PdfDocument.Open(file, options);

        var findings = new PdfValidator().Validate(document).Findings;

        findings.Select(finding => (finding.RuleId, finding.Location, finding.Message)).Should().Equal((
            PdfValidationRuleIds.XRefCheckedInPart,
            PdfValidationLocation.OfObject(new PdfObjectId(4), row),
            string.Create(
                CultureInfo.InvariantCulture,
                $"Reading the header of object 4 where its entry places it, at offset {row}, or near it, the reader reached PdfReaderLimits.MaxObjectLength: the entry was not checked. Raising the limit lets it be.")));
    }

    [Fact]
    public void An_object_found_near_a_row_whose_white_space_runs_past_the_object_guard_is_said_to_be_unchecked()
    {
        // Two hundred blanks, then object 4, where its row places it; the reader reads no object past 128 bytes. A load reaches
        // the guard on the blanks before it finds the object near the row: what the row leads to is the limit's to say.
        var file = WithFourWritten(new string(' ', 200) + "4 0 obj\n(four)\nendobj\n", out var row);
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = 128 } };
        using var document = PdfDocument.Open(file, options);

        var findings = new PdfValidator().Validate(document).Findings;

        findings.Select(finding => (finding.RuleId, finding.Location, finding.Message)).Should().Equal((
            PdfValidationRuleIds.XRefCheckedInPart,
            PdfValidationLocation.OfObject(new PdfObjectId(4), row),
            string.Create(
                CultureInfo.InvariantCulture,
                $"Reading the header of object 4 where its entry places it, at offset {row}, or near it, the reader reached PdfReaderLimits.MaxObjectLength: the entry was not checked. Raising the limit lets it be.")));
        document.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == PdfDiagnosticCodes.LimitObject && diagnostic.Position == row);
    }

    [Fact]
    public void Settling_reports_the_object_guard_where_the_header_found_near_a_row_reaches_it()
    {
        // Nothing of object 4 is where its row places it; two bytes on, its header holds two hundred blanks before obj, which
        // the search near the row reads back over, and a load reaches the guard on (#175).
        var file = WithFourWritten("x\n4 0" + new string(' ', 200) + "obj\n(four)\nendobj\n", out var row);
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = 128 } };
        using var document = PdfDocument.Open(file, options);

        document.Reader.SettleIndex();

        document.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == PdfDiagnosticCodes.LimitObject && diagnostic.Position == row + 2);
        document.WasRepaired.Should().BeTrue();
    }

    [Fact]
    public void A_row_in_white_space_that_runs_to_the_file_s_end_leads_nowhere()
    {
        // The row places object 4 in blanks after the end-of-file marker, which run to the file's end: a load reads them to
        // the end and finds nothing, which is no guard reached.
        static byte[] Build(long row)
        {
            var file = new HandWrittenFile();
            var rows = new Dictionary<int, long>
            {
                [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
                [2] = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
                [3] = file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << >> >>"),
                [4] = row,
            };
            file.Table(5, rows, "/Size 5 /Root 1 0 R");
            file.Raw(new string(' ', 1000));
            return file.Bytes;
        }

        var row = Build(0).Length - 500L;
        using var document = PdfDocument.Open(Build(row));

        var report = new PdfValidator().Validate(document);

        Single(report, PdfValidationRuleIds.XRefEntryBroken).Message.Should().Be(string.Create(
            CultureInfo.InvariantCulture,
            $"The entry of object 4 gives offset {row}, where there is no object header, and the object is not within 512 bytes of it."));
        report.Contains(PdfValidationRuleIds.XRefCheckedInPart).Should().BeFalse();
        document.Diagnostics.Should().NotContain(diagnostic => diagnostic.Code == PdfDiagnosticCodes.LimitObject);
    }

    [Fact]
    public void An_entry_placing_its_object_at_any_of_the_file_s_last_bytes_is_checked_without_asking_the_source_past_its_end()
    {
        // Object 3's entry places it at each of the file's last 64 bytes, and at its end. The probe read 64 bytes at an entry
        // wherever it lay, and a source that refuses a read past its end threw out of the validator (#167): it reads a header
        // as a load does, through windows the file clamps.
        var length = PdfTemplate.SoundWith("{row:3}", RowAt(0)).Length;

        ValidatedOverAStrictSourceAsInMemory(Enumerable.Range(0, 65).Select(back =>
            ($"object 3 placed {back} bytes before the end", PdfTemplate.SoundWith("{row:3}", RowAt(length - back)))));

        static string RowAt(long offset) => string.Create(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n ");
    }

    [Fact]
    public void An_entry_a_few_bytes_before_a_header_the_file_ends_with_is_shifted_the_search_near_it_reaching_the_last_byte()
    {
        // Object 4's header ends the file, its obj keyword the file's last three bytes, and its entry gives an offset 3 bytes
        // before it: the search near the entry reaches the file's last byte, as it reaches 512 bytes past an entry elsewhere
        // (#197), and the probe finds the header there, as a load does.
        var template = PdfTemplate.SoundEndingWith("4 0 obj").Replace("{row:4}", "{row:4:0:-3}", StringComparison.Ordinal);
        var file = PdfTemplate.Build(template);
        var header = file.Length - "4 0 obj".Length;

        var finding = Single(Validate(file), PdfValidationRuleIds.XRefEntryShifted);

        finding.Location.Should().Be(PdfValidationLocation.OfObject(new PdfObjectId(4), header - 3));
        finding.Message.Should().Be(
            $"The entry of object 4 gives offset {header - 3}, where there is no object header; the object starts 3 bytes after it, at offset {header}.");
    }

    [Fact]
    public void A_startxref_naming_any_of_the_file_s_last_bytes_is_followed_without_asking_the_source_past_its_end()
    {
        // startxref names each of the file's last 40 bytes, and its end: the chain finds no table there, the index is rebuilt,
        // and nothing the reader or the probe reads runs past the end (#167).
        var length = PdfTemplate.SoundWith("startxref\n{xref:1}", "startxref\n" + Offset(0)).Length;

        ValidatedOverAStrictSourceAsInMemory(Enumerable.Range(0, 41).Select(back =>
            ($"startxref naming {back} bytes before the end", PdfTemplate.SoundWith("startxref\n{xref:1}", "startxref\n" + Offset(length - back)))));

        static string Offset(long offset) => offset.ToString("D10", CultureInfo.InvariantCulture);
    }

    [Fact]
    public void A_file_cut_short_anywhere_in_its_last_bytes_is_read_without_asking_the_source_past_its_end()
    {
        // The sound file loses 1 to 40 bytes from its end: its marker, then its startxref, then its trailer (#167).
        var sound = PdfTemplate.Build(PdfTemplate.Sound);

        ValidatedOverAStrictSourceAsInMemory(Enumerable.Range(1, 40).Select(cut => ($"cut {cut} bytes short", sound[..^cut])));
    }

    [Fact]
    public void An_object_ending_the_file_inside_its_stream_is_read_without_a_read_past_the_end_or_an_empty_one()
    {
        // Object 4, which the catalog names, ends the file somewhere in its stream. In the last shape, 9,000 bytes of data,
        // past the window the object is parsed through, are followed by white space to the end of the file, longer than the
        // first read after the data: the search for endstream read it to the end, and asked the source for the no bytes
        // there, which a source may refuse as it may any read past its end (#167).
        const string Head = "4 0 obj\n<< /Length 5 >>\nstream";
        var blanks = new string(' ', IO.PdfObjectParser.EndObjLookahead + 1);

        ValidatedOverAStrictSourceAsInMemory(
        [
            ("the stream keyword", PdfTemplate.Build(PdfTemplate.SoundEndingWith(Head))),
            ("the data", PdfTemplate.Build(PdfTemplate.SoundEndingWith(Head + "\nhello"))),
            ("the endstream keyword", PdfTemplate.Build(PdfTemplate.SoundEndingWith(Head + "\nhello\nendstream"))),
            ("white space after endstream", PdfTemplate.Build(PdfTemplate.SoundEndingWith(Head + "\nhello\nendstream" + blanks))),
            ("the data, short of its length", PdfTemplate.Build(PdfTemplate.SoundEndingWith("4 0 obj\n<< /Length 50 >>\nstream\nhello\n"))),
            ("white space after long data", PdfTemplate.Build(PdfTemplate.SoundEndingWith(
                "4 0 obj\n<< /Length 9000 >>\nstream\n" + new string('a', 9000) + blanks))),
        ]);
    }

    [Fact]
    public void A_cross_reference_stream_with_no_data_is_decoded_without_an_empty_read()
    {
        // The only section is a cross-reference stream of /Length 0: a source that does not hold the file in memory was asked
        // to read its no bytes, which it may refuse as it may any read past its end (#167).
        var text = new StringBuilder("%PDF-1.7\n1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n");
        var seven = text.Length;
        text.Append("7 0 obj\n<< /Type /XRef /Size 8 /W [1 4 2] /Root 1 0 R /Length 0 >>\nstream\n\nendstream\nendobj\n");
        text.Append(CultureInfo.InvariantCulture, $"startxref\n{seven}\n%%EOF\n");

        ValidatedOverAStrictSourceAsInMemory([("a cross-reference stream of /Length 0", Encoding.Latin1.GetBytes(text.ToString()))]);
    }

    [Theory]
    [InlineData("x %", false)]
    [InlineData("x %", true)]
    [InlineData("4 x %", false)]
    [InlineData("4 70000 %", true)]
    [InlineData("0 0 %", false)]
    [InlineData("-4 0 %", true)]
    public void A_whole_token_no_header_begins_with_rules_one_out_however_long_what_follows(string start, bool throwOnLimit)
    {
        // Where object 4's row places it, a token no header begins with, then a comment longer than the reader reads an
        // object in: a load grew its window to the guard and reported limit.object, or threw, for bytes that hold no object,
        // and the probe said the entry was not checked. A whole token rules a header out, however large a window (#175).
        var file = WithFourWritten(start + new string('c', 4000) + "\n(not an object)\n", out var row);
        var options = new PdfReaderOptions { ThrowOnLimit = throwOnLimit, Limits = PdfReaderLimits.Default with { MaxObjectLength = 2048 } };
        using var document = PdfDocument.Open(file, options);

        document.GetObject(new PdfObjectId(4)).Should().BeOfType<PdfString>().Which.ToText().Should().Be("four");
        document.Diagnostics.Should().NotContain(diagnostic => diagnostic.Code == PdfDiagnosticCodes.LimitObject);
        var report = new PdfValidator().Validate(document);

        var finding = Single(report, PdfValidationRuleIds.XRefEntryBroken);
        finding.Location.Should().Be(PdfValidationLocation.OfObject(new PdfObjectId(4), row));
        finding.Message.Should().Be(string.Create(
            CultureInfo.InvariantCulture,
            $"The entry of object 4 gives offset {row}, where there is no object header, and the object is not within 512 bytes of it."));
        report.Contains(PdfValidationRuleIds.XRefCheckedInPart).Should().BeFalse();
    }

    [Theory]
    [InlineData("a comment of 9,000 bytes between its number and its generation")]
    [InlineData("9,000 blanks before its obj keyword")]
    [InlineData("9,000 zeros leading its number")]
    public void Valid_headers_longer_than_any_first_read_never_spend_what_validation_reads(string shape)
    {
        // Thirty objects whose valid headers run 9,000 bytes each (ISO 32000-1, 7.2.3, 7.3.3): settling ran out of what it may
        // read and rebuilt the index, which cannot read a header with a comment inside it, and the probe said 21 entries were
        // not checked. A header's reading consumes the header's own bytes, the file's length in all (#175).
        const int Count = 30;
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            [3] = file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << >> >>"),
        };

        for (var number = 4; number < Count + 4; number++)
        {
            rows[number] = file.Position;
            var header = shape switch
            {
                "a comment of 9,000 bytes between its number and its generation" => $"{number} %{new string('c', 9000)}\n0 obj",
                "9,000 blanks before its obj keyword" => $"{number} 0{new string(' ', 9000)}obj",
                _ => $"{new string('0', 9000)}{number} 0 obj",
            };
            file.Raw(string.Create(CultureInfo.InvariantCulture, $"{header}\n({number})\nendobj\n"));
        }

        file.Table(Count + 4, rows, string.Create(CultureInfo.InvariantCulture, $"/Size {Count + 4} /Root 1 0 R"));
        using var document = PdfDocument.Open(file.Bytes);

        new PdfValidator().Validate(document).Findings.Should().BeEmpty();

        document.WasRepaired.Should().BeFalse();
        for (var number = 4; number < Count + 4; number++)
        {
            document.GetObject(new PdfObjectId(number)).Should().BeOfType<PdfString>()
                .Which.ToText().Should().Be(number.ToString(CultureInfo.InvariantCulture));
        }
    }

    [Fact]
    public void An_entry_past_the_file_s_end_whose_object_a_load_finds_near_it_is_shifted()
    {
        // Object 4 is written last, and its row places it ten bytes past the file's end: a load looks near the offset all the
        // same, finds it, and keeps the index. The probe called the entry broken (#175).
        static byte[] Build(long row)
        {
            var file = new HandWrittenFile();
            var rows = new Dictionary<int, long>
            {
                [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
                [2] = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
                [3] = file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << >> >>"),
            };
            file.Object(4, "(four)");
            rows[4] = row;
            file.Table(5, rows, "/Size 5 /Root 1 0 R");
            return file.Bytes;
        }

        var row = Build(0).Length + 10L;
        var bytes = Build(row);
        var four = PdfTemplate.OffsetOf(bytes, "4 0 obj");
        using var document = PdfDocument.Open(bytes);

        var report = new PdfValidator().Validate(document);

        var finding = Single(report, PdfValidationRuleIds.XRefEntryShifted);
        finding.Location.Should().Be(PdfValidationLocation.OfObject(new PdfObjectId(4)));
        finding.Message.Should().Be(string.Create(
            CultureInfo.InvariantCulture,
            $"The entry of object 4 gives offset {row}, outside the file; the object starts {row - four} bytes before it, at offset {four}."));
        report.Contains(PdfValidationRuleIds.XRefEntryBroken).Should().BeFalse();
        document.WasRepaired.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Entries_left_undecided_once_the_probe_has_read_its_fill_are_said_not_checked_in_one_finding(bool nearby)
    {
        // Rows 3, 4 and 5 place their objects in one comment of 100 KB, which each reads to its end: the first two read twice
        // the file's length, and the third is left undecided. Row 6 places its object two bytes before a header that runs
        // past a first read: the search near the row finds it, and reading it would read more (#175).
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"),
        };
        var comment = file.Position;
        file.Raw("%" + new string('c', 100 * 1024) + "\n");
        rows[3] = rows[4] = rows[5] = comment;

        if (nearby)
        {
            rows[6] = file.Position;
            file.Raw("x\n6 0" + new string(' ', 100) + "obj\n(six)\nendobj\n");
        }

        var size = nearby ? 7 : 6;
        file.Table(size, rows, string.Create(CultureInfo.InvariantCulture, $"/Size {size} /Root 1 0 R"));
        using var document = PdfDocument.Open(file.Bytes);

        var probe = CrossReferenceProbe.Run(document);

        probe.Broken.Select(finding => finding.Location.Object).Should().Equal(new PdfObjectId(3), new PdfObjectId(4));
        var notChecked = probe.NotChecked.Should().ContainSingle().Which;
        notChecked.Location.Should().Be(PdfValidationLocation.OfObject(new PdfObjectId(5), comment));
        notChecked.Message.Should().Be(
            "The validator stopped reading object headers where entries place them once it had read twice the file's length of them, past the first bytes of each: " +
            (nearby
                ? "the entries of 2 objects it had not decided by then, the first object 5's, were not checked."
                : "the entry of object 5, which it had not decided by then, was not checked."));
    }

    [Theory]
    [InlineData("a comment")]
    [InlineData("blanks")]
    [InlineData("nothing: the header is at the row")]
    public void A_header_decided_across_the_last_of_the_allowance_is_decided(string lead)
    {
        // M5-07, M5-11: 100 bytes are left to consume, and the window that decides object 4's row holds 300 bytes of lead
        // before a token no header begins with — a comment, or blanks. The deciding window is charged whole, what remains
        // taken, and the entry is decided rather than left unchecked; a header at the row is read in its first 64 bytes,
        // free of what is left, even once nothing is.
        var written = lead switch
        {
            "a comment" => "%" + new string('c', 298) + "\nx y z\n",
            "blanks" => new string(' ', 300) + "x y z\n",
            _ => "4 0 obj\n(four)\nendobj\n",
        };
        var place = lead.StartsWith("nothing", StringComparison.Ordinal) ? HeaderPlace.AtOffset : HeaderPlace.NotFound;
        var file = WithFourWritten(written, out _);
        using var document = PdfDocument.Open(file);
        document.Reader.ChainIndex!.TryGet(4, out var entry).Should().BeTrue();
        var reading = new HeaderReading(place == HeaderPlace.AtOffset ? 0 : 100);

        var placement = document.Reader.PlaceHeader(4, entry, reading);

        placement.Place.Should().Be(place);
        reading.Remaining.Should().Be(0);
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
        // cross-reference rules, and 4 0 again once the stream had been read before validating. A row outside the file
        // places it nowhere, and the finding is located at the object alone (#125).
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
        finding.Location.Position.Should().Be(off < 0 ? four + off : null);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_object_stream_whose_row_wraps_once_the_header_is_added_is_located_at_the_object_alone(bool readFirst)
    {
        // #125: five bytes before the header carry the row past what a long holds. The sum wrapped negative, and
        // validating the file threw where the finding was located.
        var rows = WithRows([1, 8, 2], number: 4, type: 1, second: long.MaxValue, third: 0);
        var file = Encoding.Latin1.GetBytes("----\n" + Encoding.Latin1.GetString(rows)
            .Replace("/N 2 /First", "/N 2./First", StringComparison.Ordinal));
        using var document = PdfDocument.Open(file);

        if (readFirst)
        {
            document.GetObject(new PdfObjectId(4)).Should().BeOfType<PdfStream>();
        }

        var finding = Single(new PdfValidator().Validate(document), PdfValidationRuleIds.XRefObjectStreamValueWrong);

        finding.Location.Object.Should().Be(new PdfObjectId(4));
        finding.Location.Position.Should().BeNull();
    }

    [Fact]
    public void An_object_stream_found_near_a_row_past_the_end_is_located_at_the_object_alone()
    {
        // #125: the row of object stream 4, which has no /N, lies just past the end of the file, and the stream is found
        // within reach of it: the probe's finding is located at the object alone, not past the end.
        static byte[] Build(ulong row) => Encoding.Latin1.GetBytes(Encoding.Latin1.GetString(
                WithRows([1, 8, 2], number: 4, type: 1, second: row, third: 0))
            .Replace("/N 2 /First", "/X 2 /First", StringComparison.Ordinal));
        var length = Build(0).Length;
        using var document = PdfDocument.Open(Build((ulong)length + 10));

        var finding = Single(new PdfValidator().Validate(document), PdfValidationRuleIds.XRefObjectStreamBroken);

        finding.Location.Object.Should().Be(new PdfObjectId(4));
        finding.Location.Position.Should().BeNull();
    }

    [Fact]
    public void An_object_stream_whose_row_wraps_is_not_looked_for_at_the_file_s_start()
    {
        // #125: six hundred bytes before the header, and an object stream without /N, near the file's start. The wrapped
        // row, less the search's radius, did not wrap back: the probe read the file's first kilobyte, found the stream
        // there, and validating the file threw where its finding was located. Held to the same row giving 999999.
        static byte[] Shifted(ulong row)
        {
            var rows = WithRows([1, 8, 2], number: 4, type: 1, second: row, third: 0);
            return Encoding.Latin1.GetBytes(new string('-', 599) + "\n" + Encoding.Latin1.GetString(rows)
                .Replace("/N 2 /First", "/X 2 /First", StringComparison.Ordinal));
        }

        static string[] Findings(byte[] file)
        {
            using var document = PdfDocument.Open(file);
            return [.. new PdfValidator().Validate(document).Findings.Select(f => f.RuleId).Order(StringComparer.Ordinal)];
        }

        Findings(Shifted(long.MaxValue)).Should().Equal(Findings(Shifted(999999)));
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

    [Theory]
    [InlineData(2, "the 2 objects its index places in an object stream were not checked, that stream being readable only once decrypted.")]
    [InlineData(1, "the 1 object its index places in an object stream was not checked, that stream being readable only once decrypted.")]
    public void An_encrypted_document_s_object_streams_are_said_to_be_unchecked(int packed, string said)
    {
        // The builder packs the objects in one object stream, 4: the verb follows the objects, the stream that one.
        var file = Replace(
            XRefStreamBuilder()
                .WithObject(5, Encryption)
                .BuildWithXRefStream(rootNumber: 1, compressedObjects: packed == 2 ? [2, 3] : [3]),
            "/Type /XRef",
            "/Type /XRef /Encrypt 5 0 R");
        using var document = PdfDocument.Open(file, new PdfReaderOptions { ThrowOnEncrypted = false });

        var finding = new PdfValidator().Validate(document).Findings.Should().ContainSingle().Which;

        finding.RuleId.Should().Be(PdfValidationRuleIds.XRefCheckedInPart);
        finding.Location.IsDocument.Should().BeTrue();
        finding.Message.Should().Be("The document is encrypted: " + said);
    }

    [Fact]
    public void Objects_an_encrypted_document_packs_in_several_object_streams_are_said_to_be_unchecked_in_those_streams()
    {
        // The page tree in object stream 4, the page in object stream 6: the streams are counted as well as the objects.
        var file = new HandWrittenFile(version: "1.5");
        var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
        {
            [1] = (1, file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"), 0),
            [2] = (2, 4, 0),
            [3] = (2, 6, 0),
            [4] = (1, file.ObjectStream(4, [(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")]), 0),
            [5] = (1, file.Object(5, Encryption), 0),
            [6] = (1, file.ObjectStream(6, [(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << >> >>")]), 0),
        };
        file.CrossReferenceStream(7, 8, rows, "/Root 1 0 R /Encrypt 5 0 R");
        using var document = PdfDocument.Open(file.Bytes, new PdfReaderOptions { ThrowOnEncrypted = false });

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

    /// <summary>An encryption dictionary of the standard handler: the reader decrypts nothing until M16.</summary>
    private const string Encryption = "<< /Filter /Standard /V 1 /R 2 /O <00> /U <00> /P -4 >>";

    /// <summary>A document whose index is a cross-reference stream, objects 2 and 3 packed in object stream 4.</summary>
    private static byte[] XRefStreamFile() => XRefStreamBuilder().BuildWithXRefStream(rootNumber: 1, compressedObjects: [2, 3]);

    /// <summary>The bytes objects 2 and 3 of <see cref="XRefStreamBuilder"/> take packed in an object stream, past its header.</summary>
    private const int PackedLength = 42 + 73;

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

    /// <summary>
    /// The sound one-page document with <paramref name="written"/> where object 4's row places it, which nothing names, then
    /// a padding object of 1,200 bytes and a later object 4, <c>(four)</c>: a rebuild finds that one.
    /// </summary>
    private static byte[] WithFourWritten(string written, out long row)
    {
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            [3] = file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << >> >>"),
            [4] = file.Position,
        };
        file.Raw(written);
        rows[6] = file.Padding(6);
        file.Object(4, "(four)");
        file.Table(7, rows, "/Size 7 /Root 1 0 R");
        row = rows[4];
        return file.Bytes;
    }

    /// <summary>A valid header of object 4, generation 0, longer than 64 bytes or with a comment inside it.</summary>
    private static string LongHeader(string shape) => shape switch
    {
        "seventy zeros leading its number" => new string('0', 70) + "4 0 obj",
        "sixty zeros leading its number" => new string('0', 60) + "4 0 obj",
        "a comment between its number and its generation" => "4 % a comment\n0 obj",
        "a comment of eighty bytes between its number and its generation" => "4 %" + new string('c', 80) + "\n0 obj",
        "eighty blanks before its generation" => "4" + new string(' ', 80) + "0 obj",
        "seventy zeros for its generation" => "4 " + new string('0', 70) + " obj",
        _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null),
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

    /// <summary>
    /// Opens and validates each file over a source that refuses any read it does not hold — one past its end, or one of no
    /// bytes —, and asserts that the report and the diagnostics are those the same file gives in memory.
    /// </summary>
    private static void ValidatedOverAStrictSourceAsInMemory(IEnumerable<(string Shape, byte[] File)> files)
    {
        var failures = new List<string>();

        foreach (var (shape, file) in files)
        {
            using var inMemory = PdfDocument.Open(file);
            var expected = Outcome(inMemory);

            try
            {
                using var strict = PdfDocument.Open(new StrictCountingSource(file), options: null, ownsSource: true);

                if (Outcome(strict) is var outcome && outcome != expected)
                {
                    failures.Add($"{shape}: {outcome}, where memory gives {expected}");
                }
            }
            catch (InvalidOperationException exception)
            {
                failures.Add($"{shape}: {exception.Message}");
            }
        }

        failures.Should().BeEmpty();

        static string Outcome(PdfDocument document)
        {
            var report = new PdfValidator().Validate(document);
            return string.Join("; ", report.Findings) + " | " + string.Join("; ", document.Diagnostics);
        }
    }

    private static byte[] Replace(byte[] file, string text, string replacement)
    {
        var content = Encoding.Latin1.GetString(file);
        content.Should().Contain(text);
        return Encoding.Latin1.GetBytes(content.Replace(text, replacement, StringComparison.Ordinal));
    }
}
