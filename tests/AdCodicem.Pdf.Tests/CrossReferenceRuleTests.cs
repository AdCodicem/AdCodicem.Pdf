using System.Text;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.Objects;
using AdCodicem.Pdf.Validation;

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

    [Fact]
    public void A_subsection_header_without_its_count_makes_the_table_malformed()
    {
        var file = PdfTemplate.SoundWith("xref\n0 4\n", "xref\n0\n");

        Single(Validate(file), PdfValidationRuleIds.XRefSectionMalformed).Message
            .Should().Contain("does not give a first object number and a count of rows");
    }

    [Theory]
    [InlineData("/W [1 9 2]", "cannot be read: its /W gives a field a width outside 0 to 8 bytes.")]
    [InlineData("/W [1 4]", "cannot be read: its /W does not give the widths of three fields.")]
    [InlineData("/W [0 0 0]", "cannot be read: its /W gives rows of no bytes.")]
    [InlineData("/W [1 4 2] /Index [0 -1]", "is malformed: its /Index gives a subsection a count of rows out of range.")]
    public void A_cross_reference_stream_whose_widths_or_ranges_cannot_be_read_is_malformed(string layout, string fault)
    {
        var file = Replace(XRefStreamFile(), "/W [1 4 2]", layout);

        Single(Validate(file), PdfValidationRuleIds.XRefSectionMalformed).Message
            .Should().StartWith("The cross-reference stream at offset ").And.EndWith(fault);
    }

    [Theory]
    [InlineData("/Extra", "a name")]
    [InlineData("1.5", "a real number")]
    [InlineData("(rows)", "a string")]
    [InlineData("<41>", "a string")]
    [InlineData("[", "an array delimiter")]
    [InlineData(">>", "the end of a dictionary")]
    [InlineData(")", "a byte that starts no token")]
    public void A_table_holding_something_else_where_a_subsection_should_start_says_what(string text, string what)
    {
        var file = PdfTemplate.SoundWith("{row:3}\ntrailer", "{row:3}\n" + text + "\ntrailer");

        Single(Validate(file), PdfValidationRuleIds.XRefSectionMalformed).Message.Should().EndWith(
            $": it holds {what} at offset {PdfTemplate.OffsetOf(file, text + "\ntrailer")}, where a subsection or the trailer should start.");
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
    [InlineData("12 0 R", "The /Prev of the cross-reference section at offset {update} is the reference 12 0 R, not an offset.")]
    [InlineData("-1", "The /Prev of the cross-reference section at offset {update} is the integer -1, not an offset.")]
    [InlineData("/Offset", "The /Prev of the cross-reference section at offset {update} is the name /Offset, not an offset.")]
    [InlineData("1.5", "The /Prev of the cross-reference section at offset {update} is a value of type real, not an offset.")]
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

        report.Findings.Select(finding => (finding.RuleId, finding.Location.Object)).Should().Equal(
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
    [InlineData("x 0 ")]
    [InlineData("2 x ")]
    [InlineData("0 0 ")]
    [InlineData("2147483648 0 ")]
    public void An_object_stream_whose_header_lists_something_other_than_objects_cannot_be_read(string start)
    {
        // The header starts "2 0 ", object 2 at offset 0; the objects keep their offsets from /First.
        var file = XRefStreamBuilder().BuildWithXRefStream(
            rootNumber: 1, compressedObjects: [2, 3], objectStreamHeader: header => start + header[4..]);

        Single(Validate(file), PdfValidationRuleIds.XRefObjectStreamBroken).Message.Should().Be(
            "Object stream 4, where the index places 2 objects, cannot be read: its header lists 0 of the 2 objects its /N declares, then something else.");
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

    [Fact]
    public void An_encrypted_document_s_object_streams_are_said_to_be_unchecked()
    {
        var file = Replace(XRefStreamFile(), "/Type /XRef", "/Type /XRef /Encrypt 99 0 R");
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
            .WithObject(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] >>");

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
