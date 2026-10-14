using System.Globalization;
using System.IO.Compression;
using System.Text;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.IO;
using AdCodicem.Pdf.IO.XRef;
using AdCodicem.Pdf.Objects;
using AdCodicem.Pdf.Validation;

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
    public void Quotes_a_keyword_holding_an_escape_where_a_trailer_should_be_escaped()
    {
        // The original table's trailer keyword holds ESC: the report quotes the keyword the file wrote, escaped (#159).
        var (file, original) = Updated(padding: 600);
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

    [Fact]
    public void Sections_named_a_few_bytes_off_are_looked_for_at_thirty_two_places_a_document_at_most()
    {
        // Forty hybrid sections each name their stream three bytes past its header, which is found there, the nearest
        // place: each search reads a window, and a document's searches stop at the thirty-second, so that the streams of
        // the eight oldest sections are missing rather than looked for.
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
            var stream = text.Length;
            var row = Encoding.Latin1.GetString([1, (byte)(stream >> 24), (byte)(stream >> 16), (byte)(stream >> 8), (byte)stream, 0, 0]);
            text.Append(CultureInfo.InvariantCulture, $"{number} 0 obj\n<< /Type /XRef /Size {Sections + 3} /W [1 4 2] /Index [{number} 1] /Length 7 >>\nstream\n")
                .Append(row).Append("\nendstream\nendobj\n");
            var table = text.Length;
            text.Append("xref\n0 3\n0000000000 65535 f\r\n")
                .Append(CultureInfo.InvariantCulture, $"{catalog:D10} 00000 n\r\n{pages:D10} 00000 n\r\n")
                .Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {Sections + 3} /Root 1 0 R /XRefStm {stream + 3}")
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
        var (file, _) = Updated(padding: 600, originalAsStream: true);
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
