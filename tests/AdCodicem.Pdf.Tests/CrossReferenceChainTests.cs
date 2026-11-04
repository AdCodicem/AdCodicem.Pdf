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
        // The offset a /Prev gives counts from the header; the report gives a position in the file (#187).
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
    }

    [Fact]
    public void Places_a_loop_back_to_a_section_named_outside_the_file_at_what_named_it()
    {
        // An /XRefStm and a /Prev naming one offset past the end: the /Prev loops back to what the /XRefStm named,
        // and the report goes to the section whose trailer names both, a position in the file (#187).
        var (file, _) = Updated(prevAt: Offset.PastTheEnd);
        var text = Encoding.Latin1.GetString(file).Replace("/Prev 99999999", "/Prev 99999999 /XRefStm 99999999", StringComparison.Ordinal);
        var section = text.LastIndexOf("\nxref", StringComparison.Ordinal) + 1;

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
    [InlineData(false, "which the chain has already named")]
    [InlineData(true, "which the chain has already read")]
    public void The_prev_a_lost_stream_hands_on_ends_the_chain_in_a_loop_where_it_names_what_the_chain_reached(bool newest, string reached)
    {
        // The stream's /Prev names the stream itself, which the chain named and could not read, or the newest table, which it
        // read: a loop, reported as one, and the objects the chain lost are found by rebuilding the index (#208).
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
        document.Reader.Structure.LoopOffsetRead.Should().BeTrue();
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
        // The newest section indexes nothing and fits the 16 bytes tables are read to; the original's does not reach
        // its first row within them. The guard's report names the limit that lifts it: the section is not reported again.
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
                $"%{new string('-', 600)}\nxref\ntrailer\n<< /Size 6 /Root 1 0 R /Prev {section} >>\nstartxref\n{update}\n%%EOF\n")),
        ];
        var options = PdfReaderOptions.Default with { Limits = PdfReaderLimits.Default with { MaxXRefSectionLength = 16 } };

        using var document = PdfDocument.Open(file, options);

        // The catalog only the original indexes is needed at opening: the index is rebuilt there.
        document.Diagnostics.Select(d => d.Code).Should().Equal(PdfDiagnosticCodes.LimitXRefSectionLength, PdfDiagnosticCodes.XRefRebuilt);
        document.Diagnostics[0].Position.Should().Be(section);
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

    /// <summary>Where the first object starts: after the header line and the comment of binary bytes.</summary>
    private const long FirstObjectOffset = 15;

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
