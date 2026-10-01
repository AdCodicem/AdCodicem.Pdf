using System.Globalization;
using System.Text;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.Objects;

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
    [InlineData(-7)]
    [InlineData(-2)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(12)]
    public void Reads_a_section_that_prev_names_a_few_bytes_off(int error)
    {
        // Twelve bytes past the keyword is where IBM's QMF manual points, inside the first row of the table. One
        // byte before it is the end-of-line the keyword follows, where the table is read as it stands.
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
            $"The cross-reference section /Prev names at offset {original + error} was found {-error} bytes from there."));
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
        var (unreadable, original) = Updated(padding: 600);
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
        // The original table's trailer keyword is a stray name: its rows are read, its trailer is not. The padding
        // keeps the update's table beyond the search near the original's, which would land on it (#188).
        var (file, original) = Updated(padding: 600);
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
    public void Reports_a_table_whose_rows_run_into_a_dictionary_with_no_trailer_keyword_as_unreadable()
    {
        var (file, original) = Updated(padding: 600);
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
        var (file, original) = Updated(padding: 600, originalAsStream: true);
        var text = Encoding.Latin1.GetString(file);
        var widths = text.IndexOf("/W [1 4 2]", StringComparison.Ordinal);
        widths.Should().BeGreaterThan((int)original, "the original's stream comes first");
        file = Encoding.Latin1.GetBytes(string.Concat(text.AsSpan(0, widths), "/W [1 9 2]", text.AsSpan(widths + 10)));

        using var document = PdfDocument.Open(file);

        var report = document.Diagnostics.Should().ContainSingle().Which;
        report.Code.Should().Be(PdfDiagnosticCodes.XRefSectionUnreadable);
        report.Position.Should().Be(original);
        report.Message.Should().StartWith(string.Create(
            CultureInfo.InvariantCulture,
            $"The cross-reference stream /Prev names at offset {original} is there but cannot be read: its /W gives a field a width outside 0 to 8 bytes."));

        document.WasRepaired.Should().BeFalse("nothing asked yet for what the stream indexed");
        ReadsWhole(document);
        document.WasRepaired.Should().BeTrue();
    }

    [Fact]
    public void Reports_a_cross_reference_stream_that_xrefstm_names_and_cannot_be_read_as_unreadable_and_finds_its_objects_when_asked()
    {
        var (file, stream) = Updated(xrefStm: XRefStm.Present, padding: 600);
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
        // tiff2pdf writes /Prev 576066 0 R. A reference names an object, not a section: it is not resolved, which
        // would load an object while the index is still being read, and the section is looked for on demand.
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
    public void Looks_for_an_object_the_index_lacks_when_a_guard_stopped_the_chain()
    {
        // Two saves on top of the original, and a chain read no further than two sections: the original section,
        // which alone indexes the page tree, is left unread by the reader's own limit, so what it held is looked
        // for by rebuilding the index when asked for, rather than read as null.
        var (original, _) = Updated();
        var file = TestPdfBuilder.AppendIncrementalUpdate(original, rootNumber: 1, [(1, "<< /Type /Catalog /Pages 2 0 R >>")]);
        var options = PdfReaderOptions.Default with { Limits = PdfReaderLimits.Default with { MaxXRefSectionCount = 2 } };

        using var document = PdfDocument.Open(file, options);

        document.WasRepaired.Should().BeFalse();
        document.Diagnostics.Select(d => d.Code).Should().Equal(PdfDiagnosticCodes.LimitXRefSectionCount);
        ReadsWhole(document);
        document.WasRepaired.Should().BeTrue();
    }

    [Fact]
    public void Looks_for_an_object_the_index_lacks_when_a_guard_stopped_the_table()
    {
        // Tables read no further than 128 bytes: the update's, which names the catalog, fits; the original's six
        // rows do not, and the last, object 5's, is cut. Object 5 is looked for by rebuilding the index when
        // asked for, rather than read as null.
        var (file, _) = Updated();
        var options = PdfReaderOptions.Default with { Limits = PdfReaderLimits.Default with { MaxXRefSectionLength = 128 } };

        using var document = PdfDocument.Open(file, options);

        document.WasRepaired.Should().BeFalse();
        document.Diagnostics.Select(d => d.Code).Should().Equal(PdfDiagnosticCodes.LimitXRefSectionLength);
        ReadsWhole(document);
        document.WasRepaired.Should().BeTrue();
    }

    [Fact]
    public void Reports_a_cross_reference_stream_whose_dictionary_the_guard_cuts_under_the_guard_alone()
    {
        // The original section's dictionary is longer than the 48 bytes trailers are read to: what the reader makes of
        // the cut — no stream data after it — is not the file's fault, and the guard's report is the only one.
        var (file, _) = Updated(padding: 600, originalAsStream: true);
        var options = PdfReaderOptions.Default with { Limits = PdfReaderLimits.Default with { MaxTrailerLength = 48 } };

        using var document = PdfDocument.Open(file, options);

        document.Diagnostics.Select(d => d.Code).Should().Equal(PdfDiagnosticCodes.LimitTrailer);
        ReadsWhole(document);
        document.WasRepaired.Should().BeTrue();
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
    /// starts: the original section, or the update's cross-reference stream. A <paramref name="padding"/> keeps
    /// each of them more than <c>padding</c> bytes from the section that follows it; <paramref name="originalAsStream"/>
    /// writes the original section as a cross-reference stream.
    /// </summary>
    private static (byte[] File, long Section) Updated(
        int prevError = 0,
        Offset prevAt = Offset.Section,
        XRefStm xrefStm = XRefStm.None,
        int xrefStmError = 0,
        int padding = 0,
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

        // A comment line after each section the test is about keeps the next one out of reach of the search near it.
        void Pad()
        {
            if (padding > 0)
            {
                Append($"%{new string('-', padding)}\n");
            }
        }

        Pad();
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
            Pad();
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

    /// <summary>Checks the objects each section alone indexes: the update's catalog and object 4, the original's page tree.</summary>
    private static void ReadsWhole(PdfDocument document)
    {
        document.Catalog.GetDictionary(PdfName.Pages)!.GetArray(PdfName.Kids)!.Resolved(0).AsDictionary()!
            .IsOfType(PdfName.Page).Should().BeTrue();
        Text(document, 4).Should().Be("updated");
        Text(document, 5).Should().HaveLength(1024);
    }

    private static string Text(PdfDocument document, int number) =>
        document.GetObject(new PdfObjectId(number)).Should().BeOfType<PdfString>().Subject.ToText();

    private static long StartXRef(byte[] file)
    {
        var text = Encoding.Latin1.GetString(file);
        var start = text.LastIndexOf("startxref", StringComparison.Ordinal) + "startxref".Length;
        return long.Parse(text[start..].Trim().Split('\n')[0], CultureInfo.InvariantCulture);
    }
}
