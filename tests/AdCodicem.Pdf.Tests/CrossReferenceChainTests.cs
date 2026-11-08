using System.Globalization;
using System.IO.Compression;
using System.Text;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.IO;
using AdCodicem.Pdf.IO.XRef;
using AdCodicem.Pdf.Objects;
using AdCodicem.Pdf.Validation;
using FsCheck;
using FsCheck.Fluent;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// How the reader follows the chain of cross-reference sections when a section is not where the chain names it
/// (T25): looked for nearby and read, or reported, never dropped in silence; and what a reference to an object
/// the index lacks reads as (T27): null, as the specification says, unless the index may have lost it.
/// </summary>
/// <remarks>
/// Most files are a small document saved once more: an update whose section indexes the catalog and a new
/// version of object 4, and names through <c>/Prev</c> the original section, which alone indexes the page tree,
/// the page and object 5.
/// </remarks>
public class CrossReferenceChainTests
{
    [Theory]
    [InlineData(-7, "7 bytes after")]
    [InlineData(-2, "2 bytes after")]
    [InlineData(1, "1 byte before")]
    [InlineData(3, "3 bytes before")]
    [InlineData(12, "12 bytes before")]
    public void Reads_a_section_that_prev_names_a_few_bytes_off(int error, string found)
    {
        // Twelve bytes past the keyword is where IBM's QMF manual points, inside the first row of the table. One
        // byte before it is the end-of-line the keyword follows, where the table is read as it stands. The report says
        // which way the section was found, a count of one in the singular (#241).
        var (file, original) = Updated(prevError: error);

        using var document = PdfDocument.Open(file);

        ReadsWhole(document);
        document.WasRepaired.Should().BeFalse();
        var report = document.Diagnostics.Should().ContainSingle().Which;
        report.Code.Should().Be(PdfDiagnosticCodes.XRefOffsetAdjusted);
        report.Severity.Should().Be(PdfDiagnosticSeverity.Repair);
        report.Position.Should().Be(original);
        report.Message.Should().Be(string.Create(
            CultureInfo.InvariantCulture,
            $"The cross-reference section /Prev names at offset {original + error} was found {found} it."));
    }

    [Fact]
    public void Reports_a_section_that_prev_names_where_nothing_is_and_finds_its_objects_when_asked()
    {
        // /Prev names the start of the file's first object, more than half a kilobyte from any section. The
        // section is reported at opening; the index is rebuilt only when an object it alone indexed is asked for.
        var (file, _) = Updated(prevAt: Offset.FirstObject);

        using var document = PdfDocument.Open(file);

        document.WasRepaired.Should().BeFalse("nothing asked yet for what the missing section indexed");
        var report = document.Diagnostics.Should().ContainSingle().Which;
        report.Code.Should().Be(PdfDiagnosticCodes.XRefSectionMissing);
        report.Severity.Should().Be(PdfDiagnosticSeverity.Warning);
        report.Position.Should().Be(FirstObjectOffset);
        report.Message.Should().StartWith(string.Create(
            CultureInfo.InvariantCulture,
            $"The cross-reference section /Prev names at offset {FirstObjectOffset} is not there, nor near it"));

        ReadsWhole(document);
        document.WasRepaired.Should().BeTrue();
    }

    [Theory]
    [InlineData(700, 500, true)]
    [InlineData(700, 508, true)]
    [InlineData(700, 509, false)]
    [InlineData(700, 600, false)]
    [InlineData(2000, 500, true)]
    [InlineData(2000, 508, true)]
    [InlineData(2000, 509, false)]
    [InlineData(2000, 600, false)]
    public void A_section_prev_names_is_found_near_the_offset_only_where_it_lies_within_512_bytes_after_it_however_near_the_file_s_start(
        long section, long distance, bool nearby)
    {
        // /Prev names an offset before the original table, which no section read yet holds: in the file's first 512 bytes when
        // the table starts at 700, past them when it starts at 2,000. The search near the offset finds the table where its xref
        // keyword lies whole within 512 bytes of it; otherwise the section is missing, and what only it indexes is found by
        // rebuilding the index when it is asked for. A window clamped at the file's start once reached up to 1,024 bytes past
        // the offset: one shift was found near the start, and missing past it (#197).
        var named = section - distance;

        using var document = PdfDocument.Open(NearbyFiles.UpdateNamingItsOriginal(section, named));

        var report = document.Diagnostics.Should().ContainSingle().Which;

        if (nearby)
        {
            report.Code.Should().Be(PdfDiagnosticCodes.XRefOffsetAdjusted);
            report.Position.Should().Be(section);
            report.Message.Should().Be($"The cross-reference section /Prev names at offset {named} was found {distance} bytes after it.");
        }
        else
        {
            report.Code.Should().Be(PdfDiagnosticCodes.XRefSectionMissing);
            report.Position.Should().Be(named);
            report.Message.Should().StartWith($"The cross-reference section /Prev names at offset {named} is not there, nor near it;");
        }

        document.WasRepaired.Should().BeFalse("nothing asked yet for what the original indexes");
        Text(document, 4).Should().Be("updated");
        Text(document, 5).Should().Be("five");
        document.WasRepaired.Should().Be(!nearby);
    }

    [Theory]
    [InlineData(null, 512, true)]
    [InlineData(null, 513, false)]
    [InlineData(7, 512, true)]
    [InlineData(7, 513, false)]
    [InlineData(14, 513, false)]
    public void A_section_prev_names_is_found_near_the_offset_only_where_it_starts_within_512_bytes_before_it_whatever_number_the_window_s_start_cuts(
        int? stream, long before, bool nearby)
    {
        // /Prev names an offset 512 or 513 bytes past where the original section starts: a classic table, cross-reference
        // stream 7, or cross-reference stream 14, the window the search reads near the offset then starting on its 4. The
        // search reads the byte before its window too: a section counts only where its xref keyword, or its number's first
        // digit, lies within the window, and stream 14 is not taken for a stream that starts at its 4 (#197). Otherwise the
        // section is missing, and what only it indexes is found by rebuilding the index when it is asked for.
        const long Section = 700;
        var named = Section + before;

        using var document = PdfDocument.Open(NearbyFiles.UpdateNamingItsOriginal(Section, named, stream));

        var report = document.Diagnostics.Should().ContainSingle().Which;
        report.Code.Should().Be(nearby ? PdfDiagnosticCodes.XRefOffsetAdjusted : PdfDiagnosticCodes.XRefSectionMissing);

        if (nearby)
        {
            report.Position.Should().Be(Section);
            report.Message.Should().Be($"The cross-reference section /Prev names at offset {named} was found 512 bytes before it.");
        }

        Text(document, 5).Should().Be("five");
        document.WasRepaired.Should().Be(!nearby);
    }

    [Theory]
    [InlineData("7 0 obj")]
    [InlineData("7 % a comment\n0 obj")]
    [InlineData("7 0 % a comment\nobj")]
    [InlineData("7\r% one\r0 % two\robj")]
    public void A_cross_reference_stream_whose_header_holds_comments_is_found_near_the_offset_prev_names(string header)
    {
        // /Prev names the offset two bytes past where the original cross-reference stream starts, its header written with
        // comments between its tokens or without. The search near the offset reads a header back from its obj keyword as a
        // load reads it, comments and all (#261): the stream is found where it starts, where a header that holds a comment
        // was once no place to look for a section, and the section missing.
        const long Section = 700;

        using var document = PdfDocument.Open(NearbyFiles.UpdateNamingItsOriginal(Section, Section + 2, 7, streamHeader: header));

        var report = document.Diagnostics.Should().ContainSingle().Which;
        report.Code.Should().Be(PdfDiagnosticCodes.XRefOffsetAdjusted);
        report.Position.Should().Be(Section);
        report.Message.Should().Be("The cross-reference section /Prev names at offset 702 was found 2 bytes before it.");
        Text(document, 5).Should().Be("five");
        document.WasRepaired.Should().BeFalse();
    }

    [Fact]
    public void A_window_that_starts_on_the_file_s_second_byte_reads_its_first_too_and_takes_no_number_it_cuts_for_a_section()
    {
        // The file starts with cross-reference stream 14, before its header, and /Prev names the offset of the byte 513 bytes
        // into the file: the window the search reads near it starts on the 4 of 14, the file's second byte. It reads the first
        // byte too, as every window that does not start the file does, and stream 14 is not taken for a stream that starts at
        // its 4 (#197). The section is missing.
        const string Junk = "14 0 obj\n<< /Type /XRef /Size 15 /W [1 4 2] /Index [14 1] /Length 7 >>\nstream\n\u0001\0\0\0\0\0\0\nendstream\nendobj\n";

        using var document = PdfDocument.Open(NearbyFiles.UpdateNamingItsOriginal(1700, 513 - Junk.Length, junk: Junk));

        document.Diagnostics.Should().ContainSingle(diagnostic => diagnostic.Code == PdfDiagnosticCodes.XRefSectionMissing)
            .Which.Position.Should().Be(513);
        document.Diagnostics.Should().NotContain(
            diagnostic => diagnostic.Message.StartsWith("The cross-reference section /Prev names", StringComparison.Ordinal) &&
                diagnostic.Code == PdfDiagnosticCodes.XRefOffsetAdjusted);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(512)]
    public void The_end_of_a_startxref_keyword_is_no_place_a_section_is_looked_for_even_where_the_window_s_start_cuts_it(int past)
    {
        // A startxref keyword, rows and a trailer after it, lies alone near where /Prev names a section: 3 bytes before the
        // offset, or 512, so that the window searched near it starts on the keyword's xref. Its xref is the end of a longer
        // keyword, read with the byte before it, there as anywhere in the window (#197): no place to look for the section,
        // which is missing.
        var file = new HandWrittenFile();
        var catalog = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        var pages = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        file.Raw("%" + new string('p', 600) + "\n");
        var keyword = file.Position + "start".Length;
        file.Raw(string.Create(
            CultureInfo.InvariantCulture,
            $"startxref\n0 3\n0000000000 65535 f\r\n{catalog:D10} 00000 n\r\n{pages:D10} 00000 n\r\ntrailer\n<< /Size 3 /Root 1 0 R >>\n"));
        file.Raw("%" + new string('u', 1200) + "\n");
        var update = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        var table = file.Position;
        file.Raw(string.Create(
            CultureInfo.InvariantCulture,
            $"xref\n1 1\n{update:D10} 00000 n\r\ntrailer\n<< /Size 3 /Root 1 0 R /Prev {keyword + past} >>\nstartxref\n{table}\n%%EOF\n"));

        using var document = PdfDocument.Open(file.Bytes);

        var report = document.Diagnostics.Should().ContainSingle().Which;
        report.Code.Should().Be(PdfDiagnosticCodes.XRefSectionMissing);
        report.Position.Should().Be(keyword + past);
    }

    [Fact]
    public void Reports_a_section_that_prev_names_past_the_end_of_the_file_once_at_the_section_naming_it()
    {
        var (file, _) = Updated(prevAt: Offset.PastTheEnd);
        var naming = Encoding.Latin1.GetString(file).LastIndexOf("\nxref", StringComparison.Ordinal) + 1;

        using var document = PdfDocument.Open(file);

        var report = document.Diagnostics.Should().ContainSingle().Which;
        report.Code.Should().Be(PdfDiagnosticCodes.XRefSectionMissing);
        report.Position.Should().Be(naming, "the offset named lies outside the file (#187)");
        report.Message.Should().StartWith(string.Create(
            CultureInfo.InvariantCulture,
            $"The cross-reference section /Prev names at offset 99999999 lies outside the file, which is {file.Length:N0} bytes long"));
        ReadsWhole(document);
    }

    [Fact]
    public void Reports_an_xrefstm_named_past_the_end_of_the_file_once_at_the_section_naming_it()
    {
        var (file, _) = Updated();
        var original = Encoding.Latin1.GetString(file);
        var trailerEnd = original.LastIndexOf(" >>\nstartxref", StringComparison.Ordinal);
        var text = original.Insert(trailerEnd, " /XRefStm 99999999");
        var naming = text.LastIndexOf("\nxref", StringComparison.Ordinal) + 1;

        using var document = PdfDocument.Open(Encoding.Latin1.GetBytes(text));

        var report = document.Diagnostics.Should().ContainSingle().Which;
        report.Code.Should().Be(PdfDiagnosticCodes.XRefSectionMissing);
        report.Position.Should().Be(naming);
        ReadsWhole(document);
    }

    [Fact]
    public void Reports_a_startxref_past_the_end_of_the_file_as_the_rebuild_alone()
    {
        var file = PdfTemplate.Build(PdfTemplate.Sound.Replace("{xref:1}", "99999999", StringComparison.Ordinal));

        using var document = PdfDocument.Open(file);

        document.Diagnostics.Select(d => d.Code).Should().Equal(PdfDiagnosticCodes.XRefRebuilt);
        document.Catalog.Should().NotBeNull();
    }

    [Fact]
    public void Reports_a_loop_in_the_chain_at_the_section_it_loops_back_to_whatever_precedes_the_header()
    {
        // The offset a /Prev gives counts from the header; the report gives a position in the file (#187). The /Prev names
        // where the section starts, and the validation counts it among no offsets that name a section short (#188).
        var original = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .BuildClassic(rootNumber: 1);
        var looping = TestPdfBuilder.AppendIncrementalUpdate(
            original, rootNumber: 1, [(2, "<< /Type /Pages /Kids [] /Count 0 >>")], pointPreviousAtSelf: true);
        byte[] prefixed = [.. Encoding.ASCII.GetBytes(new string('j', 101) + "\n"), .. looping];
        var section = Encoding.Latin1.GetString(prefixed).LastIndexOf("\nxref", StringComparison.Ordinal) + 1;

        using var document = PdfDocument.Open(prefixed);

        var cycle = document.Diagnostics.Should().ContainSingle(d => d.Code == PdfDiagnosticCodes.XRefChainCycle).Which;
        cycle.Position.Should().Be(section);
        cycle.Message.Should().EndWith(string.Create(
            CultureInfo.InvariantCulture,
            $"the /Prev of the section at offset {section} names offset {section}, which the chain has already read."));
        new PdfValidator().Validate(document).Findings.Select(finding => finding.RuleId).Should().NotContain(PdfValidationRuleIds.XRefOffsetImprecise);
    }

    [Fact]
    public void Places_a_loop_back_to_a_section_named_outside_the_file_at_what_named_it()
    {
        // A newer table's /XRefStm and the update's /Prev naming one offset past the end: the /Prev loops back to what the
        // /XRefStm named, and the report goes to the section whose trailer names it again, a position in the file (#187). An
        // /XRefStm that names what its own table's /Prev names is not followed, the /Prev reading it (#156): the loop takes
        // two tables.
        var (file, _) = Updated(prevAt: Offset.PastTheEnd);
        var text = Encoding.Latin1.GetString(file);
        var section = text.LastIndexOf("\nxref", StringComparison.Ordinal) + 1;
        text += string.Create(
            CultureInfo.InvariantCulture,
            $"xref\n0 1\n0000000000 65535 f\r\ntrailer\n<< /Size 8 /Root 1 0 R /Prev {section} /XRefStm 99999999 >>\nstartxref\n{text.Length}\n%%EOF\n");

        using var document = PdfDocument.Open(Encoding.Latin1.GetBytes(text));

        var cycle = document.Diagnostics.Should().ContainSingle(d => d.Code == PdfDiagnosticCodes.XRefChainCycle).Which;
        cycle.Position.Should().Be(section);
        cycle.Message.Should().EndWith(string.Create(
            CultureInfo.InvariantCulture,
            $"the /Prev of the section at offset {section} names offset 99999999, outside the file, which the chain has already named."));
    }

    [Fact]
    public void Places_a_loop_at_the_section_it_loops_back_to_rather_than_at_the_one_that_named_it()
    {
        // The newest section names the older through /Prev, and the older names the newest back.
        var (file, newest, older) = Looping();

        using var document = PdfDocument.Open(file);

        var cycle = document.Diagnostics.Should().ContainSingle(d => d.Code == PdfDiagnosticCodes.XRefChainCycle).Which;
        cycle.Position.Should().Be(newest).And.NotBe(older);
        cycle.Message.Should().EndWith(string.Create(
            CultureInfo.InvariantCulture,
            $"the /Prev of the section at offset {older} names offset {newest}, which the chain has already read."));
    }

    [Fact]
    public void Places_sections_the_chain_cannot_read_or_find_in_the_file_whatever_precedes_the_header()
    {
        // Five bytes before the header shift every offset the file gives; a report gives an offset in the file.
        var (unreadable, original) = Updated();
        var text = Encoding.Latin1.GetString(unreadable);
        var trailer = text.IndexOf("trailer", StringComparison.Ordinal);
        unreadable = Encoding.Latin1.GetBytes("junk\n" + string.Concat(text.AsSpan(0, trailer), "/Bad 1 ", text.AsSpan(trailer + 7)));
        var (missing, _) = Updated(prevAt: Offset.FirstObject);
        missing = [.. "junk\n"u8, .. missing];

        using var first = PdfDocument.Open(unreadable);
        using var second = PdfDocument.Open(missing);

        first.Diagnostics.Should().ContainSingle(d => d.Code == PdfDiagnosticCodes.XRefSectionUnreadable).Which.Position.Should().Be(original + 5);
        second.Diagnostics.Should().ContainSingle(d => d.Code == PdfDiagnosticCodes.XRefSectionMissing).Which.Position.Should().Be(FirstObjectOffset + 5);
    }

    [Fact]
    public void Places_a_section_whose_offset_wraps_once_the_header_s_is_added_at_the_section_naming_it()
    {
        // Five bytes before the header and a /Prev of the largest long: the sum wraps negative (#125), and is no
        // position in the file either.
        var (file, _) = Updated(prevAt: Offset.PastTheEnd);
        var text = "junk\n" + Encoding.Latin1.GetString(file).Replace("/Prev 99999999", "/Prev 9223372036854775807", StringComparison.Ordinal);
        var naming = text.LastIndexOf("\nxref", StringComparison.Ordinal) + 1;

        using var document = PdfDocument.Open(Encoding.Latin1.GetBytes(text));

        document.Diagnostics.Should().ContainSingle(d => d.Code == PdfDiagnosticCodes.XRefSectionMissing).Which.Position.Should().Be(naming);
    }

    [Fact]
    public void Takes_a_section_named_at_the_file_s_length_for_one_outside_it()
    {
        // The last byte of the file is at its length less one: the length itself is past the end.
        var (file, _) = Updated(prevAt: Offset.PastTheEnd);
        var text = Encoding.Latin1.GetString(file);
        file = Encoding.Latin1.GetBytes(text.Replace("/Prev 99999999", "/Prev " + file.Length.ToString("D8", CultureInfo.InvariantCulture), StringComparison.Ordinal));
        var naming = text.LastIndexOf("\nxref", StringComparison.Ordinal) + 1;

        using var document = PdfDocument.Open(file);

        var report = document.Diagnostics.Should().ContainSingle().Which;
        report.Code.Should().Be(PdfDiagnosticCodes.XRefSectionMissing);
        report.Position.Should().Be(naming);
        report.Message.Should().StartWith(string.Create(
            CultureInfo.InvariantCulture,
            $"The cross-reference section /Prev names at offset {file.Length} lies outside the file, which is {file.Length:N0} bytes long"));
    }

    [Fact]
    public void Goes_on_through_prev_past_an_xrefstm_that_names_nothing()
    {
        // A hybrid file's stream indexes what its classic table leaves out. When it is missing, that is reported,
        // and the chain still reaches the original section through /Prev: nothing it indexes needs a rebuild.
        var (file, _) = Updated(xrefStm: XRefStm.Nowhere);

        using var document = PdfDocument.Open(file);

        ReadsWhole(document);
        document.WasRepaired.Should().BeFalse();
        var report = document.Diagnostics.Should().ContainSingle().Which;
        report.Code.Should().Be(PdfDiagnosticCodes.XRefSectionMissing);
        report.Message.Should().StartWith(string.Create(
            CultureInfo.InvariantCulture,
            $"The cross-reference section /XRefStm names at offset {FirstObjectOffset} is not there, nor near it"));
    }

    [Fact]
    public void Reports_a_table_that_prev_names_and_cannot_be_read_as_unreadable_and_keeps_the_rows_read_before_the_fault()
    {
        // The original table's trailer keyword is a stray name: its rows are read, its trailer is not. The update's table,
        // within 512 bytes of it, is not taken for it: the original is there (#188, #281).
        var (file, original) = Updated();
        var text = Encoding.Latin1.GetString(file);
        var trailer = text.IndexOf("trailer", StringComparison.Ordinal);
        trailer.Should().BeGreaterThan((int)original, "the original's trailer comes first");
        file = Encoding.Latin1.GetBytes(string.Concat(text.AsSpan(0, trailer), "/Bad 1 ", text.AsSpan(trailer + 7)));

        using var document = PdfDocument.Open(file);

        var report = document.Diagnostics.Should().ContainSingle().Which;
        report.Code.Should().Be(PdfDiagnosticCodes.XRefSectionUnreadable);
        report.Severity.Should().Be(PdfDiagnosticSeverity.Warning);
        report.Position.Should().Be(original);
        report.Message.Should().StartWith(string.Create(
            CultureInfo.InvariantCulture,
            $"The cross-reference table /Prev names at offset {original} is there but cannot be read: it holds a name at offset {trailer}, where a subsection or the trailer should start."));

        ReadsWhole(document);
        document.WasRepaired.Should().BeFalse("the rows read before the fault index the original's objects");
    }

    [Fact]
    public void Quotes_a_keyword_holding_an_escape_where_a_trailer_should_be_escaped()
    {
        // The original table's trailer keyword holds ESC: the report quotes the keyword the file wrote, escaped (#159).
        var (file, original) = Updated();
        var text = Encoding.Latin1.GetString(file);
        var trailer = text.IndexOf("trailer", StringComparison.Ordinal);
        file = Encoding.Latin1.GetBytes(string.Concat(text.AsSpan(0, trailer), "tr\u001Biler", text.AsSpan(trailer + 7)));

        using var document = PdfDocument.Open(file);

        document.Diagnostics.Should().ContainSingle(d => d.Code == PdfDiagnosticCodes.XRefSectionUnreadable).Which.Message.Should().StartWith(
            string.Create(
                CultureInfo.InvariantCulture,
                $"The cross-reference table /Prev names at offset {original} is there but cannot be read: it holds the keyword tr#1Biler at offset {trailer},"));
    }

    [Fact]
    public void Reports_a_table_whose_rows_run_into_a_dictionary_with_no_trailer_keyword_as_unreadable()
    {
        var (file, original) = Updated();
        var text = Encoding.Latin1.GetString(file);
        var trailer = text.IndexOf("trailer", StringComparison.Ordinal);
        file = Encoding.Latin1.GetBytes(string.Concat(text.AsSpan(0, trailer), "       ", text.AsSpan(trailer + 7)));

        using var document = PdfDocument.Open(file);

        var report = document.Diagnostics.Should().ContainSingle().Which;
        report.Code.Should().Be(PdfDiagnosticCodes.XRefSectionUnreadable);
        report.Position.Should().Be(original);
        report.Message.Should().StartWith(string.Create(
            CultureInfo.InvariantCulture,
            $"The cross-reference table /Prev names at offset {original} is there but cannot be read: its rows are not followed by the trailer keyword."));
        ReadsWhole(document);
        document.WasRepaired.Should().BeFalse();
    }

    [Fact]
    public void Reports_a_cross_reference_stream_that_prev_names_and_cannot_be_read_as_unreadable_and_finds_its_objects_when_asked()
    {
        var (file, original) = Updated(originalAsStream: true);
        var text = Encoding.Latin1.GetString(file);
        var widths = text.IndexOf("/W [1 4 2]", StringComparison.Ordinal);
        widths.Should().BeGreaterThan((int)original, "the original's stream comes first");
        file = Encoding.Latin1.GetBytes(string.Concat(text.AsSpan(0, widths), "/W[1 -4 2]", text.AsSpan(widths + 10)));

        using var document = PdfDocument.Open(file);

        var report = document.Diagnostics.Should().ContainSingle().Which;
        report.Code.Should().Be(PdfDiagnosticCodes.XRefSectionUnreadable);
        report.Position.Should().Be(original);
        report.Message.Should().StartWith(string.Create(
            CultureInfo.InvariantCulture,
            $"The cross-reference stream /Prev names at offset {original} is there but cannot be read: its /W gives a field a negative width."));

        document.WasRepaired.Should().BeFalse("nothing asked yet for what the stream indexed");
        ReadsWhole(document);
        document.WasRepaired.Should().BeTrue();
    }

    [Fact]
    public void Reports_a_cross_reference_stream_that_xrefstm_names_and_cannot_be_read_as_unreadable_and_finds_its_objects_when_asked()
    {
        var (file, stream) = Updated(xrefStm: XRefStm.Present);
        file = Encoding.Latin1.GetBytes(Encoding.Latin1.GetString(file).Replace("/W [1 4 2]", "/W [1 4]  ", StringComparison.Ordinal));

        using var document = PdfDocument.Open(file);

        var report = document.Diagnostics.Should().ContainSingle().Which;
        report.Code.Should().Be(PdfDiagnosticCodes.XRefSectionUnreadable);
        report.Position.Should().Be(stream);
        report.Message.Should().StartWith(string.Create(
            CultureInfo.InvariantCulture,
            $"The cross-reference stream /XRefStm names at offset {stream} is there but cannot be read: its /W does not give the widths of three fields."));

        ReadsWhole(document);
        document.WasRepaired.Should().BeFalse("the chain goes on through /Prev past the stream");
        Text(document, 6).Should().Be("only in the stream");
        document.WasRepaired.Should().BeTrue();
        document.Diagnostics.Should().NotContain(d => d.Code == PdfDiagnosticCodes.XRefSectionMissing);
    }

    [Theory]
    [InlineData("/W [1 -4 2]", "its /W gives a field a negative width")]
    [InlineData("/W [1 4]", "its /W does not give the widths of three fields")]
    public void A_cross_reference_stream_whose_rows_cannot_be_read_hands_its_prev_on_to_the_older_sections(string widths, string fault)
    {
        // The middle of three revisions is a stream of /Type /XRef whose /W no row can be read with, its dictionary read
        // whole, its /Prev naming the oldest table. The chain stopped there: the oldest table was never read, and the catalog
        // and object 3, which it alone indexes, cost a rebuild of the whole file (#208). The chain goes on through the /Prev:
        // object 3 needs no rebuild, nor does object 8, which the stream does not number; object 4, which only the stream's
        // rows index, is found by rebuilding the index.
        var (sound, _, _, _) = LostStreamFiles.ThreeRevisions(LostStreamFiles.Unreadable.Replace("/W [1 -4 2]", "/W [1 4 2]", StringComparison.Ordinal));
        var (file, oldest, stream, newest) = LostStreamFiles.ThreeRevisions(LostStreamFiles.Unreadable.Replace("/W [1 -4 2]", widths, StringComparison.Ordinal));

        using (var whole = PdfDocument.Open(sound))
        {
            whole.Diagnostics.Should().BeEmpty();
            Text(whole, 3).Should().Be("only in the oldest");
            Text(whole, 4).Should().Be("only in the stream");
            whole.WasRepaired.Should().BeFalse();
        }

        using var document = PdfDocument.Open(file);

        var report = document.Diagnostics.Should().ContainSingle().Which;
        report.Code.Should().Be(PdfDiagnosticCodes.XRefSectionUnreadable);
        report.Severity.Should().Be(PdfDiagnosticSeverity.Warning);
        report.Position.Should().Be(stream);
        report.Message.Should().Be(string.Create(
            CultureInfo.InvariantCulture,
            $"The cross-reference stream /Prev names at offset {stream} is there but cannot be read: {fault}. The chain goes on through its /Prev; the objects it numbers that no section read before it indexes are looked for by rebuilding the index, rather than taken from an older section's rows."));

        var sections = document.Reader.Structure.Sections;
        sections.Select(section => (section.Offset, section.State)).Should().Equal(
            (newest, XRefSectionState.Read), (stream, XRefSectionState.Malformed), (oldest, XRefSectionState.Read));
        sections[1].PreviousHandedOn.Should().BeTrue();
        sections[1].Fault.Should().Be(fault);
        sections[2].NamedBy.Should().Be("/Prev");
        sections[2].NamedFrom.Should().Be(stream);
        Text(document, 3).Should().Be("only in the oldest");
        Text(document, 8).Should().Be("old eight");
        document.WasRepaired.Should().BeFalse("the oldest table indexes the catalog and objects 3 and 8");
        Text(document, 4).Should().Be("only in the stream");
        document.WasRepaired.Should().BeTrue();
    }

    [Theory]
    [InlineData("/FlateDecode", PdfDiagnosticCodes.FilterFailed)]
    [InlineData("/Foo", PdfDiagnosticCodes.FilterUnsupported)]
    [InlineData("/Fl", PdfDiagnosticCodes.FilterUnsupported)]
    public void A_cross_reference_stream_whose_filters_decode_nothing_of_its_data_hands_its_prev_on_to_the_older_sections(
        string filter, string decoding)
    {
        // The middle of three revisions is a stream of /Type /XRef whose 21 bytes of data, xxxxxxxxxxxxxxxxxxxxx, its filter
        // decodes nothing of: Flate data corrupt before its first byte decodes, a filter the library does not know, or /Fl, an
        // abbreviation it does not read in a stream's /Filter (#250). The encoded bytes were read as the stream's rows, the
        // first byte of each, 120, a reserved type: objects 4 and 7 read as the null object, no older row standing for them
        // (#221). The data holds no row, and the stream cannot be read, as one whose /W no row can be read with: the chain
        // goes on through its /Prev, the numbers it declares refused to the older sections' rows, and objects 4 and 7, which
        // its rows alone index in their last revisions, are found by rebuilding the index (#208). What decoding met is
        // reported once, beside the section.
        var (file, oldest, stream, newest) = LostStreamFiles.ThreeRevisions(
            "/Type /XRef /Size 9 /Index [4 2 7 1] /W [1 4 2] /Filter " + filter, written: new string('x', 21));

        using var document = PdfDocument.Open(file);

        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(decoding, PdfDiagnosticCodes.XRefSectionUnreadable);
        document.Diagnostics[1].Message.Should().Be(string.Create(
            CultureInfo.InvariantCulture,
            $"The cross-reference stream /Prev names at offset {stream} is there but cannot be read: its data could not be decoded. The chain goes on through its /Prev; the objects it numbers that no section read before it indexes are looked for by rebuilding the index, rather than taken from an older section's rows."));

        var sections = document.Reader.Structure.Sections;
        sections.Select(section => (section.Offset, section.State)).Should().Equal(
            (newest, XRefSectionState.Read), (stream, XRefSectionState.Malformed), (oldest, XRefSectionState.Read));
        sections[1].PreviousHandedOn.Should().BeTrue();
        sections[1].Fault.Should().Be("its data could not be decoded");
        Text(document, 3).Should().Be("only in the oldest");
        Text(document, 8).Should().Be("old eight");
        document.WasRepaired.Should().BeFalse("the oldest table indexes the catalog and objects 3 and 8");
        Text(document, 4).Should().Be("only in the stream");
        Text(document, 7).Should().Be("new seven");
        document.WasRepaired.Should().BeTrue();
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().NotContain(PdfDiagnosticCodes.XRefEntryReserved);
    }

    [Fact]
    public void A_cross_reference_stream_found_moved_whose_filters_decode_nothing_of_its_data_is_reported_where_it_was_found()
    {
        // The same file, the newest table's /Prev naming a place 3 bytes into the stream's header, where nothing is: the
        // search near it finds the stream, which declares itself a section, the nearest place, and cannot be read. Its
        // encoded bytes were read as rows, the stream taken for the section found moved, and objects 4 and 7 read as the
        // null object (#221). The stream is the section found moved, reported where it was found, what its decoding met
        // beside it, and the chain goes on through its /Prev, its numbers refused to the older sections' rows (#208, #281).
        var (file, oldest, stream, newest) = LostStreamFiles.ThreeRevisions(
            "/Type /XRef /Size 9 /Index [4 2 7 1] /W [1 4 2] /Filter /FlateDecode", written: new string('x', 21), namedShortBy: -3);

        using var document = PdfDocument.Open(file);

        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(
            PdfDiagnosticCodes.FilterFailed, PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefSectionUnreadable);
        document.Diagnostics[2].Message.Should().StartWith(string.Create(
            CultureInfo.InvariantCulture,
            $"The cross-reference stream /Prev names at offset {stream + 3}, found at offset {stream}, is there but cannot be read: its data could not be decoded. The chain goes on through its /Prev;"));
        document.Reader.Structure.Sections.Select(section => (section.Offset, section.State)).Should().Equal(
            (newest, XRefSectionState.Read), (stream, XRefSectionState.Malformed), (oldest, XRefSectionState.Read));
        Text(document, 3).Should().Be("only in the oldest");
        document.WasRepaired.Should().BeFalse();
        Text(document, 4).Should().Be("only in the stream");
        Text(document, 7).Should().Be("new seven");
        document.WasRepaired.Should().BeTrue();
    }

    [Theory]
    [InlineData("+5 0 obj", "/Foo", PdfDiagnosticCodes.FilterUnsupported, -3)]
    [InlineData("+5 0 obj", "/Foo", PdfDiagnosticCodes.FilterUnsupported, 12)]
    [InlineData("+5 0 obj", "/FlateDecode", PdfDiagnosticCodes.FilterFailed, -3)]
    [InlineData("++5 0 obj", "/Foo", PdfDiagnosticCodes.FilterUnsupported, -3)]
    [InlineData("+-5 0 obj", "/FlateDecode", PdfDiagnosticCodes.FilterFailed, 12)]
    [InlineData("5 0 obj", "/Foo", PdfDiagnosticCodes.FilterUnsupported, -3)]
    [InlineData("05 0 obj", "/FlateDecode", PdfDiagnosticCodes.FilterFailed, 12)]
    public void A_cross_reference_stream_found_moved_whose_filters_decode_nothing_is_reported_where_it_was_found_beside_an_older_table_whether_a_plus_sign_starts_its_number_or_not(
        string header, string filter, string decoding, int namedShortBy)
    {
        // The same file, the oldest table within 512 bytes of the stream, whose number is written with a sign, a run of
        // signs that starts with a +, or none, and which the newest table's /Prev names 3 bytes into its header or 12
        // bytes short of it. The walk back from obj finds the header at its first digit, where the line test found no
        // token starting, the lexer's token starting at the sign: the stream was dropped as declaring nothing, though a
        // load reads it from the sign as object 5, and the oldest table taken in its place, objects 4 and 7 served from
        // the oldest revision or as null with nothing reported but the shift. The header is a token from its first sign,
        // as a load reads it: the stream is the section found moved, at the digit the walk found, reported there, and the
        // chain goes on through its /Prev, its numbers refused to the oldest table's rows (#281, #327).
        var (file, oldest, stream, newest) = LostStreamFiles.ThreeRevisions(
            "/Type /XRef /Size 9 /Index [4 2 7 1] /W [1 4 2] /Filter " + filter,
            before: 0,
            namedShortBy: namedShortBy,
            written: new string('x', 21),
            header: header);
        var found = stream + header.AsSpan().IndexOfAnyInRange('0', '9');
        var named = stream - namedShortBy;
        (named - oldest).Should().BeLessThan(PdfFileReader.NearbySearchRadius, "the oldest table lies within reach of the search");

        using var document = PdfDocument.Open(file);

        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(
            decoding, PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefSectionUnreadable);
        document.Diagnostics[2].Position.Should().Be(found);
        document.Diagnostics[2].Message.Should().StartWith(Invariant(
            $"The cross-reference stream /Prev names at offset {named}, found at offset {found}, is there but cannot be read: its data could not be decoded. The chain goes on through its /Prev;"));
        document.Reader.Structure.Sections.Select(section => (section.Offset, section.State)).Should().Equal(
            (newest, XRefSectionState.Read), (found, XRefSectionState.Malformed), (oldest, XRefSectionState.Read));
        Text(document, 3).Should().Be("only in the oldest");
        document.WasRepaired.Should().BeFalse();
        Text(document, 4).Should().Be("only in the stream");
        Text(document, 7).Should().Be("new seven");
        document.WasRepaired.Should().BeTrue();
        new PdfValidator().Validate(document).Findings.Where(finding => finding.Severity == PdfValidationSeverity.Error)
            .Select(finding => (finding.RuleId, finding.Location.Position)).Should().Equal((PdfValidationRuleIds.XRefSectionMalformed, found));
    }

    [Theory]
    [InlineData("-5 0 obj")]
    [InlineData("-+5 0 obj")]
    public void A_cross_reference_stream_whose_number_a_minus_sign_starts_declares_nothing_and_the_older_table_within_reach_is_taken(string header)
    {
        // The same file, the stream's number written with a run of signs that starts with a -: a load reads a negative
        // number there, no object's header, and the stream declares nothing, though the walk back from obj finds digits
        // there that a header could start with. The search drops it, what decoding its data met with it, and takes the
        // oldest table, the nearest place left (#327).
        var (file, oldest, stream, _) = LostStreamFiles.ThreeRevisions(
            "/Type /XRef /Size 9 /Index [4 2 7 1] /W [1 4 2] /Filter /Foo", before: 0, namedShortBy: -3, written: new string('x', 21), header: header);

        using var document = PdfDocument.Open(file);

        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(PdfDiagnosticCodes.XRefOffsetAdjusted);
        document.Diagnostics[0].Message.Should().Be(Invariant(
            $"The cross-reference section /Prev names at offset {stream + 3} was found {stream + 3 - oldest} bytes before it."));
        document.Reader.Structure.Sections[1].Should().Match<XRefSectionRecord>(
            section => section.State == XRefSectionState.Relocated && section.Offset == oldest);
        Text(document, 7).Should().Be("old seven");
        document.WasRepaired.Should().BeFalse();
    }

    [Theory]
    [InlineData("/FlateDecode", "xxxxxxx", PdfDiagnosticCodes.FilterFailed)]
    [InlineData("/Foo", "xxxxxxx", PdfDiagnosticCodes.FilterUnsupported)]
    [InlineData("/Fl", "xxxxxxx", PdfDiagnosticCodes.FilterUnsupported)]
    [InlineData("[/ASCIIHexDecode /FlateDecode]", "78787878787878>", PdfDiagnosticCodes.FilterFailed)]
    [InlineData("[/ASCIIHexDecode /Foo]", "78787878787878>", PdfDiagnosticCodes.FilterUnsupported)]
    public void A_cross_reference_stream_startxref_names_whose_filters_decode_nothing_of_its_data_leaves_the_file_to_a_rebuild(
        string filter, string data, string decoding)
    {
        // startxref names a stream of /Type /XRef that numbers object 3 alone, over the oldest table, which writes object 3
        // too. Its filters decode nothing of its data, the bytes xxxxxxx: a filter that does not run or Flate data corrupt
        // before its first byte decodes, alone or past ASCIIHex, which decoded. The bytes were read as the stream's row,
        // whose first byte, 120, is a reserved type: object 3 read as the null object, no older row standing for it (#221).
        // The data holds no row, and the stream cannot be read: the file is rebuilt as it opens, as where startxref names any
        // section that cannot be read, object 3 found where the oldest table places it, and the validator says what made the
        // stream unreadable. What decoding met is reported once.
        var (file, _, stream) = UndecodedStreamFiles.OverAnOlderTable(filter, data);

        using var document = PdfDocument.Open(file);

        document.Reader.Structure.Sections.Select(section => (section.Offset, section.State, section.Fault)).Should().Equal(
            (stream, XRefSectionState.Malformed, "its data could not be decoded"));
        document.Reader.Structure.Sections[0].UndecodedReports.Should().BeNull(
            "what the decoding reported stands beside a section the chain names, and no mark is kept to drop it");
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(decoding, PdfDiagnosticCodes.XRefRebuilt);
        Text(document, 3).Should().Be("three, as the oldest table gives it");

        var finding = new PdfValidator().Validate(document).Findings.Should().ContainSingle().Which;
        finding.RuleId.Should().Be(PdfValidationRuleIds.XRefSectionMalformed);
        finding.Message.Should().Be(string.Create(
            CultureInfo.InvariantCulture, $"The cross-reference stream at offset {stream} cannot be read: its data could not be decoded."));
    }

    [Fact]
    public void A_cross_reference_stream_under_no_filter_gives_its_data_as_rows_whatever_its_bytes()
    {
        // The same file, the stream giving no /Filter, its 7 bytes of data a zero, then xxxxxx: what it holds is its row as
        // written, a free row for object 3, which reads as the null object, the oldest table's row for it standing for
        // nothing. Only data that filters decode nothing of holds no rows (#221).
        var (file, _, _) = UndecodedStreamFiles.OverAnOlderTable(string.Empty, "\0xxxxxx");

        using var document = PdfDocument.Open(file);

        document.Diagnostics.Should().BeEmpty();
        document.GetObject(new PdfObjectId(3)).Should().BeSameAs(PdfNull.Instance);
        document.WasRepaired.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(2)]
    public void A_writer_s_update_in_zlib_data_serves_the_new_revision_of_what_it_rewrites_whether_its_data_decodes_whole_or_nothing_of_it_decodes(int? damagedAt)
    {
        // A writer's update rewrites object 4 and indexes it in a cross-reference stream of zlib data, which startxref names
        // over the oldest table. Sound, the data decodes to the update's rows. Damaged at its third byte, the first of the
        // deflate data, it decodes to nothing: its zlib header's first byte, 120, was read as a row's type, a reserved one,
        // and object 4 as the null object, no older row standing for it (#221) — and where reserved rows were passed over,
        // the oldest table's row served object 4's older revision. The data holds no row, the file is rebuilt as it opens, and
        // the rebuild, which keeps the last definition the file writes, serves the new revision.
        var (file, _, _) = UndecodedStreamFiles.UpdateInZlibData(damagedAt);

        using var document = PdfDocument.Open(file);

        Text(document, 4).Should().Be("new four");
        document.WasRepaired.Should().Be(damagedAt is not null);
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(
            damagedAt is null ? [] : [PdfDiagnosticCodes.FilterFailed, PdfDiagnosticCodes.XRefRebuilt, PdfDiagnosticCodes.ObjectRedefined]);
    }

    [Fact]
    public void A_file_whose_only_cross_reference_stream_decodes_to_nothing_is_rebuilt_and_its_catalog_found()
    {
        // The file's one cross-reference stream, which startxref names, holds zlib data damaged at its third byte, which
        // decodes to nothing, and the file has no trailer keyword. The encoded bytes were read as rows, three of a reserved
        // type, and the validator said no object of the file is a catalog, though the rebuild found one (#260). The data holds
        // no row, the file is rebuilt as it opens, and the rebuild finds the catalog, the stream's dictionary, which alone
        // gave /Root, read as no trailer: the validator says only what made the stream unreadable.
        var (file, stream) = UndecodedStreamFiles.AloneInDamagedZlibData();

        using var document = PdfDocument.Open(file);

        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(
            PdfDiagnosticCodes.FilterFailed, PdfDiagnosticCodes.XRefRebuilt, PdfDiagnosticCodes.TrailerRootRecovered);
        document.WasRepaired.Should().BeTrue();
        document.GetObject(new PdfObjectId(1)).AsDictionary().Required().IsOfType(PdfName.Catalog).Should().BeTrue();

        var finding = new PdfValidator().Validate(document).Findings.Should().ContainSingle().Which;
        finding.RuleId.Should().Be(PdfValidationRuleIds.XRefSectionMalformed);
        finding.Location.Position.Should().Be(stream);
    }

    [Theory]
    [InlineData("/XRefStm", "filter.failed xref.section-unreadable")]
    [InlineData("/Prev and /XRefStm", "xref.hybrid-stream-lost filter.failed xref.section-unreadable")]
    [InlineData("/XRefStm then /Prev", "filter.failed xref.section-unreadable xref.chain-cycle xref.rebuilt object.redefined")]
    public void What_decoding_a_cross_reference_stream_to_nothing_met_is_reported_once_whatever_names_the_stream(string naming, string codes)
    {
        // A hybrid file's update writes object 3 again and indexes it in a cross-reference stream whose Flate data decodes to
        // nothing, named through /XRefStm, through the /Prev and the /XRefStm of one table, or through the /XRefStm of the
        // newest table and the /Prev of the one before it. The encoded bytes were read as the stream's row, of a reserved type,
        // and object 3 read as the null object (#221). The data holds no row: the stream is reported once, where the chain
        // first reads it — beside the naming its table's /XRefStm makes of the offset its /Prev names, or before a /Prev that
        // names it again ends the chain in a loop (#156) —, the numbers it declares are refused to the older sections' rows,
        // and the rebuild serves object 3's new revision. What decoding met is reported once.
        var (file, stream) = UndecodedStreamFiles.NamedByTables(naming);

        using var document = PdfDocument.Open(file);

        string.Join(' ', document.Diagnostics.Select(diagnostic => diagnostic.Code)).Should().Be(codes);
        document.Diagnostics.Single(diagnostic => diagnostic.Code == PdfDiagnosticCodes.XRefSectionUnreadable).Message.Should().StartWith(
            string.Create(
                CultureInfo.InvariantCulture,
                $"The cross-reference stream {(naming == "/Prev and /XRefStm" ? "/Prev" : "/XRefStm")} names at offset {stream} is there but cannot be read: its data could not be decoded."));
        Text(document, 3).Should().Be("new three");
        document.WasRepaired.Should().BeTrue();
        document.Diagnostics.Count(diagnostic => diagnostic.Code == PdfDiagnosticCodes.FilterFailed).Should().Be(1);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(3, 7)]
    [InlineData(8, 7)]
    [InlineData(4, 7)]
    [InlineData(7, 4, 7)]
    public void An_object_the_lost_stream_redefines_is_read_in_its_new_revision_whatever_was_read_before(params int[] order)
    {
        // Object 7 is written in the oldest revision and again in the stream's, whose rows cannot be read. The oldest table's
        // row for it describes the older revision: were it taken, object 7 would read as the old one, and as the new one once
        // a rebuild some other object set off had run. The stream declares 7: no older row stands for it, and it is found by
        // rebuilding the index, which keeps the last definition the file writes, whatever was read before (#208).
        var (file, _, _, _) = LostStreamFiles.ThreeRevisions();

        using var document = PdfDocument.Open(file);

        document.Reader.Structure.Sections.Should().HaveCount(3, "the chain goes on past the stream");

        foreach (var number in order)
        {
            Text(document, number).Should().Be(number switch
            {
                3 => "only in the oldest",
                4 => "only in the stream",
                7 => "new seven",
                _ => "old eight",
            });
        }

        document.WasRepaired.Should().BeTrue();
    }

    [Fact]
    public void A_lost_stream_past_one_that_refused_every_number_says_the_chain_goes_on_through_its_prev()
    {
        // Two streams whose rows cannot be read are chained: the newer numbers its rows from object -1, which refuses every
        // number to the older sections' rows (#208). The older keeps no row, whatever was refused before the chain came to it:
        // it says the chain goes on through its /Prev, every object the sections read before it do not index being looked
        // for by rebuilding the index, and not that none of the rows read before its fault stands, as a table does (#210).
        var file = LostStreamFiles.Chain(2, rows: 5, objects: [], s => s == 0 ? [(-1L, 1L)] : [(3L, 2L)]);

        using var document = PdfDocument.Open(file);

        var lost = document.Diagnostics.Where(d => d.Code == PdfDiagnosticCodes.XRefSectionUnreadable).ToList();
        lost.Should().HaveCount(2);
        lost.Should().AllSatisfy(report => report.Message.Should().EndWith(
            "The chain goes on through its /Prev; every object no section read before it indexes is looked for by rebuilding the index, rather than taken from an older section's rows."));
        document.Reader.Structure.Sections[2].EveryNumberRefusedBefore.Should().BeTrue();
        document.Reader.ChainIndex!.TryGet(3, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("/Index /X", null)]
    [InlineData("/Index [4]", null)]
    [InlineData("/Index [4 2 7 9 0 R]", "1")]
    [InlineData("/Index 9 0 R", "[4 2 7 1]")]
    public void A_lost_stream_whose_numbering_cannot_be_read_leaves_no_older_row_standing(string index, string? nine)
    {
        // The stream's /Index is no array of pairs, or is written as a reference, or holds one, which ISO 32000-1 makes
        // direct: which numbers its rows give is unknown, and no older row may stand for any. The oldest table is read all
        // the same, its trailer merged and its rows judged, but every object it alone indexes is found by rebuilding the
        // index, the catalog first, at once (#208).
        var (file, oldest, stream, _) = LostStreamFiles.ThreeRevisions(
            LostStreamFiles.Unreadable.Replace("/Index [4 2 7 1]", index, StringComparison.Ordinal), nine: nine);

        using var document = PdfDocument.Open(file);

        document.Diagnostics[0].Message.Should().EndWith(
            "The chain goes on through its /Prev; every object no section read before it indexes is looked for by rebuilding the index, rather than taken from an older section's rows.");
        document.Reader.Structure.Sections.Select(section => section.Offset).Should().Equal(document.Reader.Structure.Sections[0].Offset, stream, oldest);
        var chain = document.Reader.ChainIndex!;
        chain.TryGet(3, out _).Should().BeFalse("no older row stands");
        chain.TryGet(8, out _).Should().BeFalse("no older row stands, whatever the stream declares");
        chain.TryGet(6, out _).Should().BeTrue("the newest table's rows stand");
        document.WasRepaired.Should().BeTrue("the catalog is the oldest table's alone");
        Text(document, 3).Should().Be("only in the oldest");
        Text(document, 7).Should().Be("new seven");
    }

    [Theory]
    [InlineData("/Size 8", new[] { 0, 6, 8 })]
    [InlineData("/Size 8 /Index null", new[] { 0, 6, 8 })]
    [InlineData("/Size 0", new[] { 0, 1, 2, 3, 6, 7, 8 })]
    [InlineData("/Size 9 0 R", new[] { 0, 6, 9 })]
    [InlineData("", new[] { 0, 6 })]
    public void A_lost_stream_without_an_index_declares_the_numbers_below_its_size(string size, int[] standing)
    {
        // Without an /Index, or with one that is null, a stream's rows number the objects from 0 to its /Size less one: no
        // older row stands for those, and the oldest table's row for object 8 stands past a /Size of 8. A /Size written as a
        // reference — to an 8, which would leave object 8's row standing were it read —, or none, leaves the numbers unknown,
        // and no older row stands (#208).
        var (file, _, _, _) = LostStreamFiles.ThreeRevisions(
            LostStreamFiles.Unreadable.Replace("/Size 9 /Index [4 2 7 1]", size, StringComparison.Ordinal),
            nine: size.Contains(" R", StringComparison.Ordinal) ? "8" : null);

        using var document = PdfDocument.Open(file);

        document.Reader.Structure.Sections.Should().HaveCount(3);
        document.Reader.ChainIndex!.CopyNumbers().Should().BeEquivalentTo(standing);
    }

    [Theory]
    [InlineData(1, 4096, false)]
    [InlineData(1, 4097, true)]
    [InlineData(2, 2048, false)]
    [InlineData(2, 2049, true)]
    public void Lost_streams_keep_their_numbers_apart_up_to_4096_ranges_and_refuse_every_number_past_them(
        int streams, int each, bool every)
    {
        // Each stream declares one-row subsections of its own, every other number from 100, the streams' interleaved. Up to
        // 4,096 ranges, the numbers they declare are kept apart, and an older row stands for any other; past them, every
        // number is refused, as where the numbering cannot be read, and what the older table alone indexes is found by
        // rebuilding the index: what a refusal costs stays bounded (#208).
        var file = LostStreamFiles.Chain(
            streams,
            rows: 100 + (2 * streams * each) + 2,
            objects: [100, 101, 100 + (2 * streams * each) - 2, 100 + (2 * streams * each) - 1],
            s => Enumerable.Range(0, each).Select(i => (100L + (2 * s) + (2L * streams * i), 1L)));

        using var document = PdfDocument.Open(file);

        var index = document.Reader.ChainIndex!;
        index.TryGet(3, out _).Should().Be(!every, "object 3 is no stream's");
        index.TryGet(101, out _).Should().Be(!every, "no stream declares 101");
        index.TryGet(100 + (2 * streams * each) - 1, out _).Should().Be(!every, "no stream declares the number past the last");
        index.TryGet(100, out _).Should().BeFalse("the newest stream declares 100");
        index.TryGet(100 + (2 * streams * each) - 2, out _).Should().BeFalse("the oldest stream declares the last number");
        document.Diagnostics.Select(d => d.Code).Should().StartWith(Enumerable.Repeat(PdfDiagnosticCodes.XRefSectionUnreadable, streams));
        document.Diagnostics[streams - 1].Message.Should().Contain(every ? "every object no section read before it indexes" : "the objects it numbers");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Subsections_that_meet_are_one_range_and_one_of_no_rows_is_none(int step)
    {
        // A stream declares 4,096 one-row subsections every other number, beside one of no rows, or 5,000 one after the other
        // from 100, which meet: 4,096 ranges at most either way, its numbers kept apart, and object 3's row stands (#208).
        var subsections = step == 1
            ? Enumerable.Range(0, 5000).Select(i => (100L + i, 1L))
            : Enumerable.Range(0, 4096).Select(i => (100L + (2L * i), 1L)).Prepend((7L, 0L));
        var file = LostStreamFiles.Chain(1, rows: 100 + (2 * 5000), objects: [7, 99, 5100], _ => subsections);

        using var document = PdfDocument.Open(file);

        var index = document.Reader.ChainIndex!;
        index.TryGet(3, out _).Should().BeTrue();
        index.TryGet(7, out _).Should().BeTrue("a subsection of no rows declares no number");
        index.TryGet(99, out _).Should().BeTrue();
        index.TryGet(100, out _).Should().BeFalse();
    }

    [Fact]
    public void An_older_row_stands_for_a_number_exactly_where_no_lost_stream_declares_it()
    {
        // One to three lost streams, each declaring up to six subsections anywhere among the oldest table's numbers, which
        // overlap, meet, nest or stand apart, in any order: an older row stands for a number exactly where none declares it,
        // however the ranges were sorted and joined (#208).
        var cases =
            from streams in Gen.Choose(1, 3)
            from declared in Gen.Choose(3, 60).SelectMany(first => Gen.Choose(0, 8).Select(count => (First: (long)first, Count: (long)count)))
                .ArrayOf().Select(pairs => pairs.Take(6).ToArray()).ArrayOf(streams)
            select declared;

        Check.One(Properties, Prop.ForAll(cases.ToArbitrary(), declared =>
        {
            var file = LostStreamFiles.Chain(
                declared.Length,
                rows: 70,
                objects: [.. Enumerable.Range(4, 66)],
                s => declared[s].Length == 0 ? [(3, 0)] : declared[s]);

            using var document = PdfDocument.Open(file);
            var index = document.Reader.ChainIndex!;

            for (var number = 3; number < 70; number++)
            {
                var refused = declared.Any(pairs => pairs.Any(pair => number >= pair.First && number < pair.First + pair.Count));
                index.TryGet(number, out _).Should().Be(!refused, "object {0} is {1}declared", number, refused ? string.Empty : "not ");
            }
        }));
    }

    [Fact]
    public void A_stream_with_no_prev_or_no_data_or_no_type_hands_nothing_on()
    {
        // The chain goes on only through what a stream of /Type /XRef, its dictionary read whole and followed by its data,
        // names through /Prev: one that names nothing ends it as before, as does a dictionary no data follows, which is no
        // stream's, and an object that only gives a /W, which may be anything (#263). The catalog, which the oldest table
        // alone indexes, is found by rebuilding the index.
        foreach (var (file, fault) in new[]
        {
            (LostStreamFiles.ThreeRevisions(prev: string.Empty).File, "its /W gives a field a negative width"),
            (LostStreamFiles.ThreeRevisions(data: false).File, "its dictionary is not followed by stream data"),
            (LostStreamFiles.ThreeRevisions(LostStreamFiles.Unreadable.Replace("/Type /XRef ", string.Empty, StringComparison.Ordinal)).File, "its /W gives a field a negative width"),
        })
        {
            using var document = PdfDocument.Open(file);

            document.Reader.Structure.Sections.Should().HaveCount(2);
            document.Reader.Structure.Sections[1].PreviousHandedOn.Should().BeFalse();
            document.Diagnostics[0].Message.Should().EndWith(
                $": {fault}. The rows read before the fault, if any, were kept; the objects only the rest indexes are looked for by rebuilding the index.");
            document.WasRepaired.Should().BeTrue();
        }
    }

    [Fact]
    public void A_stream_a_guard_cut_hands_nothing_on()
    {
        // The stream's /W is the reference 9 0 R, and MaxObjectLength cuts object 9: the section is the guard's, which ends
        // the chain there, and the oldest table is not read past it, whatever the stream's /Prev names (ADR 34, #208).
        var (file, _, stream, _) = LostStreamFiles.ThreeRevisions(
            LostStreamFiles.Unreadable.Replace("/W [1 -4 2]", "/W 9 0 R", StringComparison.Ordinal), nine: "%" + new string('-', 2000) + "\n[1 4 2]");

        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = 1024 } });

        document.Diagnostics.Select(d => d.Code).Should().Equal(PdfDiagnosticCodes.LimitObject);
        document.Reader.Structure.Sections.Should().HaveCount(2);
        document.Reader.Structure.Sections[1].Offset.Should().Be(stream);
        document.Reader.Structure.Sections[1].CutByLimit.Should().BeTrue();
        document.GetObject(new PdfObjectId(3)).Should().BeSameAs(PdfNull.Instance, "what only the older sections index is the guard's to hide");
    }

    [Fact]
    public void The_prev_a_lost_stream_hands_on_is_followed_under_the_section_count_guard()
    {
        // Read under MaxXRefSectionCount 2, the section the stream's /Prev names is the third: the guard ends the chain there,
        // and the objects the stream lost, the catalog among them, are found by rebuilding the index, as for any section lost
        // before a guard (#208).
        var (file, oldest, stream, _) = LostStreamFiles.ThreeRevisions();

        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxXRefSectionCount = 2 } });

        document.Diagnostics.Select(d => d.Code).Should().StartWith([PdfDiagnosticCodes.XRefSectionUnreadable, PdfDiagnosticCodes.LimitXRefSectionCount]);
        document.Diagnostics[0].Position.Should().Be(stream);
        document.Diagnostics[1].Position.Should().Be(oldest);
        document.Reader.Structure.Sections.Should().HaveCount(2);
        Text(document, 3).Should().Be("only in the oldest");
    }

    [Theory]
    [InlineData(3, "3 bytes before it")]
    [InlineData(-3, "3 bytes after it")]
    public void The_prev_a_lost_stream_hands_on_finds_its_section_near_where_it_names_it(int error, string distance)
    {
        // The stream's /Prev names a place a few bytes off the oldest table: it is found there, as any section named a few
        // bytes off is, and object 3 needs no rebuild (#208).
        var oldest = LostStreamFiles.ThreeRevisions().Oldest;
        var (file, _, stream, _) = LostStreamFiles.ThreeRevisions(prev: (oldest + error).ToString(CultureInfo.InvariantCulture));

        using var document = PdfDocument.Open(file);

        document.Diagnostics.Select(d => d.Code).Should().Equal(PdfDiagnosticCodes.XRefSectionUnreadable, PdfDiagnosticCodes.XRefOffsetAdjusted);
        document.Diagnostics[1].Message.Should().Be(string.Create(
            CultureInfo.InvariantCulture, $"The cross-reference section /Prev names at offset {oldest + error} was found {distance}."));
        document.Reader.Structure.Sections[2].NamedFrom.Should().Be(stream);
        Text(document, 3).Should().Be("only in the oldest");
        document.WasRepaired.Should().BeFalse();
    }

    [Theory]
    [InlineData(false, "where the chain has already reached a section it could not read")]
    [InlineData(true, "which the chain has already read")]
    public void The_prev_a_lost_stream_hands_on_ends_the_chain_in_a_loop_where_it_names_what_the_chain_reached(bool newest, string reached)
    {
        // The stream's /Prev names the stream itself, which the chain reached and could not read, or the newest table, which
        // it read: a loop, reported as one, in words that say which, and the objects the chain lost are found by rebuilding
        // the index (#188, #208).
        var named = newest ? LostStreamFiles.ThreeRevisions(prev: "{stream}").Newest : -1;
        var (file, _, stream, at) = LostStreamFiles.ThreeRevisions(prev: newest ? named.ToString(CultureInfo.InvariantCulture) : "{stream}");
        var looped = newest ? at : stream;
        looped.Should().Be(newest ? named : stream, "the newest table lies where it lay with the stream naming itself");

        using var document = PdfDocument.Open(file);

        document.Diagnostics.Select(d => d.Code).Should().StartWith([PdfDiagnosticCodes.XRefSectionUnreadable, PdfDiagnosticCodes.XRefChainCycle]);
        document.Diagnostics[1].Message.Should().Be(string.Create(
            CultureInfo.InvariantCulture,
            $"The cross-reference chain loops back on itself: the /Prev of the section at offset {stream} names offset {looped}, {reached}."));
        document.Reader.Structure.Sections.Should().HaveCount(2);
        Text(document, 3).Should().Be("only in the oldest");
    }

    [Theory]
    [InlineData("/Nowhere", false)]
    [InlineData("-5", false)]
    [InlineData("99 0 R", false)]
    [InlineData("99999999", true)]
    [InlineData("{oldest}", true)]
    [InlineData("9 0 R", true)]
    public void A_lost_stream_is_said_to_hand_its_prev_on_only_where_that_prev_names_an_offset_or_what_a_guard_hides(
        string prev, bool goesOn)
    {
        // The stream's /Prev is a name, a negative integer or a reference no section places: no offset, which the report
        // after it says, the chain ending there. The stream was said all the same to hand it on, the chain going on through
        // it (#208). An offset, in the file or outside it, is gone on to, and a reference that MaxObjectLength cuts, object 9
        // holding the oldest table's offset past a comment, is the guard's, which may hide a section.
        var oldest = LostStreamFiles.ThreeRevisions().Oldest;
        var (file, _, stream, _) = LostStreamFiles.ThreeRevisions(
            prev: prev, nine: prev == "9 0 R" ? "%" + new string('-', 2000) + "\n" + oldest.ToString(CultureInfo.InvariantCulture) : null);

        using var document = PdfDocument.Open(
            file, prev == "9 0 R" ? new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = 1024 } } : null);

        var lost = document.Diagnostics[0];
        lost.Code.Should().Be(PdfDiagnosticCodes.XRefSectionUnreadable);
        lost.Message.Should().Be(Invariant(
            $"The cross-reference stream /Prev names at offset {stream} is there but cannot be read: its /W gives a field a negative width. {(goesOn ? "The chain goes on through its /Prev; the objects" : "The objects")} it numbers that no section read before it indexes are looked for by rebuilding the index, rather than taken from an older section's rows."));
        document.Reader.Structure.Sections[1].PreviousHandedOn.Should().BeTrue();

        if (!goesOn)
        {
            document.Diagnostics[1].Code.Should().Be(PdfDiagnosticCodes.XRefSectionMissing);
            document.Diagnostics[1].Message.Should().Be(Invariant(
                $"The /Prev of the cross-reference section at offset {stream} is not an offset; the sections it names are looked for by rebuilding the index."));
        }
    }

    [Theory]
    [InlineData(0, "")]
    [InlineData(0, "junk\n")]
    [InlineData(1200, "")]
    public void A_table_prev_names_that_is_there_and_cannot_be_read_is_reported_where_it_is_not_found_in_the_table_naming_it(
        int gap, string junk)
    {
        // The first table holds a stray name where its trailer should start, and the update's, which names it, lies within
        // 512 bytes of it, or past a comment of 1,200 bytes, or the file starts with junk before its header. The update's
        // table was found there, read again, and its /Prev named the first table once more: a loop and a shift the file does
        // not have, the malformed table never reported (#188). The keyword is there: the table is reported where it is, its
        // rows kept, as when nothing is near it.
        var (file, malformed, update) = NearbyFiles.MalformedTableUnderAnUpdate(gap, junk);
        var name = PdfTemplate.OffsetOf(file, "/Bad");

        using var document = PdfDocument.Open(file);

        var report = document.Diagnostics.Should().ContainSingle(d => !d.Message.StartsWith("The PDF header starts", StringComparison.Ordinal)).Which;
        report.Code.Should().Be(PdfDiagnosticCodes.XRefSectionUnreadable);
        report.Position.Should().Be(malformed);
        report.Message.Should().StartWith(string.Create(
            CultureInfo.InvariantCulture,
            $"The cross-reference table /Prev names at offset {malformed} is there but cannot be read: it holds a name at offset {name}, where a subsection or the trailer should start."));
        document.Reader.Structure.Sections.Should().HaveCount(2);
        document.Reader.Structure.Sections[0].Offset.Should().Be(update);
        var section = document.Reader.Structure.Sections[1];
        section.State.Should().Be(XRefSectionState.Malformed);
        section.Offset.Should().Be(malformed);
        section.HighestNumber.Should().Be(2, "the rows read before the fault are its own");
        document.Catalog.GetDictionary(PdfName.Pages).Should().NotBeNull();
        document.WasRepaired.Should().BeFalse("the rows read before the fault index the catalog and the page tree");
    }

    [Theory]
    [InlineData(0, "")]
    [InlineData(0, "junk\n")]
    [InlineData(1500, "")]
    public void An_xrefstm_naming_an_object_of_another_type_near_its_table_is_missing_and_what_only_the_stream_indexes_is_found(
        int gap, string junk)
    {
        // The hybrid table's /XRefStm names object 4, a dictionary of /Type /Foo, within 512 bytes of the table, or 1,500 bytes
        // before it. The table itself was found there and read again in the stream's place: nothing was reported missing, and
        // the page, which only the stream would index, read as null with no rebuild (#188). The chain has read the table, which
        // is no place to find another section: the stream is missing, and the page is found by rebuilding the index.
        var (file, four, _) = NearbyFiles.HybridNamingAnotherObject(gap, junk);

        using var document = PdfDocument.Open(file);

        var report = document.Diagnostics.Should().ContainSingle(d => !d.Message.StartsWith("The PDF header starts", StringComparison.Ordinal)).Which;
        report.Code.Should().Be(PdfDiagnosticCodes.XRefSectionMissing);
        report.Position.Should().Be(four);
        report.Message.Should().StartWith(string.Create(
            CultureInfo.InvariantCulture,
            $"The cross-reference section /XRefStm names at offset {four} is not there, nor near it;"));
        document.WasRepaired.Should().BeFalse("nothing asked yet for what the stream indexed");
        document.GetObject(new PdfObjectId(3)).AsDictionary().IsOfType(PdfName.Page).Should().BeTrue();
        document.WasRepaired.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1200)]
    public void A_prev_naming_no_section_within_reach_of_the_table_naming_it_is_missing_not_a_loop(int gap)
    {
        // The only table names through /Prev a place inside the page tree's dictionary, within 512 bytes of the table or 1,200
        // bytes before it. The table was found there, read again, and named the place once more: a loop (#188). It is a section
        // the chain has read, and no other: the section /Prev names is missing.
        var (file, named, _) = NearbyFiles.TableNamingNoSection(gap);

        using var document = PdfDocument.Open(file);

        var report = document.Diagnostics.Should().ContainSingle().Which;
        report.Code.Should().Be(PdfDiagnosticCodes.XRefSectionMissing);
        report.Position.Should().Be(named);
        document.Reader.Structure.Sections.Should().HaveCount(2);
        document.Reader.Structure.LoopOffset.Should().Be(-1);
        document.GetObject(new PdfObjectId(3)).AsDictionary().IsOfType(PdfName.Page).Should().BeTrue();
        document.WasRepaired.Should().BeFalse("the table indexes every object");
    }

    [Theory]
    [InlineData(69)]
    [InlineData(1269)]
    public void A_prev_naming_a_place_between_two_tables_finds_the_one_the_chain_has_not_read(int after)
    {
        // /Prev names a place 115 bytes past the first table's keyword, and the update's table, which names it, starts 69
        // bytes past that place, or 1,269. The nearer table, the update's, was taken, read again and named the place once more,
        // and the first table was never read (#188). The update's is passed over, and the first is found where it lies.
        var (file, original, named, _) = NearbyFiles.UpdateNamingBetweenItsTables(115, after);

        using var document = PdfDocument.Open(file);

        var report = document.Diagnostics.Should().ContainSingle().Which;
        report.Code.Should().Be(PdfDiagnosticCodes.XRefOffsetAdjusted);
        report.Position.Should().Be(original);
        report.Message.Should().Be($"The cross-reference section /Prev names at offset {named} was found 115 bytes before it.");
        document.Reader.Structure.Sections[1].State.Should().Be(XRefSectionState.Relocated);
        document.GetObject(new PdfObjectId(3)).AsDictionary().IsOfType(PdfName.Page).Should().BeTrue();
        document.WasRepaired.Should().BeFalse();
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1200)]
    [InlineData(true, 0)]
    [InlineData(true, 1200)]
    public void A_section_that_is_there_and_cannot_be_read_is_not_taken_for_an_older_one_near_it(bool streams, int gap)
    {
        // The second section alone indexes object 4, and cannot be read: a table holding a stray name before its first
        // subsection, or a stream of /Type /XRef whose /W gives a name for a width. The first, which the chain has not read,
        // lies within 512 bytes of it, or 1,200 bytes more. It was taken for the second moved, and object 4 read as null with
        // nothing reported but a shift (#281). The keyword, or /Type /XRef, says the second is there: it is reported where it
        // is, and object 4 is found by rebuilding the index. The table's trailer, past the fault, is unknown, and the catalog,
        // indexed by the first alone, asks for the rebuild at once; the stream's /Prev, read whole, leads the chain on to the
        // first, which indexes the catalog (#208).
        var (file, first, second) = NearbyFiles.MalformedUpdateNearItsOriginal(streams, gap);

        using var document = PdfDocument.Open(file);

        if (streams)
        {
            document.Diagnostics.Select(d => d.Code).Should().Equal(PdfDiagnosticCodes.XRefSectionUnreadable);
            document.Reader.Structure.Sections.Should().HaveCount(3);
            document.Reader.Structure.Sections[2].Offset.Should().Be(first);
            document.WasRepaired.Should().BeFalse("the first section, read through the stream's /Prev, indexes the catalog");
        }
        else
        {
            document.Diagnostics.Select(d => d.Code).Should().Equal(PdfDiagnosticCodes.XRefSectionUnreadable, PdfDiagnosticCodes.XRefRebuilt);
        }

        document.Diagnostics[0].Position.Should().Be(second);
        document.Diagnostics[0].Message.Should().StartWith(string.Create(
            CultureInfo.InvariantCulture,
            $"The cross-reference {(streams ? "stream" : "table")} /Prev names at offset {second} is there but cannot be read:"));
        document.Reader.Structure.Sections[1].Offset.Should().Be(second);
        document.Reader.Structure.Sections[1].State.Should().Be(XRefSectionState.Malformed);
        Text(document, 4).Should().Be("four");
    }

    [Theory]
    [InlineData(false, 0, 1200, "")]
    [InlineData(false, 0, 0, "")]
    [InlineData(false, 1200, 1200, "")]
    [InlineData(true, 0, 1200, "")]
    [InlineData(true, 0, 0, "")]
    [InlineData(true, 1200, 1200, "")]
    [InlineData(true, 0, 1200, "+")]
    [InlineData(true, 0, 0, "+")]
    [InlineData(true, 1200, 1200, "+")]
    [InlineData(true, 0, 1200, "++")]
    [InlineData(true, 0, 1200, "+-")]
    public void A_section_that_cannot_be_read_nearest_to_where_its_offset_names_none_is_found_moved_not_passed_over(
        bool streams, int gap, int after, string sign)
    {
        // The third section's /Prev names the second 7 bytes short of it, where no section starts, and the second, which alone
        // indexes object 4 and cannot be read — a table holding a stray name, a stream of /Type /XRef whose /W gives a name for
        // a width, its number written with a sign, a run of signs that starts with a +, or none —, is the nearest place the
        // search finds. The first lies within 512 bytes of it, or 1,200 bytes more, and the third, which names it, within 512
        // bytes too, or 1,200 bytes past it. The second was passed over and the first taken in its place, object 4 read as
        // null with nothing reported but a shift; or, with nothing else near, the second was said to be not there, nor near
        // it — where its number is written with a sign, because the walk back from obj finds the header at its first digit,
        // where no token starts (#327). It declares itself a section: found moved, it is reported where it was found, at
        // that digit, as where it is named (#281). The stream's /Prev leads the chain on to the first (#208); the table's
        // trailer, past its fault, is unknown, and the catalog, indexed by the first alone, asks for the rebuild at once.
        // Object 4 is found by rebuilding the index.
        var (file, first, second) = NearbyFiles.MalformedUpdateNearItsOriginal(streams, gap, shortBy: 7, after: after, sign: sign);

        using var document = PdfDocument.Open(file);

        document.Diagnostics.Select(d => d.Code).Should().Equal(
            streams
                ? [PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefSectionUnreadable]
                : [PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefSectionUnreadable, PdfDiagnosticCodes.XRefRebuilt]);
        document.Diagnostics[0].Position.Should().Be(second);
        document.Diagnostics[0].Message.Should().Be(Invariant($"The cross-reference section /Prev names at offset {second - 7} was found 7 bytes after it."));
        document.Diagnostics[1].Position.Should().Be(second);
        document.Diagnostics[1].Message.Should().StartWith(Invariant(
            $"The cross-reference {(streams ? "stream" : "table")} /Prev names at offset {second - 7}, found at offset {second}, is there but cannot be read: "));
        var sections = document.Reader.Structure.Sections;
        sections.Select(section => (section.State, section.Offset)).Skip(1).Should().Equal(
            streams
                ? [(XRefSectionState.Malformed, second), (XRefSectionState.Read, first)]
                : [(XRefSectionState.Malformed, second)]);
        sections[1].NamedOffset.Should().Be(second - 7);
        sections[1].NumbersRefused.Should().Be(streams);
        document.Reader.Structure.LoopOffset.Should().Be(-1);
        Text(document, 4).Should().Be("four");
    }

    [Theory]
    [InlineData("%xref\n", 11)]
    [InlineData("% the xref section follows\n", 32)]
    [InlineData("6 0 obj\n(see xref)\nendobj\n", 31)]
    public void The_word_xref_in_a_comment_or_a_string_near_where_an_offset_names_no_section_is_no_section_found_moved(string between, int distance)
    {
        // The newest table's /Prev names a place inside the endobj before the update's table, past which a comment or a
        // string holds the word xref, nearer than the table. Read from that word, it is a table's keyword followed by what
        // no table holds: it was taken for the section found moved, which cannot be read, the chain ended there, and the
        // file was rebuilt as it opened. A load reads no keyword there, the line lexed from its start: the word is dropped
        // as any place that holds no section, and the search goes on to the update's table (#281).
        var (file, update, named) = NearbyFiles.UpdateBehindAStrayWord(between);
        using var document = PdfDocument.Open(file);

        var sections = document.Reader.Structure.Sections;
        sections[1].State.Should().Be(XRefSectionState.Relocated);
        sections[1].Offset.Should().Be(update);
        sections.Should().HaveCount(3).And.OnlyContain(section => section.State != XRefSectionState.Malformed);
        document.Diagnostics.Should().ContainSingle().Which.Message.Should().Be(Invariant(
            $"The cross-reference section /Prev names at offset {named} was found {distance} bytes after it."));
        Text(document, 3).Should().Be("three new");
        document.WasRepaired.Should().BeFalse();
    }

    [Theory]
    [InlineData("xref\n")]
    [InlineData(">>xref\n")]
    public void An_xref_keyword_a_load_reads_on_its_line_near_where_an_offset_names_no_section_is_the_section_found_moved(string between)
    {
        // The same file, with an xref keyword where the word was: at the start of its line, or after the end of a dictionary on
        // it, a token a load reads. What follows it is no table's: it is the section found moved, which cannot be read, and
        // ends the search, as the keyword ends it where an offset names it (#281).
        var (file, update, _) = NearbyFiles.UpdateBehindAStrayWord(between);
        var keyword = update - between.Length + between.IndexOf('x', StringComparison.Ordinal);
        using var document = PdfDocument.Open(file);

        var found = document.Reader.Structure.Sections[1];
        found.State.Should().Be(XRefSectionState.Malformed);
        found.Offset.Should().Be(keyword);
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().StartWith([PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefSectionUnreadable]);
    }

    [Theory]
    [InlineData("a comment", 100)]
    [InlineData("a comment", 700)]
    [InlineData("a string", 700)]
    public void The_word_xref_in_a_comment_or_a_string_whose_line_starts_before_the_bytes_searched_is_no_section_found_moved(string holder, int letters)
    {
        // The update frees object 4, and the line before object 6, inside whose endobj the newest table's /Prev names the
        // update's table, is a comment or a string holding, past some letters, the word xref, some 40 bytes before the place
        // named, nearer than the update's table, 65 bytes past it. Where the line starts more than 512 bytes before the
        // place, before the bytes searched near it, they were lexed from where they start, the comment or the string unseen:
        // the word was taken for the section found moved, which cannot be read, the chain ended there, and the rebuild that
        // followed served object 4's old revision, which the update freed. The line is lexed from its start, read again
        // before the bytes searched: the word is dropped, as on a short line, and the search goes on to the update's table
        // (#281).
        var line = holder == "a comment"
            ? "%" + new string('a', letters) + " xref see the table below\n"
            : "9 0 obj\n(" + new string('a', letters) + " xref see the table below)\nendobj\n";
        var (file, update, named) = NearbyFiles.UpdateAfterAStrayLine(line);
        using var document = PdfDocument.Open(file);

        var sections = document.Reader.Structure.Sections;
        sections[1].State.Should().Be(XRefSectionState.Relocated);
        sections[1].Offset.Should().Be(update);
        sections.Should().OnlyContain(section => section.State != XRefSectionState.Malformed);
        document.Diagnostics.Should().ContainSingle().Which.Message.Should().Be(Invariant(
            $"The cross-reference section /Prev names at offset {named} was found 65 bytes after it."));
        document.GetObject(new PdfObjectId(4)).Should().BeSameAs(PdfNull.Instance);
        document.WasRepaired.Should().BeFalse();
    }

    [Theory]
    [InlineData("% 8 0 obj << /Type /XRef >>\n")]
    [InlineData("10 0 obj\n(see 8 0 obj << /Type /XRef >> here)\nendobj\n")]
    [InlineData("x8 0 obj << /Type /XRef >>\n")]
    public void The_header_of_a_cross_reference_stream_in_a_comment_a_string_or_behind_a_letter_near_where_an_offset_names_no_section_is_no_section_found_moved(string between)
    {
        // The update frees object 3, and between where the newest table's /Prev names the update's table and where it is lies
        // a comment or a string holding the header of an object of /Type /XRef, or such a header whose number a letter runs
        // into. Read from its number, it is a cross-reference stream's dictionary that no stream data follows: it was taken
        // for the section found moved, which cannot be read, the chain ended there, and the rebuild that followed served
        // object 3's old revision, which the update freed. A load reads no header there, its line lexed from its start: it is
        // dropped as any place that holds no section is, and the search goes on to the update's table (#281).
        var (file, update, named) = NearbyFiles.UpdateBehindAStrayWord(between, deleted: true);
        using var document = PdfDocument.Open(file);

        var sections = document.Reader.Structure.Sections;
        sections[1].State.Should().Be(XRefSectionState.Relocated);
        sections[1].Offset.Should().Be(update);
        sections.Should().OnlyContain(section => section.State != XRefSectionState.Malformed);
        document.Diagnostics.Should().ContainSingle().Which.Message.Should().Be(Invariant(
            $"The cross-reference section /Prev names at offset {named} was found {update - named} bytes after it."));
        document.GetObject(new PdfObjectId(3)).Should().BeSameAs(PdfNull.Instance);
        document.WasRepaired.Should().BeFalse();
    }

    [Theory]
    [InlineData("% 8 0 obj << /Type /XRef >>\n")]
    [InlineData("10 0 obj\n(see 8 0 obj << /Type /XRef >> here)\nendobj\n")]
    public void An_xrefstm_falling_short_of_its_stream_past_a_stream_header_in_a_comment_or_a_string_reads_the_stream(string between)
    {
        // The update's /XRefStm names a place inside the endobj before its stream, past which a comment or a string holds the
        // header of an object of /Type /XRef, nearer than the stream. Taken for a stream found moved that cannot be read, it
        // was reported as what the /XRefStm names, and the oldest table's row served object 7's old revision. A load reads no
        // header there: the search goes on to the stream (#156, #281).
        var (file, stream, named) = NearbyFiles.HybridBehindAStrayWord(between);
        using var document = PdfDocument.Open(file);

        var read = document.Reader.Structure.Sections.Should().ContainSingle(section => section.IsHybridStream).Which;
        read.State.Should().Be(XRefSectionState.Relocated);
        read.Offset.Should().Be(stream);
        document.Diagnostics.Should().ContainSingle().Which.Message.Should().Be(Invariant(
            $"The cross-reference section /XRefStm names at offset {named} was found {stream - named} bytes after it."));
        Text(document, 7).Should().Be("new seven");
        document.WasRepaired.Should().BeFalse();
    }

    [Theory]
    [InlineData("8 % c\n0 obj << /Type /XRef >>\n", 0)]
    [InlineData("endobj 8 0 obj << /Type /XRef >>\n", 7)]
    [InlineData("+8 0 obj << /Type /XRef >>\n", 1)]
    [InlineData("++8 0 obj << /Type /XRef >>\n", 2)]
    [InlineData("+-8 0 obj << /Type /XRef >>\n", 2)]
    public void The_header_of_a_cross_reference_stream_a_load_reads_near_where_an_offset_names_no_section_is_the_section_found_moved(string between, int at)
    {
        // The same file, with the header of an object of /Type /XRef where the comment was: a comment between its number and
        // its generation, or after a stray endobj on its line, or its number written with a sign or a run of signs that
        // starts with a +, a header a load reads. No stream data follows its dictionary: it is the section found moved,
        // which cannot be read, and ends the search, as it ends it where an offset names it (#281). The walk back from obj
        // finds a signed header at its first digit, where it is read and found: its line was asked there whether a token
        // starts, though the lexer's token starts at the sign, and it was dropped for the update's table (#327).
        var (file, update, _) = NearbyFiles.UpdateBehindAStrayWord(between);
        using var document = PdfDocument.Open(file);

        var found = document.Reader.Structure.Sections[1];
        found.State.Should().Be(XRefSectionState.Malformed);
        found.Offset.Should().Be(update - between.Length + at);
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().StartWith([PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefSectionUnreadable]);
    }

    [Theory]
    [InlineData("8 0 obj", 512, true)]
    [InlineData("+8 0 obj", 512, true)]
    [InlineData("++8 0 obj", 512, true)]
    [InlineData("8 0 obj", 513, false)]
    [InlineData("+8 0 obj", 513, false)]
    [InlineData("++8 0 obj", 513, false)]
    public void A_header_that_cannot_be_read_is_a_section_found_moved_only_where_its_token_starts_within_the_bytes_searched(
        string header, int past, bool found)
    {
        // The newest table's /Prev names a place inside a comment, 512 or 513 bytes past a line that holds the header of an
        // object of /Type /XRef, which no stream data follows, its number written with a sign, a run of signs, or none; no
        // other section lies within reach. The bytes searched near the place start 512 bytes before it: a header whose
        // first byte lies there, a sign or a digit, is the section found moved; one whose first byte lies before them, in
        // the byte the window holds to tell a longer token, counts for nothing, as one whose digits start there does, and
        // the section is reported missing. The header the walk back from obj finds at a digit within the bytes searched
        // was asked there whether a token starts, and a signed one at the first byte searched dropped (#197, #327).
        var (file, at, named) = NearbyFiles.StrayHeaderBeforeTheBytesSearched(header, past);
        var number = at + header.AsSpan().IndexOfAnyInRange('0', '9');
        using var document = PdfDocument.Open(file);

        var section = document.Reader.Structure.Sections[1];

        if (found)
        {
            section.State.Should().Be(XRefSectionState.Malformed);
            section.Offset.Should().Be(number);
            document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(
                PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefSectionUnreadable, PdfDiagnosticCodes.XRefRebuilt);
            document.Diagnostics[0].Message.Should().Be(Invariant($"The cross-reference section /Prev names at offset {named} was found {named - number} bytes before it."));
        }
        else
        {
            section.State.Should().Be(XRefSectionState.NotFound);
            document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(PdfDiagnosticCodes.XRefSectionMissing, PdfDiagnosticCodes.XRefRebuilt);
        }

        Text(document, 3).Should().Be("three");
    }

    [Theory]
    [InlineData("Notes on the file format.\n", "xref")]
    [InlineData("Notes on the file format.\nThe ", "xref")]
    [InlineData("Example:\n", "12 0 obj\n<< /Type /XRef /Size 13 /W [1 2 1] >>\nendobj\n")]
    [InlineData("Notes on the file format.\n", "text")]
    public void The_word_xref_or_a_cross_reference_stream_s_header_in_stream_data_short_of_where_a_prev_names_is_taken_for_the_section_found_moved(
        string before, string word)
    {
        // Object 4, an embedded file the update writes uncompressed, holds in its data the word xref, at the start of a line
        // or in its middle, or the header and dictionary of an object of /Type /XRef, and the newest table's /Prev names the
        // place 2 bytes past where it starts, short of the update's table. No load lexes an embedded file's data, but the
        // search near the offset lexes the place's line alone, from its start, and sees no stream data an earlier line opened
        // (#171): the word, or the header, is a token there, taken for the section found moved, which cannot be read and ends
        // the search, the update's table left unread. The chain ends there, and the index is rebuilt as the document opens,
        // for the catalog, which only the first table indexes: object 6, which the update freed, is read as the rebuild finds
        // it. With no such word in the data, the search goes on to the update's table, and object 6 is free.
        var (file, at, named, update) = NearbyFiles.UpdatePastAWordInStreamData(before, word, StreamDataAfterTheWord);
        using var document = PdfDocument.Open(file);

        var found = document.Reader.Structure.Sections[1];

        if (word == "text")
        {
            (found.State, found.Offset).Should().Be((XRefSectionState.Relocated, update));
            document.Diagnostics.Should().ContainSingle().Which.Message.Should().Be(Invariant(
                $"The cross-reference section /Prev names at offset {named} was found {update - named} bytes after it."));
            document.GetObject(new PdfObjectId(6)).Should().BeSameAs(PdfNull.Instance);
            document.WasRepaired.Should().BeFalse();
            return;
        }

        (found.State, found.Kind, found.NamedOffset, found.Offset).Should().Be(
            (XRefSectionState.Malformed, word == "xref" ? XRefSectionKind.Table : XRefSectionKind.Stream, named, at));
        document.WasRepaired.Should().BeTrue("the catalog is the first table's alone");
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(
            PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefSectionUnreadable, PdfDiagnosticCodes.XRefRebuilt);
        document.Diagnostics[0].Message.Should().Be(Invariant($"The cross-reference section /Prev names at offset {named} was found 2 bytes before it."));
        document.Diagnostics[1].Position.Should().Be(at);
        Text(document, 6).Should().Be("six, freed by the update");
    }

    [Theory]
    [InlineData("12 0 obj\n<< /Type /XRef /Size 13 /W [1 2 1] >>\nendobj\n")]
    [InlineData("text")]
    public void A_cross_reference_stream_s_header_in_stream_data_short_of_where_an_xrefstm_names_is_taken_for_the_stream_found_moved(string word)
    {
        // As above, through the /XRefStm of the update's table, which falls short of the update's cross-reference stream, 2
        // bytes past where the header and dictionary of an object of /Type /XRef start in the data of the embedded file the
        // update writes before it: the header is taken for the stream found moved, which cannot be read (#171). A dictionary
        // no stream data follows refuses no number to the older sections' rows: the first table's row for object 3, which the
        // update wrote again, stands, and object 3, read before anything sets a rebuild off, is read as the first revision
        // wrote it. With no header in the data, the search goes on to the stream, and object 3 reads as the update wrote it.
        var (file, at, named, stream) = NearbyFiles.HybridPastAWordInStreamData("Example:\n", word, StreamDataAfterTheWord);
        using var document = PdfDocument.Open(file);

        var found = document.Reader.Structure.Sections[1];
        found.NamedBy.Should().Be("/XRefStm");

        if (word == "text")
        {
            (found.State, found.Kind, found.Offset).Should().Be((XRefSectionState.Relocated, XRefSectionKind.Stream, stream));
            document.Diagnostics.Should().ContainSingle().Which.Message.Should().Be(Invariant(
                $"The cross-reference section /XRefStm names at offset {named} was found {stream - named} bytes after it."));
            Text(document, 3).Should().Be("three new");
            document.WasRepaired.Should().BeFalse();
            return;
        }

        (found.State, found.Kind, found.NamedOffset, found.Offset, found.NumbersRefused).Should().Be(
            (XRefSectionState.Malformed, XRefSectionKind.Stream, named, at, false));
        Text(document, 3).Should().Be("three old");
        document.WasRepaired.Should().BeFalse();
        document.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Position)).Should().Equal(
            (PdfDiagnosticCodes.XRefOffsetAdjusted, at), (PdfDiagnosticCodes.XRefSectionUnreadable, at));
    }

    [Theory]
    [InlineData("<< /Notes (Notes on the\n", "xref", true)]
    [InlineData("<< /Notes (Notes on the\nThe ", "xref", true)]
    [InlineData("<< /Notes (Example:\n", "12 0 obj << /Type /XRef /Size 13 /W [1 2 1] >> endobj", true)]
    [InlineData("<< /Notes (Notes on the ", "xref", false)]
    public void The_word_xref_or_a_cross_reference_stream_s_header_in_a_string_short_of_where_a_prev_names_is_taken_for_the_section_found_moved_only_where_an_earlier_line_opened_the_string(
        string before, string word, bool taken)
    {
        // Object 4, which the update writes, is a dictionary whose string holds the word xref, at the start of a line or in
        // its middle, or the header and dictionary of an object of /Type /XRef, and the newest table's /Prev names the place
        // 2 bytes past where it starts, short of the update's table. A load lexes the string whole, but the search near the
        // offset lexes the place's line alone, from its start, and sees no string an earlier line opened (#171): the word, or
        // the header, is a token there, taken for the section found moved, which cannot be read and ends the search, the
        // update's table left unread. The chain ends there, and the index is rebuilt as the document opens, for the catalog,
        // which only the first table indexes: object 6, which the update freed, is read as the rebuild finds it. With the
        // string opened on the word's own line, the word is in it, the search goes on to the update's table, and object 6
        // is free.
        var (file, at, named, update) = NearbyFiles.UpdatePastAWordInAnObject(before, word, " tables) >>");
        using var document = PdfDocument.Open(file);

        var found = document.Reader.Structure.Sections[1];

        if (!taken)
        {
            (found.State, found.Offset).Should().Be((XRefSectionState.Relocated, update));
            document.Diagnostics.Should().ContainSingle().Which.Message.Should().Be(Invariant(
                $"The cross-reference section /Prev names at offset {named} was found {update - named} bytes after it."));
            document.GetObject(new PdfObjectId(6)).Should().BeSameAs(PdfNull.Instance);
            document.WasRepaired.Should().BeFalse();
            return;
        }

        (found.State, found.Kind, found.NamedOffset, found.Offset).Should().Be(
            (XRefSectionState.Malformed, word == "xref" ? XRefSectionKind.Table : XRefSectionKind.Stream, named, at));
        document.WasRepaired.Should().BeTrue("the catalog is the first table's alone");
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(
            PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefSectionUnreadable, PdfDiagnosticCodes.XRefRebuilt);
        document.Diagnostics[0].Message.Should().Be(Invariant($"The cross-reference section /Prev names at offset {named} was found 2 bytes before it."));
        document.Diagnostics[1].Position.Should().Be(at);
        Text(document, 6).Should().Be("six, freed by the update");
    }

    [Theory]
    [InlineData("an attached file's own table")]
    [InlineData("an attached file's own table giving a /Prev")]
    [InlineData("a cross-reference stream the data quotes, read in part")]
    public void A_section_in_stream_data_that_reads_short_of_where_a_prev_names_is_taken_and_the_chain_goes_on_through_its_trailer(string quoted)
    {
        // Object 4, an embedded file the update writes uncompressed, holds in its data an attached file whose own table
        // reads — its trailer naming no older section, or a /Prev near the first table —, or a cross-reference stream it
        // quotes, whose data holds fewer rows than it declares and whose /Prev lies near the first table too; the newest
        // table's /Prev names the place 2 bytes past where it starts, short of the update's table. A place that reads, whole
        // or in part, is taken before any line is lexed, whatever its line is: it is the section found moved, and the chain
        // goes on through its trailer, the update's table left unread; telling stream data apart would have to be asked of
        // such a place too (#171). The chain ends at the attached table, and the index is rebuilt as the document opens,
        // for the catalog, which only the first table indexes; or it goes on to the first table, found near the /Prev the
        // data gives, and object 4, which only the update's table indexes, is null — but where the stream read in part
        // refuses the numbers it declares, the catalog's among them, and the rebuild that follows finds it. Object 6, which
        // the update freed, is read whichever way: as the rebuild finds it, or as the first table's row gives it.
        var (before, word, after, kind) = quoted switch
        {
            "an attached file's own table" => (
                "%PDF-1.3\n%attached original\n",
                "xref",
                "\n0 1\n0000000000 65535 f \ntrailer\n<< /Size 1 >>\nstartxref\n28\n%%EOF\n",
                XRefSectionKind.Table),
            "an attached file's own table giving a /Prev" => (
                "%PDF-1.3\n",
                "xref",
                "\n0 1\n0000000000 65535 f \ntrailer\n<< /Size 1 /Prev 12 >>\n%%EOF\n",
                XRefSectionKind.Table),
            _ => (
                "Example:\n",
                "12 0 obj",
                "\n<< /Type /XRef /Size 13 /W [1 2 1] /Prev 9 /Length 3 >>\nstream\nabc\nendstream\nendobj\n",
                XRefSectionKind.Stream),
        };
        var (file, at, named, update) = NearbyFiles.UpdatePastAWordInStreamData(before, word, after);
        var first = PdfTemplate.OffsetOf(file, "xref");
        using var document = PdfDocument.Open(file);

        var sections = document.Reader.Structure.Sections;
        (sections[1].State, sections[1].Kind, sections[1].NamedOffset, sections[1].Offset).Should().Be(
            (XRefSectionState.Relocated, kind, named, at));
        sections.Should().NotContain(section => section.Offset == update, "the update's table is never read");
        document.Diagnostics[0].Message.Should().Be(Invariant($"The cross-reference section /Prev names at offset {named} was found 2 bytes before it."));
        Text(document, 6).Should().Be("six, freed by the update");

        switch (quoted)
        {
            case "an attached file's own table":
                sections.Should().HaveCount(2);
                document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(
                    PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefRebuilt);
                document.WasRepaired.Should().BeTrue("the catalog is the first table's alone");
                document.GetObject(new PdfObjectId(4)).Should().BeOfType<PdfStream>();
                break;
            case "an attached file's own table giving a /Prev":
                (sections.Count, sections[2].State, sections[2].Offset).Should().Be((3, XRefSectionState.Relocated, first));
                document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(
                    PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefOffsetAdjusted);
                document.WasRepaired.Should().BeFalse();
                document.GetObject(new PdfObjectId(4)).Should().BeSameAs(PdfNull.Instance);
                break;
            default:
                (sections.Count, sections[1].Incomplete, sections[2].State, sections[2].Offset).Should().Be(
                    (3, true, XRefSectionState.Relocated, first));
                document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(
                    PdfDiagnosticCodes.XRefOffsetAdjusted,
                    PdfDiagnosticCodes.XRefSectionUnreadable,
                    PdfDiagnosticCodes.XRefOffsetAdjusted,
                    PdfDiagnosticCodes.XRefRebuilt);
                document.WasRepaired.Should().BeTrue("the stream refused the numbers it declares, the catalog's among them");
                document.GetObject(new PdfObjectId(4)).Should().BeOfType<PdfStream>();
                break;
        }
    }

    [Fact]
    public void An_xref_keyword_on_a_line_that_starts_before_the_bytes_searched_is_lexed_from_the_line_s_start()
    {
        // The newest table's /Prev names a place inside object 4, an array of 400 zeros written on one line, 50 bytes before
        // the xref keyword that follows its endobj on that line, and no table after it. The line starts more than 512 bytes
        // before the place, before the bytes searched near it: it is lexed from its start, read again before them, and the
        // keyword, a token there, is the section found moved, which cannot be read (#281).
        var file = new HandWrittenFile();
        var catalog = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        var pages = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        file.Table(3, new() { [1] = catalog, [2] = pages }, "/Size 3 /Root 1 0 R");
        file.Raw("%" + new string('-', 1200) + "\n");
        var line = "4 0 obj [" + string.Concat(Enumerable.Repeat("0 ", 400)) + "] endobj xref 0 1 bad\n";
        var keyword = file.Position + line.IndexOf("xref", StringComparison.Ordinal);
        file.Raw(line);
        file.Raw("%" + new string('-', 1200) + "\n");
        file.Table(3, new() { [1] = catalog, [2] = pages }, Invariant($"/Size 3 /Root 1 0 R /Prev {keyword - 50}"));
        using var document = PdfDocument.Open(file.Bytes);

        var found = document.Reader.Structure.Sections[1];
        found.State.Should().Be(XRefSectionState.Malformed);
        found.Offset.Should().Be(keyword);
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().StartWith([PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefSectionUnreadable]);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void An_xref_keyword_on_a_long_line_is_a_section_found_moved_only_where_its_line_starts_within_reach_of_the_bytes_searched(
        int farther, bool found)
    {
        // The same file, whose line holds 4,640 spaces in object 4's array, or one more: the end of line before it lies 4,096
        // bytes before the bytes searched near the place named, or 4,097. The line is lexed from its start where that end of
        // line lies within 4,096 bytes of them, and the keyword, a token there, is the section found moved; farther back,
        // the place declares nothing, and is dropped as any place that holds no section is (#281).
        var file = new HandWrittenFile();
        var catalog = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        var pages = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        file.Table(3, new() { [1] = catalog, [2] = pages }, "/Size 3 /Root 1 0 R");
        file.Raw("%" + new string('-', 1200) + "\n");
        var line = "4 0 obj [" + new string(' ', 4640 + farther) + "] endobj xref 0 1 bad\n";
        var lineStart = file.Position;
        var keyword = lineStart + line.IndexOf("xref", StringComparison.Ordinal);
        file.Raw(line);
        file.Raw("%" + new string('-', 1200) + "\n");
        file.Table(3, new() { [1] = catalog, [2] = pages }, Invariant($"/Size 3 /Root 1 0 R /Prev {keyword - 50}"));
        var searched = keyword - 50 - PdfFileReader.NearbySearchRadius - 1;
        (searched - (lineStart - 1)).Should().Be(4096 + farther, "the end of line before the line lies that far before the bytes searched");
        using var document = PdfDocument.Open(file.Bytes);

        var section = document.Reader.Structure.Sections[1];
        section.State.Should().Be(found ? XRefSectionState.Malformed : XRefSectionState.NotFound);
        section.Offset.Should().Be(found ? keyword : keyword - 50);
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Contains(PdfDiagnosticCodes.XRefSectionUnreadable).Should().Be(found);
    }

    [Theory]
    [InlineData("at the first byte searched")]
    [InlineData("at the file's first byte")]
    public void An_xref_keyword_is_lexed_from_where_its_line_starts_at_the_first_byte_searched_or_at_the_file_s_first(string where)
    {
        // The newest table's /Prev names a place where no section lies, 512 bytes past an xref keyword that starts a line
        // past a comment of 5,000 bytes, the bytes searched near the place starting at the end of line before it; or 50
        // bytes before an xref keyword that follows an endobj on the first line of a file with no header, 618 bytes long.
        // What follows the keyword is no table's: it is the section found moved, its line lexed from where it starts, in
        // the bytes searched or at the file's first byte, though no end of line lies within 4,096 bytes before them (#281).
        var text = new StringBuilder(where == "at the first byte searched" ? "%PDF-1.7\n" : string.Empty);
        long keyword;
        long named;

        if (where == "at the first byte searched")
        {
            text.Append('%').Append('-', 5000).Append('\n');
            keyword = text.Length;
            text.Append("xref 0 1 bad\n%").Append('-', 600).Append('\n');
            named = keyword + PdfFileReader.NearbySearchRadius;
        }
        else
        {
            var line = "4 0 obj [" + new string(' ', 600) + "] endobj xref 0 1 bad\n";
            keyword = line.IndexOf("xref", StringComparison.Ordinal);
            text.Append(line);
            named = keyword - 50;
        }

        var catalog = text.Length;
        text.Append("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        var pages = text.Length;
        text.Append("2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n%").Append('-', 1200).Append('\n');
        var table = text.Length;
        text.Append(Invariant($"xref\n0 3\n0000000000 65535 f \n{catalog:D10} 00000 n \n{pages:D10} 00000 n \ntrailer\n<< /Size 3 /Root 1 0 R /Prev {named} >>\nstartxref\n{table}\n%%EOF\n"));
        using var document = PdfDocument.Open(Encoding.Latin1.GetBytes(text.ToString()));

        var section = document.Reader.Structure.Sections[1];
        section.State.Should().Be(XRefSectionState.Malformed);
        section.Offset.Should().Be(keyword);
    }

    [Theory]
    [InlineData("a line that starts before the bytes searched", "\n")]
    [InlineData("a line that starts before the bytes searched", "\r")]
    [InlineData("a line that starts in the bytes searched", "\n")]
    [InlineData("a line that starts in the bytes searched", "\r")]
    public void An_xref_keyword_past_a_comment_a_line_feed_or_a_lone_carriage_return_ends_is_lexed_from_where_its_line_starts(
        string line, string eol)
    {
        // Past a comment of 5,000 bytes, which a line feed or a lone carriage return ends, as it ends every line of the file
        // but, where it is a carriage return, the tables' rows, which a carriage return and a line feed end, the xref keyword
        // follows the endobj of object 4, an array of 400 zeros written on one line, or a stray endobj on a short line; the
        // newest table's /Prev names the place 50 bytes before it, and no table follows it. The long line starts more than 512
        // bytes before that place, and the end of line that ends the comment is the only one within 4,096 bytes before the
        // bytes searched; the short line starts within them, past that end of line. Either end of line starts a line, as a
        // load reads it: the keyword is a token there, the section found moved, which cannot be read (#281).
        var (file, keyword) = NearbyFiles.KeywordPastALongComment(
            line == "a line that starts before the bytes searched"
                ? "4 0 obj [" + string.Concat(Enumerable.Repeat("0 ", 400)) + "] endobj xref 0 1 bad"
                : "endobj xref 0 1 bad",
            eol);
        using var document = PdfDocument.Open(file);

        var found = document.Reader.Structure.Sections[1];
        found.State.Should().Be(XRefSectionState.Malformed);
        found.Offset.Should().Be(keyword);
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().StartWith([PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefSectionUnreadable]);
    }

    [Theory]
    [InlineData("before the bytes read again")]
    [InlineData("before the bytes searched")]
    [InlineData("10 bytes before the word")]
    public async Task A_line_the_source_gives_short_of_the_place_as_it_is_read_again_declares_nothing_and_the_file_opens(string cut)
    {
        // The line before object 6, inside whose endobj the newest table's /Prev names the update's table, is a comment of 700
        // letters past 6,000 bytes of comment, which holds the word xref some 40 bytes before the place named: the line starts
        // before the bytes searched near the place, and is read again from 4,096 bytes before them. The file is cut short as
        // that read is made: before its first byte, before the bytes searched, or 10 bytes before the word. The read was
        // taken to give every byte asked for: the first two threw out of the opening, the third lexed the end of the bytes
        // given forever. Bytes given that end at or before the place are all the file holds there: the word declares nothing,
        // as where its line starts farther back, and the search goes on, to the update's table, which the cut took too; the
        // section is missing (#281).
        var (file, _, named) = NearbyFiles.UpdateAfterAStrayLine("%" + new string('a', 700) + " xref see the table below\n", gap: 6000);
        var word = Encoding.Latin1.GetString(file).IndexOf(" xref see", StringComparison.Ordinal) + 1;
        var readAgain = named - PdfFileReader.NearbySearchRadius - 1 - 4096;
        using var source = new CutShortSource(file);
        source.CutWhenReadAt(readAgain, cut switch
        {
            "before the bytes read again" => (int)readAgain - 100,
            "before the bytes searched" => (int)readAgain + 100,
            _ => word - 10,
        });

        var (state, offset, codes) = await Within(() =>
        {
            using var document = PdfDocument.Open(source, options: null, ownsSource: false);
            var section = document.Reader.Structure.Sections[1];
            return (section.State, section.Offset, document.Diagnostics.Select(diagnostic => diagnostic.Code).ToArray());
        });

        state.Should().Be(XRefSectionState.NotFound);
        offset.Should().Be(named);
        codes.Should().Contain(PdfDiagnosticCodes.XRefSectionMissing);
    }

    [Fact]
    public async Task A_source_that_gives_4096_bytes_a_read_finds_the_section_past_the_word_xref_on_a_line_read_again()
    {
        // The file of the theory above, with no cut, read through a source that gives at most 4,096 bytes a read, as a source
        // over a store of blocks may. The line read again from 4,096 bytes before the bytes searched comes back short of the
        // word, and the reading lexed the end of the bytes given forever. The bytes given are all the file holds there: the
        // word declares nothing, and the search goes on to the update's table, 65 bytes past the place named, which frees
        // object 4, as a source that gives every byte asked for finds it (#52, #281).
        var (file, update, named) = NearbyFiles.UpdateAfterAStrayLine("%" + new string('a', 700) + " xref see the table below\n", gap: 6000);

        var (state, offset, messages, four, repaired) = await Within(() =>
        {
            using var document = PdfDocument.Open(new CappedReadSource(file, 4096), options: null, ownsSource: true);
            var section = document.Reader.Structure.Sections[1];
            return (
                section.State,
                section.Offset,
                document.Diagnostics.Select(diagnostic => diagnostic.Message).ToArray(),
                document.GetObject(new PdfObjectId(4)),
                document.WasRepaired);
        });

        state.Should().Be(XRefSectionState.Relocated);
        offset.Should().Be(update);
        messages.Should().ContainSingle().Which.Should().Be(Invariant($"The cross-reference section /Prev names at offset {named} was found 65 bytes after it."));
        four.Should().BeSameAs(PdfNull.Instance);
        repaired.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task An_xref_keyword_is_a_section_found_moved_where_the_source_gives_its_first_byte_as_its_line_is_read_again(int past)
    {
        // The keyword follows the endobj of object 4, an array of 400 zeros written on one line past a comment of 5,000 bytes,
        // and the newest table's /Prev names the place 50 bytes before it: the line starts before the bytes searched near the
        // place, and is read again from 4,096 bytes before them. The file is cut short as that read is made, at the keyword
        // or one byte past it. Whether a token starts at a place is told by the bytes before it and its first: given, the
        // keyword is the section found moved, which cannot be read; not given, the bytes given end at the place, which the
        // reading lexed the end of forever, and the keyword declares nothing, the section missing (#281).
        var (file, keyword) = NearbyFiles.KeywordPastALongComment("4 0 obj [" + string.Concat(Enumerable.Repeat("0 ", 400)) + "] endobj xref 0 1 bad");
        var readAgain = keyword - 50 - PdfFileReader.NearbySearchRadius - 1 - 4096;
        using var source = new CutShortSource(file);
        source.CutWhenReadAt(readAgain, (int)keyword + past);

        var (state, offset) = await Within(() =>
        {
            using var document = PdfDocument.Open(source, options: null, ownsSource: false);
            var section = document.Reader.Structure.Sections[1];
            return (section.State, section.Offset);
        });

        state.Should().Be(past == 1 ? XRefSectionState.Malformed : XRefSectionState.NotFound);
        offset.Should().Be(past == 1 ? keyword : keyword - 50);
    }

    [Theory]
    [InlineData("%xref\n")]
    [InlineData("a comment of 700 letters")]
    [InlineData("a string of 700 letters")]
    public void An_object_an_update_frees_stays_free_where_the_word_xref_in_a_comment_or_a_string_lies_between_where_its_table_is_named_and_where_it_is(string between)
    {
        // The update frees object 3, or object 4, and a comment holding the word xref lies between where the newest table's
        // /Prev names the update's table and where it is — or, on a line of 700 letters that starts before the bytes searched
        // near the place named, a comment or a string holds it before that place. Taken for the section found moved, the word
        // ended the chain, and the rebuild that followed found the freed object's old revision (#281).
        var (file, freed) = between switch
        {
            "a comment of 700 letters" => (NearbyFiles.UpdateAfterAStrayLine("%" + new string('a', 700) + " xref see the table below\n").File, 4),
            "a string of 700 letters" => (NearbyFiles.UpdateAfterAStrayLine("9 0 obj\n(" + new string('a', 700) + " xref see the table below)\nendobj\n").File, 4),
            _ => (NearbyFiles.UpdateBehindAStrayWord(between, deleted: true).File, 3),
        };
        using var document = PdfDocument.Open(file);

        document.GetObject(new PdfObjectId(freed)).Should().BeSameAs(PdfNull.Instance);
        document.WasRepaired.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("%xref\n")]
    public void An_xrefstm_falling_short_of_its_stream_past_the_word_xref_in_a_comment_reads_the_stream(string between)
    {
        // The update's /XRefStm names a place inside the endobj before its stream, past which a comment holds the word xref,
        // nearer than the stream. Taken for a table found moved that cannot be read, the word was reported as what the
        // /XRefStm names instead of a stream, every number refused, and the file rebuilt as it opened. The word is no
        // keyword a load reads: the search goes on to the stream (#156, #281).
        var (file, stream, named) = NearbyFiles.HybridBehindAStrayWord(between);
        using var document = PdfDocument.Open(file);

        var read = document.Reader.Structure.Sections.Should().ContainSingle(section => section.IsHybridStream).Which;
        read.State.Should().Be(XRefSectionState.Relocated);
        read.Offset.Should().Be(stream);
        document.Diagnostics.Should().ContainSingle().Which.Message.Should().Be(Invariant(
            $"The cross-reference section /XRefStm names at offset {named} was found {stream - named} bytes after it."));
        Text(document, 7).Should().Be("new seven");
        document.WasRepaired.Should().BeFalse();
    }

    [Fact]
    public void The_nearest_section_that_cannot_be_read_is_found_moved_though_one_that_reads_lies_farther_off()
    {
        // The newest table's /Prev names a place 30 bytes into the oldest table's rows; the oldest table, which cannot be
        // read, is the nearest place, and the next table, which reads, lies farther on. The next table was taken, and its
        // /Prev read the oldest where it lies. What is nearest decides: the oldest table is the section found moved, reported
        // where it is, its rows read before the fault kept; the next table, which nothing names, is not read (#281).
        var (file, oldest, next, named) = NearbyFiles.UnreadableTableNearerThanTheNextOne();
        (next - named).Should().BeGreaterThan(named - oldest, "the next table lies farther from the named offset than the oldest does");

        using var document = PdfDocument.Open(file);

        document.Diagnostics.Select(d => d.Code).Should().Equal(PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefSectionUnreadable);
        document.Diagnostics[0].Message.Should().Be(Invariant($"The cross-reference section /Prev names at offset {named} was found 30 bytes before it."));
        document.Diagnostics[1].Position.Should().Be(oldest);
        document.Reader.Structure.Sections.Select(section => (section.State, section.Offset)).Skip(1).Should().Equal((XRefSectionState.Malformed, oldest));
        new PdfValidator().Validate(document).Findings.Select(finding => (finding.RuleId, finding.Location.Position)).Should().BeEquivalentTo(
            [(PdfValidationRuleIds.XRefSectionMalformed, oldest), (PdfValidationRuleIds.XRefSectionShifted, oldest)]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1200)]
    public void An_object_that_only_gives_a_width_and_cannot_be_read_nearest_to_where_an_offset_names_none_is_passed_over(int gap)
    {
        // As above, the second section a stream that gives no /Type: it may be anything, a guess the search drops for the
        // next place, the first section where it lies within 512 bytes, or none (#263).
        var (file, first, second) = NearbyFiles.MalformedUpdateNearItsOriginal(streams: true, gap, shortBy: 7, untyped: true);

        using var document = PdfDocument.Open(file);

        var section = document.Reader.Structure.Sections[1];

        if (gap == 0)
        {
            document.Diagnostics.Select(d => d.Code).Should().Equal(PdfDiagnosticCodes.XRefOffsetAdjusted);
            document.Diagnostics[0].Position.Should().Be(first);
            section.State.Should().Be(XRefSectionState.Relocated);
            section.Offset.Should().Be(first);
        }
        else
        {
            document.Diagnostics.Select(d => d.Code).Should().Equal(PdfDiagnosticCodes.XRefSectionMissing, PdfDiagnosticCodes.XRefRebuilt);
            document.Diagnostics[0].Position.Should().Be(second - 7);
            section.State.Should().Be(XRefSectionState.NotFound);
        }
    }

    [Theory]
    [InlineData("table read in part", "/Prev", PdfDiagnosticCodes.XRefSectionUnreadable, "was read only in part: the row for object 3")]
    [InlineData("table read in part", "/XRefStm", PdfDiagnosticCodes.XRefSectionUnreadable, "was read only in part: the row for object 3")]
    [InlineData("table holding no subsection", "/Prev", PdfDiagnosticCodes.XRefSectionEmpty, "holds no subsection: ")]
    [InlineData("table that cannot be read", "/Prev", PdfDiagnosticCodes.XRefSectionUnreadable, "is there but cannot be read: it holds a name")]
    [InlineData("stream read in part", "/Prev", PdfDiagnosticCodes.XRefSectionUnreadable, "was read only in part: it holds 4 rows where its /Index and /Size declare 5; ")]
    [InlineData("stream read in part", "/XRefStm", PdfDiagnosticCodes.XRefSectionUnreadable, "was read only in part: it holds 4 rows where its /Index and /Size declare 5; ")]
    [InlineData("stream that cannot be read", "/Prev", PdfDiagnosticCodes.XRefSectionUnreadable, "is there but cannot be read: its /W gives a field a negative width. ")]
    [InlineData("stream that cannot be read", "/XRefStm", PdfDiagnosticCodes.XRefSectionUnreadable, "is there but cannot be read: its /W gives a field a negative width. ")]
    public void A_report_on_a_section_found_moved_names_the_offset_that_names_it_and_where_it_was_found(
        string kind, string link, string code, string said)
    {
        // The update names the section 7 bytes short of it, and the search near that offset finds it. Each report on what the
        // section held — read only in part, holding no subsection, a row of a reserved type, cannot be read — gave where it was
        // found as the offset the link names, which the report of its shift just before gives as it is.
        var (file, section, _) = NearbyFiles.SectionNamedShort(kind, link);
        var subject = Invariant(
            $"The cross-reference {(kind.StartsWith("table", StringComparison.Ordinal) ? "table" : "stream")} {link} names at offset {section - 7}, found at offset {section}, ");

        using var document = PdfDocument.Open(file);

        document.Diagnostics[0].Code.Should().Be(PdfDiagnosticCodes.XRefOffsetAdjusted);
        document.Diagnostics[0].Message.Should().Be(Invariant($"The cross-reference section {link} names at offset {section - 7} was found 7 bytes after it."));
        var report = document.Diagnostics.Should().ContainSingle(d => d.Code == code).Which;
        report.Position.Should().Be(section);
        report.Message.Should().StartWith(subject + said);

        if (kind == "stream read in part")
        {
            var reserved = document.Diagnostics.Should().ContainSingle(d => d.Code == PdfDiagnosticCodes.XRefEntryReserved).Which;
            reserved.Position.Should().Be(section);
            reserved.Message.Should().StartWith(subject + "gives 1 row a reserved type: the row for object 3 gives it type 7.");
        }
    }

    [Fact]
    public void A_table_an_xrefstm_names_short_of_it_that_cannot_be_read_is_found_moved_and_reported_as_no_stream()
    {
        // The update's /XRefStm names a table that cannot be read 7 bytes short of it: found moved, it is reported where it is,
        // and, being no cross-reference stream whatever its rows hold, as what the /XRefStm names in the stream's place (#156).
        var (file, section, update) = NearbyFiles.SectionNamedShort("table that cannot be read", "/XRefStm");

        using var document = PdfDocument.Open(file);

        document.Diagnostics.Select(d => d.Code).Should().Equal(
            PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefSectionUnreadable, PdfDiagnosticCodes.XRefHybridStreamLost);
        document.Diagnostics[2].Position.Should().Be(section - 7);
        document.Diagnostics[2].Message.Should().Be(Invariant(
            $"The /XRefStm of the cross-reference table at offset {update} names offset {section - 7}, a cross-reference table, which cannot be read: ISO 32000-1 has it name the cross-reference stream of the table's revision (Table 19). The objects only that stream indexes are looked for by rebuilding the index."));
    }

    [Theory]
    [InlineData("")]
    [InlineData("junk\n")]
    public void A_section_found_moved_and_then_named_where_it_lies_ends_the_chain_at_once(string junk)
    {
        // The update's /Prev names a place among the first table's rows, and the first table is found 40 bytes before it; its
        // own /Prev names itself, where it lies, an offset counted from the header, which junk may precede. The chain held the
        // offset it named, not the one it read, and read the first table a second time before the loop was seen (#188). The
        // table was read there: the loop is seen at once.
        var (file, position, _) = NearbyFiles.UpdateNamingAroundItsOriginal(40, 0, junk);

        using var document = PdfDocument.Open(file);

        document.Diagnostics.Where(d => !d.Message.StartsWith("The PDF header starts", StringComparison.Ordinal)).Select(d => d.Code)
            .Should().Equal(PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefChainCycle);
        var cycle = document.Diagnostics[^1];
        cycle.Position.Should().Be(position);
        cycle.Message.Should().EndWith(string.Create(
            CultureInfo.InvariantCulture,
            $"the /Prev of the section at offset {position} names offset {position}, which the chain has already read."));
        document.Reader.Structure.Sections.Should().HaveCount(2, "the first table is read once");
        document.Reader.Structure.LoopTarget.Should().Be(XRefLoopTarget.Read);
    }

    [Fact]
    public void A_section_found_moved_and_named_again_from_where_it_was_moved_ends_the_chain_without_a_search()
    {
        // As above, but the first table's /Prev names the place among its rows the update named: an offset the chain has
        // named, where it found no section and looked for one nearby. Named again, it is a loop, not a place to look again.
        var (file, original, _) = NearbyFiles.UpdateNamingAroundItsOriginal(40, 40);

        using var document = PdfDocument.Open(file);

        document.Diagnostics.Select(d => d.Code).Should().Equal(PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefChainCycle);
        document.Diagnostics[1].Message.Should().EndWith(string.Create(
            CultureInfo.InvariantCulture,
            $"the /Prev of the section at offset {original} names offset {original + 40}, which the chain has already read."));
        document.Reader.Structure.Sections.Should().HaveCount(2);
    }

    [Fact]
    public void A_table_read_past_the_white_space_its_offset_falls_short_by_is_no_place_a_later_section_is_found()
    {
        // The update's /Prev names the end of line before the first table's keyword, where the table is read as it stands;
        // the first table's /Prev names a place among its own rows. The table was read at its keyword, not at the offset that
        // named it: it is passed over there too, and the section the first table names is missing — not found in the first
        // table, read again in a loop (#188).
        var (file, original, _) = NearbyFiles.UpdateNamingAroundItsOriginal(-1, 40);

        using var document = PdfDocument.Open(file);

        var report = document.Diagnostics.Should().ContainSingle().Which;
        report.Code.Should().Be(PdfDiagnosticCodes.XRefSectionMissing);
        report.Position.Should().Be(original + 40);
        document.Reader.Structure.Sections.Should().HaveCount(3);
        document.Reader.Structure.Sections[1].Padding.Should().Be(1);
        document.Reader.Structure.LoopOffset.Should().Be(-1);
    }

    [Fact]
    public void A_prev_naming_a_place_inside_the_stream_its_hybrid_table_names_is_missing_not_found_in_that_stream()
    {
        // The hybrid table's /XRefStm names its stream, which the chain reads, and its /Prev a place 30 bytes into that stream,
        // more than 512 bytes from the first table. The stream was found there and taken for the section /Prev names: no
        // section was reported missing, the first table never read, and the catalog it alone indexes taken by a rebuild that
        // said nothing of why. The stream is passed over: the section /Prev names is missing, and what only it indexed is
        // found by rebuilding the index.
        var (file, _, named) = NearbyFiles.HybridNamingInsideItsStream();

        using var document = PdfDocument.Open(file);

        document.Diagnostics.Select(d => d.Code).Should().Equal(PdfDiagnosticCodes.XRefSectionMissing, PdfDiagnosticCodes.XRefRebuilt);
        document.Diagnostics[0].Position.Should().Be(named);
        Text(document, 5).Should().Be("only in the stream");
        document.GetObject(new PdfObjectId(3)).AsDictionary().IsOfType(PdfName.Page).Should().BeTrue();
    }

    [Theory]
    [InlineData("<< /Type /Font /Subtype /CIDFontType2 /W [0 [500]] /BaseFont /F >>", true)]
    [InlineData("<< /Type /Font /W [1 -4 2] /Size 8 /Size 8 /Length 3 >>\nstream\nabc\nendstream", true)]
    [InlineData("<< /Subtype /CIDFontType2 /W [0 [500]] /BaseFont /F >>", false)]
    public void An_object_of_another_type_startxref_names_is_no_section_and_its_dictionary_no_trailer(string six, bool another)
    {
        // startxref names object 6, which gives a /W: a CIDFont, or a stream of /Type /Font whose dictionary gives a key twice.
        // It was recorded as a cross-reference stream that is there and cannot be read, and its dictionary judged as a
        // trailer, where the same object a /Prev names holds no section (#281). The index is rebuilt either way; one that
        // gives no /Type stays a stream that cannot be read.
        var (file, at) = NearbyFiles.StartxrefNamingAnObject(six);

        using var document = PdfDocument.Open(file);

        document.Diagnostics.Select(d => d.Code).Where(code => code != PdfDiagnosticCodes.SyntaxKeyRepeated).Should().StartWith([PdfDiagnosticCodes.XRefRebuilt]);
        var section = document.Reader.Structure.Sections.Should().ContainSingle().Which;
        section.Offset.Should().Be(at);

        if (another)
        {
            section.State.Should().Be(XRefSectionState.NotFound);
            section.Fault.Should().Be("holds object 6, which is not a cross-reference stream");
            section.Trailer.Should().BeNull();
            section.TrailerFault.Should().Be(XRefTrailerFault.None);
        }
        else
        {
            section.State.Should().Be(XRefSectionState.Malformed);
            section.Fault.Should().Be("its dictionary is not followed by stream data");
        }
    }

    [Theory]
    [InlineData("<< /Type /Font /Subtype /CIDFontType2 /W [0 [500]] /BaseFont /F >>", PdfDiagnosticCodes.XRefSectionMissing)]
    [InlineData("<< /Type /Font /W [1 -4 2] /Size 8 /Size 8 /Length 3 >>\nstream\nabc\nendstream", PdfDiagnosticCodes.XRefSectionMissing)]
    [InlineData("<< /Subtype /CIDFontType2 /W [0 [500]] /BaseFont /F >>", PdfDiagnosticCodes.XRefSectionUnreadable)]
    public void An_object_of_another_type_the_prev_names_with_no_section_near_is_no_section(string six, string code)
    {
        // /Prev names object 6, which gives a /W, and no section lies within 512 bytes of it. A CIDFont, or a stream of /Type
        // /Font whose dictionary gives a key twice, was said to be a cross-reference stream that is there and cannot be read.
        // Where it gives another /Type, it says what it is: no section, missing, as it is where a section may have been moved
        // to, its dictionary no trailer to judge (#281). One that gives no /Type stays a stream that cannot be read.
        var (file, at) = NearbyFiles.TableNamingAnObject(six);

        using var document = PdfDocument.Open(file);

        var report = document.Diagnostics.Should().ContainSingle(d => d.Code != PdfDiagnosticCodes.SyntaxKeyRepeated).Which;
        report.Code.Should().Be(code);
        report.Position.Should().Be(at);
        var section = document.Reader.Structure.Sections[1];

        if (code == PdfDiagnosticCodes.XRefSectionMissing)
        {
            section.State.Should().Be(XRefSectionState.NotFound);
            section.Fault.Should().Be("holds object 6, which is not a cross-reference stream");
            section.Trailer.Should().BeNull();
            section.TrailerFault.Should().Be(XRefTrailerFault.None);
        }
        else
        {
            section.State.Should().Be(XRefSectionState.Malformed);
            section.Fault.Should().Be("its dictionary is not followed by stream data");
        }
    }

    [Theory]
    [InlineData(-4)]
    [InlineData(6)]
    public void Reads_a_cross_reference_stream_that_xrefstm_names_a_few_bytes_off(int error)
    {
        // Object 6 is indexed by the update's cross-reference stream only.
        var (file, stream) = Updated(xrefStm: XRefStm.Present, xrefStmError: error);

        using var document = PdfDocument.Open(file);

        Text(document, 6).Should().Be("only in the stream");
        document.WasRepaired.Should().BeFalse();
        var report = document.Diagnostics.Should().ContainSingle().Which;
        report.Code.Should().Be(PdfDiagnosticCodes.XRefOffsetAdjusted);
        report.Position.Should().Be(stream);
    }

    [Fact]
    public void Reports_a_prev_that_is_not_an_offset_and_finds_what_it_named_when_asked()
    {
        // tiff2pdf writes /Prev 576066 0 R. A reference is read where a section already read places its object, without
        // loading it while the index is still being read (#182); this one names an object no section places, so it names
        // no section, and the section is looked for on demand.
        var (file, _) = Updated(prevAt: Offset.AsReference);
        var update = StartXRef(file);

        using var document = PdfDocument.Open(file);

        document.WasRepaired.Should().BeFalse();
        var report = document.Diagnostics.Should().ContainSingle().Which;
        report.Code.Should().Be(PdfDiagnosticCodes.XRefSectionMissing);
        report.Position.Should().Be(update);
        report.Message.Should().StartWith(string.Create(
            CultureInfo.InvariantCulture,
            $"The /Prev of the cross-reference section at offset {update} is not an offset"));

        ReadsWhole(document);
        document.WasRepaired.Should().BeTrue();
    }

    [Theory]
    [InlineData(9)]
    [InlineData(4)]
    public void Reads_a_reference_to_an_object_the_file_does_not_define_as_null_without_rebuilding(int number)
    {
        // Object 9 lies past /Size; object 4 is a free entry of the table. Each is written after the end of the
        // file, where only a rebuild would find it: reading it as null, with nothing to report, shows that the
        // index was taken at its word (T27).
        var written = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .WithObject(3, $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents {number} 0 R >>")
            .WithObject(5, "(the table's last object)")
            .BuildClassic(rootNumber: 1);
        byte[] file = [.. written, .. Encoding.ASCII.GetBytes($"{number} 0 obj\n(outside the index)\nendobj\n")];

        using var document = PdfDocument.Open(file);
        var page = document.Catalog.GetDictionary(PdfName.Pages)!.GetArray(PdfName.Kids)!.Resolved(0).AsDictionary()!;

        page.GetRaw(PdfName.Contents).Resolved().Should().BeSameAs(PdfNull.Instance);
        document.GetObject(new PdfObjectId(number)).Should().BeSameAs(PdfNull.Instance);
        document.WasRepaired.Should().BeFalse();
        document.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void The_entry_guard_counts_the_entries_of_every_section_and_stops_the_chain_at_the_one_that_fills_it()
    {
        // Read newest first: the third update's ten objects, the second's forty, then ten of the first's, which fill an
        // index of sixty. The first update's section is the one reported, and the original table, older still, is not read.
        const string Catalog = "<< /Type /Catalog /Pages 2 0 R >>";
        const string Pages = "<< /Type /Pages /Kids [] /Count 0 >>";
        var builder = new TestPdfBuilder().WithObject(1, Catalog).WithObject(2, Pages);
        for (var number = 3; number < 43; number++)
        {
            builder.WithObject(number, "null");
        }

        var file = builder.BuildClassic(rootNumber: 1);
        var sections = new List<long>();

        foreach (var (first, count) in new[] { (43, 40), (83, 40), (123, 8) })
        {
            sections.Add(file.Length);
            var updates = Enumerable.Range(first, count).Select(number => (number, "null")).ToList();
            if (first == 123)
            {
                updates.AddRange([(1, Catalog), (2, Pages)]);
            }

            file = TestPdfBuilder.AppendIncrementalUpdate(file, rootNumber: 1, [.. updates]);
        }

        var firstUpdate = OffsetAfter(file, sections[0], "xref\n");
        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxIndexEntries = 60 } });

        document.ObjectCount.Should().Be(60);
        document.Catalog.Required().IsOfType(PdfName.Catalog).Should().BeTrue();
        document.GetObject(new PdfObjectId(52)).Should().BeSameAs(PdfNull.Instance);
        document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.LimitIndexEntries)
            .Which.Position.Should().Be(firstUpdate);
        document.Reader.Structure.Sections.Should().HaveCount(3);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/Size 9 0 R ")]
    [InlineData("/Size 2 ")]
    [InlineData("/Size -9223372036854775808 ")]
    public void Reads_a_chain_whose_newest_size_numbers_no_object_past_its_rows_whole(string size)
    {
        // Once the newest section is read, the index makes room for the objects its /Size numbers. One the trailer lacks,
        // writes as a reference, or gives no larger than the entries the newest section holds asks for no room: the older
        // sections make room for their own rows, and nothing else changes.
        var (updated, _) = Updated();
        var text = Encoding.Latin1.GetString(updated);
        var newest = text.LastIndexOf("/Size 8 ", StringComparison.Ordinal);
        var file = Encoding.Latin1.GetBytes(string.Concat(text.AsSpan(0, newest), size, text.AsSpan(newest + "/Size 8 ".Length)));

        using var document = PdfDocument.Open(file);

        document.ObjectCount.Should().Be(6);
        document.WasRepaired.Should().BeFalse();
        document.Diagnostics.Should().BeEmpty();
        ReadsWhole(document);
    }

    [Fact]
    public void A_full_index_ends_the_chain_at_the_stream_of_a_hybrid_section()
    {
        // The chain ended when a section filled the index, but not when a hybrid section's stream did: the original section
        // was read on, a row of it refused, and the next lookup of an object past the bound rebuilt the index.
        var (updated, _) = Updated(xrefStm: XRefStm.Present);
        var text = Encoding.Latin1.GetString(updated);
        var pages = text.IndexOf("2 0 obj", StringComparison.Ordinal);
        var row = string.Create(CultureInfo.InvariantCulture, $"{pages:D10} 00000 n");
        text.Should().Contain(row);
        var file = Encoding.Latin1.GetBytes(text.Replace(row, string.Create(CultureInfo.InvariantCulture, $"{pages:D10} 70000 n"), StringComparison.Ordinal));
        var options = PdfReaderOptions.Default with { Limits = PdfReaderLimits.Default with { MaxIndexEntries = 2 } };

        using var document = PdfDocument.Open(file, options);

        document.Reader.Structure.Sections.Should().HaveCount(2);
        document.GetObject(new PdfObjectId(2)).Should().BeSameAs(PdfNull.Instance);
        document.Reader.GetPresence(2).Should().Be(ObjectPresence.Unindexed);
        document.WasRepaired.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_section_found_near_where_prev_names_it_is_not_searched_past_the_rows_a_guard_left_unread(bool full)
    {
        // A place a section may have been moved to is a guess, and a guess cut by a guard was searched to the file's end for
        // its trailer, each of the 32 the chain may try: the older section's trailer, whose keys the newest gives again,
        // is not looked for past its rows.
        var (file, original) = Updated(prevError: 3);
        var limits = full
            ? PdfReaderLimits.Default with { MaxIndexEntries = 4 }
            : PdfReaderLimits.Default with { MaxXRefSectionLength = 80 };

        using var document = PdfDocument.Open(file, PdfReaderOptions.Default with { Limits = limits });

        var older = document.Reader.Structure.Sections.Should().HaveCount(2).And.Subject.Last();
        older.Offset.Should().Be(original);
        older.CutByLimit.Should().BeTrue();
        older.TrailerPosition.Should().Be(-1);
    }

    [Fact]
    public void An_object_only_a_section_past_a_cut_trailer_indexes_is_unknown_rather_than_missing()
    {
        // The update's trailer runs past MaxTrailerLength before its /Prev: the original section is left unread, which is
        // the guard's, and the page tree it alone indexes was said to be missing, the validator blaming the file.
        var original = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .BuildClassic(rootNumber: 1);
        var text = Encoding.Latin1.GetString(
            TestPdfBuilder.AppendIncrementalUpdate(original, rootNumber: 1, [(1, "<< /Type /Catalog /Pages 2 0 R >>")]));
        var trailer = text.LastIndexOf("/Root 1 0 R /Prev", StringComparison.Ordinal);
        var file = Encoding.Latin1.GetBytes(text.Insert(trailer + "/Root 1 0 R".Length, $" /Pad ({new string('x', 70 * 1024)})"));

        using var document = PdfDocument.Open(file);

        document.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.LimitTrailer);
        document.Catalog.GetDictionary(PdfName.Pages).Should().BeNull();
        document.Reader.GetPresence(2).Should().Be(ObjectPresence.Unindexed);
        new PdfValidator().Validate(document).Findings.Select(finding => finding.RuleId)
            .Should().NotContain(PdfValidationRuleIds.ObjectReferenceMissing);
    }

    [Fact]
    public void A_cross_reference_stream_whose_dictionary_a_guard_cut_before_it_declared_itself_is_no_section_missing()
    {
        // The original section is a cross-reference stream whose dictionary runs past MaxTrailerLength before its /Type and
        // /W: it was taken for no section at all, reported missing, and the index rebuilt for what it alone indexes.
        var written = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .BuildWithXRefStream(rootNumber: 1);
        var original = Encoding.Latin1.GetBytes(Encoding.Latin1.GetString(written)
            .Replace("<< /Type /XRef", $"<< /Pad ({new string('x', 70 * 1024)}) /Type /XRef", StringComparison.Ordinal));
        var file = TestPdfBuilder.AppendIncrementalUpdate(original, rootNumber: 1, [(1, "<< /Type /Catalog /Pages 2 0 R >>")]);

        using var document = PdfDocument.Open(file);

        document.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.LimitTrailer);
        document.GetObject(new PdfObjectId(2)).Should().BeSameAs(PdfNull.Instance);
        document.Reader.GetPresence(2).Should().Be(ObjectPresence.Unindexed);
        document.WasRepaired.Should().BeFalse();
    }

    [Theory]
    [InlineData("array")]
    [InlineData("dictionary")]
    [InlineData("image")]
    [InlineData("stream")]
    public void An_object_a_guard_cut_where_prev_names_a_section_is_a_section_missing_when_it_cannot_be_one(string kind)
    {
        // The update's /Prev names object 3, which runs past MaxTrailerLength: an array, a dictionary of another type, or a
        // stream whose dictionary was read whole is no cross-reference stream, cut or not. It was taken for one the guard
        // cut before it declared itself, and what only the original section indexes read as unknown, with no rebuild.
        var pad = new string('x', 70 * 1024);
        var builder = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(3, kind switch
            {
                "array" => "[" + string.Concat(Enumerable.Repeat("0 ", 36 * 1024)) + "]",
                "dictionary" => $"<< /Type /Annot /Pad ({pad}) >>",
                "image" => $"<< /Type /XObject /Subtype /Image /Width 1 /Height 1 /Length 4 0 R >>\nstream\n{pad}\nendstream",
                _ => $"<< /Length 4 0 R >>\nstream\n{pad}\nendstream",
            })
            .WithObject(4, (pad.Length + 1).ToString(CultureInfo.InvariantCulture));
        var original = builder.BuildClassic(rootNumber: 1);
        var updated = Encoding.Latin1.GetString(
            TestPdfBuilder.AppendIncrementalUpdate(original, rootNumber: 1, [(1, "<< /Type /Catalog /Pages 2 0 R >>")]));
        var prev = updated.LastIndexOf("/Prev ", StringComparison.Ordinal) + "/Prev ".Length;
        var file = Encoding.Latin1.GetBytes(string.Concat(
            updated[..prev],
            updated.IndexOf("3 0 obj", StringComparison.Ordinal).ToString(CultureInfo.InvariantCulture),
            updated[updated.IndexOf(' ', prev)..]));

        using var document = PdfDocument.Open(file);

        var named = document.Reader.Structure.Sections.Should().HaveCount(2).And.Subject.Last();
        named.CutByLimit.Should().BeFalse();
        named.State.Should().Be(XRefSectionState.NotFound);
        document.Diagnostics.Select(entry => entry.Code).Should().Contain(PdfDiagnosticCodes.XRefSectionMissing);
        document.GetObject(new PdfObjectId(2)).Should().BeOfType<PdfDictionary>();
        document.WasRepaired.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Sections_named_a_few_bytes_off_are_looked_for_at_thirty_two_places_a_document_at_most(bool namedAtAnObject)
    {
        // Forty hybrid sections each name their stream three bytes past its header, which is found there, the nearest
        // place: each search reads a window, and a document's searches stop at the thirty-second, so that the streams of
        // the eight oldest sections are missing rather than looked for. Or each names the header of a string just before its
        // stream: the place named, a header, is where the section is not, and is not tried again, nor counted — the stream,
        // the next place, is the one place each search tries.
        const int Sections = 40;
        var text = new StringBuilder("%PDF-1.7\n");
        var catalog = text.Length;
        text.Append("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        var pages = text.Length;
        text.Append("2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n");
        var previous = -1;

        for (var section = 0; section < Sections; section++)
        {
            var number = section + 3;
            var other = text.Length;

            if (namedAtAnObject)
            {
                text.Append(CultureInfo.InvariantCulture, $"{number + Sections} 0 obj\n(not a section)\nendobj\n");
            }

            var stream = text.Length;
            var named = namedAtAnObject ? other : stream + 3;
            var row = Encoding.Latin1.GetString([1, (byte)(stream >> 24), (byte)(stream >> 16), (byte)(stream >> 8), (byte)stream, 0, 0]);
            text.Append(CultureInfo.InvariantCulture, $"{number} 0 obj\n<< /Type /XRef /Size {Sections + 3} /W [1 4 2] /Index [{number} 1] /Length 7 >>\nstream\n")
                .Append(row).Append("\nendstream\nendobj\n");
            var table = text.Length;
            text.Append("xref\n0 3\n0000000000 65535 f\r\n")
                .Append(CultureInfo.InvariantCulture, $"{catalog:D10} 00000 n\r\n{pages:D10} 00000 n\r\n")
                .Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {Sections + 3} /Root 1 0 R /XRefStm {named}")
                .Append(previous < 0 ? string.Empty : string.Create(CultureInfo.InvariantCulture, $" /Prev {previous}"))
                .Append(" >>\n");
            previous = table;
        }

        text.Append(CultureInfo.InvariantCulture, $"startxref\n{previous}\n%%EOF\n");

        using var document = PdfDocument.Open(Encoding.Latin1.GetBytes(text.ToString()));

        var codes = document.Diagnostics.Select(entry => entry.Code).ToList();
        codes.Count(code => code == PdfDiagnosticCodes.XRefOffsetAdjusted).Should().Be(32);
        codes.Count(code => code == PdfDiagnosticCodes.XRefSectionMissing).Should().Be(Sections - 32);
        document.Reader.Structure.Sections.Should().HaveCount(2 * Sections);
    }

    [Fact]
    public void The_search_near_a_section_reaches_the_file_s_last_byte_where_an_xref_keyword_ending_the_file_is_one_more_place_tried()
    {
        // Thirty-two tables, each but the oldest naming the one before it three bytes past its keyword, where it is found at
        // the first place tried: thirty-one places. The oldest names a place in a comment after the end-of-file marker, 20
        // bytes before the keyword xref the file ends with, and further from the table before the marker. The window searched
        // near the place ends at the file's end, as it ends 512 bytes past it elsewhere (#197): the keyword the file ends with,
        // the nearest place, is the thirty-second tried, whatever it reads, and the table, further, is not tried.
        const int Tables = 32;
        var text = new StringBuilder("%PDF-1.7\n");
        var catalog = text.Length;
        text.Append("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        var pages = text.Length;
        text.Append("2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n");
        var previous = -1;

        for (var table = 0; table < Tables; table++)
        {
            var at = text.Length;
            text.Append("xref\n0 3\n0000000000 65535 f\r\n")
                .Append(CultureInfo.InvariantCulture, $"{catalog:D10} 00000 n\r\n{pages:D10} 00000 n\r\n")
                .Append(previous < 0 ? "trailer\n<< /Size 3 /Root 1 0 R /Prev ########## >>\n" : string.Create(
                    CultureInfo.InvariantCulture, $"trailer\n<< /Size 3 /Root 1 0 R /Prev {previous + 3} >>\n"));
            previous = at;
        }

        text.Append('%').Append('-', 600).Append('\n');
        var last = text.Length;
        text.Append(CultureInfo.InvariantCulture, $"xref\n0 1\n0000000000 65535 f\r\ntrailer\n<< /Size 3 /Root 1 0 R >>\nstartxref\n{previous}\n%%EOF\n%")
            .Append('-', 40).Append("\nxref");
        var named = text.Length - 4 - 20;
        named.Should().BeGreaterThan(last + 50, "the keyword the file ends with is the nearest place");
        var file = Encoding.Latin1.GetBytes(text.ToString().Replace("##########", named.ToString("D10", CultureInfo.InvariantCulture), StringComparison.Ordinal));

        using var document = PdfDocument.Open(file);

        var adjusted = document.Diagnostics.Where(entry => entry.Code == PdfDiagnosticCodes.XRefOffsetAdjusted).ToList();
        adjusted.Count(entry => entry.Message.EndsWith(" was found 3 bytes before it.", StringComparison.Ordinal)).Should().Be(Tables - 1);
        adjusted.Should().NotContain(entry => entry.Position == last, "the table is further than the thirty-second place tried");
    }

    [Fact]
    public void A_full_index_ends_the_chain_at_the_cross_reference_stream_that_filled_it()
    {
        // The update's stream fills the index at its second row: the original section, which /Prev names, could add
        // nothing, and is not read.
        var file = UpdatedThroughAStream(hybrid: false);
        var options = PdfReaderOptions.Default with { Limits = PdfReaderLimits.Default with { MaxIndexEntries = 1 } };

        using var document = PdfDocument.Open(file, options);

        document.Reader.Structure.Sections.Should().ContainSingle().Which.Kind.Should().Be(XRefSectionKind.Stream);
        document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.LimitIndexEntries);
        document.Reader.GetPresence(2).Should().Be(ObjectPresence.Unindexed);
        document.WasRepaired.Should().BeFalse();
    }

    [Theory]
    [InlineData(false, 1000)]
    [InlineData(true, 1000)]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    public void An_older_section_does_not_stand_for_what_a_cross_reference_stream_s_cut_rows_define(bool hybrid, int capacity)
    {
        // The update redefines object 3 in a cross-reference stream whose rows MaxDecodedStreamLength cuts before 3's: the
        // original section's row for it would serve the revision the update replaced. The chain ends at the cut, a hybrid
        // file's stream included, and object 3 reads as null until the guard is lifted. The cut was known by its report,
        // and taken for the file's damage once the diagnostics were full.
        var file = UpdatedThroughAStream(hybrid);
        var options = PdfReaderOptions.Default with
        {
            Limits = PdfReaderLimits.Default with { MaxDecodedStreamLength = 7 },
            DiagnosticCapacity = capacity,
        };

        using (var document = PdfDocument.Open(file, options))
        {
            document.GetObject(new PdfObjectId(3)).Should().BeSameAs(PdfNull.Instance);
            document.Reader.GetPresence(3).Should().Be(ObjectPresence.Unindexed);
            document.WasRepaired.Should().BeFalse();
        }

        using var lifted = PdfDocument.Open(file);
        Text(lifted, 3).Should().Be("new three");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_cross_reference_stream_a_guard_cut_is_not_looked_for_near_where_it_was_named(bool hybrid)
    {
        // MaxTrailerLength cuts the update's stream before its /Type, where the table that names a hybrid file's stream, or
        // a later update's /Prev, names it: the original's table, or the naming one, within reach of it, stood for it, so
        // that the chain went on and object 3 read as the revision the update replaced. The section is where it was named,
        // and the guard's.
        var file = UpdatedThroughAStream(hybrid, $" /Pad ({new string('x', 70 * 1024)})");

        if (!hybrid)
        {
            file = TestPdfBuilder.AppendIncrementalUpdate(file, rootNumber: 1, [(2, "<< /Type /Pages /Kids [] /Count 0 >>")]);
        }

        using (var document = PdfDocument.Open(file))
        {
            document.GetObject(new PdfObjectId(3)).Should().BeSameAs(PdfNull.Instance);
            document.Reader.GetPresence(3).Should().Be(ObjectPresence.Unindexed);
            document.WasRepaired.Should().BeFalse();
            document.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.LimitTrailer);
            document.Reader.Structure.Sections.Should().ContainSingle(section => section.CutByLimit)
                .Which.State.Should().Be(XRefSectionState.Malformed);
        }

        using var lifted = PdfDocument.Open(file, PdfReaderOptions.Default with
        {
            Limits = PdfReaderLimits.Default with { MaxTrailerLength = 1024 * 1024 },
        });
        Text(lifted, 3).Should().Be("new three");
    }

    [Fact]
    public void A_stream_a_guard_cut_where_prev_names_it_is_not_found_again_in_the_table_that_names_it()
    {
        // The original is a cross-reference stream, and MaxTrailerLength cuts its dictionary: the update's table, within
        // reach of it, was taken for it, which made a loop of the chain, a rebuild, and an error of the file's.
        var written = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .BuildWithXRefStream(rootNumber: 1);
        var file = TestPdfBuilder.AppendIncrementalUpdate(written, rootNumber: 1, [(1, "<< /Type /Catalog /Pages 2 0 R >>")]);
        var options = PdfReaderOptions.Default with { Limits = PdfReaderLimits.Default with { MaxTrailerLength = 40 } };

        using var document = PdfDocument.Open(file, options);

        document.GetObject(new PdfObjectId(2)).Should().BeSameAs(PdfNull.Instance);
        document.Reader.GetPresence(2).Should().Be(ObjectPresence.Unindexed);
        document.WasRepaired.Should().BeFalse();
        document.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.LimitTrailer);
        new PdfValidator().Validate(document).Findings.Select(finding => finding.RuleId)
            .Should().NotContain([PdfValidationRuleIds.XRefChainLoop, PdfValidationRuleIds.XRefSectionShifted]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_trailer_a_guard_cut_past_its_prev_ends_the_chain(bool hybrid)
    {
        // MaxTrailerLength cuts the newest trailer past its /Prev: the /XRefStm, or the /Root, past the cut is unknown,
        // and the chain went on to the original, whose rows and trailer stood for what the cut hid.
        string file;

        if (hybrid)
        {
            var text = Encoding.Latin1.GetString(UpdatedThroughAStream(hybrid: true));
            file = text.Insert(text.LastIndexOf(" /XRefStm", StringComparison.Ordinal), $" /Pad ({new string('x', 70 * 1024)})");
        }
        else
        {
            var original = new TestPdfBuilder()
                .WithObject(1, "<< /Type /Catalog /Pages 2 0 R /Lang (old) >>")
                .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
                .BuildClassic(rootNumber: 1);
            var text = Encoding.Latin1.GetString(TestPdfBuilder.AppendIncrementalUpdate(
                original, rootNumber: 7, [(7, "<< /Type /Catalog /Pages 2 0 R /Lang (new) >>")]));
            var root = text.LastIndexOf(" /Root 7 0 R", StringComparison.Ordinal);
            file = text.Remove(root, " /Root 7 0 R".Length)
                .Insert(text.IndexOf(" >>", root, StringComparison.Ordinal) - " /Root 7 0 R".Length, $" /Pad ({new string('x', 70 * 1024)}) /Root 7 0 R");
        }

        using var document = PdfDocument.Open(Encoding.Latin1.GetBytes(file));

        document.Reader.Structure.Sections.Should().ContainSingle().Which.TrailerCutByLimit.Should().BeTrue();
        document.WasRepaired.Should().BeFalse();

        if (hybrid)
        {
            document.GetObject(new PdfObjectId(3)).Should().BeSameAs(PdfNull.Instance);
            document.Reader.GetPresence(3).Should().Be(ObjectPresence.Unindexed);
        }
        else
        {
            // The catalog is found among the objects the newest section indexes, not taken from the original's trailer.
            document.Catalog.Required().GetRaw(PdfName.Get("Lang")).Should().BeOfType<PdfString>()
                .Which.ToText().Should().Be("new");
        }
    }

    [Fact]
    public void A_rebuild_reports_a_full_index_once_though_an_object_stream_lies_before_the_header()
    {
        // The rebuild placed an object stream found before the header at an offset counted from it, negative, and the
        // report at that offset: the next number with no room, the report seeming not made, was reported again.
        static string Packed(int number, int first)
        {
            var header = string.Join(' ', Enumerable.Range(0, 50).Select(i => string.Create(CultureInfo.InvariantCulture, $"{first + i} {i * 4}")));
            var members = string.Concat(Enumerable.Range(0, 50).Select(_ => "(m)\n"));
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{number} 0 obj\n<< /Type /ObjStm /N 50 /First {header.Length + 1} /Length {header.Length + 1 + members.Length} >>\nstream\n{header}\n{members}\nendstream\nendobj\n");
        }

        var text = Packed(3, 100) + "%PDF-1.7\n1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n"
            + Packed(4, 200) + "trailer\n<< /Root 1 0 R >>\n%%EOF\n";
        var options = PdfReaderOptions.Default with { Limits = PdfReaderLimits.Default with { MaxIndexEntries = 20 } };

        using var document = PdfDocument.Open(Encoding.Latin1.GetBytes(text), options);

        document.WasRepaired.Should().BeTrue();
        document.ObjectCount.Should().Be(20);
        document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.LimitIndexEntries)
            .Which.Position.Should().Be(0);
    }

    [Fact]
    public void Reads_an_object_only_a_section_past_the_section_count_indexes_as_null_until_the_guard_is_lifted()
    {
        // Two saves on top of the original, and a chain read no further than two sections: the original section, which
        // alone indexes the page tree, is left unread by the reader's own limit. That is no damage: what it held reads as
        // null, unknown rather than missing, and the index is not rebuilt for it (#183), until the guard is lifted. A
        // rebuild once lost what the chain had read, past the two million headers it counted.
        var (original, _) = Updated();
        var file = TestPdfBuilder.AppendIncrementalUpdate(original, rootNumber: 1, [(1, "<< /Type /Catalog /Pages 2 0 R >>")]);
        var options = PdfReaderOptions.Default with { Limits = PdfReaderLimits.Default with { MaxXRefSectionCount = 2 } };

        using (var document = PdfDocument.Open(file, options))
        {
            document.Catalog.GetDictionary(PdfName.Pages).Should().BeNull();
            document.GetObject(new PdfObjectId(5)).Should().BeSameAs(PdfNull.Instance);
            Text(document, 4).Should().Be("updated");
            document.Reader.GetPresence(5).Should().Be(ObjectPresence.Unindexed);
            document.WasRepaired.Should().BeFalse();
            document.Diagnostics.Select(d => d.Code).Should().Equal(PdfDiagnosticCodes.LimitXRefSectionCount);
        }

        using var lifted = PdfDocument.Open(file);
        ReadsWhole(lifted);
        lifted.WasRepaired.Should().BeFalse();
    }

    [Fact]
    public void Reads_an_object_a_length_bound_cut_out_of_its_table_as_null_until_the_guard_is_lifted()
    {
        // Tables read no further than 128 bytes: the update's, which names the catalog, fits; the original's six rows do
        // not, and the last, object 5's, is cut. Object 5 reads as null, unknown rather than missing, and the index is not
        // rebuilt for it (#183), until the guard is lifted.
        var (file, _) = Updated();
        var options = PdfReaderOptions.Default with { Limits = PdfReaderLimits.Default with { MaxXRefSectionLength = 128 } };

        using (var document = PdfDocument.Open(file, options))
        {
            Text(document, 4).Should().Be("updated");
            document.GetObject(new PdfObjectId(5)).Should().BeSameAs(PdfNull.Instance);
            document.Reader.GetPresence(5).Should().Be(ObjectPresence.Unindexed);
            document.GetObject(new PdfObjectId(99)).Should().BeSameAs(PdfNull.Instance);
            document.Reader.GetPresence(99).Should().Be(ObjectPresence.Unindexed, "the rows past the cut might have defined it");
            document.WasRepaired.Should().BeFalse();
            document.Diagnostics.Select(d => d.Code).Should().Equal(PdfDiagnosticCodes.LimitXRefSectionLength);
        }

        using var lifted = PdfDocument.Open(file);
        ReadsWhole(lifted);
        lifted.Reader.GetPresence(99).Should().Be(ObjectPresence.Missing);
    }

    [Fact]
    public void A_stream_whose_length_a_guard_left_out_of_the_index_is_read_to_its_endstream_and_not_reported()
    {
        // The update places the stream; its /Length is object 9, which only the original section places, past the guard.
        // Whether the file holds a length there is unknown: the data is taken up to its endstream, and the guard's report
        // is the only one.
        var original = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(9, "5")
            .BuildClassic(rootNumber: 1);
        var file = TestPdfBuilder.AppendIncrementalUpdate(original, rootNumber: 1, [(4, "<< /Length 9 0 R >>\nstream\nhello\nendstream")]);
        var options = PdfReaderOptions.Default with { Limits = PdfReaderLimits.Default with { MaxXRefSectionCount = 1 } };

        using var document = PdfDocument.Open(file, options);

        document.GetObject(new PdfObjectId(4)).AsStream().Required().GetRawBytes().ToArray().Should().Equal("hello"u8.ToArray());
        document.Reader.GetPresence(9).Should().Be(ObjectPresence.Unindexed);
        document.Diagnostics.Select(d => d.Code).Should().Equal(PdfDiagnosticCodes.LimitXRefSectionCount);
    }

    [Fact]
    public void A_cross_reference_stream_whose_length_a_guard_left_out_of_the_index_is_not_reported()
    {
        // The newest section, a cross-reference stream, gives its /Length as object 9, which only the original table places:
        // read once the chain is, and the chain stopped short of that table. The length is unknown, not wrong.
        var original = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(9, "7")
            .BuildClassic(rootNumber: 1);
        var table = StartXRef(original);
        var update = original.Length;
        var head = Encoding.ASCII.GetBytes(string.Create(
            CultureInfo.InvariantCulture,
            $"10 0 obj\n<< /Type /XRef /Size 11 /W [1 4 2] /Index [10 1] /Prev {table} /Root 1 0 R /Length 9 0 R >>\nstream\n"));
        byte[] row = [1, (byte)(update >> 24), (byte)(update >> 16), (byte)(update >> 8), (byte)update, 0, 0];
        var tail = Encoding.ASCII.GetBytes(string.Create(CultureInfo.InvariantCulture, $"\nendstream\nendobj\nstartxref\n{update}\n%%EOF\n"));
        byte[] file = [.. original, .. head, .. row, .. tail];
        var options = PdfReaderOptions.Default with { Limits = PdfReaderLimits.Default with { MaxXRefSectionCount = 1 } };

        using var document = PdfDocument.Open(file, options);

        document.Catalog.Should().BeNull("the catalog lies past the guard");
        document.Reader.GetPresence(9).Should().Be(ObjectPresence.Unindexed);
        document.Diagnostics.Select(d => d.Code).Should().Equal(PdfDiagnosticCodes.LimitXRefSectionCount);
    }

    [Fact]
    public void Reports_a_cross_reference_stream_whose_dictionary_the_guard_cuts_under_the_guard_alone()
    {
        // The original section's dictionary is longer than the 48 bytes trailers are read to: what the reader makes of
        // the cut — no stream data after it — is not the file's fault, and the guard's report is the only one. What only
        // that section indexes reads as null until the guard is lifted, and the index is not rebuilt for it (#183).
        var (file, _) = Updated(originalAsStream: true);
        var options = PdfReaderOptions.Default with { Limits = PdfReaderLimits.Default with { MaxTrailerLength = 48 } };

        using var document = PdfDocument.Open(file, options);

        document.Diagnostics.Select(d => d.Code).Should().Equal(PdfDiagnosticCodes.LimitTrailer);
        Text(document, 4).Should().Be("updated");
        document.GetObject(new PdfObjectId(5)).Should().BeSameAs(PdfNull.Instance);
        document.Reader.GetPresence(5).Should().Be(ObjectPresence.Unindexed);
        document.WasRepaired.Should().BeFalse();
    }

    [Fact]
    public void Reports_a_table_that_prev_names_and_the_guard_cuts_before_any_row_under_the_guard_alone()
    {
        // The newest section indexes nothing, its one subsection 0 0, and fits the 17 bytes tables are read to; the original's
        // does not reach the end of its first row within them. The guard's report names the limit that lifts it: the section
        // is not reported again.
        var original = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .WithObject(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>")
            .WithObject(4, "(original)")
            .WithObject(5, $"({new string('x', 1024)})")
            .BuildClassic(rootNumber: 1);
        var section = StartXRef(original);
        var update = original.Length + 602;
        byte[] file =
        [
            .. original,
            .. Encoding.ASCII.GetBytes(string.Create(
                CultureInfo.InvariantCulture,
                $"%{new string('-', 600)}\nxref\n0 0\ntrailer\n<< /Size 6 /Root 1 0 R /Prev {section} >>\nstartxref\n{update}\n%%EOF\n")),
        ];
        var options = PdfReaderOptions.Default with { Limits = PdfReaderLimits.Default with { MaxXRefSectionLength = 17 } };

        using var document = PdfDocument.Open(file, options);

        // The original is kept, the guard's, its keyword read before the cut; but the chain indexes nothing, and a chain a guard
        // leaves no row is read as before (ADR 34): the index is rebuilt at opening. Under a newer section that indexes a row,
        // the catalog would read as null, the guard's, with nothing rebuilt.
        document.Diagnostics.Select(d => d.Code).Should().Equal(PdfDiagnosticCodes.LimitXRefSectionLength, PdfDiagnosticCodes.XRefRebuilt);
        document.Diagnostics[0].Position.Should().Be(section);
        document.Reader.Structure.Sections[1].State.Should().Be(XRefSectionState.Read);
        document.Reader.Structure.Sections[1].CutByLimit.Should().BeTrue();
        Text(document, 4).Should().Be("original");
    }

    [Fact]
    public void Looks_for_an_object_a_cross_reference_stream_holds_no_row_for()
    {
        // The stream declares six rows, objects 0 to 5, and its data holds five: object 5, written in the file,
        // has no entry the data could give, so it is looked for by rebuilding the index.
        var file = new List<byte>();
        var offsets = new long[6];

        void Append(string text) => file.AddRange(Encoding.Latin1.GetBytes(text));

        Append("%PDF-1.7\n");
        offsets[1] = file.Count;
        Append("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        offsets[2] = file.Count;
        Append("2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n");
        offsets[3] = file.Count;
        Append("3 0 obj\n(three)\nendobj\n");
        offsets[5] = file.Count;
        Append("5 0 obj\n(five)\nendobj\n");
        offsets[4] = file.Count;
        Append("4 0 obj\n<< /Type /XRef /W [1 4 2] /Size 6 /Root 1 0 R /Length 35 >>\nstream\n");

        for (var number = 0; number < 5; number++)
        {
            var offset = offsets[number];
            file.AddRange([(byte)(number == 0 ? 0 : 1), (byte)(offset >> 24), (byte)(offset >> 16), (byte)(offset >> 8), (byte)offset, 0, 0]);
        }

        Append(string.Create(CultureInfo.InvariantCulture, $"\nendstream\nendobj\nstartxref\n{offsets[4]}\n%%EOF\n"));

        using var document = PdfDocument.Open(file.ToArray());

        Text(document, 3).Should().Be("three");
        document.WasRepaired.Should().BeFalse();
        Text(document, 5).Should().Be("five");
        document.WasRepaired.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("t")]
    [InlineData("trai")]
    [InlineData("traile")]
    public void A_table_prev_names_that_the_end_of_the_file_cuts_after_a_row_is_reported_once_its_rows_kept_and_what_its_trailer_named_found_by_a_rebuild(string tail)
    {
        // The update names through /Prev a table the end of the file cuts after object 3's row: its trailer, which would have
        // named the older table that indexes object 4, is lost. It read as sound, in silence, and object 4 as null (#207). The
        // table is kept, read only in part, reported once: object 3 is read through its row with no rebuild, and object 4,
        // which only what the cut took leads to, by rebuilding the index. A cut within the trailer keyword is a cut before
        // it: the start of the keyword was taken for a stray token, and the table for one that cannot be read.
        var file = PdfTemplate.Build(PartialFiles.CutUpdate("3 1\n{row:3}\n" + tail));
        var cut = PdfTemplate.OffsetOf(file, "\nxref\n", 3) + 1;

        using var document = PdfDocument.Open(file);

        var report = document.Diagnostics.Should().ContainSingle().Which;
        report.Code.Should().Be(PdfDiagnosticCodes.XRefSectionUnreadable);
        report.Severity.Should().Be(PdfDiagnosticSeverity.Warning);
        report.Position.Should().Be(cut);
        report.Message.Should().Be(string.Create(
            CultureInfo.InvariantCulture,
            $"The cross-reference table /Prev names at offset {cut} was read only in part: the file ends before its trailer; the rows read were kept, and what only the rest of the table, or the sections its trailer would name, index is looked for by rebuilding the index."));

        var section = document.Reader.Structure.Sections[^1];
        section.Offset.Should().Be(cut);
        section.State.Should().Be(XRefSectionState.Read);
        section.Incomplete.Should().BeTrue();
        section.TrailerFault.Should().Be(XRefTrailerFault.Missing);
        section.TrailerPosition.Should().Be(file.Length);
        Text(document, 3).Should().Be("object 3");
        document.WasRepaired.Should().BeFalse("the cut table's row places object 3");
        Text(document, 4).Should().Be("object 4");
        document.WasRepaired.Should().BeTrue();
        document.Diagnostics.Select(d => d.Code).Should().Equal(PdfDiagnosticCodes.XRefSectionUnreadable, PdfDiagnosticCodes.XRefRebuilt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("trai")]
    [InlineData("9 1\n")]
    public void A_table_prev_names_whose_row_that_is_no_row_the_end_of_the_file_follows_says_what_its_lost_trailer_would_name_is_found_by_a_rebuild(string tail)
    {
        // The table /Prev names gives object 3's row, then one that is no row, and the file ends — at once, within the trailer
        // keyword, or after a subsection header —: the trailer that would have named the older table indexing object 4 is lost
        // too. The report named the bad row, as it does, but said only the rest of that subsection was looked for by
        // rebuilding the index, as where the trailer follows (#207, #210): it says what the sections the trailer would name
        // index is looked for so too, as a table the end of the file cuts after a row says it.
        var file = PdfTemplate.Build(PartialFiles.CutUpdate("3 2\n{row:3}\njunk junk junk\n" + tail));
        var cut = PdfTemplate.OffsetOf(file, "\nxref\n", 3) + 1;
        var row = PdfTemplate.OffsetOf(file, "junk junk junk");

        using var document = PdfDocument.Open(file);

        document.Diagnostics.Should().ContainSingle().Which.Message.Should().Be(Invariant(
            $"The cross-reference table /Prev names at offset {cut} was read only in part: the row for object 4, at offset {row}, is not an offset, a generation and n or f; the rows read were kept, and what only the rest of the table, or the sections its trailer would name, index is looked for by rebuilding the index."));
        var section = document.Reader.Structure.Sections[^1];
        section.TrailerFault.Should().Be(XRefTrailerFault.Missing);
        section.SubsectionsUnknown.Should().BeTrue();
        Text(document, 3).Should().Be("object 3");
        document.WasRepaired.Should().BeFalse("the cut table's row places object 3");
        Text(document, 4).Should().Be("object 4");
        document.WasRepaired.Should().BeTrue();
    }

    [Fact]
    public void The_table_startxref_names_whose_row_that_is_no_row_the_end_of_the_file_follows_says_what_its_lost_trailer_would_name_is_found_by_a_rebuild()
    {
        // The table startxref names gives the rows of objects 0 to 3, then one that is no row, and the file ends: its trailer is
        // lost, and the report says what it would have named is looked for by rebuilding the index, rather than the rest of
        // that subsection alone (#207, #210). Object 4, which only an older table nothing names indexes, is found so.
        var file = PdfTemplate.Build(PartialFiles.CutFirst.Replace(
            "0 4\n{free}\n{row:1}\n{row:2}\n{row:3}\n", "0 5\n{free}\n{row:1}\n{row:2}\n{row:3}\njunk junk junk\n", StringComparison.Ordinal));
        var cut = PdfTemplate.OffsetOf(file, "\nxref\n", 2) + 1;
        var row = PdfTemplate.OffsetOf(file, "junk junk junk");

        using var document = PdfDocument.Open(file);

        document.Diagnostics.Select(d => d.Code).Should().Equal(PdfDiagnosticCodes.XRefSectionUnreadable, PdfDiagnosticCodes.TrailerRootRecovered);
        document.Diagnostics[0].Message.Should().Be(Invariant(
            $"The cross-reference table startxref names at offset {cut} was read only in part: the row for object 4, at offset {row}, is not an offset, a generation and n or f; the rows read were kept, and what only the rest of the table, or the sections its trailer would name, index is looked for by rebuilding the index."));
        document.WasRepaired.Should().BeFalse();
        Text(document, 4).Should().Be("object 4");
        document.WasRepaired.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("t")]
    [InlineData("trai")]
    public void The_table_startxref_names_that_the_end_of_the_file_cuts_after_a_row_is_kept_and_nothing_rebuilt_at_opening(string tail)
    {
        // The section startxref names is judged by its own rows, as one /Prev names: it read the catalog's, and is kept, read
        // only in part, its trailer lost; the catalog, which no trailer then names, is found among the objects it indexes.
        // Object 4, which only an older table nothing names indexes, is found by rebuilding the index when it is asked for,
        // not at opening (#207). A cut within the trailer keyword is a cut before it: the table was taken for one that cannot
        // be read, and the whole file rebuilt at opening.
        var file = PdfTemplate.Build(PartialFiles.CutFirst + tail);
        var cut = PdfTemplate.OffsetOf(file, "\nxref\n", 2) + 1;

        using var document = PdfDocument.Open(file);

        document.Diagnostics.Select(d => d.Code).Should().Equal(PdfDiagnosticCodes.XRefSectionUnreadable, PdfDiagnosticCodes.TrailerRootRecovered);
        document.Diagnostics[0].Message.Should().StartWith(string.Create(
            CultureInfo.InvariantCulture,
            $"The cross-reference table startxref names at offset {cut} was read only in part: the file ends before its trailer;"));
        document.WasRepaired.Should().BeFalse();
        Text(document, 3).Should().Be("object 3");
        document.WasRepaired.Should().BeFalse();
        Text(document, 4).Should().Be("object 4");
        document.WasRepaired.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("trai")]
    public void An_object_the_table_startxref_names_frees_reads_as_null_though_the_end_of_the_file_cuts_the_table(string tail)
    {
        // The table startxref names frees object 4 in its last row, and the end of the file cuts it before its trailer, or
        // within the trailer keyword: the free row stands, and object 4 reads as null, the older definition no row of the
        // newest revision gives left aside. Taken for a table that cannot be read, the whole file was rebuilt at opening, and
        // object 4 read as the definition the newest revision deleted (#207).
        var file = PdfTemplate.Build(PartialFiles.CutFirst.Replace(
            "0 4\n{free}\n{row:1}\n{row:2}\n{row:3}\n", "0 5\n{free}\n{row:1}\n{row:2}\n{row:3}\n0000000000 00001 f \n", StringComparison.Ordinal) + tail);

        using var document = PdfDocument.Open(file);

        document.GetObject(new PdfObjectId(4)).Should().BeSameAs(PdfNull.Instance);
        document.WasRepaired.Should().BeFalse();
        document.Reader.Structure.Sections.Should().ContainSingle().Which.Incomplete.Should().BeTrue();
    }

    [Theory]
    [InlineData("x", "the keyword x")]
    [InlineData("tx", "the keyword tx")]
    [InlineData("/t", "a name")]
    public void A_token_ending_the_file_that_is_no_start_of_the_trailer_keyword_is_a_stray_one(string tail, string what)
    {
        // The end of the file cuts the table /Prev names after object 3's row, past a token that the trailer keyword does not
        // start: it is no cut keyword, but a stray token where a subsection or the trailer should start, and the table one
        // that is there and cannot be read (#207).
        var file = PdfTemplate.Build(PartialFiles.CutUpdate("3 1\n{row:3}\n" + tail));
        var cut = PdfTemplate.OffsetOf(file, "\nxref\n", 3) + 1;

        using var document = PdfDocument.Open(file);

        document.Diagnostics[0].Message.Should().StartWith(Invariant(
            $"The cross-reference table /Prev names at offset {cut} is there but cannot be read: it holds {what} at offset {file.Length - tail.Length}, where a subsection or the trailer should start."));
        document.Reader.Structure.Sections[^1].State.Should().Be(XRefSectionState.Malformed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("3 1\n")]
    [InlineData("3 1\n00000001")]
    public void A_table_the_end_of_the_file_cuts_before_any_row_is_there_and_cannot_be_read_and_nothing_near_it_is_taken_for_it(string rows)
    {
        // The table /Prev names holds its xref keyword alone, a subsection header, or the start of a row, when the file ends:
        // it read no row, and is no section to keep, but one that is there and cannot be read, reported where it is — not the
        // update's table just before it, which the chain has read (#207, #281).
        var file = PdfTemplate.Build(PartialFiles.CutUpdate(rows));
        var cut = PdfTemplate.OffsetOf(file, "\nxref\n", 3) + 1;

        using var document = PdfDocument.Open(file);

        var report = document.Diagnostics.Should().ContainSingle().Which;
        report.Code.Should().Be(PdfDiagnosticCodes.XRefSectionUnreadable);
        report.Position.Should().Be(cut);
        report.Message.Should().Be(string.Create(
            CultureInfo.InvariantCulture,
            $"The cross-reference table /Prev names at offset {cut} is there but cannot be read: the file ends before its trailer. The rows read before the fault, if any, were kept; the objects only the rest indexes are looked for by rebuilding the index."));
        document.Reader.Structure.Sections[^1].State.Should().Be(XRefSectionState.Malformed);
        Text(document, 4).Should().Be("object 4");
        Text(document, 3).Should().Be("object 3");
        document.WasRepaired.Should().BeTrue();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_table_whose_last_row_is_no_row_is_reported_read_in_part_once_whether_prev_or_startxref_names_it(bool updated)
    {
        // Object 4's row, the table's last, ends in x, and its trailer follows: the table was read, its fault reported
        // nowhere, the rebuild that found object 4 the only report (#210). It is reported once, at the table, read only in part.
        var file = PdfTemplate.Build(PartialFiles.RowEndingInX(updated));
        var table = PdfTemplate.OffsetOf(file, "\nxref\n") + 1;
        var row = PdfTemplate.OffsetOf(file, string.Create(CultureInfo.InvariantCulture, $"{PdfTemplate.OffsetOf(file, "4 0 obj")} 00000 x"));

        using var document = PdfDocument.Open(file);

        var report = document.Diagnostics.Should().ContainSingle().Which;
        report.Code.Should().Be(PdfDiagnosticCodes.XRefSectionUnreadable);
        report.Severity.Should().Be(PdfDiagnosticSeverity.Warning);
        report.Position.Should().Be(table);
        report.Message.Should().Be(string.Create(
            CultureInfo.InvariantCulture,
            $"The cross-reference table {(updated ? "/Prev" : "startxref")} names at offset {table} was read only in part: the row for object 4, at offset {row}, is not an offset, a generation and n or f; the rows read were kept, and the objects the rest of that subsection numbers are looked for by rebuilding the index where no section read before it indexes them, rather than taken from an older section's rows."));
        Text(document, 3).Should().Be("object 3");
        document.WasRepaired.Should().BeFalse();
        Text(document, 4).Should().Be("object 4");
        document.WasRepaired.Should().BeTrue();
        document.Diagnostics.Select(d => d.Code).Should().Equal(PdfDiagnosticCodes.XRefSectionUnreadable, PdfDiagnosticCodes.XRefRebuilt);
    }

    [Theory]
    [InlineData(true, "/XRefStm")]
    [InlineData(false, "startxref")]
    public void A_cross_reference_stream_holding_fewer_rows_than_it_declares_is_reported_once_and_no_older_row_stands_for_one_it_lacks(
        bool hybrid, string naming)
    {
        // The stream declares objects 4, 5 and 7, and its data holds the rows of 4 and 5: object 7's row, which gives its
        // newer revision, is lost. It was read in silence, and the oldest table's row served the older revision (#210). The
        // stream is reported once, read only in part, and no older row stands for a number it declares: object 7 is found by
        // rebuilding the index, its newer revision, while object 3, which no stream declares, is read through the oldest
        // table's row.
        var (whole, _, _, _) = PartialFiles.StreamOverOlderRows(hybrid: hybrid);
        var (file, _, stream, _) = PartialFiles.StreamOverOlderRows(rows: 2, hybrid: hybrid);

        using (var sound = PdfDocument.Open(whole))
        {
            sound.Diagnostics.Should().BeEmpty();
            Text(sound, 7).Should().Be("new seven");
            Text(sound, 3).Should().Be("only in the oldest");
            sound.WasRepaired.Should().BeFalse();
        }

        using var document = PdfDocument.Open(file);

        var report = document.Diagnostics.Should().ContainSingle().Which;
        report.Code.Should().Be(PdfDiagnosticCodes.XRefSectionUnreadable);
        report.Position.Should().Be(stream);
        report.Message.Should().Be(string.Create(
            CultureInfo.InvariantCulture,
            $"The cross-reference stream {naming} names at offset {stream} was read only in part: it holds 2 rows where its /Index and /Size declare 3; the rows it holds were kept, and the objects whose rows it lacks are looked for by rebuilding the index where no section read before it indexes them, rather than taken from an older section's rows."));
        Text(document, 3).Should().Be("only in the oldest");
        Text(document, 4).Should().Be("only in the stream");
        document.WasRepaired.Should().BeFalse();
        document.Reader.ChainIndex!.TryGet(7, out _).Should().BeFalse("the oldest table's row for object 7 does not stand");
        Text(document, 7).Should().Be("new seven");
        document.WasRepaired.Should().BeTrue();
    }

    [Theory]
    [InlineData(1, true, "/XRefStm", 0)]
    [InlineData(1, false, "startxref", 0)]
    [InlineData(0, true, "/XRefStm", 0)]
    [InlineData(0, false, "startxref", 0)]
    [InlineData(1, true, "/XRefStm", 2)]
    public void A_cross_reference_stream_holding_one_row_or_none_of_those_it_declares_is_reported_in_words_that_agree_with_the_count(
        int rows, bool hybrid, string naming, int shift)
    {
        // As above, with the row of object 4 alone, or none: the report said of the one row the stream holds that the rows
        // it holds were kept, and of none that they were kept too. It agrees with the count, as every count the messages
        // give does (#241): the row it holds was kept, or, of none, nothing is said kept — where the stream is found moved
        // too, the /XRefStm naming it 2 bytes past its header.
        var (file, _, stream, _) = PartialFiles.StreamOverOlderRows(rows: rows, hybrid: hybrid);
        file = Encoding.Latin1.GetBytes(Encoding.Latin1.GetString(file).Replace(
            Invariant($"/XRefStm {stream} "), Invariant($"/XRefStm {stream + shift} "), StringComparison.Ordinal));
        var named = shift == 0 ? Invariant($"{stream}") : Invariant($"{stream + shift}, found at offset {stream},");
        var kept = rows == 1 ? "1 row where its /Index and /Size declare 3; the row it holds was kept, and" : "0 rows where its /Index and /Size declare 3;";

        using var document = PdfDocument.Open(file);

        document.Diagnostics.Should().ContainSingle(d => d.Code == PdfDiagnosticCodes.XRefSectionUnreadable).Which.Message.Should().Be(Invariant(
            $"The cross-reference stream {naming} names at offset {named} was read only in part: it holds {kept} the objects whose rows it lacks are looked for by rebuilding the index where no section read before it indexes them, rather than taken from an older section's rows."));
        document.Diagnostics.Select(d => d.Code).Should().Equal(shift == 0
            ? [PdfDiagnosticCodes.XRefSectionUnreadable]
            : [PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefSectionUnreadable]);
        document.Reader.ChainIndex!.TryGet(4, out _).Should().Be(rows == 1);
        document.Reader.ChainIndex!.TryGet(7, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(
        "/Index [4 2 7 1]",
        "its /W gives a field a negative width",
        "The objects it numbers that no section read before it indexes are looked for by rebuilding the index, rather than taken from an older section's rows.")]
    [InlineData(
        "/Index [4 2 7 1 0 R]",
        "its /Index holds the reference 1 0 R, which no section read before it places where it can be read",
        "Every object no section read before it indexes is looked for by rebuilding the index, rather than taken from an older section's rows.")]
    public void A_hybrid_file_s_stream_that_cannot_be_read_leaves_no_older_row_standing_for_the_numbers_it_declares(string index, string fault, string lost)
    {
        // The stream the hybrid table names through /XRefStm declares objects 4, 5 and 7, and cannot be read: its /W gives a
        // negative width, or its /Index holds a reference no section read before it places. Its rows are lost, and the oldest
        // table's row for object 7 served the older revision. No older row stands for a number it declares — for any, where
        // its numbering is written with a reference, which ISO 32000-1 makes direct —, which is found by rebuilding the index
        // (#210).
        var (file, _, stream, _) = PartialFiles.StreamOverOlderRows("/Type /XRef /W [1 -4 2] " + index, hybrid: true);

        using var document = PdfDocument.Open(file);

        var report = document.Diagnostics[0];
        report.Code.Should().Be(PdfDiagnosticCodes.XRefSectionUnreadable);
        report.Position.Should().Be(stream);
        report.Message.Should().Be(string.Create(
            CultureInfo.InvariantCulture,
            $"The cross-reference stream /XRefStm names at offset {stream} is there but cannot be read: {fault}. {lost}"));
        document.Reader.Structure.Sections.Should().HaveCount(3, "the chain goes on through the table's /Prev");
        document.Reader.ChainIndex!.TryGet(7, out _).Should().BeFalse();
        document.Reader.ChainIndex!.TryGet(3, out _).Should().Be(index == "/Index [4 2 7 1]");
        Text(document, 7).Should().Be("new seven");
        Text(document, 3).Should().Be("only in the oldest");
    }

    [Theory]
    [InlineData("[4 1 6 2]", "2", false, "the rows it holds were kept, and the objects whose rows it lacks are looked for by rebuilding the index where no section read before it indexes them, rather than taken from an older section's rows")]
    [InlineData("[4 1 6 9 0 R]", "2", false, "the rows it holds were kept, and every other object no section read before it indexes is looked for by rebuilding the index, rather than taken from an older section's rows")]
    [InlineData("9 0 R", "[4 1 6 2]", false, "the rows it holds were kept, and every other object no section read before it indexes is looked for by rebuilding the index, rather than taken from an older section's rows")]
    [InlineData("[4 1 6 2]", "2", true, "the rows it holds were kept, and the objects whose rows it lacks are looked for by rebuilding the index where no section read before it indexes them, rather than taken from an older section's rows")]
    [InlineData("[4 1 6 9 0 R]", "2", true, "the rows it holds were kept, and every other object no section read before it indexes is looked for by rebuilding the index, rather than taken from an older section's rows")]
    public void A_stream_read_in_part_whose_numbering_is_written_with_references_says_every_other_object_is_looked_for_by_rebuilding_the_index(
        string index, string nine, bool hybrid, string said)
    {
        // Cross-reference stream 6, which the newest table names through /Prev, or through /XRefStm in a hybrid file, declares
        // objects 4, 6 and 7, and holds the rows of 4 and 6: read in part. Its /Index holds the reference 9 0 R, or is it,
        // which the newest table places, where ISO 32000-1 makes it direct: the numbers the stream lost are not kept apart, and
        // every number is refused to the older sections' rows, the file rebuilt as it opens for the catalog, which only the
        // oldest table indexes (#210). The stream was reported before that refusal, and said only the objects whose rows it
        // lacks were looked for by rebuilding the index; reported once it refused them, it says every other object is. With a
        // direct /Index, the oldest table's rows for the catalog and object 3 stand.
        var (file, stream, _) = PartialFiles.NumberedThroughAReference(index, nine, hybrid: hybrid);
        var every = index != "[4 1 6 2]";

        using var document = PdfDocument.Open(file);

        var report = document.Diagnostics[0];
        report.Code.Should().Be(PdfDiagnosticCodes.XRefSectionUnreadable);
        report.Position.Should().Be(stream);
        report.Message.Should().Be(Invariant(
            $"The cross-reference stream {(hybrid ? "/XRefStm" : "/Prev")} names at offset {stream} was read only in part: it holds 2 rows where its /Index and /Size declare 3; {said}."));
        document.Reader.ChainIndex!.TryGet(4, out _).Should().BeTrue();
        document.Reader.ChainIndex!.TryGet(3, out _).Should().Be(!every, "object 3 is the oldest table's alone");
        document.WasRepaired.Should().Be(every, "the catalog is the oldest table's alone");
        Text(document, 3).Should().Be("only in the oldest");
        document.WasRepaired.Should().Be(every);
    }

    [Theory]
    [InlineData(1, false, false, "1 row where its /Index and /Size declare 3; the row it holds was kept, and every other object no section read before it indexes is looked for by rebuilding the index, rather than taken from an older section's rows")]
    [InlineData(1, true, false, "1 row where its /Index and /Size declare 3; the row it holds was refused, and every object no section read before it indexes is looked for by rebuilding the index, rather than taken from an older section's rows")]
    [InlineData(1, false, true, "1 row where its /Index and /Size declare 3; the row it holds does not stand, a newer section having refused its number, and every object no section read before it indexes is looked for by rebuilding the index, rather than taken from an older section's rows")]
    [InlineData(0, false, false, "0 rows where its /Index and /Size declare 3; every object no section read before it indexes is looked for by rebuilding the index, rather than taken from an older section's rows")]
    public void A_stream_read_in_part_whose_numbering_holds_a_reference_says_every_object_but_a_row_it_kept_is_looked_for_whatever_rows_it_holds(
        int rows, bool refused, bool fourRefused, string said)
    {
        // As above, through /Prev, the stream holding object 4's row alone, or none. The row places object 4; or gives it type
        // 2 in object stream 0, which no entry holds, and the stream refuses it itself; or follows the newest table's own row
        // for object 4, which gives it generation 65536 and is refused, its number with it. The report said only the objects
        // whose rows the stream lacks, or that it numbers, were looked for by rebuilding the index, though it refused every
        // number: it says every other object is, beside the row it kept, and every object, where it kept none (#210).
        var (file, stream, _) = PartialFiles.NumberedThroughAReference("[4 1 6 9 0 R]", "2", rows, refused: refused, fourRefused: fourRefused);

        using var document = PdfDocument.Open(file);

        document.Diagnostics.Should().ContainSingle(diagnostic => diagnostic.Code == PdfDiagnosticCodes.XRefSectionUnreadable && diagnostic.Position == stream).Which.Message.Should().Be(Invariant(
            $"The cross-reference stream /Prev names at offset {stream} was read only in part: it holds {said}."));
        document.Reader.ChainIndex!.TryGet(4, out _).Should().Be(rows == 1 && !refused && !fourRefused);
        document.Reader.ChainIndex!.TryGet(3, out _).Should().BeFalse("object 3 is the oldest table's alone");
        document.WasRepaired.Should().BeTrue("the catalog is the oldest table's alone");
        Text(document, 3).Should().Be("only in the oldest");
        Text(document, 4).Should().Be("four");
    }

    [Theory]
    [InlineData(4095, true, "the row for object 4, at offset {row}, is not an offset, a generation and n or f; the rows read were kept, and the objects the rest of that subsection numbers are looked for by rebuilding the index where no section read before it indexes them, rather than taken from an older section's rows")]
    [InlineData(4096, true, "the row for object 4, at offset {row}, is not an offset, a generation and n or f; the rows read were kept, and every other object no section read before it indexes is looked for by rebuilding the index, rather than taken from an older section's rows")]
    [InlineData(4095, false, "it holds 2 rows where its /Index and /Size declare 3; the rows it holds were kept, and the objects whose rows it lacks are looked for by rebuilding the index where no section read before it indexes them, rather than taken from an older section's rows")]
    [InlineData(4096, false, "it holds 2 rows where its /Index and /Size declare 3; the rows it holds were kept, and every other object no section read before it indexes is looked for by rebuilding the index, rather than taken from an older section's rows")]
    public void A_section_read_in_part_whose_lost_numbers_pass_the_bound_once_joined_with_those_refused_before_says_every_other_object_is_looked_for(
        int ranges, bool table, string said)
    {
        // The newest table names through /Prev a cross-reference stream whose rows cannot be read, which declares one-row
        // subsections every other number from 1000, 4,095 or 4,096 of them, refused to the older sections' rows (#208). Its
        // /Prev names a section read in part: a table whose subsection 3 2 ends in a row that is no row, losing object 4; or a
        // stream that declares objects 3 to 5 and holds the rows of 3 and 4. Joined with the ranges refused before, the range
        // it lost makes 4,096 ranges, kept apart, or 4,097, past the bound, and every number is refused, object 6, which only
        // the oldest table indexes, read through a rebuild. The report said only the rest of the subsection, or the objects
        // whose rows the stream lacks, were looked for so; it says every other object is, past the bound (#210).
        var declared = new StringBuilder("[");

        for (var i = 0; i < ranges; i++)
        {
            declared.Append(CultureInfo.InvariantCulture, $"{1000 + (2 * i)} 1 ");
        }

        var (file, middle, _) = PartialFiles.PastALostStream(
            declared.Append(']').ToString(),
            table ? "3 2\n{three} 00000 n \n0000000010 00000 x \ntrailer\n<< /Size 7 /Prev {oldest} >>\n" : null,
            streamRows: 2);
        var every = ranges == 4096;
        var row = table ? PdfTemplate.OffsetOf(file, "0000000010 00000 x") : -1;

        using var document = PdfDocument.Open(file);

        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(PdfDiagnosticCodes.XRefSectionUnreadable, PdfDiagnosticCodes.XRefSectionUnreadable);
        document.Diagnostics[1].Position.Should().Be(middle);
        document.Diagnostics[1].Message.Should().Be(Invariant(
            $"The cross-reference {(table ? "table" : "stream")} /Prev names at offset {middle} was read only in part: {said.Replace("{row}", Invariant($"{row}"), StringComparison.Ordinal)}."));
        document.Reader.ChainIndex!.TryGet(6, out _).Should().Be(!every, "object 6 is the oldest table's alone");
        document.Reader.ChainIndex!.TryGet(3, out _).Should().BeTrue("the section read in part places object 3");
        document.WasRepaired.Should().BeFalse("the newest table places the catalog");
        Text(document, 6).Should().Be("old six");
        document.WasRepaired.Should().Be(every);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_stream_read_whole_whose_index_a_newer_section_places_leaves_the_older_rows_standing(bool hybrid)
    {
        // The newest table places object 8, [5 2], and names, through /Prev or as a hybrid file's /XRefStm, cross-reference
        // stream 6, whose /Index is the reference 8 0 R, which ISO 32000-1 makes direct and the reader follows where a section
        // read before places it: the stream reads whole, the rows of objects 5 and 6. Only a section that lost rows refuses
        // the numbers it declares to the older sections' rows — every number, where its numbering is written with a reference
        // —: the oldest table's rows for the catalog, the page tree and object 3 stand (#208, #210).
        var file = new HandWrittenFile(version: "1.5");
        var catalog = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        var pages = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        var three = file.Object(3, "(three)");
        var oldest = file.Table(4, new Dictionary<int, long> { [1] = catalog, [2] = pages, [3] = three }, "/Size 4 /Root 1 0 R");
        var five = file.Object(5, "(five)");
        var stream = file.Position;
        var data = new StringBuilder();

        foreach (var offset in new[] { five, stream })
        {
            data.Append('\u0001').Append((char)((offset >> 24) & 0xFF)).Append((char)((offset >> 16) & 0xFF))
                .Append((char)((offset >> 8) & 0xFF)).Append((char)(offset & 0xFF)).Append('\0').Append('\0');
        }

        var prev = hybrid ? string.Empty : Invariant($" /Prev {oldest}");
        file.Object(6, Invariant($"<< /Type /XRef /Size 7 /Index 8 0 R /W [1 4 2]{prev} /Length {data.Length} >>\nstream\n{data}\nendstream"));
        var eight = file.Object(8, "[5 2]");
        var newest = file.Position;
        var links = hybrid ? Invariant($"/Prev {oldest} /XRefStm {stream}") : Invariant($"/Prev {stream}");
        file.Raw(Invariant($"xref\n0 1\n0000000000 65535 f \n8 1\n{eight:D10} 00000 n \ntrailer\n<< /Size 9 /Root 1 0 R {links} >>\nstartxref\n{newest}\n%%EOF\n"));

        using var document = PdfDocument.Open(file.Bytes);

        document.Reader.Structure.Sections.Should().HaveCount(3).And.OnlyContain(section => section.State == XRefSectionState.Read);
        var index = document.Reader.ChainIndex!;
        index.TryGet(5, out _).Should().BeTrue();
        index.TryGet(3, out _).Should().BeTrue();
        index.TryGet(1, out _).Should().BeTrue();
        document.WasRepaired.Should().BeFalse();
        Text(document, 3).Should().Be("three");
    }

    [Fact]
    public void A_table_read_in_part_past_a_stream_that_refused_every_number_is_said_to_leave_every_object_to_a_rebuild()
    {
        // The newest table names through /Prev a cross-reference stream whose rows cannot be read and whose /Index is no array:
        // every number is refused to the older sections' rows (#208). The stream's /Prev names the oldest table, whose last
        // row, object 4's, ends in x: read in part, it refuses the rest of that subsection, which every number includes
        // already. None of its rows stands, and its report says so, and that every object no section read before it indexes
        // is looked for by rebuilding the index, rather than the rest of the subsection alone; it said the rows read were
        // kept (#210).
        var file = new HandWrittenFile(version: "1.5");
        var catalog = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        var pages = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        var three = file.Object(3, "(only in the oldest)");
        var oldest = file.Position;
        file.Raw(Invariant($"xref\n0 5\n0000000000 65535 f \n{catalog:D10} 00000 n \n{pages:D10} 00000 n \n{three:D10} 00000 n \n0000000000 00000 x \ntrailer\n<< /Size 5 >>\n"));
        var stream = file.Object(6, Invariant($"<< /Type /XRef /Size 7 /Index /X /W [1 -4 2] /Prev {oldest} /Length 0 >>\nstream\n\nendstream"));
        var newest = file.Position;
        file.Raw(Invariant($"xref\n0 3\n0000000000 65535 f \n{catalog:D10} 00000 n \n{pages:D10} 00000 n \ntrailer\n<< /Size 7 /Root 1 0 R /Prev {stream} >>\nstartxref\n{newest}\n%%EOF\n"));
        var bytes = file.Bytes;
        var row = PdfTemplate.OffsetOf(bytes, "0000000000 00000 x");

        using var document = PdfDocument.Open(bytes);

        document.Diagnostics.Select(d => d.Message).Should().Equal(
            Invariant($"The cross-reference stream /Prev names at offset {stream} is there but cannot be read: its /W gives a field a negative width. The chain goes on through its /Prev; every object no section read before it indexes is looked for by rebuilding the index, rather than taken from an older section's rows."),
            Invariant($"The cross-reference table /Prev names at offset {oldest} was read only in part: the row for object 4, at offset {row}, is not an offset, a generation and n or f; none of the rows read stands, a newer section having refused every number to the older sections' rows, and every object no section read before it indexes is looked for by rebuilding the index."));
        var table = document.Reader.Structure.Sections[^1];
        table.Offset.Should().Be(oldest);
        table.State.Should().Be(XRefSectionState.Read);
        table.Incomplete.Should().BeTrue();
        document.Reader.ChainIndex!.TryGet(3, out _).Should().BeFalse();
        document.WasRepaired.Should().BeFalse();
        Text(document, 3).Should().Be("only in the oldest");
        document.WasRepaired.Should().BeTrue();
    }

    [Theory]
    [InlineData("/X", "none of the rows read stands, a newer section having refused every number to the older sections' rows, and every object no section read before it indexes is looked for by rebuilding the index", false, false)]
    [InlineData("[3 2]", "the rows read were kept but for those whose numbers a newer section refused, and the objects the rest of that subsection numbers are looked for by rebuilding the index where no section read before it indexes them, rather than taken from an older section's rows", false, false)]
    [InlineData("[3 1]", "the rows read were kept but for those whose numbers a newer section refused, and the objects the rest of that subsection numbers are looked for by rebuilding the index where no section read before it indexes them, rather than taken from an older section's rows", false, true)]
    [InlineData("[10 1]", "the rows read were kept, and the objects the rest of that subsection numbers are looked for by rebuilding the index where no section read before it indexes them, rather than taken from an older section's rows", true, true)]
    public void A_table_read_in_part_past_a_lost_stream_says_which_of_its_rows_stand(string index, string said, bool three, bool four)
    {
        // The newest table names through /Prev a cross-reference stream whose rows cannot be read: it refuses to the older
        // sections' rows every number, its /Index being no array, or the numbers its /Index declares (#208). Its /Prev names a
        // table whose rows for objects 3 and 4 are followed by one that is no row, read in part: it was said to have kept the
        // rows read, though none of them, or not those whose numbers the stream refused, stood. It says which stand, and the
        // objects whose rows do not are read through a rebuild; a stream that declares object 10 alone leaves them all (#210).
        var (file, middle, _) = PartialFiles.PastALostStream(index, "3 3\n{three} 00000 n \n{four} 00000 n \njunk junk junk\ntrailer\n<< /Size 7 /Prev {oldest} >>\n");
        var row = PdfTemplate.OffsetOf(file, "junk junk junk");

        using var document = PdfDocument.Open(file);

        document.Diagnostics.Select(d => d.Code).Should().Equal(PdfDiagnosticCodes.XRefSectionUnreadable, PdfDiagnosticCodes.XRefSectionUnreadable);
        document.Diagnostics[1].Position.Should().Be(middle);
        document.Diagnostics[1].Message.Should().Be(Invariant(
            $"The cross-reference table /Prev names at offset {middle} was read only in part: the row for object 5, at offset {row}, is not an offset, a generation and n or f; {said}."));
        ShouldServeTheNewerRevisions(document, three, four);
    }

    [Theory]
    [InlineData(2, "/X", "none of the rows it holds stands, a newer section having refused every number to the older sections' rows, and every object no section read before it indexes is looked for by rebuilding the index", false)]
    [InlineData(2, "[3 2]", "the rows it holds were kept but for those whose numbers a newer section refused, and the objects whose rows it lacks are looked for by rebuilding the index where no section read before it indexes them, rather than taken from an older section's rows", false)]
    [InlineData(2, "[10 1]", "the rows it holds were kept, and the objects whose rows it lacks are looked for by rebuilding the index where no section read before it indexes them, rather than taken from an older section's rows", true)]
    [InlineData(1, "/X", "the row it holds does not stand, a newer section having refused every number to the older sections' rows, and every object no section read before it indexes is looked for by rebuilding the index", false)]
    [InlineData(1, "[3 2]", "the row it holds does not stand, a newer section having refused its number, and the objects whose rows it lacks are looked for by rebuilding the index where no section read before it indexes them, rather than taken from an older section's rows", false)]
    [InlineData(1, "[10 1]", "the row it holds was kept, and the objects whose rows it lacks are looked for by rebuilding the index where no section read before it indexes them, rather than taken from an older section's rows", true)]
    [InlineData(0, "/X", "every object no section read before it indexes is looked for by rebuilding the index, a newer section having refused every number to the older sections' rows", false)]
    [InlineData(0, "[10 1]", "the objects whose rows it lacks are looked for by rebuilding the index where no section read before it indexes them, rather than taken from an older section's rows", false)]
    public void A_stream_read_in_part_past_a_lost_stream_says_which_of_the_rows_it_holds_stand(int rows, string index, string said, bool three)
    {
        // A cross-reference stream the lost stream's /Prev names declares objects 3 to 5, and holds the rows of the newer
        // objects 3 and 4, of object 3 alone, or none: it was said to have kept the rows it holds, though the lost stream
        // refused every number, or theirs, and none of them stood. It says which stand; object 4, whose row it lacks or the
        // lost stream refused, is read through a rebuild, as object 3 is where its row does not stand (#208, #210).
        var (file, middle, _) = PartialFiles.PastALostStream(index, table: null, streamRows: rows);

        using var document = PdfDocument.Open(file);

        document.Diagnostics[1].Position.Should().Be(middle);
        document.Diagnostics[1].Message.Should().Be(Invariant(
            $"The cross-reference stream /Prev names at offset {middle} was read only in part: it holds {rows} {(rows == 1 ? "row" : "rows")} where its /Index and /Size declare 3; {said}."));
        ShouldServeTheNewerRevisions(document, three, four: rows == 2 && three);
    }

    [Theory]
    [InlineData("/X", false, "none of the rows read stands, a newer section having refused every number to the older sections' rows, and every object no section read before it indexes is looked for by rebuilding the index", false)]
    [InlineData("[3 2]", false, "that row was refused, the rows after it read, and the other rows read kept but for those it refused and those whose numbers a newer section refused; its object is looked for by rebuilding the index where no row read before it indexes it, rather than taken from an older section's row", false)]
    [InlineData("[10 1]", false, "that row alone was refused, the rows after it read, and its object is looked for by rebuilding the index where no row read before it indexes it, rather than taken from an older section's row", true)]
    [InlineData("/X", true, "none of the rows read, as far as one of the reader's limits let the section be read, stands, a newer section having refused every number to the older sections' rows, and every object no section read before it indexes is looked for by rebuilding the index", false)]
    [InlineData("[3 2]", true, "that row was refused, the rows after it read as far as one of the reader's limits let the section be read, and the other rows read kept but for those it refused and those whose numbers a newer section refused; its object is looked for by rebuilding the index where no row read before it indexes it, rather than taken from an older section's row", false)]
    [InlineData("[10 1]", true, "that row alone was refused, the rows after it read as far as one of the reader's limits let the section be read, and its object is looked for by rebuilding the index where no row read before it indexes it, rather than taken from an older section's row", true)]
    [InlineData("[4 1]", false, "that row was refused, the rows after it read, and the other rows read kept but for those it refused and those whose numbers a newer section refused; its object is looked for by rebuilding the index where no row read before it indexes it, rather than taken from an older section's row", false, "3 2\n{three} 65536 n \n{four} 00000 n \n3 1\n{three} 00000 n \n")]
    [InlineData("[4 1]", false, "that row was refused, the rows after it read, and the other rows read kept but for those it refused and those whose numbers a newer section refused; its object is looked for by rebuilding the index where no row read before it indexes it, rather than taken from an older section's row", false, "3 2\n{three} 65536 n \n{four} 00000 n \n5 1\n0000000009 65536 n \n", false, 5)]
    [InlineData("[4 1]", false, "that row was refused, the rows after it read, and the other rows read kept but for those it refused and those whose numbers a newer section refused; its object is looked for by rebuilding the index where no row read before it indexes it, rather than taken from an older section's row", false, "3 2\n{three} 00000 n \n{four} 00000 n \n3 1\n{three} 65536 n \n", true)]
    public void A_table_that_refused_a_row_past_a_lost_stream_says_it_alone_was_refused_only_where_the_others_stand(
        string index, bool guarded, string said, bool four, string rows = "3 2\n{three} 65536 n \n{four} 00000 n \n", bool three = false, int? refusedToo = null)
    {
        // The table the lost stream's /Prev names gives object 3 generation 65536, which no entry holds, and object 4 its newer
        // revision: object 3's row was said to be the one refused, the rows after it read, though the lost stream refused
        // every number, or object 4's, and object 4's row did not stand either (#208, #210). Where MaxXRefSectionLength cuts
        // the free rows after them, the report says how far the rows after it were read, as it does otherwise. Past a lost
        // stream declaring object 4, a row the table refused itself after the one named — a later row for object 3, which
        // it gives twice, or one giving object 5 generation 65536 — was said to be kept beside the rows the stream refused,
        // though it did not stand: it is not (#210). Where the row placing object 3 comes first, that row stands, and object
        // 3 is read through it.
        var free = guarded ? "20 20\n" + string.Concat(Enumerable.Repeat("0000000000 00000 f \n", 20)) : string.Empty;
        var (file, middle, _) = PartialFiles.PastALostStream(index, rows + free + "trailer\n<< /Size 40 /Prev {oldest} >>\n");
        var row = PdfTemplate.OffsetOf(file, "65536 n") - 11;

        using var document = guarded
            ? PdfDocument.Open(file, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxXRefSectionLength = 150 } })
            : PdfDocument.Open(file);

        document.Diagnostics.Should().ContainSingle(d => d.Code == PdfDiagnosticCodes.XRefSectionUnreadable && d.Position == middle).Which.Message.Should().Be(Invariant(
            $"The cross-reference table /Prev names at offset {middle} was read only in part: the row for object 3, at offset {row}, gives it generation 65536, outside 0 to 65535; {said}."));
        document.Reader.Structure.Sections[^1].CutByLimit.Should().Be(guarded);

        if (refusedToo is { } number)
        {
            document.Reader.ChainIndex!.TryGet(number, out _).Should().BeFalse();
        }

        if (!guarded)
        {
            ShouldServeTheNewerRevisions(document, three, four);
        }
    }

    [Theory]
    [InlineData(true, false, "three")]
    [InlineData(false, false, "three")]
    [InlineData(true, true, "new three")]
    [InlineData(false, true, "new three")]
    public void A_table_that_gives_a_number_twice_and_refused_one_of_the_rows_says_no_newer_section_refused_the_other(bool refusedFirst, bool older, string expected)
    {
        // The table startxref names gives object 3 twice, one row giving it generation 65536, which no entry holds. With the
        // refused row first, the other did not stand either, and the report said a newer section had refused it, where there
        // is none; with the placing row first, that row stands, and the report said the object was looked for by rebuilding
        // the index. It says the refused row's object is looked for so where no row read before it indexes it, which the
        // table's own row does in the second order: the object is read through it, an older table's row standing for
        // nothing either way (#210).
        var (file, table) = PartialFiles.GivenTwice(refusedFirst, older);
        var row = PdfTemplate.OffsetOf(file, "65536 n") - 11;

        using var document = PdfDocument.Open(file);

        document.Diagnostics.Should().ContainSingle().Which.Message.Should().Be(Invariant(
            $"The cross-reference table startxref names at offset {table} was read only in part: the row for object 3, at offset {row}, gives it generation 65536, outside 0 to 65535; that row alone was refused, the rows after it read, and its object is looked for by rebuilding the index where no row read before it indexes it, rather than taken from an older section's row."));
        document.Reader.Structure.Sections[0].RowsRefusedBefore.Should().BeFalse();
        document.Reader.ChainIndex!.TryGet(3, out _).Should().Be(!refusedFirst);
        Text(document, 3).Should().Be(expected);
        document.WasRepaired.Should().Be(refusedFirst);
    }

    [Fact]
    public void A_table_that_refuses_its_row_for_a_number_twice_refuses_the_number_once()
    {
        // The table startxref names gives object 3 generation 65536 in two subsections, an older table placing its older
        // revision: the number is refused once, the second row changing nothing, and the object is read through a rebuild,
        // no older row standing for it (#210).
        var (file, table) = PartialFiles.OwnRefusal(
            "3 2\n{three} 65536 n \n{four} 00000 n \n3 1\n{three} 65536 n \ntrailer\n<< /Size 5 /Root 1 0 R /Prev {oldest} >>\n");
        var row = PdfTemplate.OffsetOf(file, "65536 n") - 11;

        using var document = PdfDocument.Open(file);

        document.Diagnostics.Should().ContainSingle().Which.Message.Should().Be(Invariant(
            $"The cross-reference table startxref names at offset {table} was read only in part: the row for object 3, at offset {row}, gives it generation 65536, outside 0 to 65535; that row alone was refused, the rows after it read, and its object is looked for by rebuilding the index where no row read before it indexes it, rather than taken from an older section's row."));
        ShouldServeTheNewerRevisions(document, three: false, four: true);
    }

    [Theory]
    [InlineData("stream holding one row", "it holds 1 row where its /Index and /Size declare 2; the row it holds was refused, and the objects it numbers are looked for by rebuilding the index where no section read before it indexes them, rather than taken from an older section's rows", false)]
    [InlineData("stream holding two rows", "it holds 2 rows where its /Index and /Size declare 3; the rows it holds were kept but for those it refused, and the objects whose rows it lacks are looked for by rebuilding the index where no section read before it indexes them, rather than taken from an older section's rows", true)]
    [InlineData("table with a row that is no row", "{fault}; the rows read were kept but for those it refused, and the objects the rest of that subsection numbers are looked for by rebuilding the index where no section read before it indexes them, rather than taken from an older section's rows", true)]
    public void A_section_read_in_part_does_not_say_it_kept_a_row_it_refused_itself(string shape, string said, bool four)
    {
        // The section startxref names refuses its own row for object 3, which gives it generation 65536, and loses rows too:
        // the stream holds fewer rows than it declares, the table's row for object 5 is no row. The report names the fault
        // that lost rows, not the refused row, as it does, but said the rows read were kept, the refused one with them, though
        // no older section's row stood for object 3: it says they were kept but for those it refused, and a stream's one row
        // that it refused is said to be refused (#210). Object 3 is read through a rebuild.
        var (file, section) = shape switch
        {
            "stream holding one row" => PartialFiles.OwnRefusal(table: null, held: 1, declared: 2),
            "stream holding two rows" => PartialFiles.OwnRefusal(table: null, held: 2, declared: 3),
            _ => PartialFiles.OwnRefusal("3 3\n{three} 65536 n \n{four} 00000 n \njunk junk junk\ntrailer\n<< /Size 6 /Root 1 0 R /Prev {oldest} >>\n"),
        };
        var kind = shape.StartsWith("stream", StringComparison.Ordinal) ? "stream" : "table";
        var fault = shape.StartsWith("table", StringComparison.Ordinal)
            ? Invariant($"the row for object 5, at offset {PdfTemplate.OffsetOf(file, "junk junk junk")}, is not an offset, a generation and n or f")
            : string.Empty;

        using var document = PdfDocument.Open(file);

        document.Diagnostics.Should().ContainSingle().Which.Message.Should().Be(Invariant(
            $"The cross-reference {kind} startxref names at offset {section} was read only in part: {said.Replace("{fault}", fault, StringComparison.Ordinal)}."));
        ShouldServeTheNewerRevisions(document, three: false, four);
    }

    [Theory]
    [InlineData("[10 1]", "the rows read were kept but for those it refused")]
    [InlineData("[4 1]", "the rows read were kept but for those it refused and those whose numbers a newer section refused")]
    public void A_table_read_in_part_past_a_lost_stream_tells_its_own_refused_rows_from_those_a_newer_section_refused(string index, string kept)
    {
        // The table the lost stream's /Prev names refuses its own row for object 3, generation 65536, and its row for object 5
        // is no row. The lost stream declares object 10, or object 4, whose row the table gives too: the report says the rows
        // read were kept but for those the table refused, and those whose numbers the lost stream refused where it refused
        // any (#208, #210).
        var (file, middle, _) = PartialFiles.PastALostStream(index, "3 3\n{three} 65536 n \n{four} 00000 n \njunk junk junk\ntrailer\n<< /Size 7 /Prev {oldest} >>\n");
        var row = PdfTemplate.OffsetOf(file, "junk junk junk");

        using var document = PdfDocument.Open(file);

        document.Diagnostics[1].Position.Should().Be(middle);
        document.Diagnostics[1].Message.Should().Be(Invariant(
            $"The cross-reference table /Prev names at offset {middle} was read only in part: the row for object 5, at offset {row}, is not an offset, a generation and n or f; {kept}, and the objects the rest of that subsection numbers are looked for by rebuilding the index where no section read before it indexes them, rather than taken from an older section's rows."));
        ShouldServeTheNewerRevisions(document, three: false, four: index == "[10 1]");
    }

    [Fact]
    public void A_table_that_cannot_be_read_does_not_say_it_kept_a_row_it_refused_itself_before_its_fault()
    {
        // The table the lost stream's /Prev names refuses its own row for object 3, generation 65536, then runs into a row that
        // is no row and a stray token: it cannot be read. Its rows read before the fault were said kept, the refused one with
        // them: they were kept but for those it refused, and object 3 is read through a rebuild (#210).
        var (file, middle, _) = PartialFiles.PastALostStream("[10 1]", "3 3\n{three} 65536 n \n{four} 00000 n \njunk junk junk\nmore\ntrailer\n<< /Size 7 /Prev {oldest} >>\n");
        var row = PdfTemplate.OffsetOf(file, "junk junk junk");

        using var document = PdfDocument.Open(file);

        document.Diagnostics[1].Message.Should().Be(Invariant(
            $"The cross-reference table /Prev names at offset {middle} is there but cannot be read: the row for object 5, at offset {row}, is not an offset, a generation and n or f. The rows read before the fault, if any, were kept but for those it refused; the objects only the rest indexes are looked for by rebuilding the index."));
        document.Reader.Structure.Sections[^1].State.Should().Be(XRefSectionState.Malformed);
        ShouldServeTheNewerRevisions(document, three: false, four: true);
    }

    [Fact]
    public void A_table_the_end_of_the_file_cuts_does_not_say_it_kept_a_row_it_refused_itself()
    {
        // The table /Prev names refuses its own row for object 3, generation 65536, places object 4, and the file ends before its
        // trailer: the report said the rows read were kept, the refused one with them. It says they were kept but for those
        // it refused, and object 3, which no row stands for, is read through a rebuild (#207, #210).
        var file = PdfTemplate.Build(PartialFiles.CutUpdate("3 2\n{off:3} 65536 n \n{row:4}\n"));
        var cut = PdfTemplate.OffsetOf(file, "\nxref\n", 3) + 1;

        using var document = PdfDocument.Open(file);

        document.Diagnostics.Should().ContainSingle().Which.Message.Should().Be(Invariant(
            $"The cross-reference table /Prev names at offset {cut} was read only in part: the file ends before its trailer; the rows read were kept but for those it refused, and what only the rest of the table, or the sections its trailer would name, index is looked for by rebuilding the index."));
        document.Reader.ChainIndex!.TryGet(3, out _).Should().BeFalse();
        Text(document, 4).Should().Be("object 4");
        document.WasRepaired.Should().BeFalse();
        Text(document, 3).Should().Be("object 3");
        document.WasRepaired.Should().BeTrue();
    }

    [Theory]
    [InlineData("/X", "None of the rows read before the fault, if any, stands, a newer section having refused every number to the older sections' rows; every object no section read before it indexes is looked for by rebuilding the index.", false, false)]
    [InlineData("[3 2]", "The rows read before the fault, if any, were kept but for those whose numbers a newer section refused; the objects only the rest indexes are looked for by rebuilding the index.", false, false)]
    [InlineData("[10 1]", "The rows read before the fault, if any, were kept; the objects only the rest indexes are looked for by rebuilding the index.", true, true)]
    public void A_table_that_cannot_be_read_past_a_lost_stream_says_which_of_the_rows_read_before_its_fault_stand(string index, string said, bool three, bool four)
    {
        // The table the lost stream's /Prev names runs from its rows for objects 3 and 4 into a row that is no row, then a stray
        // token: it cannot be read, and ends the chain. Its rows read before the fault were said to be kept, though the lost
        // stream refused every number, or theirs, and none of them stood (#208, #210).
        var (file, middle, _) = PartialFiles.PastALostStream(index, "3 3\n{three} 00000 n \n{four} 00000 n \njunk junk junk\nmore\ntrailer\n<< /Size 7 /Prev {oldest} >>\n");
        var row = PdfTemplate.OffsetOf(file, "junk junk junk");

        using var document = PdfDocument.Open(file);

        document.Diagnostics[1].Position.Should().Be(middle);
        document.Diagnostics[1].Message.Should().Be(Invariant(
            $"The cross-reference table /Prev names at offset {middle} is there but cannot be read: the row for object 5, at offset {row}, is not an offset, a generation and n or f. {said}"));
        document.Reader.Structure.Sections[^1].State.Should().Be(XRefSectionState.Malformed);
        ShouldServeTheNewerRevisions(document, three, four);
    }

    [Theory]
    [InlineData("/X", "None of the rows read before the fault, if any, stands, a newer section having refused every number to the older sections' rows; every object no section read before it indexes is looked for by rebuilding the index.")]
    [InlineData("[10 1]", "The rows read before the fault, if any, were kept; the objects only the rest indexes are looked for by rebuilding the index.")]
    public void A_stream_that_cannot_be_read_and_hands_nothing_on_past_a_lost_stream_says_what_the_lost_stream_refused(string index, string said)
    {
        // The lost stream's /Prev names a cross-reference stream whose /W gives a negative width and which gives no /Prev of
        // its own: it ends the chain, and whatever rows it read before its fault were said to be kept, and only what the rest
        // indexed to be looked for by rebuilding the index, though the lost stream had refused every number (#208, #210).
        var (file, middle, _) = PartialFiles.PastALostStream(index, table: null, stream: "/Type /XRef /Size 12 /Index [3 3] /W [1 -4 2]");

        using var document = PdfDocument.Open(file);

        document.Diagnostics.Should().ContainSingle(d => d.Code == PdfDiagnosticCodes.XRefSectionUnreadable && d.Position == middle).Which.Message.Should().Be(Invariant(
            $"The cross-reference stream /Prev names at offset {middle} is there but cannot be read: its /W gives a field a negative width. {said}"));
        document.Reader.Structure.Sections[^1].NumbersRefused.Should().BeFalse();
    }

    [Theory]
    [InlineData("/X", "junk junk junk\nmore\n", "is there but cannot be read: {fault}. None of the rows read before the fault, if any, stands, a newer section having refused every number to the older sections' rows; every object no section read before it indexes is looked for by rebuilding the index.", false)]
    [InlineData("[6 1]", "junk junk junk\nmore\n", "is there but cannot be read: {fault}. The rows read before the fault, if any, were kept but for those whose numbers a newer section refused; every other object no section read before it indexes is looked for by rebuilding the index, rather than taken from an older section's rows.", false)]
    [InlineData("[10 1]", "junk junk junk\nmore\n", "is there but cannot be read: {fault}. The rows read before the fault, if any, were kept; every other object no section read before it indexes is looked for by rebuilding the index, rather than taken from an older section's rows.", true)]
    [InlineData("/X", "junk junk junk\ntrailer\n<< /Size 9 >>\n", "was read only in part: {fault}; none of the rows read stands, a newer section having refused every number to the older sections' rows, and every object no section read before it indexes is looked for by rebuilding the index.", false)]
    [InlineData("[6 1]", "junk junk junk\ntrailer\n<< /Size 9 >>\n", "was read only in part: {fault}; the rows read were kept but for those whose numbers a newer section refused, and the objects the rest of that subsection numbers are looked for by rebuilding the index where no section read before it indexes them, rather than taken from an older section's rows.", false)]
    [InlineData("[10 1]", "junk junk junk\ntrailer\n<< /Size 9 >>\n", "was read only in part: {fault}; the rows read were kept, and the objects the rest of that subsection numbers are looked for by rebuilding the index where no section read before it indexes them, rather than taken from an older section's rows.", true)]
    public void A_table_an_xrefstm_names_past_a_lost_stream_says_which_of_its_rows_stand_whichever_refusal_came_first(
        string index, string rest, string said, bool six)
    {
        // The hybrid table the lost stream's /Prev names names through /XRefStm a classic table whose row for object 6 is
        // followed by one that is no row, then a stray token, or its trailer. That one cannot be read refuses every number
        // itself, before it is reported: the newer refusal the report names is the one the lost stream made before the chain
        // came to the table — every number, or object 6's —, which it tells apart from the table's own, whose rows stand
        // (#156, #208, #210).
        var (file, table, _) = HybridFiles.PastALostStream(index, rest);
        var fault = Invariant($"the row for object 7, at offset {PdfTemplate.OffsetOf(file, "junk junk junk")}, is not an offset, a generation and n or f");

        using var document = PdfDocument.Open(file);

        document.Diagnostics.Should().ContainSingle(d => d.Position == table && d.Code == PdfDiagnosticCodes.XRefSectionUnreadable)
            .Which.Message.Should().Be(Invariant($"The cross-reference table /XRefStm names at offset {table} ") + said.Replace("{fault}", fault, StringComparison.Ordinal));
        document.Reader.ChainIndex!.TryGet(6, out _).Should().Be(six);
        Text(document, 6).Should().Be("new six");
        document.WasRepaired.Should().Be(!six);
    }

    [Fact]
    public void A_table_found_moved_past_a_lost_stream_says_its_rows_stand_but_for_those_the_stream_refused()
    {
        // The lost stream's /Prev names the table read in part two bytes past its xref keyword, where it is found moved: what
        // its rows lost to the stream's refusal is said where it was found, as where it is named (#208, #210).
        var (file, middle, _) = PartialFiles.PastALostStream("[3 1]", "3 3\n{three} 00000 n \n{four} 00000 n \njunk junk junk\ntrailer\n<< /Size 7 /Prev {oldest} >>\n", shift: 2);
        var row = PdfTemplate.OffsetOf(file, "junk junk junk");

        using var document = PdfDocument.Open(file);

        document.Diagnostics.Select(d => d.Code).Should().Equal(
            PdfDiagnosticCodes.XRefSectionUnreadable, PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefSectionUnreadable);
        document.Diagnostics[2].Message.Should().Be(Invariant(
            $"The cross-reference table /Prev names at offset {middle + 2}, found at offset {middle}, was read only in part: the row for object 5, at offset {row}, is not an offset, a generation and n or f; the rows read were kept but for those whose numbers a newer section refused, and the objects the rest of that subsection numbers are looked for by rebuilding the index where no section read before it indexes them, rather than taken from an older section's rows."));
        ShouldServeTheNewerRevisions(document, three: false, four: true);
    }

    [Fact]
    public void A_table_read_in_part_whose_row_a_newer_table_refused_alone_says_that_row_does_not_stand()
    {
        // The newest table gives object 3 generation 65536, a row refused alone, which no older row stands for. The table its
        // /Prev names, read in part, gives object 3 a row too, which did not stand, and it was said to have kept every row
        // read (#210).
        var file = new HandWrittenFile();
        var catalog = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        var pages = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        var oldThree = file.Object(3, "(old three)");
        var four = file.Object(4, "(four)");
        var older = file.Position;
        file.Raw(Invariant($"xref\n0 6\n0000000000 65535 f \n{catalog:D10} 00000 n \n{pages:D10} 00000 n \n{oldThree:D10} 00000 n \n{four:D10} 00000 n \njunk junk junk\ntrailer\n<< /Size 6 >>\n"));
        var newThree = file.Object(3, "(new three)");
        var newest = file.Position;
        file.Raw(Invariant($"xref\n3 1\n{newThree:D10} 65536 n \ntrailer\n<< /Size 6 /Root 1 0 R /Prev {older} >>\nstartxref\n{newest}\n%%EOF\n"));
        var bytes = file.Bytes;
        var row = PdfTemplate.OffsetOf(bytes, "junk junk junk");

        using var document = PdfDocument.Open(bytes);

        document.Diagnostics[1].Message.Should().Be(Invariant(
            $"The cross-reference table /Prev names at offset {older} was read only in part: the row for object 5, at offset {row}, is not an offset, a generation and n or f; the rows read were kept but for those whose numbers a newer section refused, and the objects the rest of that subsection numbers are looked for by rebuilding the index where no section read before it indexes them, rather than taken from an older section's rows."));
        document.Reader.ChainIndex!.TryGet(3, out _).Should().BeFalse();
        document.Reader.ChainIndex!.TryGet(4, out _).Should().BeTrue();
        Text(document, 3).Should().Be("new three");
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    public void A_table_read_in_part_leaves_no_older_row_standing_for_the_rest_of_the_subsection_its_fault_ended(int count, bool fiveRefused)
    {
        // The newer table's subsection 4 1, or 4 2, holds one row, object 4's, which ends in x: the rows of that subsection
        // from object 4 on are lost — 4's, or 4's and 5's. No older row stands for them: the oldest table's row for object 4
        // served its older revision. Object 3, past the subsection, is read through the oldest table's row (#210).
        var (file, _, middle) = PartialFiles.LostRowOverOlderOnes(count);

        using var document = PdfDocument.Open(file);

        document.Diagnostics.Should().ContainSingle().Which.Message.Should().StartWith(string.Create(
            CultureInfo.InvariantCulture,
            $"The cross-reference table startxref names at offset {middle} was read only in part: the row for object 4, at offset "));
        var index = document.Reader.ChainIndex!;
        index.TryGet(4, out _).Should().BeFalse();
        index.TryGet(5, out _).Should().Be(!fiveRefused);
        index.TryGet(3, out _).Should().BeTrue();
        Text(document, 3).Should().Be("only in the oldest");
        document.WasRepaired.Should().BeFalse();
        Text(document, 4).Should().Be("new four");
        Text(document, 5).Should().Be("old five");
        document.WasRepaired.Should().BeTrue();
    }

    [Fact]
    public void A_table_read_in_part_keeps_the_subsections_after_its_fault_standing_over_the_older_rows()
    {
        // The rows a subsection after the fault gives are the table's own: the number the lost one refuses is refused to the
        // sections read after it, not to the rest of the table — a later subsection 5 1 placing object 5 stands (#210).
        var (sound, _, _) = PartialFiles.LostRowOverOlderOnes(2);
        var five = PdfTemplate.OffsetOf(sound, "5 0 obj");
        var (file, _, _) = PartialFiles.LostRowOverOlderOnes(2, string.Create(CultureInfo.InvariantCulture, $"5 1\n{five:D10} 00000 n \n"));

        using var document = PdfDocument.Open(file);

        document.Diagnostics.Should().ContainSingle();
        document.Reader.ChainIndex!.TryGet(5, out var entry).Should().BeTrue();
        entry.Offset.Should().Be(five);
        Text(document, 5).Should().Be("old five");
        document.WasRepaired.Should().BeFalse();
    }

    [Theory]
    [InlineData(4096, false)]
    [InlineData(4097, true)]
    public void A_table_keeps_the_numbers_its_faults_lost_apart_up_to_4096_ranges_and_refuses_every_number_past_them(int lost, bool every)
    {
        // Every other number from 100, a one-row subsection whose row ends in x, and then a thousand free rows that run past
        // the 64 KB a table is first read through: the rows are read twice, the first window's edge cutting the free rows
        // short, which the second reads whole. Up to 4,096 ranges, the numbers the table lost are kept apart, and the oldest
        // table's row for object 3 stands; past them, every number is refused, as for a stream whose numbering cannot be read,
        // and what only the oldest table indexes is found by rebuilding the index: what the refusal keeps stays bounded (#210).
        // The report says which: it said the rest of the subsection alone was looked for so, though every number was refused.
        var file = PartialFiles.LostSubsections(lost, free: 1000);
        var newest = PdfTemplate.OffsetOf(file, "\nxref\n", 2) + 1;
        (PdfTemplate.OffsetOf(file, "20000 1000") - newest).Should().BeLessThan(PdfFileReader.XRefWindow, "the first window holds every lost subsection");
        (file.Length - newest).Should().BeGreaterThan(PdfFileReader.XRefWindow, "the free rows run past it");

        using var document = PdfDocument.Open(file);

        var index = document.Reader.ChainIndex!;
        index.TryGet(3, out _).Should().Be(!every);
        index.TryGet(100, out _).Should().BeFalse();
        index.TryGet(20999, out _).Should().BeTrue("the newest table's free rows stand");
        document.Diagnostics[0].Code.Should().Be(PdfDiagnosticCodes.XRefSectionUnreadable);
        document.Diagnostics[0].Message.Should().EndWith(every
            ? "; the rows read were kept, and every other object no section read before it indexes is looked for by rebuilding the index, rather than taken from an older section's rows."
            : "; the rows read were kept, and the objects the rest of that subsection numbers are looked for by rebuilding the index where no section read before it indexes them, rather than taken from an older section's rows.");
        document.WasRepaired.Should().Be(every, "the catalog is the oldest table's alone");
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(
            every
                ? [PdfDiagnosticCodes.XRefSectionUnreadable, PdfDiagnosticCodes.XRefRebuilt]
                : [PdfDiagnosticCodes.XRefSectionUnreadable]);
    }

    [Theory]
    [InlineData(4096, false)]
    [InlineData(4097, false)]
    [InlineData(4096, true)]
    [InlineData(4097, true)]
    public void A_table_the_end_of_the_file_cuts_where_startxref_or_a_prev_names_it_says_what_only_its_rest_indexes_is_looked_for_however_many_ranges_it_lost(
        int lost, bool prev)
    {
        // The table startxref names, or the one a /Prev names, loses every other number from 100, a one-row subsection whose
        // row ends in x, 4,096 or 4,097 times, and the end of the file then cuts it before its trailer: its rest cannot be
        // known, every number is refused whatever the count, and the chain ends there. Past the 4,096 ranges kept apart, as
        // within them, the report says that what only the rest of the table, or the sections its trailer would name, index
        // is looked for by rebuilding the index: the same objects as every other one no section read before it indexes, no
        // section past it being read. Object 3, which only the oldest table indexes, is read after the rebuild that the
        // catalog, the oldest table's too, sets off as the document opens, and the table is reported once (#210).
        var (file, cut) = PartialFiles.CutAfterLostSubsections(lost, prev);
        var naming = prev ? "/Prev" : "startxref";

        using var document = PdfDocument.Open(file);

        var last = document.Reader.Structure.Sections[^1];
        (last.NamedBy, last.Offset, last.Incomplete, last.SubsectionsUnknown).Should().Be((naming, cut, true, true));
        document.Diagnostics[0].Message.Should().Be(Invariant(
            $"The cross-reference table {naming} names at offset {cut} was read only in part: the row for object 100, at offset {cut + 35}, is not an offset, a generation and n or f; the rows read were kept, and what only the rest of the table, or the sections its trailer would name, index is looked for by rebuilding the index."));
        document.Reader.ChainIndex!.TryGet(3, out _).Should().BeFalse();
        Text(document, 3).Should().Be("only in the oldest");
        document.WasRepaired.Should().BeTrue();
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(
            PdfDiagnosticCodes.XRefSectionUnreadable, PdfDiagnosticCodes.XRefRebuilt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Subsections_a_table_loses_one_after_the_other_are_one_range(bool descending)
    {
        // The newest table's 5,000 one-row subsections from object 100, one after the other, each end in x: the numbers they
        // lose meet, and are one range, as a stream's declared so are. They were 5,000 ranges, past the 4,096 kept apart,
        // and every number was refused: the whole file rebuilt at opening for the catalog, which only the oldest table
        // indexes. Object 3's row stands, and no row stands for a number the table lost (#210). So it does where the table
        // writes them from the highest number down, each meeting the last one lost from below, which was kept apart.
        var file = PartialFiles.LostSubsections(5000, apart: false, descending: descending);

        using var document = PdfDocument.Open(file);

        var index = document.Reader.ChainIndex!;
        index.TryGet(3, out _).Should().BeTrue();
        index.TryGet(100, out _).Should().BeFalse();
        index.TryGet(5099, out _).Should().BeFalse();
        document.Diagnostics.Should().ContainSingle().Which.Code.Should().Be(PdfDiagnosticCodes.XRefSectionUnreadable);
        document.WasRepaired.Should().BeFalse();
    }

    [Theory]
    [InlineData("7 1\n0 0 x\n5 1\n0 0 x\n", false)]
    [InlineData("5 3\n0 0 x\n6 1\n0 0 x\n", true)]
    [InlineData("5 1\n0 0 x\n6 1\n0 0 x\n7 1\n0 0 x\n", true)]
    [InlineData("6 2\n0 0 x\n5 2\n0 0 x\n", true)]
    public void Each_number_a_table_loses_is_refused_whatever_order_its_subsections_take(string subsections, bool sixLost)
    {
        // The newest table loses objects 5 and 7, and 6 with them — in a subsection numbered below the one before it, or
        // within it, or in subsections that meet, from above or from below —, and the ranges lost are joined as they are
        // lost: none is dropped, nor shortened, whichever order the subsections take, and no row of the oldest table stands
        // for them, while its rows for objects 3, and 6 where the newest table does not lose it, do (#210).
        var file = new HandWrittenFile();
        var catalog = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        var pages = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        var three = file.Object(3, "(three)");
        var five = file.Object(5, "(old five)");
        var six = file.Object(6, "(old six)");
        var seven = file.Object(7, "(old seven)");
        var oldest = file.Table(
            8, new Dictionary<int, long> { [1] = catalog, [2] = pages, [3] = three, [5] = five, [6] = six, [7] = seven }, "/Size 8 /Root 1 0 R");
        var newest = file.Position;
        file.Raw(Invariant($"xref\n0 1\n0000000000 65535 f \n{subsections}trailer\n<< /Size 8 /Root 1 0 R /Prev {oldest} >>\nstartxref\n{newest}\n%%EOF\n"));

        using var document = PdfDocument.Open(file.Bytes);

        var index = document.Reader.ChainIndex!;
        index.TryGet(3, out _).Should().BeTrue();
        index.TryGet(5, out _).Should().BeFalse();
        index.TryGet(6, out _).Should().Be(!sixLost);
        index.TryGet(7, out _).Should().BeFalse();
        document.Diagnostics.Should().ContainSingle().Which.Code.Should().Be(PdfDiagnosticCodes.XRefSectionUnreadable);
    }

    [Theory]
    [InlineData(4097, 0)]
    [InlineData(1, 4095)]
    public void Ranges_a_table_loses_count_toward_the_bound_once_joined(int same, int apart)
    {
        // The newest table loses object 100's row 4,097 times, in as many one-row subsections, which is one range; or
        // objects 100 and 101, in two subsections that meet, then 4,095 numbers apart from 200, every other one: 4,096 ranges
        // once joined. Either is kept apart within the bound, and the oldest table's row for object 3 stands (#210).
        var lost = new StringBuilder();

        for (var i = 0; i < same; i++)
        {
            lost.Append("100 1\n0 0 x\n");
        }

        if (apart > 0)
        {
            lost.Append("101 1\n0 0 x\n");

            for (var i = 0; i < apart; i++)
            {
                lost.Append(CultureInfo.InvariantCulture, $"{200 + (2 * i)} 1\n0 0 x\n");
            }
        }

        var file = new HandWrittenFile();
        var catalog = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        var pages = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        var three = file.Object(3, "(three)");
        var oldest = file.Table(4, new Dictionary<int, long> { [1] = catalog, [2] = pages, [3] = three }, "/Size 4 /Root 1 0 R");
        var newest = file.Position;
        file.Raw(Invariant($"xref\n{lost}trailer\n<< /Size 10000 /Root 1 0 R /Prev {oldest} >>\nstartxref\n{newest}\n%%EOF\n"));

        using var document = PdfDocument.Open(file.Bytes);

        var index = document.Reader.ChainIndex!;
        index.TryGet(3, out _).Should().BeTrue();
        index.TryGet(100, out _).Should().BeFalse();
        document.WasRepaired.Should().BeFalse("the catalog is the oldest table's alone");
    }

    [Theory]
    [InlineData("", "startxref")]
    [InlineData("", "/Prev")]
    [InlineData("0 0\n", "startxref")]
    public void A_table_holding_no_subsection_is_read_as_indexing_nothing_and_reported_once(string subsections, string naming)
    {
        // An update whose trailer keyword follows its xref keyword, where ISO 32000-1 has one or more subsections follow it,
        // read as sound in silence (#222): it is read as indexing nothing, as one whose one subsection is 0 0 is, the index
        // left whole and nothing rebuilt, and reported once. Named through /Prev by a newer update that indexes nothing too,
        // it is reported as /Prev names it. The 0 0 one is sound.
        var template = PartialFiles.EmptyUpdate(subsections);
        template += naming == "/Prev"
            ? "xref\n0 1\n{free}\ntrailer\n<< /Size 4 /Root 1 0 R /Prev {xref:2} >>\nstartxref\n{xref:3}\n%%EOF\n"
            : string.Empty;
        var file = PdfTemplate.Build(template);
        var update = PdfTemplate.OffsetOf(file, "\nxref\n", 2) + 1;

        using var document = PdfDocument.Open(file);

        if (subsections.Length > 0)
        {
            document.Diagnostics.Should().BeEmpty();
        }
        else
        {
            var report = document.Diagnostics.Should().ContainSingle().Which;
            report.Code.Should().Be(PdfDiagnosticCodes.XRefSectionEmpty);
            report.Severity.Should().Be(PdfDiagnosticSeverity.Repair);
            report.Position.Should().Be(update);
            report.Message.Should().Be(string.Create(
                CultureInfo.InvariantCulture,
                $"The cross-reference table {naming} names at offset {update} holds no subsection: its trailer keyword follows its xref keyword. It was read as indexing nothing."));
        }

        document.Reader.Structure.Sections.Single(section => section.Offset == update).NoSubsection.Should().Be(subsections.Length == 0);
        document.Catalog.GetDictionary(PdfName.Pages)!.GetArray(PdfName.Kids)!.Resolved(0).AsDictionary()!.IsOfType(PdfName.Page).Should().BeTrue();
        document.GetObject(new PdfObjectId(4)).Should().BeSameAs(PdfNull.Instance, "no section indexes object 4, and the index is whole");
        document.WasRepaired.Should().BeFalse();
    }

    [Fact]
    public void A_file_whose_only_table_holds_no_subsection_is_reported_and_rebuilt()
    {
        // The only table holds no subsection: it indexes nothing, and the file is rebuilt at opening, as for any chain that
        // gives no row (#222).
        var file = PdfTemplate.Build(PdfTemplate.Sound.Replace("xref\n0 4\n{free}\n{row:1}\n{row:2}\n{row:3}\n", "xref\n", StringComparison.Ordinal));

        using var document = PdfDocument.Open(file);

        document.Diagnostics.Select(d => d.Code).Should().Equal(PdfDiagnosticCodes.XRefSectionEmpty, PdfDiagnosticCodes.XRefRebuilt);
        document.Catalog.IsOfType(PdfName.Catalog).Should().BeTrue();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_catalog_a_rebuild_after_opening_takes_again_is_reported_recovered_once(bool cut)
    {
        // No trailer the file holds gives /Root: the catalog is found among the objects the chain indexes as the document
        // opens. A rebuild set off after — by object 4, which the only table the end of the file cuts lacks (#207), or by the
        // page, whose row a generation of 65,536 refuses — sets that guess aside and finds the same object again, which
        // was reported a second time.
        var file = cut
            ? PdfTemplate.Build(PartialFiles.CutAlone)
            : PdfTemplate.Build(PdfTemplate.Sound
                .Replace("{row:3}", "{off:3} 65536 n", StringComparison.Ordinal)
                .Replace("/Root 1 0 R", "/Rot 1 0 R", StringComparison.Ordinal));
        var asked = cut ? 4 : 3;

        using var document = PdfDocument.Open(file);

        document.Diagnostics.Select(d => d.Code).Should().Equal(PdfDiagnosticCodes.XRefSectionUnreadable, PdfDiagnosticCodes.TrailerRootRecovered);
        document.GetObject(new PdfObjectId(asked)).Should().NotBeSameAs(PdfNull.Instance);
        document.WasRepaired.Should().BeTrue();
        document.Diagnostics.Select(d => d.Code).Should().Equal(
            PdfDiagnosticCodes.XRefSectionUnreadable, PdfDiagnosticCodes.TrailerRootRecovered, PdfDiagnosticCodes.XRefRebuilt);
        document.Diagnostics.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void A_rebuild_after_opening_that_takes_another_object_for_the_catalog_reports_it()
    {
        // No trailer gives /Root. The table frees object 1, an older catalog, and indexes object 5, the newer one, taken as
        // the document opens; the page's row is refused. The rebuild its read sets off finds both, and takes the first the
        // file writes, object 1: the reader now serves another catalog, which is reported.
        var file = new HandWrittenFile();
        file.Object(1, "<< /Type /Catalog /Pages 2 0 R /Rev (old) >>");
        var pages = file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        var page = file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] >>");
        var catalog = file.Object(5, "<< /Type /Catalog /Pages 2 0 R /Rev (new) >>");
        var table = file.Position;
        file.Raw(string.Create(
            CultureInfo.InvariantCulture,
            $"xref\n0 6\n0000000000 65535 f \n0000000000 00001 f \n{pages:D10} 00000 n \n{page:D10} 65536 n \n0000000000 00001 f \n{catalog:D10} 00000 n \ntrailer\n<< /Size 6 >>\nstartxref\n{table}\n%%EOF\n"));

        using var document = PdfDocument.Open(file.Bytes);

        document.Diagnostics.Where(d => d.Code == PdfDiagnosticCodes.TrailerRootRecovered).Should().ContainSingle()
            .Which.Message.Should().EndWith("the catalog was found as object 5.");
        document.GetObject(new PdfObjectId(3)).AsDictionary()!.IsOfType(PdfName.Page).Should().BeTrue();
        document.WasRepaired.Should().BeTrue();
        document.Diagnostics.Where(d => d.Code == PdfDiagnosticCodes.TrailerRootRecovered).Select(d => d.Message).Should().Equal(
            "The trailer's /Root does not lead to a document catalog; the catalog was found as object 5.",
            "The trailer's /Root does not lead to a document catalog; the catalog was found as object 1.");
    }

    [Theory]
    [InlineData("0 1\n{free}\n")]
    [InlineData("3 1\n{off:3} 65536 n \n")]
    public void A_table_the_end_of_the_file_cuts_after_a_free_or_refused_row_is_kept_read_in_part(string rows)
    {
        // What decides is a row of the table's own, whatever it gives: object 0's free row, or object 3's, refused for its
        // generation, keep the table, read only in part, its trailer lost (#207).
        var file = PdfTemplate.Build(PartialFiles.CutUpdate(rows));
        var cut = PdfTemplate.OffsetOf(file, "\nxref\n", 3) + 1;

        using var document = PdfDocument.Open(file);

        document.Reader.Structure.Sections[^1].State.Should().Be(XRefSectionState.Read);
        document.Diagnostics.Should().ContainSingle().Which.Message.Should().StartWith(string.Create(
            CultureInfo.InvariantCulture,
            $"The cross-reference table /Prev names at offset {cut} was read only in part: the file ends before its trailer;"));
        Text(document, 3).Should().Be("object 3");
        document.WasRepaired.Should().BeTrue();
    }

    [Theory]
    [InlineData("5 2\n0000000000 00000 f \n")]
    [InlineData("4 1\n0000000000 00000 f \n6 1\n")]
    [InlineData("5 1\n0000000000 00000 f \n")]
    public void A_table_an_xrefstm_names_that_the_end_of_the_file_cuts_leaves_no_older_row_standing_for_a_number_it_does_not_give(string rows)
    {
        // The hybrid table's /XRefStm names a table that ends the file inside its subsection 5 2, after object 5's row, after
        // the header of its subsection 6 1, or after the whole of its subsection 5 1. The chain goes on through the hybrid
        // table's /Prev to the oldest table, whose rows for objects 6 and 7 give their older revisions. What the rest of the
        // cut table held cannot be known, and no older row stands for a number it does not give — object 7's, which stood past
        // the subsection the cut ended (#207, #210), and the catalog's, which a rebuild then finds as the document opens —:
        // each is found by rebuilding the index, which keeps the last definition the file writes. The report says so, and not
        // that the cut loses the sections the table's trailer would name, which the chain does not follow (#156).
        var (file, _, cut) = PartialFiles.CutTableAnXRefStmNames(rows);

        using var document = PdfDocument.Open(file);

        document.Diagnostics.Should().ContainSingle(d => d.Code == PdfDiagnosticCodes.XRefSectionUnreadable).Which.Message.Should().Be(Invariant(
            $"The cross-reference table /XRefStm names at offset {cut} was read only in part: the file ends before its trailer; the rows read were kept, and every other object no section read before it indexes is looked for by rebuilding the index, rather than taken from an older section's rows."));
        var index = document.Reader.ChainIndex!;
        index.TryGet(5, out _).Should().Be(rows.StartsWith('5'), "the cut table's own free row for object 5 stands");
        index.TryGet(6, out _).Should().BeFalse();
        index.TryGet(7, out _).Should().BeFalse();
        index.TryGet(3, out _).Should().BeFalse();
        index.TryGet(1, out _).Should().BeFalse();
        document.WasRepaired.Should().BeTrue();
        Text(document, 7).Should().Be("old seven");
        Text(document, 6).Should().Be("new six");
        Text(document, 3).Should().Be("only in the oldest");
    }

    [Theory]
    [InlineData("6 2\n{six}junk junk junk\nmore\n", "the row for object 7, at offset {at}, is not an offset, a generation and n or f", "junk", false, true, false, false)]
    [InlineData("6 2\n{six}junk junk junk\n/Bad\ntrailer\n<< /Size 8 >>\n", "the row for object 7, at offset {at}, is not an offset, a generation and n or f", "junk", false, true, false, false)]
    [InlineData("6 1\n{six}7 x\n{seven}trailer\n<< /Size 8 >>\n", "the subsection header at offset {at} does not give a first object number and a count of rows", "7 x", false, true, false, false)]
    [InlineData("6 1\n{six}2147483647 2\n{seven}trailer\n<< /Size 8 >>\n", "the subsection header at offset {at} numbers its rows past object 2147483647", "2147483647", false, true, false, false)]
    [InlineData("6 2\n{six}{seven}/Bad\ntrailer\n<< /Size 8 >>\n", "it holds a name at offset {at}, where a subsection or the trailer should start", "/Bad", false, true, true, false)]
    [InlineData("6 1\n", "the file ends before its trailer", "", false, true, false, true)]
    [InlineData("6 2\n{six}junk junk junk\n<< /Size 8 >>\n", "the row for object 7, at offset {at}, is not an offset, a generation and n or f", "junk", false, false, false, true)]
    [InlineData("6 2\n{six}junk junk junk\ntrailer\n<< /Size 8 >>\n", "the row for object 7, at offset {at}, is not an offset, a generation and n or f", "junk", true, false, false, false)]
    public void A_table_an_xrefstm_names_that_cannot_be_read_leaves_no_older_row_standing_for_what_it_lost(
        string rows, string fault, string needle, bool read, bool every, bool sevenGiven, bool trailerMissing)
    {
        // The hybrid table's /XRefStm names a classic table that gives the newer revisions of objects 6 and 7, or the first
        // of them, and cannot be read: a row that is no row, a subsection header that is no header, a stray token, or the end
        // of the file before any row. The chain goes on through the hybrid table's /Prev to the oldest table, whose row for
        // object 7 served its older revision, the table's lost row standing in silence where the same table read in part
        // refused it (#156, #210). No older row stands for the rest of the subsection its fault ended, and, where what follows
        // the fault cannot be known, for any number its rows read before the fault do not give — the catalog's among them,
        // which a rebuild then finds as the document opens —: each is found by rebuilding the index, the newer revision. A
        // row that is no row followed by a dictionary, where the trailer starts, or by the trailer itself, loses the rest of
        // its subsection alone.
        var (file, table) = PartialFiles.TableAnXRefStmNamesOverOlderRows(rows);
        var said = fault.Replace("{at}", needle.Length > 0 ? Invariant($"{PdfTemplate.OffsetOf(file, needle)}") : string.Empty, StringComparison.Ordinal);
        var lost = every
            ? "every other object no section read before it indexes is looked for by rebuilding the index, rather than taken from an older section's rows."
            : "the objects the rest of that subsection numbers are looked for by rebuilding the index where no section read before it indexes them, rather than taken from an older section's rows.";

        using var document = PdfDocument.Open(file);

        document.Diagnostics.Should().ContainSingle(d => d.Code == PdfDiagnosticCodes.XRefSectionUnreadable).Which.Message.Should().Be(read
            ? Invariant($"The cross-reference table /XRefStm names at offset {table} was read only in part: {said}; the rows read were kept, and {lost}")
            : Invariant($"The cross-reference table /XRefStm names at offset {table} is there but cannot be read: {said}. The rows read before the fault, if any, were kept; {lost}"));
        document.Diagnostics.Should().ContainSingle(d => d.Code == PdfDiagnosticCodes.XRefHybridStreamLost);
        var index = document.Reader.ChainIndex!;
        index.TryGet(6, out _).Should().Be(rows.Contains("{six}", StringComparison.Ordinal), "the table's own row for object 6 stands");
        index.TryGet(7, out _).Should().Be(sevenGiven, "only the table's own row for object 7 stands");
        index.TryGet(5, out _).Should().Be(!every);
        index.TryGet(3, out _).Should().Be(!every);
        index.TryGet(1, out _).Should().Be(!every);
        document.WasRepaired.Should().Be(every, "the catalog is the oldest table's alone");
        Text(document, 7).Should().Be("new seven");
        Text(document, 6).Should().Be("new six");
        Text(document, 5).Should().Be("old five");
        Text(document, 3).Should().Be("only in the oldest");

        // A table whose rows, after any bad row, run into a dictionary, or whose subsection header the end of the file
        // follows, is said to miss its trailer, and every other one to be malformed; whether a bad row before a missing
        // trailer is said malformed too is #325's to settle, and is not held here.
        var findings = new PdfValidator().Validate(document).Findings.Select(finding => finding.RuleId).ToList();
        findings.Should().Contain(PdfValidationRuleIds.XRefHybridStreamWrong);
        findings.Should().Contain(trailerMissing ? PdfValidationRuleIds.FileTrailerMissing : PdfValidationRuleIds.XRefSectionMalformed);

        if (!trailerMissing)
        {
            findings.Should().NotContain(PdfValidationRuleIds.FileTrailerMissing);
        }
    }

    [Fact]
    public void A_table_an_xrefstm_names_that_cannot_be_read_found_moved_leaves_no_older_row_standing_for_what_it_lost()
    {
        // As above, the table's rows run into a stray name, and the /XRefStm names it 2 bytes past its xref keyword, where
        // it is found moved, the nearest place that declares itself a section (#281): what follows its rows cannot be known
        // where it was found either, and no older row stands for a number it does not give.
        var (file, table) = PartialFiles.TableAnXRefStmNamesOverOlderRows("6 2\n{six}{seven}/Bad\ntrailer\n<< /Size 8 >>\n", shift: 2);

        using var document = PdfDocument.Open(file);

        document.Diagnostics.Should().ContainSingle(d => d.Code == PdfDiagnosticCodes.XRefOffsetAdjusted).Which.Position.Should().Be(table);
        document.Diagnostics.Should().ContainSingle(d => d.Code == PdfDiagnosticCodes.XRefSectionUnreadable).Which.Message.Should().Be(Invariant(
            $"The cross-reference table /XRefStm names at offset {table + 2}, found at offset {table}, is there but cannot be read: it holds a name at offset {PdfTemplate.OffsetOf(file, "/Bad")}, where a subsection or the trailer should start. The rows read before the fault, if any, were kept; every other object no section read before it indexes is looked for by rebuilding the index, rather than taken from an older section's rows."));
        var index = document.Reader.ChainIndex!;
        index.TryGet(7, out _).Should().BeTrue("the table's own row for object 7 stands");
        index.TryGet(5, out _).Should().BeFalse();
        index.TryGet(3, out _).Should().BeFalse();
        document.WasRepaired.Should().BeTrue("the catalog is the oldest table's alone");
        Text(document, 5).Should().Be("old five");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_row_refused_alone_is_reported_once_at_its_section_and_the_rows_after_it_read(bool stream)
    {
        // The page's row gives it generation 65,536, or, in a cross-reference stream, places it in object stream 0, which is
        // no object number: refused alone, the rows after it read, its fault reported nowhere but by the rebuild its object
        // set off (#210). It is reported once, at the section, read only in part.
        byte[] file;
        string fault;
        long section;

        if (stream)
        {
            var written = new HandWrittenFile(version: "1.5");
            var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
            {
                [1] = (1, written.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"), 0),
                [2] = (1, written.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"), 0),
                [3] = (2, 0, 1),
            };
            written.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] >>");
            rows[4] = (1, written.Object(4, "(after it)"), 0);
            section = written.CrossReferenceStream(5, 6, rows, "/Root 1 0 R");
            file = written.Bytes;
            fault = "the row for object 3 places it in object stream 0, which is no object number";
        }
        else
        {
            file = PdfTemplate.Build(PdfTemplate.Spread.Replace("{row:3}", "{off:3} 65536 n", StringComparison.Ordinal));
            section = PdfTemplate.OffsetOf(file, "\nxref\n") + 1;
            var page = PdfTemplate.OffsetOf(file, "\n3 0 obj") + 1;
            fault = string.Create(
                CultureInfo.InvariantCulture,
                $"the row for object 3, at offset {PdfTemplate.OffsetOf(file, $"{page} 65536 n")}, gives it generation 65536, outside 0 to 65535");
        }

        using var document = PdfDocument.Open(file);

        var report = document.Diagnostics.Should().ContainSingle().Which;
        report.Code.Should().Be(PdfDiagnosticCodes.XRefSectionUnreadable);
        report.Position.Should().Be(section);
        report.Message.Should().Be(string.Create(
            CultureInfo.InvariantCulture,
            $"The cross-reference {(stream ? "stream" : "table")} startxref names at offset {section} was read only in part: {fault}; that row alone was refused, the rows after it read, and its object is looked for by rebuilding the index where no row read before it indexes it, rather than taken from an older section's row."));
        document.GetObject(new PdfObjectId(4)).Should().BeOfType<PdfString>();
        document.WasRepaired.Should().BeFalse();
        document.GetObject(new PdfObjectId(3)).AsDictionary()!.IsOfType(PdfName.Page).Should().BeTrue();
        document.WasRepaired.Should().BeTrue();
    }

    [Theory]
    [InlineData("table length")]
    [InlineData("table index at")]
    [InlineData("table index after")]
    [InlineData("stream index at")]
    public void A_row_refused_alone_in_a_section_a_guard_stops_is_said_to_leave_the_rows_after_it_read_as_far_as_the_guard_let_them_be(string shape)
    {
        // As above, the page's row is refused, and a guard stops the section past it: MaxXRefSectionLength cuts the table's
        // rows, twenty free ones added, or MaxIndexEntries fills the index at the refused row or the next. The report said the
        // rows after it were read, which the guard's own report, at the same offset, contradicts; it says they were read as
        // far as one of the reader's limits let the section be read. The row's fault is the file's, whatever the guard did
        // (#210).
        byte[] file;
        string fault;
        long section;
        PdfReaderLimits limits;

        if (shape.StartsWith("stream", StringComparison.Ordinal))
        {
            var written = new HandWrittenFile(version: "1.5");
            var rows = new Dictionary<int, (int Type, long Field2, int Field3)>
            {
                [1] = (1, written.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"), 0),
                [2] = (1, written.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"), 0),
                [3] = (2, 0, 1),
            };
            written.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] >>");
            rows[4] = (1, written.Object(4, "(after it)"), 0);
            section = written.CrossReferenceStream(5, 6, rows, "/Root 1 0 R");
            file = written.Bytes;
            fault = "the row for object 3 places it in object stream 0, which is no object number";
            limits = PdfReaderLimits.Default with { MaxIndexEntries = 3 };
        }
        else
        {
            var free = string.Concat(Enumerable.Repeat("0000000000 00000 f \n", 20));
            file = PdfTemplate.Build(PdfTemplate.Spread
                .Replace("{row:3}", "{off:3} 65536 n", StringComparison.Ordinal)
                .Replace("0 5\n", "0 25\n", StringComparison.Ordinal)
                .Replace("{row:4}\n", "{row:4}\n" + free, StringComparison.Ordinal));
            section = PdfTemplate.OffsetOf(file, "\nxref\n") + 1;
            var page = PdfTemplate.OffsetOf(file, "\n3 0 obj") + 1;
            fault = Invariant($"the row for object 3, at offset {PdfTemplate.OffsetOf(file, $"{page} 65536 n")}, gives it generation 65536, outside 0 to 65535");
            limits = shape switch
            {
                "table length" => PdfReaderLimits.Default with { MaxXRefSectionLength = 150 },
                "table index at" => PdfReaderLimits.Default with { MaxIndexEntries = 3 },
                _ => PdfReaderLimits.Default with { MaxIndexEntries = 4 },
            };
        }

        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = limits });

        document.Diagnostics.Select(d => d.Code).Should().Equal(
            shape == "table length" ? PdfDiagnosticCodes.LimitXRefSectionLength : PdfDiagnosticCodes.LimitIndexEntries,
            PdfDiagnosticCodes.XRefSectionUnreadable);
        document.Diagnostics[0].Position.Should().Be(section);
        document.Diagnostics[1].Message.Should().Be(Invariant(
            $"The cross-reference {(shape.StartsWith("stream", StringComparison.Ordinal) ? "stream" : "table")} startxref names at offset {section} was read only in part: {fault}; that row alone was refused, the rows after it read as far as one of the reader's limits let the section be read, and its object is looked for by rebuilding the index where no row read before it indexes it, rather than taken from an older section's row."));
        document.Reader.Structure.Sections.Should().ContainSingle().Which.CutByLimit.Should().BeTrue();
    }

    [Fact]
    public void A_table_holding_no_subsection_found_near_where_prev_names_it_is_reported_where_it_was_found()
    {
        // The newest update's /Prev names the empty update 2 bytes before it: found there, read as indexing nothing, and
        // reported where it was found, as found moved (#222), the report naming the offset the /Prev gives and where the table
        // was found, as the report of its shift does. It gave where it was found as the offset the /Prev names.
        var empty = PdfTemplate.Build(PartialFiles.EmptyUpdate(string.Empty));
        var update = PdfTemplate.OffsetOf(empty, "\nxref\n", 2) + 1;
        byte[] file =
        [
            .. empty,
            .. Encoding.Latin1.GetBytes(string.Create(
                CultureInfo.InvariantCulture,
                $"xref\n0 1\n0000000000 65535 f \ntrailer\n<< /Size 4 /Root 1 0 R /Prev {update - 2} >>\nstartxref\n{empty.Length}\n%%EOF\n")),
        ];

        using var document = PdfDocument.Open(file);

        document.Diagnostics.Select(d => d.Code).Should().Equal(PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefSectionEmpty);
        document.Diagnostics[1].Message.Should().Be(string.Create(
            CultureInfo.InvariantCulture,
            $"The cross-reference table /Prev names at offset {update - 2}, found at offset {update}, holds no subsection: its trailer keyword follows its xref keyword. It was read as indexing nothing."));
        document.Reader.Structure.Sections[1].NoSubsection.Should().BeTrue();
        new PdfValidator().Validate(document).Findings.Select(finding => finding.RuleId).Should().Contain(PdfValidationRuleIds.XRefSubsectionMissing);
    }

    [Theory]
    [InlineData(1, "the table itself")]
    [InlineData(2, "the cross-reference table at offset {at}, which the chain read through /Prev")]
    [InlineData(3, "the cross-reference table at offset {at}, which the chain read through startxref")]
    public void An_xrefstm_naming_a_table_the_chain_reads_is_reported_and_leaves_what_the_stream_indexes_to_a_rebuild(int table, string what)
    {
        // The oldest of three tables names through /XRefStm itself, the table its /Prev names, or the newest: no
        // cross-reference stream of its revision, which alone could index the page. The naming was passed over in silence,
        // the page read as null with nothing rebuilt; it is reported, and the page found by rebuilding the index (#156).
        var file = HybridFiles.OldestNaming(Invariant($"{{xref:{table}}}"));
        var oldest = PdfTemplate.OffsetOf(file, "\nxref\n") + 1;
        var named = PdfTemplate.OffsetOf(file, "\nxref\n", table) + 1;
        using var document = PdfDocument.Open(file);

        var sections = document.Reader.Structure.Sections;
        sections.Select(section => section.State).Should().Equal(
            XRefSectionState.Read, XRefSectionState.Read, XRefSectionState.Read, XRefSectionState.NamedAgain);
        var again = sections[3];
        again.NamedFrom.Should().Be(oldest);
        again.ReadAs.Should().BeSameAs(sections[3 - table], "the chain reads the newest table first");
        var lost = document.Diagnostics.Should().ContainSingle().Which;
        lost.Code.Should().Be(PdfDiagnosticCodes.XRefHybridStreamLost);
        lost.Severity.Should().Be(PdfDiagnosticSeverity.Warning);
        lost.Position.Should().Be(named);
        lost.Message.Should().Be(Invariant(
            $"The /XRefStm of the cross-reference table at offset {oldest} names offset {named}, {what.Replace("{at}", named.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)}: ISO 32000-1 has it name the cross-reference stream of the table's revision (Table 19). The objects only that stream indexes are looked for by rebuilding the index."));
        document.WasRepaired.Should().BeFalse();
        document.GetObject(new PdfObjectId(3)).AsDictionary()!.IsOfType(PdfName.Page).Should().BeTrue();
        document.WasRepaired.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_xrefstm_naming_a_table_found_moved_is_reported_whether_it_names_where_it_was_found_or_where_it_was_named(bool named)
    {
        // The newest table's /Prev names the middle table 3 bytes short of it, where it is found; the oldest table's /XRefStm
        // names the middle table where it lies, or at the offset that /Prev gives. Either is what the chain read, by where it
        // read it or by the offset that named it: no stream of the oldest table's revision (#156, #188).
        var built = HybridFiles.OldestNaming("{xref:2}");
        var middle = PdfTemplate.OffsetOf(built, "\nxref\n", 2) + 1;
        var oldest = PdfTemplate.OffsetOf(built, "\nxref\n") + 1;
        var text = Encoding.Latin1.GetString(built).Replace(Invariant($"/Prev {middle} >>"), Invariant($"/Prev {middle - 3} >>"), StringComparison.Ordinal);
        var given = named ? middle - 3 : middle;
        var file = Encoding.Latin1.GetBytes(text.Replace(Invariant($"/XRefStm {middle} "), Invariant($"/XRefStm {given} "), StringComparison.Ordinal));
        using var document = PdfDocument.Open(file);

        var sections = document.Reader.Structure.Sections;
        sections[1].State.Should().Be(XRefSectionState.Relocated);
        sections[3].ReadAs.Should().BeSameAs(sections[1]);
        document.Diagnostics.Should().ContainSingle(diagnostic => diagnostic.Code == PdfDiagnosticCodes.XRefHybridStreamLost).Which.Message.Should().StartWith(Invariant(
            $"The /XRefStm of the cross-reference table at offset {oldest} names offset {given}, the cross-reference table at offset {middle}, which the chain read through /Prev: "));
    }

    [Theory]
    [InlineData("", "/Prev 0 /XRefStm 0", 0, "what its /Prev names too, which the chain looks for through that /Prev")]
    [InlineData(" /XRefStm 0", "/Prev 0", 0, "the table itself")]
    [InlineData("", "/Prev {xref:1} /XRefStm 0", 0, "what its /Prev names too, which the chain looks for through that /Prev")]
    [InlineData("", "/Prev 0 /XRefStm 8", 8, "what its /Prev names too, which the chain looks for through that /Prev")]
    public void An_xrefstm_naming_offset_zero_is_judged_as_one_naming_any_other_offset(string original, string update, long named, string what)
    {
        // Offset 0 holds the header, and the first table lies within 512 bytes of it, where the chain finds it: an /XRefStm
        // that names 0 is named again or left to the /Prev that reaches the first table too, by 0 or by its own offset, as
        // one that names any other offset (#156); so is one that names the end of the header's line, before the catalog's
        // header, where a /Prev naming 0 comes to as well. Found there through the /XRefStm, the table had its own links
        // left unread, and the /Prev that names it was reported as a loop.
        var file = PdfTemplate.Build(SoundUpdated
            .Replace("<< /Size 4 /Root 1 0 R >>", "<< /Size 4 /Root 1 0 R" + original + " >>", StringComparison.Ordinal)
            .Replace("/Prev {xref:1}", update, StringComparison.Ordinal));
        var naming = PdfTemplate.OffsetOf(file, "\nxref\n", original.Length > 0 ? 1 : 2) + 1;
        using var document = PdfDocument.Open(file);

        document.Diagnostics.Should().ContainSingle(diagnostic => diagnostic.Code == PdfDiagnosticCodes.XRefHybridStreamLost).Which.Message.Should().StartWith(Invariant(
            $"The /XRefStm of the cross-reference table at offset {naming} names offset {named}, {what}: "));
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().NotContain(PdfDiagnosticCodes.XRefChainCycle);
        document.Reader.Structure.LoopOffset.Should().Be(-1);
        document.Reader.Structure.Sections.Should().ContainSingle(section => section.NamedBy == "/Prev").Which.State.Should().NotBe(XRefSectionState.NotFound);
        document.WasRepaired.Should().BeFalse();
    }

    [Fact]
    public void An_xrefstm_naming_offset_zero_of_a_table_with_no_prev_is_read_where_it_names_whatever_precedes_the_header()
    {
        // Five bytes precede the header, and the update's table, which gives no /Prev, names offset 0 through /XRefStm: what
        // it names is looked for there, the first table found near it and read in the stream's place. A table with no /Prev
        // has no section its /Prev reaches, though the byte before the header, where an offset of -1 would lie, is in the
        // file (#156).
        var file = Encoding.Latin1.GetBytes("junk\n" + Encoding.Latin1.GetString(PdfTemplate.Build(SoundUpdated
            .Replace("/Prev {xref:1}", "/XRefStm 0", StringComparison.Ordinal))));
        var naming = PdfTemplate.OffsetOf(file, "\nxref\n", 2) + 1;
        using var document = PdfDocument.Open(file);

        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(
            PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefHybridStreamLost);
        document.Diagnostics[^1].Message.Should().StartWith(Invariant(
            $"The /XRefStm of the cross-reference table at offset {naming} names offset 5, a cross-reference table, whose rows were read in the stream's place: "));
        document.Reader.Structure.Sections.Select(section => (section.NamedBy, section.State)).Should().Equal(
            ("startxref", XRefSectionState.Read), ("/XRefStm", XRefSectionState.Relocated));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void An_xrefstm_of_a_file_without_a_header_is_judged_by_what_it_reaches_from_the_file_s_first_byte(bool namesTheFirstByte)
    {
        // A file with no header counts its offsets from its first byte, where the first table lies. Read through startxref, it
        // names the second through /Prev, and the second names it back through /Prev and /XRefStm both: the /XRefStm names
        // the table the chain read through startxref, not what its own /Prev reaches, and the /Prev loops. Or the second, read
        // first, names the table at the first byte through /Prev and gives an /XRefStm outside the file, which reaches
        // nothing, whatever the /Prev reaches: a section missing, the first table read (#156).
        var (file, second) = namesTheFirstByte
            ? Headerless(" /Prev {second}", " /Prev 0 /XRefStm 0", startAtFirst: true)
            : Headerless(string.Empty, " /Prev 0 /XRefStm 99999999", startAtFirst: false);
        using var document = PdfDocument.Open(file);

        var codes = document.Diagnostics.Select(diagnostic => diagnostic.Code).ToList();
        codes[0].Should().Be(PdfDiagnosticCodes.HeaderMissing);

        if (namesTheFirstByte)
        {
            codes.Should().Equal(PdfDiagnosticCodes.HeaderMissing, PdfDiagnosticCodes.XRefHybridStreamLost, PdfDiagnosticCodes.XRefChainCycle);
            document.Diagnostics[1].Message.Should().StartWith(Invariant(
                $"The /XRefStm of the cross-reference table at offset {second} names offset 0, the cross-reference table at offset 0, which the chain read through startxref: "));
            document.Reader.Structure.LoopOffset.Should().Be(0);
        }
        else
        {
            codes.Should().Equal(PdfDiagnosticCodes.HeaderMissing, PdfDiagnosticCodes.XRefSectionMissing);
            document.Diagnostics[1].Message.Should().StartWith("The cross-reference section /XRefStm names at offset 99999999 lies outside the file");
            document.Reader.Structure.Sections.Select(section => (section.NamedBy, section.State)).Should().Equal(
                ("startxref", XRefSectionState.Read), ("/XRefStm", XRefSectionState.NotFound), ("/Prev", XRefSectionState.Read));
        }
    }

    [Fact]
    public void An_xrefstm_falling_short_of_where_its_table_s_prev_names_a_section_found_moved_names_no_section_short()
    {
        // The newest table names through /Prev the endobj 7 bytes before the table before it, where the search near it finds
        // that table, and through /XRefStm the end of line before that endobj: both links reach the endobj, which the /Prev,
        // left to read it, finds no section at. What the /XRefStm crossed is white space before no section, and its offset is
        // not counted among those that name a section short (#156).
        var built = HybridFiles.NamingThePreviousTable();
        var previous = PreviousSection(built, stream: false);
        var file = Encoding.Latin1.GetBytes(Encoding.Latin1.GetString(built).Replace(
            Invariant($"/Prev {previous} /XRefStm {previous} >>"), Invariant($"/Prev {previous - 7} /XRefStm {previous - 8} >>"), StringComparison.Ordinal));
        file.Should().HaveCount(built.Length, "the offsets are written in as many digits");
        Encoding.Latin1.GetString(file, (int)previous - 8, 8).Should().Be("\nendobj\n");
        using var document = PdfDocument.Open(file);

        var sections = document.Reader.Structure.Sections;
        sections.Select(section => (section.State, section.Offset)).Skip(1).Take(2).Should().Equal(
            (XRefSectionState.NamedAgain, previous - 8), (XRefSectionState.Relocated, previous));
        sections[1].ReadAs.Should().BeSameAs(sections[2]);
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(PdfDiagnosticCodes.XRefHybridStreamLost, PdfDiagnosticCodes.XRefOffsetAdjusted);
        var findings = new PdfValidator().Validate(document).Findings.Select(finding => finding.RuleId).ToList();
        findings.Should().Contain(PdfValidationRuleIds.XRefSectionShifted).And.NotContain(PdfValidationRuleIds.XRefOffsetImprecise);
    }

    [Fact]
    public void An_xrefstm_and_a_prev_naming_a_table_the_chain_read_report_the_naming_and_the_loop()
    {
        // The first table names the newest, which the chain read through startxref, through /Prev and /XRefStm both: the
        // /XRefStm names no stream of its revision, and the /Prev loops (#156).
        var file = HybridFiles.FirstNamingTheNewestTwice();
        var first = PdfTemplate.OffsetOf(file, "\nxref\n") + 1;
        var newest = PdfTemplate.OffsetOf(file, "\nxref\n", 2) + 1;
        using var document = PdfDocument.Open(file);

        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(PdfDiagnosticCodes.XRefHybridStreamLost, PdfDiagnosticCodes.XRefChainCycle);
        document.Diagnostics[0].Message.Should().StartWith(Invariant(
            $"The /XRefStm of the cross-reference table at offset {first} names offset {newest}, the cross-reference table at offset {newest}, which the chain read through startxref: "));
        document.Reader.Structure.Sections[2].ReadAs.Should().BeSameAs(document.Reader.Structure.Sections[0]);
        document.Reader.Structure.LoopOffset.Should().Be(newest);
    }

    [Fact]
    public void A_table_read_in_the_stream_s_place_and_named_again_is_reported_for_each_table_that_names_it()
    {
        // The update's /XRefStm and the first table's both name a table no /Prev names: read once, in the update's stream's
        // place, and no stream of either revision (#156).
        var file = HybridFiles.NamingATableNeverRead(firstNamesIt: true);
        var named = PdfTemplate.OffsetOf(file, "\nxref\n", 2) + 1;
        using var document = PdfDocument.Open(file);

        var sections = document.Reader.Structure.Sections;
        sections.Select(section => section.State).Should().Equal(
            XRefSectionState.Read, XRefSectionState.Read, XRefSectionState.Read, XRefSectionState.NamedAgain);
        sections[3].ReadAs.Should().BeSameAs(sections[1]);
        document.Diagnostics.Select(diagnostic => diagnostic.Message).Should().SatisfyRespectively(
            first => first.Should().Contain(Invariant($"names offset {named}, a cross-reference table, whose rows were read in the stream's place: ")),
            second => second.Should().Contain(Invariant($"names offset {named}, the cross-reference table at offset {named}, which the chain read through /XRefStm: ")));
    }

    [Fact]
    public void An_offset_two_tables_name_where_nothing_lies_is_reported_missing_once()
    {
        // Both tables' /XRefStm name an offset in a comment, more than 512 bytes from anything: looked for once, reported
        // missing once, and the older table's naming recorded as naming what the chain looked for there (#156).
        var hole = PdfTemplate.OffsetOf(HybridFiles.TablesNamingNothing("0000000000"), "%---") + 600;
        var file = HybridFiles.TablesNamingNothing(hole.ToString("D10", CultureInfo.InvariantCulture));
        using var document = PdfDocument.Open(file);

        var sections = document.Reader.Structure.Sections;
        sections.Select(section => section.State).Should().Equal(
            XRefSectionState.Read, XRefSectionState.NotFound, XRefSectionState.Read, XRefSectionState.NamedAgain);
        sections[3].ReadAs.Should().BeSameAs(sections[1]);
        sections[3].NamesNoStream.Should().BeNull();
        document.Diagnostics.Should().ContainSingle().Which.Code.Should().Be(PdfDiagnosticCodes.XRefSectionMissing);
    }

    [Fact]
    public void An_xrefstm_naming_the_cross_reference_stream_startxref_names_is_reported()
    {
        // A table's /XRefStm names a stream, but one the chain read through startxref, of a newer revision: no stream of the
        // table's own revision, which alone could index the page (#156).
        var (file, table, stream) = HybridFiles.NamingANewerStream();
        using var document = PdfDocument.Open(file);

        var again = document.Reader.Structure.Sections.Should().ContainSingle(section => section.State == XRefSectionState.NamedAgain).Which;
        again.ReadAs.Should().BeSameAs(document.Reader.Structure.Sections[0]);
        document.Diagnostics.Should().ContainSingle().Which.Message.Should().Be(Invariant(
            $"The /XRefStm of the cross-reference table at offset {table} names offset {stream}, the cross-reference stream at offset {stream}, which the chain read through startxref: ISO 32000-1 has it name the cross-reference stream of the table's revision (Table 19). The objects only that stream indexes are looked for by rebuilding the index."));
        document.GetObject(new PdfObjectId(3)).AsDictionary()!.IsOfType(PdfName.Page).Should().BeTrue();
        document.WasRepaired.Should().BeTrue();
    }

    [Fact]
    public void A_table_an_xrefstm_names_and_no_prev_reaches_is_read_in_the_stream_s_place_and_reported()
    {
        // The update's /XRefStm names a classic table no /Prev names: its rows are read in the stream's place, as batch 16
        // settled (#263), and the naming, passed over in silence, is reported (#156). What it indexes reads with no rebuild.
        var file = HybridFiles.NamingATableNeverRead();
        var named = PdfTemplate.OffsetOf(file, "\nxref\n", 2) + 1;
        var update = PdfTemplate.OffsetOf(file, "\nxref\n", 3) + 1;
        using var document = PdfDocument.Open(file);

        var table = document.Reader.Structure.Sections[1];
        table.NamedBy.Should().Be("/XRefStm");
        table.State.Should().Be(XRefSectionState.Read);
        table.NamesNoStream.Should().Be("a cross-reference table, whose rows were read in the stream's place");
        var lost = document.Diagnostics.Should().ContainSingle().Which;
        lost.Code.Should().Be(PdfDiagnosticCodes.XRefHybridStreamLost);
        lost.Position.Should().Be(named);
        lost.Message.Should().StartWith(Invariant($"The /XRefStm of the cross-reference table at offset {update} names offset {named}, a cross-reference table, whose rows were read in the stream's place: "));
        Text(document, 9).Should().Be("nine");
        Text(document, 10).Should().Be("ten");
        document.WasRepaired.Should().BeFalse();
    }

    [Fact]
    public void A_table_an_xrefstm_names_is_reported_where_a_guard_cuts_its_rows_as_where_it_is_read_whole()
    {
        // The index guard fills at the table the update's /XRefStm names: that the /XRefStm names a table was seen before the
        // cut, and is the file's to report, as the whole read reports it (ADR 34, #156).
        var file = HybridFiles.NamingATableNeverRead();
        using var whole = PdfDocument.Open(file);
        using var guarded = PdfDocument.Open(file, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxIndexEntries = 2 } });

        guarded.Reader.Structure.Sections[1].CutByLimit.Should().BeTrue();
        var lost = guarded.Diagnostics.Should().ContainSingle(diagnostic => diagnostic.Code == PdfDiagnosticCodes.XRefHybridStreamLost).Which;
        var read = whole.Diagnostics.Should().ContainSingle().Which;
        lost.Position.Should().Be(read.Position);
        lost.Message.Should().Be(read.Message);
        guarded.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == PdfDiagnosticCodes.LimitIndexEntries);
    }

    [Theory]
    [InlineData("10 1\n(garbage)\n", "the row for object 10, at offset ")]
    [InlineData("(garbage)\n{row:10}\n", "it holds a string at offset ")]
    public void A_table_an_xrefstm_names_that_cannot_be_read_is_reported_beside_its_own_fault_as_naming_no_stream(string ten, string fault)
    {
        // The update's /XRefStm names a classic table no /Prev names, whose row for object 10, or whose subsection's header,
        // is no such thing. Only the table's fault was reported: that the /XRefStm names a table, where ISO 32000-1 has it name
        // a cross-reference stream, whatever the table holds, was not, as it is where the table reads (#156). What follows the
        // fault, a token that starts no subsection, cannot be known: no older row stands for a number the table does not give,
        // the catalog among them, which a rebuild finds as the document opens.
        var file = HybridFiles.NamingATableNeverRead(ten: ten);
        var table = PdfTemplate.OffsetOf(file, "\nxref\n", 2) + 1;
        var update = PdfTemplate.OffsetOf(file, "\nxref\n", 3) + 1;

        using var document = PdfDocument.Open(file);

        document.Diagnostics.Select(d => d.Code).Should().Equal(
            PdfDiagnosticCodes.XRefSectionUnreadable, PdfDiagnosticCodes.XRefHybridStreamLost, PdfDiagnosticCodes.XRefRebuilt);
        document.Diagnostics[0].Message.Should().StartWith(Invariant($"The cross-reference table /XRefStm names at offset {table} is there but cannot be read: {fault}"))
            .And.EndWith(". The rows read before the fault, if any, were kept; every other object no section read before it indexes is looked for by rebuilding the index, rather than taken from an older section's rows.");
        var lost = document.Diagnostics[1];
        lost.Position.Should().Be(table);
        lost.Message.Should().Be(Invariant(
            $"The /XRefStm of the cross-reference table at offset {update} names offset {table}, a cross-reference table, which cannot be read: ISO 32000-1 has it name the cross-reference stream of the table's revision (Table 19). The objects only that stream indexes are looked for by rebuilding the index."));
        document.Reader.Structure.Sections[1].NamesNoStream.Should().Be("a cross-reference table, which cannot be read");
        Text(document, 10).Should().Be("ten");
    }

    [Fact]
    public void A_table_two_xrefstm_name_that_cannot_be_read_is_reported_for_each_table_that_names_it()
    {
        // Both tables name through /XRefStm the classic table that cannot be read: each naming is reported, as each naming of a
        // table that reads is (#156), the second as named again, the table read once. The catalog, which only the first table
        // indexes, is found by rebuilding the index, what follows the table's fault being unknown.
        var file = HybridFiles.NamingATableNeverRead(firstNamesIt: true, ten: "10 1\n(garbage)\n");
        var first = PdfTemplate.OffsetOf(file, "\nxref\n") + 1;
        var table = PdfTemplate.OffsetOf(file, "\nxref\n", 2) + 1;
        var update = PdfTemplate.OffsetOf(file, "\nxref\n", 3) + 1;

        using var document = PdfDocument.Open(file);

        document.Diagnostics.Select(d => d.Code).Should().Equal(
            PdfDiagnosticCodes.XRefSectionUnreadable,
            PdfDiagnosticCodes.XRefHybridStreamLost,
            PdfDiagnosticCodes.XRefHybridStreamLost,
            PdfDiagnosticCodes.XRefRebuilt);
        document.Diagnostics[1].Message.Should().StartWith(Invariant(
            $"The /XRefStm of the cross-reference table at offset {update} names offset {table}, a cross-reference table, which cannot be read: "));
        document.Diagnostics[2].Message.Should().StartWith(Invariant(
            $"The /XRefStm of the cross-reference table at offset {first} names offset {table}, the cross-reference table at offset {table}, which cannot be read: "));
        var again = document.Reader.Structure.Sections.Should().ContainSingle(section => section.State == XRefSectionState.NamedAgain).Which;
        again.ReadAs.Should().BeSameAs(document.Reader.Structure.Sections[1]);
    }

    [Theory]
    [InlineData(true, "a cross-reference table, which one of the reader's limits cut before its first row")]
    [InlineData(false, "a cross-reference table, whose rows were read in the stream's place")]
    public void A_table_an_xrefstm_names_that_a_guard_cuts_before_its_rows_is_said_to_be_cut_there_and_never_one_that_cannot_be_read(
        bool guarded, string what)
    {
        // The newest table indexes nothing and names through /XRefStm a table whose rows lie past a comment of 200 bytes, which
        // MaxXRefSectionLength = 128 cuts before them: the table is the guard's, which says so, not one that cannot be read,
        // and no report calls it so (ADR 34). The /XRefStm names a table where a stream belongs, as read whole, but none of its
        // rows was read in the stream's place, as the naming said: it says the guard cut the table before its first row (#156).
        var file = PdfTemplate.Build("""
            %PDF-1.7
            1 0 obj
            << /Type /Catalog /Pages 2 0 R >>
            endobj
            2 0 obj
            << /Type /Pages /Kids [] /Count 0 >>
            endobj
            10 0 obj
            (ten)
            endobj
            xref
            {comment}
            10 1
            {row:10}
            trailer
            << /Size 11 >>
            xref
            0 0
            trailer
            << /Size 11 /Root 1 0 R /XRefStm {xref:1} >>
            startxref
            {xref:2}
            %%EOF

            """.Replace("{comment}", "%" + new string('-', 200), StringComparison.Ordinal));
        var table = PdfTemplate.OffsetOf(file, "\nxref\n") + 1;
        var newest = PdfTemplate.OffsetOf(file, "\nxref\n", 2) + 1;

        using var document = guarded
            ? PdfDocument.Open(file, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxXRefSectionLength = 128 } })
            : PdfDocument.Open(file);

        var section = document.Reader.Structure.Sections[1];
        section.CutByLimit.Should().Be(guarded);
        section.NamesNoStream.Should().Be(what);
        document.Diagnostics.Select(d => d.Code).Should().Equal(guarded
            ? [PdfDiagnosticCodes.LimitXRefSectionLength, PdfDiagnosticCodes.XRefHybridStreamLost, PdfDiagnosticCodes.XRefRebuilt]
            : [PdfDiagnosticCodes.XRefHybridStreamLost, PdfDiagnosticCodes.XRefRebuilt]);
        document.Diagnostics.Should().ContainSingle(d => d.Code == PdfDiagnosticCodes.XRefHybridStreamLost).Which.Message.Should().Be(Invariant(
            $"The /XRefStm of the cross-reference table at offset {newest} names offset {table}, {what}: ISO 32000-1 has it name the cross-reference stream of the table's revision (Table 19). The objects only that stream indexes are looked for by rebuilding the index."));
        document.Diagnostics.Should().NotContain(d => d.Code == PdfDiagnosticCodes.XRefSectionUnreadable || d.Message.Contains("which cannot be read", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("/Prev 99999999 /XRefStm 99999999", null, "outside the file, which its /Prev names too", PdfDiagnosticCodes.XRefSectionMissing)]
    [InlineData("/Prev 99999999 /XRefStm 99999999", "table rows", "outside the file, which its /Prev names too", PdfDiagnosticCodes.XRefSectionMissing)]
    [InlineData("/Prev {hole} /XRefStm {hole}", null, "what its /Prev names too, which the chain looks for through that /Prev", PdfDiagnosticCodes.XRefSectionMissing)]
    [InlineData("/Prev {hole} /XRefStm {hole}", "table rows", "the section its /Prev names too, past the guard that stopped the table", null)]
    [InlineData("/Prev {older} /XRefStm {older}", null, "what its /Prev names too, which the chain looks for through that /Prev", null)]
    public void An_xrefstm_naming_what_its_table_s_prev_names_says_no_more_of_it_than_is_known_before_the_prev_is_read(
        string links, string? guard, string what, string? next)
    {
        // The table names through /Prev and /XRefStm one offset: outside the file, inside a string of 1,500 bytes where no
        // section lies within 512 bytes, or the older table. The /XRefStm is reported before the /Prev is read, and said the
        // chain reads a section there through that /Prev, which the report after it then said lies outside the file, or is
        // not there (#156). It says what is known then: an offset outside the file; what the /Prev names, which the chain
        // looks for; or, past a guard that stopped the table, the guard's to hide (#264).
        var twentyOne = "(" + new string('x', 1500) + ")";
        var (built, _) = StoppedSectionFiles.Stopped(links.Replace("{hole}", "0000000000", StringComparison.Ordinal), twentyOne: twentyOne);
        var hole = PdfTemplate.OffsetOf(built, "(xxx") + 750;
        var (file, section) = StoppedSectionFiles.Stopped(
            links.Replace("{hole}", hole.ToString("D10", CultureInfo.InvariantCulture), StringComparison.Ordinal), twentyOne: twentyOne);
        var named = links.Contains("{hole}", StringComparison.Ordinal) ? hole
            : links.Contains("{older}", StringComparison.Ordinal) ? PdfTemplate.OffsetOf(file, "xref\n")
            : 99999999;

        using var document = guard is null ? PdfDocument.Open(file) : PdfDocument.Open(file, StoppedSectionFiles.Guard(guard));

        var lost = document.Diagnostics.Should().ContainSingle(d => d.Code == PdfDiagnosticCodes.XRefHybridStreamLost).Which;
        lost.Message.Should().StartWith(Invariant($"The /XRefStm of the cross-reference table at offset {section} names offset {named}, {what}: ISO 32000-1 "));
        document.Diagnostics.SkipWhile(d => d.Code != PdfDiagnosticCodes.XRefHybridStreamLost).Skip(1).Select(d => d.Code).FirstOrDefault().Should().Be(next);
        document.Reader.Structure.Sections.Should().ContainSingle(record => record.NamedBy == "/XRefStm").Which.NamesWhatPreviousReads
            .Should().Be(guard is null && named != 99999999);
    }

    [Theory]
    [InlineData(false, 0, "")]
    [InlineData(true, 0, "")]
    [InlineData(false, 1, "")]
    [InlineData(true, 1, "")]
    [InlineData(false, 1, "junk\n")]
    [InlineData(true, 1, "junk\n")]
    [InlineData(false, 7, "")]
    [InlineData(true, 7, "")]
    [InlineData(false, 7, "junk\n")]
    [InlineData(true, 7, "junk\n")]
    public void An_xrefstm_reaching_what_its_table_s_prev_names_is_left_to_the_prev_which_reads_the_section_whole(
        bool stream, int shortBy, string junk)
    {
        // The newest table names the section before it through /Prev and through /XRefStm: at the same offset, at the end of
        // line before it, or 7 bytes before it, in the endobj before it, where the search near that offset finds the section —
        // and the file starts with junk before its header, or not. Read as the table's stream first, the section had its own
        // /Prev left unread, the /Prev looped back to it, and what only the oldest table indexes, the page, was found by
        // rebuilding the index. The /XRefStm is not followed: the /Prev reads the section whole, its own /Prev with it, and the
        // /XRefStm, which names no stream of its revision, is reported (#156). Only an offset that names the end of line is
        // imprecise: what lies 7 bytes before is no white space.
        var built = stream ? HybridFiles.NamingThePreviousStream().File : HybridFiles.NamingThePreviousTable();
        var named = PreviousSection(built, stream);
        var file = Encoding.Latin1.GetBytes(junk + Encoding.Latin1.GetString(built).Replace(
            Invariant($"/XRefStm {named} >>"), Invariant($"/XRefStm {named - shortBy} >>"), StringComparison.Ordinal));
        var oldest = PdfTemplate.OffsetOf(file, "\nxref\n") + 1;
        var previous = PreviousSection(file, stream);
        var newest = PdfTemplate.OffsetOf(file, "\nxref\n", stream ? 2 : 3) + 1;
        using var document = PdfDocument.Open(file);

        var sections = document.Reader.Structure.Sections;
        sections.Select(section => (section.State, section.Offset)).Should().Equal(
            (XRefSectionState.Read, newest),
            (XRefSectionState.NamedAgain, previous - shortBy),
            (XRefSectionState.Read, previous),
            (XRefSectionState.Read, oldest));
        sections[1].ReadAs.Should().BeSameAs(sections[2]);
        sections[2].NamedBy.Should().Be("/Prev");
        document.Reader.Structure.LoopOffset.Should().Be(-1);
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(
            junk.Length > 0
                ? [PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefHybridStreamLost]
                : [PdfDiagnosticCodes.XRefHybridStreamLost]);
        document.Diagnostics[^1].Message.Should().Be(Invariant(
            $"The /XRefStm of the cross-reference table at offset {newest} names offset {previous - shortBy}, what its /Prev names too, which the chain looks for through that /Prev: ISO 32000-1 has it name the cross-reference stream of the table's revision (Table 19). The objects only that stream indexes are looked for by rebuilding the index."));
        document.GetObject(new PdfObjectId(3)).AsDictionary()!.IsOfType(PdfName.Page).Should().BeTrue();
        document.WasRepaired.Should().BeFalse();
        var report = new PdfValidator().Validate(document);
        var findings = report.Findings.Select(finding => finding.RuleId).ToList();
        findings.Should().NotContain(PdfValidationRuleIds.XRefChainLoop);
        report.Findings.Should().ContainSingle(finding => finding.RuleId == PdfValidationRuleIds.XRefHybridStreamWrong).Which.Message.Should().Contain(Invariant(
            $"names offset {previous - shortBy}, the section its /Prev names too, which the chain read through that /Prev, where ISO 32000-1 names"));
        findings.Count(id => id == PdfValidationRuleIds.XRefOffsetImprecise).Should().Be(shortBy == 1 ? 1 : 0);
    }

    [Theory]
    [InlineData("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>", 0)]
    [InlineData("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>", 3)]
    [InlineData("<< /Type /Font /Subtype /CIDFontType2 /BaseFont /Gentium /W [1 [500]] >>", 0)]
    [InlineData("(just a string)", 0)]
    public void An_xrefstm_whose_table_s_prev_names_no_section_goes_on_past_what_that_prev_names_to_the_stream(string four, int prevShortBy)
    {
        // The newest table names object 4 through /Prev — a font, a CIDFont giving a /W, a string, at its header or in the
        // spaces before it — and, through /XRefStm, a place inside the string of object 8, 30 bytes before object 4 and 200
        // past the stream. The search near the /XRefStm stopped at object 4, where the /Prev names, as at a section the /Prev
        // would read: it left the stream unread, said the /XRefStm named what its /Prev names, and the oldest table's rows
        // served object 7. A /Prev that names no section keeps no place: it looks for its own nearby, and the /XRefStm's
        // search goes on past object 4 to the stream (#156).
        var (file, oldest, _, stream, newest) = HybridFiles.NamingAnObjectBesideTheStream(four, prevShortBy);
        using var document = PdfDocument.Open(file);

        document.Reader.Structure.Sections.Select(section => (section.NamedBy, section.State, section.Offset)).Should().Equal(
            ("startxref", XRefSectionState.Read, newest),
            ("/XRefStm", XRefSectionState.Relocated, stream),
            ("/Prev", XRefSectionState.Relocated, oldest));
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefOffsetAdjusted);
        Text(document, 7).Should().Be("new seven");
        Text(document, 5).Should().Be("five");
        document.WasRepaired.Should().BeFalse();
        new PdfValidator().Validate(document).Findings.Select(finding => finding.RuleId).Should().NotContain(PdfValidationRuleIds.XRefHybridStreamWrong);
    }

    [Theory]
    [InlineData("a font whose dictionary a guard cuts", 0, true)]
    [InlineData("a font whose dictionary a guard cuts", 1024 * 1024, false)]
    [InlineData("a font whose dictionary a lowered guard cuts", 1024, true)]
    [InlineData("a stream of no type", 0, false)]
    [InlineData("a dictionary of no type", 0, false)]
    [InlineData("a dictionary of no type", 1024, false)]
    [InlineData("a dictionary the end of the file cuts", 0, false)]
    [InlineData("a dictionary the end of the file cuts", -1, false)]
    [InlineData("a dictionary of no type that gives a /W", 0, false)]
    [InlineData("a stream of no type that gives a /W its rows cannot be read with", 0, false)]
    public void An_xrefstm_stops_short_of_its_stream_only_where_its_table_s_prev_keeps_its_place(string four, int maxTrailerLength, bool stops)
    {
        // The newest table names object 4 through /Prev, and through /XRefStm a place 30 bytes before it, whose search comes
        // to object 4 before the stream. Object 4 is a font whose dictionary holds 70,000 bytes before its /Type, which
        // MaxTrailerLength cuts before the /Type unless it is raised, or 2,000 bytes, which MaxTrailerLength lowered to 1,024
        // cuts; a stream of 70,000 bytes whose dictionary gives no /Type; a dictionary read whole that gives none either,
        // within MaxTrailerLength lowered to 1,024 too; a dictionary a string the file never closes runs to the end of the
        // file, which MaxTrailerLength lowered to the bytes from object 4 to that end (-1) reaches; or an object that gives a
        // /W and no /Type and does not read as a section — a border's dictionary, a stream whose /W gives a negative width —,
        // which the search stopped at, as at a section the /Prev would read, leaving the stream unread and an older revision
        // of object 7 served. The /Prev keeps its place where a guard cut what is there before it said what it is, the
        // guard's, and the /XRefStm's search stops there, leaving it to the /Prev, the naming the guard's too: no report, the
        // index not left incomplete, where the naming was reported, as if the file were at fault, and what only the stream
        // indexes found by rebuilding the index. Anywhere else, the end of the file included, the /Prev looks for its own
        // section nearby, and the search goes on to the stream (#156, #264).
        var body = four switch
        {
            "a font whose dictionary a guard cuts" => "<< /Pad (" + new string('-', 70_000) + ") /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            "a font whose dictionary a lowered guard cuts" => "<< /Pad (" + new string('-', 2_000) + ") /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            "a stream of no type" => "<< /Length 70000 >>\nstream\n" + new string('-', 70_000) + "\nendstream",
            "a dictionary of no type" => "<< /Pad (none) >>",
            "a dictionary of no type that gives a /W" => "<< /W 1 /S /S >>",
            "a stream of no type that gives a /W its rows cannot be read with" => "<< /W [1 -4 2] /Size 10 /Length 0 >>\nstream\n\nendstream",
            _ => "<< /Pad (",
        };
        var (file, oldest, at, stream, _) = HybridFiles.NamingAnObjectBesideTheStream(body);
        var limits = maxTrailerLength switch
        {
            0 => PdfReaderLimits.Default,
            < 0 => PdfReaderLimits.Default with { MaxTrailerLength = (int)(file.Length - at) },
            _ => PdfReaderLimits.Default with { MaxTrailerLength = maxTrailerLength },
        };
        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = limits });

        var sections = document.Reader.Structure.Sections;

        if (stops)
        {
            sections.Select(section => (section.NamedBy, section.State, section.Offset)).Skip(1).Should().Equal(
                ("/XRefStm", XRefSectionState.NamedAgain, at - 30),
                ("/Prev", XRefSectionState.Malformed, at));
            sections[2].CutByLimit.Should().BeTrue();
            sections[1].ReadAs.Should().BeSameAs(sections[2]);
            sections[1].NamesNoStream.Should().BeNull();
            document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(PdfDiagnosticCodes.LimitTrailer);
            document.GetObject(new PdfObjectId(7)).Should().BeSameAs(PdfNull.Instance, "what only the sections past the guard index is the guard's");
            document.WasRepaired.Should().BeFalse();
        }
        else
        {
            sections.Select(section => (section.NamedBy, section.State, section.Offset)).Skip(1).Should().Equal(
                ("/XRefStm", XRefSectionState.Relocated, stream),
                ("/Prev", XRefSectionState.Relocated, oldest));
            document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().NotContain(PdfDiagnosticCodes.XRefHybridStreamLost);
            Text(document, 7).Should().Be("new seven");
            document.WasRepaired.Should().BeFalse();
        }

        new PdfValidator().Validate(document).Findings.Select(finding => finding.RuleId).Should().NotContain(PdfValidationRuleIds.XRefHybridStreamWrong);
    }

    [Theory]
    [InlineData("/Type /XRefStream ")]
    [InlineData("")]
    [InlineData("/Type /XRef ")]
    public void An_xrefstm_stops_short_of_its_stream_where_its_table_s_prev_reads_a_stream_whatever_type_it_gives(string type)
    {
        // The newest table names object 4 through /Prev — a stream of /Type /XRefStream, of no type, or of /Type /XRef, which
        // reads as a cross-reference stream where the chain names it, indexes object 7's new revision, and names the oldest
        // table through its own /Prev — and, through /XRefStm, a place 28 bytes before it, whose search comes to object 4
        // before the oldest table. Of another type, object 4 was passed over, the oldest table read in the stream's place,
        // and object 4's /Prev, read next, looped back to it: an older revision of object 7 served, the chain said to loop.
        // The /Prev keeps its place there, as its own reading does wherever an object that gives a /W reads as a section:
        // the search stops there, and the /Prev reads object 4, then the oldest table, once (#156).
        var (file, oldest, four, named) = HybridFiles.PrevNamingAStreamBesideTheOldestTable(type);
        using var document = PdfDocument.Open(file);

        var sections = document.Reader.Structure.Sections;
        sections.Select(section => (section.NamedBy, section.State, section.Offset)).Skip(1).Should().Equal(
            ("/XRefStm", XRefSectionState.NamedAgain, named),
            ("/Prev", XRefSectionState.Read, four),
            ("/Prev", XRefSectionState.Read, oldest));
        sections[1].ReadAs.Should().BeSameAs(sections[2]);
        document.Reader.Structure.LoopOffset.Should().Be(-1);
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(PdfDiagnosticCodes.XRefHybridStreamLost);
        Text(document, 7).Should().Be("new seven");
        document.WasRepaired.Should().BeFalse();
        new PdfValidator().Validate(document).Findings.Select(finding => finding.RuleId).Should().NotContain(PdfValidationRuleIds.XRefChainLoop);
    }

    [Theory]
    [InlineData(65_350, false)]
    [InlineData(65_650, true)]
    public void A_stream_a_prev_names_through_white_space_meets_the_trailer_guard_only_where_its_own_dictionary_runs_past_it(int pad, bool cut)
    {
        // The newest table names stream 4 through /Prev at the first of 200 line feeds before it, and through /XRefStm a place
        // inside the string of object 8, right before them, whose search comes to stream 4 first, the table's own stream out of
        // reach. Stream 4's dictionary, about 65,450 bytes, lay within MaxTrailerLength, but not with the line feeds before it,
        // through which the /Prev's reading opened its window: the look at stream 4 read it whole and left the /XRefStm to the
        // /Prev, whose reading the guard cut, the naming reported as the file's fault and the index rebuilt as the file
        // opened. The white space before a section and its dictionary are each bounded by MaxTrailerLength, the section read
        // from its first token: the /Prev reads stream 4 where the look reads it, as a whole read does, and only a
        // dictionary that alone runs past the guard, about 65,750 bytes, is cut, for both, the guard's (#156, #264).
        var (file, oldest, run, four, _, named) = HybridFiles.PrevNamingARunBeforeAStream(200, pad, "inside a string before the run");
        using var document = PdfDocument.Open(file);

        var sections = document.Reader.Structure.Sections;
        sections[1].ReadAs.Should().BeSameAs(sections[2]);

        if (cut)
        {
            sections.Select(section => (section.NamedBy, section.State, section.Offset, section.Padding)).Skip(1).Should().Equal(
                ("/XRefStm", XRefSectionState.NamedAgain, named, 0),
                ("/Prev", XRefSectionState.Malformed, run, 200));
            sections[2].CutByLimit.Should().BeTrue();
            sections[1].NamesNoStream.Should().BeNull();
            var guard = document.Diagnostics.Should().ContainSingle().Which;
            guard.Code.Should().Be(PdfDiagnosticCodes.LimitTrailer);
            guard.Position.Should().Be(four, "the window the guard stopped opens at the stream's header");
            document.GetObject(new PdfObjectId(7)).Should().BeSameAs(PdfNull.Instance, "what only the sections past the guard index is the guard's");
        }
        else
        {
            sections.Select(section => (section.NamedBy, section.State, section.Offset, section.Padding)).Skip(1).Should().Equal(
                ("/XRefStm", XRefSectionState.NamedAgain, named, 0),
                ("/Prev", XRefSectionState.Read, run, 200),
                ("/Prev", XRefSectionState.Read, oldest, 0));
            document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(PdfDiagnosticCodes.XRefHybridStreamLost);
            Text(document, 7).Should().Be("new seven");
        }

        document.WasRepaired.Should().BeFalse();
        new PdfValidator().Validate(document).Findings.Count(finding => finding.RuleId == PdfValidationRuleIds.XRefHybridStreamWrong).Should().Be(cut ? 0 : 1);
    }

    [Theory]
    [InlineData(65_536, "inside a string past object 4")]
    [InlineData(65_530, "inside a string past object 4")]
    [InlineData(65_536, "the stream")]
    [InlineData(65_530, "the stream")]
    [InlineData(65_536, "in the run")]
    [InlineData(65_530, "in the run")]
    public void A_prev_naming_as_much_white_space_as_a_trailer_s_window_holds_reads_the_stream_past_it_as_a_whole_read_does(int run, string named)
    {
        // The newest table names stream 4 through /Prev at the first of 65,536 line feeds before it, as many bytes as
        // MaxTrailerLength lets a trailer hold, or 65,530, the stream's header across the window's end; and, through /XRefStm,
        // a place inside the string of object 8, past stream 4 and nearer it than the table's own stream; that stream; or the
        // run, 7 bytes before stream 4. The /Prev's reading opened its window at the run, found no object whole in it, and
        // looked for its section nearby: the oldest table, whose older revision of object 7 was served, while the look at
        // stream 4, and the positions the chain compares, took stream 4 for what the /Prev reads, leaving the /XRefStm to a
        // /Prev that never read it, said to name what that /Prev names. The run and the dictionary are each bounded by
        // MaxTrailerLength: the /Prev reads stream 4 past the run, and the /XRefStm is read, or left to that /Prev, as a read
        // under a bound of 1 MB reads and leaves them (#156, #188).
        var (file, oldest, runAt, four, stream, at) = HybridFiles.PrevNamingARunBeforeAStream(run, 0, named);
        using var document = PdfDocument.Open(file);
        using var whole = PdfDocument.Open(file, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxTrailerLength = 1024 * 1024 } });

        var sections = document.Reader.Structure.Sections;
        var stops = named != "the stream";
        sections.Select(section => (section.NamedBy, section.State, section.Offset, section.Padding)).Skip(1).Should().Equal(
            stops ? ("/XRefStm", XRefSectionState.NamedAgain, at, named == "in the run" ? 7 : 0) : ("/XRefStm", XRefSectionState.Read, stream, 0),
            ("/Prev", XRefSectionState.Read, runAt, run),
            ("/Prev", XRefSectionState.Read, oldest, 0));
        sections.Select(section => (section.NamedBy, section.State, section.Offset, section.Padding)).Should().Equal(
            whole.Reader.Structure.Sections.Select(section => (section.NamedBy, section.State, section.Offset, section.Padding)));
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(
            stops ? [PdfDiagnosticCodes.XRefHybridStreamLost] : []);
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(whole.Diagnostics.Select(diagnostic => diagnostic.Code));
        Text(document, 7).Should().Be("new seven");
        document.WasRepaired.Should().BeFalse();
        new PdfValidator().Validate(document).Findings.Count(finding => finding.RuleId == PdfValidationRuleIds.XRefHybridStreamWrong).Should().Be(stops ? 1 : 0);
    }

    [Fact]
    public void A_prev_naming_more_white_space_than_a_trailer_s_window_reaches_the_guard_where_it_starts_and_is_looked_for_nearby()
    {
        // The newest table names stream 4 through /Prev at the first of 65,537 line feeds before it, a byte more than
        // MaxTrailerLength lets the white space before a section run, and its own stream through /XRefStm. The run reaches the
        // guard where it starts, in words that say the white space ran past it, and nothing past it is read: the section is
        // looked for near the offset, as one that is not there, and the oldest table, 147 bytes before it, is found (#188).
        var (file, oldest, run, _, stream, _) = HybridFiles.PrevNamingARunBeforeAStream(65_537, 0, "the stream");
        using var document = PdfDocument.Open(file);

        document.Reader.Structure.Sections.Select(section => (section.NamedBy, section.State, section.Offset, section.NamedOffset)).Skip(1).Should().Equal(
            ("/XRefStm", XRefSectionState.Read, stream, stream),
            ("/Prev", XRefSectionState.Relocated, oldest, run));
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(PdfDiagnosticCodes.LimitTrailer, PdfDiagnosticCodes.XRefOffsetAdjusted);
        document.Diagnostics[0].Position.Should().Be(run);
        document.Diagnostics[0].Message.Should().StartWith("White space and comments run past 64 KB where a cross-reference section is named;");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_prev_naming_more_white_space_than_a_lowered_trailer_bound_is_not_read_from_there_and_finds_its_section_past_it(bool stream)
    {
        // The update's table names its original, a classic table or a cross-reference stream, through /Prev at the first of
        // the 257 spaces before it, under a MaxTrailerLength of 256: a byte more than the bound lets the white space before a
        // section run. The run reaches the guard where it starts, and the section is not read from there, as the message
        // says; the search near the offset, which reaches 512 bytes past it, finds the section past the run and reads it,
        // so that object 5 reads as the original gives it and object 7 as the update does (#188, #289).
        var (file, run, original, _) = NearbyFiles.UpdateNamingTheSpacesBeforeItsOriginal(257, stream);
        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxTrailerLength = 256 } });

        document.Reader.Structure.Sections.Select(section => (section.NamedBy, section.State, section.Kind, section.Offset, section.NamedOffset)).Skip(1).Should().Equal(
            ("/Prev", XRefSectionState.Relocated, stream ? XRefSectionKind.Stream : XRefSectionKind.Table, original, run));
        document.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Position)).Should().Equal(
            (PdfDiagnosticCodes.LimitTrailer, run),
            (PdfDiagnosticCodes.XRefOffsetAdjusted, original));
        document.Diagnostics[0].Message.Should().Be(
            "White space and comments run past 256 bytes where a cross-reference section is named; the section was not read from there. Raise PdfReaderLimits.MaxTrailerLength to read past it.");
        Text(document, 5).Should().Be("five");
        Text(document, 7).Should().Be("new seven");
        new PdfValidator().Validate(document).Findings.Select(finding => finding.RuleId).Should().Equal(PdfValidationRuleIds.XRefSectionShifted);
    }

    [Fact]
    public void An_xrefstm_naming_the_token_before_white_space_its_table_s_prev_names_past_a_trailer_s_window_is_left_to_no_prev()
    {
        // The newest table names through /XRefStm the bracket that closes an array, and through /Prev the first of the 65,537
        // line feeds after it, which run past a trailer's window. The /Prev's reading reads nothing past them, the guard's, and
        // reaches no section through them: it keeps no place, and comes to no start the /XRefStm reaches, so that the
        // /XRefStm is read where it names, the table's stream found a hundred bytes or so before it, nothing left to the
        // /Prev, which finds no section near the offset it names but the one the chain read (#156, #188).
        var (file, stream, bracket) = HybridFiles.NamingTheTokenBeforeARunOfWhiteSpace(65_537);
        using var document = PdfDocument.Open(file);

        var sections = document.Reader.Structure.Sections;
        sections.Select(section => (section.NamedBy, section.State, section.Offset, section.NamedOffset)).Skip(1).Take(2).Should().Equal(
            ("/XRefStm", XRefSectionState.Relocated, stream, bracket),
            ("/Prev", XRefSectionState.NotFound, bracket + 1, bracket + 1));
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().NotContain(PdfDiagnosticCodes.XRefHybridStreamLost);
        document.Diagnostics.Should().ContainSingle(diagnostic => diagnostic.Code == PdfDiagnosticCodes.LimitTrailer).Which.Position.Should().Be(bracket + 1);
        Text(document, 7).Should().Be("seven");
    }

    [Theory]
    [InlineData("/Type /XRefStream ", false)]
    [InlineData("", false)]
    [InlineData("/Type /XRefStream ", true)]
    [InlineData("", true)]
    public void An_xrefstm_beside_what_its_table_s_prev_names_again_is_left_to_no_prev_that_loops(string type, bool inWhiteSpace)
    {
        // The newest table names stream 4 through /Prev, where it reads as no section, its /W giving a width as a reference
        // only the middle table places: the /Prev finds the middle table nearby. That table names stream 4 again through
        // /Prev, a loop, and through /XRefStm a place inside the string of object 8, nearer stream 4 than the table's own
        // stream — or the spaces before stream 4. The look at stream 4, made with the middle table's rows indexed, read it as a
        // section, and the /XRefStm's search stopped there, leaving it to the /Prev, which ended the chain in a loop and read
        // nothing: the table's own stream unread, and the naming reported as the file's, naming what the chain looks for
        // through that /Prev. A /Prev that names what the chain has reached keeps no place: the /XRefStm's search goes on to
        // the table's stream, or takes stream 4 where it gives no type, or the /XRefStm reads it where it names it, past the
        // spaces, and the loop is reported as it was (#156, #188).
        var (file, stream, named, four, table, _) = HybridFiles.PrevLoopingBesideTheTablesStream(type, inWhiteSpace);
        using var document = PdfDocument.Open(file);

        var sections = document.Reader.Structure.Sections;
        sections.Select(section => (section.NamedBy, section.State, section.Offset)).Skip(1).Should().Equal(
            ("/Prev", XRefSectionState.Relocated, table),
            inWhiteSpace ? ("/XRefStm", XRefSectionState.Read, named)
                : ("/XRefStm", XRefSectionState.Relocated, type.Length > 0 ? stream : four));
        document.Reader.Structure.LoopOffset.Should().Be(four);
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(
            inWhiteSpace
                ? [PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefChainCycle]
                : [PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefChainCycle]);

        if (type.Length > 0 && !inWhiteSpace)
        {
            Text(document, 5).Should().Be("five");
            document.WasRepaired.Should().BeFalse("the table's own stream indexes object 5");
        }

        var findings = new PdfValidator().Validate(document).Findings.Select(finding => finding.RuleId).ToList();
        findings.Should().Contain(PdfValidationRuleIds.XRefChainLoop);
        findings.Should().NotContain(PdfValidationRuleIds.XRefHybridStreamWrong);
    }

    [Theory]
    [InlineData("X", false, "")]
    [InlineData("X", false, "/Filter /Foo ")]
    [InlineData("X", false, "/Filter /FlateDecode ")]
    [InlineData("\n", true, "")]
    public void A_stream_whose_data_the_trailer_guard_cuts_beside_a_width_a_guard_cut_leaves_the_prev_s_place_to_the_guard(string after, bool reads, string filter)
    {
        // The newest table names object 4 through /Prev, and through /XRefStm a place inside the string of object 8, 18 bytes
        // before it, whose search comes to object 4 first. Object 4's stream keyword ends exactly where MaxTrailerLength,
        // lowered to 2,048, cuts, a regular byte or an end of line past it, and its /W gives a width as a reference to object
        // 12, which MaxObjectLength, lowered to 1,024, cuts before its value. The /Prev's reading keeps nothing of data that
        // starts at the edge of the window a guard stopped at: its section is the guard's, its width unknown, and the look at
        // the place reads it alike, the naming the guard's, nothing reported of it, nothing rebuilt. Taking the data past the
        // edge for the stream's own, it would find too few rows for the widths read, a section the /Prev keeps its place at,
        // and the naming reported as the file's. Data the window's edge cut is not decoded to judge the section either, even
        // under a filter that decodes nothing of it, an unknown one or Flate whose data is corrupt from its first byte: told
        // undecoded, the section would be the file's to lose, the guard that cut object 12 unreported. Past an end of line,
        // the data starts within the window, the stream's own, and too short for its rows: the /Prev reads the section in
        // part, and the /XRefStm is left to it (#156, #221, #264).
        var (file, four, named) = HybridFiles.NamingAStreamWhoseKeywordEndsAt(2048, after, filter);
        var limits = PdfReaderLimits.Default with { MaxObjectLength = 1024, MaxTrailerLength = 2048 };
        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = limits });

        var sections = document.Reader.Structure.Sections;
        sections.Select(section => (section.NamedBy, section.State, section.Offset)).Skip(1).Take(2).Should().Equal(
            ("/XRefStm", XRefSectionState.NamedAgain, named),
            ("/Prev", reads ? XRefSectionState.Read : XRefSectionState.Malformed, four));
        sections[1].ReadAs.Should().BeSameAs(sections[2]);
        sections[1].NamesWhatPreviousReads.Should().Be(reads);
        sections[2].CutByValue.Should().Be(!reads);

        if (reads)
        {
            document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().StartWith(
                [PdfDiagnosticCodes.XRefHybridStreamLost, PdfDiagnosticCodes.XRefSectionUnreadable]);
            return;
        }

        sections[1].NamesNoStream.Should().BeNull();
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(PdfDiagnosticCodes.LimitTrailer, PdfDiagnosticCodes.LimitObject);
        document.GetObject(new PdfObjectId(7)).Should().BeSameAs(PdfNull.Instance, "what only the sections past the guard index is the guard's");
        document.WasRepaired.Should().BeFalse();
        new PdfValidator().Validate(document).Findings.Select(finding => finding.RuleId).Should().NotContain(PdfValidationRuleIds.XRefHybridStreamWrong);
    }

    [Fact]
    public void What_the_look_at_a_prev_s_place_reads_indexes_nothing_where_the_chain_ends_before_that_prev()
    {
        // The newest table, the one section MaxXRefSectionCount lets the chain read, indexes the catalog and the page tree,
        // and names through /Prev stream 4, which gives a /W and no /Type and reads, its one row placing object 3, and
        // through /XRefStm a place 18 bytes before it. Whether the /Prev keeps its place there was told reading stream 4,
        // and it does: the /XRefStm's search stops there, and the naming is reported. The guard ends the chain before the
        // /Prev is read: the row stream 4 gives object 3 is not in the index, which that look read without indexing
        // anything, and object 3 is looked for by rebuilding the index once it is asked for (#156).
        var file = new HandWrittenFile(version: "1.5");
        var catalog = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        var pages = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        var three = file.Object(3, "(three)");
        file.Raw("%" + new string('-', 1200) + "\n");
        var eight = file.Object(8, "(" + new string('x', 20) + ")");
        var row = "\u0001" + (char)((three >> 24) & 0xFF) + (char)((three >> 16) & 0xFF) + (char)((three >> 8) & 0xFF) + (char)(three & 0xFF) + "\0\0";
        file.Object(4, "<< /Size 9 /W [1 4 2] /Index [3 1] /Length 7 >>\nstream\n" + row + "\nendstream");
        file.Raw("%" + new string('-', 1200) + "\n");
        var newest = file.Position;
        file.Raw(Invariant(
            $"xref\n0 3\n0000000000 65535 f \n{catalog:D10} 00000 n \n{pages:D10} 00000 n \ntrailer\n<< /Size 9 /Root 1 0 R /Prev {eight + 38} /XRefStm {eight + 20} >>\nstartxref\n{newest}\n%%EOF\n"));
        using var document = PdfDocument.Open(file.Bytes, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxXRefSectionCount = 1 } });

        document.Reader.Structure.Sections.Select(section => (section.NamedBy, section.State, section.Offset)).Skip(1).Should().Equal(
            ("/XRefStm", XRefSectionState.NamedAgain, eight + 20));
        document.WasRepaired.Should().BeFalse();
        Text(document, 3).Should().Be("three");
        document.WasRepaired.Should().BeTrue("object 3 was in no index the chain read");
    }

    [Fact]
    public void What_the_look_at_a_prev_s_place_meets_is_dropped_and_crowds_out_nothing_the_prev_s_own_reading_reports()
    {
        // The same file, whose object 4, a stream of no type that reads, holds four strings in its dictionary where keys
        // should stand. Whether the /Prev keeps its place there was told reading object 4, and what that reading met is
        // dropped with it, whatever it met: in a document whose diagnostics hold four entries, the /Prev's own reading,
        // next, reports what it meets there, as far as there is room (#156).
        var (file, _, four, _) = HybridFiles.PrevNamingAStreamBesideTheOldestTable("(a) (b) (c) (d) ");
        using var document = PdfDocument.Open(file, new PdfReaderOptions { DiagnosticCapacity = 4 });

        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(
            PdfDiagnosticCodes.XRefHybridStreamLost,
            PdfDiagnosticCodes.SyntaxUnexpectedToken,
            PdfDiagnosticCodes.SyntaxUnexpectedToken,
            PdfDiagnosticCodes.SyntaxUnexpectedToken);
        document.Diagnostics[1].Position.Should().BeGreaterThan(four);
        document.Diagnostics.SuppressedCount.Should().Be(1);
    }

    [Fact]
    public void An_xrefstm_stops_short_of_its_stream_where_its_table_s_prev_names_a_stream_that_declares_itself_and_cannot_be_read()
    {
        // The same file, whose object 4 gives /Type /XRef and a /W of a negative width: it declares itself a section, and
        // cannot be read. The /Prev keeps its place there, as its own reading does where a section declares itself: the
        // /XRefStm's search stops there, rather than taking object 4 for the stream found moved, which cannot be read, the
        // /Prev then looping back to it, and the /Prev, read next, reads it there, reported once, and goes on through its
        // /Prev to the oldest table (#156, #208).
        var (file, oldest, four, named) = HybridFiles.PrevNamingAStreamBesideTheOldestTable("/Type /XRef ", widths: "[1 -4 2]");
        using var document = PdfDocument.Open(file);

        var sections = document.Reader.Structure.Sections;
        sections.Select(section => (section.NamedBy, section.State, section.Offset)).Skip(1).Should().Equal(
            ("/XRefStm", XRefSectionState.NamedAgain, named),
            ("/Prev", XRefSectionState.Malformed, four),
            ("/Prev", XRefSectionState.Read, oldest));
        sections[1].ReadAs.Should().BeSameAs(sections[2]);
        document.Reader.Structure.LoopOffset.Should().Be(-1);
    }

    [Theory]
    [InlineData("/Foo", PdfDiagnosticCodes.FilterUnsupported)]
    [InlineData("/FlateDecode", PdfDiagnosticCodes.FilterFailed)]
    public void An_xrefstm_goes_on_past_its_table_s_prev_where_the_prev_names_a_stream_of_no_type_whose_filters_decode_nothing_of_it(
        string filter, string decoding)
    {
        // The same file, whose object 4 gives no /Type and a filter that decodes nothing of its data, its row as written: an
        // object that gives only a /W and cannot be read, as data that holds no rows cannot, is no section where it is named,
        // and the /Prev looks for its section nearby (#221, #263). The look at the /Prev's place decodes the data as the
        // /Prev's reading does, and the /Prev keeps no place there: the /XRefStm's search goes on past object 4, to the oldest
        // table, which it reads in the stream's place, and the /Prev, whose own search comes there, reads it whole. The
        // encoded bytes were taken for object 4's row, the /XRefStm's search stopping there. What the look's decoding met is
        // dropped with it, and so is what the search's decoding of object 4 met, which drops the place: the /Prev's own
        // reading, decoding it again, reports it, once.
        var (file, oldest, four, named) = HybridFiles.PrevNamingAStreamBesideTheOldestTable(string.Empty, filter: filter);
        using var document = PdfDocument.Open(file);

        var sections = document.Reader.Structure.Sections;
        sections.Select(section => (section.NamedBy, section.State, section.Offset)).Skip(1).Should().Equal(
            ("/XRefStm", XRefSectionState.NamedAgain, named),
            ("/Prev", XRefSectionState.Relocated, oldest));
        sections[1].ReadAs.Should().BeSameAs(sections[2]);
        sections[2].NamedOffset.Should().Be(four);
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(
            PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefHybridStreamLost, decoding, PdfDiagnosticCodes.XRefOffsetAdjusted);
        document.Diagnostics[2].Position.Should().BeGreaterThan(four);
    }

    [Theory]
    [InlineData("[1 4 2] ]", "/Foo", null, PdfDiagnosticCodes.SyntaxUnexpectedToken, PdfDiagnosticCodes.FilterUnsupported)]
    [InlineData("[1 -4 2] ]", "", null, PdfDiagnosticCodes.SyntaxUnexpectedToken, null)]
    [InlineData("[1 4 2] /W [1 4 2]", "/Foo", null, PdfDiagnosticCodes.SyntaxKeyRepeated, PdfDiagnosticCodes.FilterUnsupported)]
    [InlineData("[1 4 2] /W [1 4 2]", "/FlateDecode", null, PdfDiagnosticCodes.SyntaxKeyRepeated, PdfDiagnosticCodes.FilterFailed)]
    [InlineData("[1 4 2]", "/Foo", 3, PdfDiagnosticCodes.StreamLengthInvalid, PdfDiagnosticCodes.FilterUnsupported)]
    [InlineData("[1 -4 2]", "", 3, PdfDiagnosticCodes.StreamLengthInvalid, null)]
    public void A_stream_of_no_type_the_search_near_an_offset_drops_keeps_what_reading_its_dictionary_reported_once_whatever_made_it_unreadable(
        string widths, string filter, int? length, string reported, string? decoding)
    {
        // The same file, whose object 4 gives no /Type, and in its dictionary a stray ], or the key /W twice, or a /Length of
        // 3 over its 7 bytes of data: an object that gives only a /W, which cannot be read, its filter decoding nothing of its
        // data or one of its widths negative. The /XRefStm's search reads it, and drops it, a place that declares no section,
        // what reading its dictionary reported standing, once, as for any place dropped: a reading of object 4 meeting the
        // fault again does not make the report again. Where its data could not be decoded, every report its reading made was
        // dropped with it, and the fault in its dictionary was reported nowhere, though object 4 is served. Only what its
        // decoding reported goes with the place, which the /Prev's own reading, decoding it again, reports once (#221).
        var (file, oldest, four, _) = HybridFiles.PrevNamingAStreamBesideTheOldestTable(
            string.Empty, widths: widths, filter: filter, length: length);
        using var document = PdfDocument.Open(file);

        document.Reader.Structure.Sections[^1].Should().Match<XRefSectionRecord>(section =>
            section.NamedBy == "/Prev" && section.State == XRefSectionState.Relocated && section.Offset == oldest);
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(
            decoding is null
                ? [reported, PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefHybridStreamLost, PdfDiagnosticCodes.XRefOffsetAdjusted]
                : [reported, PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefHybridStreamLost, decoding, PdfDiagnosticCodes.XRefOffsetAdjusted]);
        document.Diagnostics[0].Position.Should().BeGreaterThan(four);

        document.GetObject(new PdfObjectId(4)).Should().BeOfType<PdfStream>();
        document.Diagnostics.Count(diagnostic => diagnostic.Code == reported).Should().Be(1);
        document.Diagnostics.Count(diagnostic => diagnostic.Code == decoding).Should().Be(decoding is null ? 0 : 1);
    }

    [Theory]
    [InlineData("/Prev 0 /XRefStm 4", "", "9", "/XRefStm NamedAgain 4, /Prev Read 0")]
    [InlineData("/Prev 999999 /XRefStm 4", "", "9", "/XRefStm Relocated 0, /Prev NotFound 999999")]
    [InlineData("/Prev 0", " /Prev 5", "9", "/Prev Read 0, /Prev NotFound 5")]
    [InlineData("/Prev 5 /XRefStm 0", "", "9", "/XRefStm Read 0, /Prev NotFound 5")]
    [InlineData("/Prev 5 /XRefStm 0", "", "+9", "/XRefStm Read 0, /Prev NotFound 5")]
    [InlineData("/Prev 5", " /Filter /Foo", "+9", "/Prev Malformed 1")]
    public void A_section_at_the_file_s_first_byte_is_a_place_like_any_other(string newest, string first, string number, string expected)
    {
        // A file without a header, whose first byte starts cross-reference stream 9, its number written <number>, which
        // indexes the catalog and gives <first>; the newest table, after it, gives <newest>, an /XRefStm 4 bytes into the
        // stream's header, or at its first byte. A /Prev that names offset 0 keeps its place there, and the /XRefStm's search
        // stops at it, leaving it to the /Prev; one that names no place in the file leaves the search to go on, and the
        // /XRefStm finds the stream there, moved. Read, the stream is passed over by the search near a /Prev that names a
        // place inside it, as any section the chain read is (#156) — its header too where the /XRefStm read it from the sign
        // its number is written with, which the walk back from obj finds at the digit past it: told by its data, it is the
        // stream that reading started at the file's first byte, rather than another stream the search would take there.
        // Where its data names a filter the library does not know, nothing reads it: near the place a /Prev names inside
        // it, it is the section found moved, at the digit the walk finds, its header a token from the sign at the file's
        // first byte, as a load reads it — where it was dropped, a line test asked at that digit (#281, #327).
        var text = new StringBuilder();
        var rows = new string('\0', 14);
        var stream = Invariant($"{number} 0 obj\n<< /Type /XRef /Size 10 /W [1 4 2] /Index [1 2] /Length 14{first} >>\nstream\n{rows}\nendstream\nendobj\n");
        var one = stream.Length;
        var catalog = "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n";
        var two = one + catalog.Length;
        text.Append(stream.Replace(rows, StreamRow(one) + StreamRow(two), StringComparison.Ordinal));
        text.Append(catalog).Append("2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n");
        var table = text.Length;
        text.Append(Invariant($"xref\n0 1\n0000000000 65535 f \ntrailer\n<< /Size 10 /Root 1 0 R {newest} >>\nstartxref\n{table}\n%%EOF\n"));
        using var document = PdfDocument.Open(Encoding.Latin1.GetBytes(text.ToString()));

        string.Join(", ", document.Reader.Structure.Sections.Skip(1).Select(section => Invariant($"{section.NamedBy} {section.State} {section.Offset}")))
            .Should().Be(expected);
        document.GetObject(new PdfObjectId(2)).Should().BeOfType<PdfDictionary>();

        static string StreamRow(int offset) =>
            Invariant($"\u0001{(char)((offset >> 24) & 0xFF)}{(char)((offset >> 16) & 0xFF)}{(char)((offset >> 8) & 0xFF)}{(char)(offset & 0xFF)}\0\0");
    }

    [Fact]
    public void A_prev_naming_a_dictionary_a_file_cut_short_as_it_opens_ends_keeps_no_place_there()
    {
        // The file of the theory above, object 4 a dictionary that gives no /Type, is cut short inside it, 18 bytes past its
        // header, as the reader first reads there, while the document opens. The window the reader reads it through ends
        // where the file now ends, not at an edge of its own: the dictionary is read as far as the file goes, and says
        // nothing of a section, as one the end of the file cuts. The /Prev does not keep its place there, and the /XRefStm's
        // search goes on to the stream (#156).
        var (file, _, at, stream, _) = HybridFiles.NamingAnObjectBesideTheStream("<< /Pad (none) >>");
        using var source = new CutShortSource(file);
        source.CutWhenReadAt(at, (int)at + 18);
        using var document = PdfDocument.Open(source, options: null, ownsSource: false);

        document.Reader.Structure.Sections[1].Should().Match<XRefSectionRecord>(section =>
            section.NamedBy == "/XRefStm" && section.State == XRefSectionState.Relocated && section.Offset == stream);
    }

    [Fact]
    public void Raising_max_trailer_length_reads_no_more_of_a_prev_s_object_that_a_first_window_holds_whole()
    {
        // The file of the theory above, object 4 a dictionary that gives no /Type, whose newest table's /XRefStm is looked for
        // nearby: whether its /Prev keeps its place at object 4 is told from the dictionary, which the first window, 64 KB,
        // holds whole. Raised to 16 MB, MaxTrailerLength lets windows grow further, and none is read past that first one
        // (#156).
        var (file, _, _, _, _) = HybridFiles.NamingAnObjectBesideTheStream("<< /Pad (none) >>");

        Read(PdfReaderLimits.Default).Should().Be(Read(PdfReaderLimits.Default with { MaxTrailerLength = 16 * 1024 * 1024 }));

        long Read(PdfReaderLimits limits)
        {
            var source = new StrictCountingSource(file);
            using var document = PdfDocument.Open(source, new PdfReaderOptions { Limits = limits }, ownsSource: true);
            return source.BytesRead;
        }
    }

    [Fact]
    public void What_the_look_at_a_prev_s_object_meets_takes_no_room_from_what_the_document_reports()
    {
        // The newest table names object 4 through /Prev, a dictionary whose eight keys are numbers, and through /XRefStm a
        // place in a comment past it where no stream lies; MaxXRefSectionCount 1 ends the chain at the newest table, past
        // which object 4 is never read, and the reader keeps eight reports. The look at object 4 that tells whether the /Prev
        // keeps its place there, made as the /XRefStm is looked for nearby, keeps nothing of what it meets: object 3, loaded,
        // reports its own fault, and nothing is left out (#156).
        var file = new HandWrittenFile();
        var catalog = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        var pages = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        var three = file.Object(3, "<< /A 1 2 >>");
        var four = file.Object(4, "<< 1 2 3 4 5 6 7 8 >>");
        file.Raw("%" + new string('-', 1200) + "\n");
        file.Table(4, new() { [1] = catalog, [2] = pages, [3] = three }, Invariant($"/Size 5 /Root 1 0 R /Prev {four} /XRefStm {four + 80}"));
        var options = new PdfReaderOptions { DiagnosticCapacity = 8, Limits = PdfReaderLimits.Default with { MaxXRefSectionCount = 1 } };
        using var document = PdfDocument.Open(file.Bytes, options);

        document.GetObject(new PdfObjectId(3)).Should().BeOfType<PdfDictionary>();

        document.Diagnostics.Should().ContainSingle(diagnostic => diagnostic.Code == PdfDiagnosticCodes.SyntaxUnexpectedToken)
            .Which.Position.Should().BeGreaterThan(three).And.BeLessThan(four);
        document.Diagnostics.SuppressedCount.Should().Be(0);
    }

    [Theory]
    [InlineData(300)]
    [InlineData(400)]
    public void An_xrefstm_and_a_prev_written_too_large_by_one_amount_find_the_stream_and_the_table_they_meant(int shift)
    {
        // Every offset the newest table writes is too large by one amount: its /Prev names object 6, a string, and its
        // /XRefStm the place as far past the stream right after the oldest table. The search near the /XRefStm stopped at
        // object 6, where the /Prev names, and the /Prev's own search then took the stream for the older section, the oldest
        // table, which alone indexes the catalog, never read, and the file rebuilt as it opened. Object 6 is no section the
        // /Prev keeps: the /XRefStm finds its stream, and the /Prev the oldest table — its search comes to the stream first,
        // which, read as the table's own, it takes only where no other place holds a section (#156).
        var (file, oldest, stream, newest) = HybridFiles.WritingEveryOffsetTooLarge(shift);
        using var document = PdfDocument.Open(file);

        document.Reader.Structure.Sections.Select(section => (section.NamedBy, section.State, section.Offset)).Should().Equal(
            ("startxref", XRefSectionState.Read, newest),
            ("/XRefStm", XRefSectionState.Relocated, stream),
            ("/Prev", XRefSectionState.Relocated, oldest));
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefOffsetAdjusted);
        Text(document, 7).Should().Be("new seven");
        document.WasRepaired.Should().BeFalse();
        new PdfValidator().Validate(document).Findings.Select(finding => finding.RuleId).Should().NotContain(PdfValidationRuleIds.XRefHybridStreamWrong);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(3, false)]
    public void A_prev_falling_short_of_the_table_its_xrefstm_read_in_the_stream_s_place_reads_that_table_whole(int xrefStmShortBy, bool middle)
    {
        // The newest table names the table before it through /Prev 7 bytes short, in the endobj before it, and through
        // /XRefStm at its keyword, at the end of line before it, or 3 bytes short, where the search near it finds it. Read in
        // the stream's place, its own /Prev left unread, the table was passed over by the /Prev's search as reached: the
        // middle table its /Prev names was never read, and the oldest table's rows served object 3; or, with nothing else
        // near, the table was said to be neither there nor near it, and the file rebuilt as it opened. The /Prev reads it
        // whole, its links with it, and the /XRefStm names what the /Prev read, as where it names what the /Prev names (#156).
        var (file, oldest, middleAt, previous, newest) = HybridFiles.PrevFallingShortOfTheTableBefore(xrefStmShortBy, middle);
        using var document = PdfDocument.Open(file);

        var sections = document.Reader.Structure.Sections;
        sections.Select(section => (section.NamedBy, section.State, section.Offset)).Should().Equal(
            middle
                ? [
                    ("startxref", XRefSectionState.Read, newest),
                    ("/XRefStm", XRefSectionState.NamedAgain, previous - xrefStmShortBy),
                    ("/Prev", XRefSectionState.Relocated, previous),
                    ("/Prev", XRefSectionState.Read, middleAt),
                    ("/Prev", XRefSectionState.Read, oldest),
                ]
                : [
                    ("startxref", XRefSectionState.Read, newest),
                    ("/XRefStm", XRefSectionState.NamedAgain, previous - xrefStmShortBy),
                    ("/Prev", XRefSectionState.Relocated, previous),
                    ("/Prev", XRefSectionState.Read, oldest),
                ]);
        sections[1].ReadAs.Should().BeSameAs(sections[2]);
        sections[1].NamesWhatPreviousReads.Should().BeTrue();
        sections[1].Padding.Should().Be(xrefStmShortBy == 1 ? 1 : 0, "the white space the /XRefStm crossed falls short of the table, what it found moved does not");
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(
            xrefStmShortBy == 3
                ? [PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefHybridStreamLost, PdfDiagnosticCodes.XRefOffsetAdjusted]
                : [PdfDiagnosticCodes.XRefHybridStreamLost, PdfDiagnosticCodes.XRefOffsetAdjusted]);
        document.Diagnostics.Single(diagnostic => diagnostic.Code == PdfDiagnosticCodes.XRefHybridStreamLost).Message.Should().StartWith(Invariant(
            $"The /XRefStm of the cross-reference table at offset {newest} names offset {previous - xrefStmShortBy}, a cross-reference table, whose rows were read in the stream's place: "));
        document.Diagnostics[^1].Message.Should().Be(Invariant(
            $"The cross-reference section /Prev names at offset {previous - 7} was found 7 bytes after it."));
        Text(document, 3).Should().Be(middle ? "three as the middle revision has it" : "three as the oldest has it");
        document.WasRepaired.Should().BeFalse();
        var findings = new PdfValidator().Validate(document).Findings;
        findings.Should().NotContain(finding => finding.RuleId == PdfValidationRuleIds.XRefSectionNotFound);
        findings.Should().ContainSingle(finding => finding.RuleId == PdfValidationRuleIds.XRefHybridStreamWrong).Which.Message.Should().Contain(Invariant(
            $"names offset {previous - xrefStmShortBy}, the section its /Prev names too, which the chain read through that /Prev, where ISO 32000-1 names"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    public void An_older_xrefstm_naming_again_where_a_table_s_xrefstm_read_what_its_prev_read_names_what_the_prev_read(int before)
    {
        // The middle table names through /XRefStm what the newest's /XRefStm named, the end of line before the table before
        // the newest, or that table's keyword, which the newest's /Prev read whole once its search came to it: a naming of
        // the table the chain read through that /Prev, not of the reading the newest's /XRefStm made of it (#156).
        var (file, _, middleAt, previous, _) = HybridFiles.PrevFallingShortOfTheTableBefore(1, middle: true, middleXRefStm: before);
        using var document = PdfDocument.Open(file);

        var sections = document.Reader.Structure.Sections;
        var again = sections.Should().ContainSingle(section => section.NamedFrom == middleAt && section.NamedBy == "/XRefStm").Which;
        again.State.Should().Be(XRefSectionState.NamedAgain);
        again.ReadAs.Should().BeSameAs(sections[2]);
        document.Diagnostics.Where(diagnostic => diagnostic.Code == PdfDiagnosticCodes.XRefHybridStreamLost).Select(diagnostic => diagnostic.Message).Should().ContainSingle(message =>
            message.StartsWith(Invariant($"The /XRefStm of the cross-reference table at offset {middleAt} names offset {previous - before}, the cross-reference table at offset {previous}, which the chain read through /Prev: "), StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("no row")]
    [InlineData("a reserved row")]
    [InlineData("a reserved row, one row short")]
    [InlineData("a reserved row after a refused one")]
    [InlineData("no row", true)]
    [InlineData("no row, its /Index holding a reference")]
    public void A_prev_falling_short_of_the_stream_its_xrefstm_read_reads_it_whole_and_the_naming_is_reported(string seven, bool fourRefused = false)
    {
        // The newest table names cross-reference stream 6 through /XRefStm at its header, and through /Prev 7 bytes short of
        // it, in the endobj before it; the stream names the oldest table, 1,200 bytes off, through its own /Prev, and gives
        // object 7 no row, a row of a reserved type, that row short of the rows it declares, or that row after one for object 7
        // it refused itself. Read as the table's stream, it was passed over by the /Prev's search as reached, which found
        // nothing else: no report said the /XRefStm named what the /Prev names, the /Prev was said to name a section neither
        // there nor near it, and the file was rebuilt as it opened. With no other section near, the /Prev reads the stream
        // whole, its own /Prev with it, and the /XRefStm, which names what the /Prev read, is reported; what the stream's
        // reading reported is not reported again (#156). What that reading refused itself — the numbers a stream read in part
        // declares, a row for a number it gives twice —, which the /Prev's reading meets as refused before it, is not taken
        // for a refusal before: the validation said a reserved row the reader read as the null object was not read so, its
        // number refused before it was read (#221). A reserved row after the stream's own refused row for its number stands
        // for nothing, as at the stream's first reading, and object 7 is read through a rebuild. Where the newest table
        // refused its own row for object 4 first, a newer section refused the stream's row for it, as its first reading found.
        // Where the stream's /Index holds a reference, which the newest table places, its first reading, read in part, refused
        // every number to the older sections' rows: the /Prev's record said a section read before it had, where only that
        // reading of the stream itself did; it says none had, while the oldest table, read after it, has every number refused
        // before it, and the file is rebuilt as it opens for the catalog, which that table alone indexes, the page found
        // redefined (#210).
        var (file, oldest, stream, newest) = HybridFiles.PrevFallingShortOfTheStreamBefore(seven, fourRefused: fourRefused);
        var every = seven == "no row, its /Index holding a reference";
        using var document = PdfDocument.Open(file);

        var sections = document.Reader.Structure.Sections;
        sections.Select(section => (section.NamedBy, section.State, section.Kind, section.Offset)).Should().Equal(
            ("startxref", XRefSectionState.Read, XRefSectionKind.Table, newest),
            ("/XRefStm", XRefSectionState.NamedAgain, XRefSectionKind.Unknown, stream),
            ("/Prev", XRefSectionState.Relocated, XRefSectionKind.Stream, stream),
            ("/Prev", XRefSectionState.Read, XRefSectionKind.Table, oldest));
        sections[1].ReadAs.Should().BeSameAs(sections[2]);
        string[] codes = seven switch
        {
            "no row" when fourRefused => [PdfDiagnosticCodes.XRefSectionUnreadable, PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefHybridStreamLost],
            "no row" => [PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefHybridStreamLost],
            "no row, its /Index holding a reference" =>
            [
                PdfDiagnosticCodes.XRefSectionUnreadable, PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefHybridStreamLost,
                PdfDiagnosticCodes.XRefRebuilt, PdfDiagnosticCodes.ObjectRedefined,
            ],
            "a reserved row" => [PdfDiagnosticCodes.XRefEntryReserved, PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefHybridStreamLost],
            _ => [PdfDiagnosticCodes.XRefSectionUnreadable, PdfDiagnosticCodes.XRefEntryReserved, PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefHybridStreamLost],
        };
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(codes);
        document.Diagnostics.Single(diagnostic => diagnostic.Code == PdfDiagnosticCodes.XRefHybridStreamLost).Message.Should().Be(Invariant(
            $"The /XRefStm of the cross-reference table at offset {newest} names offset {stream}, the section its /Prev names too, which the chain read through that /Prev: ISO 32000-1 has it name the cross-reference stream of the table's revision (Table 19). The objects only that stream indexes are looked for by rebuilding the index."));
        var refusedFirst = seven == "a reserved row after a refused one";
        sections[2].ReservedRowsRefused.Should().Be(refusedFirst ? 1 : 0);
        sections[2].RowsRefusedBefore.Should().Be(fourRefused);
        sections[2].EveryNumberRefusedBefore.Should().BeFalse("what the stream's first reading refused is its own");
        sections[3].EveryNumberRefusedBefore.Should().Be(every);

        if (every)
        {
            document.Diagnostics[0].Message.Should().Be(Invariant(
                $"The cross-reference stream /XRefStm names at offset {stream} was read only in part: it holds 2 rows where its /Index and /Size declare 3; the rows it holds were kept, and every other object no section read before it indexes is looked for by rebuilding the index, rather than taken from an older section's rows."));
            document.Reader.ChainIndex!.TryGet(3, out _).Should().BeFalse();
        }

        Text(document, 3).Should().Be("three as the oldest has it");
        document.WasRepaired.Should().Be(every);

        if (refusedFirst)
        {
            Text(document, 7).Should().Be("seven as the oldest has it");
            document.WasRepaired.Should().BeTrue();
        }
        else if (!seven.StartsWith("no row", StringComparison.Ordinal))
        {
            document.GetObject(new PdfObjectId(7)).Should().BeSameAs(PdfNull.Instance);
            document.WasRepaired.Should().BeFalse();
        }

        var findings = new PdfValidator().Validate(document).Findings;
        findings.Should().NotContain(finding => finding.RuleId == PdfValidationRuleIds.XRefSectionNotFound);
        findings.Should().ContainSingle(finding => finding.RuleId == PdfValidationRuleIds.XRefHybridStreamWrong);

        if (seven.StartsWith("no row", StringComparison.Ordinal))
        {
            findings.Should().NotContain(finding => finding.RuleId == PdfValidationRuleIds.XRefEntryTypeReserved);
            return;
        }

        var reader = refusedFirst
            ? "but the reader did not read the row so, its number refused before it was read: it reads its object as the rebuild finds it"
            : "as the reader did until it rebuilt the index by scanning the file; it now reads each object such a row numbers as the rebuild finds it";
        findings.Should().ContainSingle(finding => finding.RuleId == PdfValidationRuleIds.XRefEntryTypeReserved).Which.Message.Should().Be(Invariant(
            $"The cross-reference stream at offset {stream} gives 1 row a reserved type: the row for object 7 gives it type 3. ISO 32000-1 defines types 0, 1 and 2, and reads any other as a reference to the null object (7.5.8.3), {reader}."));
    }

    [Fact]
    public void A_stream_a_prev_reads_again_where_its_xrefstm_read_it_is_recorded_as_read_after_a_newer_section_refused_every_number()
    {
        // As above, object 7 given no row, but the hybrid table is named through /Prev by stream 20, a newer cross-reference
        // stream whose /Index is no array, which the newest table names through /Prev in turn: stream 20 cannot be read,
        // hands its /Prev on, and refuses every number to the older sections' rows before the hybrid table's /XRefStm reads
        // stream 6. The /Prev that reads stream 6 again takes from that first reading what the sections read before it
        // refused: every number, and with them its rows, whatever the stream refused itself (#156, #210).
        var (file, oldest, stream, hybrid, lost, newest) = HybridFiles.PrevFallingShortOfTheStreamBeforeALostStream();
        using var document = PdfDocument.Open(file);

        var sections = document.Reader.Structure.Sections;
        sections.Select(section => (section.NamedBy, section.State, section.Kind, section.Offset)).Should().Equal(
            ("startxref", XRefSectionState.Read, XRefSectionKind.Table, newest),
            ("/Prev", XRefSectionState.Malformed, XRefSectionKind.Stream, lost),
            ("/Prev", XRefSectionState.Read, XRefSectionKind.Table, hybrid),
            ("/XRefStm", XRefSectionState.NamedAgain, XRefSectionKind.Unknown, stream),
            ("/Prev", XRefSectionState.Relocated, XRefSectionKind.Stream, stream),
            ("/Prev", XRefSectionState.Read, XRefSectionKind.Table, oldest));
        sections[3].ReadAs.Should().BeSameAs(sections[4]);
        sections[4].EveryNumberRefusedBefore.Should().BeTrue("stream 20 refused every number before the /XRefStm read stream 6");
        sections[4].RowsRefusedBefore.Should().BeTrue();
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(
            PdfDiagnosticCodes.XRefSectionUnreadable, PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefHybridStreamLost,
            PdfDiagnosticCodes.XRefRebuilt, PdfDiagnosticCodes.ObjectRedefined);
    }

    [Theory]
    [InlineData("the stream itself")]
    [InlineData("the table's /Prev")]
    [InlineData("outside the file")]
    [InlineData("a string")]
    [InlineData("null")]
    public void A_prev_falling_short_of_the_stream_its_xrefstm_read_takes_it_wherever_the_stream_gives_a_prev_and_reports_what_that_prev_names(string prev)
    {
        // The file of the test above, whose stream 6 names through its own /Prev the stream itself, the offset the table's
        // /Prev names, offset 99,999,999, a string, or null. The /Prev's search near the offset it names takes the stream the
        // /XRefStm read in the stream's place wherever the stream's own dictionary gives a /Prev, whatever that /Prev names —
        // once every other place was, where it names the offset the table's does, as the table's own stream may, and here no
        // other place near holds a section —: the stream is read whole, the /XRefStm reported as naming what the /Prev read,
        // and the stream's /Prev reported as the chain finds it — a loop, a section outside the file, no offset —, the objects
        // only the oldest table indexes found by rebuilding the index. A /Prev of null is none (7.3.7): the stream, which may
        // be the table's own and hands the chain nothing on, is not taken, and the /Prev names a section neither there nor
        // near it (#156). The page, object 3, written again as a string by the oldest revision, is found redefined by the
        // rebuild.
        var (file, _, stream, newest) = HybridFiles.PrevFallingShortOfTheStreamBefore(prev: prev);
        using var document = PdfDocument.Open(file);

        var sections = document.Reader.Structure.Sections;
        var codes = document.Diagnostics.Select(diagnostic => diagnostic.Code).ToList();

        if (prev == "null")
        {
            sections.Select(section => (section.NamedBy, section.State, section.Offset)).Should().Equal(
                ("startxref", XRefSectionState.Read, newest),
                ("/XRefStm", XRefSectionState.Read, stream),
                ("/Prev", XRefSectionState.NotFound, stream - 7));
            codes.Should().Equal(PdfDiagnosticCodes.XRefSectionMissing, PdfDiagnosticCodes.XRefRebuilt, PdfDiagnosticCodes.ObjectRedefined);
            document.Diagnostics[0].Message.Should().StartWith(Invariant($"The cross-reference section /Prev names at offset {stream - 7} is not there, nor near it;"));
            return;
        }

        sections.Select(section => (section.NamedBy, section.State, section.Kind, section.Offset)).Take(3).Should().Equal(
            ("startxref", XRefSectionState.Read, XRefSectionKind.Table, newest),
            ("/XRefStm", XRefSectionState.NamedAgain, XRefSectionKind.Unknown, stream),
            ("/Prev", XRefSectionState.Relocated, XRefSectionKind.Stream, stream));
        sections[1].ReadAs.Should().BeSameAs(sections[2]);
        var (fault, message) = prev switch
        {
            "the stream itself" => (PdfDiagnosticCodes.XRefChainCycle, Invariant($"The cross-reference chain loops back on itself: the /Prev of the section at offset {stream} names offset {stream}, which the chain has already read.")),
            "the table's /Prev" => (PdfDiagnosticCodes.XRefChainCycle, Invariant($"The cross-reference chain loops back on itself: the /Prev of the section at offset {stream} names offset {stream - 7}, which the chain has already read.")),
            "outside the file" => (PdfDiagnosticCodes.XRefSectionMissing, Invariant($"The cross-reference section /Prev names at offset 99999999 lies outside the file, which is {file.Length:N0} bytes long; the objects only it indexes are looked for by rebuilding the index.")),
            _ => (PdfDiagnosticCodes.XRefSectionMissing, Invariant($"The /Prev of the cross-reference section at offset {stream} is not an offset; the sections it names are looked for by rebuilding the index.")),
        };
        codes.Should().Equal(
            prev == "a string"
                ? [fault, PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefHybridStreamLost, PdfDiagnosticCodes.XRefRebuilt, PdfDiagnosticCodes.ObjectRedefined]
                : [PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefHybridStreamLost, fault, PdfDiagnosticCodes.XRefRebuilt, PdfDiagnosticCodes.ObjectRedefined]);
        document.Diagnostics.Should().ContainSingle(diagnostic => diagnostic.Code == fault).Which.Message.Should().Be(message);
        document.Diagnostics.Should().ContainSingle(diagnostic => diagnostic.Code == PdfDiagnosticCodes.XRefHybridStreamLost).Which.Message.Should().StartWith(
            Invariant($"The /XRefStm of the cross-reference table at offset {newest} names offset {stream}, the section its /Prev names too, which the chain read through that /Prev:"));
    }

    [Theory]
    [InlineData("nothing", false)]
    [InlineData("the table's /Prev", false)]
    [InlineData("nothing", true)]
    [InlineData("the table's /Prev", true)]
    public void A_prev_falling_short_of_the_stream_its_xrefstm_read_takes_it_before_the_table_s_own_stream_and_reads_the_chain_whole(
        string ownPrev, bool shortOfOwn)
    {
        // The newest revision writes a cross-reference stream of its own, stream 11, which nothing names, within reach of the
        // search near the offset the table's /Prev names: 7 bytes short of stream 6, which the table's /XRefStm read in the
        // stream's place, or 7 bytes short of stream 11 itself, nearer than stream 6. Stream 11 gives no /Prev, or the
        // table's: it may be the table's own, which hands the chain nothing on, or back to that offset, and is not the section
        // the /Prev means. The search passed over stream 6 for every other place and took stream 11 instead: the chain ended
        // there, or looped, the oldest table went unread, the index was rebuilt as the document opened, the object stream 6
        // frees was served as the oldest table writes it, and the validator said the file held no catalog, or that the chain
        // loops. A stream that may be the table's own is told by its own /Prev and tried once every other place was, and
        // stream 6, whose /Prev names the oldest table, is taken where it comes nearest: the chain is read whole, and the
        // /XRefStm reported as naming what the /Prev read (#156). Object 10, which only stream 11 indexes, is found by
        // rebuilding the index, as what only the table's own stream indexes is. Stream 11, where the search came to it first
        // and passed over it, is never read: it is reported once the chain is read, as a stream that may hold the table's own
        // rows. Where stream 6 is the nearer, the search takes it before it comes to stream 11, and passes over nothing.
        var (file, oldest, stream, own, newest) = HybridFiles.PrevFallingShortOfTheStreamBeforeTheTablesOwn(ownPrev, shortOfOwn);
        var named = (shortOfOwn ? own : stream) - 7;
        using var document = PdfDocument.Open(file);

        var sections = document.Reader.Structure.Sections;
        sections.Select(section => (section.NamedBy, section.State, section.Kind, section.Offset)).Should().Equal(
            ("startxref", XRefSectionState.Read, XRefSectionKind.Table, newest),
            ("/XRefStm", XRefSectionState.NamedAgain, XRefSectionKind.Unknown, stream),
            ("/Prev", XRefSectionState.Relocated, XRefSectionKind.Stream, stream),
            ("/Prev", XRefSectionState.Read, XRefSectionKind.Table, oldest));
        sections[1].ReadAs.Should().BeSameAs(sections[2]);
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(
            shortOfOwn
                ? [PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefHybridStreamLost, PdfDiagnosticCodes.XRefStreamPassedOver]
                : [PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefHybridStreamLost]);
        document.Diagnostics[0].Message.Should().Be(Invariant(
            $"The cross-reference section /Prev names at offset {named} was found {(shortOfOwn ? Invariant($"{named - stream} bytes before") : "7 bytes after")} it."));

        if (shortOfOwn)
        {
            document.Diagnostics[2].Should().Match<PdfDiagnostic>(diagnostic => diagnostic.Severity == PdfDiagnosticSeverity.Warning && diagnostic.Position == own);
            document.Diagnostics[2].Message.Should().Be(Invariant(
                $"The cross-reference stream at offset {own}, near offset {named}, which the /Prev of the cross-reference table at offset {newest} names, gives no /Prev, or one that names that offset too, as that table's own stream may: the search near that offset took another section first, and the chain did not read the stream. If it is that table's own stream, the index the chain read lacks the objects only it indexes, which only a rebuild of the index for another cause finds."));
        }

        document.WasRepaired.Should().BeFalse();
        document.GetObject(new PdfObjectId(8)).Should().BeSameAs(PdfNull.Instance, "stream 6 frees object 8, which only the oldest table places");
        document.WasRepaired.Should().BeFalse();
        Text(document, 10).Should().Be("ten");
        document.WasRepaired.Should().BeTrue();

        using var validated = PdfDocument.Open(file);
        var findings = new PdfValidator().Validate(validated).Findings;
        findings.Should().NotContain(finding => finding.RuleId == PdfValidationRuleIds.FileRootInvalid || finding.RuleId == PdfValidationRuleIds.XRefChainLoop);
        findings.Should().ContainSingle(finding => finding.RuleId == PdfValidationRuleIds.XRefHybridStreamWrong);
    }

    [Theory]
    [InlineData("the table's /Prev", false)]
    [InlineData("the table's /Prev", true)]
    [InlineData("nothing", false)]
    public void A_prev_falling_short_of_the_stream_its_xrefstm_read_takes_a_stream_that_may_be_the_table_s_own_once_no_other_place_holds_a_section(
        string streamPrev, bool shortOfOwn)
    {
        // As above, stream 11 giving no /Prev, but stream 6 gives the offset the table's /Prev names, or nothing: no stream
        // near that offset names another, and no other place near holds a section. Those that may be the table's own are tried
        // once every other place was, nearest first, but for the stream the /XRefStm read where it gives no /Prev, which hands
        // the chain nothing on and is passed over: stream 6, the nearer, its /Prev looping back to that offset, stream 11,
        // passed over and never read, reported once the chain is read; stream 11, the nearer, or the only one tried, the chain
        // ending there. The oldest table, which alone indexes the catalog, is not read, and the index is rebuilt as the
        // document opens (#156).
        var (file, _, stream, own, newest) = HybridFiles.PrevFallingShortOfTheStreamBeforeTheTablesOwn("nothing", shortOfOwn, streamPrev);
        var named = (shortOfOwn ? own : stream) - 7;
        var taken = streamPrev == "nothing" || shortOfOwn ? own : stream;
        using var document = PdfDocument.Open(file);

        var sections = document.Reader.Structure.Sections;
        sections[0].Should().Match<XRefSectionRecord>(section => section.NamedBy == "startxref" && section.Offset == newest);
        sections.Should().ContainSingle(section => section.NamedBy == "/Prev" && section.State == XRefSectionState.Relocated).Which.Should().Match<XRefSectionRecord>(
            section => section.Kind == XRefSectionKind.Stream && section.Offset == taken && section.NamedOffset == named);
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(
            taken == stream
                ?
                [
                    PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefHybridStreamLost, PdfDiagnosticCodes.XRefChainCycle,
                    PdfDiagnosticCodes.XRefStreamPassedOver, PdfDiagnosticCodes.XRefRebuilt, PdfDiagnosticCodes.ObjectRedefined,
                ]
                : [PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefRebuilt, PdfDiagnosticCodes.ObjectRedefined]);
        document.Diagnostics[0].Message.Should().Be(Invariant(
            $"The cross-reference section /Prev names at offset {named} was found {CountText.Distance(taken - named, "it")}."));

        if (taken == stream)
        {
            // Rebuilt as the document opens, the index finds what stream 11 alone indexes: the report says what the index the
            // chain read lacks, which only a rebuild for another cause finds, as this one does.
            document.Diagnostics[3].Should().Match<PdfDiagnostic>(diagnostic => diagnostic.Position == own);
            document.Diagnostics[3].Message.Should().Be(Invariant(
                $"The cross-reference stream at offset {own}, near offset {named}, which the /Prev of the cross-reference table at offset {newest} names, gives no /Prev, or one that names that offset too, as that table's own stream may: the search near that offset took another section first, and the chain did not read the stream. If it is that table's own stream, the index the chain read lacks the objects only it indexes, which only a rebuild of the index for another cause finds."));
            Text(document, 10).Should().Be("ten");
        }

        document.WasRepaired.Should().BeTrue();
    }

    [Fact]
    public void A_stream_nothing_names_whose_own_prev_names_another_offset_is_taken_where_it_comes_nearest()
    {
        // As above, but stream 11, which nothing names, names the oldest table through its own /Prev, and the table's /Prev
        // falls 7 bytes short of it, nearer than stream 6: it is no stream that may be the table's own, and the search takes it
        // where it comes nearest, before stream 6, which the /XRefStm read in the stream's place. The chain goes on through
        // its /Prev to the oldest table, and is read whole, what both streams index found in the index (#156).
        var (file, oldest, stream, own, newest) = HybridFiles.PrevFallingShortOfTheStreamBeforeTheTablesOwn("the oldest table", shortOfOwn: true);
        using var document = PdfDocument.Open(file);

        document.Reader.Structure.Sections.Select(section => (section.NamedBy, section.State, section.Kind, section.Offset)).Should().Equal(
            ("startxref", XRefSectionState.Read, XRefSectionKind.Table, newest),
            ("/XRefStm", XRefSectionState.Read, XRefSectionKind.Stream, stream),
            ("/Prev", XRefSectionState.Relocated, XRefSectionKind.Stream, own),
            ("/Prev", XRefSectionState.Read, XRefSectionKind.Table, oldest));
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(PdfDiagnosticCodes.XRefOffsetAdjusted);
        document.Diagnostics[0].Message.Should().Be(Invariant($"The cross-reference section /Prev names at offset {own - 7} was found 7 bytes after it."));
        document.GetObject(new PdfObjectId(8)).Should().BeSameAs(PdfNull.Instance);
        Text(document, 10).Should().Be("ten");
        document.WasRepaired.Should().BeFalse();
    }

    [Theory]
    [InlineData("the table's /Prev")]
    [InlineData("the table's /Prev, as a real")]
    [InlineData("the table's /Prev, through a reference")]
    [InlineData("the table's /Prev, through two references")]
    [InlineData("the table's /Prev, through a chain of 32 objects")]
    [InlineData("nothing")]
    [InlineData("the table before")]
    public void A_prev_falling_inside_the_stream_its_xrefstm_read_takes_it_only_where_the_stream_s_own_prev_names_another_offset(string ownPrev)
    {
        // The newest table names through /XRefStm the stream of its own revision, read in the stream's place, and through /Prev
        // a place 12 bytes into that stream's object; the table before it lies within reach of the search near that place.
        // The stream may be the table's own where its own /Prev names the place the table's names, or nothing: it hands the
        // chain back to that place, or nothing on, and the search tries it once every other place was, or never, taking the
        // table before, which reads the chain whole. Its /Prev is read as the chain reads it: written as a real with no
        // fractional part, or as a reference to an object the newest table places — that object holding the place, or a
        // reference to another that does, which the chain reads through, as far as the 32 objects a section's offset is
        // read through —, it names that place all the same, where as written it read as another offset and the stream was
        // taken, the chain looping back to that place. One whose /Prev names another offset is a section the /Prev can mean,
        // taken where it comes nearest, before the table farther off: read whole, the /XRefStm reported as naming what the
        // /Prev read, the chain goes on through the stream's own /Prev, here the table before (#156).
        var (file, oldest, previous, stream, newest) = HybridFiles.PrevFallingInsideTheTablesOwnStream(ownPrev);
        var taken = ownPrev == "the table before";
        using var document = PdfDocument.Open(file);

        var sections = document.Reader.Structure.Sections;
        sections.Select(section => (section.NamedBy, section.State, section.Kind, section.Offset)).Should().Equal(
            taken
                ?
                [
                    ("startxref", XRefSectionState.Read, XRefSectionKind.Table, newest),
                    ("/XRefStm", XRefSectionState.NamedAgain, XRefSectionKind.Unknown, stream),
                    ("/Prev", XRefSectionState.Relocated, XRefSectionKind.Stream, stream),
                    ("/Prev", XRefSectionState.Read, XRefSectionKind.Table, previous),
                    ("/Prev", XRefSectionState.Read, XRefSectionKind.Table, oldest),
                ]
                :
                [
                    ("startxref", XRefSectionState.Read, XRefSectionKind.Table, newest),
                    ("/XRefStm", XRefSectionState.Read, XRefSectionKind.Stream, stream),
                    ("/Prev", XRefSectionState.Relocated, XRefSectionKind.Table, previous),
                    ("/Prev", XRefSectionState.Read, XRefSectionKind.Table, oldest),
                ]);
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(
            taken ? [PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefHybridStreamLost] : [PdfDiagnosticCodes.XRefOffsetAdjusted]);
        document.Diagnostics[0].Message.Should().Be(Invariant(
            $"The cross-reference section /Prev names at offset {stream + 12} was found {(taken ? 12 : stream + 12 - previous)} bytes before it."));
        document.WasRepaired.Should().BeFalse();
        Text(document, 3).Should().Be("three as the middle revision has it");
        Text(document, 4).Should().Be("four");
        document.WasRepaired.Should().BeFalse();
        var findings = new PdfValidator().Validate(document).Findings;
        findings.Should().NotContain(finding => finding.RuleId == PdfValidationRuleIds.XRefChainLoop);
        findings.Count(finding => finding.RuleId == PdfValidationRuleIds.XRefHybridStreamWrong).Should().Be(taken ? 1 : 0);
    }

    [Theory]
    [InlineData("outside the file")]
    [InlineData("a negative offset")]
    [InlineData("a name")]
    [InlineData("the table's /Prev, through a chain of 33 objects")]
    public void A_prev_falling_inside_the_stream_its_xrefstm_read_takes_it_where_the_stream_s_own_prev_names_no_offset_in_the_file(string ownPrev)
    {
        // As above, the stream giving through its own /Prev an offset past the file's end, -1, a name, or a reference through
        // a chain of 33 objects, one more than a section's offset is read through, the last object's place dropped: no offset
        // in the file, which counts as another offset than the table's, the stream no stream that may be the table's own — -1
        // no more than any, though it is the number the reader gives a /Prev that is not there. It is a section
        // the /Prev can mean, taken where it comes nearest, before the table farther off, and read whole, the /XRefStm
        // reported as naming what the /Prev read; its own /Prev is reported as the chain finds it, the older sections looked
        // for by rebuilding the index, here as the document opens, the catalog being the oldest table's (#156).
        var (file, _, _, stream, newest) = HybridFiles.PrevFallingInsideTheTablesOwnStream(ownPrev);
        using var document = PdfDocument.Open(file);

        var sections = document.Reader.Structure.Sections;
        sections.Take(3).Select(section => (section.NamedBy, section.State, section.Kind, section.Offset)).Should().Equal(
            ("startxref", XRefSectionState.Read, XRefSectionKind.Table, newest),
            ("/XRefStm", XRefSectionState.NamedAgain, XRefSectionKind.Unknown, stream),
            ("/Prev", XRefSectionState.Relocated, XRefSectionKind.Stream, stream));
        sections[3].Should().Match<XRefSectionRecord>(section => section.NamedBy == "/Prev" && section.State == XRefSectionState.NotFound);
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(
            ownPrev != "outside the file"
                ?
                [
                    PdfDiagnosticCodes.XRefSectionMissing, PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefHybridStreamLost,
                    PdfDiagnosticCodes.XRefRebuilt, PdfDiagnosticCodes.ObjectRedefined,
                ]
                :
                [
                    PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefHybridStreamLost, PdfDiagnosticCodes.XRefSectionMissing,
                    PdfDiagnosticCodes.XRefRebuilt, PdfDiagnosticCodes.ObjectRedefined,
                ]);
        document.Diagnostics.Should().ContainSingle(diagnostic => diagnostic.Code == PdfDiagnosticCodes.XRefOffsetAdjusted).Which.Message.Should().Be(Invariant(
            $"The cross-reference section /Prev names at offset {stream + 12} was found 12 bytes before it."));
        document.WasRepaired.Should().BeTrue();

        using var validated = PdfDocument.Open(file);
        new PdfValidator().Validate(validated).Findings.Where(finding => finding.Severity == PdfValidationSeverity.Error).Select(finding => finding.RuleId)
            .Should().Equal(PdfValidationRuleIds.XRefSectionNotFound, PdfValidationRuleIds.XRefHybridStreamWrong);
    }

    [Theory]
    [InlineData(true, "nothing", false)]
    [InlineData(false, "nothing", false)]
    [InlineData(true, "nothing", true)]
    [InlineData(false, "nothing", true)]
    [InlineData(true, "the table's /Prev", true)]
    [InlineData(false, "the table's /Prev", true)]
    [InlineData(true, "the oldest table", true)]
    [InlineData(false, "the oldest table", true)]
    public void The_stream_a_hybrid_table_s_xrefstm_read_is_told_near_its_prev_alike_whether_named_at_its_header_or_inside_its_number(
        bool insideItsNumber, string streamPrev, bool catalogInStream)
    {
        // The update's table names its own stream through /XRefStm, at its header or 1 byte into its object number, from
        // which the stream reads as object 3, and through /Prev a place 7 bytes before the stream's header; the oldest table,
        // which alone indexes object 5, and the catalog too where the stream does not, lies within reach. The stream the
        // /XRefStm read is told by its data, not by the byte its reading started at: the header 1 byte before that byte,
        // whose number runs on into it, is that stream's, and is told as that stream is — passed over where it gives no
        // /Prev, put back where its /Prev names the table's, the search taking the oldest table, which reads the chain whole;
        // taken where its /Prev names the oldest table, read whole, the /XRefStm reported as naming what the /Prev read and
        // the chain going on through the stream. It is no stream before the one the /XRefStm read, taken where it comes
        // nearest, which ended the chain there, the oldest table unread, or looped back to the place the /Prev names (#156).
        var (file, oldest, stream, newest) = HybridFiles.NamingTheUpdatesStreamAtOrInsideItsNumber(insideItsNumber ? 1 : 0, streamPrev, catalogInStream);
        var named = insideItsNumber ? stream + 1 : stream;
        var taken = streamPrev == "the oldest table";
        using var document = PdfDocument.Open(file);

        document.Reader.Structure.Sections.Select(section => (section.NamedBy, section.State, section.Kind, section.Offset)).Should().Equal(
            taken
                ?
                [
                    ("startxref", XRefSectionState.Read, XRefSectionKind.Table, newest),
                    ("/XRefStm", XRefSectionState.NamedAgain, XRefSectionKind.Unknown, named),
                    ("/Prev", XRefSectionState.Relocated, XRefSectionKind.Stream, named),
                    ("/Prev", XRefSectionState.Read, XRefSectionKind.Table, oldest),
                ]
                :
                [
                    ("startxref", XRefSectionState.Read, XRefSectionKind.Table, newest),
                    ("/XRefStm", XRefSectionState.Read, XRefSectionKind.Stream, named),
                    ("/Prev", XRefSectionState.Relocated, XRefSectionKind.Table, oldest),
                ]);
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(
            taken ? [PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefHybridStreamLost] : [PdfDiagnosticCodes.XRefOffsetAdjusted]);
        document.Diagnostics[0].Message.Should().Be(
            taken
                ? Invariant($"The cross-reference section /Prev names at offset {stream - 7} was found {named - stream + 7} bytes after it.")
                : Invariant($"The cross-reference section /Prev names at offset {stream - 7} was found {stream - 7 - oldest} bytes before it."));
        Text(document, 4).Should().Be("four");
        Text(document, 5).Should().Be("old five");
        document.WasRepaired.Should().BeFalse();

        new PdfValidator().Validate(document).Findings.Where(finding => finding.Severity == PdfValidationSeverity.Error).Select(finding => finding.RuleId)
            .Should().Equal(taken ? [PdfValidationRuleIds.XRefHybridStreamWrong] : []);
    }

    [Theory]
    [InlineData("+13 0 obj", 0, "the oldest table")]
    [InlineData("+13 0 obj", 1, "the oldest table")]
    [InlineData("+13 0 obj", 0, "nothing")]
    [InlineData("+13 0 %c\nobj", 1, "nothing")]
    [InlineData("+13 0 %c\nobj", 2, "nothing")]
    [InlineData("+13 0 %c\nobj", 1, "the oldest table")]
    [InlineData("+13 0 %c\nobj", 0, "nothing")]
    [InlineData("13 0 %c\nobj", 1, "nothing")]
    [InlineData("++13 0 %c\nobj", 1, "nothing")]
    [InlineData("++13 0 %c\nobj", 2, "nothing")]
    [InlineData("++13 0 %c\nobj", 3, "nothing")]
    [InlineData("++13 0 %c\nobj", 1, "the oldest table")]
    [InlineData("++13 0 %c\nobj", 3, "the oldest table")]
    [InlineData("++13 0 %c\nobj", 1, "the table's /Prev")]
    [InlineData("++13 0 %c\nobj", 2, "the table's /Prev")]
    [InlineData("+-13 0 %c\nobj", 2, "nothing")]
    [InlineData("+-13 0 %c\nobj", 3, "nothing")]
    [InlineData("++13 0 obj", 0, "the oldest table")]
    [InlineData("+-13 0 obj", 0, "the oldest table")]
    [InlineData("+++13 0 obj", 0, "the oldest table")]
    [InlineData("+++13 0 obj", 1, "the oldest table")]
    [InlineData("195 0 obj", 1, "nothing")]
    [InlineData("195 0 obj", 2, "nothing")]
    [InlineData("105 0 obj", 1, "nothing")]
    [InlineData("105 0 obj", 2, "nothing")]
    public void The_stream_a_hybrid_table_s_xrefstm_read_is_told_near_its_prev_alike_whether_its_number_is_written_with_a_sign_or_not(
        string header, int into, string streamPrev)
    {
        // As above, the update's table names its own stream through /XRefStm and a place 7 bytes before it through /Prev, the
        // oldest table within reach; the stream's object number is written with a sign, a run of signs or none — a number of
        // three digits, 0 or 9 among those past its first —, and a comment parts its header's tokens where the row says so.
        // A load reads a run of signs as it reads one, the first deciding. The walk back from obj takes the number's digits
        // and stops at the sign, so that the header the search finds starts at the digit past the signs, past where a reading
        // from a + starts; a header whose tokens a comment parts is lexed from its line's start, and starts at its first sign.
        // Either is the stream the /XRefStm read, told by its data as where no sign is written: where the /XRefStm named a
        // sign, the header found past it was tried as another stream, and taken where its /Prev names the oldest table, the
        // stream read twice and the naming never reported; where it named a digit, or a sign past the first, the header
        // found at the first sign before it was taken for a stream before the one the /XRefStm read, the chain ending there
        // where it gives no /Prev, the index rebuilt as the document opened and the validator saying the file holds no
        // catalog, or looping back to the place the /Prev names where the stream's /Prev names it too. A run of signs was
        // told only where it was one sign long (#156).
        var number = int.Parse(header.TrimStart('+', '-').Split(' ')[0], CultureInfo.InvariantCulture);
        var (file, oldest, stream, newest) = HybridFiles.NamingTheUpdatesStreamAtOrInsideItsNumber(
            into, streamPrev, catalogInStream: false, header, number);
        var named = stream + into;
        var taken = streamPrev == "the oldest table";
        using var document = PdfDocument.Open(file);

        document.Reader.Structure.Sections.Select(section => (section.NamedBy, section.State, section.Kind, section.Offset)).Should().Equal(
            taken
                ?
                [
                    ("startxref", XRefSectionState.Read, XRefSectionKind.Table, newest),
                    ("/XRefStm", XRefSectionState.NamedAgain, XRefSectionKind.Unknown, named),
                    ("/Prev", XRefSectionState.Relocated, XRefSectionKind.Stream, named),
                    ("/Prev", XRefSectionState.Read, XRefSectionKind.Table, oldest),
                ]
                :
                [
                    ("startxref", XRefSectionState.Read, XRefSectionKind.Table, newest),
                    ("/XRefStm", XRefSectionState.Read, XRefSectionKind.Stream, named),
                    ("/Prev", XRefSectionState.Relocated, XRefSectionKind.Table, oldest),
                ]);
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(
            taken ? [PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefHybridStreamLost] : [PdfDiagnosticCodes.XRefOffsetAdjusted]);
        document.Diagnostics[0].Message.Should().Be(
            taken
                ? Invariant($"The cross-reference section /Prev names at offset {stream - 7} was found {named - stream + 7} bytes after it.")
                : Invariant($"The cross-reference section /Prev names at offset {stream - 7} was found {stream - 7 - oldest} bytes before it."));
        Text(document, 4).Should().Be("four");
        Text(document, 5).Should().Be("old five");
        document.WasRepaired.Should().BeFalse();

        new PdfValidator().Validate(document).Findings.Where(finding => finding.Severity == PdfValidationSeverity.Error).Select(finding => finding.RuleId)
            .Should().Equal(taken ? [PdfValidationRuleIds.XRefHybridStreamWrong] : []);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_stream_a_table_s_xrefstm_names_again_is_told_near_its_prev_alike_whether_named_at_its_header_or_inside_its_number(bool insideItsNumber)
    {
        // The newest table's /XRefStm reads the middle revision's stream, at its header or 1 byte into its object number, and
        // the middle table's /XRefStm names it again; the middle table's /Prev falls 7 bytes short of the stream's header,
        // and the oldest table, which alone indexes object 5, lies within reach. The stream named again is not taken, a
        // section the chain reached (#330), and is told by its data: the header 1 byte before where its reading started is
        // that stream's, passed over with it, and the search takes the oldest table, which reads the chain whole. It is no
        // stream before the one named again, taken where it comes nearest, which ended the chain there, object 5 lost (#156).
        var (file, oldest, stream, middle, newest) = HybridFiles.NamingTheUpdatesStreamAgainAtOrInsideItsNumber(insideItsNumber);
        var named = insideItsNumber ? stream + 1 : stream;
        using var document = PdfDocument.Open(file);

        document.Reader.Structure.Sections.Select(section => (section.NamedBy, section.State, section.Kind, section.Offset)).Should().Equal(
            ("startxref", XRefSectionState.Read, XRefSectionKind.Table, newest),
            ("/XRefStm", XRefSectionState.Read, XRefSectionKind.Stream, named),
            ("/Prev", XRefSectionState.Read, XRefSectionKind.Table, middle),
            ("/XRefStm", XRefSectionState.NamedAgain, XRefSectionKind.Unknown, named),
            ("/Prev", XRefSectionState.Relocated, XRefSectionKind.Table, oldest));
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(PdfDiagnosticCodes.XRefOffsetAdjusted);
        document.Diagnostics[0].Message.Should().Be(Invariant($"The cross-reference section /Prev names at offset {stream - 7} was found {stream - 7 - oldest} bytes before it."));
        Text(document, 5).Should().Be("old five");
        document.WasRepaired.Should().BeFalse();
    }

    [Theory]
    [InlineData("stream 11")]
    [InlineData("an outline item")]
    public void A_place_the_search_near_a_hybrid_table_s_prev_passes_over_or_finds_no_section_at_reports_nothing_its_reading_met(string near)
    {
        // Near the offset the table's /Prev names, the search comes first to stream 11, which may be the table's own and is
        // passed over, or to an outline item, an object of another type whose dictionary gives a /Prev, which holds no
        // section: a fault of the syntax in its dictionary. The search takes stream 6 beyond it, which the /XRefStm read in
        // the stream's place, and nothing of the place's reading is reported or recorded: the fault lies in nothing the
        // chain reads, as it did where stream 11 was taken instead (#156). Stream 11, which the chain then never reads, is
        // reported once the chain is read, as a stream passed over that may hold the table's own rows, its dictionary parsed
        // already.
        var (file, oldest, stream, at, newest) = HybridFiles.PrevFallingShortOfAPlaceBesideTheStream(near, "/Junk } ");
        using var document = PdfDocument.Open(file);

        document.Reader.Structure.Sections.Select(section => (section.NamedBy, section.State, section.Kind, section.Offset)).Should().Equal(
            ("startxref", XRefSectionState.Read, XRefSectionKind.Table, newest),
            ("/XRefStm", XRefSectionState.NamedAgain, XRefSectionKind.Unknown, stream),
            ("/Prev", XRefSectionState.Relocated, XRefSectionKind.Stream, stream),
            ("/Prev", XRefSectionState.Read, XRefSectionKind.Table, oldest));
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(
            near == "stream 11"
                ? [PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefHybridStreamLost, PdfDiagnosticCodes.XRefStreamPassedOver]
                : [PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefHybridStreamLost]);
        document.Diagnostics[0].Message.Should().Be(Invariant($"The cross-reference section /Prev names at offset {at - 7} was found {at - 7 - stream} bytes before it."));
    }

    [Theory]
    [InlineData(300, "nothing", false, 256, "")]
    [InlineData(70_000, "the oldest table", false, 0, "")]
    [InlineData(70_000, "the oldest table", true, 0, "")]
    [InlineData(300, "nothing", false, 256, "+")]
    [InlineData(300, "nothing", false, 256, "+-")]
    public void A_stream_near_a_hybrid_table_s_prev_whose_dictionary_a_guard_cut_keeps_its_place_and_is_the_guard_s_whatever_prev_the_cut_hid(
        int pad, string streamPrev, bool prevFirst, int maxTrailerLength, string sign)
    {
        // Stream 11, which nothing names, lies 7 bytes past the offset the table's /Prev names; its dictionary writes a /Pad
        // longer than MaxTrailerLength, of 256 bytes or of 64 KB by default, after its /Type /XRef and before its /Prev, if any
        // — or after its /Prev, which names the oldest table. Where no /Prev lay before the cut, told by the part of its
        // dictionary the guard left, it read as giving none, and was passed over as possibly the table's own for stream 6,
        // which the /XRefStm read: the guard's cut chose the section, unreported, and the file was blamed — the /XRefStm
        // reported as naming what the /Prev read, the validator's Error with it. A dictionary a guard cut is not told by a
        // /Prev the cut may hide: stream 11 keeps its place, nearest, the section found moved there though it cannot be read,
        // having declared itself one before the cut, and the guard is reported, the chain ending there for it, as where its
        // /Prev lies before the cut (#156, ADR 34). Its number written with a sign or a run of signs that starts with a +,
        // the stream is a token from its first sign, as a load reads it, and declares itself the same: the walk back from obj
        // finds it at its digit, where no token starts, and it was dropped, stream 6 taken in its place as above, the
        // guard's cut laid on the file (#327).
        var (file, _, stream, at, newest) = HybridFiles.PrevFallingShortOfAPlaceBesideTheStream(
            "stream 11", "/Pad (" + new string('.', pad) + ") ", streamPrev, prevFirst, sign);
        var limits = maxTrailerLength > 0 ? PdfReaderLimits.Default with { MaxTrailerLength = maxTrailerLength } : PdfReaderLimits.Default;
        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = limits });

        document.Reader.Structure.Sections.Select(section => (section.NamedBy, section.State, section.Kind, section.Offset)).Should().Equal(
            ("startxref", XRefSectionState.Read, XRefSectionKind.Table, newest),
            ("/XRefStm", XRefSectionState.Read, XRefSectionKind.Stream, stream),
            ("/Prev", XRefSectionState.Malformed, XRefSectionKind.Stream, at));
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(PdfDiagnosticCodes.LimitTrailer, PdfDiagnosticCodes.XRefOffsetAdjusted);
        document.Diagnostics[0].Position.Should().Be(at);
        document.Diagnostics[0].Message.Should().Be(Invariant(
            $"The dictionary of the object read as a cross-reference stream runs past {(maxTrailerLength > 0 ? "256 bytes" : "64 KB")}; only what lies within it was read. Raise PdfReaderLimits.MaxTrailerLength to read past it."));
        document.Diagnostics[1].Message.Should().Be(Invariant($"The cross-reference section /Prev names at offset {at - 7} was found 7 bytes after it."));
        document.GetObject(new PdfObjectId(10)).Should().BeSameAs(PdfNull.Instance);
        document.WasRepaired.Should().BeFalse();

        var findings = new PdfValidator().Validate(document).Findings;
        findings.Should().ContainSingle(finding => finding.RuleId == PdfValidationRuleIds.XRefCheckedInPart).Which.Severity.Should().Be(PdfValidationSeverity.Information);
        findings.Should().NotContain(finding => finding.Severity == PdfValidationSeverity.Error);
    }

    [Fact]
    public void A_stream_near_a_hybrid_table_s_prev_whose_dictionary_a_guard_cut_throws_under_throw_on_limit()
    {
        // Stream 11's dictionary runs past 64 KB before its /Prev, which names the oldest table. Under ThrowOnLimit the guard
        // throws as the document opens, where it was passed over and the chain read past it unreported (#156, ADR 34).
        var (file, _, _, _, _) = HybridFiles.PrevFallingShortOfAPlaceBesideTheStream("stream 11", "/Pad (" + new string('.', 70_000) + ") ", "the oldest table");

        var open = () => PdfDocument.Open(file, new PdfReaderOptions { ThrowOnLimit = true });

        open.Should().Throw<PdfLimitExceededException>().Which.Message.Should().Be(
            "The dictionary of the object read as a cross-reference stream runs past 64 KB; only what lies within it was read. Raise PdfReaderLimits.MaxTrailerLength to read past it.");
    }

    [Fact]
    public void A_stream_near_a_hybrid_table_s_prev_whose_long_dictionary_a_raised_limit_reads_whole_is_told_by_its_prev()
    {
        // As above, under a MaxTrailerLength of 1 MB: stream 11's dictionary is read whole, and its /Prev, which names the
        // oldest table, is another offset than the table's: it is the section the /Prev means, taken where it comes nearest,
        // and the chain goes on through it to the oldest table, read whole (#156).
        var (file, oldest, stream, at, newest) = HybridFiles.PrevFallingShortOfAPlaceBesideTheStream("stream 11", "/Pad (" + new string('.', 70_000) + ") ", "the oldest table");
        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxTrailerLength = 1024 * 1024 } });

        document.Reader.Structure.Sections.Select(section => (section.NamedBy, section.State, section.Kind, section.Offset)).Should().Equal(
            ("startxref", XRefSectionState.Read, XRefSectionKind.Table, newest),
            ("/XRefStm", XRefSectionState.Read, XRefSectionKind.Stream, stream),
            ("/Prev", XRefSectionState.Relocated, XRefSectionKind.Stream, at),
            ("/Prev", XRefSectionState.Read, XRefSectionKind.Table, oldest));
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(PdfDiagnosticCodes.XRefOffsetAdjusted);
        Text(document, 10).Should().Be("ten");
        document.WasRepaired.Should().BeFalse();
    }

    [Fact]
    public void A_search_that_finds_nothing_near_an_xrefstm_tries_each_place_once_and_leaves_the_other_attempts_to_the_prev()
    {
        // The newest table's /XRefStm names a place inside object 6's string, near which only objects 6 and 7, strings, lie;
        // its /Prev falls 3 bytes short of the oldest table. The search near the place the /XRefStm names tries each header
        // once, finds no section, and the stream is reported missing; a place it found nothing at is not tried again, and the
        // search near the place the /Prev names, which shares with it the attempts the reader allows a document, finds the
        // oldest table (#156).
        var (file, oldest, named, newest) = HybridFiles.NamingAStringAndFallingShortOfTheTableBefore();
        using var document = PdfDocument.Open(file);

        document.Reader.Structure.Sections.Select(section => (section.NamedBy, section.State, section.Offset)).Should().Equal(
            ("startxref", XRefSectionState.Read, newest),
            ("/XRefStm", XRefSectionState.NotFound, named),
            ("/Prev", XRefSectionState.Relocated, oldest));
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(PdfDiagnosticCodes.XRefSectionMissing, PdfDiagnosticCodes.XRefOffsetAdjusted);
        document.Diagnostics[1].Message.Should().Be(Invariant($"The cross-reference section /Prev names at offset {oldest - 3} was found 3 bytes after it."));
    }

    [Theory]
    [InlineData("nothing")]
    [InlineData("offset 0")]
    [InlineData("the first revision's stream")]
    public void A_prev_naming_offset_zero_takes_a_first_revision_s_stream_near_the_file_s_start_but_never_the_stream_its_xrefstm_read_where_it_gives_no_prev(
        string streamPrev)
    {
        // The newest table's /Prev names offset 0, the header, past which lies the catalog, no section; its /XRefStm read
        // stream 6, within reach of the search near offset 0. Where stream 6 gives no /Prev, it hands the chain nothing on and
        // is passed over, at offset 0 as at any other: the /Prev names a section neither there nor near it. Where it gives
        // offset 0, the table's, it may be the table's own, and is tried once every other place was: no other place holds a
        // section, and it is taken, its /Prev looping back to offset 0. Where the file's first revision wrote a cross-reference
        // stream right after the catalog, stream 11, which gives no /Prev, as a first revision's does, and which stream 6
        // names through its own /Prev, stream 11 lies before stream 6: under updates appended to the file it is no stream of
        // the newest table's revision, and is taken where it comes nearest, the chain read whole. It was passed over as
        // possibly the table's own: stream 6 was taken in its place, read whole, the /XRefStm reported as naming what the
        // /Prev read, and the validator gave an Error for it (#156).
        var (file, first, stream, newest) = HybridFiles.PrevOfZeroNearTheStream(streamPrev);
        using var document = PdfDocument.Open(file);

        var sections = document.Reader.Structure.Sections;

        if (streamPrev == "nothing")
        {
            sections.Select(section => (section.NamedBy, section.State, section.Offset)).Should().Equal(
                ("startxref", XRefSectionState.Read, newest),
                ("/XRefStm", XRefSectionState.Read, stream),
                ("/Prev", XRefSectionState.NotFound, 0L));
            document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(PdfDiagnosticCodes.XRefSectionMissing);
            document.Diagnostics[0].Message.Should().StartWith("The cross-reference section /Prev names at offset 0 is not there, nor near it;");
            return;
        }

        if (streamPrev == "offset 0")
        {
            sections.Select(section => (section.NamedBy, section.State, section.Kind, section.Offset)).Should().Equal(
                ("startxref", XRefSectionState.Read, XRefSectionKind.Table, newest),
                ("/XRefStm", XRefSectionState.NamedAgain, XRefSectionKind.Unknown, stream),
                ("/Prev", XRefSectionState.Relocated, XRefSectionKind.Stream, stream));
            document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(
                PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefHybridStreamLost, PdfDiagnosticCodes.XRefChainCycle);
            document.Diagnostics[0].Message.Should().Be(Invariant($"The cross-reference section /Prev names at offset 0 was found {stream} bytes after it."));
            return;
        }

        sections.Select(section => (section.NamedBy, section.State, section.Kind, section.Offset)).Should().Equal(
            ("startxref", XRefSectionState.Read, XRefSectionKind.Table, newest),
            ("/XRefStm", XRefSectionState.Read, XRefSectionKind.Stream, stream),
            ("/Prev", XRefSectionState.Relocated, XRefSectionKind.Stream, first));
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(PdfDiagnosticCodes.XRefOffsetAdjusted);
        document.Diagnostics[0].Message.Should().Be(Invariant($"The cross-reference section /Prev names at offset 0 was found {first} bytes after it."));
        Text(document, 10).Should().Be("ten");
        Text(document, 4).Should().Be("four");
        document.WasRepaired.Should().BeFalse();
        new PdfValidator().Validate(document).Findings.Should().NotContain(finding => finding.Severity == PdfValidationSeverity.Error);
    }

    [Theory]
    [InlineData("the first revision's stream")]
    [InlineData("a reference")]
    [InlineData("another offset")]
    [InlineData("the table's /Prev, as a real")]
    public void A_first_revision_s_stream_near_a_hybrid_table_s_prev_is_taken_where_it_comes_nearest_whatever_prev_the_table_s_own_stream_gives(
        string streamPrev)
    {
        // The first revision's cross-reference stream, stream 8, gives no /Prev, as a first revision's does (ISO 32000-1, Table
        // 17); the update's table names its own stream, stream 9, through /XRefStm, and through /Prev a place 12 bytes into
        // stream 8's object. Read as possibly the table's own for giving no /Prev, stream 8 was passed over for stream 9,
        // farther off, whose own /Prev — stream 8's offset, a reference to an object no section places, another offset, or
        // the table's /Prev written as a real — read as naming another offset than the table's: the /XRefStm was reported as
        // naming what the /Prev read, the validator gave an Error, and the chain went on through stream 9's /Prev, to stream
        // 8, nowhere, or back to the place the table's names. Under updates appended to the file, the table's own stream lies
        // past every older revision's section: stream 8, before stream 9, is no stream of the table's revision, and is taken
        // where it comes nearest, whatever stream 9's /Prev gives. The chain is read whole: the update's object 5, and its
        // object 7 freed (#156).
        var (file, first, stream, newest) = HybridFiles.PrevFallingShortOfTheFirstRevisionsStream(streamPrev);
        using var document = PdfDocument.Open(file);

        document.Reader.Structure.Sections.Select(section => (section.NamedBy, section.State, section.Kind, section.Offset)).Should().Equal(
            ("startxref", XRefSectionState.Read, XRefSectionKind.Table, newest),
            ("/XRefStm", XRefSectionState.Read, XRefSectionKind.Stream, stream),
            ("/Prev", XRefSectionState.Relocated, XRefSectionKind.Stream, first));
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(PdfDiagnosticCodes.XRefOffsetAdjusted);
        document.Diagnostics[0].Message.Should().Be(Invariant($"The cross-reference section /Prev names at offset {first + 12} was found 12 bytes before it."));
        Text(document, 5).Should().Be("new five");
        document.GetObject(new PdfObjectId(7)).Should().BeSameAs(PdfNull.Instance, "the update frees object 7");
        document.WasRepaired.Should().BeFalse();

        var findings = new PdfValidator().Validate(document).Findings;
        findings.Should().NotContain(finding => finding.Severity == PdfValidationSeverity.Error);
        findings.Should().ContainSingle(finding => finding.RuleId == PdfValidationRuleIds.XRefSectionShifted);
    }

    [Theory]
    [InlineData("a middle revision's stream")]
    [InlineData("an older revision's stream")]
    public void A_stream_before_the_one_a_hybrid_table_s_xrefstm_read_that_gives_no_prev_is_taken_where_it_comes_nearest_the_chain_ending_there(string near)
    {
        // The newest table names its own stream through /XRefStm, and through /Prev a place a few bytes from an older
        // revision's cross-reference stream that gives no /Prev, nearer than any other section: a middle revision's, which
        // gives none though the oldest table stands before it, within reach; or the middle revision's own, its table, which
        // names the oldest through /Prev, just past it. That stream lies before the stream the /XRefStm read, and is taken
        // where it comes nearest, as a first revision's would be: the chain ends there, the oldest table unread, and the index
        // is rebuilt as the document opens, the catalog being the oldest table's alone, the validator saying the file holds
        // none (#260). That is an accepted consequence of telling the table's own stream by where it lies: passed over as
        // possibly the table's own, the stream gave way to the oldest table, its revision's objects lost in silence, or to its
        // own table, which read the chain whole (#156).
        var (file, at, stream, newest) = near == "a middle revision's stream"
            ? Middle(HybridFiles.PrevFallingShortOfAMiddleRevisionsStream())
            : Older(HybridFiles.PrevFallingShortOfAnOlderRevisionsStream());
        using var document = PdfDocument.Open(file);

        document.Reader.Structure.Sections.Select(section => (section.NamedBy, section.State, section.Kind, section.Offset)).Should().Equal(
            ("startxref", XRefSectionState.Read, XRefSectionKind.Table, newest),
            ("/XRefStm", XRefSectionState.Read, XRefSectionKind.Stream, stream),
            ("/Prev", XRefSectionState.Relocated, XRefSectionKind.Stream, at));
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(
            near == "a middle revision's stream"
                ? [PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefRebuilt]
                : [PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefRebuilt, PdfDiagnosticCodes.ObjectRedefined]);
        document.Diagnostics[0].Position.Should().Be(at);
        document.WasRepaired.Should().BeTrue();
        Text(document, 4).Should().Be("four");

        using var validated = PdfDocument.Open(file);
        new PdfValidator().Validate(validated).Findings.Where(finding => finding.Severity == PdfValidationSeverity.Error).Select(finding => finding.RuleId)
            .Should().Equal(PdfValidationRuleIds.FileRootInvalid);

        static (byte[], long, long, long) Middle((byte[] File, long Oldest, long Middle, long Stream, long Newest) shape) =>
            (shape.File, shape.Middle, shape.Stream, shape.Newest);

        static (byte[], long, long, long) Older((byte[] File, long Oldest, long Older, long Previous, long Stream, long Newest) shape) =>
            (shape.File, shape.Older, shape.Stream, shape.Newest);
    }

    [Fact]
    public void A_stream_before_the_one_a_hybrid_table_s_xrefstm_read_whose_prev_names_an_older_section_is_taken_and_its_own_table_goes_unread_unreported()
    {
        // As above, the middle revision's stream naming the oldest table through /Prev, its table alone indexing object 7.
        // The stream lies before the one the /XRefStm read, and is taken where it comes nearest, whatever its /Prev: the
        // chain goes on through it to the oldest table, its own table left unread. What only that table indexes, object 7,
        // reads as null, nothing reported of it but the section found moved, the index neither left incomplete nor rebuilt,
        // as before the rule. A known gap of telling the table's own stream by where it lies, which #333 tracks (#156).
        var (file, oldest, older, _, stream, newest) = HybridFiles.PrevFallingShortOfAnOlderRevisionsStream(olderGivesPrev: true);
        using var document = PdfDocument.Open(file);

        document.Reader.Structure.Sections.Select(section => (section.NamedBy, section.State, section.Kind, section.Offset)).Should().Equal(
            ("startxref", XRefSectionState.Read, XRefSectionKind.Table, newest),
            ("/XRefStm", XRefSectionState.Read, XRefSectionKind.Stream, stream),
            ("/Prev", XRefSectionState.Relocated, XRefSectionKind.Stream, older),
            ("/Prev", XRefSectionState.Read, XRefSectionKind.Table, oldest));
        document.Diagnostics[0].Message.Should().Be(Invariant($"The cross-reference section /Prev names at offset {older + 12} was found 12 bytes before it."));
        Text(document, 4).Should().Be("four");
        Text(document, 5).Should().Be("new five");
        document.GetObject(new PdfObjectId(7)).Should().BeSameAs(PdfNull.Instance);
        document.WasRepaired.Should().BeFalse();
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(PdfDiagnosticCodes.XRefOffsetAdjusted);
    }

    [Fact]
    public void A_first_revision_s_stream_before_the_one_a_hybrid_table_s_xrefstm_read_that_indexes_the_catalog_ends_the_chain_with_nothing_rebuilt()
    {
        // The update's table names its own stream through /XRefStm, and through /Prev a place 12 bytes into the first
        // revision's stream, which gives no /Prev and indexes the catalog, its table, which names it through /XRefStm,
        // alone indexing object 5. The stream lies before the one the /XRefStm read, and is taken where it comes nearest:
        // the chain ends there, the first revision's table unread. The catalog is found, and nothing is rebuilt: object 5
        // reads as null, nothing reported of it but the section found moved, and the validator finds no error, only the
        // section shifted. A known gap of telling the table's own stream by where it lies, which #333 tracks; where the
        // catalog is the skipped table's alone, the index is rebuilt instead, as the theory above holds (#156, #260).
        var (file, first, _, stream, newest) = HybridFiles.PrevFallingShortOfAFirstHybridRevisionsStream();
        using var document = PdfDocument.Open(file);

        document.Reader.Structure.Sections.Select(section => (section.NamedBy, section.State, section.Kind, section.Offset)).Should().Equal(
            ("startxref", XRefSectionState.Read, XRefSectionKind.Table, newest),
            ("/XRefStm", XRefSectionState.Read, XRefSectionKind.Stream, stream),
            ("/Prev", XRefSectionState.Relocated, XRefSectionKind.Stream, first));
        Text(document, 4).Should().Be("four");
        Text(document, 6).Should().Be("six");
        document.GetObject(new PdfObjectId(5)).Should().BeSameAs(PdfNull.Instance);
        document.WasRepaired.Should().BeFalse();
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(PdfDiagnosticCodes.XRefOffsetAdjusted);
        document.Diagnostics[0].Position.Should().Be(first);

        var findings = new PdfValidator().Validate(document).Findings;
        findings.Should().NotContain(finding => finding.Severity == PdfValidationSeverity.Error);
        findings.Should().ContainSingle(finding => finding.RuleId == PdfValidationRuleIds.XRefSectionShifted);
    }

    [Theory]
    [InlineData(false, "nothing")]
    [InlineData(false, "the table's /Prev")]
    [InlineData(true, "nothing")]
    [InlineData(true, "the table's /Prev")]
    public void A_table_whose_xrefstm_names_an_older_stream_first_or_again_passes_over_its_own_stream_past_it_and_reports_it(bool again, string ownPrev)
    {
        // A writer copies /XRefStm into each later trailer: the third revision's table names stream 10, the second revision's,
        // and through /Prev a place 40 bytes before the end of the second revision's trailer; its own stream, stream 11, which
        // nothing names, gives no /Prev, or the table's, and lies past stream 10, nearer than the second revision's table.
        // Where that table is the newest, its /XRefStm reads stream 10, and the search near its /Prev passes over stream 11 as
        // possibly the table's own and takes the second revision's table, the chain read whole. Where a fourth revision copies
        // the /XRefStm again, the third revision's table names stream 10 again, which the fourth's read: the search tells the
        // streams near its /Prev by it as by a stream the /XRefStm read, without taking it, a section the chain reached
        // (#330). It took stream 11 instead, the chain ending, or looping, there, the index rebuilt as the document opened,
        // and the validator saying the file held no catalog, or that the chain loops. Stream 11, never read, is reported once
        // the chain is read, its objects missing from the index: object 5 reads as the oldest table writes it, and object 11,
        // the stream itself, as null (#156).
        var (file, oldest, stream, previous, own, hybrid, newest) = HybridFiles.CopyingTheXRefStmIntoLaterTrailers(again, ownPrev);
        using var document = PdfDocument.Open(file);

        var sections = document.Reader.Structure.Sections;
        sections.Select(section => (section.NamedBy, section.State, section.Kind, section.Offset)).Should().Equal(
            again
                ?
                [
                    ("startxref", XRefSectionState.Read, XRefSectionKind.Table, newest),
                    ("/XRefStm", XRefSectionState.Read, XRefSectionKind.Stream, stream),
                    ("/Prev", XRefSectionState.Read, XRefSectionKind.Table, hybrid),
                    ("/XRefStm", XRefSectionState.NamedAgain, XRefSectionKind.Unknown, stream),
                    ("/Prev", XRefSectionState.Relocated, XRefSectionKind.Table, previous),
                    ("/XRefStm", XRefSectionState.NamedAgain, XRefSectionKind.Unknown, stream),
                    ("/Prev", XRefSectionState.Read, XRefSectionKind.Table, oldest),
                ]
                :
                [
                    ("startxref", XRefSectionState.Read, XRefSectionKind.Table, newest),
                    ("/XRefStm", XRefSectionState.Read, XRefSectionKind.Stream, stream),
                    ("/Prev", XRefSectionState.Relocated, XRefSectionKind.Table, previous),
                    ("/XRefStm", XRefSectionState.NamedAgain, XRefSectionKind.Unknown, stream),
                    ("/Prev", XRefSectionState.Read, XRefSectionKind.Table, oldest),
                ]);
        var named = sections.Single(section => section.State == XRefSectionState.Relocated).NamedOffset;
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefStreamPassedOver);
        document.Diagnostics[0].Message.Should().Be(Invariant($"The cross-reference section /Prev names at offset {named} was found {named - previous} bytes before it."));
        document.Diagnostics[1].Position.Should().Be(own);
        document.Diagnostics[1].Message.Should().Be(Invariant(
            $"The cross-reference stream at offset {own}, near offset {named}, which the /Prev of the cross-reference table at offset {hybrid} names, gives no /Prev, or one that names that offset too, as that table's own stream may: the search near that offset took another section first, and the chain did not read the stream. If it is that table's own stream, the index the chain read lacks the objects only it indexes, which only a rebuild of the index for another cause finds."));
        Text(document, 5).Should().Be("old five");
        Text(document, 4).Should().Be("four");
        document.GetObject(new PdfObjectId(11)).Should().BeSameAs(PdfNull.Instance);
        document.WasRepaired.Should().BeFalse();

        using var validated = PdfDocument.Open(file);
        new PdfValidator().Validate(validated).Findings.Should().NotContain(finding => finding.Severity == PdfValidationSeverity.Error);
    }

    [Theory]
    [InlineData(14, 0)]
    [InlineData(16, 0)]
    [InlineData(0, 28)]
    public void A_stream_passed_over_as_possibly_the_table_s_own_is_tried_again_however_many_places_the_document_counted_before(int objects, int spent)
    {
        // The update's own stream, stream 11, which nothing names and which gives no /Prev, lies 5 bytes past the offset its
        // table's /Prev names; stream 6, which the /XRefStm read, gives no /Prev, and no other place near holds a section. The
        // search passes over stream 11 as possibly the table's own, and tries every other place first: objects of one null
        // each, 28 or 32 within its reach, and objects 5 and 10, with object 4 where the window still reaches it, beside 28 of
        // them; or objects 4, 5 and 10 alone, a newer table's search having tried 28 places already. Each place counts toward
        // the 32 the reader allows a document, and the retry of stream 11 counted again: the bound stopped the search first,
        // the section was reported missing, the index rebuilt as the document opened, and object 8, which stream 6 frees,
        // served as the oldest table writes it. A place passed over counts once, at its first reading, and its retry runs
        // once the bound stops new places, the places of the window left untried where it stops them first: stream 11 is
        // taken, which places the catalog, and object 8 reads as null (#156).
        var (file, stream, own, hybrid, newest) = HybridFiles.PrevFallingShortOfTheTablesOwnStreamAmongObjects(objects, spent);
        using var document = PdfDocument.Open(file);

        var sections = document.Reader.Structure.Sections;
        sections.Select(section => (section.NamedBy, section.State, section.Kind, section.Offset)).Should().Equal(
            spent > 0
                ?
                [
                    ("startxref", XRefSectionState.Read, XRefSectionKind.Table, newest),
                    ("/Prev", XRefSectionState.Relocated, XRefSectionKind.Table, hybrid),
                    ("/XRefStm", XRefSectionState.Read, XRefSectionKind.Stream, stream),
                    ("/Prev", XRefSectionState.Relocated, XRefSectionKind.Stream, own),
                ]
                :
                [
                    ("startxref", XRefSectionState.Read, XRefSectionKind.Table, newest),
                    ("/XRefStm", XRefSectionState.Read, XRefSectionKind.Stream, stream),
                    ("/Prev", XRefSectionState.Relocated, XRefSectionKind.Stream, own),
                ]);
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(
            spent > 0 ? [PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefOffsetAdjusted] : [PdfDiagnosticCodes.XRefOffsetAdjusted]);
        document.Diagnostics[^1].Message.Should().Be(Invariant($"The cross-reference section /Prev names at offset {own - 5} was found 5 bytes after it."));
        document.GetObject(new PdfObjectId(8)).Should().BeSameAs(PdfNull.Instance, "stream 6 frees object 8, which only the oldest table places");
        Text(document, 10).Should().Be("ten");
        document.WasRepaired.Should().BeFalse();
    }

    [Fact]
    public void A_table_whose_xrefstm_names_again_a_stream_a_newer_one_found_moved_passes_over_its_own_stream_as_well()
    {
        // As above, each /XRefStm copied, 7 bytes short of stream 10: the newest table's search near that offset finds stream
        // 10 moved, and the third revision's table names it again there. The search near that table's /Prev tells the streams
        // near it by stream 10 as well: stream 11, past it, is passed over for the second revision's table, the chain read
        // whole, and reported once the chain is read (#156).
        var (file, oldest, stream, previous, own, hybrid, newest) = HybridFiles.CopyingTheXRefStmIntoLaterTrailers(again: true, "nothing", shortBy: 7);
        using var document = PdfDocument.Open(file);

        var sections = document.Reader.Structure.Sections;
        sections.Where(section => section.NamedBy == "/Prev").Select(section => (section.State, section.Kind, section.Offset)).Should().Equal(
            (XRefSectionState.Read, XRefSectionKind.Table, hybrid),
            (XRefSectionState.Relocated, XRefSectionKind.Table, previous),
            (XRefSectionState.Read, XRefSectionKind.Table, oldest));
        sections[1].Should().Match<XRefSectionRecord>(section => section.NamedBy == "/XRefStm" && section.State == XRefSectionState.Relocated && section.Offset == stream);
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(
            PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefStreamPassedOver);
        document.Diagnostics[2].Position.Should().Be(own);
        Text(document, 5).Should().Be("old five");
        document.WasRepaired.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_table_whose_xrefstm_names_a_stream_that_cannot_be_read_tells_no_stream_near_its_prev_by_it(bool again)
    {
        // As above, but stream 10's rows cannot be read: the /XRefStm that names it first, the third revision's table's or the
        // fourth's, reads no stream in the stream's place, the stream lost, and the third revision's table names it first, or
        // again. A stream the chain could not read tells nothing of where the table's own stream lies: the search near the
        // third revision's /Prev tries stream 11, the nearest place, and takes it, the chain ending there (#156).
        var (file, _, _, _, own, hybrid, _) = HybridFiles.CopyingTheXRefStmIntoLaterTrailers(again, "nothing", streamLost: true);
        using var document = PdfDocument.Open(file);

        document.Reader.Structure.Sections[^1].Should().Match<XRefSectionRecord>(
            section => section.NamedBy == "/Prev" && section.State == XRefSectionState.Relocated && section.Offset == own && section.NamedFrom == hybrid);
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(
            PdfDiagnosticCodes.XRefSectionUnreadable, PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefRebuilt, PdfDiagnosticCodes.ObjectRedefined);
        document.Diagnostics[1].Position.Should().Be(own);
    }

    [Fact]
    public void A_table_that_names_no_stream_below_one_that_names_a_stream_again_takes_the_stream_nearest_its_prev()
    {
        // The fourth revision's table names stream 10 again, which the newest read, and names through /Prev the third's,
        // which names no stream, and whose /Prev falls 5 bytes short of stream 11, which gives no /Prev, past stream 10. Only
        // the /Prev of a table whose /XRefStm read a stream, or names one again, tells by it which streams near the offset it
        // names may be the table's own: the third's searches near its /Prev as any other, nearest first, and takes stream 11,
        // the chain ending there (#156).
        var (file, _, own, middle, _) = HybridFiles.NamingAStreamAgainAboveATableThatNamesNone();
        using var document = PdfDocument.Open(file);

        document.Reader.Structure.Sections[^1].Should().Match<XRefSectionRecord>(
            section => section.NamedBy == "/Prev" && section.State == XRefSectionState.Relocated && section.Offset == own && section.NamedFrom == middle);
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(PdfDiagnosticCodes.XRefOffsetAdjusted);
        Text(document, 5).Should().Be("five");
        document.WasRepaired.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(32)]
    public void A_search_near_an_xrefstm_the_bound_stops_asks_nothing_more_of_the_places_its_window_holds(int spent)
    {
        // The newest table names object 4 through /Prev, a font whose dictionary MaxTrailerLength cuts before its /Type, and
        // through /XRefStm a place 30 bytes before it, nearer object 8: the search near that place tries object 8, then comes
        // to object 4, where the /Prev keeps its place, the guard's, and stops, the naming the guard's too. Where a newer
        // table's search spent the 32 places the reader allows a document, the bound stops the search at object 8, and asks
        // nothing more of the places the window holds, the one the /Prev names among them: the stream is reported missing, as
        // at any search the bound stops (#156).
        var (built, _, _, _, newest) = HybridFiles.NamingAnObjectBesideTheStream(
            "<< /Pad (" + new string('-', 70_000) + ") /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
        var (file, _) = spent > 0 ? HybridFiles.UnderPlacesSpent(built, newest, spent) : (built, newest);
        using var document = PdfDocument.Open(file);

        document.Reader.Structure.Sections.Should().ContainSingle(section => section.NamedBy == "/XRefStm").Which.State
            .Should().Be(spent > 0 ? XRefSectionState.NotFound : XRefSectionState.NamedAgain);
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(
            spent > 0
                ?
                [
                    PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefSectionMissing, PdfDiagnosticCodes.LimitTrailer,
                    PdfDiagnosticCodes.XRefRebuilt, PdfDiagnosticCodes.ObjectRedefined,
                ]
                : [PdfDiagnosticCodes.LimitTrailer]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(32)]
    public void A_search_near_an_xrefstm_past_the_bound_still_asks_the_place_its_table_s_prev_names_where_that_place_comes_nearest(int spent)
    {
        // As above, the /XRefStm naming a place 28 bytes into object 8, where object 4, which the /Prev names, is nearer than
        // object 8: the search comes to object 4 first, and asks whether the /Prev keeps its place there before the bound is
        // asked, in the look made once for each hybrid table, as with places to spare. It does, the guard's, and the search
        // stops there, the naming the guard's, though a newer table's search spent the 32 places the reader allows a
        // document: no nearer place is left unread, and nothing is reported missing (#156).
        var (built, _, four, _, newest) = HybridFiles.NamingAnObjectBesideTheStream(
            "<< /Pad (" + new string('-', 70_000) + ") /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>", xrefStmInto: 28);
        var (file, _) = spent > 0 ? HybridFiles.UnderPlacesSpent(built, newest, spent) : (built, newest);
        using var document = PdfDocument.Open(file);

        document.Reader.Structure.Sections.Should().ContainSingle(section => section.NamedBy == "/XRefStm").Which.State
            .Should().Be(XRefSectionState.NamedAgain);
        document.Reader.Structure.Sections[^1].Should().Match<XRefSectionRecord>(
            section => section.NamedBy == "/Prev" && section.State == XRefSectionState.Malformed && section.Offset == four && section.CutByLimit);
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(
            spent > 0 ? [PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.LimitTrailer] : [PdfDiagnosticCodes.LimitTrailer]);
        document.WasRepaired.Should().BeFalse();
    }

    [Fact]
    public void A_stream_the_search_near_a_hybrid_table_s_prev_passes_over_and_an_older_section_names_is_read_there_and_not_reported_as_passed_over()
    {
        // As in the tests above, the table's /Prev falls 7 bytes short of stream 11, which nothing names and which gives no
        // /Prev: the search passes over it as possibly the table's own, and takes stream 6, which the /XRefStm read. The
        // oldest table, which stream 6 names through /Prev, names stream 11 through /XRefStm: the chain reads it there, in
        // the stream's place, and the stream passed over is a stream the chain read, which nothing reports as passed over
        // (#156).
        var (file, oldest, stream, own, newest) = HybridFiles.PrevFallingShortOfTheStreamBeforeTheTablesOwn("nothing", shortOfOwn: true, oldestNames: "/XRefStm");
        using var document = PdfDocument.Open(file);

        document.Reader.Structure.Sections.Select(section => (section.NamedBy, section.State, section.Kind, section.Offset)).Should().Equal(
            ("startxref", XRefSectionState.Read, XRefSectionKind.Table, newest),
            ("/XRefStm", XRefSectionState.NamedAgain, XRefSectionKind.Unknown, stream),
            ("/Prev", XRefSectionState.Relocated, XRefSectionKind.Stream, stream),
            ("/Prev", XRefSectionState.Read, XRefSectionKind.Table, oldest),
            ("/XRefStm", XRefSectionState.Read, XRefSectionKind.Stream, own));
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefHybridStreamLost);
        Text(document, 10).Should().Be("ten");
    }

    [Theory]
    [InlineData("")]
    [InlineData("+")]
    public void A_stream_the_search_near_a_hybrid_table_s_prev_comes_back_to_and_takes_is_not_reported_as_passed_over_though_it_cannot_be_read(
        string streamSign)
    {
        // As in the tests above, the table's /Prev falls 7 bytes short of stream 11, which nothing names and which gives no
        // /Prev, and stream 6, which the /XRefStm read, gives none either: the search passes over stream 11 as possibly the
        // table's own, and comes back to it once no other place holds a section. Its /W gives a field a negative width: it is
        // taken, where it declared itself a section, and the chain ends there, its rows lost, which the chain reports. A
        // stream the search came back to and took is no stream it passed over (#156). Stream 6's number is written with a
        // sign where the row says so, the /XRefStm reading it from that sign: only its own header, past the sign, is told as
        // that stream, and the places past it are tried as they are (#156, #188).
        var (file, _, _, own, _) = HybridFiles.PrevFallingShortOfTheStreamBeforeTheTablesOwn(
            "nothing", shortOfOwn: true, "nothing", ownWidths: "[1 -4 2]", streamSign: streamSign);
        using var document = PdfDocument.Open(file);

        document.Reader.Structure.Sections[^1].Should().Match<XRefSectionRecord>(
            section => section.NamedBy == "/Prev" && section.State == XRefSectionState.Malformed && section.Offset == own);
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(
            PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefSectionUnreadable, PdfDiagnosticCodes.XRefRebuilt, PdfDiagnosticCodes.ObjectRedefined);
        document.Diagnostics[1].Position.Should().Be(own);
    }

    [Theory]
    [InlineData("nothing", "/Prev", "/Type /XRef ", "[1 -4 2]")]
    [InlineData("nothing", "/Prev", "/Type /XRef /Filter /Foo ", "[1 4 2]")]
    [InlineData("nothing", "/Prev", "", "[1 -4 2]")]
    [InlineData("nothing", "/XRefStm", "", "[1 -4 2]")]
    [InlineData("nothing", "/Prev, 5 bytes short", "/Type /XRef ", "[1 -4 2]")]
    [InlineData("nothing", "/Prev, 1 byte short", "/Type /XRef ", "[1 -4 2]")]
    [InlineData("nothing", "/XRefStm", "/Type /XRef ", "[1 -4 2]")]
    [InlineData("the table's /Prev", "/Prev", "/Type /XRef ", "[1 -4 2]")]
    [InlineData("nothing", "/Prev", "/Type /XRef ", "[1 4 2]")]
    [InlineData("nothing", "/Prev, 5 bytes short", "/Type /XRef ", "[1 4 2]")]
    public void A_stream_passed_over_near_a_hybrid_table_s_prev_that_an_older_section_reaches_is_reported_as_the_chain_finds_it_not_as_passed_over(
        string ownPrev, string oldestNames, string ownEntries, string ownWidths)
    {
        // As in the tests above, the table's /Prev falls 7 bytes short of stream 11, which gives no /Prev, or the table's: the
        // search passes over it as possibly the table's own, and takes stream 6, which the /XRefStm read, and whose /Prev
        // names the oldest table. The oldest table names stream 11 through /Prev or /XRefStm, or through /Prev 5 bytes short
        // of it, where a second search finds it moved, or 1 byte short, the end of line before it, which the reading there
        // crosses to it. Where the stream cannot be read — its /W gives a field a negative width, or its data names a filter
        // the library does not know, typed or not —, the chain reported it as it found it, unreadable, and its objects
        // looked for by rebuilding the index; but where it gave no /Prev to go on through, or was no typed stream an /XRefStm
        // names, nothing recorded that the chain reached it, and it was reported a second time, as a stream the chain did
        // not read, its objects missing. A section the chain reached is reported as the chain finds it — read, read where it
        // was found moved, or unreadable there, its /Prev handed on or not — and never as a stream passed over (#156).
        var (file, _, _, own, _) = HybridFiles.PrevFallingShortOfTheStreamBeforeTheTablesOwn(
            ownPrev, shortOfOwn: true, "the oldest table", oldestNames, ownWidths, ownEntries);
        var readable = ownWidths == "[1 4 2]" && !ownEntries.Contains("/Foo", StringComparison.Ordinal);
        var moved = oldestNames == "/Prev, 5 bytes short";
        using var document = PdfDocument.Open(file);

        document.Reader.Structure.Sections[^1].Should().Match<XRefSectionRecord>(
            section => section.NamedBy == (oldestNames == "/XRefStm" ? "/XRefStm" : "/Prev") && section.Kind == XRefSectionKind.Stream &&
                section.Offset + section.Padding == own &&
                section.State == (!readable ? XRefSectionState.Malformed : moved ? XRefSectionState.Relocated : XRefSectionState.Read));
        List<string> codes = [PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefHybridStreamLost];

        if (moved)
        {
            codes.Add(PdfDiagnosticCodes.XRefOffsetAdjusted);
        }

        if (ownEntries.Contains("/Foo", StringComparison.Ordinal))
        {
            codes.Add(PdfDiagnosticCodes.FilterUnsupported);
        }

        if (!readable)
        {
            codes.Add(PdfDiagnosticCodes.XRefSectionUnreadable);
        }

        if (ownPrev == "the table's /Prev")
        {
            codes.Add(PdfDiagnosticCodes.XRefChainCycle);
        }

        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(codes);

        if (!readable)
        {
            document.Diagnostics.Single(diagnostic => diagnostic.Code == PdfDiagnosticCodes.XRefSectionUnreadable).Position
                .Should().Be(document.Reader.Structure.Sections[^1].Offset);
        }
    }

    [Theory]
    [InlineData("/XRefStm", 11, 1, "[1 4 2]", "")]
    [InlineData("/Prev", 11, 1, "[1 4 2]", "")]
    [InlineData("/XRefStm", 111, 2, "[1 4 2]", "")]
    [InlineData("/XRefStm", 111, 1, "[1 4 2]", "")]
    [InlineData("/XRefStm", 11, 1, "[1 -4 2]", "")]
    [InlineData("/Prev", 11, 1, "[1 -4 2]", "")]
    [InlineData("/XRefStm", 11, 0, "[1 4 2]", "")]
    [InlineData("/Prev", 11, 0, "[1 -4 2]", "")]
    [InlineData("/XRefStm", 111, 0, "[1 4 2]", "")]
    [InlineData("/XRefStm", 195, 1, "[1 4 2]", "")]
    [InlineData("/Prev", 195, 2, "[1 4 2]", "")]
    [InlineData("/XRefStm", 105, 1, "[1 4 2]", "")]
    [InlineData("/Prev", 105, 2, "[1 4 2]", "")]
    [InlineData("/XRefStm", 109, 2, "[1 4 2]", "")]
    [InlineData("/XRefStm", 11, 0, "[1 4 2]", "+11 0 obj")]
    [InlineData("/Prev", 11, 1, "[1 4 2]", "+11 0 obj")]
    [InlineData("/XRefStm", 11, 0, "[1 4 2]", "+11 0 %c\nobj")]
    [InlineData("/XRefStm", 11, 1, "[1 4 2]", "+11 0 %c\nobj")]
    [InlineData("/Prev", 11, 2, "[1 4 2]", "+11 0 %c\nobj")]
    [InlineData("/XRefStm", 11, 1, "[1 4 2]", "++11 0 %c\nobj")]
    [InlineData("/XRefStm", 11, 2, "[1 4 2]", "++11 0 %c\nobj")]
    [InlineData("/XRefStm", 11, 3, "[1 4 2]", "++11 0 %c\nobj")]
    [InlineData("/Prev", 11, 2, "[1 4 2]", "++11 0 %c\nobj")]
    [InlineData("/XRefStm", 11, 2, "[1 4 2]", "+-11 0 %c\nobj")]
    [InlineData("/Prev", 11, 3, "[1 4 2]", "+-11 0 %c\nobj")]
    [InlineData("/XRefStm", 11, 1, "[1 -4 2]", "++11 0 %c\nobj")]
    [InlineData("/Prev", 11, 2, "[1 -4 2]", "+-11 0 %c\nobj")]
    [InlineData("/XRefStm", 20, 1, "[1 4 2]", "", 0)]
    [InlineData("/Prev", 20, 1, "[1 4 2]", "", 0)]
    [InlineData("/Prev", 100, 2, "[1 4 2]", "", 0)]
    [InlineData("/Prev", 20, 1, "[1 -4 2]", "", 0)]
    [InlineData("/XRefStm", 11, 1, "[1 4 2]", "+-11 0 obj", 2)]
    [InlineData("/Prev", 11, 1, "[1 4 2]", "+-11 0 %c\nobj", 0)]
    public void A_stream_passed_over_near_a_hybrid_table_s_prev_that_an_older_section_names_at_its_header_or_inside_its_object_number_is_reported_as_the_chain_finds_it_not_as_passed_over(
        string oldestNames, int ownNumber, int into, string ownWidths, string ownHeader, int foundMovedAt = -1)
    {
        // As above, the search near the table's /Prev passes over stream 11, written as object 11, 111, 195, 105, 109, 20
        // or 100, which gives no /Prev, and takes stream 6, whose /Prev names the oldest table; its number is written with
        // a sign or a run of signs that starts with a +, and a comment parts its header's tokens, where the row says so.
        // The oldest table names stream 11 through /XRefStm or /Prev, at its header, or 1 to 3 bytes past its start, inside
        // its object number, at a sign of that run or a digit: read from there, it is the same stream, the same dictionary
        // and data, and the chain reads it — whole, or, where its /W gives a field a negative width, there and unreadable,
        // which the chain reports. Where what is read from there is no object's header — digits that read 0, as the 0 of 20
        // or the second 0 of 100, or the - of a run of signs —, nothing is read there, and the search near that place finds
        // the stream moved, at its first digit, or at its first sign where a comment parts its header's tokens, which the
        // chain reports, and reads it there as above. The stream passed over is told by its data, as the stream an /XRefStm
        // read is: named inside its number, it was reported a second time, as a stream the chain did not read, its objects
        // said to be missing from the index, though object 10, which only it indexes, was read from it — named at a second
        // sign or past it, it still was, the number taken to end at that sign (#156).
        var (file, _, _, own, _) = HybridFiles.PrevFallingShortOfTheStreamBeforeTheTablesOwn(
            "nothing", shortOfOwn: true, "the oldest table", oldestNames, ownWidths, into: into, ownNumber: ownNumber,
            ownHeader: ownHeader.Length > 0 ? ownHeader : null);
        var readable = ownWidths == "[1 4 2]";
        var moved = foundMovedAt >= 0;
        var at = own + (moved ? foundMovedAt : into);
        using var document = PdfDocument.Open(file);

        document.Reader.Structure.Sections[^1].Should().Match<XRefSectionRecord>(
            section => section.NamedBy == oldestNames && section.Kind == XRefSectionKind.Stream && section.Offset == at &&
                section.Padding == 0 &&
                section.State == (!readable ? XRefSectionState.Malformed : moved ? XRefSectionState.Relocated : XRefSectionState.Read));
        List<string> codes = [PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefHybridStreamLost];

        if (moved)
        {
            codes.Add(PdfDiagnosticCodes.XRefOffsetAdjusted);
        }

        if (!readable)
        {
            codes.Add(PdfDiagnosticCodes.XRefSectionUnreadable);
        }

        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(codes);

        if (readable)
        {
            Text(document, 10).Should().Be("ten");
            document.WasRepaired.Should().BeFalse();
        }

        if (!readable || moved)
        {
            document.Diagnostics[^1].Position.Should().Be(at);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    public void A_stream_passed_over_near_a_hybrid_table_s_prev_that_a_newer_table_s_xrefstm_read_at_its_header_or_inside_its_object_number_is_not_reported_as_passed_over(int into)
    {
        // As above, the search near the table's /Prev passes over stream 11, which gives no /Prev, and takes stream 6, whose
        // /Prev names the oldest table. A newer table names stream 11 through /XRefStm, at its header or 1 byte past its
        // start, inside its object number: the chain read it there, whole, before the search came to its header. Named
        // inside its number, it is the same stream, told by its data, and was reported as a stream the chain did not read,
        // its objects said to be missing from the index, though object 10, which only it indexes, was read from it (#156).
        var (file, oldest, stream, own, newest, newer) = HybridFiles.NamingTheTablesOwnStreamFromANewerTable(into);
        using var document = PdfDocument.Open(file);

        document.Reader.Structure.Sections.Select(section => (section.NamedBy, section.State, section.Kind, section.Offset)).Should().Equal(
            ("startxref", XRefSectionState.Read, XRefSectionKind.Table, newer),
            ("/XRefStm", XRefSectionState.Read, XRefSectionKind.Stream, own + into),
            ("/Prev", XRefSectionState.Read, XRefSectionKind.Table, newest),
            ("/XRefStm", XRefSectionState.NamedAgain, XRefSectionKind.Unknown, stream),
            ("/Prev", XRefSectionState.Relocated, XRefSectionKind.Stream, stream),
            ("/Prev", XRefSectionState.Read, XRefSectionKind.Table, oldest));
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefHybridStreamLost);
        Text(document, 10).Should().Be("ten");
        document.WasRepaired.Should().BeFalse();
    }

    [Theory]
    [InlineData(2, "[1 4 2]", 0)]
    [InlineData(3, "[1 4 2]", 0)]
    [InlineData(0, "[1 4 2]", 0)]
    [InlineData(0, "12 0 R", 1024)]
    [InlineData(0, "12 0 R", 0)]
    public void A_stream_passed_over_near_a_hybrid_table_s_prev_that_an_older_table_names_is_left_to_a_limit_that_ended_the_chain_or_cut_the_stream(
        int maxSections, string ownWidths, int maxObjectLength)
    {
        // As in the tests above, the search near the table's /Prev passes over stream 11, and takes stream 6, whose /Prev names
        // the oldest table; the oldest table names stream 11 through /XRefStm, and the chain reads it there. Where
        // MaxXRefSectionCount stops the chain at 2 sections, before the oldest table, or MaxObjectLength cuts object 12,
        // which stream 11's /W refers to where the oldest table names it, the stream was reported as one the chain did not
        // read, its objects missing from the index: a guard's cut, made the file's. What a limit that ends the chain hides,
        // or cuts, is unknown rather than missing, and the limit's report stands for it: nothing is reported of the stream
        // passed over (#156, #264, ADR 34). Lifted, the chain reads the stream through the oldest table's /XRefStm.
        var (file, oldest, stream, own, newest) = HybridFiles.PrevFallingShortOfTheStreamBeforeTheTablesOwn(
            "nothing", shortOfOwn: true, oldestNames: "/XRefStm", ownWidths: ownWidths);
        var limits = PdfReaderLimits.Default;
        limits = maxSections > 0 ? limits with { MaxXRefSectionCount = maxSections } : limits;
        limits = maxObjectLength > 0 ? limits with { MaxObjectLength = maxObjectLength } : limits;
        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = limits });

        List<(string, XRefSectionState, XRefSectionKind, long)> sections =
        [
            ("startxref", XRefSectionState.Read, XRefSectionKind.Table, newest),
            ("/XRefStm", XRefSectionState.NamedAgain, XRefSectionKind.Unknown, stream),
            ("/Prev", XRefSectionState.Relocated, XRefSectionKind.Stream, stream),
        ];

        if (maxSections != 2)
        {
            sections.Add(("/Prev", XRefSectionState.Read, XRefSectionKind.Table, oldest));
            sections.Add(("/XRefStm", maxObjectLength > 0 ? XRefSectionState.Malformed : XRefSectionState.Read, XRefSectionKind.Stream, own));
        }

        document.Reader.Structure.Sections.Select(section => (section.NamedBy, section.State, section.Kind, section.Offset)).Should().Equal(sections);
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(
            maxSections == 2
                ?
                [
                    PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefHybridStreamLost, PdfDiagnosticCodes.LimitXRefSectionCount,
                    PdfDiagnosticCodes.XRefRebuilt, PdfDiagnosticCodes.ObjectRedefined,
                ]
                : maxObjectLength > 0
                    ? [PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefHybridStreamLost, PdfDiagnosticCodes.LimitObject]
                    : [PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefHybridStreamLost]);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(0)]
    public void A_stream_passed_over_near_a_hybrid_table_s_prev_whose_own_table_a_full_index_stops_before_its_xrefstm_is_left_to_the_limit(int maxIndexEntries)
    {
        // The newest table names through /XRefStm the first revision's stream, which it read in the stream's place, and through
        // /Prev a place 5 bytes before the second revision's stream, stream 10, which gives no /Prev and lies past the first:
        // the search passes over it as possibly the table's own, and takes the second revision's table, just past it, whose
        // /XRefStm names stream 10. Where MaxIndexEntries fills the index at that table's rows, its /XRefStm went unread, and
        // stream 10 was reported as one the chain did not read: the guard's cut, made the file's. A full index hides what the
        // table names, and the limit's report stands for it: nothing is reported of the stream passed over (#156, #264, ADR
        // 34). With room for 6 entries or more, the chain reads stream 10 through that /XRefStm.
        var (file, first, second, table, newest) = HybridFiles.NamingTheFirstStreamAndFallingShortOfTheSecond();
        var limits = maxIndexEntries > 0 ? PdfReaderLimits.Default with { MaxIndexEntries = maxIndexEntries } : PdfReaderLimits.Default;
        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = limits });

        List<(string, XRefSectionState, XRefSectionKind, long)> sections =
        [
            ("startxref", XRefSectionState.Read, XRefSectionKind.Table, newest),
            ("/XRefStm", XRefSectionState.Read, XRefSectionKind.Stream, first),
            ("/Prev", XRefSectionState.Relocated, XRefSectionKind.Table, table),
        ];

        if (maxIndexEntries != 4)
        {
            sections.Add(("/XRefStm", XRefSectionState.Read, XRefSectionKind.Stream, second));
        }

        document.Reader.Structure.Sections.Should().HaveCount(maxIndexEntries switch { 4 => 3, 6 => 4, _ => 6 });
        document.Reader.Structure.Sections.Take(sections.Count).Select(section => (section.NamedBy, section.State, section.Kind, section.Offset))
            .Should().Equal(sections);
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(
            maxIndexEntries switch
            {
                4 => [PdfDiagnosticCodes.LimitIndexEntries, PdfDiagnosticCodes.XRefOffsetAdjusted],
                6 => [PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.LimitIndexEntries],
                _ => [PdfDiagnosticCodes.XRefOffsetAdjusted],
            });
    }

    [Theory]
    [InlineData(2)]
    [InlineData(0)]
    public void A_stream_passed_over_near_a_hybrid_table_s_prev_that_no_section_names_is_left_to_a_limit_that_ended_the_chain(int maxSections)
    {
        // As above, stream 11 named by nothing: the chain never reads it, and it is reported as a stream passed over, its
        // objects missing from the index — but where MaxXRefSectionCount stops the chain at 2 sections, before the oldest
        // table, whose trailer may have named it: what lies past the guard is unknown, and the limit's report stands for it
        // (#156, #264, ADR 34).
        var (file, _, _, own, _) = HybridFiles.PrevFallingShortOfTheStreamBeforeTheTablesOwn("nothing", shortOfOwn: true);
        var limits = maxSections > 0 ? PdfReaderLimits.Default with { MaxXRefSectionCount = maxSections } : PdfReaderLimits.Default;
        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = limits });

        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(
            maxSections > 0
                ?
                [
                    PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefHybridStreamLost, PdfDiagnosticCodes.LimitXRefSectionCount,
                    PdfDiagnosticCodes.XRefRebuilt, PdfDiagnosticCodes.ObjectRedefined,
                ]
                : [PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefHybridStreamLost, PdfDiagnosticCodes.XRefStreamPassedOver]);

        if (maxSections == 0)
        {
            document.Diagnostics[2].Position.Should().Be(own);
        }
    }

    [Theory]
    [InlineData(300, 256)]
    [InlineData(440, 256)]
    [InlineData(300, 0)]
    [InlineData(440, 0)]
    [InlineData(0, 256)]
    public void A_stream_passed_over_near_a_hybrid_table_s_prev_is_left_to_the_trailer_guard_where_white_space_past_it_turned_the_prev_s_reading_into_the_search(
        int run, int maxTrailerLength)
    {
        // The newest table's /Prev names the first of 300 or 440 spaces before the oldest table, and its /XRefStm stream 11,
        // past which stream 12, of /Type /XRef and no /Prev, may be the table's own. Read whole, the /Prev crosses the spaces
        // and reads the oldest table where it is named: no search runs, and nothing is passed over. Where MaxTrailerLength,
        // lowered to 256, cuts the white space, the section is not read from there, and the search near the offset passes
        // stream 12 over and takes the oldest table past the run: stream 12 was reported as a stream the chain did not read,
        // a report only the guard made, laid on the file. The limit's report stands for what the search passed over, and
        // nothing is reported of the stream (#156, #289, ADR 34). With no run, the /Prev names the table itself.
        var (file, runAt, oldest, stream, _, newest) = HybridFiles.PrevNamingWhiteSpaceBeforeTheOldestTable(run);
        var limits = maxTrailerLength > 0 ? PdfReaderLimits.Default with { MaxTrailerLength = maxTrailerLength } : PdfReaderLimits.Default;
        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = limits });
        var cut = maxTrailerLength > 0 && run > maxTrailerLength;

        document.Reader.Structure.Sections.Select(section => (section.NamedBy, section.State, section.Kind, section.Offset)).Should().Equal(
            ("startxref", XRefSectionState.Read, XRefSectionKind.Table, newest),
            ("/XRefStm", XRefSectionState.Read, XRefSectionKind.Stream, stream),
            ("/Prev", cut ? XRefSectionState.Relocated : XRefSectionState.Read, XRefSectionKind.Table, cut ? oldest : runAt));
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(
            cut ? [PdfDiagnosticCodes.LimitTrailer, PdfDiagnosticCodes.XRefOffsetAdjusted] : []);
        document.WasRepaired.Should().BeFalse();
    }

    [Theory]
    [InlineData("the oldest table", true, 1000, 512)]
    [InlineData("the oldest table", false, 1000, 512)]
    [InlineData("the stream", true, 700, 256)]
    [InlineData("the oldest table", true, 1000, 0)]
    [InlineData("the oldest table", false, 1000, 0)]
    [InlineData("the oldest table", true, 0, 512)]
    [InlineData("the oldest table", true, 200, 512)]
    [InlineData("the stream", true, 700, 0)]
    [InlineData("the stream", true, 400, 256)]
    public void A_stream_passed_over_near_a_hybrid_table_s_prev_is_left_to_the_trailer_guard_where_white_space_past_it_keeps_what_an_older_link_names_unread(
        string runBefore, bool oldestNamesOwn, int run, int maxTrailerLength)
    {
        // The newest table's /Prev falls inside a comment near stream 12, of /Type /XRef and no /Prev, which may be the
        // table's own: the search near it passes stream 12 over and takes the table past the comment. That table's /Prev
        // names the first of a run of spaces before the oldest table, whose /XRefStm names stream 12 — or names the oldest
        // table, whose /XRefStm names the first of a run of spaces before stream 12. Read whole, the chain crosses the run,
        // and reads stream 12 through that /XRefStm: nothing is reported of it. Where MaxTrailerLength cuts a run of 1,000
        // or 700 spaces, the section is not read from there, and the search near the offset finds nothing within 512 bytes:
        // the link that names stream 12 is the guard's to hide, and stream 12 was reported as a stream the chain did not
        // read, the guard's cut laid on the file. The limit's report stands for it, beside the section reported missing
        // (#289), and nothing is reported of the stream (#156, ADR 34) — even where the oldest table names nothing, which
        // the chain read whole finds, and reports. A run the guard does not cut, or one of 400 spaces, within the search's
        // reach, past which it finds stream 12 moved, leaves stream 12 read.
        var (file, _, own, runAt, older, _, _, _) = HybridFiles.PassingOverAStreamAnOlderTableNamesPastWhiteSpace(runBefore, run, oldestNamesOwn);
        var limits = maxTrailerLength > 0 ? PdfReaderLimits.Default with { MaxTrailerLength = maxTrailerLength } : PdfReaderLimits.Default;
        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = limits });
        var cut = maxTrailerLength > 0 && run > maxTrailerLength;

        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(
            !cut
                ? oldestNamesOwn
                    ? [PdfDiagnosticCodes.XRefOffsetAdjusted]
                    : [PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefStreamPassedOver]
                : run < 512
                    ? [PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.LimitTrailer, PdfDiagnosticCodes.XRefOffsetAdjusted]
                    : [PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.LimitTrailer, PdfDiagnosticCodes.XRefSectionMissing]);
        document.Diagnostics[0].Position.Should().Be(older);
        document.Diagnostics[^1].Position.Should().Be(!cut ? (oldestNamesOwn ? older : own) : run < 512 ? own : runAt);

        if (cut)
        {
            document.Diagnostics[1].Position.Should().Be(runAt);
        }

        document.WasRepaired.Should().BeFalse();
    }

    [Theory]
    [InlineData(false, false, 300, 256)]
    [InlineData(false, false, 400, 256)]
    [InlineData(true, true, 300, 256)]
    [InlineData(false, false, 300, 0)]
    [InlineData(false, false, 400, 0)]
    [InlineData(true, true, 300, 0)]
    public void A_stream_passed_over_near_a_hybrid_table_s_prev_is_reported_nowhere_once_white_space_past_the_trailer_guard_turned_a_reading_into_the_search_whatever_section_it_finds(
        bool unnamedTable, bool oldestNamesOwn, int run, int maxTrailerLength)
    {
        // As above, the search near the newest table's /Prev passes stream 12 over and takes the table past the comment,
        // whose /Prev names the first of 300 or 400 spaces before the oldest table, which names nothing — or, past a table no
        // link names written right before the run, names stream 12 through /XRefStm. Read whole, the chain crosses the run
        // and reads the oldest table: stream 12, which no link reaches, is reported, or is read through that /XRefStm. Where
        // MaxTrailerLength, lowered to 256, cuts the run, the section is not read from there, and the search near the offset,
        // which reaches 512 bytes, takes a table: the oldest, past the run, read whole, which hides nothing, or the table no
        // link names, before the run, which hides the link that names stream 12, object 5 reading as null. The reader cannot
        // tell the two apart without reading past the guard: the search may take a section other than the one past the run,
        // and the link that names the stream is then the guard's to hide. So once that guard is reached no stream passed
        // over is reported anywhere in the document, even where the search takes the section past the run and reads it
        // whole, and the report the file makes read whole is withheld with the rest (#156, #289, ADR 34).
        var (file, _, own, runAt, older, unnamed, oldest, _) = HybridFiles.PassingOverAStreamAnOlderTableNamesPastWhiteSpace(
            "the oldest table", run, oldestNamesOwn, unnamedTable);
        var limits = maxTrailerLength > 0 ? PdfReaderLimits.Default with { MaxTrailerLength = maxTrailerLength } : PdfReaderLimits.Default;
        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = limits });

        if (maxTrailerLength > 0)
        {
            var taken = unnamedTable ? unnamed : oldest;
            document.Reader.Structure.Sections.Select(section => (section.NamedBy, section.State, section.Offset)).Skip(3).Should().Equal(
                ("/Prev", XRefSectionState.Relocated, taken));
            document.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Position)).Should().Equal(
                (PdfDiagnosticCodes.XRefOffsetAdjusted, older), (PdfDiagnosticCodes.LimitTrailer, runAt), (PdfDiagnosticCodes.XRefOffsetAdjusted, taken));
            document.Diagnostics[2].Message.Should().Be(unnamedTable
                ? string.Create(CultureInfo.InvariantCulture, $"The cross-reference section /Prev names at offset {runAt} was found {runAt - unnamed} bytes before it.")
                : string.Create(CultureInfo.InvariantCulture, $"The cross-reference section /Prev names at offset {runAt} was found {run} bytes after it."));
        }
        else
        {
            document.Reader.Structure.Sections.Select(section => (section.NamedBy, section.State, section.Offset)).Skip(3).Should().Equal(
                unnamedTable
                    ? [("/Prev", XRefSectionState.Read, runAt), ("/XRefStm", XRefSectionState.Read, own)]
                    : [("/Prev", XRefSectionState.Read, runAt)]);
            document.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Position)).Should().Equal(
                unnamedTable
                    ? [(PdfDiagnosticCodes.XRefOffsetAdjusted, older)]
                    : [(PdfDiagnosticCodes.XRefOffsetAdjusted, older), (PdfDiagnosticCodes.XRefStreamPassedOver, own)]);
        }

        if (unnamedTable && maxTrailerLength > 0)
        {
            document.GetObject(new PdfObjectId(5)).Should().BeSameAs(PdfNull.Instance);
        }
        else
        {
            Text(document, 5).Should().Be("five");
        }

        document.WasRepaired.Should().BeFalse();
    }

    [Theory]
    [InlineData("a header in a comment", false, "9 0 obj")]
    [InlineData("a header in a string", false, "9 0 obj")]
    [InlineData("a stream of no type", false, "9 0 obj")]
    [InlineData("a readable stream of no type", false, "9 0 obj")]
    [InlineData("a readable header in a comment", false, "9 0 obj")]
    [InlineData("a header on its line", true, "9 0 obj")]
    [InlineData("a readable header on its line", true, "9 0 obj")]
    [InlineData("a header on its line", true, "+9 0 obj")]
    [InlineData("a header on its line", true, "++9 0 obj")]
    [InlineData("a header on its line", true, "+-9 0 obj")]
    [InlineData("a header on its line", true, "+9 0 %c\nobj")]
    [InlineData("a readable header on its line", true, "+9 0 obj")]
    [InlineData("a header on its line", false, "-9 0 obj")]
    [InlineData("a header on its line", false, "-+9 0 obj")]
    public void A_place_passed_over_near_a_hybrid_table_s_prev_is_reported_only_where_it_declares_itself_a_cross_reference_stream(
        string place, bool reported, string header)
    {
        // Past the stream the table's /XRefStm read, which gives no /Prev, the table's /Prev falls 20 bytes short of object
        // 9, which gives no /Prev either: the search passes over it as possibly the table's own, the stream the /XRefStm read
        // with it, and takes the oldest table. Object 9 was reported as a cross-reference stream passed over whatever it is: a
        // header a comment or a string holds on its line, which says nothing of a section, or a stream that gives only a /W,
        // which may be anything, readable or not — places the search drops where a fault of the file keeps them from being
        // read, and takes where they read. Only a place that declares itself a cross-reference stream, as one the search
        // takes though a fault of the file keeps it from being read does — its dictionary gives /Type /XRef, and its header is
        // a token its line reads —, is reported (#156, #171). A header whose number is written with a sign or a run of signs
        // that starts with a +, which a load reads as object 9, is such a token from its first sign, and is reported there;
        // found by the walk back from obj at its digit, where no token starts, it was not reported unless a comment parted
        // its tokens. One a - starts gives no object's number, and is not (#327).
        var (file, oldest, stream, at, newest) = HybridFiles.PrevFallingShortOfAPlacePastTheStream(place, header: header);
        using var document = PdfDocument.Open(file);

        document.Reader.Structure.Sections.Select(section => (section.NamedBy, section.State, section.Kind, section.Offset)).Should().Equal(
            ("startxref", XRefSectionState.Read, XRefSectionKind.Table, newest),
            ("/XRefStm", XRefSectionState.Read, XRefSectionKind.Stream, stream),
            ("/Prev", XRefSectionState.Relocated, XRefSectionKind.Table, oldest));
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(
            reported ? [PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefStreamPassedOver] : [PdfDiagnosticCodes.XRefOffsetAdjusted]);
        document.Diagnostics[^1].Position.Should().Be(reported ? at : oldest);
        Text(document, 5).Should().Be("old five");
        document.WasRepaired.Should().BeFalse();
    }

    [Theory]
    [InlineData(600, false, true)]
    [InlineData(600, true, true)]
    [InlineData(5000, false, false)]
    public void A_stream_passed_over_near_a_hybrid_table_s_prev_is_reported_only_where_its_line_starts_within_the_reach_of_the_line_test(
        int lead, bool lineBroken, bool reported)
    {
        // As above, the table's /Prev falls 20 bytes into object 9, of /Type /XRef, which gives no /Prev: the search passes
        // it over as possibly the table's own, and takes the oldest table. Its header follows object 8 on a line that starts
        // 600 bytes before it, before the bytes the search reads: the line test reads those again with as many as 4,096
        // bytes before them, lexes the line from its start, and finds the header a token there, which is reported. An end
        // of line before the header starts its line within the bytes searched, reported alike. A line that starts more than
        // 4,096 bytes before them declares nothing, as for a place that cannot be read, and object 9 is not reported (#156,
        // #171).
        var (file, oldest, stream, at, newest) = HybridFiles.PrevFallingInsideAStreamOnALongLine(lead, lineBroken);
        using var document = PdfDocument.Open(file);

        document.Reader.Structure.Sections.Select(section => (section.NamedBy, section.State, section.Kind, section.Offset)).Should().Equal(
            ("startxref", XRefSectionState.Read, XRefSectionKind.Table, newest),
            ("/XRefStm", XRefSectionState.Read, XRefSectionKind.Stream, stream),
            ("/Prev", XRefSectionState.Relocated, XRefSectionKind.Table, oldest));
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(
            reported ? [PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefStreamPassedOver] : [PdfDiagnosticCodes.XRefOffsetAdjusted]);
        document.Diagnostics[^1].Position.Should().Be(reported ? at : oldest);
        Text(document, 5).Should().Be("old five");
        document.WasRepaired.Should().BeFalse();
    }

    [Theory]
    [InlineData("a header in a comment")]
    [InlineData("a header in a string")]
    [InlineData("a stream of no type")]
    [InlineData("a readable stream of no type")]
    [InlineData("a readable header in a comment")]
    [InlineData("a header on its line")]
    [InlineData("a readable header on its line")]
    public void A_place_near_a_hybrid_table_s_prev_that_no_other_section_stands_beside_is_taken_only_where_it_reads_or_declares_itself_a_section(string place)
    {
        // As above, the oldest table out of the search's reach: the search comes back to object 9 once no other place holds
        // a section, and takes it where it reads, whatever its line is, or where it declares itself a cross-reference stream
        // though it cannot be read; it drops a header a comment or a string holds on its line, and a stream that gives only a
        // /W, where they cannot be read, and the section is reported missing (#171, #281).
        var (file, _, _, at, _) = HybridFiles.PrevFallingShortOfAPlacePastTheStream(place, alone: true);
        var reads = place.StartsWith("a readable", StringComparison.Ordinal);
        var taken = reads || place == "a header on its line";
        using var document = PdfDocument.Open(file);

        document.Reader.Structure.Sections[^1].Should().Match<XRefSectionRecord>(
            section => section.NamedBy == "/Prev" && section.NamedOffset == at - 20 &&
                (taken
                    ? section.Offset == at && section.Kind == XRefSectionKind.Stream &&
                        section.State == (reads ? XRefSectionState.Relocated : XRefSectionState.Malformed)
                    : section.State == XRefSectionState.NotFound));
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(
            reads
                ? [PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefRebuilt]
                : taken
                    ? [PdfDiagnosticCodes.StreamTruncated, PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefSectionUnreadable, PdfDiagnosticCodes.XRefRebuilt]
                    : [PdfDiagnosticCodes.StreamTruncated, PdfDiagnosticCodes.XRefSectionMissing, PdfDiagnosticCodes.XRefRebuilt]);
    }

    [Theory]
    [InlineData("+9 0 obj", 1)]
    [InlineData("++9 0 obj", 2)]
    [InlineData("+-9 0 obj", 2)]
    [InlineData("+9 0 %c\nobj", 0)]
    [InlineData("-9 0 obj", -1)]
    [InlineData("-+9 0 obj", -1)]
    public void A_header_near_a_hybrid_table_s_prev_whose_number_is_written_with_signs_declares_itself_a_section_where_they_start_with_a_plus(
        string header, int found)
    {
        // As above, object 9 alone near the place the table's /Prev names, a stream of /Type /XRef that cannot be read, its
        // number written with a sign or a run of signs. A load reads a run that starts with a + as object 9's number: the
        // header is a token from its first sign, and declares itself a section. The walk back from obj finds it at its
        // digit, where it is taken, as the walk found it, and reported unreadable; asked there whether a token starts, its
        // line said none did, and the section was reported missing, though the file holds it near where it is named. A
        // header whose tokens a comment parts is found at its sign, and was taken. A run that starts with a - makes no
        // object's number, and the section is missing (#281, #327).
        var (file, _, _, at, _) = HybridFiles.PrevFallingShortOfAPlacePastTheStream("a header on its line", alone: true, header: header);
        using var document = PdfDocument.Open(file);

        document.Reader.Structure.Sections[^1].Should().Match<XRefSectionRecord>(
            section => section.NamedBy == "/Prev" && section.NamedOffset == at - 20 &&
                (found >= 0
                    ? section.Offset == at + found && section.Kind == XRefSectionKind.Stream && section.State == XRefSectionState.Malformed
                    : section.State == XRefSectionState.NotFound));
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(
            found >= 0
                ? [PdfDiagnosticCodes.StreamTruncated, PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefSectionUnreadable, PdfDiagnosticCodes.XRefRebuilt]
                : [PdfDiagnosticCodes.StreamTruncated, PdfDiagnosticCodes.XRefSectionMissing, PdfDiagnosticCodes.XRefRebuilt]);
    }

    [Theory]
    [InlineData("9 0 obj", 0)]
    [InlineData("+9 0 obj", 1)]
    [InlineData("++9 0 obj", 2)]
    [InlineData("+9 0 %c\nobj", 0)]
    public void A_stream_alone_near_a_hybrid_table_s_prev_whose_dictionary_a_guard_cut_is_the_guard_s_whether_a_plus_sign_starts_its_number_or_not(
        string header, int found)
    {
        // As above, object 9 alone near the place the table's /Prev names, a stream of /Type /XRef whose dictionary writes a
        // /Pad of 300 bytes after its type, which MaxTrailerLength 160 cuts: it declared itself a section before the cut,
        // and is the section found moved, the guard's, the chain ending there for it, nothing rebuilt. Written with a sign or
        // a run of signs that starts with a +, its header is a token from that sign, as a load reads it: found at its digit
        // by the walk back from obj, it was asked there whether a token starts, and dropped, the section reported missing
        // and the index rebuilt — a guard's cut laid on the file (#281, #327, ADR 34).
        var (file, _, _, at, _) = HybridFiles.PrevFallingShortOfAPlacePastTheStream("a long header on its line", alone: true, header: header);
        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxTrailerLength = 160 } });

        document.Reader.Structure.Sections[^1].Should().Match<XRefSectionRecord>(
            section => section.NamedBy == "/Prev" && section.NamedOffset == at - 20 && section.Offset == at + found &&
                section.Kind == XRefSectionKind.Stream && section.State == XRefSectionState.Malformed && section.CutByLimit);
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(PdfDiagnosticCodes.LimitTrailer, PdfDiagnosticCodes.XRefOffsetAdjusted);
        document.Diagnostics[0].Position.Should().Be(at + found);
        document.GetObject(new PdfObjectId(5)).Should().BeSameAs(PdfNull.Instance);
        document.WasRepaired.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_stream_passed_over_near_a_hybrid_table_s_prev_is_reported_only_where_the_chain_gave_the_index_an_entry(bool oldestFreeRow)
    {
        // As above, the search near the table's /Prev passes over stream 11, and takes the oldest table, every section of
        // the chain indexing nothing — or the oldest giving the row that heads the free list. A chain that indexed nothing
        // leaves the whole file to a rebuild, which reads every object, stream 11's among them: nothing is reported of the
        // stream passed over then (#156).
        var (file, oldest, own, _) = HybridFiles.PrevFallingShortOfTheTablesOwnStreamInAChainOfEmptySections(oldestFreeRow);
        using var document = PdfDocument.Open(file);

        document.Reader.Structure.Sections[^1].Should().Match<XRefSectionRecord>(
            section => section.NamedBy == "/Prev" && section.State == XRefSectionState.Relocated && section.Offset == oldest);
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(
            oldestFreeRow
                ? [PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefStreamPassedOver, PdfDiagnosticCodes.XRefRebuilt]
                : [PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefRebuilt]);

        if (oldestFreeRow)
        {
            document.Diagnostics[1].Position.Should().Be(own);
        }

        document.WasRepaired.Should().BeTrue();
    }

    [Fact]
    public void A_stream_two_searches_near_hybrid_tables_prevs_pass_over_is_reported_once()
    {
        // A writer copies /XRefStm into each later trailer, and the newest table's /Prev and the third revision's both fall
        // near stream 11, the third revision's own, which nothing names and which gives no /Prev, past stream 10, which the
        // /XRefStm of each names. Each search passes over stream 11 as possibly its table's own, and takes the table beyond
        // it, the chain read whole; the chain never reads stream 11, which is reported once, at the stream, by the first
        // search that passed over it (#156).
        var (file, oldest, stream, older, own, middle, newest) = HybridFiles.PassingOverOneStreamTwice();
        using var document = PdfDocument.Open(file);

        document.Reader.Structure.Sections.Select(section => (section.NamedBy, section.State, section.Kind, section.Offset)).Should().Equal(
            ("startxref", XRefSectionState.Read, XRefSectionKind.Table, newest),
            ("/XRefStm", XRefSectionState.Read, XRefSectionKind.Stream, stream),
            ("/Prev", XRefSectionState.Relocated, XRefSectionKind.Table, middle),
            ("/XRefStm", XRefSectionState.NamedAgain, XRefSectionKind.Unknown, stream),
            ("/Prev", XRefSectionState.Relocated, XRefSectionKind.Table, older),
            ("/XRefStm", XRefSectionState.NamedAgain, XRefSectionKind.Unknown, stream),
            ("/Prev", XRefSectionState.Read, XRefSectionKind.Table, oldest));
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(
            PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefStreamPassedOver);
        document.Diagnostics[2].Position.Should().Be(own);
        document.Diagnostics[2].Message.Should().StartWith(Invariant(
            $"The cross-reference stream at offset {own}, near offset {own + 40}, which the /Prev of the cross-reference table at offset {newest} names,"));
        document.WasRepaired.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(28)]
    public void The_stream_a_hybrid_table_s_xrefstm_read_put_back_unread_counts_where_it_is_read_and_the_bound_may_stop_it(int spent)
    {
        // As above, stream 6, which the /XRefStm read, gives the offset the table's /Prev names, and both it and stream 11 may
        // be the table's own: stream 6, the nearer, is put back unread, and stream 11 passed over once read, both tried once
        // every other place was, stream 6 first. Stream 11 was counted at its first reading, and its retry is not counted
        // again; stream 6 is counted where it is read. Where a newer table's search spent 28 of the places the reader allows a
        // document, the bound stops stream 6, and stream 11 is taken, the chain ending there (#156).
        var (built, _, stream, own, hybrid) = HybridFiles.PrevFallingShortOfTheStreamBeforeTheTablesOwn("nothing", shortOfOwn: false, "the table's /Prev");
        var (file, _) = spent > 0 ? HybridFiles.UnderPlacesSpent(built, hybrid, spent) : (built, hybrid);
        using var document = PdfDocument.Open(file);

        var taken = spent > 0 ? own : stream;
        document.Reader.Structure.Sections.Should().ContainSingle(section => section.NamedBy == "/Prev" && section.NamedOffset == stream - 7)
            .Which.Should().Match<XRefSectionRecord>(section => section.State == XRefSectionState.Relocated && section.Offset == taken);
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(
            spent > 0
                ? [PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefRebuilt, PdfDiagnosticCodes.ObjectRedefined]
                :
                [
                    PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefHybridStreamLost, PdfDiagnosticCodes.XRefChainCycle,
                    PdfDiagnosticCodes.XRefStreamPassedOver, PdfDiagnosticCodes.XRefRebuilt, PdfDiagnosticCodes.ObjectRedefined,
                ]);

        if (spent == 0)
        {
            document.Diagnostics[3].Position.Should().Be(own);
        }
    }

    [Fact]
    public void Only_the_prev_of_the_table_whose_xrefstm_read_a_stream_reads_it_again()
    {
        // The file of the test above, saved once more by a table that names through /XRefStm stream 6, read in the stream's
        // place, and through /Prev the table that was newest, whose own /Prev falls short of the stream as it did. The search
        // near the offset that /Prev names comes to the stream, which only the /Prev of the table that read it would take:
        // that of an older table passes over it, as over any section the chain read, and finds none (#156).
        var (file, stream, middle, newest) = HybridFiles.OlderPrevFallingShortOfTheStreamBefore();
        using var document = PdfDocument.Open(file);

        document.Reader.Structure.Sections.Select(section => (section.NamedBy, section.State, section.Offset)).Should().Equal(
            ("startxref", XRefSectionState.Read, newest),
            ("/XRefStm", XRefSectionState.Read, stream),
            ("/Prev", XRefSectionState.Read, middle),
            ("/Prev", XRefSectionState.NotFound, stream - 7));
    }

    /// <summary>
    /// Gives where the section before the newest lies in a file <see cref="HybridFiles.NamingThePreviousStream"/> writes, its
    /// stream's object header, or <see cref="HybridFiles.NamingThePreviousTable"/> writes, its second table's keyword.
    /// </summary>
    private static long PreviousSection(byte[] file, bool stream) =>
        stream ? PdfTemplate.OffsetOf(file, "5 0 obj") : PdfTemplate.OffsetOf(file, "\nxref\n", 2) + 1;

    [Fact]
    public void A_stream_two_tables_name_is_read_once_and_recorded_where_each_names_it()
    {
        // The newest table copies the update's /XRefStm, as some writers do: the stream is read once, where the newest names
        // it, and the update's naming, which left no record, is recorded as naming it again (#156).
        var (file, _, older, stream, newest) = HybridFiles.SharedStream(olderSize: 7);
        using var document = PdfDocument.Open(file);

        var sections = document.Reader.Structure.Sections;
        sections.Select(section => (section.NamedBy, section.State, section.NamedFrom)).Should().Equal(
            ("startxref", XRefSectionState.Read, PdfTemplate.OffsetOf(file, "startxref", 3)),
            ("/XRefStm", XRefSectionState.Read, newest),
            ("/Prev", XRefSectionState.Read, newest),
            ("/XRefStm", XRefSectionState.NamedAgain, older),
            ("/Prev", XRefSectionState.Read, older));
        sections[1].Offset.Should().Be(stream);
        sections[3].ReadAs.Should().BeSameAs(sections[1]);
        sections[3].NamesNoStream.Should().BeNull();
        document.Diagnostics.Should().BeEmpty();
        Text(document, 6).Should().Be("six");
    }

    [Fact]
    public void A_stream_named_short_of_its_header_and_named_at_it_again_is_read_once()
    {
        // The newest table names the stream two bytes short of its header, past which the chain reads it; the update names
        // the header itself. The chain knew it had read the stream only by the offset named first, and read it again; it
        // knows where it read each section, and records the update's naming as naming the stream again (#156).
        var (file, stream) = HybridFiles.SharedStreamNamedShort();
        using var document = PdfDocument.Open(file);

        var sections = document.Reader.Structure.Sections;
        sections.Count(section => section.IsHybridStream).Should().Be(1);
        var again = sections.Should().ContainSingle(section => section.State == XRefSectionState.NamedAgain).Which;
        again.Offset.Should().Be(stream);
        again.ReadAs.Should().BeSameAs(sections[1]);
        sections[1].Offset.Should().Be(stream - 2);
        sections[1].Padding.Should().Be(2);
        document.Diagnostics.Should().BeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_stream_one_table_names_at_its_header_and_one_short_of_it_is_read_once_whichever_names_it_first(bool headerFirst)
    {
        // The two tables name the stream at its header and two bytes short of it, in the spaces before it; its row for object
        // 8 is of type 3, and its /Size, 12, is wrong. Named at its header first, it was read again through the spaces, and
        // each report on it came out twice, the second naming's imprecise offset among them: the chain knows a section by
        // where its first token starts, and records the second naming, either way, as naming it again (#156).
        var (file, stream) = HybridFiles.SharedStreamNamedTwice(headerFirst, size: 12, eightType: 3);
        using var document = PdfDocument.Open(file);

        var sections = document.Reader.Structure.Sections;
        var read = sections.Should().ContainSingle(section => section.IsHybridStream).Which;
        read.State.Should().Be(XRefSectionState.Read);
        (read.Offset + read.Padding).Should().Be(stream);
        var again = sections.Should().ContainSingle(section => section.State == XRefSectionState.NamedAgain).Which;
        again.ReadAs.Should().BeSameAs(read);
        again.Offset.Should().Be(headerFirst ? stream - 2 : stream);
        again.Padding.Should().Be(headerFirst ? 2 : 0);
        document.Diagnostics.Should().ContainSingle().Which.Code.Should().Be(PdfDiagnosticCodes.XRefEntryReserved);
        var findings = new PdfValidator().Validate(document).Findings;
        findings.Select(finding => finding.RuleId).Should().BeEquivalentTo(
            [PdfValidationRuleIds.FileSizeWrong, PdfValidationRuleIds.XRefEntryTypeReserved, PdfValidationRuleIds.XRefOffsetImprecise]);
        findings.Single(finding => finding.RuleId == PdfValidationRuleIds.XRefOffsetImprecise).Message.Should().Be(Invariant(
            $"An offset names the white space before what it designates rather than its first byte: /XRefStm gives offset {stream - 2}, 2 bytes before the header of the cross-reference stream."));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_lost_stream_one_table_names_at_its_header_and_one_short_of_it_is_reported_once_whichever_names_it_first(bool headerFirst)
    {
        // The stream's /W gives a negative width: there, and lost, it was read again for the naming that did not name the
        // offset read first, reported twice, and found malformed twice. Where it starts is reached all the same, though the
        // chain could not read it, and the second naming records naming it again (#156).
        var (file, stream) = HybridFiles.SharedStreamNamedTwice(headerFirst, widths: "[1 -4 2]");
        using var document = PdfDocument.Open(file);

        var sections = document.Reader.Structure.Sections;
        var lost = sections.Should().ContainSingle(section => section.State == XRefSectionState.Malformed).Which;
        (lost.Offset + lost.Padding).Should().Be(stream);
        sections.Should().ContainSingle(section => section.State == XRefSectionState.NamedAgain).Which.ReadAs.Should().BeSameAs(lost);
        document.Diagnostics.Should().ContainSingle().Which.Code.Should().Be(PdfDiagnosticCodes.XRefSectionUnreadable);
        new PdfValidator().Validate(document).Findings.Select(finding => finding.RuleId).Should().Equal(PdfValidationRuleIds.XRefSectionMalformed);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    public void A_lost_stream_named_again_through_the_end_of_line_before_its_header_is_handed_on_once_and_ends_the_chain_in_a_loop(
        int namedShortBy, int ownShortBy)
    {
        // The newest table's /Prev names the lost stream at the end of line before its header, and the stream's own /Prev names
        // the header, or the other way round, or the end of line again. A stream handed on left no mark of where it is: it was
        // read and handed on again, and reported twice, before the loop was seen. It is reached where it starts, which the
        // /Prev that names it again reaches too: a loop, placed at the stream — the same offset named again among them, which
        // was placed in the white space —, through a section the chain reached and could not read, which the message said the
        // chain had only named, of an offset it never named (#188, #208).
        var stream = LostStreamFiles.ThreeRevisions().Stream;
        var (file, _, at, _) = LostStreamFiles.ThreeRevisions(
            prev: (stream - ownShortBy).ToString(CultureInfo.InvariantCulture), namedShortBy: namedShortBy);
        at.Should().Be(stream, "where the stream lies depends on nothing its /Prev or the newest table's says");
        using var document = PdfDocument.Open(file);

        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().StartWith([PdfDiagnosticCodes.XRefSectionUnreadable, PdfDiagnosticCodes.XRefChainCycle]);
        document.Diagnostics.Count(diagnostic => diagnostic.Code == PdfDiagnosticCodes.XRefSectionUnreadable).Should().Be(1);
        document.Diagnostics[1].Position.Should().Be(stream);
        document.Diagnostics[1].Message.Should().Be(Invariant(
            $"The cross-reference chain loops back on itself: the /Prev of the section at offset {stream - namedShortBy} names offset {stream - ownShortBy}, where the chain has already reached a section it could not read."));
        document.Reader.Structure.Sections.Should().HaveCount(2);
        document.Reader.Structure.LoopOffset.Should().Be(stream);
        document.Reader.Structure.LoopTarget.Should().Be(XRefLoopTarget.Reached);
        Text(document, 3).Should().Be("only in the oldest");
        var findings = new PdfValidator().Validate(document).Findings.Select(finding => finding.RuleId);
        findings.Count(id => id == PdfValidationRuleIds.XRefSectionMalformed).Should().Be(1);
        findings.Count(id => id == PdfValidationRuleIds.XRefChainLoop).Should().Be(1);
        findings.Should().NotContain(PdfValidationRuleIds.XRefOffsetImprecise, "a section that cannot be read counts nowhere it is named short");
    }

    [Fact]
    public void A_prev_looping_back_to_a_lost_stream_found_moved_says_the_chain_reached_a_section_it_could_not_read()
    {
        // The newest table names the lost stream 7 bytes short of it, inside the endobj before it, where the search near that
        // offset finds it moved, and the stream's own /Prev names its header. The loop was said to run through an offset the
        // chain had named, though it named only the place 7 bytes before: the chain reached the stream there, found moved,
        // and could not read it, which the reader and the validator say (#188, #208).
        var (file, _, stream, _) = LostStreamFiles.ThreeRevisions(prev: "{stream}", namedShortBy: 7);
        using var document = PdfDocument.Open(file);

        document.Reader.Structure.Sections[1].State.Should().Be(XRefSectionState.Malformed);
        document.Reader.Structure.Sections[1].Offset.Should().Be(stream);
        var loop = document.Diagnostics.Should().ContainSingle(diagnostic => diagnostic.Code == PdfDiagnosticCodes.XRefChainCycle).Which;
        loop.Position.Should().Be(stream);
        loop.Message.Should().Be(Invariant(
            $"The cross-reference chain loops back on itself: the /Prev of the section at offset {stream} names offset {stream}, where the chain has already reached a section it could not read."));
        new PdfValidator().Validate(document).Findings.Single(finding => finding.RuleId == PdfValidationRuleIds.XRefChainLoop).Message.Should().Be(Invariant(
            $"The /Prev of the cross-reference section at offset {stream} names offset {stream}, a section the chain has already reached and could not read: the chain loops."));
    }

    [Fact]
    public void A_prev_looping_back_to_an_object_an_xrefstm_named_that_only_gives_a_width_says_the_chain_only_named_it()
    {
        // The newest table names through /XRefStm object 9, which gives a /W of a negative width and no /Type, and the oldest
        // table's /Prev names it again. What gives only a /W may be anything: the chain could not read it, and did not reach
        // a section there, as it reaches one that declares itself a section — the loop runs through an offset it only named.
        var file = new HandWrittenFile();
        var catalog = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        var pages = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        var nine = file.Object(9, "<< /Size 10 /W [1 -4 2] /Length 0 >>\nstream\n\nendstream");
        file.Raw("%" + new string('-', 1200) + "\n");
        var oldest = file.Position;
        file.Raw(Invariant($"xref\n0 3\n0000000000 65535 f \n{catalog:D10} 00000 n \n{pages:D10} 00000 n \ntrailer\n<< /Size 3 /Root 1 0 R /Prev {nine} >>\nstartxref\n{oldest}\n%%EOF\n"));
        var newest = file.Position;
        file.Raw(Invariant($"xref\n0 1\n0000000000 65535 f \ntrailer\n<< /Size 3 /Root 1 0 R /Prev {oldest} /XRefStm {nine} >>\nstartxref\n{newest}\n%%EOF\n"));
        using var document = PdfDocument.Open(file.Bytes);

        document.Reader.Structure.Sections[1].State.Should().Be(XRefSectionState.Malformed);
        document.Reader.Structure.LoopTarget.Should().Be(XRefLoopTarget.Named);
        document.Diagnostics.Should().ContainSingle(diagnostic => diagnostic.Code == PdfDiagnosticCodes.XRefChainCycle).Which.Message.Should().Be(Invariant(
            $"The cross-reference chain loops back on itself: the /Prev of the section at offset {oldest} names offset {nine}, which the chain has already named."));
    }

    [Theory]
    [InlineData(3, 3, 1)]
    [InlineData(3, 2, 2)]
    [InlineData(3, 0, 1)]
    public void A_prev_looping_back_through_the_white_space_before_a_table_is_placed_where_it_starts_whatever_offset_it_repeats(
        int newestShortBy, int oldestShortBy, int imprecise)
    {
        // The newest table names the middle one 3 bytes short of it, in the spaces before it, and the oldest's /Prev names it
        // again: at the same offset, at another in the spaces, or at its keyword. The same offset named again was the loop's
        // place, in the spaces: the loop is placed where the table starts, whatever offset repeats, and each offset that
        // falls short of it is counted once (#188).
        var (file, oldest, middle, _) = NearbyFiles.ChainNamingTheSpacesBeforeTheMiddle(newestShortBy, oldestPrev: oldestShortBy);
        using var document = PdfDocument.Open(file);

        var loop = document.Diagnostics.Should().ContainSingle(diagnostic => diagnostic.Code == PdfDiagnosticCodes.XRefChainCycle).Which;
        loop.Position.Should().Be(middle);
        loop.Message.Should().Be(Invariant(
            $"The cross-reference chain loops back on itself: the /Prev of the section at offset {oldest} names offset {middle - oldestShortBy}, which the chain has already read."));
        document.Reader.Structure.LoopOffset.Should().Be(middle);
        document.Reader.Structure.LoopTarget.Should().Be(XRefLoopTarget.Read);
        var finding = new PdfValidator().Validate(document).Findings.Single(finding => finding.RuleId == PdfValidationRuleIds.XRefOffsetImprecise);
        finding.Message.Should().StartWith(imprecise == 1 ? "An offset names the white space" : Invariant($"{imprecise} offsets name the white space"));
        finding.Message.Should().EndWith(Invariant($"/Prev gives offset {middle - newestShortBy}, {newestShortBy} bytes before the xref keyword."));
    }

    [Fact]
    public void A_prev_looping_back_to_the_offset_the_prev_of_the_newest_table_s_xrefstm_reached_is_placed_where_its_section_starts()
    {
        // The newest table's /XRefStm names the end of line before the table before it, which it read in the stream's place,
        // and its /Prev the endobj 7 bytes short, whose search reads that table again, whole; the middle table's /Prev names
        // again the offset the /XRefStm wrote. What the chain recorded there is the /Prev's reading, found moved from another
        // offset: the white space is crossed again, and the loop placed where the table starts (#156, #188).
        var (file, _, middleAt, previous, _) = HybridFiles.PrevFallingShortOfTheTableBefore(1, middle: true, middlePrev: 1);
        using var document = PdfDocument.Open(file);

        var loop = document.Diagnostics.Should().ContainSingle(diagnostic => diagnostic.Code == PdfDiagnosticCodes.XRefChainCycle).Which;
        loop.Position.Should().Be(previous);
        loop.Message.Should().Be(Invariant(
            $"The cross-reference chain loops back on itself: the /Prev of the section at offset {middleAt} names offset {previous - 1}, which the chain has already read."));
    }

    [Fact]
    public void A_prev_looping_back_to_the_offset_startxref_names_short_of_a_stream_is_placed_at_the_stream_and_counted_once()
    {
        // startxref names the stream two bytes short of its header, where the chain reads it, and the stream's /Prev writes
        // the same offset: a loop placed at the stream, as one byte off is, and one offset that names the white space before
        // it, written twice (#188).
        var (file, stream) = NearbyFiles.StreamNamedShortTwice(prevShortBy: 2);
        using var document = PdfDocument.Open(file);

        document.Reader.Structure.LoopOffset.Should().Be(stream);
        document.Diagnostics.Should().ContainSingle(diagnostic => diagnostic.Code == PdfDiagnosticCodes.XRefChainCycle).Which.Position.Should().Be(stream);
        new PdfValidator().Validate(document).Findings.Single(finding => finding.RuleId == PdfValidationRuleIds.XRefOffsetImprecise).Message.Should().Be(Invariant(
            $"An offset names the white space before what it designates rather than its first byte: startxref gives offset {stream - 2}, 2 bytes before the header of the cross-reference stream."));
    }

    [Theory]
    [InlineData(0, 2, 2, null, null, 1, "/XRefStm", 2)]
    [InlineData(0, 2, null, 2, null, 1, "/XRefStm", 2)]
    [InlineData(2, null, null, 0, 2, 1, "/Prev", 2)]
    [InlineData(0, 2, 1, null, null, 2, "/XRefStm", 2)]
    [InlineData(3, null, null, 2, null, 2, "/Prev", 3)]
    public void Each_offset_that_names_the_white_space_before_a_section_counts_once_whichever_naming_the_chain_met_first(
        int newestPrev, int? newestXRefStm, int? middleXRefStm, int? oldestPrev, int? oldestXRefStm, int count, string first, int firstShortBy)
    {
        // The links name the white space before the middle table: one offset that an /XRefStm the newest table leaves to its
        // /Prev writes, and the middle table's /XRefStm, or the oldest's looping /Prev, writes again; the same offset met first
        // through a /Prev; and two offsets apart. An offset first met through an /XRefStm left to the /Prev was counted again
        // at its next naming, and one first met through a /Prev was not: each offset, as written, counts once (#156, #188).
        var (file, _, middle, _) = NearbyFiles.ChainNamingTheSpacesBeforeTheMiddle(newestPrev, newestXRefStm, middleXRefStm, oldestPrev, oldestXRefStm);
        using var document = PdfDocument.Open(file);

        var finding = new PdfValidator().Validate(document).Findings.Single(finding => finding.RuleId == PdfValidationRuleIds.XRefOffsetImprecise);
        finding.Message.Should().StartWith(count == 1 ? "An offset names the white space" : Invariant($"{count} offsets name the white space"));
        finding.Message.Should().EndWith(Invariant($"{first} gives offset {middle - firstShortBy}, {firstShortBy} bytes before the xref keyword."));
    }

    [Fact]
    public void A_stream_whose_prev_names_the_end_of_line_before_its_header_is_read_once_and_the_loop_placed_where_it_starts()
    {
        // The stream reads whole, gives /Size 12 where the numbers read make it 8, and its /Prev names the end of line before its
        // header. The chain read it a second time there, judged its /Size twice, and placed the loop at the copy. It is where
        // the chain read it, past the white space: the /Prev loops back to it at once (#188). No /Size is judged: the loop
        // leaves the section the /Prev should have named unknown, which may number the objects a /Size counts (#156).
        const string Readable = "/Type /XRef /Size 12 /Index [4 2 7 1] /W [1 4 2]";
        var stream = LostStreamFiles.ThreeRevisions(Readable).Stream;
        var (file, _, at, newest) = LostStreamFiles.ThreeRevisions(Readable, prev: (stream - 1).ToString(CultureInfo.InvariantCulture));
        at.Should().Be(stream);
        using var document = PdfDocument.Open(file);

        document.Reader.Structure.Sections.Select(section => (section.State, section.Offset)).Should().Equal(
            (XRefSectionState.Read, newest),
            (XRefSectionState.Read, stream));
        var loop = document.Diagnostics.Should().ContainSingle(diagnostic => diagnostic.Code == PdfDiagnosticCodes.XRefChainCycle).Which;
        loop.Position.Should().Be(stream);
        loop.Message.Should().Be(Invariant(
            $"The cross-reference chain loops back on itself: the /Prev of the section at offset {stream} names offset {stream - 1}, which the chain has already read."));
        var findings = new PdfValidator().Validate(document).Findings;
        findings.Should().NotContain(finding => finding.RuleId == PdfValidationRuleIds.FileSizeWrong);
        findings.Count(finding => finding.RuleId == PdfValidationRuleIds.XRefChainLoop).Should().Be(1);
        findings.Single(finding => finding.RuleId == PdfValidationRuleIds.XRefOffsetImprecise).Message.Should().Be(Invariant(
            $"An offset names the white space before what it designates rather than its first byte: /Prev gives offset {stream - 1}, 1 byte before the header of the cross-reference stream."));
    }

    [Fact]
    public void A_lost_stream_an_xrefstm_names_again_through_the_end_of_line_before_its_header_is_reported_once()
    {
        // The chain goes on past the lost stream through its /Prev, to the oldest table, whose /XRefStm names the stream at the
        // end of line before its header: read again there, in the stream's place, it was reported twice, and found malformed
        // twice. It is reached where it starts, and the /XRefStm records naming it again (#156, #208).
        // The offset is written in ten digits, so that the stream lies where it lay with a stand-in of the same length.
        var stream = LostStreamFiles.ThreeRevisions(oldestTrailer: " /XRefStm 0000000000").Stream;
        var (file, _, at, _) = LostStreamFiles.ThreeRevisions(oldestTrailer: Invariant($" /XRefStm {stream - 1:D10}"));
        at.Should().Be(stream);
        using var document = PdfDocument.Open(file);

        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(PdfDiagnosticCodes.XRefSectionUnreadable);
        var sections = document.Reader.Structure.Sections;
        var again = sections.Should().ContainSingle(section => section.State == XRefSectionState.NamedAgain).Which;
        again.ReadAs.Should().BeSameAs(sections[1]);
        again.Padding.Should().Be(1);
        Text(document, 3).Should().Be("only in the oldest");
        new PdfValidator().Validate(document).Findings.Select(finding => finding.RuleId).Should().ContainSingle(id => id == PdfValidationRuleIds.XRefSectionMalformed);
    }

    [Theory]
    [InlineData("an empty table", PdfDiagnosticCodes.XRefSectionEmpty, PdfValidationRuleIds.XRefSubsectionMissing, "the xref keyword")]
    [InlineData("a table read in part", PdfDiagnosticCodes.XRefSectionUnreadable, PdfValidationRuleIds.XRefSectionMalformed, "the xref keyword")]
    [InlineData("a stream of a reserved row", PdfDiagnosticCodes.XRefEntryReserved, PdfValidationRuleIds.XRefEntryTypeReserved, "the header of the cross-reference stream")]
    public void A_section_whose_prev_names_the_end_of_line_before_it_ends_the_chain_in_a_loop_at_it_and_is_reported_once(
        string kind, string code, string rule, string target)
    {
        // The section startxref names gives a /Prev that names the end of line before it: the chain read it again there,
        // reported it twice, and found it twice, the loop seen one step late. Its first token is where the chain read it: the
        // /Prev loops back to it at once, and the offset that falls short of it is counted once among the imprecise ones
        // (#156, #188).
        var (file, at) = SectionNamingItsOwnEndOfLine(kind);
        using var document = PdfDocument.Open(file);

        document.Diagnostics.Count(diagnostic => diagnostic.Code == code).Should().Be(1);
        var loop = document.Diagnostics.Should().ContainSingle(diagnostic => diagnostic.Code == PdfDiagnosticCodes.XRefChainCycle).Which;
        loop.Position.Should().Be(at);
        loop.Message.Should().Be(Invariant(
            $"The cross-reference chain loops back on itself: the /Prev of the section at offset {at} names offset {at - 1}, which the chain has already read."));
        document.Reader.Structure.Sections.Should().ContainSingle();
        var findings = new PdfValidator().Validate(document).Findings;
        findings.Count(finding => finding.RuleId == rule).Should().Be(1);
        findings.Count(finding => finding.RuleId == PdfValidationRuleIds.XRefChainLoop).Should().Be(1);
        findings.Single(finding => finding.RuleId == PdfValidationRuleIds.XRefOffsetImprecise).Message.Should().Be(Invariant(
            $"An offset names the white space before what it designates rather than its first byte: /Prev gives offset {at - 1}, 1 byte before {target}."));
    }

    [Fact]
    public void A_section_a_prev_names_again_through_the_white_space_before_it_where_it_was_found_moved_ends_the_chain_in_a_loop_at_once()
    {
        // The middle table, which the newest names 3 bytes short of it, is found moved; the oldest's /Prev names the end of line
        // before it. The chain read it again there, its rows and its trailer, judged its /Size twice, and blamed the loop on
        // the middle table's sound /Prev. The oldest table's /Prev loops back to it, where it was found (#188). The oldest's
        // offset is written in thirteen digits, as long as the /XRefStm it stands for.
        var built = HybridFiles.OldestNaming("0000000000");
        var middle = PdfTemplate.OffsetOf(built, "\nxref\n", 2) + 1;
        var oldest = PdfTemplate.OffsetOf(built, "\nxref\n") + 1;
        var text = Encoding.Latin1.GetString(built)
            .Replace(Invariant($"/Prev {middle} >>"), Invariant($"/Prev {middle - 3} >>"), StringComparison.Ordinal)
            .Replace("/XRefStm 0000000000 >>", Invariant($"/Prev {middle - 1:D13} >>"), StringComparison.Ordinal);
        using var document = PdfDocument.Open(Encoding.Latin1.GetBytes(text));

        var sections = document.Reader.Structure.Sections;
        sections.Select(section => section.State).Should().Equal(XRefSectionState.Read, XRefSectionState.Relocated, XRefSectionState.Read);
        sections[1].Offset.Should().Be(middle);
        var loop = document.Diagnostics.Should().ContainSingle(diagnostic => diagnostic.Code == PdfDiagnosticCodes.XRefChainCycle).Which;
        loop.Position.Should().Be(middle);
        loop.Message.Should().Be(Invariant(
            $"The cross-reference chain loops back on itself: the /Prev of the section at offset {oldest} names offset {middle - 1}, which the chain has already read."));
        var findings = new PdfValidator().Validate(document).Findings;
        findings.Single(finding => finding.RuleId == PdfValidationRuleIds.XRefChainLoop).Message.Should().Be(Invariant(
            $"The /Prev of the cross-reference section at offset {oldest} names offset {middle - 1}, a section the chain has already read: the chain loops."));
        findings.Should().NotContain(finding => finding.Message.Contains(Invariant($"at offset {middle - 1} gives /Size"), StringComparison.Ordinal));
        findings.Single(finding => finding.RuleId == PdfValidationRuleIds.XRefOffsetImprecise).Message.Should().Be(Invariant(
            $"An offset names the white space before what it designates rather than its first byte: /Prev gives offset {middle - 1}, 1 byte before the xref keyword."));
    }

    [Fact]
    public void An_xrefstm_copied_a_few_bytes_off_the_table_the_prev_names_is_judged_at_each_naming_by_what_it_reaches()
    {
        // The middle table and the newest both name through /XRefStm the endobj 7 bytes before the middle table, which the
        // newest's /Prev names: the search near that offset comes to the middle table, which is left to the /Prev, read
        // whole. Named again by the middle table, the offset leads to that table itself, as the middle table's own offset
        // would. It was looked near again, the middle table passed over as read, and the oldest table read in the stream's
        // place, which the middle table's /Prev then looped back to (#156).
        var (file, oldest, middle, newest) = HybridFiles.CopyingAnXRefStmOffThePreviousTable();
        using var document = PdfDocument.Open(file);

        var sections = document.Reader.Structure.Sections;
        sections.Select(section => (section.NamedBy, section.State, section.Offset)).Should().Equal(
            ("startxref", XRefSectionState.Read, newest),
            ("/XRefStm", XRefSectionState.NamedAgain, middle - 7),
            ("/Prev", XRefSectionState.Read, middle),
            ("/XRefStm", XRefSectionState.NamedAgain, middle - 7),
            ("/Prev", XRefSectionState.Read, oldest));
        sections[1].ReadAs.Should().BeSameAs(sections[2]);
        sections[3].ReadAs.Should().BeSameAs(sections[2]);
        document.Reader.Structure.LoopOffset.Should().Be(-1);
        document.Diagnostics.Select(diagnostic => diagnostic.Message).Should().SatisfyRespectively(
            first => first.Should().StartWith(Invariant(
                $"The /XRefStm of the cross-reference table at offset {newest} names offset {middle - 7}, what its /Prev names too, which the chain looks for through that /Prev: ")),
            second => second.Should().StartWith(Invariant(
                $"The /XRefStm of the cross-reference table at offset {middle} names offset {middle - 7}, the table itself: ")));
        document.GetObject(new PdfObjectId(3)).AsDictionary()!.IsOfType(PdfName.Page).Should().BeTrue();
        document.WasRepaired.Should().BeFalse();
    }

    [Fact]
    public void A_prev_and_an_xrefstm_writing_one_offset_short_of_the_previous_section_count_once_among_the_imprecise_offsets()
    {
        // The newest table names the section before it, through /Prev and through /XRefStm, at the end of line before it: one
        // offset, written twice, names the white space before the section, counted once, as an offset a table copies from
        // another is (#156).
        var built = HybridFiles.NamingThePreviousTable();
        var previous = PreviousSection(built, stream: false);
        var file = Encoding.Latin1.GetBytes(Encoding.Latin1.GetString(built).Replace(
            Invariant($"/Prev {previous} /XRefStm {previous} >>"), Invariant($"/Prev {previous - 1} /XRefStm {previous - 1} >>"), StringComparison.Ordinal));
        using var document = PdfDocument.Open(file);

        var sections = document.Reader.Structure.Sections;
        sections[1].State.Should().Be(XRefSectionState.NamedAgain);
        sections[1].ReadAs.Should().BeSameAs(sections[2]);
        sections[2].Padding.Should().Be(1);
        new PdfValidator().Validate(document).Findings.Single(finding => finding.RuleId == PdfValidationRuleIds.XRefOffsetImprecise).Message.Should().Be(Invariant(
            $"An offset names the white space before what it designates rather than its first byte: /Prev gives offset {previous - 1}, 1 byte before the xref keyword."));
    }

    [Fact]
    public void An_xrefstm_naming_again_the_white_space_before_a_section_found_moved_names_it_short()
    {
        // The middle table, which the newest names 3 bytes short of it, is found moved; the oldest's /XRefStm names the end
        // of line before it, where it lies: a naming again, through the white space before the section, counted among the
        // imprecise offsets as the section's own would be (#156).
        var built = HybridFiles.OldestNaming("0000000000");
        var middle = PdfTemplate.OffsetOf(built, "\nxref\n", 2) + 1;
        var text = Encoding.Latin1.GetString(built)
            .Replace(Invariant($"/Prev {middle} >>"), Invariant($"/Prev {middle - 3} >>"), StringComparison.Ordinal)
            .Replace("/XRefStm 0000000000 >>", Invariant($"/XRefStm {middle - 1:D10} >>"), StringComparison.Ordinal);
        using var document = PdfDocument.Open(Encoding.Latin1.GetBytes(text));

        var sections = document.Reader.Structure.Sections;
        sections[1].State.Should().Be(XRefSectionState.Relocated);
        var again = sections.Should().ContainSingle(section => section.State == XRefSectionState.NamedAgain).Which;
        again.ReadAs.Should().BeSameAs(sections[1]);
        again.Padding.Should().Be(1);
        new PdfValidator().Validate(document).Findings.Single(finding => finding.RuleId == PdfValidationRuleIds.XRefOffsetImprecise).Message.Should().Be(Invariant(
            $"An offset names the white space before what it designates rather than its first byte: /XRefStm gives offset {middle - 1}, 1 byte before the xref keyword."));
    }

    [Fact]
    public void A_prev_looping_back_through_the_white_space_before_a_section_named_short_counts_as_a_second_imprecise_offset()
    {
        // startxref names the stream two bytes short of its header, where the chain reads it, and the stream's /Prev names it
        // one byte short: a loop at the stream, and two offsets that name the white space before it (#188).
        var (file, stream) = NearbyFiles.StreamNamedShortTwice();
        using var document = PdfDocument.Open(file);

        document.Reader.Structure.Sections.Should().ContainSingle().Which.Padding.Should().Be(2);
        document.Reader.Structure.LoopOffset.Should().Be(stream);
        new PdfValidator().Validate(document).Findings.Single(finding => finding.RuleId == PdfValidationRuleIds.XRefOffsetImprecise).Message.Should().Be(Invariant(
            $"2 offsets name the white space before what they designate rather than its first byte; the first: startxref gives offset {stream - 2}, 2 bytes before the header of the cross-reference stream."));
    }

    [Fact]
    public void An_object_that_only_gives_a_width_is_no_section_the_chain_reached_and_a_naming_short_of_it_is_looked_for_near_where_it_names()
    {
        // The newest table's /XRefStm names the object, which may be anything (#263): nothing near it reads, and it is
        // reported. The older table's names the spaces 600 bytes before it, where the search comes to cross-reference stream
        // 7, 400 bytes further back, and reads it in the stream's place: the object is no section the chain reached, so that
        // a naming through the white space before it is a guess of its own (#156).
        var (file, stream, named) = NearbyFiles.ObjectGivingOnlyAWidthNamedTwice();
        using var document = PdfDocument.Open(file);

        var sections = document.Reader.Structure.Sections;
        sections.Select(section => (section.NamedBy, section.State)).Should().Equal(
            ("startxref", XRefSectionState.Read),
            ("/XRefStm", XRefSectionState.Malformed),
            ("/Prev", XRefSectionState.Read),
            ("/XRefStm", XRefSectionState.Relocated),
            ("/Prev", XRefSectionState.Read));
        sections[1].Offset.Should().Be(named);
        sections[3].Offset.Should().Be(stream);
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(PdfDiagnosticCodes.XRefSectionUnreadable, PdfDiagnosticCodes.XRefOffsetAdjusted);
        Text(document, 6).Should().Be("six");
        document.WasRepaired.Should().BeFalse();
    }

    [Fact]
    public void A_section_giving_rows_of_a_reserved_type_is_reported_once_its_first_row_named_and_its_rows_counted()
    {
        // The update gives object 3 and objects 7 to 1,006 rows of type 3: each reads as a reference to the null object, as
        // ISO 32000-1 reads it, its number indexed as a free one's is, and the section is reported once, its count grouped by
        // thousands (#221). They were skipped in silence, the numbers left out of the index.
        var (file, _, newer) = ReservedRowFiles.Updated(type: 3, more: 1000);
        using var document = PdfDocument.Open(file);

        var report = document.Diagnostics.Should().ContainSingle().Which;
        report.Code.Should().Be(PdfDiagnosticCodes.XRefEntryReserved);
        report.Severity.Should().Be(PdfDiagnosticSeverity.Warning);
        report.Position.Should().Be(newer);
        report.Message.Should().Be(Invariant(
            $"The cross-reference stream startxref names at offset {newer} gives 1,001 rows a reserved type: the row for object 3 gives it type 3, the first of them. ISO 32000-1 defines types 0, 1 and 2, and reads any other as a reference to the null object (7.5.8.3): the rows were read so, and no older section's row stands for their objects."));
        document.ObjectCount.Should().Be(1007);
        document.ObjectNumbers.Should().Contain(1006);
        document.Reader.Index.TryGet(1006, out var last).Should().BeTrue();
        last.Kind.Should().Be(XRefEntryKind.Free);
        document.GetObject(new PdfObjectId(1006)).Should().BeSameAs(PdfNull.Instance);
        document.WasRepaired.Should().BeFalse();
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(1UL, "new three")]
    public void A_row_of_a_reserved_type_in_a_section_prev_names_is_reported_at_that_section_whatever_a_newer_row_gives(
        ulong? newer, string? expected)
    {
        // The original stream gives object 3 a row of type 9. Where the update gives it no row, the row stands, and object 3
        // reads as null; where the update places a newer revision, that row stands. Either way the original's row is the
        // file's, and reported at the section the /Prev names (#221).
        var (file, older, _) = ReservedRowFiles.Updated(newer, newer: newer is not null, olderType: 9);
        using var document = PdfDocument.Open(file);
        var three = document.GetObject(new PdfObjectId(3));

        var report = document.Diagnostics.Should().ContainSingle().Which;
        report.Code.Should().Be(PdfDiagnosticCodes.XRefEntryReserved);
        report.Position.Should().Be(older);
        report.Message.Should().StartWith(Invariant(
            $"The cross-reference stream /Prev names at offset {older} gives 1 row a reserved type: the row for object 3 gives it type 9. "));

        if (expected is null)
        {
            three.Should().BeSameAs(PdfNull.Instance);
        }
        else
        {
            three.Should().BeOfType<PdfString>().Which.ToText().Should().Be(expected);
        }
    }

    [Fact]
    public void A_row_of_a_reserved_type_in_a_hybrid_file_s_stream_reads_as_null_over_the_older_table_s_row()
    {
        // The update's stream, which its table names through /XRefStm, gives object 3 a row of type 3; the original table
        // places object 3. The stream's row stands, a reference to the null object, and is reported at the stream: the
        // original revision, which the update superseded, was served (#221).
        var (file, stream, _) = ReservedRowFiles.Hybrid();
        using var document = PdfDocument.Open(file);

        var report = document.Diagnostics.Should().ContainSingle().Which;
        report.Code.Should().Be(PdfDiagnosticCodes.XRefEntryReserved);
        report.Position.Should().Be(stream);
        report.Message.Should().StartWith(Invariant(
            $"The cross-reference stream /XRefStm names at offset {stream} gives 1 row a reserved type: the row for object 3 gives it type 3. "));
        document.GetObject(new PdfObjectId(3)).Should().BeSameAs(PdfNull.Instance);
        document.WasRepaired.Should().BeFalse();
    }

    [Fact]
    public void A_row_of_a_reserved_type_in_a_section_found_moved_is_reported_where_the_section_was_found()
    {
        // The update's /Prev names the original stream four bytes past its header, where the chain finds it moved: what its
        // rows gave is carried to the section where it was named, and reported where it was found (#221).
        var (file, older, _) = ReservedRowFiles.Updated(type: null, olderType: 3, prevShift: 4);
        using var document = PdfDocument.Open(file);

        document.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefEntryReserved);
        document.Diagnostics[1].Position.Should().Be(older);
        var section = document.Reader.Structure.Sections[1];
        section.State.Should().Be(XRefSectionState.Relocated);
        section.ReservedRow.Should().Be("the row for object 3 gives it type 3");
        section.ReservedRows.Should().Be(1);
        new PdfValidator().Validate(document).Findings.Should().ContainSingle(finding => finding.RuleId == PdfValidationRuleIds.XRefEntryTypeReserved)
            .Which.Location.Position.Should().Be(older);
    }

    [Fact]
    public void A_section_read_in_part_reports_its_rows_of_a_reserved_type_after_what_it_lost()
    {
        // The update declares rows for objects 7 to 9 and holds one of them: read only in part, it is reported so first, then
        // for the rows of a reserved type it holds, object 3's and object 7's (#210, #221). The index it left incomplete is
        // rebuilt as the validation starts, which reads object 3 as the scan finds it, the original revision's: the finding
        // says the reader read the rows as the null object only until then.
        var (file, _, newer) = ReservedRowFiles.Updated(type: 3, more: 1, missing: 2);
        using var document = PdfDocument.Open(file);

        document.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.XRefSectionUnreadable, PdfDiagnosticCodes.XRefEntryReserved);
        document.Diagnostics.Should().OnlyContain(entry => entry.Position == newer);
        document.Diagnostics[1].Message.Should().Contain(" gives 2 rows a reserved type: the row for object 3 gives it type 3, the first of them. ");
        document.GetObject(new PdfObjectId(3)).Should().BeSameAs(PdfNull.Instance);
        document.WasRepaired.Should().BeFalse();
        new PdfValidator().Validate(document).Findings.Should().ContainSingle(finding => finding.RuleId == PdfValidationRuleIds.XRefEntryTypeReserved)
            .Which.Message.Should().Be(Invariant(
                $"The cross-reference stream at offset {newer} gives 2 rows a reserved type: the row for object 3 gives it type 3, the first of them. ISO 32000-1 defines types 0, 1 and 2, and reads any other as a reference to the null object (7.5.8.3), as the reader did until it rebuilt the index by scanning the file; it now reads each object such a row numbers as the rebuild finds it."));
        document.WasRepaired.Should().BeTrue();
        document.GetObject(new PdfObjectId(3)).Should().BeOfType<PdfString>().Which.ToText().Should().Be("three");
    }

    [Theory]
    [InlineData("/X", 1, "", ", but its number was refused before the row was read: it stands for nothing, and its object is looked for by rebuilding the index", 1, "new three")]
    [InlineData("[3 1]", 1, "", ", but its number was refused before the row was read: it stands for nothing, and its object is looked for by rebuilding the index", 1, "new three")]
    [InlineData(null, 1, "3 1\n{three} 65536 n \n", ", but its number was refused before the row was read: it stands for nothing, and its object is looked for by rebuilding the index", 1, "new three")]
    [InlineData("[3 2]", 2, "", ", but their numbers were refused before the rows were read: they stand for nothing, and their objects are looked for by rebuilding the index", 2, "new three")]
    [InlineData("[3 1]", 2, "", ": the rows were read so but for those whose numbers were refused before they were read, which stand for nothing and whose objects are looked for by rebuilding the index; no older section's row stands for the objects of the others", 1, "new three")]
    [InlineData("[10 1]", 1, "", ": the row was read so, and no older section's row stands for its object", 0, null)]
    [InlineData(null, 1, "3 1\n{three} 00000 n \n", ": the row was read so, and no older section's row stands for its object", 0, "new three")]
    [InlineData(null, 1, "", ", but its number was refused before the row was read: it stands for nothing, and its object is looked for by rebuilding the index", 1, "new three", true)]
    [InlineData(null, 1, "", ": the row was read so, and no older section's row stands for its object", 0, null, false)]
    public void A_row_of_a_reserved_type_whose_number_was_refused_before_it_is_said_to_stand_for_nothing(
        string? lost, int reserved, string newest, string said, int refused, string? three, bool? refusedRowFirst = null)
    {
        // A cross-reference stream gives object 3, and object 4 after it, a row of type 7, which ISO 32000-1 reads as a
        // reference to the null object; a newer section refused object 3's number first: a lost stream declaring every
        // number, objects 3 and 4, or object 3, or the newest table's own row for it, refused alone. The row did not stand,
        // its object read through a rebuild, though the report said it was read so: it says such rows stand for nothing —
        // the row, both rows, or, where only object 3's number was refused, those whose numbers were refused, which it
        // neither names nor counts (#208, #210, #221). Where the lost stream declares object 10, the row stands, and object
        // 3 reads as null; where the newest table places object 3, that row supersedes it, as any newer row does.
        // Where the stream's own earlier row for object 3, which it refused, refused the number, the row stands for nothing
        // too, in words that name no newer section, and no newer section is said to have refused it; with the rows the other
        // way round, the row of type 7 stands, and object 3 reads as null.
        var (file, middle) = ReservedRowFiles.PastARefusal(lost, reserved, newest, refusedRowFirst: refusedRowFirst);

        using var document = PdfDocument.Open(file);

        var many = reserved > 1;
        document.Diagnostics.Should().ContainSingle(d => d.Code == PdfDiagnosticCodes.XRefEntryReserved).Which.Message.Should().Be(Invariant(
            $"The cross-reference stream /Prev names at offset {middle} gives {(many ? "2 rows" : "1 row")} a reserved type: the row for object 3 gives it type 7{(many ? ", the first of them" : string.Empty)}. ISO 32000-1 defines types 0, 1 and 2, and reads any other as a reference to the null object (7.5.8.3){said}."));
        var section = document.Reader.Structure.Sections.Single(s => s.Offset == middle);
        section.ReservedRowsRefused.Should().Be(refused);
        section.RowsRefusedBefore.Should().Be(refused > 0 && refusedRowFirst is null);
        var index = document.Reader.ChainIndex!;
        index.TryGet(3, out _).Should().Be(refused == 0);

        if (many && refused == 1)
        {
            // Object 4's row, whose number no newer section refused, stands as the null object.
            index.TryGet(4, out var four).Should().BeTrue();
            four.Kind.Should().Be(XRefEntryKind.Free);
        }

        if (three is null)
        {
            document.GetObject(new PdfObjectId(3)).Should().BeSameAs(PdfNull.Instance);
        }
        else
        {
            Text(document, 3).Should().Be(three);
        }

        document.WasRepaired.Should().Be(refused > 0);
    }

    [Fact]
    public void A_row_of_a_reserved_type_whose_number_was_refused_before_it_is_said_to_stand_for_nothing_where_its_stream_was_found_moved()
    {
        // As above, the lost stream refusing every number, its /Prev naming the stream four bytes past its header, where the
        // chain finds it moved: what its rows gave, the row a newer refusal set aside counted, is carried to the section where
        // it was named, and reported where it was found (#221).
        var (file, middle) = ReservedRowFiles.PastARefusal("/X", shift: 4);

        using var document = PdfDocument.Open(file);

        var section = document.Reader.Structure.Sections.Single(s => s.Offset == middle);
        section.State.Should().Be(XRefSectionState.Relocated);
        section.ReservedRowsRefused.Should().Be(1);
        document.Diagnostics.Should().ContainSingle(d => d.Code == PdfDiagnosticCodes.XRefEntryReserved).Which.Message.Should().Be(Invariant(
            $"The cross-reference stream /Prev names at offset {middle + 4}, found at offset {middle}, gives 1 row a reserved type: the row for object 3 gives it type 7. ISO 32000-1 defines types 0, 1 and 2, and reads any other as a reference to the null object (7.5.8.3), but its number was refused before the row was read: it stands for nothing, and its object is looked for by rebuilding the index."));
    }

    /// <summary>Where the first object starts: after the header line and the comment of binary bytes.</summary>
    private const long FirstObjectOffset = 15;

    /// <summary>The sound document, saved once more: an update adding object 4, its <c>/Prev</c> naming the first table.</summary>
    private const string SoundUpdated = PdfTemplate.Sound + """
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

    /// <summary>
    /// What follows the word, or the header, in the data of the embedded file that
    /// <see cref="NearbyFiles.UpdatePastAWordInStreamData"/> and <see cref="NearbyFiles.HybridPastAWordInStreamData"/> write:
    /// the rest of a line of notes, a line more and a comment, which put the section past the data farther from the place
    /// named than the word is, within the search near it.
    /// </summary>
    private static readonly string StreamDataAfterTheWord = " tables list offsets; see section 7.5.4.\nEnd of notes.\n%" + new string('x', 60);

    private enum Offset
    {
        Section,
        FirstObject,
        PastTheEnd,
        AsReference,
    }

    private enum XRefStm
    {
        None,
        Nowhere,
        Present,
    }

    /// <summary>
    /// Builds the document and its update. Returns the file, and where the section the test is about really
    /// starts: the original section, or the update's cross-reference stream. <paramref name="originalAsStream"/> writes the
    /// original section as a cross-reference stream. The update's sections lie within 512 bytes of the original's.
    /// </summary>
    private static (byte[] File, long Section) Updated(
        int prevError = 0,
        Offset prevAt = Offset.Section,
        XRefStm xrefStm = XRefStm.None,
        int xrefStmError = 0,
        bool originalAsStream = false)
    {
        // Filler keeps the original section more than half a kilobyte from the first object.
        var builder = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .WithObject(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>")
            .WithObject(4, "(original)")
            .WithObject(5, $"({new string('x', 1024)})");
        var original = originalAsStream ? builder.BuildWithXRefStream(rootNumber: 1) : builder.BuildClassic(rootNumber: 1);
        var originalSection = StartXRef(original);
        original.AsSpan().IndexOf("1 0 obj"u8).Should().Be((int)FirstObjectOffset);

        var file = new List<byte>(original);
        var offsets = new SortedDictionary<int, long>();

        void Append(string text) => file.AddRange(Encoding.Latin1.GetBytes(text));

        offsets[1] = file.Count;
        Append("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        offsets[4] = file.Count;
        Append("4 0 obj\n(updated)\nendobj\n");

        var stream = -1L;
        if (xrefStm == XRefStm.Present)
        {
            var sixth = file.Count;
            Append("6 0 obj\n(only in the stream)\nendobj\n");
            stream = file.Count;
            Append("7 0 obj\n<< /Type /XRef /W [1 4 2] /Size 8 /Index [6 1] /Length 7 >>\nstream\n");
            file.AddRange([1, (byte)(sixth >> 24), (byte)(sixth >> 16), (byte)(sixth >> 8), (byte)sixth, 0, 0]);
            Append("\nendstream\nendobj\n");
        }

        var xref = file.Count;
        Append("xref\n");

        foreach (var (number, offset) in offsets)
        {
            Append(string.Create(CultureInfo.InvariantCulture, $"{number} 1\n{offset:D10} 00000 n\r\n"));
        }

        var prev = prevAt switch
        {
            Offset.FirstObject => FirstObjectOffset.ToString(CultureInfo.InvariantCulture),
            Offset.PastTheEnd => "99999999",
            Offset.AsReference => string.Create(CultureInfo.InvariantCulture, $"{originalSection} 0 R"),
            _ => (originalSection + prevError).ToString(CultureInfo.InvariantCulture),
        };
        var hybrid = xrefStm switch
        {
            XRefStm.Nowhere => string.Create(CultureInfo.InvariantCulture, $" /XRefStm {FirstObjectOffset}"),
            XRefStm.Present => string.Create(CultureInfo.InvariantCulture, $" /XRefStm {stream + xrefStmError}"),
            _ => string.Empty,
        };
        Append(string.Create(
            CultureInfo.InvariantCulture,
            $"trailer\n<< /Size 8 /Root 1 0 R /Prev {prev}{hybrid} >>\nstartxref\n{xref}\n%%EOF\n"));

        return ([.. file], xrefStm == XRefStm.Present ? stream : originalSection);
    }

    /// <summary>
    /// Builds a file of two classic tables that name each other through <c>/Prev</c>, and returns it with where the
    /// newest and the older table start.
    /// </summary>
    private static (byte[] File, long Newest, long Older) Looping()
    {
        const string Objects = "%PDF-1.7\n1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n";
        var older = Objects.Length;
        var catalog = Objects.IndexOf("1 0 obj", StringComparison.Ordinal);
        var pages = Objects.IndexOf("2 0 obj", StringComparison.Ordinal);
        string Older(long newest) => string.Create(
            CultureInfo.InvariantCulture,
            $"xref\n0 3\n0000000000 65535 f\r\n{catalog:D10} 00000 n\r\n{pages:D10} 00000 n\r\ntrailer\n<< /Size 3 /Root 1 0 R /Prev {newest:D10} >>\n");
        var newest = older + Older(0).Length;
        var text = Objects + Older(newest) + string.Create(
            CultureInfo.InvariantCulture,
            $"xref\n0 1\n0000000000 65535 f\r\ntrailer\n<< /Size 3 /Root 1 0 R /Prev {older} >>\nstartxref\n{newest}\n%%EOF\n");
        return (Encoding.Latin1.GetBytes(text), newest, older);
    }

    /// <summary>
    /// The catalog, an empty page tree and object 3, <c>(old three)</c>, in a classic table; then an update that redefines
    /// object 3, <c>(new three)</c>, in a cross-reference stream, object 4, whose Flate rows are 4's and then 3's, seven
    /// bytes each. The stream is the update's section, or, when <paramref name="hybrid"/>, the /XRefStm of a classic table
    /// that holds only row 0. Its dictionary starts with <paramref name="pad"/>, when one is given.
    /// </summary>
    private static byte[] UpdatedThroughAStream(bool hybrid, string pad = "")
    {
        var original = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(3, "(old three)")
            .BuildClassic(rootNumber: 1);
        var previous = StartXRef(original);
        var file = new List<byte>(original);

        void Append(string text) => file.AddRange(Encoding.Latin1.GetBytes(text));

        var three = file.Count;
        Append("3 0 obj\n(new three)\nendobj\n");
        var stream = file.Count;

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write([1, (byte)(stream >> 24), (byte)(stream >> 16), (byte)(stream >> 8), (byte)stream, 0, 0]);
            zlib.Write([1, (byte)(three >> 24), (byte)(three >> 16), (byte)(three >> 8), (byte)three, 0, 0]);
        }

        var link = hybrid ? string.Empty : string.Create(CultureInfo.InvariantCulture, $" /Prev {previous} /Root 1 0 R");
        Append(string.Create(
            CultureInfo.InvariantCulture,
            $"4 0 obj\n<<{pad} /Type /XRef /Size 5 /W [1 4 2] /Index [4 1 3 1]{link} /Filter /FlateDecode /Length {compressed.Length} >>\nstream\n"));
        file.AddRange(compressed.ToArray());
        Append("\nendstream\nendobj\n");

        if (!hybrid)
        {
            Append(string.Create(CultureInfo.InvariantCulture, $"startxref\n{stream}\n%%EOF\n"));
            return [.. file];
        }

        var table = file.Count;
        Append(string.Create(
            CultureInfo.InvariantCulture,
            $"xref\n0 1\n0000000000 65535 f\r\ntrailer\n<< /Size 5 /Root 1 0 R /Prev {previous} /XRefStm {stream} >>\nstartxref\n{table}\n%%EOF\n"));
        return [.. file];
    }

    /// <summary>Checks the objects each section alone indexes: the update's catalog and object 4, the original's page tree.</summary>
    private static void ReadsWhole(PdfDocument document)
    {
        document.Catalog.GetDictionary(PdfName.Pages)!.GetArray(PdfName.Kids)!.Resolved(0).AsDictionary()!
            .IsOfType(PdfName.Page).Should().BeTrue();
        Text(document, 4).Should().Be("updated");
        Text(document, 5).Should().HaveLength(1024);
    }

    private static Config Properties =>
        Config.QuickThrowOnFailure.WithMaxTest(200).WithReplay(0x5EED_1729UL, 0x9E37_79B9_7F4A_7C15UL).WithQuietOnSuccess(true);

    private static string Text(PdfDocument document, int number) =>
        document.GetObject(new PdfObjectId(number)).Should().BeOfType<PdfString>().Subject.ToText();

    /// <summary>
    /// Asserts that the chain's index of a file <see cref="PartialFiles.PastALostStream"/> writes holds a row for objects 3
    /// and 4 as <paramref name="three"/> and <paramref name="four"/> say, and that each reads as its newer revision, through
    /// its row, or through a rebuild of the index where no row stands for it, which nothing before asked for.
    /// </summary>
    private static void ShouldServeTheNewerRevisions(PdfDocument document, bool three, bool four)
    {
        var index = document.Reader.ChainIndex!;
        index.TryGet(3, out _).Should().Be(three);
        index.TryGet(4, out _).Should().Be(four);
        document.WasRepaired.Should().BeFalse();
        Text(document, 3).Should().Be("new three");
        Text(document, 4).Should().Be("new four");
        document.WasRepaired.Should().Be(!(three && four));
    }

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Gives what <paramref name="read"/> returns, run on a thread of its own, and fails where it has not returned within half
    /// a minute: a reading that never ends fails the test rather than stalling the run, its thread left to the process.
    /// </summary>
    private static async Task<T> Within<T>(Func<T> read)
    {
        var reading = Task.Factory.StartNew(read, TestContext.Current.CancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        var first = await Task.WhenAny(reading, Task.Delay(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
        ReferenceEquals(first, reading).Should().BeTrue("the reading ends, whatever the source gives");
        return await reading;
    }

    /// <summary>
    /// Gives a file with no header, whose offsets count from its first byte, and where its second table lies: a classic
    /// table at offset 0 that indexes the catalog and an empty page tree, written after it, and gives <paramref name="first"/>
    /// in its trailer, <c>{second}</c> standing for the second table's offset; then a table that indexes nothing and gives
    /// <paramref name="second"/>. <c>startxref</c> names the first table where <paramref name="startAtFirst"/>, the second
    /// otherwise. Offsets are written in ten digits, so that a stand-in moves nothing.
    /// </summary>
    private static (byte[] File, long Second) Headerless(string first, string second, bool startAtFirst)
    {
        // Built once with stand-ins as long as what they stand for, to find where each object and the second table lie.
        var stand = Build(0, 0, 0);
        var text = Encoding.Latin1.GetString(stand);
        var catalog = text.IndexOf("1 0 obj", StringComparison.Ordinal);
        var pages = text.IndexOf("2 0 obj", StringComparison.Ordinal);
        var at = text.LastIndexOf("\nxref\n", StringComparison.Ordinal) + 1;
        var file = Build(catalog, pages, at);
        file.Should().HaveCount(stand.Length);
        return (file, at);

        byte[] Build(long catalogAt, long pagesAt, long secondAt) => Encoding.Latin1.GetBytes(Invariant(
            $"xref\n0 3\n0000000000 65535 f \n{catalogAt:D10} 00000 n \n{pagesAt:D10} 00000 n \ntrailer\n<< /Size 3 /Root 1 0 R{first.Replace("{second}", Invariant($"{secondAt:D10}"), StringComparison.Ordinal)} >>\n1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\nxref\n0 1\n0000000000 65535 f \ntrailer\n<< /Size 3 /Root 1 0 R{second} >>\nstartxref\n{(startAtFirst ? 0 : secondAt):D10}\n%%EOF\n"));
    }

    /// <summary>
    /// Gives a file whose section <c>startxref</c> names, of <paramref name="kind"/>, gives a <c>/Prev</c> that names the end
    /// of line before it, and where that section starts: an update's table that holds no subsection (#222), a table whose
    /// last row ends in <c>x</c> (#210), or an update's cross-reference stream whose row for object 3 is of type 7 (#221).
    /// Each is built once to find where the section lies, then again with its <c>/Prev</c>, which nothing before it moves.
    /// </summary>
    private static (byte[] File, long At) SectionNamingItsOwnEndOfLine(string kind)
    {
        if (kind == "an empty table")
        {
            var built = PdfTemplate.Build(PartialFiles.EmptyUpdate(string.Empty));
            var first = PdfTemplate.OffsetOf(built, "\nxref\n") + 1;
            var at = PdfTemplate.OffsetOf(built, "\nxref\n", 2) + 1;
            var file = Encoding.Latin1.GetBytes(Encoding.Latin1.GetString(built).Replace(
                Invariant($"/Prev {first} >>"), Invariant($"/Prev {at - 1} >>"), StringComparison.Ordinal));
            file.Should().HaveCount(built.Length, "the offsets are written in as many digits");
            return (file, at);
        }

        if (kind == "a table read in part")
        {
            var at = PdfTemplate.OffsetOf(PdfTemplate.Build(PartialFiles.RowEndingInX(updated: false)), "\nxref\n") + 1;
            var file = PdfTemplate.Build(PartialFiles.RowEndingInX(updated: false).Replace(
                "<< /Size 5 /Root 1 0 R >>", Invariant($"<< /Size 5 /Root 1 0 R /Prev {at - 1} >>"), StringComparison.Ordinal));
            return (file, at);
        }

        var (_, older, newer) = ReservedRowFiles.Updated(type: 7);
        return (ReservedRowFiles.Updated(type: 7, prevShift: (int)(newer - 1 - older)).File, newer);
    }

    /// <summary>Gives where <paramref name="text"/> first appears in <paramref name="file"/> at or after <paramref name="start"/>.</summary>
    private static long OffsetAfter(byte[] file, long start, string text) =>
        start + Encoding.Latin1.GetString(file, (int)start, file.Length - (int)start).IndexOf(text, StringComparison.Ordinal);

    private static long StartXRef(byte[] file)
    {
        var text = Encoding.Latin1.GetString(file);
        var start = text.LastIndexOf("startxref", StringComparison.Ordinal) + "startxref".Length;
        return long.Parse(text[start..].Trim().Split('\n')[0], CultureInfo.InvariantCulture);
    }
}
