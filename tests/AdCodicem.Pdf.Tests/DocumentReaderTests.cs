using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.IO;
using AdCodicem.Pdf.IO.XRef;
using AdCodicem.Pdf.Objects;
using AdCodicem.Pdf.Validation;

namespace AdCodicem.Pdf.Tests;

public class DocumentReaderTests
{
    [Fact]
    public void Opens_a_document_indexed_by_a_classic_table()
    {
        using var document = PdfDocument.Open(SampleDocument().BuildClassic(rootNumber: 1));

        document.Version.Should().Be("1.7");
        document.WasRepaired.Should().BeFalse();
        document.Catalog.Required().IsOfType(PdfName.Catalog).Should().BeTrue();
        PageContent(document).Should().Be("BT (Bonjour) Tj ET");
    }

    [Theory]
    [InlineData("%PDF-1.7", "1.7")]
    [InlineData("%PDF-2.0", "2.0")]
    [InlineData("%PDF-1.8", "1.8")]
    [InlineData("%PDF-1.4 ", "1.4")]
    [InlineData("%PDF-1.4\r", "1.4")]
    [InlineData("%PDF-1.4\t", "1.4")]
    [InlineData("%PDF-1.4%comment", "1.4")]
    [InlineData("%XYZ-1.7", null)]
    [InlineData("%PDF-x", null)]
    [InlineData("%PDF-", null)]
    [InlineData("%PDF- 1.7", null)]
    [InlineData("%PDF-1.4.5.6", null)]
    [InlineData("%PDF-1.99999999", null)]
    [InlineData("%PDF-1.7abc", null)]
    [InlineData("%PDF-2.0x", null)]
    [InlineData("%PDF-14", null)]
    [InlineData("%PDF-1.", null)]
    [InlineData("%PDF-.5", null)]
    [InlineData("%PDF-01.4", null)]
    public void The_version_is_the_header_s_when_it_names_one_digit_a_period_and_one_digit(string header, string? version)
    {
        using var document = PdfDocument.Open(PdfTemplate.SoundWith("%PDF-1.7", header));

        document.Version.Should().Be(version);
        document.Catalog.Should().NotBeNull("the header names a version or not; the file reads all the same");
    }

    [Fact]
    public void A_version_the_header_search_cuts_is_none()
    {
        // The header's search ends at the 4,096th byte, right after "1.7": what follows it there is not known.
        var file = Encoding.Latin1.GetBytes(new string('%', 4087) + "\n").Concat(PdfTemplate.Build(PdfTemplate.Sound)).ToArray();

        using var document = PdfDocument.Open(file);

        document.Version.Should().BeNull();
        document.Reader.Structure.HeaderVersion.Should().Be("1.7");
        document.Catalog.Should().NotBeNull();
    }

    [Fact]
    public void A_version_the_end_of_the_file_ends_is_one()
    {
        // The header is the file's last bytes: the end of the file ends its version, as white space would.
        var body = PdfTemplate.Build(PdfTemplate.Sound)[9..];
        var file = body.Concat(Encoding.Latin1.GetBytes("%PDF-1.7")).ToArray();

        using var document = PdfDocument.Open(file);

        document.Version.Should().Be("1.7");
        document.Reader.Structure.HeaderVersionCut.Should().BeFalse();
    }

    [Fact]
    public void A_version_that_ends_where_the_header_search_and_the_file_both_end_is_one()
    {
        // 4,096 bytes in all: the version's last digit is the file's last byte and the search window's.
        var body = PdfTemplate.Build(PdfTemplate.Sound)[9..];
        var file = body.Concat(Encoding.Latin1.GetBytes("\n" + new string('%', 4096 - body.Length - 9) + "%PDF-1.7")).ToArray();
        file.Length.Should().Be(4096);

        using var document = PdfDocument.Open(file);

        document.Version.Should().Be("1.7");
    }

    [Theory]
    [InlineData(9, "/1.7", "1.7")]
    [InlineData(9, "(1.7)", "1.4")]
    [InlineData(8, "/1.7", "1.4")]
    public void A_catalog_s_version_written_as_a_reference_is_read_as_the_document_opens(int named, string value, string version)
    {
        var file = new TestPdfBuilder()
            .WithObject(1, $"<< /Type /Catalog /Pages 2 0 R /Version {named} 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .WithObject(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>")
            .WithObject(9, value)
            .BuildClassic(rootNumber: 1);
        "%PDF-1.4"u8.CopyTo(file);

        using var document = PdfDocument.Open(file);

        document.Version.Should().Be(version);
    }

    [Fact]
    public void Without_a_catalog_the_version_is_the_header_s()
    {
        var file = new TestPdfBuilder()
            .WithObject(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 /Version /1.7 >>")
            .WithObject(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>")
            .BuildClassic(rootNumber: 1);
        "%PDF-1.4"u8.CopyTo(file);

        using var document = PdfDocument.Open(file);

        document.Catalog.Should().BeNull("no object is the catalog /Root names");
        document.Version.Should().Be("1.4");
    }

    [Theory]
    [InlineData("%PDF-1.4", "/Version /1.7", "1.7")]
    [InlineData("%PDF-1.3", "/Version /1.6", "1.6")]
    [InlineData("%PDF-1.4", "/Version /2.0", "2.0")]
    [InlineData("%PDF-1.4", "/Version /1.8", "1.8")]
    [InlineData("%PDF-1.7", "/Version /1.4", "1.7")]
    [InlineData("%PDF-1.7", "/Version /1.7", "1.7")]
    [InlineData("%PDF-1.4", "/Version (1.7)", "1.4")]
    [InlineData("%PDF-1.4", "/Version /1.7.1", "1.4")]
    [InlineData("%PDF-1.4", "/Version /17", "1.4")]
    [InlineData("%PDF-1.4", "/Version 1.7", "1.4")]
    [InlineData("%PDF-x", "/Version /1.7", null)]
    [InlineData("%XYZ-1.7", "/Version /1.7", null)]
    public void The_version_is_the_catalog_s_when_it_is_written_as_one_and_later_than_the_header_s(
        string header, string catalog, string? version)
    {
        var file = PdfTemplate.Build(PdfTemplate.Sound
            .Replace("%PDF-1.7", header, StringComparison.Ordinal)
            .Replace("<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Catalog /Pages 2 0 R " + catalog + " >>", StringComparison.Ordinal));

        using var document = PdfDocument.Open(file);

        document.Version.Should().Be(version);
    }

    [Fact]
    public void Opens_a_document_indexed_by_a_cross_reference_stream()
    {
        using var document = PdfDocument.Open(SampleDocument().BuildWithXRefStream(rootNumber: 1));

        document.WasRepaired.Should().BeFalse();
        PageContent(document).Should().Be("BT (Bonjour) Tj ET");
    }

    [Fact]
    public void Reads_objects_packed_into_an_object_stream()
    {
        var bytes = SampleDocument().BuildWithXRefStream(rootNumber: 1, compressedObjects: [1, 2, 3]);

        using var document = PdfDocument.Open(bytes);

        document.WasRepaired.Should().BeFalse();
        document.Catalog.Required().IsOfType(PdfName.Catalog).Should().BeTrue();
        Page(document).GetArray(PdfName.MediaBox).Required().Count.Should().Be(4);
        PageContent(document).Should().Be("BT (Bonjour) Tj ET");
    }

    [Fact]
    public void Finds_objects_whose_recorded_offsets_are_wrong()
    {
        var bytes = SampleDocument().BuildClassic(rootNumber: 1, offsetError: 3);

        using var document = PdfDocument.Open(bytes);

        PageContent(document).Should().Be("BT (Bonjour) Tj ET");
        document.Diagnostics.Contains(PdfDiagnosticCodes.XRefOffsetAdjusted).Should().BeTrue();
    }

    [Theory]
    [InlineData("sv-SE")]
    [InlineData("fa-IR")]
    public void Writes_the_distance_to_an_object_found_before_its_entry_the_same_whatever_the_culture(string name)
    {
        // Every entry points 3 bytes past its object: the report says the object lies before it in words, where it once
        // wrote a minus sign, which Swedish writes as U+2212 and Persian as U+200E U+2212 (#241).
        var bytes = SampleDocument().BuildClassic(rootNumber: 1, offsetError: 3);
        var culture = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(name);
            using var document = PdfDocument.Open(bytes);
            _ = PageContent(document);

            document.Diagnostics.Where(entry => entry.Code == PdfDiagnosticCodes.XRefOffsetAdjusted).Should().NotBeEmpty()
                .And.OnlyContain(entry => entry.Message.EndsWith(" was found 3 bytes before where the index said.", StringComparison.Ordinal));
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    [Theory]
    [InlineData(1, "1 byte before")]
    [InlineData(3, "3 bytes before")]
    [InlineData(-2, "2 bytes after")]
    [InlineData(-3, "3 bytes after")]
    public void Says_which_way_and_how_far_from_its_entry_each_object_was_found(int offsetError, string found)
    {
        // Every entry points past its object, or before it — one byte before is the end-of-line its header follows,
        // which is read as it stands —: each report says the way in words, and a count of one in the singular (#241).
        using var document = PdfDocument.Open(SampleDocument().BuildClassic(rootNumber: 1, offsetError: offsetError));
        _ = PageContent(document);

        document.Diagnostics.Where(entry => entry.Code == PdfDiagnosticCodes.XRefOffsetAdjusted).Select(entry => entry.Message)
            .Should().BeEquivalentTo(Enumerable.Range(1, 4).Select(number => $"Object {number} was found {found} where the index said."));
    }

    [Theory]
    [InlineData(100, 500, true)]
    [InlineData(100, 600, false)]
    [InlineData(400, 500, true)]
    [InlineData(400, 505, true)]
    [InlineData(400, 506, false)]
    [InlineData(400, 600, false)]
    [InlineData(513, 505, true)]
    [InlineData(513, 506, false)]
    [InlineData(1400, 500, true)]
    [InlineData(1400, 505, true)]
    [InlineData(1400, 506, false)]
    [InlineData(1400, 600, false)]
    public void An_object_is_found_near_its_entry_only_where_its_header_ends_within_512_bytes_after_it_however_near_the_file_s_start(
        long row, long distance, bool nearby)
    {
        // The page's entry gives an offset before its header, "3 0 obj", in the file's first 512 bytes or past them. The search
        // near the entry finds the header where it lies whole within 512 bytes of the offset, its obj keyword ending 512 bytes
        // after it at most; otherwise the index is rebuilt, which finds it. A window clamped at the file's start once reached up
        // to 1,024 bytes past the offset: one shift was found near the start, and rebuilt past it (#197). An entry 513 bytes in
        // has the window start on the file's second byte, the byte read before it the file's first: it reaches as far.
        using var document = PdfDocument.Open(NearbyFiles.PageNamedAt(row, row + distance));

        document.GetObject(new PdfObjectId(3)).Should().BeOfType<PdfDictionary>().Which.IsOfType(PdfName.Page).Should().BeTrue();
        document.WasRepaired.Should().Be(!nearby);
        var report = document.Diagnostics.Should().ContainSingle().Which;
        report.Code.Should().Be(nearby ? PdfDiagnosticCodes.XRefOffsetAdjusted : PdfDiagnosticCodes.XRefRebuilt);

        if (nearby)
        {
            report.Message.Should().Be($"Object 3 was found {distance} bytes after where the index said.");
        }
    }

    [Theory]
    [InlineData(-500, 500, false)]
    [InlineData(-500, 600, false)]
    [InlineData(-511, 500, false)]
    [InlineData(-511, 600, false)]
    [InlineData(-1, 500, true)]
    [InlineData(-1, 504, true)]
    [InlineData(-1, 505, false)]
    [InlineData(-1, 600, false)]
    [InlineData(-9, 490, true)]
    [InlineData(-9, 500, false)]
    public void An_object_whose_entry_lies_before_the_file_s_start_is_found_near_it_only_where_its_header_ends_within_512_bytes_after_it(
        long row, long at, bool nearby)
    {
        // The page's entry gives a negative offset, and its header, "3 0 obj", starts at 490 to 600 bytes into the file: the
        // search near the entry finds it where its obj keyword ends 512 bytes after the offset at most, as for any other entry,
        // and the index is otherwise rebuilt. A window clamped at the file's start once reached 1,024 bytes into the file
        // whatever the offset, so that an entry of -500 found an object 1,323 bytes after it (#197).
        using var document = PdfDocument.Open(NearbyFiles.PageNamedAt(row, at));

        document.GetObject(new PdfObjectId(3)).Should().BeOfType<PdfDictionary>().Which.IsOfType(PdfName.Page).Should().BeTrue();
        document.WasRepaired.Should().Be(!nearby);
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(
            PdfDiagnosticCodes.XRefEntryOutOfRange, nearby ? PdfDiagnosticCodes.XRefOffsetAdjusted : PdfDiagnosticCodes.XRefRebuilt);

        if (nearby)
        {
            document.Diagnostics[1].Message.Should().Be($"Object 3 was found {at - row} bytes after where the index said.");
        }
    }

    [Theory]
    [InlineData(4, 512, "near", true)]
    [InlineData(4, 513, "near", false)]
    [InlineData(14, 513, "four", false)]
    public void An_object_is_found_near_its_entry_only_where_its_header_starts_within_512_bytes_before_it_whatever_number_the_window_s_start_cuts(
        int number, long before, string served, bool nearby)
    {
        // Object 4's entry gives an offset 512 or 513 bytes past the header of object 4, or 513 bytes past object 14's, whose 4
        // the window the search reads near the entry starts on. The search reads the byte before its window too: a header counts
        // only where its number's first digit lies within the window, and object 14's body, which was served as object 4's with
        // a warning, is not taken for it (#197). Otherwise the index is rebuilt, which finds object 4 where it is.
        const long At = 805;
        using var document = PdfDocument.Open(NearbyFiles.HeaderNear(number, At, At + before));

        document.GetObject(new PdfObjectId(4)).Should().BeOfType<PdfString>().Which.ToText().Should().Be(served);
        document.WasRepaired.Should().Be(!nearby);
        var report = document.Diagnostics.Should().ContainSingle().Which;
        report.Code.Should().Be(nearby ? PdfDiagnosticCodes.XRefOffsetAdjusted : PdfDiagnosticCodes.XRefRebuilt);

        if (nearby)
        {
            report.Message.Should().Be("Object 4 was found 512 bytes before where the index said.");
        }
    }

    [Fact]
    public void A_window_that_starts_on_the_file_s_second_byte_reads_its_first_too_and_takes_no_number_it_cuts_for_a_header()
    {
        // The file starts with "14 0 obj", before its header, and object 4's entry gives the offset of the byte 513 bytes into
        // the file: the window the search reads near it starts on the 4 of 14, the file's second byte. It reads the first byte
        // too, as every window that does not start the file does, and object 14 is not taken for object 4 (#197). The index
        // is rebuilt, which finds object 4 where it is.
        using var document = PdfDocument.Open(NearbyFiles.FourNamedPastJunk("14 0 obj\n(fourteen)\nendobj\n", 513));

        document.GetObject(new PdfObjectId(4)).Should().BeOfType<PdfString>().Which.ToText().Should().Be("four");
        document.WasRepaired.Should().BeTrue();
        document.Diagnostics.Should().NotContain(diagnostic => diagnostic.Message.StartsWith("Object 4 ", StringComparison.Ordinal));
    }

    /// <summary>
    /// Headers of object 4 with comments between their tokens, each after what precedes it on its line, or on the line before
    /// it — which ISO 32000-1 allows (7.2.3), and a load reads (#175).
    /// </summary>
    public static readonly TheoryData<string, string> HeadersHoldingComments = new()
    {
        { string.Empty, "4 % a comment\n0 obj" },
        { string.Empty, "4 0 % a comment\nobj" },
        { string.Empty, "4 % one\n0 % two\nobj" },
        { string.Empty, "4% right after the number\n0 obj" },
        { string.Empty, "4\n% one\n% two\n0\n% three\nobj" },
        { string.Empty, "4 % a comment\r0 obj" },
        { string.Empty, "4 % a comment\r\n0 obj" },
        { string.Empty, "4 0 % a comment\robj" },
        { string.Empty, "4 % 50% done\n0 obj" },
        { string.Empty, "4 %\n0 obj" },
        { "(50%) ", "4 % a comment\n0 obj" },
        { "null ", "4 % a comment\n0 obj" },
        { "(\n", "4 % a comment\n0 obj" },
        { "(\r", "4 % a comment\r0 obj" },
    };

    [Theory]
    [MemberData(nameof(HeadersHoldingComments))]
    public void An_object_whose_header_holds_comments_is_read_where_its_entry_places_it(string before, string header)
    {
        using var document = PdfDocument.Open(NearbyFiles.FourWrittenAs(header, before: before));

        document.GetObject(new PdfObjectId(4)).Should().BeOfType<PdfString>().Which.ToText().Should().Be("four");
        document.WasRepaired.Should().BeFalse();
        document.Diagnostics.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(HeadersHoldingComments))]
    public void An_object_whose_header_holds_comments_is_found_near_an_entry_two_bytes_past_it(string before, string header)
    {
        // The search near an entry reads a header back from its obj keyword: where an end of line lies between its tokens, the
        // line it ends is read as a load reads it, from its start, which tells a comment from a % inside a string, and a line
        // that holds only a comment is passed. It once stopped at the comment, and rebuilt the index, which did not find the
        // object either (#261). What precedes the header on its line is read for what it is, and what a line before it leaves
        // open is not: the header's line is read from its own start.
        using var document = PdfDocument.Open(NearbyFiles.FourWrittenAs(header, shift: 2, before: before));

        document.GetObject(new PdfObjectId(4)).Should().BeOfType<PdfString>().Which.ToText().Should().Be("four");
        document.WasRepaired.Should().BeFalse();
        var report = document.Diagnostics.Should().ContainSingle().Which;
        report.Code.Should().Be(PdfDiagnosticCodes.XRefOffsetAdjusted);
        report.Message.Should().Be("Object 4 was found 2 bytes before where the index said.");
    }

    [Theory]
    [MemberData(nameof(HeadersHoldingComments))]
    public void A_rebuild_finds_an_object_whose_header_holds_comments_where_its_number_starts(string before, string header)
    {
        // startxref names the catalog's header, no section: the index is rebuilt as the document opens, and its scan reads a
        // header with comments between its tokens as the search near an entry does (#261).
        var file = NearbyFiles.FourWrittenAs(header, startxrefLost: true, before: before);
        using var document = PdfDocument.Open(file);

        document.WasRepaired.Should().BeTrue();
        document.GetObject(new PdfObjectId(4)).Should().BeOfType<PdfString>().Which.ToText().Should().Be("four");
        document.Reader.Index.TryGet(4, out var entry).Should().BeTrue();
        entry.Offset.Should().Be(PdfTemplate.OffsetOf(file, before + header) + before.Length);
    }

    [Theory]
    [InlineData("% a note 4\n0 obj")]
    [InlineData("4 (x) % a comment\nobj")]
    [InlineData("4 0.0 % a comment\nobj")]
    [InlineData("4 -1 % a comment\nobj")]
    [InlineData("4 (open % a comment\n0 obj")]
    [InlineData("/Four 0 % a comment\nobj")]
    [InlineData("x4 % a comment\n0 obj")]
    [InlineData("4\n(x) % a comment\n0 obj")]
    [InlineData("4\n/Four 0 % a comment\nobj")]
    public void Tokens_a_load_takes_for_no_header_are_none_for_a_rebuild_whatever_comments_lie_between_them(string header)
    {
        // Each is no header of object 4 as a load reads it: digits a comment ends with, a string, a real, a negative
        // generation, a string the line does not close, a name, a keyword. The rebuild's scan, which reads the line a comment
        // ends as a load does, takes none for object 4's header, where it once took digits ending a comment for its number.
        using var document = PdfDocument.Open(NearbyFiles.FourWrittenAs(header, startxrefLost: true));

        document.WasRepaired.Should().BeTrue();
        document.Reader.Index.TryGet(4, out _).Should().BeFalse();
        document.GetObject(new PdfObjectId(4)).Should().BeSameAs(PdfNull.Instance);
    }

    [Fact]
    public void A_header_after_a_string_holding_a_percent_sign_on_its_line_is_read_by_its_tokens()
    {
        // The line that ends with object 4's number holds a % in a string, and no comment: it is lexed from its start as a
        // load reads it, the string read past, and the number at its end taken, near the entry as by a rebuild (#261).
        using var nearby = PdfDocument.Open(NearbyFiles.FourWrittenAs("4\n0 obj", shift: 2, before: "(50%) "));
        using var rebuilt = PdfDocument.Open(NearbyFiles.FourWrittenAs("4\n0 obj", startxrefLost: true, before: "(50%) "));

        nearby.GetObject(new PdfObjectId(4)).Should().BeOfType<PdfString>().Which.ToText().Should().Be("four");
        nearby.WasRepaired.Should().BeFalse();
        rebuilt.GetObject(new PdfObjectId(4)).Should().BeOfType<PdfString>().Which.ToText().Should().Be("four");
        rebuilt.WasRepaired.Should().BeTrue();
    }

    [Theory]
    [InlineData("4 0 obj", "", true)]
    [InlineData("% 4 0 obj", "", false)]
    [InlineData("4 0 obj", "% a note ", false)]
    [InlineData("4 0 obj", "(a note) ", true)]
    [InlineData("4 0 obj", "(a note ", false)]
    [InlineData("4 0 obj", "(50%) ", true)]
    [InlineData("4 0 obj", "x", false)]
    [InlineData("4 0 obj", "/", false)]
    [InlineData("4 0 obj", "%%EOF", true)]
    [InlineData("4 0 obj", "% %%EOF", false)]
    [InlineData("4 0 obj", "((\\\n9 0 %\\\n%) trailer \\\nobj ", false)]
    [InlineData("4 0\n%endstream %trailer\nobj", "5 0 obj\n<< /Length 3 >>\nstream\n", true)]
    [InlineData("4 0 obj", "5 0 obj\n<< /Length 3 >>\nstream\n9 0 obj (nine) endobj\n(endstream endobj ", true)]
    public void A_rebuild_reads_the_line_of_a_header_between_objects_as_a_load_lexes_it(string header, string before, bool found)
    {
        // The line a header starts on is lexed as a load lexes it: a header a comment or a string holds is none, nor digits a
        // longer token holds; a string closed before the header ends before it (#171). The end-of-file marker is the one
        // comment that ends with itself: a writer that appends an update with no end of line after it glues the update's
        // first header to it, as pdfcpu does. A string's parentheses balance, and a backslash carries it past the end of its
        // line: what the lines it crosses seem to hold, a header or a comment, is the string's. In the data of a stream whose
        // length is not confirmed, up to its first endstream, no comment or string opens: a header that starts there is taken
        // whatever comments its tokens hold, and the line after that endstream is read from it.
        var file = NearbyFiles.FourWrittenAs(header, startxrefLost: true, before: before);
        using var document = PdfDocument.Open(file);

        document.Reader.Index.TryGet(4, out _).Should().Be(found);
    }

    [Fact]
    public void Digits_a_comment_ends_with_are_no_object_number_for_the_search_near_an_entry()
    {
        // Object 4's entry gives where "% a note 4" starts, a line before "0 obj": a comment, then the header of no object,
        // as a load reads it. The search near the entry once took the 4 for object 4's number, and served what follows as
        // object 4, 9 bytes from its entry (#261); the index is rebuilt, which finds no object 4.
        using var document = PdfDocument.Open(NearbyFiles.FourWrittenAs("% a note 4\n0 obj"));

        document.GetObject(new PdfObjectId(4)).Should().BeSameAs(PdfNull.Instance);
        document.WasRepaired.Should().BeTrue();
        document.Diagnostics.Should().NotContain(diagnostic => diagnostic.Code == PdfDiagnosticCodes.XRefOffsetAdjusted);
    }

    [Theory]
    [InlineData("x4 0 obj", "x4\n0 obj")]
    [InlineData("-4 0 obj", "-4\n0 obj")]
    public void A_line_end_between_a_header_s_tokens_changes_nothing_where_no_comment_lies_on_its_line(string oneLine, string twoLines)
    {
        // A line that holds no % is read back by its digits, whatever ends it: the rebuild's scan reads a header whose tokens a
        // line end parts as it reads the same header on one line, here digits a regular character or a sign precedes (#171).
        Read(oneLine).Should().Be(Read(twoLines));

        static string Read(string header)
        {
            using var document = PdfDocument.Open(NearbyFiles.FourWrittenAs(header, startxrefLost: true));
            return document.GetObject(new PdfObjectId(4)).ToString() ?? string.Empty;
        }
    }

    [Fact]
    public void Rebuilds_the_index_of_a_file_that_has_none()
    {
        var bytes = SampleDocument().BuildClassic(rootNumber: 1, includeXRef: false);

        using var document = PdfDocument.Open(bytes);

        document.WasRepaired.Should().BeTrue();
        document.Diagnostics.Contains(PdfDiagnosticCodes.XRefRebuilt).Should().BeTrue();
        document.Catalog.Required().IsOfType(PdfName.Catalog).Should().BeTrue();
        PageContent(document).Should().Be("BT (Bonjour) Tj ET");
    }

    [Fact]
    public void Rebuilds_the_index_of_a_file_whose_tail_was_lost()
    {
        var complete = SampleDocument().BuildClassic(rootNumber: 1);
        var truncated = complete.AsSpan(0, complete.Length - 40).ToArray();

        using var document = PdfDocument.Open(truncated);

        document.WasRepaired.Should().BeTrue();
        document.Catalog.Required();
    }

    [Fact]
    public void Rebuilds_the_index_of_a_file_cut_right_after_its_trailer_keyword()
    {
        var complete = SampleDocument().BuildClassic(rootNumber: 1);
        var keyword = complete.AsSpan().LastIndexOf("trailer"u8);
        var truncated = complete.AsSpan(0, keyword + "trailer".Length).ToArray();

        using var document = PdfDocument.Open(truncated);

        document.WasRepaired.Should().BeTrue();
        document.Catalog.Required().IsOfType(PdfName.Catalog).Should().BeTrue();
        PageContent(document).Should().Be("BT (Bonjour) Tj ET");
        document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.SyntaxTruncatedObject)
            .Which.Position.Should().Be(truncated.Length, "the trailer the keyword announces would start where the file ends");
    }

    [Theory]
    [InlineData("<< /K 1 >>\nstream\n) /Data (\nendstream")]
    [InlineData("[1 << /K 1 >>\nstream\n) /Data (\nendstream 2]")]
    public void A_trailer_a_rebuild_merges_ends_at_a_stream_nested_in_it_noted_its_data_unread(string nested)
    {
        // A rebuild's scan for trailers parses the dictionary after each trailer keyword alone, and reads no stream (#171):
        // a stream nested in it — in a value, or in an array —, which no file may write there, ends the parse at its
        // keyword, with every container open around it. The keys before the one whose value holds it are merged; that
        // key, the stream's data, which spells a key, and the key after it are not. What the rebuild so leaves out is noted
        // once, at the stream's keyword; the data, unread, gives no fault.
        var file = new HandWrittenFile();
        file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        var keyword = file.Position;
        file.Raw($"trailer\n<< /Root 1 0 R /Nested {nested} /After 2 >>\n");
        var bytes = file.Bytes;

        using var document = PdfDocument.Open(bytes);

        document.WasRepaired.Should().BeTrue();
        document.Trailer.Keys.Select(key => key.Value).Should().Equal("Root");
        var noted = document.Diagnostics.Should()
            .ContainSingle(diagnostic => diagnostic.Code.StartsWith("syntax.", StringComparison.Ordinal)).Which;
        noted.Code.Should().Be(PdfDiagnosticCodes.SyntaxUnexpectedToken);
        noted.Severity.Should().Be(PdfDiagnosticSeverity.Warning);
        noted.Position.Should().Be(bytes.AsSpan().IndexOf("stream\n) /Data ("u8));
        noted.Message.Should().Be(string.Create(
            CultureInfo.InvariantCulture,
            $"A stream inside the dictionary after the trailer keyword at offset {keyword} ends what the rebuild merges of it: the key that holds it and the keys after it are not merged."));
    }

    [Fact]
    public void A_trailer_a_rebuild_merges_gives_every_key_of_a_stream_s_dictionary_unnoted_its_data_unread()
    {
        // A stream after the dictionary a trailer keyword introduces, which a cross-reference stream's dictionary after a
        // keyword in a comment is, ends nothing of it: every key is merged, the stream's data, which spells a key, unread,
        // and nothing is noted (#171).
        var file = new HandWrittenFile();
        file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        file.Raw("trailer\n<< /Root 1 0 R /Nested << /K 1 >> /After 2 >>\nstream\n) /Data (\nendstream\n");

        using var document = PdfDocument.Open(file.Bytes);

        document.WasRepaired.Should().BeTrue();
        document.Trailer.Keys.Select(key => key.Value).Should().Equal("Root", "Nested", "After");
        document.Diagnostics.Should().NotContain(diagnostic => diagnostic.Code.StartsWith("syntax.", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("trailer\n5 0 R\n")]
    [InlineData("trailer % the dictionary follows\n5 0 R\n")]
    public void A_trailer_a_rebuild_merges_is_a_dictionary_not_what_a_reference_after_the_keyword_names_which_is_noted(string tail)
    {
        // A rebuild's scan for trailers parses what follows each trailer keyword as the dictionary alone, and loads nothing
        // (#171): a reference written in the dictionary's place, white space and a comment before it or not, is none, and
        // is not followed, so that what object 5 holds is not merged. The rebuild notes it once, where the reference
        // starts; the catalog, which the trailer then lacks, is searched for and found.
        var file = new HandWrittenFile();
        file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        file.Object(5, "<< /Root 1 0 R /Marker true >>");
        var keyword = file.Position;
        file.Raw(tail);
        var bytes = file.Bytes;

        using var document = PdfDocument.Open(bytes);

        document.WasRepaired.Should().BeTrue();
        document.Trailer.Keys.Select(key => key.Value).Should().Equal("Root");
        document.Diagnostics.Contains(PdfDiagnosticCodes.TrailerRootRecovered).Should().BeTrue();
        var noted = document.Diagnostics.Should()
            .ContainSingle(diagnostic => diagnostic.Code.StartsWith("syntax.", StringComparison.Ordinal)).Which;
        noted.Code.Should().Be(PdfDiagnosticCodes.SyntaxUnexpectedToken);
        noted.Severity.Should().Be(PdfDiagnosticSeverity.Warning);
        noted.Position.Should().Be(bytes.AsSpan().LastIndexOf("5 0 R"u8));
        noted.Message.Should().Be(string.Create(
            CultureInfo.InvariantCulture,
            $"The trailer keyword at offset {keyword} is followed by a reference to object 5, not by a dictionary; the rebuild does not follow it."));
    }

    [Theory]
    [InlineData("trailer\n<< /Root 1 0 R /Nested << /K 1 >>\nstream\nabc\nendstream /After 2 >>\n", "stream\nabc")]
    [InlineData("trailer\n5 0 R\n", "5 0 R")]
    public void A_note_the_scan_for_trailers_makes_of_a_trailer_found_in_the_overlap_of_two_windows_is_made_once(
        string tail, string noted)
    {
        // The scan for trailers reads a megabyte at a time, each window overlapping the last by 64 bytes: the trailer keyword
        // lies 40 bytes before the first megabyte ends, so both windows find it, and what follows it, a dictionary a stream
        // nested in it cuts short or a reference in the dictionary's place, is parsed twice. What the parse notes goes with
        // its faults, which are kept once: one note, where the stream's keyword or the reference starts (#171).
        var file = new HandWrittenFile();
        file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        file.Object(5, "<< /Root 1 0 R /Marker true >>");
        file.Raw("%" + new string('x', (int)((1024 * 1024) - 40 - file.Position - 2)) + "\n");
        var keyword = file.Position;
        file.Raw(tail);
        file.Padding(3, 200);

        using var document = PdfDocument.Open(file.Bytes);

        keyword.Should().Be((1024 * 1024) - 40);
        document.WasRepaired.Should().BeTrue();
        document.Trailer.Keys.Select(key => key.Value).Should().Equal("Root");
        document.Diagnostics.Should().ContainSingle(diagnostic => diagnostic.Code.StartsWith("syntax.", StringComparison.Ordinal))
            .Which.Position.Should().Be(keyword + tail.IndexOf(noted, StringComparison.Ordinal));
    }

    [Fact]
    public void A_trailer_a_rebuild_merges_keeps_whole_a_reference_its_window_ends_on()
    {
        // A rebuild's scan for trailers parses the dictionary after each trailer keyword through a window of 64 KB, which a
        // longer trailer runs past, what lies past it unread (#49). Here the window ends on the R of the reference /Root
        // holds: the file says what follows it, white space, as it does to a load (#224), so the reference is read whole
        // and the catalog it names is the document's, rather than a number the window's edge made of it and a catalog
        // searched for.
        const string Before = "\n<< /Pad (";
        const string After = ") /Root 1 0 R";
        var file = new HandWrittenFile();
        file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        file.Raw("trailer" + Before + new string('x', PdfFileReader.XRefWindow - Before.Length - After.Length) + After + " >>\n");

        using var document = PdfDocument.Open(file.Bytes);

        document.WasRepaired.Should().BeTrue();
        document.Trailer.GetRaw(PdfName.Root).Should().BeOfType<PdfReference>().Which.Id.Should().Be(new PdfObjectId(1));
        document.Diagnostics.Should().NotContain(diagnostic => diagnostic.Code == PdfDiagnosticCodes.TrailerRootRecovered);
    }

    [Theory]
    [InlineData("<< /K 1 >> ", "streamline", "")]
    [InlineData("<< /K 1 >> ", "streamX", "")]
    [InlineData("[<< /K 1 >> ", "streamline", "]")]
    [InlineData("[<< /K 1 >> ", "streamX", "]")]
    public void A_keyword_longer_than_stream_a_rebuilt_trailer_s_window_cuts_after_a_nested_dictionary_ends_nothing_wherever_the_edge_falls_and_is_not_noted(
        string opening, string word, string closing)
    {
        // A rebuild's scan for trailers parses the dictionary after each trailer keyword through a window of 64 KB, which a
        // longer trailer runs past, what lies past it unread (#49). Here the window's edge falls 5, 6 or 7 bytes into a
        // keyword the file spells longer than stream, after a dictionary /S holds, or an array /S holds starts with. The
        // file says the keyword goes on past the edge: even cut to "stream", it is no stream nested in the trailer, and ends
        // nothing of it. The trailer is the same wherever the edge falls, /S as far as it was read, and what the edge cut
        // short is not noted (#171).
        var held = new List<string>();

        foreach (var inside in (int[])[5, 6, 7])
        {
            var (document, _, _) = OpenTrailerWhoseWindowEndsInside(opening, word, closing, inside);

            using (document)
            {
                document.WasRepaired.Should().BeTrue();
                document.Trailer.Keys.Select(key => key.Value).Should().BeEquivalentTo("Root", "Pad", "S");
                document.Diagnostics.Should().NotContain(diagnostic => diagnostic.Code.StartsWith("syntax.", StringComparison.Ordinal));
                held.Add(string.Create(CultureInfo.InvariantCulture, $"{document.Trailer.GetRaw(PdfName.Get("S"))}"));
            }
        }

        held.Should().AllBe(held[0], "the edge cuts the keyword 6 bytes in as it does 5 or 7 bytes in");
    }

    [Theory]
    [InlineData("<< /K 1 >> ", "stream\nabc\nendstream", "")]
    [InlineData("[<< /K 1 >> ", "stream abc endstream", "]")]
    public void A_stream_keyword_a_rebuilt_trailer_s_window_ends_on_after_a_nested_dictionary_ends_the_trailer_there_and_is_noted_once(
        string opening, string word, string closing)
    {
        // As above, the window's edge falls 6 bytes into what follows a dictionary /S holds, here the stream keyword itself:
        // the file says the keyword ends there, so that it is the keyword of a stream nested in the trailer, which ends the
        // parse, /S and the keys after it not merged, and is noted once, where the keyword starts (#171).
        var (document, keyword, at) = OpenTrailerWhoseWindowEndsInside(opening, word, closing, 6);

        using (document)
        {
            document.WasRepaired.Should().BeTrue();
            document.Trailer.Keys.Select(key => key.Value).Should().BeEquivalentTo("Root", "Pad");
            var noted = document.Diagnostics.Should()
                .ContainSingle(diagnostic => diagnostic.Code.StartsWith("syntax.", StringComparison.Ordinal)).Which;
            noted.Code.Should().Be(PdfDiagnosticCodes.SyntaxUnexpectedToken);
            noted.Position.Should().Be(at);
            noted.Message.Should().StartWith(string.Create(
                CultureInfo.InvariantCulture,
                $"A stream inside the dictionary after the trailer keyword at offset {keyword} ends what the rebuild merges of it"));
        }
    }

    [Fact]
    public void Prefers_the_newest_definition_after_an_incremental_update()
    {
        var original = SampleDocument().BuildClassic(rootNumber: 1);
        var updated = TestPdfBuilder.AppendIncrementalUpdate(
            original,
            rootNumber: 1,
            [(4, "<< /Length 20 >>\nstream\nBT (Au revoir) Tj ET\nendstream")]);

        using var document = PdfDocument.Open(updated);

        document.WasRepaired.Should().BeFalse();
        PageContent(document).Should().Be("BT (Au revoir) Tj ET");
    }

    [Fact]
    public void Stops_when_the_chain_of_sections_loops()
    {
        var original = SampleDocument().BuildClassic(rootNumber: 1);
        var looping = TestPdfBuilder.AppendIncrementalUpdate(
            original,
            rootNumber: 1,
            [(4, "<< /Length 18 >>\nstream\nBT (Bonjour) Tj ET\nendstream")],
            pointPreviousAtSelf: true);

        using var document = PdfDocument.Open(looping);

        document.Catalog.Required();
        document.Diagnostics.Contains(PdfDiagnosticCodes.XRefChainCycle).Should().BeTrue();
    }

    [Theory]
    [InlineData("0000099999")]
    [InlineData("-000000001")]
    public void Reports_an_entry_outside_the_file_with_no_position_and_its_offset_in_the_message(string offset)
    {
        // No position in the file names an entry the index keeps without where it was read (#187).
        var template = PdfTemplate.Sound
            .Replace("/Type /Catalog /Pages 2 0 R", "/Type /Catalog /Pages 2 0 R /Outlines 4 0 R", StringComparison.Ordinal)
            .Replace("0 4\n", "0 5\n", StringComparison.Ordinal)
            .Replace("{row:3}\n", "{row:3}\n" + offset + " 00000 n \n", StringComparison.Ordinal)
            .Replace("/Size 4", "/Size 5", StringComparison.Ordinal);

        using var document = PdfDocument.Open(PdfTemplate.Build(template));
        document.GetObject(new PdfObjectId(4));

        var report = document.Diagnostics.Should().ContainSingle(d => d.Code == PdfDiagnosticCodes.XRefEntryOutOfRange).Which;
        report.Position.Should().Be(-1);
        report.Message.Should().Be(string.Create(
            CultureInfo.InvariantCulture,
            $"The entry of object 4 places it at offset {long.Parse(offset, CultureInfo.InvariantCulture)}, outside the file."));
    }

    [Fact]
    public void Reads_a_file_that_starts_with_junk_before_the_header()
    {
        var original = SampleDocument().BuildClassic(rootNumber: 1);
        var prefixed = Encoding.ASCII.GetBytes("garbage bytes from a broken download\n").Concat(original).ToArray();

        using var document = PdfDocument.Open(prefixed);

        document.Catalog.Required();
        PageContent(document).Should().Be("BT (Bonjour) Tj ET");
    }

    [Fact]
    public void Refuses_an_encrypted_document_with_a_typed_exception()
    {
        var builder = SampleDocument().WithObject(9, "<< /Filter /Standard /V 1 /R 2 /P -1 >>");
        var bytes = builder.BuildClassic(rootNumber: 1);
        var encrypted = Encoding.Latin1.GetString(bytes)
            .Replace("/Root 1 0 R >>", "/Root 1 0 R /Encrypt 9 0 R >>", StringComparison.Ordinal);

        FluentThrow<PdfEncryptedException>(() => PdfDocument.Open(Encoding.Latin1.GetBytes(encrypted)));
    }

    [Fact]
    public void Refuses_an_empty_input()
    {
        FluentThrow<PdfFormatException>(() => PdfDocument.Open(ReadOnlyMemory<byte>.Empty));
    }

    [Fact]
    public void A_reader_handed_an_empty_source_finds_no_header_and_rebuilds_an_index_that_stays_empty()
    {
        // A document refuses an empty input before any reader sees it; handed one all the same, the reader finds
        // neither a header nor a startxref, and says so.
        var diagnostics = new PdfDiagnostics();
        var reader = new PdfFileReader(
            PdfFileSource.FromMemory(ReadOnlyMemory<byte>.Empty),
            diagnostics,
            new PdfLimitGuard(PdfReaderLimits.Default, throwOnLimit: false),
            cacheCapacity: 64,
            ownsSource: true);

        reader.ObjectCount.Should().Be(0);
        reader.WasRepaired.Should().BeTrue();
        reader.Structure.StartXRefPosition.Should().Be(-1);
        diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Message, diagnostic.Position)).Should().Equal(
            (PdfDiagnosticCodes.HeaderMissing, "The file has no PDF header (%PDF-) in its first 4,096 bytes; its offsets were counted from its first byte.", -1L),
            (PdfDiagnosticCodes.XRefRebuilt, "The cross-reference index was rebuilt by scanning the file.", -1L));
    }

    [Theory]
    [InlineData("%PDF.1.7\n")]
    [InlineData("")]
    public void A_file_without_a_header_is_read_through_its_table_and_its_header_reported_as_a_repair(string header)
    {
        // Nothing is rebuilt for a missing header: the file's offsets count from its first byte, as from a header
        // there (#187).
        using var document = PdfDocument.Open(PdfTemplate.Build(PdfTemplate.Sound.Replace("%PDF-1.7\n", header, StringComparison.Ordinal)));

        document.Catalog.Should().NotBeNull();
        document.WasRepaired.Should().BeFalse();
        var report = document.Diagnostics.Should().ContainSingle().Which;
        report.Code.Should().Be(PdfDiagnosticCodes.HeaderMissing);
        report.Severity.Should().Be(PdfDiagnosticSeverity.Repair);
        report.Position.Should().Be(0);
        document.Diagnostics.Contains(PdfDiagnosticCodes.XRefRebuilt).Should().Be(document.WasRepaired);
    }

    [Fact]
    public void Does_not_read_stream_data_until_it_is_asked_for()
    {
        var payload = new string('A', 500_000);
        var builder = SampleDocument(content: payload);
        var source = new CountingSource(builder.BuildClassic(rootNumber: 1));

        using var document = PdfDocument.Open(source, options: null, ownsSource: false);

        var afterOpen = source.BytesRead;
        afterOpen.Should().BeLessThan(200_000);

        var stream = Page(document).GetStream(PdfName.Contents).Required();
        stream.GetRawBytes().Length.Should().Be(payload.Length);

        source.BytesRead.Should().BeGreaterThan(afterOpen + payload.Length - 1);
    }

    [Fact]
    public void Stream_data_a_file_lost_after_its_dictionary_was_read_is_read_as_far_as_the_file_still_goes()
    {
        // The file is cut five bytes into the page's content once its dictionary is read: the data is those five bytes,
        // not the eighteen its /Length gives with zeros where the rest was.
        var bytes = SampleDocument().BuildClassic(rootNumber: 1);
        var source = new CutShortSource(bytes);
        using var document = PdfDocument.Open(source, options: null, ownsSource: true);
        var stream = Page(document).GetStream(PdfName.Contents).Required();

        source.CutAt((int)PdfTemplate.OffsetOf(bytes, "BT (Bonjour)") + 5);

        Encoding.ASCII.GetString(stream.GetRawBytes().Span).Should().Be("BT (B");
    }

    public static TheoryData<string, string> StreamKinds()
    {
        var kinds = new[]
        {
            "a memory stream written to", "a memory stream that shows its buffer", "a memory stream that hides its buffer",
            "a memory stream whose buffer starts past an origin", "a stream derived from a memory stream", "a buffered stream",
            "a file stream",
        };
        var data = new TheoryData<string, string>();

        foreach (var kind in kinds)
        {
            foreach (var place in new[] { "at its start", "at its end", "partly read", "past its end" })
            {
                data.Add(kind, place);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(StreamKinds))]
    public void Opens_a_seekable_stream_from_its_start_wherever_it_stands(string kind, string place)
    {
        var bytes = SampleDocument().BuildClassic(rootNumber: 1);
        var path = kind == "a file stream" ? Path.GetTempFileName() : null;

        try
        {
            using var stream = SeekableStream(kind, bytes, path);
            stream.Position = place switch
            {
                "at its start" => 0,
                "at its end" => stream.Length,
                "partly read" => 5,
                _ => stream.Length + 100,
            };

            using var document = PdfDocument.Open(stream);

            PageContent(document).Should().Be("BT (Bonjour) Tj ET");
            document.ObjectCount.Should().Be(5, "objects 1 to 4, and the free head of the table");
            document.Diagnostics.Should().BeEmpty("the stream is the file from its first byte");
        }
        finally
        {
            if (path is not null)
            {
                File.Delete(path);
            }
        }
    }

    [Theory]
    [InlineData("a memory stream written to")]
    [InlineData("a memory stream that hides its buffer")]
    [InlineData("a file stream")]
    public void A_seekable_stream_holds_what_comes_before_the_header_whatever_was_read_of_it(string kind)
    {
        var bytes = Encoding.Latin1.GetBytes(new string('x', 100) + "\n").Concat(SampleDocument().BuildClassic(rootNumber: 1)).ToArray();
        var path = kind == "a file stream" ? Path.GetTempFileName() : null;

        try
        {
            using var stream = SeekableStream(kind, bytes, path);
            stream.Position = 101;

            using var document = PdfDocument.Open(stream);

            PageContent(document).Should().Be("BT (Bonjour) Tj ET");
            document.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Position))
                .Should().Equal((PdfDiagnosticCodes.XRefOffsetAdjusted, 101L));
        }
        finally
        {
            if (path is not null)
            {
                File.Delete(path);
            }
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void Opens_a_stream_that_cannot_seek_from_where_it_stands(int prefix)
    {
        var file = SampleDocument().BuildClassic(rootNumber: 1);
        var bytes = Encoding.Latin1.GetBytes(new string('x', prefix)).Concat(file).ToArray();
        using var stream = new ForwardOnlyStream(new MemoryStream(bytes));
        stream.ReadExactly(new byte[prefix]);

        using var document = PdfDocument.Open(stream);

        PageContent(document).Should().Be("BT (Bonjour) Tj ET");
        document.Diagnostics.Should().BeEmpty("what was read of it before is not the file's");
    }

    [Fact]
    public void A_seekable_stream_is_copied_into_an_array_of_its_length()
    {
        using var stream = new DerivedMemoryStream();
        stream.Write(new byte[1_000_001]);
        using (PdfFileSource.FromStream(stream))
        {
            // Warmed up first: the pool's buffer for the copy is not what is measured.
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        using var source = PdfFileSource.FromStream(stream);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        source.Length.Should().Be(1_000_001);
        allocated.Should().BeLessThan(1_250_000, "the copy is sized from the stream's length, not grown by doubling");
    }

    [Fact]
    public void A_seekable_stream_longer_than_an_array_holds_is_copied_as_it_reads()
    {
        // A stream that says it is longer than any array: its copy is not sized from what it says.
        using var stream = new OverlongStream(SampleDocument().BuildClassic(rootNumber: 1));

        using var document = PdfDocument.Open(stream);

        PageContent(document).Should().Be("BT (Bonjour) Tj ET");
    }

    [Fact]
    public void A_stream_that_cannot_seek_and_was_read_to_its_end_is_empty()
    {
        using var stream = new ForwardOnlyStream(new MemoryStream(SampleDocument().BuildClassic(rootNumber: 1)));
        stream.CopyTo(Stream.Null);

        FluentActions.Invoking(() => PdfDocument.Open(stream))
            .Should().Throw<PdfFormatException>().WithMessage("The input is empty.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Only_a_memory_stream_itself_is_read_in_its_buffer(bool derived)
    {
        // A derived memory stream may lend its buffer again once disposed, as a pooled one does: it is copied, and what is
        // written over its buffer after the document opened reaches none of the document's objects.
        var bytes = SampleDocument().BuildClassic(rootNumber: 1);
        using var stream = derived ? new DerivedMemoryStream() : new MemoryStream();
        stream.Write(bytes);

        using var document = PdfDocument.Open(stream);
        var content = Page(document).GetRaw(PdfName.Contents).Should().BeOfType<PdfReference>().Subject;
        stream.GetBuffer().AsSpan(0, bytes.Length).Fill((byte)'z');

        (content.Resolve() is PdfStream).Should().Be(derived, "a document read in the caller's buffer reads what is there now");
    }

    private static Stream SeekableStream(string kind, byte[] bytes, string? path)
    {
        switch (kind)
        {
            case "a memory stream written to":
                var written = new MemoryStream();
                written.Write(bytes);
                return written;

            case "a memory stream that shows its buffer":
                return new MemoryStream(bytes, 0, bytes.Length, writable: false, publiclyVisible: true);

            case "a memory stream that hides its buffer":
                return new MemoryStream(bytes);

            case "a memory stream whose buffer starts past an origin":
                var padded = Encoding.Latin1.GetBytes(new string('x', 37)).Concat(bytes).ToArray();
                return new MemoryStream(padded, 37, bytes.Length, writable: false, publiclyVisible: true);

            case "a stream derived from a memory stream":
                var derived = new DerivedMemoryStream();
                derived.Write(bytes);
                return derived;

            case "a buffered stream":
                return new BufferedStream(new MemoryStream(bytes));

            default:
                File.WriteAllBytes(path!, bytes);
                return new FileStream(path!, FileMode.Open, FileAccess.Read);
        }
    }

    /// <summary>A memory stream of a type derived from it, as a pooled one is.</summary>
    private sealed class DerivedMemoryStream : MemoryStream;

    /// <summary>A memory stream that says it is longer than any array can be.</summary>
    private sealed class OverlongStream(byte[] buffer) : MemoryStream(buffer)
    {
        public override long Length => (long)Array.MaxLength + 1;
    }

    /// <summary>A stream that can be read forward only, as a network response is.</summary>
    private sealed class ForwardOnlyStream(Stream inner) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_source_reads_nothing_outside_itself(bool fromFile)
    {
        var path = Path.GetTempFileName();

        try
        {
            File.WriteAllBytes(path, "%PDF"u8.ToArray());
            using var source = fromFile ? PdfFileSource.FromFile(path) : PdfFileSource.FromMemory("%PDF"u8.ToArray());
            var buffer = new byte[4];

            source.Read(-1, buffer).Should().Be(0);
            source.Read(4, buffer).Should().Be(0);
            source.Read(1, buffer).Should().Be(3);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(-1, 4)]
    [InlineData(4, 4)]
    [InlineData(0, 0)]
    [InlineData(0, -1)]
    public void A_source_gives_an_empty_window_for_a_range_outside_itself_or_of_no_length(long offset, int length)
    {
        using var source = PdfFileSource.FromMemory("%PDF"u8.ToArray());

        using var window = source.GetWindow(offset, length);

        window.Length.Should().Be(0);
        window.Offset.Should().Be(0);
    }

    [Theory]
    [InlineData(-1, 2, false)]
    [InlineData(0, -1, false)]
    [InlineData(3, 2, false)]
    [InlineData(5, 0, false)]
    [InlineData(1, 3, true)]
    [InlineData(4, 0, true)]
    public void A_source_in_memory_lends_only_a_range_it_holds_whole(long offset, int length, bool lent)
    {
        // A stream's data is lent where it lies in a file in memory (#170). The reader asks only for data that starts in the
        // file and whose length it clamped to the file, which no file can make otherwise: a range before the source, of a
        // negative length, or running past its end is lent nothing, rather than sliced out of bytes the source does not hold.
        // An empty range at its end is lent as empty.
        var data = "%PDF"u8.ToArray();
        using var source = PdfFileSource.FromMemory(data);

        source.TryLend(offset, length, out var memory).Should().Be(lent);
        memory.ToArray().Should().Equal(lent ? data.AsSpan((int)offset, length).ToArray() : []);
    }

    [Fact]
    public void A_stream_s_data_is_read_from_the_file_each_time_it_is_asked_for_and_kept_by_nothing()
    {
        // A stream's data was read once and kept with the stream for as long as the stream was held, by the object cache or
        // by the caller (#170). It is read each time it is asked for: a file in memory lends it where it lies, and any other
        // source is read again.
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .Stream(3, string.Empty, new string('A', 20_000))
            .BuildClassic(rootNumber: 1);
        using var source = new StrictCountingSource(file);
        using var document = PdfDocument.Open(source, options: null, ownsSource: false);
        var stream = document.GetObject(new PdfObjectId(3)).AsStream().Required();
        var before = source.BytesRead;

        var first = stream.GetRawBytes().ToArray();
        var second = stream.GetRawBytes().ToArray();

        first.Should().Equal(second);
        first.Should().HaveCount(20_000).And.OnlyContain(value => value == (byte)'A');
        (source.BytesRead - before).Should().Be(40_000, "the data is read each time it is asked for");

        using var inMemory = PdfDocument.Open(file);
        var lent = inMemory.GetObject(new PdfObjectId(3)).AsStream().Required().GetRawBytes();
        System.Runtime.InteropServices.MemoryMarshal.TryGetArray(lent, out var segment).Should().BeTrue();
        segment.Array.Should().BeSameAs(file, "a file in memory lends the data where it lies");
    }

    [Fact]
    public void Disposing_a_document_twice_is_harmless()
    {
        var document = PdfDocument.Open(SampleDocument().BuildClassic(rootNumber: 1));

        document.Dispose();
        FluentActions.Invoking(document.Dispose).Should().NotThrow();
    }

    public static TheoryData<string> Openings() =>
        new("bytes", "a stream it copies", "a memory stream it reads in place", "a path", "a source it does not own");

    [Theory]
    [MemberData(nameof(Openings))]
    public void A_disposed_document_answers_nothing_but_its_diagnostics_nor_does_what_was_taken_from_it(string opening)
    {
        var bytes = HeldObjectsDocument();
        var path = opening == "a path" ? Path.GetTempFileName() : null;

        try
        {
            if (path is not null)
            {
                File.WriteAllBytes(path, bytes);
            }

            using var stream = opening switch
            {
                "a stream it copies" => new MemoryStream(bytes),
                "a memory stream it reads in place" => new MemoryStream(bytes, 0, bytes.Length, writable: false, publiclyVisible: true),
                _ => null,
            };
            using var source = opening == "a source it does not own" ? new CountingSource(bytes) : null;
            var document = opening switch
            {
                "bytes" => PdfDocument.Open(bytes),
                "a path" => PdfDocument.Open(path!),
                "a source it does not own" => PdfDocument.Open(source!, options: null, ownsSource: false),
                _ => PdfDocument.Open(stream!),
            };

            // Taken before: a page, cached; its content stream, cached and its data read; a stream whose data was not
            // read; an object of an object stream read, so cached, and one of the same stream not read. A stream an object
            // stream holds, which ISO 32000-1 forbids, reads as null, as does an array there holding one (#161).
            var page = document.GetObject(new PdfObjectId(3)).AsDictionary().Required();
            var contents = page.GetRaw(PdfName.Contents).Should().BeOfType<PdfReference>().Subject;
            var read = contents.Resolve().Should().BeOfType<PdfStream>().Subject;
            read.GetRawBytes().Length.Should().Be(18);
            var unread = document.GetObject(new PdfObjectId(7)).Should().BeOfType<PdfStream>().Subject;
            var states = page.GetDictionary(PdfName.Get("Resources")).Required().GetDictionary(PdfName.Get("ExtGState")).Required();
            states.GetDictionary(PdfName.Get("A")).Should().NotBeNull();
            var packed = states.GetRaw(PdfName.Get("A")).Should().BeOfType<PdfReference>().Subject;
            var packedUnread = states.GetRaw(PdfName.Get("B")).Should().BeOfType<PdfReference>().Subject;
            document.GetObject(new PdfObjectId(8)).Should().BeSameAs(PdfNull.Instance);
            document.GetObject(new PdfObjectId(9)).Should().BeSameAs(PdfNull.Instance);
            using var numbers = document.ObjectNumbers.GetEnumerator();
            numbers.MoveNext().Should().BeTrue();

            document.Dispose();

            document.Diagnostics.Select(entry => entry.Code).Should().Equal(
                [PdfDiagnosticCodes.ObjectStreamMemberStream, PdfDiagnosticCodes.ObjectStreamMemberStream],
                "the record of the reading keeps answering");
            Disposed(() => document.Trailer);
            Disposed(() => document.Catalog);
            Disposed(() => document.Version);
            Disposed(() => document.WasRepaired);
            Disposed(() => document.ObjectCount);
            Disposed(() => document.ObjectNumbers);
            Disposed(() => document.IsEncrypted);
            Disposed(() => document.GetObject(new PdfObjectId(3)));
            Disposed(() => document.GetObject(new PdfObjectId(2)));
            Disposed(() => numbers.MoveNext());
            Disposed(() => contents.Resolve());
            Disposed(() => packed.Resolve());
            Disposed(() => packedUnread.Resolve());
            Disposed(() => states.GetDictionary(PdfName.Get("B")));
            Disposed(() => page.GetStream(PdfName.Contents));
            Disposed(() => read.GetRawBytes());
            Disposed(() => read.Decode(document.Diagnostics));
            Disposed(() => unread.GetRawBytes());

            read.RawLength.Should().Be(18, "what was handed out stays the caller's");
            read.ToString().Should().Be("<<stream of 18 bytes>>");
            page.ToString().Should().Be("<</Type /Page ... 5 entries>>");
            FluentActions.Invoking(document.Dispose).Should().NotThrow();
            source?.Disposals.Should().Be(0, "a source the document did not own stays the caller's");
        }
        finally
        {
            if (path is not null)
            {
                File.Delete(path);
            }
        }

        static void Disposed(Func<object?> act) =>
            FluentActions.Invoking(act).Should().Throw<ObjectDisposedException>()
                .Which.ObjectName.Should().Be(typeof(PdfDocument).FullName);
    }

    [Fact]
    public void What_was_taken_from_a_document_read_in_the_caller_s_memory_reads_none_of_what_it_holds_once_disposed()
    {
        // The caller reuses its buffer once the document is disposed, as a pool does: a reference or a stream taken
        // before reads neither the bytes the document was read from nor those the caller wrote since.
        var buffer = HeldObjectsDocument();
        var document = PdfDocument.Open(buffer);
        var contents = document.GetObject(new PdfObjectId(3)).AsDictionary().Required().GetRaw(PdfName.Contents)!;
        var unread = document.GetObject(new PdfObjectId(7)).Should().BeOfType<PdfStream>().Subject;

        document.Dispose();
        SampleDocument("BT (Another file) Tj ET").BuildWithXRefStream(rootNumber: 1).CopyTo(buffer, 0);

        FluentActions.Invoking(contents.Resolve).Should().Throw<ObjectDisposedException>();
        FluentActions.Invoking(() => unread.GetRawBytes()).Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void Closing_a_reader_again_does_nothing_more()
    {
        var source = new CountingSource(SampleDocument().BuildClassic(rootNumber: 1));
        var reader = new PdfFileReader(
            source,
            new PdfDiagnostics(),
            new PdfLimitGuard(PdfReaderLimits.Default, throwOnLimit: false),
            cacheCapacity: 64,
            ownsSource: true);

        reader.Close();
        var closed = reader.Source;
        reader.Close();

        source.Disposals.Should().Be(1, "the source the reader owns is released once");
        reader.Source.Should().BeSameAs(closed, "a second close changes nothing the first left");
        FluentActions.Invoking(() => reader.GetObject(new PdfObjectId(1))).Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void Disposing_lets_go_of_the_index_the_object_streams_and_what_was_noted_of_each_object()
    {
        // A stream whose /Length is wrong and an object that lost its endobj, read before: what the reader noted of
        // them, its index and the object streams it decoded go once the document is disposed.
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .WithObject(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R >>")
            .WithObject(4, "<< /Length 99 >>\nstream\nBT (Bonjour) Tj ET\nendstream")
            .WithObject(5, "<< /Type /ExtGState /CA 1 >>")
            .BuildWithXRefStream(rootNumber: 1, compressedObjects: [5]);
        var damaged = Encoding.Latin1.GetString(file).Replace("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R >>\nendobj", "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R >>\n      ", StringComparison.Ordinal);
        var document = PdfDocument.Open(Encoding.Latin1.GetBytes(damaged));
        var reader = document.Reader;

        foreach (var number in document.ObjectNumbers.ToList())
        {
            _ = document.GetObject(new PdfObjectId(number));
        }

        reader.TryGetStreamLengthFault(4, out _).Should().BeTrue("the stream's /Length was found wrong");
        reader.IsEndObjMissing(3, out _).Should().BeTrue("the page lost its endobj");
        reader.ObjectStreamBytes.Should().BePositive();

        document.Dispose();

        reader.TryGetStreamLengthFault(4, out _).Should().BeFalse();
        reader.IsEndObjMissing(3, out _).Should().BeFalse();
        reader.ObjectStreamBytes.Should().Be(0);
        reader.ObjectCount.Should().Be(0);
        reader.ChainIndex.Required().Count.Should().Be(0, "the copy of the chain's index goes as well");
    }

    [Fact]
    public void Disposing_lets_go_of_the_copy_of_the_chain_s_index()
    {
        // Every row three bytes off: each object is found near it, and the reader keeps the chain's index beside its own.
        var document = PdfDocument.Open(SampleDocument().BuildClassic(rootNumber: 1, offsetError: 3));
        var reader = document.Reader;

        foreach (var number in document.ObjectNumbers.ToList())
        {
            _ = document.GetObject(new PdfObjectId(number));
        }

        document.WasRepaired.Should().BeFalse("each object was found near its row");
        reader.ChainIndex.Should().NotBeSameAs(reader.Index, "the reader corrected its own index");

        document.Dispose();

        reader.ChainIndex.Required().Count.Should().Be(0);
    }

    [Fact]
    public void A_reference_s_source_cannot_close_the_document_s_file()
    {
        var path = Path.GetTempFileName();

        try
        {
            File.WriteAllBytes(path, HeldObjectsDocument());
            using var document = PdfDocument.Open(path);
            var page = document.GetObject(new PdfObjectId(3)).AsDictionary().Required();
            var contents = page.GetRaw(PdfName.Contents).Should().BeOfType<PdfReference>().Subject;

            (contents.Source as IDisposable)?.Dispose();

            contents.Source.Should().NotBeAssignableTo<IDisposable>("no holder of a reference may close the document's file");
            contents.Resolve().Should().BeOfType<PdfStream>().Which.GetRawBytes().Length.Should().Be(18);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Disposing_lets_go_of_what_the_document_kept_though_a_reference_taken_from_it_is_held()
    {
        var (held, cached, source) = OpenTakeAndDispose();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        cached.IsAlive.Should().BeFalse("the objects the document cached are let go");
        source.IsAlive.Should().BeFalse("and so is the file it was read from");
        GC.KeepAlive(held);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static (PdfObject Held, WeakReference Cached, WeakReference Source) OpenTakeAndDispose()
    {
        var source = PdfFileSource.FromMemory(HeldObjectsDocument());
        var document = PdfDocument.Open(source, options: null, ownsSource: true);
        var page = document.GetObject(new PdfObjectId(3));
        var taken = (page.AsDictionary().Required().GetRaw(PdfName.Contents)!, new WeakReference(page), new WeakReference(source));

        document.Dispose();
        return taken;
    }

    /// <summary>
    /// A page whose content stream, a stream beside it, two objects of an object stream, and two streams an object stream
    /// holds, as no file should, are what a caller takes from a document before disposing of it.
    /// </summary>
    private static byte[] HeldObjectsDocument() =>
        new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .WithObject(
                3,
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /ExtGState << /A 5 0 R /B 6 0 R >> >> >>")
            .Stream(4, string.Empty, "BT (Bonjour) Tj ET")
            .WithObject(5, "<< /Type /ExtGState /CA 1 >>")
            .WithObject(6, "<< /Type /ExtGState /CA 0.5 >>")
            .Stream(7, string.Empty, "q Q")
            .WithObject(8, "<< /Length 3 >>\nstream\nabc\nendstream")
            .WithObject(9, "[<< /Length 3 >>\nstream\nxyz\nendstream]")
            .BuildWithXRefStream(rootNumber: 1, compressedObjects: [5, 6, 8, 9]);

    [Fact]
    public void Reads_a_cross_reference_stream_that_leaves_out_the_type_field()
    {
        // /W [0 4 2]: with no type field, every row is an object at an offset (ISO 32000-1, Table 17).
        var file = new List<byte>();
        void Append(string text) => file.AddRange(Encoding.Latin1.GetBytes(text));

        Append("%PDF-1.5\n");
        var catalog = file.Count;
        Append("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        var pages = file.Count;
        Append("2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n");
        var stream = file.Count;
        Append("3 0 obj\n<< /Type /XRef /Size 4 /Index [1 3] /W [0 4 2] /Root 1 0 R /Length 18 >>\nstream\n");
        foreach (var offset in new[] { catalog, pages, stream })
        {
            file.AddRange([(byte)(offset >> 24), (byte)(offset >> 16), (byte)(offset >> 8), (byte)offset, 0, 0]);
        }

        Append($"\nendstream\nendobj\nstartxref\n{stream}\n%%EOF\n");

        using var document = PdfDocument.Open(file.ToArray());

        document.WasRepaired.Should().BeFalse();
        document.Catalog.GetDictionary(PdfName.Pages).IsOfType(PdfName.Pages).Should().BeTrue();
    }

    [Fact]
    public void Reads_what_an_object_stream_header_lists_before_it_goes_wrong()
    {
        // The header lists object 2, then 3 at an offset that is not a number.
        var file = Packed(header => header[..6] + "x ");
        using var document = PdfDocument.Open(file);

        document.GetObject(new PdfObjectId(2)).AsDictionary().IsOfType(PdfName.Pages).Should().BeTrue();
        document.GetObject(new PdfObjectId(3)).Should().BeSameAs(PdfNull.Instance);
        var report = document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.ObjectStreamUnreadable).Which;
        report.Severity.Should().Be(PdfDiagnosticSeverity.Warning);
        report.Position.Should().Be(ObjectStreamDataStart(file));
        report.Message.Should().Be(
            "Object stream 4 has a header that holds something other than an object number and an offset after 1 of the 2 objects its /N declares; only the first 1 of its 2 objects can be read from it.");
        document.Diagnostics.Should().NotContain(entry => entry.Code == PdfDiagnosticCodes.SyntaxUnexpectedToken);
    }

    [Theory]
    [InlineData("4294967298")]
    [InlineData("92233720368547758082")]
    [InlineData("2147483648")]
    [InlineData("0")]
    [InlineData("-2")]
    public void Reads_no_member_an_object_stream_header_numbers_as_no_object_can_be(string number)
    {
        // The header lists object 2 under a number no object has: narrowed to an int, 4294967298 was 2, and
        // 92233720368547758082 wrapped to it (#157). A number past a long is a real, which ends the header as any token
        // other than an integer does.
        var file = Packed(header => number + header[1..]);
        using var document = PdfDocument.Open(file);

        document.GetObject(new PdfObjectId(2)).Should().BeSameAs(PdfNull.Instance);
        document.GetObject(new PdfObjectId(3)).Should().BeSameAs(PdfNull.Instance);
        var report = document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.ObjectStreamUnreadable).Which;
        report.Position.Should().Be(ObjectStreamDataStart(file));
        report.Message.Should().Be(number.Length > 19
            ? "Object stream 4 has a header that holds something other than an object number and an offset after 0 of the 2 objects its /N declares; none of its objects can be read from it."
            : $"Object stream 4 has a header that lists object {number} at offset 0, which no member can be, after 0 of the 2 objects its /N declares; none of its objects can be read from it.");
    }

    [Fact]
    public void Reads_an_object_stream_header_that_lists_the_largest_number_a_member_can_have()
    {
        // The header is read, whatever the entries then find in it.
        using var document = PdfDocument.Open(Packed(header => "2147483647 0 " + header[4..]));

        document.GetObject(new PdfObjectId(3)).AsDictionary().IsOfType(PdfName.Page).Should().BeTrue();
        document.Diagnostics.Should().NotContain(entry => entry.Code == PdfDiagnosticCodes.ObjectStreamUnreadable);
    }

    [Theory]
    [InlineData("6 0 7 0 ", "lists object 7 at offset 0, not past the offset of the object before it, 0")]
    [InlineData("6 6 7 0 ", "lists object 7 at offset 0, not past the offset of the object before it, 6")]
    [InlineData("6 0 7 99 ", "lists object 7 at offset 99, past what its {0} bytes of decoded data hold")]
    [InlineData("6 0 7 13 ", "lists object 7 at offset 13, past what its {0} bytes of decoded data hold")]
    public void Ends_an_object_stream_header_at_a_member_that_starts_before_the_one_before_it_ends_or_past_the_data(string header, string pair)
    {
        // The members are "(six)" at offset 0 and "(sevn)" at offset 6, thirteen bytes with their ends of line. Offsets must
        // increase (7.5.7), so that each member is read to the next one's start; an offset that does not, or a member that
        // would start at or past the end of the data, ends the header as a pair no member can have does (#160, #202).
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(6, "(six)")
            .WithObject(7, "(sevn)")
            .BuildWithXRefStream(rootNumber: 1, compressedObjects: [6, 7], objectStreamHeader: _ => header);

        using var document = PdfDocument.Open(file);

        document.GetObject(new PdfObjectId(6)).AsText().Should().Be(header.StartsWith("6 6", StringComparison.Ordinal) ? "sevn" : "six");
        document.GetObject(new PdfObjectId(7)).Should().BeSameAs(PdfNull.Instance);
        var report = document.Diagnostics.Should().ContainSingle().Which;
        report.Code.Should().Be(PdfDiagnosticCodes.ObjectStreamUnreadable);
        report.Position.Should().Be(ObjectStreamDataStart(file));
        report.Message.Should().Be(string.Create(
            CultureInfo.InvariantCulture,
            $"Object stream 8 has a header that {string.Format(CultureInfo.InvariantCulture, pair, header.Length + 13)}, after 1 of the 2 objects its /N declares; only the first 1 of its 2 objects can be read from it."));
    }

    [Theory]
    [InlineData("6 13 7 14 ")]
    [InlineData("6 2147483647 7 0 ")]
    public void Reads_none_of_an_object_stream_whose_first_member_starts_past_its_data(string header)
    {
        // Thirteen bytes of members: the first starts at their end, or past what an offset into them can reach.
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(6, "(six)")
            .WithObject(7, "(sevn)")
            .BuildWithXRefStream(rootNumber: 1, compressedObjects: [6, 7], objectStreamHeader: _ => header);
        var offset = header[2..header.IndexOf(" 7", StringComparison.Ordinal)];

        using var document = PdfDocument.Open(file);

        document.GetObject(new PdfObjectId(6)).Should().BeSameAs(PdfNull.Instance);
        document.GetObject(new PdfObjectId(7)).Should().BeSameAs(PdfNull.Instance);
        document.Diagnostics.Should().ContainSingle().Which.Message.Should().Be(string.Create(
            CultureInfo.InvariantCulture,
            $"Object stream 8 has a header that lists object 6 at offset {offset}, past what its {header.Length + 13} bytes of decoded data hold, after 0 of the 2 objects its /N declares; none of its objects can be read from it."));
    }

    [Theory]
    [InlineData("/Type /XObj  ")]
    [InlineData("/Kind /ObjStm")]
    public void Reads_none_of_the_members_of_a_stream_that_is_not_of_type_object_stream(string type)
    {
        // The validator and the rebuild read no member from such a stream, nor does the reader (#274). The replacement
        // keeps every offset.
        var file = Encoding.Latin1.GetBytes(Encoding.Latin1.GetString(Packed(header => header)).Replace("/Type /ObjStm", type, StringComparison.Ordinal));

        using var document = PdfDocument.Open(file);

        document.GetObject(new PdfObjectId(3)).Should().BeSameAs(PdfNull.Instance);
        document.Reader.KeptObjectStreamData(4).IsEmpty.Should().BeTrue("the reader keeps the stream as one that serves no object, and no data of it");
        var report = document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.ObjectStreamUnreadable).Which;
        report.Position.Should().Be(Encoding.Latin1.GetString(file).IndexOf("stream\n", Encoding.Latin1.GetString(file).IndexOf("/N 2", StringComparison.Ordinal), StringComparison.Ordinal) + "stream\n".Length);
        report.Message.Should().Be("Object stream 4 is a stream that is not of /Type /ObjStm; none of its objects can be read from it.");
    }

    [Fact]
    public void Finds_an_object_its_entry_misplaces_at_the_first_place_its_object_stream_lists_it()
    {
        // Object 6 is listed twice, at indexes 0 and 1, and its entry gives index 5: it is the first that is read.
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(6, "(six)")
            .WithObject(7, "(sevn)")
            .BuildWithXRefStream(rootNumber: 1, compressedObjects: [6, 7], objectStreamHeader: _ => "6 0 6 6 ");
        var text = Encoding.Latin1.GetString(file);
        var rows = text.IndexOf("stream\n", text.IndexOf("/Type /XRef", StringComparison.Ordinal), StringComparison.Ordinal) + "stream\n".Length;
        file[rows + (6 * 7) + 6] = 5;

        using var document = PdfDocument.Open(file);

        document.GetObject(new PdfObjectId(6)).AsText().Should().Be("six");
        document.Diagnostics.Should().ContainSingle().Which.Message.Should().Be(
            "Object 6 is at index 0 of object stream 8, not at index 5, where the cross-reference index places it.");
    }

    [Theory]
    [InlineData(-5L, false)]
    [InlineData(2147483648L, false)]
    [InlineData(4294967296L, true)]
    public void Reads_no_member_an_object_stream_header_places_where_no_member_can_start(long offset, bool fromItsOwn)
    {
        // Object 3's offset, negative, past an int, or its own plus 2^32, which narrowed to an int was its own again.
        var written = 0L;
        var file = Packed(header =>
        {
            var own = long.Parse(header.AsSpan(6, header.Length - 7), CultureInfo.InvariantCulture);
            written = fromItsOwn ? own + offset : offset;
            return string.Create(CultureInfo.InvariantCulture, $"{header[..6]}{written} ");
        });
        using var document = PdfDocument.Open(file);

        document.GetObject(new PdfObjectId(2)).AsDictionary().IsOfType(PdfName.Pages).Should().BeTrue();
        document.GetObject(new PdfObjectId(3)).Should().BeSameAs(PdfNull.Instance);
        document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.ObjectStreamUnreadable).Which.Message.Should().Be(
            string.Create(
                CultureInfo.InvariantCulture,
                $"Object stream 4 has a header that lists object 3 at offset {written}, which no member can be, after 1 of the 2 objects its /N declares; only the first 1 of its 2 objects can be read from it."));
    }

    [Fact]
    public void Reads_an_object_where_its_stream_s_header_lists_it_rather_than_where_the_index_says()
    {
        // The header lists 3 at index 0 and 2 at index 1; the index places 2 at index 0.
        var file = Packed(header => "3" + header[1..4] + "2" + header[5..]);
        using var document = PdfDocument.Open(file);

        document.GetObject(new PdfObjectId(2)).AsDictionary().IsOfType(PdfName.Page).Should().BeTrue();
        var report = document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.ObjectStreamMemberMoved).Which;
        report.Severity.Should().Be(PdfDiagnosticSeverity.Repair);
        report.Position.Should().Be(ObjectStreamDataStart(file));
        report.Message.Should().Be("Object 2 is at index 1 of object stream 4, not at index 0, where the cross-reference index places it.");
        document.Diagnostics.Should().NotContain(entry => entry.Code == PdfDiagnosticCodes.XRefOffsetAdjusted);
    }

    [Fact]
    public void Reads_an_object_whose_index_lies_past_its_stream_s_header_where_the_header_lists_it()
    {
        // The header lists object 3 alone, at index 0, and ends there; the index places 3 at index 1.
        var file = Packed(header => header[4..]);
        using var document = PdfDocument.Open(file);

        document.GetObject(new PdfObjectId(3)).AsDictionary().IsOfType(PdfName.Page).Should().BeTrue();
        document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.ObjectStreamUnreadable)
            .Which.Message.Should().Be(
                "Object stream 4 has a header that ends after 1 of the 2 objects its /N declares; only the first 1 of its 2 objects can be read from it.");
        var moved = document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.ObjectStreamMemberMoved).Which;
        moved.Position.Should().Be(ObjectStreamDataStart(file));
        moved.Message.Should().Be("Object 3 is at index 0 of object stream 4, not at index 1, where the cross-reference index places it.");
    }

    [Fact]
    public void Places_a_fault_met_inside_an_object_stream_member_where_the_stream_s_data_starts()
    {
        // A byte of decoded data is no offset in the file: the report is placed at the stream, the byte in its message.
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .WithObject(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Rotate ) >>")
            .BuildWithXRefStream(rootNumber: 1, compressedObjects: [2, 3]);
        var dataStart = ObjectStreamDataStart(file);
        var stray = Encoding.Latin1.GetString(file).IndexOf(')', (int)dataStart) - dataStart;

        using var document = PdfDocument.Open(file);
        document.GetObject(new PdfObjectId(3)).AsDictionary().IsOfType(PdfName.Page).Should().BeTrue();

        var report = document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.SyntaxUnexpectedToken).Which;
        report.Position.Should().Be(dataStart);
        report.Message.Should().Be(string.Create(
            CultureInfo.InvariantCulture,
            $"A token was found where a value was expected. It was met in object 3, at byte {stray} of object stream 4's decoded data."));
    }

    [Fact]
    public void Says_that_an_object_stream_s_data_rather_than_the_file_ended_in_the_middle_of_a_member()
    {
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(3, "<< /Type /Page /Rotate")
            .BuildWithXRefStream(rootNumber: 1, compressedObjects: [2, 3]);
        var dataStart = ObjectStreamDataStart(file);
        var opens = Encoding.Latin1.GetString(file).IndexOf("<< /Type /Page /Rotate", (int)dataStart, StringComparison.Ordinal) - dataStart;

        using var document = PdfDocument.Open(file);
        _ = document.GetObject(new PdfObjectId(3));

        // The member is a dictionary the data ends inside, after a key: it is placed where the dictionary opens (#119).
        var report = document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.SyntaxTruncatedObject).Which;
        report.Position.Should().Be(dataStart);
        report.Message.Should().Be(string.Create(
            CultureInfo.InvariantCulture,
            $"The object stream's decoded data ended inside a dictionary, which was never closed, before the value of its last key. It was met in object 3, at byte {opens} of object stream 4's decoded data."));
    }

    [Theory]
    [InlineData("<< /Length 99 >>\nstream\nabc\nendstream")]
    [InlineData("[1 << /Length 3 >> stream\nabc\nendstream]")]
    [InlineData("<< /Inner << /Length 3 >> stream\nabc\nendstream >>")]
    public void Reads_an_object_written_as_a_stream_inside_an_object_stream_as_null_and_reports_it_once(string member)
    {
        // An object stream cannot hold a stream (7.5.7): the member, or the member holding one, is no value; its data is
        // not read, and nothing of its length reported. Read again once the cache let it go, it is not reported again (#161).
        var builder = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(3, member);

        for (var number = 10; number < 90; number++)
        {
            builder.WithObject(number, "(filler)");
        }

        var file = builder.BuildWithXRefStream(rootNumber: 1, compressedObjects: [2, 3]);
        var dataStart = ObjectStreamDataStart(file);
        var opens = Encoding.Latin1.GetString(file).IndexOf(member, (int)dataStart, StringComparison.Ordinal) - dataStart;

        using var document = PdfDocument.Open(file, PdfReaderOptions.Default with { ObjectCacheCapacity = 64 });

        for (var round = 0; round < 2; round++)
        {
            document.GetObject(new PdfObjectId(3)).Should().BeSameAs(PdfNull.Instance);

            for (var number = 10; number < 90; number++)
            {
                document.GetObject(new PdfObjectId(number));
            }
        }

        var report = document.Diagnostics.Should().ContainSingle().Which;
        report.Code.Should().Be(PdfDiagnosticCodes.ObjectStreamMemberStream);
        report.Severity.Should().Be(PdfDiagnosticSeverity.Warning);
        report.Position.Should().Be(dataStart);
        report.Message.Should().Be(string.Create(
            CultureInfo.InvariantCulture,
            $"Object 3, at byte {opens} of object stream 90's decoded data, is written as a stream, which an object stream cannot hold; it reads as null."));
    }

    [Theory]
    [InlineData("/First 4", "has no /N that gives a count of objects; none of its objects can be read from it.")]
    [InlineData("/N -1 /First 4", "has no /N that gives a count of objects; none of its objects can be read from it.")]
    [InlineData("/N 1", "has no /First that gives where its objects start; none of its objects can be read from it.")]
    [InlineData("/N 1 /First -4", "has no /First that gives where its objects start; none of its objects can be read from it.")]
    [InlineData("/N 9 /First 4", "has a header that ends after 1 of the 9 objects its /N declares; only the first 1 of its 9 objects can be read from it.")]
    [InlineData("/N 1 /First 40", "gives in /First an offset, 40, past the end of its decoded data, which is 8 bytes long; none of its objects can be read from it.")]
    [InlineData("/N 2 /First 4", "has a header that ends after 1 of the 2 objects its /N declares; only the first 1 of its 2 objects can be read from it.")]
    [InlineData("/N 1 /First 0", "has a header that ends after 0 of the 1 object its /N declares; none of its objects can be read from it.")]
    public void Reports_an_object_stream_whose_dictionary_or_header_cannot_be_believed_where_its_data_starts(string entries, string fault)
    {
        // The index is rebuilt, which reads every object stream the file holds: object stream 5 holds object 6.
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(5, $"<< /Type /ObjStm {entries} /Length 8 >>\nstream\n6 0 <<>>\nendstream")
            .BuildClassic(rootNumber: 1, includeXRef: false);

        using var document = PdfDocument.Open(file);

        var report = document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.ObjectStreamUnreadable).Which;
        report.Severity.Should().Be(PdfDiagnosticSeverity.Warning);
        report.Position.Should().Be(ObjectStreamDataStart(file));
        report.Message.Should().Be("Object stream 5 " + fault);
    }

    [Fact]
    public void Reports_an_object_stream_whose_header_ends_after_an_object_number_with_no_offset()
    {
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(5, "<< /Type /ObjStm /N 2 /First 6 /Length 10 >>\nstream\n6 0 7 <<>>\nendstream")
            .BuildClassic(rootNumber: 1, includeXRef: false);

        using var document = PdfDocument.Open(file);

        document.GetObject(new PdfObjectId(6)).Should().BeOfType<PdfDictionary>();
        document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.ObjectStreamUnreadable).Which.Message.Should().Be(
            "Object stream 5 has a header that ends after 1 of the 2 objects its /N declares; only the first 1 of its 2 objects can be read from it.");
    }

    [Fact]
    public void Leaves_a_first_past_data_a_guard_cut_to_the_guard_s_report()
    {
        // The decoded-stream guard stops the object stream's data before its /First: the stream is not at fault.
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .WithObject(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>")
            .BuildWithXRefStream(rootNumber: 1, compressedObjects: [2, 3], objectStreamHeader: header => header + new string(' ', 200), compressObjectStream: true);
        var options = PdfReaderOptions.Default with { Limits = PdfReaderLimits.Default with { MaxDecodedStreamLength = 100 } };

        using var document = PdfDocument.Open(file, options);
        document.GetObject(new PdfObjectId(2)).Should().BeSameAs(PdfNull.Instance);

        document.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.LimitDecodedStream);
    }

    [Theory]
    [InlineData("6 x <<>>", "/N 1 /First 4")]
    [InlineData("/X <<>>", "/N 1 /First 3")]
    public void Reports_an_object_stream_whose_header_starts_with_a_stray_token_as_serving_none_of_its_objects(string data, string entries)
    {
        // A stray object number is the fault, though the header ends right after it.
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(5, $"<< /Type /ObjStm {entries} /Length {data.Length} >>\nstream\n{data}\nendstream")
            .BuildClassic(rootNumber: 1, includeXRef: false);

        using var document = PdfDocument.Open(file);

        document.GetObject(new PdfObjectId(6)).Should().BeSameAs(PdfNull.Instance);
        document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.ObjectStreamUnreadable).Which.Message.Should().Be(
            "Object stream 5 has a header that holds something other than an object number and an offset after 0 of the 1 object its /N declares; none of its objects can be read from it.");
    }

    [Theory]
    [InlineData(64)]
    [InlineData(1_000_000)]
    public void Reports_an_object_at_another_index_of_its_stream_once_however_often_it_is_parsed(int cacheCapacity)
    {
        // The header swaps objects 2 and 3: both entries are wrong. Between two readings of them, a hundred other
        // objects push them out of a cache of 64, which parses them again every time.
        var builder = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .WithObject(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>");

        for (var number = 10; number < 110; number++)
        {
            builder.WithObject(number, string.Create(CultureInfo.InvariantCulture, $"({number})"));
        }

        var file = builder.BuildWithXRefStream(rootNumber: 1, compressedObjects: [2, 3], objectStreamHeader: header => "3" + header[1..4] + "2" + header[5..]);
        using var document = PdfDocument.Open(file, PdfReaderOptions.Default with { ObjectCacheCapacity = cacheCapacity });

        for (var round = 0; round < 3; round++)
        {
            document.GetObject(new PdfObjectId(2)).AsDictionary().IsOfType(PdfName.Page).Should().BeTrue();
            document.GetObject(new PdfObjectId(3)).AsDictionary().IsOfType(PdfName.Pages).Should().BeTrue();

            for (var number = 10; number < 110; number++)
            {
                document.GetObject(new PdfObjectId(number)).Should().BeOfType<PdfString>();
            }
        }

        document.Diagnostics.Where(entry => entry.Code == PdfDiagnosticCodes.ObjectStreamMemberMoved).Select(entry => entry.Message).Should().Equal(
            "Object 2 is at index 1 of object stream 110, not at index 0, where the cross-reference index places it.",
            "Object 3 is at index 0 of object stream 110, not at index 1, where the cross-reference index places it.");
    }

    [Fact]
    public void An_object_its_stream_s_header_does_not_list_is_null()
    {
        using var document = PdfDocument.Open(Packed(header => header.Replace("3 ", "7 ", StringComparison.Ordinal)));

        document.GetObject(new PdfObjectId(3)).Should().BeSameAs(PdfNull.Instance);
    }

    [Fact]
    public void An_object_stream_that_declares_no_object_needs_no_first_and_is_not_reported()
    {
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(5, "<< /Type /ObjStm /N 0 /Length 0 >>\nstream\n\nendstream")
            .BuildClassic(rootNumber: 1, includeXRef: false);

        using var document = PdfDocument.Open(file);

        document.Diagnostics.Should().NotContain(entry => entry.Code == PdfDiagnosticCodes.ObjectStreamUnreadable);
    }

    [Fact]
    public void An_object_stream_that_declares_no_object_serves_none_and_is_not_reported()
    {
        var bytes = Packed(header => header);
        var text = Encoding.Latin1.GetString(bytes);
        using var document = PdfDocument.Open(Encoding.Latin1.GetBytes(text.Replace("/N 2", "/N 0", StringComparison.Ordinal)));

        document.GetObject(new PdfObjectId(2)).Should().BeSameAs(PdfNull.Instance);
        document.Diagnostics.Should().BeEmpty();
    }

    private const string KeptRule =
        " Of each number, the definition kept is the one the file writes last, an object stream's members counting where the stream lies.";

    [Fact]
    public void Reports_an_object_a_rebuild_finds_defined_twice_in_the_file_once_as_information()
    {
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(4, "(first)")
            .WithObject(4, "(second)")
            .BuildClassic(rootNumber: 1, includeXRef: false);

        using var document = PdfDocument.Open(file);

        document.GetObject(new PdfObjectId(4)).Should().BeOfType<PdfString>().Which.ToText().Should().Be("second");
        var report = document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.ObjectRedefined).Which;
        report.Severity.Should().Be(PdfDiagnosticSeverity.Information);
        report.Position.Should().Be(-1);
        report.Message.Should().Be("Rebuilding the index met a second definition of object 4." + KeptRule);
    }

    [Theory]
    [InlineData("a direct definition, then an object stream", "in the object stream")]
    [InlineData("an object stream, then a direct definition", "written directly")]
    [InlineData("two object streams", "in the later object stream")]
    [InlineData("two object streams, the later one's number defined before both", "in the later object stream")]
    [InlineData("one object stream listing it twice", "listed first")]
    public void A_rebuild_keeps_the_definition_the_file_writes_last_a_member_counting_where_its_stream_lies(string shape, string kept)
    {
        // Object 6 is defined twice; the index is rebuilt. A member of an object stream counts where the stream lies,
        // whatever order the object streams are taken in, which follows where each number was first met (#189).
        var builder = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>");

        switch (shape)
        {
            case "a direct definition, then an object stream":
                builder.WithObject(6, "(written directly)").WithObject(10, ObjectStreamOf((6, "(in the object stream)")));
                break;
            case "an object stream, then a direct definition":
                builder.WithObject(10, ObjectStreamOf((6, "(in the object stream)"))).WithObject(6, "(written directly)");
                break;
            case "two object streams":
                builder.WithObject(20, ObjectStreamOf((6, "(in the earlier object stream)")))
                    .WithObject(11, ObjectStreamOf((6, "(in the later object stream)")));
                break;
            case "two object streams, the later one's number defined before both":
                builder.WithObject(11, "null")
                    .WithObject(20, ObjectStreamOf((6, "(in the earlier object stream)")))
                    .WithObject(11, ObjectStreamOf((6, "(in the later object stream)")));
                break;
            default:
                builder.WithObject(10, ObjectStreamOf((6, "(listed first)"), (6, "(listed second)")));
                break;
        }

        using var document = PdfDocument.Open(builder.BuildClassic(rootNumber: 1, includeXRef: false));

        document.GetObject(new PdfObjectId(6)).Should().BeOfType<PdfString>().Which.ToText().Should().Be(kept);
        document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.ObjectRedefined)
            .Which.Message.Should().StartWith("Rebuilding the index met ").And.EndWith(KeptRule);
    }

    [Fact]
    public void An_object_stream_a_later_object_stream_defines_again_serves_none_of_its_members()
    {
        // Object stream 20, written after object stream 10, holds an object numbered 10: the definition of 10 the file writes
        // last is no object stream, and 5, which only the earlier 10 holds, reads as null, though the rebuild decoded that 10
        // as it took it in (#189).
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(10, ObjectStreamOf((5, "(five)")))
            .WithObject(20, ObjectStreamOf((10, "(ten)")))
            .BuildClassic(rootNumber: 1, includeXRef: false);

        using var document = PdfDocument.Open(file);

        document.GetObject(new PdfObjectId(10)).Should().BeOfType<PdfString>().Which.ToText().Should().Be("ten");
        document.GetObject(new PdfObjectId(5)).Should().BeSameAs(PdfNull.Instance);

        // The earlier 10's decoded data no longer counts toward what the reader keeps of object streams: 20's alone does,
        // its data and an entry for its one object.
        document.Reader.ObjectStreamBytes.Should().Be(Encoding.ASCII.GetByteCount("10 0 (ten)\n") + 16);
    }

    [Fact]
    public void An_object_stream_defined_again_twice_after_a_read_through_it_found_none_still_opens()
    {
        // Object stream 10 places 20; object streams 11 and 13, written after it, each hold an object numbered 10, and stream
        // 12, between them, takes its /Length from 20: reading 20 through 10, which 11 has replaced, finds no object stream,
        // which the reader keeps as 10's contents. 13 replaces 10 again, and the rebuild lets go of that, holding nothing
        // (#189).
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(10, ObjectStreamOf((20, "(member of 10)")))
            .WithObject(11, ObjectStreamOf((10, "42")))
            .WithObject(12, "<< /Length 20 0 R >>\nstream\nabc\nendstream")
            .WithObject(13, ObjectStreamOf((10, "43")))
            .BuildClassic(rootNumber: 1, includeXRef: false);

        using var document = PdfDocument.Open(file);

        document.GetObject(new PdfObjectId(10)).AsInteger().Should().Be(43);
    }

    [Fact]
    public void An_obj_a_regular_byte_follows_where_the_search_near_an_entry_ends_is_not_read()
    {
        // The row of 3 places it where no header is; "3 0 obj", its obj ending where the 512 bytes after the row end, runs into
        // a word longer than MaxObjectLength. The byte past them tells it from a header (#189): it is not read, and reaches no
        // guard; 3 is looked for by rebuilding the index, which finds no header of it either.
        var head = new StringBuilder("%PDF-1.4\n");
        var catalog = head.Length;
        head.Append("1 0 obj <</Type/Catalog/Pages 2 0 R>> endobj\n");
        var pages = head.Length;
        head.Append("2 0 obj <</Type/Pages/Kids[]/Count 0/Extra 3 0 R>> endobj\n%").Append(' ', 600).Append('\n');
        var row = head.Length;
        head.Append("/Pad").Append(' ', row + PdfFileReader.NearbySearchRadius - "3 0 obj".Length - head.Length);
        head.Append("3 0 obj").Append('x', 2048).Append('\n');
        var table = head.Length;
        head.Append(string.Create(CultureInfo.InvariantCulture, $"xref\n0 4\n0000000000 65535 f \n{catalog:D10} 00000 n \n{pages:D10} 00000 n \n{row:D10} 00000 n \n"));
        head.Append(string.Create(CultureInfo.InvariantCulture, $"trailer\n<< /Root 1 0 R /Size 4 >>\nstartxref\n{table}\n%%EOF\n"));

        using var document = PdfDocument.Open(
            Encoding.Latin1.GetBytes(head.ToString()), new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = 1024 } });

        document.GetObject(new PdfObjectId(3)).Should().BeSameAs(PdfNull.Instance);
        document.Diagnostics.Should().NotContain(entry => entry.Code == PdfDiagnosticCodes.LimitObject);
    }

    /// <summary>Writes the body of an object stream holding <paramref name="members"/>, uncompressed.</summary>
    private static string ObjectStreamOf(params (int Number, string Body)[] members)
    {
        var header = new StringBuilder();
        var data = new StringBuilder();

        foreach (var (number, body) in members)
        {
            header.Append(CultureInfo.InvariantCulture, $"{number} {data.Length} ");
            data.Append(body).Append('\n');
        }

        var content = header.ToString() + data;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"<< /Type /ObjStm /N {members.Length} /First {header.Length} /Length {content.Length} >>\nstream\n{content}\nendstream");
    }

    [Theory]
    [InlineData(new[] { 4, 4, 4 }, "met 2 definitions of object numbers it had already found, of object 4.")]
    [InlineData(new[] { 4, 4, 5, 5 }, "met 2 definitions of object numbers it had already found, of objects 4, 5.")]
    [InlineData(
        new[] { 10, 10, 11, 11, 12, 12, 13, 13, 14, 14, 15, 15, 16, 16, 17, 17, 18, 18, 19, 19, 20, 20 },
        "met 11 definitions of object numbers it had already found, among them those of objects 10, 11, 12, 13, 14, 15, 16, 17, 18, 19.")]
    public void Lists_the_first_numbers_a_rebuild_finds_defined_more_than_once(int[] numbers, string met)
    {
        var builder = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>");

        foreach (var number in numbers)
        {
            builder.WithObject(number, "null");
        }

        using var document = PdfDocument.Open(builder.BuildClassic(rootNumber: 1, includeXRef: false));

        document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.ObjectRedefined)
            .Which.Message.Should().Be("Rebuilding the index " + met + KeptRule);
    }

    [Fact]
    public void Reports_no_redefinition_when_a_rebuild_finds_each_number_once()
    {
        using var document = PdfDocument.Open(SampleDocument().BuildClassic(rootNumber: 1, includeXRef: false));

        document.WasRepaired.Should().BeTrue();
        document.Diagnostics.Should().NotContain(entry => entry.Code == PdfDiagnosticCodes.ObjectRedefined);
    }

    [Theory]
    [InlineData("far from the object's header")]
    [InlineData("its obj ending a piece of the scan")]
    [InlineData("its obj ending a piece of the scan, between two objects")]
    public void A_rebuild_takes_no_obj_a_regular_character_follows_for_a_header(string where)
    {
        // Object 9's string holds "7 0 objx", which a load reads as no header: the scan takes it for none either, and object
        // 7 reads as written (#189). The scan reads the file a megabyte at a time: the byte after an obj that ends one piece
        // is the next piece's first. Between object 9 and a stray endobj, no object holds it.
        byte[] Build(int filler) => new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(7, "(seven)")
            .WithObject(8, "(" + new string('x', filler) + ")")
            .WithObject(9, where.EndsWith("between two objects", StringComparison.Ordinal) ? "null\nendobj\n7 0 objx" : "(7 0 objx)")
            .BuildClassic(rootNumber: 1, includeXRef: false);
        var file = Build(600);

        if (where != "far from the object's header")
        {
            var probe = Build(1_000_000);
            file = Build(1_000_000 + (1024 * 1024) - probe.AsSpan().IndexOf("objx"u8) - 3);
            file.AsSpan().IndexOf("objx"u8).Should().Be((1024 * 1024) - 3);
        }

        using var document = PdfDocument.Open(file);

        document.GetObject(new PdfObjectId(7)).Should().BeOfType<PdfString>().Which.ToText().Should().Be("seven");
        document.Diagnostics.Should().NotContain(entry => entry.Code == PdfDiagnosticCodes.ObjectRedefined);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(2)]
    [InlineData(1)]
    public void A_header_whose_obj_ends_a_piece_of_the_scan_or_runs_into_the_next_is_read_once(int before)
    {
        // The scan reads the file a megabyte at a time, each piece with the byte past it: object 7's obj starts `before`
        // bytes before the first megabyte ends, ending the piece or running into the next one, and is read once (#189).
        byte[] Build(int filler) => new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(9, "(" + new string('x', filler) + ")")
            .WithObject(7, "(seven)")
            .BuildClassic(rootNumber: 1, includeXRef: false);
        var probe = Build(1_000_000);
        var file = Build(1_000_000 + (1024 * 1024) - before - (probe.AsSpan().IndexOf("7 0 obj"u8) + 4));
        file.AsSpan().IndexOf("7 0 obj"u8).Should().Be((1024 * 1024) - before - 4);

        using var document = PdfDocument.Open(file);

        document.GetObject(new PdfObjectId(7)).Should().BeOfType<PdfString>().Which.ToText().Should().Be("seven");
        document.Diagnostics.Should().NotContain(entry => entry.Code == PdfDiagnosticCodes.ObjectRedefined);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_cache_keeps_the_last_objects_read_as_many_as_its_capacity(bool replaced)
    {
        // A hundred objects read one after the other leave the last 64 in a cache of 64: the 64th from the last is served
        // from it, the 65th parsed again. Where the rebuild loaded object 6 written directly, then took in object stream 10,
        // written after it, which holds 6 again, the cache let go of the direct 6, and its turn in the cache's queue comes
        // with nothing to let go: the cache still keeps 64 (#189).
        var builder = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>");

        if (replaced)
        {
            builder.WithObject(6, "(direct)").WithObject(10, ObjectStreamOf((6, "(in the object stream)")));
        }

        for (var number = 100; number < 200; number++)
        {
            builder.WithObject(number, "<< /N " + number.ToString(CultureInfo.InvariantCulture) + " >>");
        }

        using var document = PdfDocument.Open(
            builder.BuildClassic(rootNumber: 1, includeXRef: !replaced), PdfReaderOptions.Default with { ObjectCacheCapacity = 64 });
        var read = new List<PdfObject>();

        for (var number = 100; number < 200; number++)
        {
            read.Add(document.GetObject(new PdfObjectId(number)));
        }

        document.GetObject(new PdfObjectId(200 - 64)).Should().BeSameAs(read[^64]);
        document.GetObject(new PdfObjectId(200 - 65)).Should().NotBeSameAs(read[^65]);
    }

    [Fact]
    public void A_search_near_an_entry_takes_no_obj_a_regular_character_follows_for_the_object_s_header()
    {
        // The row of 7 falls inside object 6's string; "7 0 objx" lies within 512 bytes of it, before the real header: the
        // search near the row passes it, as a load would, and finds 7 (#189).
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"),
            [6] = file.Padding(6, 100),
        };
        rows[7] = rows[6] + 50;
        file.Raw("% 7 0 objx\n");
        var seven = file.Object(7, "(seven)");
        file.Table(8, rows, "/Size 8 /Root 1 0 R");
        using var document = PdfDocument.Open(file.Bytes);

        document.GetObject(new PdfObjectId(7)).Should().BeOfType<PdfString>().Which.ToText().Should().Be("seven");
        document.WasRepaired.Should().BeFalse();
        document.Diagnostics.Should().ContainSingle().Which.Code.Should().Be(PdfDiagnosticCodes.XRefOffsetAdjusted);
        document.Reader.Index.TryGet(7, out var entry).Should().BeTrue();
        entry.Offset.Should().Be(seven);
    }

    [Fact]
    public void Takes_a_header_the_scan_finds_in_the_overlap_of_two_windows_for_one_definition()
    {
        // The scan reads a megabyte at a time, each window overlapping the last by 64 bytes: object 1's header lies
        // 40 bytes before the first megabyte ends, so both windows find it.
        var file = new TestPdfBuilder()
            .WithObject(9, "(" + new string('x', 1_048_503) + ")")
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .BuildClassic(rootNumber: 1, includeXRef: false);
        file.AsSpan().IndexOf("1 0 obj"u8).Should().Be((1024 * 1024) - 40);

        using var document = PdfDocument.Open(file);

        document.WasRepaired.Should().BeTrue();
        document.Catalog.IsOfType(PdfName.Catalog).Should().BeTrue();
        document.Diagnostics.Should().NotContain(entry => entry.Code == PdfDiagnosticCodes.ObjectRedefined);
    }

    [Fact]
    public void Counts_two_definitions_the_scan_finds_in_the_overlap_of_two_windows_as_one_redefinition()
    {
        // Both headers of object 7 lie in the 64 bytes the first and second megabyte's windows share; object 8 takes the
        // file past the first.
        byte[] Build(int filler) => new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(9, "(" + new string('x', filler) + ")")
            .WithObject(7, "null")
            .WithObject(7, "1")
            .WithObject(8, "(" + new string('y', 200) + ")")
            .BuildClassic(rootNumber: 1, includeXRef: false);
        var probe = Build(1_000_000);
        var file = Build(1_000_000 + ((1024 * 1024) - 61) - probe.AsSpan().IndexOf("7 0 obj"u8));
        file.AsSpan().IndexOf("7 0 obj"u8).Should().Be((1024 * 1024) - 61);

        using var document = PdfDocument.Open(file);

        document.GetObject(new PdfObjectId(7)).AsInteger().Should().Be(1);
        document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.ObjectRedefined)
            .Which.Message.Should().Be("Rebuilding the index met a second definition of object 7." + KeptRule);
    }

    [Fact]
    public void Reads_a_header_that_straddles_the_start_of_a_scan_window_under_its_own_number()
    {
        // "123 0 obj" starts a byte before the second megabyte's window, which object 8 makes the file reach: that window
        // must not read it as object 23.
        byte[] Build(int filler) => new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(23, "(the real object 23)")
            .WithObject(9, "(" + new string('x', filler) + ")")
            .WithObject(123, "(object 123)")
            .WithObject(8, "(" + new string('y', 200) + ")")
            .BuildClassic(rootNumber: 1, includeXRef: false);
        var probe = Build(1_000_000);
        var file = Build(1_000_000 + ((1024 * 1024) - 65) - probe.AsSpan().IndexOf("123 0 obj"u8));
        file.AsSpan().IndexOf("123 0 obj"u8).Should().Be((1024 * 1024) - 65);

        using var document = PdfDocument.Open(file);

        document.GetObject(new PdfObjectId(23)).Should().BeOfType<PdfString>().Which.ToText().Should().Be("the real object 23");
        document.GetObject(new PdfObjectId(123)).Should().BeOfType<PdfString>().Which.ToText().Should().Be("object 123");
        document.Diagnostics.Should().NotContain(entry => entry.Code == PdfDiagnosticCodes.ObjectRedefined);
    }

    [Fact]
    public void Writes_the_numbers_a_rebuild_finds_redefined_whatever_the_culture()
    {
        // One object stream lists object 6 a thousand and two times, each at its own offset: the 1,001 definitions past
        // the first are counted with the invariant culture's comma, not with the non-breaking space Swedish groups digits
        // with.
        var header = string.Concat(Enumerable.Range(0, 1002).Select(index => string.Create(CultureInfo.InvariantCulture, $"6 {2 * index} ")));
        var data = header + string.Concat(Enumerable.Repeat("1 ", 1002));
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(5, $"<< /Type /ObjStm /N 1002 /First {header.Length} /Length {data.Length} >>\nstream\n{data}\nendstream")
            .BuildClassic(rootNumber: 1, includeXRef: false);
        var culture = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("sv-SE");
            using var document = PdfDocument.Open(file);

            document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.ObjectRedefined)
                .Which.Message.Should().Be("Rebuilding the index met 1,001 definitions of object numbers it had already found, of object 6." + KeptRule);
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    [Theory]
    [InlineData(" 65536 n ")]
    [InlineData(" -0001 n ")]
    public void Serves_no_older_revision_of_an_object_whose_newest_row_is_refused(string row)
    {
        // The update's row for object 3 gives a generation no object in use can have. Refused, it must not let the row
        // of the section the update's /Prev names stand for the object's current revision: the object is the index's to
        // find, and the rebuild finds the last definition written.
        var original = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(3, "(first)")
            .BuildClassic(rootNumber: 1);
        var text = Encoding.Latin1.GetString(TestPdfBuilder.AppendIncrementalUpdate(original, rootNumber: 1, [(3, "(second)")]));
        var second = text.LastIndexOf("3 0 obj", StringComparison.Ordinal);
        var written = string.Create(CultureInfo.InvariantCulture, $"{second:D10} 00000 n ");
        var file = Encoding.Latin1.GetBytes(text.Replace(written, string.Create(CultureInfo.InvariantCulture, $"{second:D10}") + row, StringComparison.Ordinal));

        using var document = PdfDocument.Open(file);

        document.GetObject(new PdfObjectId(3)).Should().BeOfType<PdfString>().Which.ToText().Should().Be("second");
    }

    [Fact]
    public void Indexes_no_member_a_rebuild_finds_listed_under_a_number_no_object_can_have()
    {
        // Two object streams list -5: the rebuild once indexed it, and met it twice (#157). The stream written last is taken in
        // first, and reported first (#348).
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(5, "<< /Type /ObjStm /N 1 /First 5 /Length 10 >>\nstream\n-5 0 (one)\nendstream")
            .WithObject(7, "<< /Type /ObjStm /N 1 /First 5 /Length 10 >>\nstream\n-5 0 (two)\nendstream")
            .BuildClassic(rootNumber: 1, includeXRef: false);

        using var document = PdfDocument.Open(file);

        document.ObjectNumbers.Should().OnlyContain(number => number > 0);
        document.Diagnostics.Should().NotContain(entry => entry.Code == PdfDiagnosticCodes.ObjectRedefined);
        document.Diagnostics.Where(entry => entry.Code == PdfDiagnosticCodes.ObjectStreamUnreadable).Select(entry => entry.Message)
            .Should().Equal(
                "Object stream 7 has a header that lists object -5 at offset 0, which no member can be, after 0 of the 1 object its /N declares; none of its objects can be read from it.",
                "Object stream 5 has a header that lists object -5 at offset 0, which no member can be, after 0 of the 1 object its /N declares; none of its objects can be read from it.");
    }

    [Fact]
    public void Drops_what_parsing_meets_where_a_guard_cut_an_object_stream_s_data()
    {
        // The data is cut right after object 3's /Parent key: what the parser makes of the end is the guard's.
        const string Pages = "<< /Type /Pages /Kids [3 0 R] /Count 1 >>";
        const string Page = "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>";
        var header = string.Create(CultureInfo.InvariantCulture, $"2 0 3 {Pages.Length + 1} ");
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, Pages)
            .WithObject(3, Page)
            .BuildWithXRefStream(rootNumber: 1, compressedObjects: [2, 3], compressObjectStream: true);
        var cut = header.Length + Pages.Length + 1 + "<< /Type /Page /Parent".Length;
        var options = PdfReaderOptions.Default with { Limits = PdfReaderLimits.Default with { MaxDecodedStreamLength = cut } };

        using var document = PdfDocument.Open(file, options);
        document.GetObject(new PdfObjectId(3)).AsDictionary().IsOfType(PdfName.Page).Should().BeTrue();

        document.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.LimitDecodedStream);
    }

    [Fact]
    public void Rebuilds_an_index_whose_objects_lie_past_the_first_megabyte()
    {
        // The scan reads the file a megabyte at a time: the catalog and the page lie in the second.
        var bytes = new TestPdfBuilder()
            .WithObject(9, "(" + new string('x', 1_100_000) + ")")
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .WithObject(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R >>")
            .Stream(4, string.Empty, "BT (Bonjour) Tj ET")
            .BuildClassic(rootNumber: 1, includeXRef: false);

        using var document = PdfDocument.Open(bytes);

        document.WasRepaired.Should().BeTrue();
        PageContent(document).Should().Be("BT (Bonjour) Tj ET");
    }

    [Fact]
    public void An_object_a_rebuild_does_not_find_either_is_null()
    {
        // The last row is malformed, which leaves the index incomplete: asking for an object it lacks rebuilds it.
        using var document = PdfDocument.Open(PdfTemplate.SoundWith("{row:3}", "0000000abc 00000 n "));
        document.WasRepaired.Should().BeFalse();

        document.GetObject(new PdfObjectId(99)).Should().BeSameAs(PdfNull.Instance);

        document.WasRepaired.Should().BeTrue();
    }

    [Fact]
    public void A_reference_the_chain_cannot_read_rebuilds_nothing_while_the_chain_is_read()
    {
        // Two cross-reference streams, each header one a rebuild's scan does not find (ChainFiles.HiddenHeader). The newer
        // declares rows its data lacks, leaving the index incomplete; the older, which its /Prev names, numbers its rows from
        // object 9, which the index lacks. Resolving it once rebuilt the index before any of its rows was read, and the
        // chain's empty index asked for a rebuild once more (#182). Nothing is loaded while the chain is read: the reference
        // names nothing the chain can read, the older stream is malformed, and the empty index the chain leaves is rebuilt
        // once. The newer stream, read only in part, is reported first (#210).
        const string Template = """
            %PDF-1.5
            {seven}
            << /Type /XRef /W [1 2 0] /Index [0 2] /Prev 00000 /Length 0 >>
            stream

            endstream
            endobj
            {eight}
            << /Type /XRef /W [1 2 0] /Index [9 0 R 1] /Length 0 >>
            stream

            endstream
            endobj
            startxref
            10
            %%EOF

            """;
        var text = Template.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("{seven}", ChainFiles.HiddenHeader(7), StringComparison.Ordinal)
            .Replace("{eight}", ChainFiles.HiddenHeader(8), StringComparison.Ordinal);
        var older = text.IndexOf(ChainFiles.HiddenHeader(8), StringComparison.Ordinal) + ChainFiles.HiddenHeaderLead;
        var file = Encoding.ASCII.GetBytes(text.Replace("/Prev 00000", string.Create(CultureInfo.InvariantCulture, $"/Prev {older:D5}"), StringComparison.Ordinal));
        var diagnostics = new PdfDiagnostics();

        var reader = new PdfFileReader(
            PdfFileSource.FromMemory(file),
            diagnostics,
            new PdfLimitGuard(PdfReaderLimits.Default, throwOnLimit: false),
            cacheCapacity: 64,
            ownsSource: true);

        reader.ObjectCount.Should().Be(0);
        reader.WasRepaired.Should().BeTrue();
        reader.Structure.ChainRead.Should().BeFalse();
        reader.Structure.Sections[1].Fault.Should().Be("its /Index holds the reference 9 0 R, which no section read before it places where it can be read");
        diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(
            PdfDiagnosticCodes.XRefSectionUnreadable, PdfDiagnosticCodes.XRefSectionUnreadable, PdfDiagnosticCodes.XRefRebuilt);
        diagnostics[0].Message.Should().StartWith(
            "The cross-reference stream startxref names at offset 10 was read only in part: it holds 0 rows where its /Index and /Size declare 2;");
        diagnostics[2].Message.Should().Be("The cross-reference index was rebuilt by scanning the file.");
    }

    [Fact]
    public void Reads_a_cross_reference_stream_whose_length_only_it_indexes_as_its_direct_twin()
    {
        // ISO 32000-1 lets a stream's /Length be indirect, a cross-reference stream's too. Object 4, which only the stream
        // indexes, cannot be read while the chain is: the data is taken up to its endstream, and the length read and checked
        // once the chain is read. Nothing is reported, and object 4 reads what it holds — it was once kept as null, with a
        // /Length said to name an object the file lacks (#182).
        using var document = PdfDocument.Open(ChainFiles.AloneWithIndirectLength());

        document.Diagnostics.Should().BeEmpty();
        document.WasRepaired.Should().BeFalse();
        document.GetObject(new PdfObjectId(4)).AsInteger().Should().Be(42);
        document.GetObject(new PdfObjectId(3)).AsDictionary().IsOfType(PdfName.Page).Should().BeTrue();
    }

    [Theory]
    [InlineData(4, "The stream declared 46 bytes but ended after 42.")]
    [InlineData(-4, "The stream declared 38 bytes but ended after 42.")]
    [InlineData(-41, "The stream declared 1 byte but ended after 42.")]
    [InlineData(1, null)]
    public void Checks_a_deferred_length_against_the_data_once_the_chain_is_read(int lengthOff, string? message)
    {
        // Object 4 gives a length of 46, 38 or 1 where the data holds 42 bytes: the length is reported once the chain is
        // read, as any stream's is, one byte in the singular (#241). One that counts the end-of-line before endstream is
        // confirmed by it, as the parser takes any stream's, and says nothing. (The data's last bytes are zeros, which PDF
        // counts as white space: a length that leaves them out is confirmed as well.)
        using var document = PdfDocument.Open(ChainFiles.AloneWithIndirectLength(lengthOff));

        document.WasRepaired.Should().BeFalse();

        if (message is null)
        {
            document.Diagnostics.Should().BeEmpty();
            return;
        }

        var report = document.Diagnostics.Should().ContainSingle().Which;
        report.Code.Should().Be(PdfDiagnosticCodes.StreamLengthInvalid);
        report.Message.Should().Be(message);
    }

    [Theory]
    [InlineData("4 0 obj\n42\n", "4 0 obj\n/F\n", "The /Length of cross-reference stream 5 names object 4 0, which holds the name /F, not a length; the chain took its data up to its endstream, after 42 bytes.")]
    [InlineData("4 0 obj\n42\n", "4 0 obj\n-1\n", "The /Length of cross-reference stream 5 names object 4 0, which holds the integer -1, not a length; the chain took its data up to its endstream, after 42 bytes.")]
    [InlineData("/Length 4 0 R", "/Length 9 0 R", "The /Length of cross-reference stream 5 names object 9 0, which the file lacks; the chain took its data up to its endstream, after 42 bytes.")]
    [InlineData("4 0 obj\n42\n", "4 0 xbj\n42\n", "The stream's /Length names object 4 0, which could not be read; its data ends after 42 bytes.")]
    public void Reports_a_deferred_length_that_gives_no_length_once_the_chain_is_read(string written, string replacement, string message)
    {
        // Object 4 holds a name, or a negative integer; the /Length names an object no section indexes; object 4's header
        // is misspelled, which no search near its entry nor any rebuild then finds — the rebuild its load sets off parses
        // the stream again, and the parser reports the length first: the stream is reported once. Each replacement keeps
        // every offset.
        var file = Encoding.Latin1.GetBytes(
            Encoding.Latin1.GetString(ChainFiles.AloneWithIndirectLength()).Replace(written, replacement, StringComparison.Ordinal));
        using var document = PdfDocument.Open(file);

        document.Diagnostics.Should().ContainSingle(diagnostic => diagnostic.Code == PdfDiagnosticCodes.StreamLengthInvalid)
            .Which.Message.Should().Be(message);
        document.GetObject(new PdfObjectId(3)).AsDictionary().IsOfType(PdfName.Page).Should().BeTrue();
    }

    [Theory]
    [InlineData(false, true, "\n")]
    [InlineData(false, true, "\r\n")]
    [InlineData(false, false, "\r\n")]
    [InlineData(true, false, "\r\n")]
    public void Reads_every_row_of_a_cross_reference_stream_whose_data_a_deferred_length_leaves_to_its_end_of_line(
        bool compressed, bool endingInCarriageReturn, string endOfLine)
    {
        // The data's last byte is a carriage return — object 6's row, free with generation 13 —, or the end-of-line before
        // endstream is one. Taken as part of that end-of-line, the last byte was once lost with the last row, and the
        // length said to disagree with the data. The chain keeps the carriage return; the length, read once the chain is,
        // says how much of it is data, and an end-of-line it leaves out of the data is no fault.
        var file = ChainFiles.AloneWithIndirectLength(compressed: compressed, endingInCarriageReturn: endingInCarriageReturn, endOfLine: endOfLine);
        using var document = PdfDocument.Open(file);

        document.Diagnostics.Should().BeEmpty();
        document.WasRepaired.Should().BeFalse();
        document.Reader.Index.TryGet(6, out var six).Should().Be(endingInCarriageReturn);
        document.GetObject(new PdfObjectId(3)).AsDictionary().IsOfType(PdfName.Page).Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9_500)]
    public void Rows_that_spell_endstream_after_the_length_s_own_row_are_read_as_the_length_gives(int padding)
    {
        // Two rows of reserved types spell an endstream after an end-of-line, a few bytes in or past the first 64 KB: the
        // chain, which could not read the /Length, took the data up to it, and read the rows before it alone — once with a
        // report, the section said to lack rows. The stream's own row for object 4 lies among them: the object it places
        // gives the length, which the endstream after it confirms, and the data is taken again to it (#220). The two rows
        // that spell it, of reserved types, read as the null object, their numbers indexed as free ones, and are reported
        // (#221).
        var file = ChainFiles.AloneWithIndirectLength(spellingEndStream: true, padding: padding);
        using var document = PdfDocument.Open(file);

        document.Diagnostics.Should().ContainSingle().Which.Message.Should().Contain(string.Create(
            CultureInfo.InvariantCulture,
            $" gives 2 rows a reserved type: the row for object {6 + padding} gives it type 10, the first of them. "));
        document.WasRepaired.Should().BeFalse();
        document.Reader.Index.Count.Should().Be(8 + padding);
        document.Reader.Structure.Sections.Should().ContainSingle().Which.Incomplete.Should().BeFalse();
        new PdfValidator().Validate(document).Findings.Select(finding => finding.RuleId).Should().Equal(PdfValidationRuleIds.XRefEntryTypeReserved);
    }

    [Fact]
    public void Rows_that_spell_endstream_in_an_update_leave_none_of_its_objects_to_the_revision_it_supersedes()
    {
        // The update's stream indexes its /Length, object 4, and object 8 after rows that spell an endstream: read up to it,
        // the stream held object 4's row alone, and object 8 read as the first revision wrote it, a valid file said to have
        // a malformed section. The update's rows are read whole, and object 8 reads as it does in its direct twin (#220),
        // whose rows for objects 5 and 6 are free where the spelling rows are of reserved types, read as free ones and
        // reported (#221).
        using var document = PdfDocument.Open(ChainFiles.UpdateWithIndirectLength(spellingEndStream: true));
        using var twin = PdfDocument.Open(ChainFiles.UpdateWithIndirectLength(spellingEndStream: false));

        document.GetObject(new PdfObjectId(8)).Should().BeOfType<PdfString>().Which.ToText().Should().Be("new");
        twin.GetObject(new PdfObjectId(8)).Should().BeOfType<PdfString>().Which.ToText().Should().Be("new");
        document.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.XRefEntryReserved);
        twin.Diagnostics.Should().BeEmpty();
        document.Reader.Index.Count.Should().Be(twin.Reader.Index.Count);
        new PdfValidator().Validate(document).Findings.Select(finding => finding.RuleId).Should().Equal(PdfValidationRuleIds.XRefEntryTypeReserved);
    }

    [Fact]
    public void A_deferred_length_is_not_taken_again_when_the_row_the_section_keeps_for_it_is_of_a_reserved_type()
    {
        // The stream's /Index numbers its /Length, object 4, first in a row of a reserved type whose fields give where object
        // 4 lies, then in a row that places it, before rows that spell an endstream. The first row is the one the reading of
        // the rows keeps, a reference to the null object as ISO 32000-1 reads it (#221): it places no length, whatever its
        // fields give, and the data stays as the chain took it, up to the spelled endstream, the section lacking the rows past
        // it, the page's among them, which a rebuild finds. The row after it, skipped as the first, once took the data again
        // to the length (#220).
        using var document = PdfDocument.Open(ChainFiles.AloneWithIndirectLengthBeforeSpelledRows(reservedRowFirst: true));

        document.Diagnostics.Select(entry => entry.Code).Should().Equal(
            PdfDiagnosticCodes.XRefSectionUnreadable, PdfDiagnosticCodes.XRefEntryReserved, PdfDiagnosticCodes.StreamLengthInvalid);
        var section = document.Reader.Structure.Sections.Should().ContainSingle().Which;
        section.Incomplete.Should().BeTrue();
        section.ReservedRow.Should().Be("the row for object 4 gives it type 3");
        document.GetObject(new PdfObjectId(3)).AsDictionary().IsOfType(PdfName.Page).Should().BeTrue();
        document.WasRepaired.Should().BeTrue();
        new PdfValidator().Validate(document).Contains(PdfValidationRuleIds.XRefSectionMalformed).Should().BeTrue();
    }

    [Fact]
    public void A_deferred_length_is_taken_again_by_a_row_with_no_type_field()
    {
        // With no type field, every row is of type 1: the section's row for its /Length, before rows that spell an endstream,
        // places it, and the data is taken again to the length it gives (#220).
        using var document = PdfDocument.Open(ChainFiles.AloneWithIndirectLengthAndNoTypeField());

        document.Reader.Structure.Sections.Should().ContainSingle().Which.Incomplete.Should().BeFalse();
        document.GetObject(new PdfObjectId(3)).AsDictionary().IsOfType(PdfName.Page).Should().BeTrue();
        document.Diagnostics.Should().NotContain(entry => entry.Code == PdfDiagnosticCodes.StreamLengthInvalid);
    }

    [Theory]
    [InlineData(32)]
    [InlineData(33)]
    public void A_deferred_length_is_taken_again_through_as_many_references_as_the_parse_resolves_a_length_through_and_no_more(int objects)
    {
        // Stream 7's /Length names object 4, which only its own row places, before rows that spell an endstream; object 4
        // names object 100, which names the next, and so on, the newer table placing them, to the last, which holds the
        // length. Through 32 objects, as far as the parse resolves a /Length, the data is taken again to that length, and
        // every row is read. Through one more, the chain reaches no length, as a whole read of the stream reaches none: the
        // data is left as the chain took it, up to the spelled endstream, and the rows past it are the file's to lack — the
        // section incomplete, the length reported once the chain is read, the index rebuilt to find the page (#220).
        var file = ChainFiles.UpdatePlacingAPaddedLengthChain(objects, ownRow: true);
        using var document = PdfDocument.Open(file);
        var section = document.Reader.Structure.Sections[1];

        document.GetObject(new PdfObjectId(3)).AsDictionary().Required().IsOfType(PdfName.Page).Should().BeTrue();

        if (objects == 32)
        {
            // The rows that spell the endstream, of reserved types, are read, and reported (#221).
            section.Incomplete.Should().BeFalse();
            document.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.XRefEntryReserved);
            return;
        }

        section.Incomplete.Should().BeTrue();
        section.Fault.Should().Be("it holds 5 rows where its /Index and /Size declare 8");
        document.WasRepaired.Should().BeTrue();
        document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.StreamLengthInvalid).Which.Message.Should().Be(
            "The /Length of cross-reference stream 7 names object 4 0, which holds null, not a length; the chain took its data up to its endstream, after 35 bytes.");
    }

    [Fact]
    public void A_deferred_length_a_newer_section_refused_takes_no_data_again()
    {
        // A third revision's table refuses the row it gives object 4, the update's /Length: no row of the update's stream
        // stands for it then, and the data is left as the chain took it, up to the endstream its rows spell (#220).
        var file = ChainFiles.UpdateWithIndirectLength(spellingEndStream: true);
        var text = Encoding.Latin1.GetString(file);
        var startxref = text.LastIndexOf("startxref\n", StringComparison.Ordinal) + "startxref\n".Length;
        var second = text[startxref..text.IndexOf('\n', startxref)];
        var update = string.Create(
            CultureInfo.InvariantCulture,
            $"xref\n4 1\n0000000010 99999 n\r\ntrailer\n<< /Size 11 /Root 1 0 R /Prev {second} >>\nstartxref\n{file.Length}\n%%EOF\n");
        using var document = PdfDocument.Open(file.Concat(Encoding.ASCII.GetBytes(update)).ToArray());

        var sections = document.Reader.Structure.Sections;
        sections[0].RefusedRow.Should().NotBeNull();
        sections[1].Incomplete.Should().BeTrue();
        sections[1].Fault.Should().Be("it holds 1 row where its /Index and /Size declare 5");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_deferred_length_is_not_checked_against_data_a_guard_cut(bool decoding)
    {
        // The stream's data, taken up to the endstream its rows spell, runs past MaxStreamLength: decoding cut it, and the
        // length, read once the chain is, was checked against it, the guard's cut reported as the file's (ADR 34). Or the
        // data, compressed in stored blocks that show the rows as written, decodes past MaxDecodedStreamLength, which kept
        // it from being taken again to its length.
        var file = ChainFiles.AloneWithIndirectLengthBeforeSpelledRows(reservedRowFirst: false, stored: decoding);
        var limits = decoding
            ? PdfReaderLimits.Default with { MaxDecodedStreamLength = 30 }
            : PdfReaderLimits.Default with { MaxStreamLength = 30 };
        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = limits });
        using var whole = PdfDocument.Open(file);

        document.Diagnostics.Select(entry => entry.Code).Should().Equal(
            decoding ? PdfDiagnosticCodes.LimitDecodedStream : PdfDiagnosticCodes.LimitStreamLength);
        document.Reader.Structure.Sections.Should().ContainSingle().Which.CutByLimit.Should().BeTrue();

        // Read whole, the rows that spell the endstream, of reserved types, are read, and reported (#221).
        whole.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.XRefEntryReserved);
    }

    [Fact]
    public void A_filter_guard_that_keeps_a_deferred_length_s_data_from_being_taken_again_leaves_no_filter_fault()
    {
        // The stream's data, taken up to the endstream its rows spell, is compressed twice in stored blocks, and
        // MaxFilterChainLength lets the first Flate alone run: over data cut short at the spelled endstream, it failed, and
        // the guard, which kept the data from being taken again to its length, left that failure to the file (#220).
        var file = ChainFiles.AloneWithIndirectLengthBeforeSpelledRows(reservedRowFirst: false, stored: true, layers: 2);
        using var whole = PdfDocument.Open(file);
        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxFilterChainLength = 1 } });

        // Read whole, the rows that spell the endstream, of reserved types, are read, and reported (#221).
        whole.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.XRefEntryReserved);
        document.Diagnostics.Should().Contain(entry => entry.Code == PdfDiagnosticCodes.LimitFilterChain);
        document.Diagnostics.Should().NotContain(entry => entry.Code.StartsWith("filter.", StringComparison.Ordinal));

        // The guard's report stands once, however often the stream is decoded again.
        document.GetObject(new PdfObjectId(5)).AsStream().Required().Decode();
        new PdfValidator().Validate(document);
        document.Diagnostics.Count(entry => entry.Code == PdfDiagnosticCodes.LimitFilterChain).Should().Be(1);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    public void A_guard_s_report_the_capacity_drops_beside_a_deferred_length_s_decoding_is_counted(int capacity)
    {
        // As above, under a capacity with room for one report, or none: the guard's report, which the capacity dropped, went
        // with the decoding's other reports, neither kept nor counted (#220).
        var file = ChainFiles.AloneWithIndirectLengthBeforeSpelledRows(reservedRowFirst: false, stored: true, layers: 2);
        var limits = PdfReaderLimits.Default with { MaxFilterChainLength = 1 };
        using var full = PdfDocument.Open(file, new PdfReaderOptions { Limits = limits });
        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = limits, DiagnosticCapacity = capacity });

        document.Diagnostics.Should().Equal(full.Diagnostics.Take(capacity));
        (document.Diagnostics.Count + document.Diagnostics.SuppressedCount).Should().Be(full.Diagnostics.Count);
    }

    [Theory]
    [InlineData(nameof(PdfReaderLimits.MaxFilterChainLength))]
    [InlineData(nameof(PdfReaderLimits.MaxDecodedStreamLength))]
    [InlineData(nameof(PdfReaderLimits.MaxStreamLength))]
    public void A_fault_a_deferred_length_s_data_holds_before_a_guard_stops_it_is_the_file_s_as_for_a_direct_one(string limit)
    {
        // Stream 5's hexadecimal data starts with a stray x, which encodes nothing, and its rows spell no endstream. Read
        // whole, the fault is the file's, whether the /Length names object 4, which only the stream places, or is written
        // direct. Under a guard on decoding, or MaxStreamLength, only the guard's report stood when the /Length waited for
        // the chain: but the fault lies before the guard stops, where the whole data meets it, and only how the data ends is
        // the guard's to hide (#220).
        var limits = limit switch
        {
            nameof(PdfReaderLimits.MaxFilterChainLength) => PdfReaderLimits.Default with { MaxFilterChainLength = 1 },
            nameof(PdfReaderLimits.MaxDecodedStreamLength) => PdfReaderLimits.Default with { MaxDecodedStreamLength = 20 },
            _ => PdfReaderLimits.Default with { MaxStreamLength = 40 },
        };
        using var direct = PdfDocument.Open(ChainFiles.UpdateOverAStrayHexByte(deferred: false), new PdfReaderOptions { Limits = limits });
        using var deferred = PdfDocument.Open(ChainFiles.UpdateOverAStrayHexByte(deferred: true), new PdfReaderOptions { Limits = limits });

        Faults(direct).Should().ContainSingle().Which.Should().StartWith("An ASCIIHex stream holds a byte that encodes nothing");
        Faults(deferred).Should().Equal(Faults(direct));

        static string[] Faults(PdfDocument document) =>
            [.. document.Diagnostics.Where(entry => entry.Code == PdfDiagnosticCodes.FilterFailed).Select(entry => entry.Message)];
    }

    [Fact]
    public void A_deferred_length_s_data_whose_last_digit_alone_crosses_MaxDecodedStreamLength_is_cut_as_its_direct_twin_s()
    {
        // Stream 5's hexadecimal rows spell an endstream after the digits of 14 bytes and one more, where the deferred search
        // stops: the lone digit makes a fifteenth byte, past MaxDecodedStreamLength. Decoded once more as data a guard cut,
        // the digit was dropped and the data fit: the rows past the spelled endstream were charged to the file, the
        // section rebuilt, where the direct twin's are the guard's. A hidden end silences what decoding says of the end,
        // and changes nothing it decodes (#220, #224).
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxDecodedStreamLength = 14 } };
        using var direct = PdfDocument.Open(ChainFiles.UpdateOverHexRows(deferred: false, compressed: false), options);
        using var deferred = PdfDocument.Open(ChainFiles.UpdateOverHexRows(deferred: true, compressed: false), options);

        direct.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.LimitDecodedStream);
        deferred.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.LimitDecodedStream);
        deferred.WasRepaired.Should().BeFalse();
        deferred.Reader.Structure.Sections[^1].CutByLimit.Should().BeTrue();
        deferred.Reader.Structure.Sections[^1].Incomplete.Should().BeFalse();
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Deferred_lengths_followed_by_long_white_space_have_it_read_on_once_however_their_data_ends(string endOfLine)
    {
        // Thirty revisions, each a cross-reference stream whose /Length only its own rows place, its endstream followed by
        // 65,472 spaces before endobj. With a carriage return before the line feed, which a deferred length's data keeps, the
        // search's end and the length's lie apart before one keyword, and each read on through the spaces: the file's
        // searches read close to four times its length, the most they may, where twice is said (#174, #220).
        var file = ChainFiles.UpdatesWithDeferredLengthsBeforeWhiteSpace(30, 65_472, endOfLine);
        using var document = PdfDocument.Open(file);

        document.Diagnostics.Should().BeEmpty();
        document.Reader.EndStreamSearchBytes.Should().BeLessThan(2L * file.Length + 1024);
    }

    [Theory]
    [InlineData("A85 RL RL RL")]
    [InlineData("A85 RL RL Fl")]
    [InlineData("A85 RL Fl RL")]
    public void Data_whose_end_a_guard_hides_holds_no_more_rows_than_its_direct_twin_through_four_filters(string chain)
    {
        // As below, through four filters: the second, given less, wrote less, and left the third more room than the direct
        // twin's decoding did, which read on, and the last read rows past where the direct twin stopped at the bound — the
        // page indexed beside a guard, or rows and no rebuild where the direct twin rebuilds (#220).
        var direct = ChainFiles.RowsThroughFilters(deferred: false, chain.Split(' '));
        var deferred = ChainFiles.RowsThroughFilters(deferred: true, chain.Split(' '));

        for (var bound = 1; bound <= 1500; bound++)
        {
            var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxDecodedStreamLength = bound } };
            using var directDocument = PdfDocument.Open(direct, options);
            using var deferredDocument = PdfDocument.Open(deferred, options);

            Read(deferredDocument).Should().Be(Read(directDocument), $"MaxDecodedStreamLength is {bound}");
        }

        static string Read(PdfDocument document) => string.Join(
            ' ',
            document.WasRepaired,
            document.Reader.Structure.Sections[^1].CutByLimit,
            document.GetObject(new PdfObjectId(1)) is PdfDictionary,
            document.GetObject(new PdfObjectId(39)) is PdfDictionary,
            document.Reader.ChainIndex?.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Data_whose_end_a_guard_hides_leaves_the_filters_after_a_dropped_group_the_room_its_direct_twin_s_have(bool hex)
    {
        // Stream 5's rows pass through three filters, the first lacking its end marker and ending inside a unit. Decoded once
        // more knowing its end hidden, the first filter drops the unit, whose bytes counted against MaxDecodedStreamLength
        // but not against what the filters before the last share: the filter after it was given more room than the direct
        // twin's, and read a row past where it stopped — the page indexed beside a guard where the direct twin leaves it out,
        // or a row and no rebuild where the direct twin rebuilds (#220).
        var direct = ChainFiles.RowsThroughThreeFilters(deferred: false, hex);
        var deferred = ChainFiles.RowsThroughThreeFilters(deferred: true, hex);

        for (var bound = 1; bound <= 400; bound++)
        {
            var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxDecodedStreamLength = bound } };
            using var directDocument = PdfDocument.Open(direct, options);
            using var deferredDocument = PdfDocument.Open(deferred, options);

            Read(deferredDocument).Should().Be(Read(directDocument), $"MaxDecodedStreamLength is {bound}");
        }

        static string Read(PdfDocument document) => string.Join(
            ' ',
            document.WasRepaired,
            document.Reader.Structure.Sections[^1].CutByLimit,
            document.GetObject(new PdfObjectId(1)) is PdfDictionary,
            document.GetObject(new PdfObjectId(39)) is PdfDictionary,
            document.Reader.ChainIndex?.Count);
    }

    [Fact]
    public void A_deferred_length_s_data_that_holds_every_row_under_a_guard_on_decoding_is_checked_against_its_length()
    {
        // Stream 5's RunLength runs hold its Flate rows and zero bytes, then the bytes of a spelled endstream, where the
        // deferred search stops; MaxDecodedStreamLength stops the Flate output past every row. Nothing would take the data
        // again, and the RunLength run the spelled endstream parts is reported as read whole, but the length, which says the
        // chain took the data short, was not checked under the guard: the run-out stood as the file's fault alone (#220).
        var file = ChainFiles.UpdateOverRunsSpellingAnEndStream(deferred: true);
        using var whole = PdfDocument.Open(file);
        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxDecodedStreamLength = 100 } });

        whole.Diagnostics.Should().Contain(entry => entry.Code == PdfDiagnosticCodes.StreamLengthInvalid);
        document.Diagnostics.Select(entry => entry.Code).Where(code => code != PdfDiagnosticCodes.LimitDecodedStream)
            .Should().Equal(whole.Diagnostics.Select(entry => entry.Code));
        document.Diagnostics.Should().Contain(entry => entry.Code == PdfDiagnosticCodes.LimitDecodedStream);
    }

    [Theory]
    [InlineData(60, true)]
    [InlineData(50, true)]
    [InlineData(49, false)]
    public void A_deferred_length_s_data_MaxStreamLength_cuts_past_every_row_is_checked_against_its_length(int bound, bool everyRow)
    {
        // As above, MaxStreamLength cutting the data past every row, from 50 bytes on — a RunLength byte, 7 of the stored
        // block's header and 42 of rows —: the run the cut parts is the guard's, but the length, checked against where the
        // search found the endstream, which no cut moves, says the chain took the data short, as read whole. It was left
        // unchecked, as data a guard cut that lacks rows is, which the cut keeps from being taken again (#220). The Flate data
        // is stored: compressed, where a cut parts it depended on the platform's zlib, which zlib-ng writes otherwise.
        var file = ChainFiles.UpdateOverRunsSpellingAnEndStream(deferred: true, stored: true);
        using var whole = PdfDocument.Open(file);
        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxStreamLength = bound } });
        var invalid = whole.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.StreamLengthInvalid).Which;

        document.Diagnostics.Should().Equal(everyRow ? [document.Diagnostics[0], invalid] : [document.Diagnostics[0]]);
        document.Diagnostics[0].Code.Should().Be(PdfDiagnosticCodes.LimitStreamLength);
        document.Reader.Structure.Sections[^1].CutByLimit.Should().BeTrue();
    }

    [Fact]
    public void A_deferred_length_s_data_whose_rows_a_guard_on_decoding_holds_all_of_reports_its_filters_ends()
    {
        // Stream 5's rows and 30 zero bytes, compressed, are written in hexadecimal with no ">", and MaxDecodedStreamLength
        // stops the Flate output past every row the section declares: no row is missing, so nothing would take the data
        // again, and the endstream it was taken to ends it whatever the guard does. Its missing ">" is the file's, as for
        // the direct twin (#220).
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxDecodedStreamLength = 50 } };
        using var direct = PdfDocument.Open(ChainFiles.UpdateOverHexRows(deferred: false, compressed: true, padding: 30), options);
        using var deferred = PdfDocument.Open(ChainFiles.UpdateOverHexRows(deferred: true, compressed: true, padding: 30), options);

        direct.Diagnostics.Should().Contain(entry => entry.Message.StartsWith("An ASCIIHex stream ends without its > end marker", StringComparison.Ordinal));
        deferred.Diagnostics.Select(entry => entry.Message).Should().Equal(direct.Diagnostics.Select(entry => entry.Message));
    }

    [Fact]
    public void A_deferred_length_whose_reference_leads_to_an_object_only_its_own_section_places_is_read_once_the_chain_is()
    {
        // The cross-reference stream's /Length, object 5, which the newer table places, holds a reference to object 6, which
        // only the stream indexes: the chain read object 5 alone, and judged the length as holding null (#182).
        var file = ChainFiles.UnderAnUpdate(
            "/Type /XRef /Size 8 /W [1 4 2] /Length 5 0 R", "6 0 R", compressed: false, predicted: false, placed: true, six: "56");
        using var document = PdfDocument.Open(file);

        document.Diagnostics.Should().BeEmpty();
        document.Reader.Structure.Sections.Should().OnlyContain(section => section.State == IO.XRef.XRefSectionState.Read);
    }

    [Theory]
    [InlineData("9 0 R", "The /Length of cross-reference stream 5 names object 9 0, which the file lacks; the chain took its data up to its endstream, after 1 byte.")]
    [InlineData("4 0 R", "The /Length of cross-reference stream 5 gives 13 bytes, which an endstream confirms; the chain, which could not read it, took the data up to an endstream 1 byte in, and read no row past it.")]
    public void A_deferred_length_the_chain_read_one_byte_of_data_without_says_so_in_the_singular(string length, string message)
    {
        // The stream's one row is one byte, after which its data spells an endstream: the chain takes that byte. The length,
        // read once the chain is, names an object the file lacks, or gives the whole data, which the stream's own endstream
        // confirms (#241).
        using var document = PdfDocument.Open(ChainFiles.OneByteRowBeforeASpelledEndStream(length));

        document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.StreamLengthInvalid).Which.Message.Should().Be(message);
        document.Catalog.Required().IsOfType(PdfName.Catalog).Should().BeTrue();
    }

    [Fact]
    public void Reports_the_rows_a_deferred_length_shows_the_chain_did_not_read()
    {
        // The rows spell an endstream before the stream's own row for its /Length, the last: what the chain read holds no
        // row to place it by. The length, read once the chain is, gives 21 bytes more, which the endstream after them
        // confirms: what the chain left unread is reported, and the section's missing rows set off a rebuild.
        var file = ChainFiles.AloneWithIndirectLength(spellingEndStream: true, lengthLast: true);
        using var document = PdfDocument.Open(file);

        var report = document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.StreamLengthInvalid).Which;
        report.Position.Should().Be(PdfTemplate.OffsetOf(file, "stream\n", 1) + "stream\n".Length);
        report.Message.Should().Be(
            "The /Length of cross-reference stream 5 gives 63 bytes, which an endstream confirms; the chain, which could not read it, took the data up to an endstream 42 bytes in, and read no row past it.");
        document.Reader.Structure.Sections.Should().ContainSingle().Which.Incomplete.Should().BeTrue();
        document.GetObject(new PdfObjectId(3)).AsDictionary().IsOfType(PdfName.Page).Should().BeTrue();
    }

    [Theory]
    [InlineData("generation")]
    [InlineData("object stream")]
    [InlineData("name")]
    [InlineData("short")]
    [InlineData("unconfirmed")]
    [InlineData("unreadable")]
    [InlineData("superseded")]
    public void A_length_row_that_does_not_stand_leaves_the_data_up_to_the_endstream_the_rows_spell(string fault)
    {
        // The stream's row for object 4 places it under another generation, or in an object stream; object 4 holds a name;
        // it gives no more than the data taken, or more that no endstream confirms; its header is misspelled; an update
        // gives object 4 a place that holds nothing. None says where the data ends: the rows before the endstream they
        // spell are all the section holds, as the chain took them (#220).
        var file = ChainFiles.AloneWithIndirectLength(
            spellingEndStream: true, lengthOff: fault switch { "short" => -14, "unconfirmed" => 3, _ => 0 });
        var row = (int)PdfTemplate.OffsetOf(file, "stream\n", 1) + "stream\n".Length + (4 * 7);

        switch (fault)
        {
            case "generation":
                file[row + 6] = 1;
                break;
            case "object stream":
                file[row] = 2;
                break;
            case "name":
            case "unreadable":
                var replaced = Encoding.Latin1.GetString(file).Replace(
                    "4 0 obj\n56\n", fault == "name" ? "4 0 obj\n/F\n" : "4 0 xbj\n56\n", StringComparison.Ordinal);
                file = Encoding.Latin1.GetBytes(replaced);
                break;
            case "superseded":
                file = WithUpdatePlacingFourOutside(file);
                break;
        }

        using var document = PdfDocument.Open(file);

        document.Reader.Structure.Sections[^1].Incomplete.Should().BeTrue();
        document.Reader.Structure.Sections[^1].Fault.Should().Be("it holds 6 rows where its /Index and /Size declare 8");

        static byte[] WithUpdatePlacingFourOutside(byte[] file)
        {
            var stream = PdfTemplate.OffsetOf(file, "5 0 obj");
            var update = string.Create(
                CultureInfo.InvariantCulture,
                $"xref\n4 1\n0000099999 00000 n\r\ntrailer\n<< /Size 8 /Root 1 0 R /Prev {stream} >>\nstartxref\n{file.Length}\n%%EOF\n");
            return [.. file, .. Encoding.Latin1.GetBytes(update)];
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Rows_that_spell_endstream_in_an_older_section_are_read_as_its_length_gives(bool twin)
    {
        // The older section's /Length names object 4, which only it indexes, and two of its rows spell an endstream: the
        // search for it stops at the update's first object, past them. Its data is taken again to the length object 4
        // gives, as the newest section's is (#220).
        // The rows that spell it, of reserved types, are reported at the older section (#221).
        var file = ChainFiles.OlderWithIndirectLength(spellingEndStream: !twin);
        using var document = PdfDocument.Open(file);
        string[] reported = twin ? [] : [PdfDiagnosticCodes.XRefEntryReserved];
        string[] found = twin ? [] : [PdfValidationRuleIds.XRefEntryTypeReserved];

        document.Diagnostics.Select(entry => entry.Code).Should().Equal(reported);
        document.WasRepaired.Should().BeFalse();
        document.Reader.Structure.Sections.Should().HaveCount(2).And.OnlyContain(section => !section.Incomplete);
        document.GetObject(new PdfObjectId(8)).Should().BeOfType<PdfString>().Which.ToText().Should().Be("new");
        new PdfValidator().Validate(document).Findings.Select(finding => finding.RuleId).Should().Equal(found);
    }

    [Fact]
    public void Updates_each_with_a_long_deferred_length_are_all_searched()
    {
        // Five revisions, each a cross-reference stream of 70,000 bytes of rows whose /Length only it indexes: each is
        // searched for its endstream past the first window, up to the next object the newer sections place or the end of
        // the file. Together they read about the file once, and one read past each: no search is left undone for want of
        // what the document's searches may read (EndStreamSearchPasses).
        var file = ChainFiles.UpdatesWithIndirectLengths(count: 5, padding: 10_000);
        using var source = new StrictCountingSource(file);
        using var document = PdfDocument.Open(source, options: null, ownsSource: false);

        document.Diagnostics.Should().BeEmpty();
        document.Reader.Structure.Sections.Should().HaveCount(5).And.OnlyContain(section => section.State == XRefSectionState.Read && !section.Incomplete);
        document.Reader.Index.Count.Should().Be(4 + (5 * 10_002));
        source.BytesRead.Should().BeLessThan(3L * file.Length);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public void Reports_the_data_a_deferred_length_gives_past_the_chain_s_endstream_once_the_searches_have_read_all_they_may(int sections)
    {
        // Cross-reference streams written one after the other, with no endstream of their own, share 512 KB of data and the
        // endstream after it, which four zero bytes and a second endstream follow; each one's /Length names an object only
        // the oldest section, a classic table, places. Each is searched past its window up to the first endstream, and five
        // such searches read more than the four times the file the document's searches may (EndStreamSearchPasses). The
        // newest one's length, read once the chain is, gives the data up to the second endstream: what the chain did not read
        // is reported, as for one such stream alone, though the file may no longer be read to find where the first starts.
        var file = SectionsSharingTheirData(sections, 512 * 1024, newestExtra: 14);
        using var document = PdfDocument.Open(file);

        document.WasRepaired.Should().BeFalse();
        document.Reader.Structure.Sections.Should().HaveCount(sections + 1).And.OnlyContain(section => section.State == XRefSectionState.Read);
        (document.Reader.EndStreamSearchBytes >= PdfFileReader.EndStreamSearchPasses * file.Length).Should().Be(sections == 5);
        var report = document.Diagnostics.Should().ContainSingle().Which;
        report.Code.Should().Be(PdfDiagnosticCodes.StreamLengthInvalid);
        report.Position.Should().Be(PdfTemplate.OffsetOf(file, ">>\nstream\n", sections) + ">>\nstream\n".Length);
        report.Message.Should().Be(string.Create(
            CultureInfo.InvariantCulture,
            $"The /Length of cross-reference stream {40 + sections} gives 524305 bytes, which an endstream confirms; the chain, which could not read it, took the data up to an endstream 524291 bytes in, and read no row past it."));
    }

    [Theory]
    [InlineData(false, false, "\n")]
    [InlineData(true, false, "\n")]
    [InlineData(false, true, "\r\n")]
    public void A_cross_reference_stream_whose_length_only_it_indexes_is_read_past_the_first_window_as_its_direct_twin(
        bool compressed, bool endingInCarriageReturn, string endOfLine)
    {
        // 70,000 bytes of rows, past the 64 KB the chain's window first holds: the data, whose /Length object 4 gives and
        // only the stream indexes, was searched for its endstream within the window alone, which grew to MaxTrailerLength,
        // reported reached, and 9,350 of 10,000 rows were read. It is searched in the file, as any stream's whose length is
        // not confirmed, and read whole — its last byte a carriage return too (#220).
        var file = ChainFiles.AloneWithIndirectLength(padding: 10_000, compressed: compressed, endingInCarriageReturn: endingInCarriageReturn, endOfLine: endOfLine);
        var options = new PdfReaderOptions { ThrowOnLimit = true };
        using var document = PdfDocument.Open(file, options);

        document.Diagnostics.Should().BeEmpty();
        document.WasRepaired.Should().BeFalse();
        document.Reader.Index.Count.Should().Be(10_006 + (endingInCarriageReturn ? 1 : 0));
        document.Reader.Structure.Sections.Should().ContainSingle().Which.CutByLimit.Should().BeFalse();
    }

    [Fact]
    public void A_deferred_length_s_data_is_read_once_more_than_its_direct_twin_s()
    {
        // The search reads the data to its endstream, and the decoding reads it again: the file is read once more than its
        // direct twin, whose /Length, written in place of "4 0 R", takes as many bytes, and not a byte past the next read.
        var file = ChainFiles.AloneWithIndirectLength(padding: 10_000);
        var text = Encoding.Latin1.GetString(file);
        var twin = Encoding.Latin1.GetBytes(text.Replace("/Length 4 0 R", "/Length 70042", StringComparison.Ordinal));
        twin.Length.Should().Be(file.Length);

        var read = BytesReadOpening(file);
        var twinRead = BytesReadOpening(twin);

        read.Should().BeInRange(twinRead, twinRead + 70_042 + PdfFileReader.EndStreamSearchChunk);

        static long BytesReadOpening(byte[] file)
        {
            using var source = new StrictCountingSource(file);
            using var document = PdfDocument.Open(source, options: null, ownsSource: false);
            document.Diagnostics.Should().BeEmpty();
            return source.BytesRead;
        }
    }

    [Fact]
    public void Rows_that_spell_an_object_header_do_not_end_a_deferred_length_s_data()
    {
        // Rows of reserved types spell "1 0 obj" past the first 64 KB: a header the bytes hold ends a search for a stream's
        // endstream, but not one whose /Length the chain could not read, whose rows may spell anything (#220). Those two rows
        // read as the null object, their numbers indexed as free ones, and are reported (#221).
        using var document = PdfDocument.Open(ChainFiles.AloneWithIndirectLength(padding: 10_000, spellingHeader: true));

        document.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.XRefEntryReserved);
        document.Reader.Index.Count.Should().Be(10_008);
    }

    [Fact]
    public void A_deferred_length_s_data_with_no_endstream_before_the_end_of_the_file_makes_its_section_malformed()
    {
        // The stream's endstream is misspelled: its data, which no length the chain could read ends, ends nowhere, and the
        // section is not read — once, the rest of the file was taken for its data (#220). The index is rebuilt.
        var file = ChainFiles.AloneWithIndirectLength(padding: 10_000);
        var at = (int)PdfTemplate.OffsetOf(file, "endstream");
        file[at] = (byte)'x';
        using var document = PdfDocument.Open(file);

        document.WasRepaired.Should().BeTrue();
        var section = document.Reader.Structure.Sections.Should().ContainSingle().Which;
        section.State.Should().Be(XRefSectionState.Malformed);
        section.Fault.Should().Be(
            "its /Length is the reference 4 0 R, which no section read before it places where it can be read, and no endstream follows its data before the end of the file");
        document.Diagnostics.Select(entry => entry.Code).Should().NotContain(code => code.StartsWith("limit.", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("/W [1 4 2]", "/W [1 4 /]", "its /W gives a field a width that is not an integer", 0)]
    [InlineData("/W [1 4 2]", "/W [1 4 /]", "its /W gives a field a width that is not an integer", 1200)]
    [InlineData("/Root 1 0 R", "/Index [0 ]", "its /Index holds 1 value, not pairs of a first object number and a count of rows", 0)]
    [InlineData("/Root 1 0 R", "/Index [0 ]", "its /Index holds 1 value, not pairs of a first object number and a count of rows", 1200)]
    [InlineData("/Size 9 ", "/Size/X ", "it has no /Index, and its /Size gives no count of objects", 0)]
    [InlineData("/Size 9 ", "/Size/X ", "it has no /Index, and its /Size gives no count of objects", 1200)]
    public void A_cross_reference_stream_an_update_names_that_cannot_be_read_is_reported_where_it_is_however_near_the_update(
        string written, string faulty, string fault, int gap)
    {
        // The older stream's /W, /Index or /Size is written so that no row can be read, in place, and the newer stream, which
        // names it through /Prev, lies within 512 bytes of it, or past a comment of 1,200 bytes. The newer stream was taken
        // for the older moved, read again and named the older once more: a loop, a rebuild, and the older stream never said
        // to be malformed (#281). It declares itself a cross-reference stream: it is reported where it is, as out of reach,
        // and what only it indexes is found by rebuilding the index.
        var sound = ChainFiles.UpdateWithIndirectLength(spellingEndStream: false, gap: gap);
        var older = PdfTemplate.OffsetOf(sound, "7 0 obj");
        var text = Encoding.Latin1.GetString(sound);
        var at = text.IndexOf(written, StringComparison.Ordinal);
        at.Should().BeGreaterThan((int)older).And.BeLessThan((int)PdfTemplate.OffsetOf(sound, "stream\n"), "the older stream's dictionary holds it");
        var file = Encoding.Latin1.GetBytes(string.Concat(text.AsSpan(0, at), faulty, text.AsSpan(at + written.Length)));

        using (var whole = PdfDocument.Open(sound))
        {
            whole.Diagnostics.Should().BeEmpty();
            whole.Reader.Structure.Sections.Should().HaveCount(2).And.OnlyContain(section => section.State == XRefSectionState.Read);
        }

        using var document = PdfDocument.Open(file);

        document.Diagnostics.Select(entry => entry.Code).Should().Equal(
            PdfDiagnosticCodes.XRefSectionUnreadable, PdfDiagnosticCodes.XRefRebuilt, PdfDiagnosticCodes.ObjectRedefined);
        document.Diagnostics[0].Position.Should().Be(older);
        document.Diagnostics[0].Message.Should().StartWith(string.Create(
            CultureInfo.InvariantCulture, $"The cross-reference stream /Prev names at offset {older} is there but cannot be read: {fault}."));
        document.Reader.Structure.Sections.Should().HaveCount(2);
        document.Reader.Structure.Sections[1].State.Should().Be(XRefSectionState.Malformed);
        document.Reader.Structure.Sections[1].Offset.Should().Be(older);
        document.GetObject(new PdfObjectId(8)).Should().BeOfType<PdfString>().Which.ToText().Should().Be("new");
        document.GetObject(new PdfObjectId(3)).AsDictionary().IsOfType(PdfName.Page).Should().BeTrue();
    }

    [Fact]
    public void A_deferred_length_s_data_with_no_endstream_before_the_next_object_makes_its_section_malformed()
    {
        // The older stream's /Length names object 9, which only it indexes, and its endstream is misspelled: the search for
        // it stops at the update's first object the newer section places (#220).
        var file = ChainFiles.UpdateWithIndirectLength(spellingEndStream: false, olderIndirect: true);
        var at = (int)PdfTemplate.OffsetOf(file, "endstream");
        file[at] = (byte)'x';
        var next = PdfTemplate.OffsetOf(file, "8 0 obj\n(new)");
        using var document = PdfDocument.Open(file);

        document.Reader.Structure.Sections[1].Fault.Should().Be(string.Create(
            CultureInfo.InvariantCulture,
            $"its /Length is the reference 9 0 R, which no section read before it places where it can be read, and no endstream follows its data before the next object, at {next}"));
    }

    [Fact]
    public void A_deferred_length_s_data_the_searches_could_no_longer_read_makes_its_section_malformed_and_says_why()
    {
        // Six revisions, newest first in the file, each a cross-reference stream whose /Length only its own rows place, and
        // whose data runs over the streams after it and 500,000 spaces to the endstream they share at the end of the file:
        // the first five are each searched for it past their first window, and read the file about once each, more than
        // four times in all — as much as the document's searches may (EndStreamSearchPasses). The sixth's data is searched
        // in the bytes read with its object alone, which hold no endstream: it ends nowhere the section says, and the fault
        // says the search was not made, not that no endstream follows the data (#220).
        var file = ChainFiles.UpdatesSpendingTheSearches(spenders: 6, padding: 500_000, cutTarget: false);
        using var document = PdfDocument.Open(file);

        var sections = document.Reader.Structure.Sections;
        sections.Should().HaveCount(6);
        sections.Take(5).Should().OnlyContain(section => section.State == XRefSectionState.Read && !section.Incomplete);
        sections[5].State.Should().Be(XRefSectionState.Malformed);
        sections[5].CutByLimit.Should().BeFalse();
        const string Fault =
            "its /Length is the reference 30 0 R, which no section read before it places where it can be read, and its data was not searched for its endstream, the document's searches for endstream having read as much of the file as they may";
        sections[5].Fault.Should().Be(Fault);
        document.Reader.EndStreamSearchBytes.Should().BeGreaterThanOrEqualTo(PdfFileReader.EndStreamSearchPasses * file.Length);
        document.Diagnostics.Should().ContainSingle().Which.Should().Match<PdfDiagnostic>(diagnostic =>
            diagnostic.Code == PdfDiagnosticCodes.XRefSectionUnreadable && diagnostic.Position == sections[5].Offset &&
            diagnostic.Message.Contains(Fault, StringComparison.Ordinal));
        document.GetObject(new PdfObjectId(3)).AsDictionary().Required().IsOfType(PdfName.Page).Should().BeTrue();
    }

    [Fact]
    public void A_deferred_length_read_once_the_chain_is_says_what_its_chain_leads_to()
    {
        // Stream 7's /Length names object 5, which the newer table places, and which holds 6 0 R, which only stream 7 places:
        // the length waits for the chain, then resolves to a name, which is said, as the parse says what it resolves to — not
        // the reference object 5 holds (#220).
        var file = ChainFiles.UnderAnUpdate(
            "/Type /XRef /Size 8 /W [1 4 2] /Length 5 0 R", "6 0 R", compressed: false, predicted: false, placed: true, six: "/Foo");
        using var document = PdfDocument.Open(file);

        document.Diagnostics.Should().ContainSingle().Which.Message.Should().Be(
            "The /Length of cross-reference stream 7 names object 5 0, which holds the name /Foo, not a length; the chain took its data up to its endstream, after 56 bytes.");
    }

    [Theory]
    [InlineData(31, false)]
    [InlineData(32, false)]
    [InlineData(33, true)]
    [InlineData(34, true)]
    public void A_deferred_length_whose_chain_is_too_long_to_follow_is_reported_at_opening_as_a_later_load_reads_it(int objects, bool tooLong)
    {
        // Stream 7's /Length names object 5, which the newer table places, and which names object 100, the next, and so on;
        // the last holds 56, and none is cut. The chain read follows as many objects as the parse resolves a /Length through,
        // and reads a longer chain as null, as a later load of stream 7 does: the length is reported at opening, and the load
        // adds nothing. Once, the parse resolved the reference the chain read gave back, took 56, and the report waited for
        // the load (#220, #224).
        var placed = new Dictionary<int, string>();
        var last = 100 + objects - 2;

        for (var number = 100; number <= last; number++)
        {
            placed[number] = number < last ? string.Create(CultureInfo.InvariantCulture, $"{number + 1} 0 R") : "56";
        }

        var file = ChainFiles.UnderAnUpdate(
            "/Type /XRef /Size 8 /W [1 4 2] /Length 5 0 R", "100 0 R", compressed: false, predicted: false, placed: true, placedToo: placed);
        string[] expected = tooLong ? [PdfDiagnosticCodes.StreamLengthInvalid] : [];
        using var document = PdfDocument.Open(file);

        document.WasRepaired.Should().BeFalse();
        document.Diagnostics.Select(entry => entry.Code).Should().Equal(expected);

        document.GetObject(new PdfObjectId(7)).AsStream().Required().GetRawBytes().Length.Should().Be(56);
        document.Diagnostics.Select(entry => entry.Code).Should().Equal(expected);
    }

    [Fact]
    public void A_deferred_length_s_fault_names_the_reference_at_the_end_of_its_chain()
    {
        // As above, the update placing object 9, which holds the reference 11 0 R, which no section places: the fault names
        // the link the chain could not read, not the first, which it read (#220).
        var file = ChainFiles.UpdateWithIndirectLength(spellingEndStream: false, olderIndirect: true, nine: "11 0 R");
        var at = (int)PdfTemplate.OffsetOf(file, "endstream");
        file[at] = (byte)'x';
        var next = PdfTemplate.OffsetOf(file, "8 0 obj\n(new)");
        using var document = PdfDocument.Open(file);

        document.Reader.Structure.Sections[1].Fault.Should().Be(string.Create(
            CultureInfo.InvariantCulture,
            $"its /Length holds the reference 11 0 R, which no section read before it places where it can be read, and no endstream follows its data before the next object, at {next}"));
    }

    [Theory]
    [MemberData(nameof(ChainFiles.Keys), MemberType = typeof(ChainFiles))]
    public void Changes_nothing_of_the_index_while_the_chain_is_read(string key)
    {
        // #182: a value the older stream refers to, object 5, which the newer table places outside the file. Loading it as
        // the chain was read rebuilt the index, and the rebuilt one was served as the chain's. Whatever reads object 5 now
        // does so once the chain is read — the catalog looked for, the /Length checked —, behind a copy of the index as the
        // file wrote it; a /Type the rows are read without needs nothing read.
        using var document = PdfDocument.Open(ChainFiles.UnderAnUpdate(key, placed: false));

        document.WasRepaired.Should().Be(key != "Type");
        document.Reader.Structure.ChainRead.Should().BeTrue();
        document.Reader.ChainIndex!.TryGet(5, out var written).Should().BeTrue();
        written.Offset.Should().Be(99999);

        if (document.WasRepaired)
        {
            document.Reader.ChainIndex.Should().NotBeSameAs(document.Reader.Index);
        }
    }

    [Fact]
    public void A_value_the_chain_reads_that_a_guard_cuts_leaves_the_section_whole()
    {
        // #182: object 5, the older stream's /Length, runs past MaxObjectLength, padded with white space. Its load, as the
        // chain was read, reached the guard and marked the section cut, which silenced what the rules found of it. The
        // chain leaves it unread; the stream's data is taken up to its endstream, and the guard reached, and reported at
        // object 5, when the length is read once the chain is.
        var file = ChainFiles.UnderAnUpdate("Length", placed: true, padding: 3000);
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = 1024 } };

        using var document = PdfDocument.Open(file, options);

        document.Reader.Structure.Sections.Should().OnlyContain(section => !section.CutByLimit);
        document.Diagnostics.Should().ContainSingle(diagnostic => diagnostic.Code == PdfDiagnosticCodes.LimitObject)
            .Which.Position.Should().Be(PdfTemplate.OffsetOf(file, "\n5 0 obj") + 1);
    }

    [Fact]
    public void A_guard_a_deferred_length_reaches_throws_once_the_chain_is_read_when_asked_to()
    {
        // The guard left the length unread as the chain was read; read once the chain is, it is reached as any load
        // reaches it, and thrown when the caller asked for it.
        var file = ChainFiles.UnderAnUpdate("Length", placed: true, padding: 3000);
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = 1024 }, ThrowOnLimit = true };

        var opening = () => PdfDocument.Open(file, options);

        opening.Should().Throw<PdfLimitExceededException>().Which.LimitName.Should().Be(nameof(PdfReaderLimits.MaxObjectLength));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_cross_reference_stream_whose_dictionary_the_trailer_guard_cuts_before_it_declares_itself_reaches_the_guard(bool throwOnLimit)
    {
        // Where the chain names a section, a dictionary the guard cuts is the guard's to report, whatever the cut part
        // holds — not yet /Type, nor /W. Only a place the section may have been moved to is a guess, left in silence.
        var file = ChainFiles.UnderAnUpdate(
            "/Producer (" + new string('x', 400) + ") /Type /XRef /Size 8 /W [1 4 2] /Length {length}", "0", compressed: false, predicted: false, placed: true);
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxTrailerLength = 256 }, ThrowOnLimit = throwOnLimit };

        if (throwOnLimit)
        {
            var opening = () => PdfDocument.Open(file, options);
            opening.Should().Throw<PdfLimitExceededException>().Which.LimitName.Should().Be(nameof(PdfReaderLimits.MaxTrailerLength));
            return;
        }

        using var document = PdfDocument.Open(file, options);

        document.Diagnostics.Should().ContainSingle(diagnostic => diagnostic.Code == PdfDiagnosticCodes.LimitTrailer)
            .Which.Position.Should().Be(PdfTemplate.OffsetOf(file, "\n7 0 obj") + 1);
    }

    [Theory]
    [InlineData("<< /A 1 /B >>", 64 * 1024)]
    [InlineData("<< /Producer (an object longer than the trailer guard allows, which a place the section was moved to is only a guess at; it is read as itself when it is asked for, under the guard that bounds objects) >>", 128)]
    [InlineData("<< /Type /Font /Subtype /CIDFontType2 /BaseFont /Foo /DW 1000 /W [0 [500] 1 [500] 2 [500] 3 [500] 4 [500] 5 [500] 6 [500] 7 [500] 8 [500] 9 [500] 10 [500] 11 [500] 12 [500] 13 [500] 14 [500] 15 [500] 16 [500] 17 [500] 18 [500] 19 [500] 20 [500] 21 [500] 22 [500] 23 [500] 24 [500] 25 [500] 26 [500] 27 [500] 28 [500] 29 [500]] >>", 128)]
    public void A_place_a_section_may_have_moved_to_that_holds_another_object_leaves_nothing_of_its_reading(string six, int maxTrailerLength)
    {
        // /Prev names a place 2 bytes into object 6, just before the older stream: object 6, nearer, is tried first, and is
        // no section — a dictionary with a key short of a value, or one the trailer guard cuts, a CIDFont's whose /W gives
        // its glyphs' widths among them. Neither its syntax fault nor the guard is reported: it was read as a section, and
        // is read as itself when asked for. The font, once taken for the moved section, ended the chain as the guard's.
        var built = ChainFiles.UnderAnUpdate("/Type /XRef /Size 8 /W [1 4 2] /Length {length}", "0", compressed: false, predicted: false, placed: true, six: six);
        var stream = PdfTemplate.OffsetOf(built, "\n7 0 obj") + 1;
        var named = PdfTemplate.OffsetOf(built, "\n6 0 obj") + 3;
        var prev = string.Create(CultureInfo.InvariantCulture, $"/Prev {named}");
        prev.Length.Should().Be(string.Create(CultureInfo.InvariantCulture, $"/Prev {stream}").Length, "no offset moves");
        var file = Encoding.Latin1.GetBytes(Encoding.Latin1.GetString(built).Replace(
            string.Create(CultureInfo.InvariantCulture, $"/Prev {stream}"), prev, StringComparison.Ordinal));
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxTrailerLength = maxTrailerLength } };

        using var document = PdfDocument.Open(file, options);

        document.WasRepaired.Should().BeFalse();
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(PdfDiagnosticCodes.XRefOffsetAdjusted);
        document.GetObject(new PdfObjectId(6)).Should().NotBeSameAs(PdfNull.Instance);
    }

    [Fact]
    public void A_place_a_section_may_have_moved_to_whose_dictionary_a_guard_cuts_is_no_section_the_chain_ends_at()
    {
        // Object 6 is a CIDFont that writes its /Type past a /W the trailer guard cuts: cut before it says what it is, the
        // dictionary may be a section's, and the guard that hid it is reported. It is not the section all the same — only a
        // value a section is read with that leads to an object a guard cut is (#263) —: the older stream, farther, is found.
        var built = ChainFiles.UnderAnUpdate("/Type /XRef /Size 8 /W [1 4 2] /Length {length}", "0", compressed: false, predicted: false, placed: true, six: "<< /Subtype /CIDFontType2 /W [0 [500] 1 [500] 2 [500] 3 [500] 4 [500] 5 [500] 6 [500] 7 [500] 8 [500] 9 [500] 10 [500] 11 [500] 12 [500] 13 [500] 14 [500] 15 [500] 16 [500] 17 [500] 18 [500] 19 [500] 20 [500] 21 [500] 22 [500] 23 [500] 24 [500] 25 [500] 26 [500] 27 [500] 28 [500] 29 [500]] /Type /Font >>");
        var stream = PdfTemplate.OffsetOf(built, "\n7 0 obj") + 1;
        var named = PdfTemplate.OffsetOf(built, "\n6 0 obj") + 3;
        var file = Encoding.Latin1.GetBytes(Encoding.Latin1.GetString(built).Replace(
            string.Create(CultureInfo.InvariantCulture, $"/Prev {stream}"),
            string.Create(CultureInfo.InvariantCulture, $"/Prev {named}"),
            StringComparison.Ordinal));
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxTrailerLength = 128 } };

        using var document = PdfDocument.Open(file, options);

        document.WasRepaired.Should().BeFalse();
        document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(PdfDiagnosticCodes.LimitTrailer, PdfDiagnosticCodes.XRefOffsetAdjusted);
        document.Diagnostics[1].Message.Should().EndWith(string.Create(CultureInfo.InvariantCulture, $"was found {stream - named} bytes after it."));
        document.GetObject(new PdfObjectId(6)).AsDictionary().IsOfType(PdfName.Font).Should().BeTrue();
    }

    [Fact]
    public void Reads_a_value_the_chain_refers_to_past_the_window_it_starts_with()
    {
        // Object 5, the older stream's /Index, runs past the 8 KB the reading starts with, padded with white space: the
        // window grows, as a load's does, up to MaxObjectLength.
        using var document = PdfDocument.Open(ChainFiles.UnderAnUpdate("Index", placed: true, padding: 3 * PdfFileReader.InitialObjectWindow));

        document.WasRepaired.Should().BeFalse();
        document.Diagnostics.Should().BeEmpty();
        document.GetObject(new PdfObjectId(3)).AsDictionary().IsOfType(PdfName.Page).Should().BeTrue();
    }

    [Fact]
    public void Finds_an_object_whose_entry_points_past_the_end_of_the_file()
    {
        using var document = PdfDocument.Open(PdfTemplate.SoundWith("{row:3}", "0009999999 00000 n "));

        document.GetObject(new PdfObjectId(3)).AsDictionary().IsOfType(PdfName.Page).Should().BeTrue();
        document.WasRepaired.Should().BeTrue();
    }

    [Fact]
    public void A_rebuild_finds_a_header_with_the_largest_generation_an_object_in_use_may_have()
    {
        const string Objects = """
            1 0 obj
            << /Type /Catalog /Pages 2 65535 R >>
            endobj
            2 65535 obj
            << /Type /Pages /Kids [] /Count 0 >>
            endobj
            %%EOF

            """;

        using var document = PdfDocument.Open(Encoding.ASCII.GetBytes("%PDF-1.7\n" + Objects));

        document.WasRepaired.Should().BeTrue();
        document.GetObject(new PdfObjectId(2)).AsDictionary().IsOfType(PdfName.Pages).Should().BeTrue();
        document.Reader.Index.TryGet(2, out var entry).Should().BeTrue();
        entry.Generation.Should().Be(65535, "the rebuilt entry records the generation its header gives (#118)");
    }

    [Theory]
    [InlineData("3 1 obj", "3 1 R", 1)]
    [InlineData("3 0001 obj", "3 1 R", 1)]
    [InlineData("3\t\r\n2\0\f obj", "3 2 R", 2)]
    [InlineData("3 00000000000000000000002 obj", "3 2 R", 2)]
    public void A_rebuilt_entry_records_the_generation_its_header_gives(string header, string reference, int generation)
    {
        // #118: the scan read the generation's digits to know a header from the obj of an endobj, and recorded every object
        // at generation 0 all the same. It records the generation as the parser reads it, white space and zeros aside.
        var objects = string.Create(
            CultureInfo.InvariantCulture,
            $"1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n2 0 obj\n<< /Type /Pages /Kids [{reference}] /Count 1 >>\nendobj\n{header}\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>\nendobj\n%%EOF\n");
        using var document = PdfDocument.Open(Encoding.Latin1.GetBytes("%PDF-1.7\n" + objects));

        document.WasRepaired.Should().BeTrue();
        document.Reader.Index.TryGet(3, out var entry).Should().BeTrue();
        entry.Generation.Should().Be(generation);
        document.GetObject(new PdfObjectId(3, generation)).AsDictionary().IsOfType(PdfName.Page).Should().BeTrue();
    }

    [Theory]
    [InlineData("4 0 obj", "4 1 obj", 1)]
    [InlineData("4 2 obj", "4 1 obj", 1)]
    public void A_rebuild_keeps_the_last_definition_of_a_number_under_the_generation_its_header_gives(string first, string last, int generation)
    {
        // An update that wrote object 4 again under a new generation, or a later definition under a lower one: the last
        // definition wins, as it did — which one a rebuild keeps is #189's —, and its entry records its own generation.
        var objects = string.Create(
            CultureInfo.InvariantCulture,
            $"1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n{first}\n(older)\nendobj\n{last}\n(newer)\nendobj\n%%EOF\n");
        var file = Encoding.ASCII.GetBytes("%PDF-1.7\n" + objects);
        using var document = PdfDocument.Open(file);

        document.WasRepaired.Should().BeTrue();
        document.Reader.Index.TryGet(4, out var entry).Should().BeTrue();
        entry.Generation.Should().Be(generation);
        entry.Offset.Should().Be(PdfTemplate.OffsetOf(file, last));
        document.GetObject(new PdfObjectId(4, generation)).AsText().Should().Be("newer");
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 0)]
    [InlineData(1, 7)]
    [InlineData(0, 0)]
    [InlineData(0, 1)]
    [InlineData(2, 2)]
    [InlineData(2, 0)]
    public void A_relocated_entry_records_the_generation_its_header_gives_whatever_reference_found_it(int row, int asked)
    {
        // #118: object 3's row lies 4 bytes past its header, written 3 1 obj. The relocation recorded the generation of the
        // reference that asked first, so the index depended on the order objects were asked for, and a finding could name a
        // generation that neither the row nor the header gives. The chain's index keeps the row's, which the rules judge.
        var template = PdfTemplate.Sound
            .Replace("3 0 obj", "3 1 obj", StringComparison.Ordinal)
            .Replace("/Kids [3 0 R]", "/Kids [3 1 R]", StringComparison.Ordinal)
            .Replace("{row:3}", string.Create(CultureInfo.InvariantCulture, $"{{row:3:{row}:4}}"), StringComparison.Ordinal);
        using var document = PdfDocument.Open(PdfTemplate.Build(template));

        document.GetObject(new PdfObjectId(3, asked)).AsDictionary().IsOfType(PdfName.Page).Should().BeTrue();

        document.Diagnostics.Should().ContainSingle(diagnostic => diagnostic.Code == PdfDiagnosticCodes.XRefOffsetAdjusted);
        document.Reader.Index.TryGet(3, out var entry).Should().BeTrue();
        entry.Generation.Should().Be(1);
        document.Reader.ChainIndex!.TryGet(3, out var written).Should().BeTrue();
        written.Generation.Should().Be(row);
    }

    [Theory]
    [InlineData("row")]
    [InlineData("shifted")]
    [InlineData("rebuilt")]
    public void A_catalog_found_by_its_type_is_named_as_its_entry_names_it(string layout)
    {
        // #118: the trailer's /Root names nothing, and the reader finds the catalog, written 1 1 obj, among the objects the
        // index holds — through a row of 1, through a row of 0 a few bytes off, which the catalog's own load relocates, or
        // in a rebuilt index. It once put 1 0 R in the trailer, so that a report named the catalog both 1 1 and 1 0.
        using var document = PdfDocument.Open(CatalogWrittenUnderGeneration1(layout));

        document.Diagnostics.Should().ContainSingle(diagnostic => diagnostic.Code == PdfDiagnosticCodes.TrailerRootRecovered);
        document.WasRepaired.Should().Be(layout == "rebuilt");
        document.Trailer.GetRaw(PdfName.Root).Should().BeOfType<PdfReference>().Which.Id.Should().Be(new PdfObjectId(1, 1));
        document.Catalog.IsOfType(PdfName.Catalog).Should().BeTrue();
    }

    /// <summary>
    /// The sound document with its catalog written <c>1 1 obj</c> and a trailer whose <c>/Root</c> names nothing: the
    /// catalog's row gives generation 1 (<c>row</c>), or 0 a few bytes past its header (<c>shifted</c>), or
    /// <c>startxref</c> names nothing and the index is rebuilt (<c>rebuilt</c>). <paramref name="catalog"/> replaces the
    /// catalog's entries.
    /// </summary>
    internal static byte[] CatalogWrittenUnderGeneration1(string layout, string catalog = "/Type /Catalog /Pages 2 0 R")
    {
        var template = PdfTemplate.Sound
            .Replace("1 0 obj", "1 1 obj", StringComparison.Ordinal)
            .Replace("/Type /Catalog /Pages 2 0 R", catalog, StringComparison.Ordinal)
            .Replace("/Root 1 0 R", "/Root 9 0 R", StringComparison.Ordinal)
            .Replace("{row:1}", layout == "shifted" ? "{row:1:0:4}" : "{row:1:1}", StringComparison.Ordinal);

        return PdfTemplate.Build(layout == "rebuilt" ? template.Replace("startxref\n{xref:1}", "startxref\n999999", StringComparison.Ordinal) : template);
    }

    [Fact]
    public void A_rebuild_finds_a_header_whose_number_and_generation_zeros_lead()
    {
        // Eleven digits each, which the parser reads as 2 and 0: the rebuild's scan judges each run by its value, as the
        // parser does, not by how many digits it has.
        const string Objects = """
            1 0 obj
            << /Type /Catalog /Pages 2 0 R >>
            endobj
            00000000002 00000000000 obj
            << /Type /Pages /Kids [] /Count 0 >>
            endobj
            %%EOF

            """;

        using var document = PdfDocument.Open(Encoding.ASCII.GetBytes("%PDF-1.7\n" + Objects));

        document.WasRepaired.Should().BeTrue();
        document.GetObject(new PdfObjectId(2)).AsDictionary().IsOfType(PdfName.Pages).Should().BeTrue();
    }

    [Fact]
    public void A_rebuild_ignores_object_headers_numbered_zero_or_beyond_what_an_object_number_holds()
    {
        const string Objects = """
            1 0 obj
            << /Type /Catalog /Pages 2 0 R >>
            endobj
            2 0 obj
            << /Type /Pages /Kids [] /Count 0 >>
            endobj
            %%EOF

            """;
        const string Bogus = """
            0 0 obj
            (zero)
            endobj
            9999999999 0 obj
            (too large)
            endobj
            5 99999999999999999999 obj
            (a generation past what a long holds)
            endobj

            """;

        using var sound = PdfDocument.Open(Encoding.ASCII.GetBytes("%PDF-1.7\n" + Objects));
        using var document = PdfDocument.Open(Encoding.ASCII.GetBytes("%PDF-1.7\n" + Bogus + Objects));

        document.WasRepaired.Should().BeTrue();
        document.ObjectCount.Should().Be(sound.ObjectCount);
        document.Catalog.IsOfType(PdfName.Catalog).Should().BeTrue();
    }

    [Fact]
    public void A_rebuild_finds_a_header_of_the_largest_object_number_and_generation()
    {
        // The largest number an object can have, 2,147,483,647, under the largest generation, 65,535, is a header the scan
        // takes, as the parser does.
        const string Text = """
            %PDF-1.7
            2147483647 65535 obj
            (largest)
            endobj
            1 0 obj
            << /Type /Catalog /Pages 2 0 R >>
            endobj
            2 0 obj
            << /Type /Pages /Kids [] /Count 0 >>
            endobj
            %%EOF

            """;

        using var document = PdfDocument.Open(Encoding.ASCII.GetBytes(Text));

        document.WasRepaired.Should().BeTrue();
        document.Reader.Index.TryGet(PdfObjectId.MaxNumber, out var entry).Should().BeTrue();
        entry.Generation.Should().Be(PdfObjectId.MaxGeneration);
    }

    [Theory]
    [InlineData(LargeIndex.ClassicTable, 300_000, 12_610_000)]
    [InlineData(LargeIndex.CrossReferenceStream, 300_002, 23_640_000)]
    public void Opening_an_index_of_three_hundred_thousand_objects_stays_within_its_memory_budget(
        string shape, int entries, long budget)
    {
        // M01's file of several hundred thousand objects (#193). Opening allocates the reader's index, which the section
        // sizes before its rows are read to its 300,000 rows, 36 bytes each and a slot of 4 for each number, made once,
        // and a cross-reference stream's rows decoded besides. Measured twice on 2026-10-28, after a warm-up: 12,004,632
        // bytes for the table, 22,507,624 for the stream, whose rows Flate stores uncompressed so that no runtime's zlib
        // moves the figure — 16,875,928 and 27,378,920 on main at e3e2034, the same day, with the map of 324,449 slots of
        // 52 bytes the index was before #155, and 32,572,336 and 43,075,328 when that map grew by doubling (#164). The
        // budget is 5 % over each.
        var file = LargeIndexFile(shape);
        using (PdfDocument.Open(file))
        {
            // The first opening pays what any first call does once: what the reader's statics and names cost.
        }

        var elapsed = Stopwatch.StartNew();
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        using var document = PdfDocument.Open(file);
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        elapsed.Stop();

        document.ObjectCount.Should().Be(entries, "the file holds what its index says, and the reader read it whole");
        document.WasRepaired.Should().BeFalse();
        document.Diagnostics.Should().BeEmpty();
        allocated.Should().BeLessThan(budget, $"opening {shape} of {entries:N0} entries holds the index of them and little else");

        // A bound, not a throughput: what a regression that allocates nothing, such as a quadratic search, would pass.
        elapsed.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void Opening_a_chain_of_a_thousand_sections_resizes_the_index_no_more_often_than_doubling_it()
    {
        // Each section makes room for its rows, and never by less than the index holds already: room made for each in
        // turn would copy the index at every section. Once a doubling comes within a doubling of what the newest /Size
        // numbers, room is made for it at once. A thousand sections of 300 rows: 24,231,088 bytes allocated, measured
        // twice on 2026-10-28 after a warm-up, where the map the index was before #155 allocated 30,571,304, growth
        // 33,196,448 and doubling alone 44,621,888 (2026-10-14). The newest sections number the highest objects, and the
        // room made counts toward the numbers the index keeps a slot for. The budget is 5 % over.
        var file = ChainOfSections(sections: 1000, rows: 300);
        using (PdfDocument.Open(file))
        {
            // The first opening pays what any first call does once.
        }

        var elapsed = Stopwatch.StartNew();
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        using var document = PdfDocument.Open(file);
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        elapsed.Stop();

        document.ObjectCount.Should().Be(300_000);
        document.WasRepaired.Should().BeFalse();
        document.Diagnostics.Should().BeEmpty();
        document.Catalog.IsOfType(PdfName.Catalog).Should().BeTrue();
        allocated.Should().BeLessThan(25_450_000, "the room doubles at most, section after section, and lands on the newest /Size");
        elapsed.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void A_newest_size_far_past_the_room_made_so_far_is_not_made_room_for_at_once()
    {
        // The newest section's /Size claims 100,000 objects; its rows and the older section's hold 2,000. Room is made for
        // the size at once only when a doubling would come within a doubling of it, four times what the index held.
        var text = Encoding.Latin1.GetString(ChainOfSections(sections: 2, rows: 1000));
        var newest = text.LastIndexOf("/Size 2000 ", StringComparison.Ordinal);
        var file = Encoding.Latin1.GetBytes(string.Concat(text.AsSpan(0, newest), "/Size 100000 ", text.AsSpan(newest + "/Size 2000 ".Length)));

        using var document = PdfDocument.Open(file);

        document.ObjectCount.Should().Be(2000);
        document.Reader.Index.Capacity.Should().BeLessThan(4000, "the room doubles once from the newest section's rows, to 2,004 entries");
    }

    [Theory]
    [InlineData("3000")]
    [InlineData("3000.0")]
    public void An_update_s_rows_and_the_original_s_make_room_for_the_objects_the_newest_size_numbers(string size)
    {
        // The update rewrites a thousand of the original's 3,000 objects: the original's rows for them supersede nothing,
        // and room made for all its rows on top of the update's doubled the map. The newest /Size numbers every object,
        // written direct, as an integer or as a real with no fractional part, which the reader reads as the integer it is.
        var built = TestPdfBuilder.AppendIncrementalUpdate(
            ChainOfSections(sections: 1, rows: 3000),
            rootNumber: 1,
            [.. Enumerable.Range(3, 1000).Select(number => (number, "(updated)"))]);
        var text = Encoding.Latin1.GetString(built);
        var newest = text.LastIndexOf("/Size 3000", StringComparison.Ordinal);
        var file = Encoding.Latin1.GetBytes(string.Concat(text.AsSpan(0, newest), "/Size " + size, text.AsSpan(newest + "/Size 3000".Length)));

        using var document = PdfDocument.Open(file);

        document.ObjectCount.Should().Be(3000);
        document.Reader.Index.Capacity.Should().BeLessThan(4000, "3,000 objects take room for 3,000 entries, and 6,000 doubled");
    }

    [Fact]
    public void A_table_of_many_subsections_makes_room_for_its_rows_once()
    {
        // Three hundred subsections of ten rows each: room made for each in turn doubled the map from 11 slots, to 4,861.
        var file = ChainOfSections(sections: 1, rows: 3000);
        var text = Encoding.Latin1.GetString(file);
        var rows = text.IndexOf("xref\n0 3000\n", StringComparison.Ordinal) + "xref\n".Length;
        var table = new StringBuilder();
        var lines = text[(rows + "0 3000\n".Length)..text.IndexOf("trailer", StringComparison.Ordinal)].Split("\r\n");

        for (var first = 0; first < 3000; first += 10)
        {
            table.Append(CultureInfo.InvariantCulture, $"{first} 10\n");

            for (var number = first; number < first + 10; number++)
            {
                table.Append(lines[number]).Append("\r\n");
            }
        }

        // The table follows the objects, so that the rows written in subsections move no offset.
        var split = string.Concat(text.AsSpan(0, rows), table.ToString(), text.AsSpan(text.IndexOf("trailer", StringComparison.Ordinal)));
        using var document = PdfDocument.Open(Encoding.Latin1.GetBytes(split));

        document.ObjectCount.Should().Be(3000);
        document.Diagnostics.Should().BeEmpty();
        document.Reader.Index.Capacity.Should().BeLessThan(4000, "room is made once, for the 3,278 rows the table's bytes hold");
    }

    [Fact]
    public void A_table_read_again_in_a_larger_window_makes_room_for_its_rows_once()
    {
        // The trailer keyword starts four bytes before the first window's end, which cuts it: the rows are read again in a
        // larger window, and room was made for them again on top of those the index held, which doubled the map to 7,013.
        var file = ChainOfSections(sections: 1, rows: 3276);
        var table = PdfTemplate.OffsetOf(file, "xref\n");
        PdfTemplate.OffsetOf(file, "trailer").Should().Be(table + (64 * 1024) - 4);

        using var document = PdfDocument.Open(file);

        document.ObjectCount.Should().Be(3276);
        document.Reader.Index.Capacity.Should().BeLessThan(4000, "room is made once, for the 3,278 rows the window holds");
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r")]
    public void Rows_written_a_byte_short_make_room_for_each_of_them(string end)
    {
        // Rows ended by one byte, as some producers write them: room made for as many 20-byte rows as their bytes hold fell
        // short of the count, and the map doubled past it, to 156,437 slots for 76,000 objects, as it does when it grows.
        var text = Encoding.Latin1.GetString(ChainOfSections(sections: 1, rows: 76_000));
        var rows = text.IndexOf("xref\n", StringComparison.Ordinal);
        var file = Encoding.Latin1.GetBytes(string.Concat(text.AsSpan(0, rows), text[rows..].Replace("\r\n", end, StringComparison.Ordinal)));
        file.Length.Should().Be(text.Length - 76_000);

        using var document = PdfDocument.Open(file);

        document.ObjectCount.Should().Be(76_000);
        document.Reader.Index.Capacity.Should().BeLessThan(100_000, "76,000 rows take room for 76,000 entries");
    }

    [Fact]
    public void A_newest_size_an_update_did_not_raise_makes_room_for_the_objects_its_rows_add()
    {
        // The update adds a hundred objects past those the original numbers, and keeps the original's /Size: room made for
        // no more than the /Size fell short of the count, and the map doubled past it.
        var original = ChainOfSections(sections: 1, rows: 62_800);
        var updates = new (int Number, string Body)[100];

        for (var i = 0; i < updates.Length; i++)
        {
            updates[i] = (62_800 + i, "null");
        }

        var file = Encoding.Latin1.GetBytes(Encoding.Latin1.GetString(TestPdfBuilder.AppendIncrementalUpdate(original, rootNumber: 1, updates))
            .Replace("/Size 62900", "/Size 62800", StringComparison.Ordinal));

        using var document = PdfDocument.Open(file);

        document.ObjectCount.Should().Be(62_900);
        document.Reader.Index.Capacity.Should().BeLessThan(80_000, "62,900 entries take room for as many");
    }

    [Fact]
    public void Older_sections_numbering_past_a_stale_size_make_room_by_doubling_not_one_entry_at_a_time()
    {
        // The newest section indexes objects 1 to 100,000 under a /Size of 1, and each of a thousand older sections one
        // number past the highest read so far. The stale size capped the room at one past that number: each older section made
        // room for one entry more, and copied the index whole, 3,616,173,432 bytes in all. A size the sections read past
        // caps nothing, and the room doubles: 12,614,040 bytes, measured twice on 2026-10-28, where the map the index was
        // before #155 grew by .NET's primes, about 1.2 times a step, to 6,166,336. The budget is 5 % over.
        const int Newest = 100_000;
        const int Older = 1000;
        var file = StaleSizeChain(Newest, Older);
        using (PdfDocument.Open(file))
        {
            // The first opening pays what any first call does once.
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread();
        using var document = PdfDocument.Open(file);
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;

        document.ObjectCount.Should().Be(Newest + Older);
        document.Catalog.IsOfType(PdfName.Catalog).Should().BeTrue();
        allocated.Should().BeLessThan(13_250_000, "a size the sections read past caps no room");

        static byte[] StaleSizeChain(int newest, int older)
        {
            var file = new StringBuilder("%PDF-1.7\n");
            var catalog = file.Length;
            file.Append("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
            var pages = file.Length;
            file.Append("2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n");
            long previous = -1;

            for (var k = older; k >= 1; k--)
            {
                var at = file.Length;
                file.Append(CultureInfo.InvariantCulture, $"xref\n{newest + k} 1\n0000000000 00001 f\r\ntrailer\n<< /Size 1 /Root 1 0 R");
                file.Append(previous < 0 ? string.Empty : string.Create(CultureInfo.InvariantCulture, $" /Prev {previous}")).Append(" >>\n");
                previous = at;
            }

            var table = file.Length;
            file.Append(CultureInfo.InvariantCulture, $"xref\n1 {newest}\n{catalog:D10} 00000 n\r\n{pages:D10} 00000 n\r\n");

            for (var number = 3; number <= newest; number++)
            {
                file.Append("0000000000 00001 f\r\n");
            }

            file.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size 1 /Root 1 0 R /Prev {previous} >>\nstartxref\n{table}\n%%EOF\n");
            return Encoding.ASCII.GetBytes(file.ToString());
        }
    }

    [Theory]
    [InlineData(StaleChain.Alternating, 100_500, 13_250_000)]
    [InlineData(StaleChain.StreamsPast, 101_000, 15_000_000)]
    [InlineData(StaleChain.TablesPast, 101_000, 14_770_000)]
    public void Older_sections_that_once_numbered_past_a_stale_size_leave_it_bounding_nothing(StaleChain shape, int objects, long budget)
    {
        // Under a /Size of 1, every other older section numbers one past the highest read and the rest number nothing; or
        // each older section's /XRefStm stream numbers one past it while its table numbers below; or each table numbers
        // past it while its stream numbers nothing. Once one has, the stale size bounds the room no more for the rest of
        // the chain: bounding it again after a section or a stream that numbers nothing, or never seeing a stream number
        // past it, made room one entry at a time, copying the index whole each time — 1.8 GB, 3.6 GB and 3.6 GB. Measured
        // twice on 2026-10-28, after a warm-up: 12,613,968, 14,256,152 and 14,063,600 bytes. The budget is 5 % over.
        var file = StaleSizeChainOf(newest: 100_000, older: 1000, shape);
        using (PdfDocument.Open(file))
        {
            // The first opening pays what any first call does once.
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread();
        using var document = PdfDocument.Open(file);
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;

        document.ObjectCount.Should().Be(objects);
        document.Diagnostics.Should().BeEmpty();
        document.Catalog.IsOfType(PdfName.Catalog).Should().BeTrue();
        allocated.Should().BeLessThan(budget, "a stale size bounds no room once an older section has numbered past the newer ones");

        static byte[] StaleSizeChainOf(int newest, int older, StaleChain shape)
        {
            var hybrid = shape != StaleChain.Alternating;
            var file = new StringBuilder("%PDF-1.7\n");
            var catalog = file.Length;
            file.Append("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
            var pages = file.Length;
            file.Append("2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n");
            long previous = -1;

            // Written oldest first, so that the chain reads section k = 1 right after the newest.
            for (var k = older; k >= 1; k--)
            {
                var stream = file.Length;

                if (shape == StaleChain.StreamsPast)
                {
                    file.Append(CultureInfo.InvariantCulture, $"{newest + k} 0 obj\n<< /Type /XRef /Size {newest + k + 1} /Index [{newest + k} 1] /W [1 1 1] /Length 3 >>\nstream\n");
                    file.Append("\0\0\u0001\nendstream\nendobj\n");
                }
                else if (shape == StaleChain.TablesPast)
                {
                    file.Append(CultureInfo.InvariantCulture, $"{newest + older + k} 0 obj\n<< /Type /XRef /Size 0 /W [1 1 1] /Length 0 >>\nstream\n\nendstream\nendobj\n");
                }

                var at = file.Length;
                file.Append(shape switch
                {
                    StaleChain.StreamsPast => "xref\n3 1\n0000000000 00001 f\r\n",
                    StaleChain.TablesPast => string.Create(CultureInfo.InvariantCulture, $"xref\n{newest + k} 1\n0000000000 00001 f\r\n"),
                    _ when k % 2 == 1 => string.Create(CultureInfo.InvariantCulture, $"xref\n{newest + ((k + 1) / 2)} 1\n0000000000 00001 f\r\n"),
                    _ => "xref\n0 0\n",
                });
                file.Append("trailer\n<< /Size 1 /Root 1 0 R");
                file.Append(hybrid ? string.Create(CultureInfo.InvariantCulture, $" /XRefStm {stream}") : string.Empty);
                file.Append(previous < 0 ? string.Empty : string.Create(CultureInfo.InvariantCulture, $" /Prev {previous}")).Append(" >>\n");
                previous = at;
            }

            var table = file.Length;
            file.Append(CultureInfo.InvariantCulture, $"xref\n1 {newest}\n{catalog:D10} 00000 n\r\n{pages:D10} 00000 n\r\n");

            for (var number = 3; number <= newest; number++)
            {
                file.Append("0000000000 00001 f\r\n");
            }

            file.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size 1 /Root 1 0 R /Prev {previous} >>\nstartxref\n{table}\n%%EOF\n");
            return Encoding.Latin1.GetBytes(file.ToString());
        }
    }

    [Theory]
    [InlineData(3, 60_000)]
    [InlineData(520, 300)]
    public void A_stale_size_only_the_newest_section_numbers_past_makes_room_up_to_the_highest_number_read(int sections, int rows)
    {
        // Every trailer keeps the oldest section's /Size, as an updater that never raised it would, padded so that no
        // offset moves, and the newest section numbers the highest objects, as updates do. One past the highest number the newest section gave stands for the
        // size, which the older sections, numbering below it, never pass: room is made for every object and no more. Taken
        // as bounding nothing, the room doubled past the count, to 240,008 entries for 180,000 and 309,248 for 156,000.
        var text = Encoding.Latin1.GetString(ChainOfSections(sections, rows));
        var file = Encoding.Latin1.GetBytes(Regex.Replace(text, @"/Size \d+", size => $"/Size {rows}".PadRight(size.Length)));

        using var document = PdfDocument.Open(file);

        document.ObjectCount.Should().Be(sections * rows);
        document.Diagnostics.Should().BeEmpty();
        document.Reader.Index.Capacity.Should().Be(sections * rows, "one past the highest number read numbers every object");
    }

    [Fact]
    public void A_lowered_entry_guard_reads_a_table_in_no_larger_window_than_its_room_needs()
    {
        // The window grew until it held the rows' end before any row was read: for a guard of a thousand entries, a window
        // of 16 MB for a table of 300,000 rows. The rows past the thousandth are searched for the trailer a megabyte at a
        // time.
        var file = ChainOfSections(sections: 1, rows: 300_000);
        using var source = new CountingSource(file);
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxIndexEntries = 1000 } };

        using var document = PdfDocument.Open(source, options, ownsSource: false);

        document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.LimitIndexEntries);
        document.Trailer.GetRaw(PdfName.Root).Should().NotBeNull();
        source.LargestRead.Should().BeLessThanOrEqualTo(1024 * 1024);
    }

    [Fact]
    public void A_subsection_claiming_millions_of_rows_makes_room_for_the_rows_its_bytes_hold()
    {
        // A table at the file's start claims three million rows and holds four, then its trailer; the file goes on for 2 MB.
        // Room was made for as many rows as the bytes to the file's end hold: 100,000.
        const string Header = "%PDF-1.7\n";
        const string Catalog = "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n";
        const string Pages = "2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n";
        string Table(int first) => string.Create(
            CultureInfo.InvariantCulture,
            $"xref\n0 3000000\n0000000000 65535 f\r\n{first:D10} 00000 n\r\n{first + Catalog.Length:D10} 00000 n\r\n{first + Catalog.Length + Pages.Length:D10} 00000 n\r\ntrailer\n<< /Size 4 /Root 1 0 R >>\n");
        var objects = Header.Length + Table(0).Length;
        var text = Header + Table(objects) + Catalog + Pages + $"3 0 obj\n({new string('x', 2 * 1024 * 1024)})\nendobj\n"
            + string.Create(CultureInfo.InvariantCulture, $"startxref\n{Header.Length}\n%%EOF\n");

        using var document = PdfDocument.Open(Encoding.Latin1.GetBytes(text));

        document.Catalog.IsOfType(PdfName.Catalog).Should().BeTrue();
        document.Reader.Index.Capacity.Should().BeLessThan(64, "the table holds four rows");
    }

    /// <summary>The chains of a stale <c>/Size</c> whose older sections number past the newer ones.</summary>
    public enum StaleChain
    {
        /// <summary>Every other older section numbers one past the highest read, and the rest number nothing.</summary>
        Alternating,

        /// <summary>Each older section's table numbers below the highest read, and its /XRefStm stream one past it.</summary>
        StreamsPast,

        /// <summary>Each older section's table numbers one past the highest read, and its /XRefStm stream nothing.</summary>
        TablesPast,
    }

    /// <summary>The ways <see cref="LargeIndexFile"/> indexes its objects.</summary>
    private static class LargeIndex
    {
        public const string ClassicTable = "a classic table";
        public const string CrossReferenceStream = "a cross-reference stream";
    }

    /// <summary>A catalog, a page tree of one page, and nulls up to object 299,999, indexed the way the shape says.</summary>
    private static byte[] LargeIndexFile(string shape)
    {
        var builder = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .WithObject(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>");

        for (var number = 4; number < 300_000; number++)
        {
            builder.WithObject(number, "null");
        }

        return shape == LargeIndex.ClassicTable
            ? builder.BuildClassic(rootNumber: 1)
            : builder.BuildWithXRefStream(rootNumber: 1, compressXRefStream: true, compressionLevel: CompressionLevel.NoCompression);
    }

    /// <summary>
    /// A catalog, an empty page tree and nulls, indexed by a chain of <paramref name="sections"/> classic sections of
    /// <paramref name="rows"/> rows, each written after the objects it adds, its /Size counting every object up to them
    /// and its /Prev naming the section before it.
    /// </summary>
    private static byte[] ChainOfSections(int sections, int rows)
    {
        var file = new StringBuilder("%PDF-1.7\n");
        var offsets = new long[sections * rows];
        long previous = -1;

        for (var section = 0; section < sections; section++)
        {
            var first = section * rows;

            for (var number = Math.Max(first, 1); number < first + rows; number++)
            {
                offsets[number] = file.Length;
                file.Append(CultureInfo.InvariantCulture, $"{number} 0 obj\n").Append(number switch
                {
                    1 => "<< /Type /Catalog /Pages 2 0 R >>",
                    2 => "<< /Type /Pages /Kids [] /Count 0 >>",
                    _ => "null",
                }).Append("\nendobj\n");
            }

            var at = file.Length;
            file.Append(CultureInfo.InvariantCulture, $"xref\n{first} {rows}\n");

            for (var number = first; number < first + rows; number++)
            {
                file.Append(number == 0 ? "0000000000 65535 f\r\n" : string.Create(CultureInfo.InvariantCulture, $"{offsets[number]:D10} 00000 n\r\n"));
            }

            file.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {first + rows} /Root 1 0 R");
            file.Append(previous < 0 ? string.Empty : string.Create(CultureInfo.InvariantCulture, $" /Prev {previous}")).Append(" >>\n");
            previous = at;
        }

        file.Append(CultureInfo.InvariantCulture, $"startxref\n{previous}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(file.ToString());
    }

    /// <summary>
    /// A classic table placing the catalog, an empty page tree and objects 31 onward, then <paramref name="count"/>
    /// cross-reference streams, 41 onward, each naming the one before it, the oldest the table, written one after the other
    /// with no <c>endstream</c> of its own: the data of each, its one free row first, runs through the streams after it and
    /// <paramref name="padding"/> bytes of <c>x</c> to the <c>endstream</c> they share, which four zero bytes and a second
    /// <c>endstream</c> follow. Each stream's <c>/Length</c> is the object the table places for it, 31 onward, which gives
    /// its data up to the first <c>endstream</c>; the newest one's gives <paramref name="newestExtra"/> bytes more.
    /// </summary>
    private static byte[] SectionsSharingTheirData(int count, int padding, int newestExtra)
    {
        // Each length is written in ten digits: the offsets the first pass finds hold for the second, which writes them.
        var lengths = new long[count];
        var bytes = Array.Empty<byte>();

        for (var pass = 0; pass < 2; pass++)
        {
            var file = new HandWrittenFile(version: "1.5");
            var rows = new Dictionary<int, long>
            {
                [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
                [2] = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"),
            };

            for (var i = 0; i < count; i++)
            {
                rows[31 + i] = file.Object(31 + i, lengths[i].ToString("D10", CultureInfo.InvariantCulture));
            }

            var previous = file.Table(31 + count, rows, string.Create(CultureInfo.InvariantCulture, $"/Size {31 + count} /Root 1 0 R"));
            var dataStarts = new long[count];

            for (var i = 0; i < count; i++)
            {
                var at = file.Position;
                file.Raw(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{41 + i} 0 obj\n<< /Type /XRef /Size 60 /W [1 1 1] /Index [{51 + i} 1] /Root 1 0 R /Prev {previous} /Length {31 + i} 0 R >>\nstream\n"));
                dataStarts[i] = file.Position;
                file.Raw("\0\0\0");
                previous = at;
            }

            file.Raw(new string('x', padding));
            var dataEnd = file.Position;
            file.Raw("\nendstream\0\0\0\0\nendstream\nendobj\n");
            file.Raw(string.Create(CultureInfo.InvariantCulture, $"startxref\n{previous}\n%%EOF\n"));
            bytes = file.Bytes;

            for (var i = 0; i < count; i++)
            {
                lengths[i] = dataEnd - dataStarts[i] + (i == count - 1 ? newestExtra : 0);
            }
        }

        return bytes;
    }

    /// <summary>
    /// Opens a file of the catalog and an empty page tree, with no table, whose trailer is <c>&lt;&lt; /Root 1 0 R /Pad (…)
    /// /S</c>, then <paramref name="opening"/>, <paramref name="word"/> and <paramref name="closing"/>, then
    /// <c>/Info 2 0 R &gt;&gt;</c>, the string as long as it takes for the window the scan for trailers parses it through to
    /// end <paramref name="inside"/> bytes into <paramref name="word"/>: the document, where the trailer keyword starts, and
    /// where <paramref name="word"/> does.
    /// </summary>
    private static (PdfDocument Document, long Keyword, long Word) OpenTrailerWhoseWindowEndsInside(
        string opening, string word, string closing, int inside)
    {
        const string Before = "\n<< /Root 1 0 R /Pad (";
        var after = ") /S " + opening;
        var file = new HandWrittenFile();
        file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        var keyword = file.Position;
        file.Raw("trailer" + Before + new string('x', PdfFileReader.XRefWindow - inside - Before.Length - after.Length) + after);
        var at = file.Position;
        file.Raw(word + closing + " /Info 2 0 R >>\n");

        at.Should().Be(keyword + "trailer".Length + PdfFileReader.XRefWindow - inside);
        return (PdfDocument.Open(file.Bytes), keyword, at);
    }

    /// <summary>Where the data of the file's object stream starts.</summary>
    private static long ObjectStreamDataStart(byte[] file)
    {
        var text = Encoding.Latin1.GetString(file);
        var dictionary = text.IndexOf("/Type /ObjStm", StringComparison.Ordinal);
        return text.IndexOf("stream\n", dictionary, StringComparison.Ordinal) + "stream\n".Length;
    }

    /// <summary>The catalog, and the page tree and the page packed in object stream 4, its header rewritten.</summary>
    private static byte[] Packed(Func<string, string> header) =>
        new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .WithObject(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>")
            .BuildWithXRefStream(rootNumber: 1, compressedObjects: [2, 3], objectStreamHeader: header);

    private static TestPdfBuilder SampleDocument(string content = "BT (Bonjour) Tj ET") =>
        new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .WithObject(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R >>")
            .Stream(4, string.Empty, content);

    private static PdfDictionary Page(PdfDocument document) =>
        document.Catalog
            .GetDictionary(PdfName.Pages)
            .Required()
            .GetArray(PdfName.Kids)
            .Required()
            .Resolved(0)
            .AsDictionary()
            .Required();

    private static string PageContent(PdfDocument document)
    {
        var stream = Page(document).GetStream(PdfName.Contents).Required();
        return Encoding.ASCII.GetString(stream.Decode(document.Diagnostics).Span);
    }

    [Theory]
    [InlineData("%\n", false, false)]
    [InlineData("t ", true, false)]
    [InlineData("<a", true, false)]
    [InlineData("%\n", false, true)]
    public void The_search_for_the_trailer_past_rows_left_unread_reads_the_file_once(string hit, bool stray, bool full)
    {
        // The search took one comment, or one candidate past a stray byte, a window at a time, and read a megabyte again for
        // each: hours for a file a little past the default bound, opened from a path. A two-megabyte comment before
        // startxref keeps each window a megabyte long.
        var builder = new TestPdfBuilder();

        for (var number = 1; number <= 10; number++)
        {
            builder.WithObject(number, number switch
            {
                1 => "<< /Type /Catalog /Pages 2 0 R >>",
                2 => "<< /Type /Pages /Kids [] /Count 0 >>",
                _ => "null",
            });
        }

        var text = Encoding.Latin1.GetString(builder.BuildClassic(rootNumber: 1));
        var hits = (stray ? "x " : string.Empty) + string.Concat(Enumerable.Repeat(hit, 10_000)) + "\n";
        text = text
            .Replace("trailer\n", hits + "trailer\n", StringComparison.Ordinal)
            .Replace("startxref", "%" + new string('-', 2 * 1024 * 1024) + "\nstartxref", StringComparison.Ordinal);
        var file = Encoding.Latin1.GetBytes(text);
        var limits = full
            ? PdfReaderLimits.Default with { MaxIndexEntries = 4 }
            : PdfReaderLimits.Default with { MaxXRefSectionLength = 64 };
        using var source = new CountingSource(file);

        using var document = PdfDocument.Open(source, new PdfReaderOptions { Limits = limits }, ownsSource: false);

        document.Trailer.GetRaw(PdfName.Root).Should().NotBeNull();
        source.BytesRead.Should().BeLessThan(2L * file.Length);
    }

    [Theory]
    [InlineData("stream data whose direct /Length endstream confirms", false)]
    [InlineData("a string", false)]
    [InlineData("a string that holds endobj", true)]
    [InlineData("a string a comment holding the word obj comes before", true)]
    [InlineData("a comment between objects", false)]
    [InlineData("a comment the end-of-file marker opens before a stream's endstream", false)]
    [InlineData("stream data whose /Length is an object", true)]
    [InlineData("stream data whose direct /Length is wrong", true)]
    [InlineData("nothing: written between objects", true)]
    public void A_rebuild_takes_no_header_another_object_holds(string where, bool injected)
    {
        // The file's startxref names nothing, and its index is rebuilt as it opens. A second page tree, object 2 again,
        // marked /Injected, lies where the case says. The scan delimits each object it takes, as a load reads it: a header
        // inside another object's value, or inside data its length confirms, is none, nor one a comment holds; object 2 is
        // then the file's own page tree. Where the length is not confirmed, the data ends at the next header, as a load's
        // search ends it, and the header there is taken, the last definition winning (#171). A string that holds endobj
        // runs past its object's end, as one an unbalanced parenthesis runs on does, and delimits nothing: the header on
        // the line after its start is taken as the bytes give it. So it is where a comment that holds the word obj comes
        // between the object's header and its value.
        const string Injected = "2 0 obj\n<< /Type /Pages /Kids [] /Count 0 /Injected true >>\nendobj\n";
        var file = new HandWrittenFile();
        file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] >>");
        var length = Injected.Length.ToString(CultureInfo.InvariantCulture);

        switch (where)
        {
            case "stream data whose direct /Length endstream confirms":
                file.Object(5, $"<< /Length {length} >>\nstream\n{Injected}\nendstream");
                break;
            case "a string":
                file.Object(5, "(\n" + Injected[..Injected.IndexOf("endobj", StringComparison.Ordinal)] + ")");
                break;
            case "a string that holds endobj":
                file.Object(5, $"(\n{Injected})");
                break;
            case "a string a comment holding the word obj comes before":
                file.Object(5, "% the value of obj 5\n(\n" + Injected[..Injected.IndexOf("endobj", StringComparison.Ordinal)] + ")");
                break;
            case "a comment between objects":
                file.Raw("% " + Injected.Replace("\n", " ", StringComparison.Ordinal) + "\n");
                break;
            case "a comment the end-of-file marker opens before a stream's endstream":
                file.Object(5, "<< /Length 4 >>\nstream\nabcd\n%%EOF " + Injected.Replace("\n", " ", StringComparison.Ordinal) + "\nendstream");
                break;
            case "stream data whose /Length is an object":
                file.Object(5, $"<< /Length 6 0 R >>\nstream\n{Injected}\nendstream");
                file.Object(6, length);
                break;
            case "stream data whose direct /Length is wrong":
                file.Object(5, $"<< /Length 3 >>\nstream\n{Injected}\nendstream");
                break;
            default:
                file.Raw(Injected);
                break;
        }

        file.Raw("trailer\n<< /Root 1 0 R >>\nstartxref\n9\n%%EOF\n");
        using var document = PdfDocument.Open(file.Bytes);

        document.WasRepaired.Should().BeTrue();
        document.GetObject(new PdfObjectId(2)).AsDictionary().Required().ContainsKey(PdfName.Get("Injected")).Should().Be(injected);
    }

    [Theory]
    [InlineData("a string object an unbalanced parenthesis runs on")]
    [InlineData("a string in a dictionary an unbalanced parenthesis runs on to a stray one")]
    [InlineData("a string a cut leaves open, an update after it")]
    [InlineData("a hexadecimal string a cut leaves open, an update after it")]
    public void A_string_that_runs_past_its_object_s_end_hides_no_header_after_it(string shape)
    {
        // Some writers leave a parenthesis in a string unescaped. An unbalanced one runs the string, as a load lexes it, past
        // its object's end: to a stray parenthesis further on, or to the end of the file. A file cut inside a string and
        // updated after leaves it open the same way. Such a string delimits nothing: the objects after it, each on a line
        // of its own, are the file's, and the index rebuilt as the file opens takes them (#171).
        var file = new HandWrittenFile();
        file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");

        switch (shape)
        {
            case "a string object an unbalanced parenthesis runs on":
                file.Object(5, "(Report :-( draft)");
                break;
            case "a string in a dictionary an unbalanced parenthesis runs on to a stray one":
                file.Object(5, "<< /Title (Report :-( draft) >>");
                break;
            case "a string a cut leaves open, an update after it":
                file.Raw("5 0 obj\n(lorem-ip\n");
                break;
            default:
                file.Raw("5 0 obj\n<48656C6C6F\n");
                break;
        }

        // A hexadecimal string runs to the next '>': object 6, which holds none, lies within it with its endobj.
        file.Object(6, "(six)");
        file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] >>");
        file.Object(7, "<< /Contents (Done :-)) >>");
        file.Object(8, "(eight)");
        file.Raw("trailer\n<< /Root 1 0 R >>\nstartxref\n9\n%%EOF\n");
        using var document = PdfDocument.Open(file.Bytes);

        document.WasRepaired.Should().BeTrue();
        document.GetObject(new PdfObjectId(2)).AsDictionary().Required().GetArray(PdfName.Kids).Required().Count.Should().Be(1);
        document.GetObject(new PdfObjectId(3)).AsDictionary().Required().IsOfType(PdfName.Page).Should().BeTrue();
        document.GetObject(new PdfObjectId(7)).AsDictionary().Should().NotBeNull();
        document.GetObject(new PdfObjectId(6)).AsText().Should().Be("six");
        document.GetObject(new PdfObjectId(8)).AsText().Should().Be("eight");
        document.Diagnostics.Should().NotContain(d => d.Code == PdfDiagnosticCodes.TrailerRootRecovered);
    }

    [Theory]
    [InlineData("[ true (\n{2}\n) ]", false)]
    [InlineData("<< /Flag true /Odd ) /Note (\n{2}\n) >>", false)]
    [InlineData("(\n{2}\nendobjects)", false)]
    [InlineData("(\n{2}\nsendobj)", false)]
    [InlineData("(endobj\n{2}\n)", true)]
    [InlineData("(\n{2}\nendobj)", true)]
    [InlineData("true (\n{2}\n)", true)]
    [InlineData(") (\n{2}\n)", true)]
    public void A_rebuild_delimits_an_object_as_a_load_lexes_its_value(string value, bool injected)
    {
        // Object 5's value holds, on a line of its own inside a string, a second page tree, object 2 again, marked /Injected.
        // The scan delimits object 5 as a load lexes it: a keyword or a stray parenthesis inside a container is one of its
        // tokens, and endobj inside a string a word only where no regular character runs into it, so the string, delimited,
        // hides the header. A string that holds endobj as a word runs past its object's end and delimits nothing; a value
        // that ends with its first token, true, leaves the string after it between objects, and one no load reads, a stray
        // parenthesis, has what follows its header read as the bytes give it: the header on its own line is taken (#171).
        const string Injected = "2 0 obj\n<< /Type /Pages /Kids [] /Count 0 /Injected true >>";
        var file = new HandWrittenFile();
        file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        file.Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        file.Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] >>");
        file.Object(5, value.Replace("{2}", Injected, StringComparison.Ordinal));
        file.Raw("trailer\n<< /Root 1 0 R >>\nstartxref\n9\n%%EOF\n");
        using var document = PdfDocument.Open(file.Bytes);

        document.WasRepaired.Should().BeTrue();
        document.GetObject(new PdfObjectId(2)).AsDictionary().Required().ContainsKey(PdfName.Get("Injected")).Should().Be(injected);
    }

    [Fact]
    public void A_header_whose_line_starts_in_a_stream_s_data_is_read_from_the_stream_s_end_wherever_the_piece_starts()
    {
        // Stream 4's data ends its line with a comment, " % def", and its line goes on past the data: " endstream endobj 5",
        // object 5's generation and obj on the next line. The scan read the line of object 5's number from its start inside
        // stream 4's data, where the comment took the number, and lost object 5 — unless stream 4 crossed the end of a piece,
        // where the line was read from the stream's end and object 5 taken. A header is read from where the object before it
        // ends, in a piece as from further back (#171).
        var file = new HandWrittenFile();
        file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        file.Raw("4 0 obj\n<< /Length 206 >>\nstream\n" + new string('x', 200) + " % def endstream endobj 5\n0 obj\n(five)\nendobj\n");
        file.Raw("trailer\n<< /Root 1 0 R >>\nstartxref\n9\n%%EOF\n");
        using var document = PdfDocument.Open(file.Bytes);

        document.WasRepaired.Should().BeTrue();
        document.GetObject(new PdfObjectId(5)).AsText().Should().Be("five");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(70)]
    public void A_header_glued_to_a_stream_s_endstream_is_none_however_much_white_space_comes_before_the_keyword(int spaces)
    {
        // Stream 4's direct length is confirmed by the endstream after it, and object 5's header is glued to the keyword:
        // "endstream5 0 obj". A load's lexer reads "endstream5" as one token, and the number is no header's. The scan ended
        // the stream past the whole token when a first read saw it, and past the keyword alone when white space ran past a
        // first read, where the digits after it read as object 5. It ends the stream past the whole token either way, and
        // object 5 is the one written before (#171, #327).
        var file = new HandWrittenFile();
        file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        file.Object(5, "(old five)");
        file.Raw("4 0 obj\n<< /Length 10 >>\nstream\n0123456789" + new string(' ', spaces) + "endstream5 0 obj\n(new five)\nendobj\n");
        file.Raw("trailer\n<< /Root 1 0 R >>\nstartxref\n9\n%%EOF\n");
        using var document = PdfDocument.Open(file.Bytes);

        document.WasRepaired.Should().BeTrue();
        document.GetObject(new PdfObjectId(5)).AsText().Should().Be("old five");
    }

    [Fact]
    public void A_string_that_runs_past_MaxObjectLength_is_read_as_any_object_past_it_wherever_its_piece_ends()
    {
        // Object 5's string holds endobj and runs 6 KB, past a MaxObjectLength of 4 KB; a line inside it, "% 9 0 obj", a
        // comment between objects once object 5 is not delimited. The scan read what a runaway string runs into as the bytes
        // give it when its piece held the string whole, and the header in the comment defined object 9 again; when the piece
        // cut it, the string ran past the bound and the comment hid the header. A string past the bound is read as any
        // object past it, wherever its piece ends (#171).
        var file = new HandWrittenFile();
        file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        file.Object(9, "(real nine)");
        file.Object(5, "(a endobj " + new string('x', 6000) + "\n% 9 0 obj\n(inner)\nendobj\n" + new string('x', 100) + ")");
        file.Raw("trailer\n<< /Root 1 0 R >>\nstartxref\n9\n%%EOF\n");
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = 4096 } };
        using var document = PdfDocument.Open(file.Bytes, options);

        document.WasRepaired.Should().BeTrue();
        document.GetObject(new PdfObjectId(9)).AsText().Should().Be("real nine");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_object_past_MaxObjectLength_is_let_go_at_the_bound_wherever_its_piece_ends(bool nearThePieceEnd)
    {
        // Object 5's string runs about 4.5 KB, past a MaxObjectLength of 4 KB, and holds past the bound a stream 2 whose data,
        // its length confirmed, spells object 3 again. The scan took the headers within what it had lexed of object 5
        // undelimited: as far as the string's end where the piece held it, where stream 2's header lay, and object 3 was
        // defined again; only to the bound where the piece cut it. It lexes no more than the bound wherever the piece ends:
        // stream 2 is delimited, and its data hides object 3's header (#171).
        var file = new HandWrittenFile();
        file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        file.Object(3, "(real three)");

        if (nearThePieceEnd)
        {
            file.Raw("%" + new string('-', (1024 * 1024) - 1000 - (int)file.Position) + "\n");
        }

        file.Object(5, "(" + new string('x', 4200) + "\n2 0 obj << /Length 23 >> stream\n3 0 obj (inner) endobj\nendstream\n" + new string('x', 200) + ")");
        file.Raw("trailer\n<< /Root 1 0 R >>\nstartxref\n9\n%%EOF\n");
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = 4096 } };
        using var document = PdfDocument.Open(file.Bytes, options);

        document.WasRepaired.Should().BeTrue();
        document.GetObject(new PdfObjectId(3)).AsText().Should().Be("real three");
    }

    [Fact]
    public void A_stream_whose_endstream_a_long_run_of_letters_follows_is_confirmed_as_a_load_confirms_it()
    {
        // Stream 4's direct length is right, and "endstream" is glued to sixty letters: a load lexes them as one token after
        // the data, and confirms the length. The scan gave up on a token longer than its read and left the length
        // unconfirmed, and the header the data spells replaced the catalog. It reads the token to its end (#171).
        var file = new HandWrittenFile();
        file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        file.Raw("4 0 obj\n<< /Length 32 >>\nstream\n1 0 obj << /Type /Fake >> endobj\nendstream" + new string('X', 60) + "\nendobj\n");
        file.Raw("trailer\n<< /Root 1 0 R >>\nstartxref\n9\n%%EOF\n");
        using var document = PdfDocument.Open(file.Bytes);

        document.WasRepaired.Should().BeTrue();
        document.Catalog.IsOfType(PdfName.Catalog).Should().BeTrue();
    }

    [Fact]
    public void An_object_a_piece_cuts_is_delimited_under_a_MaxObjectLength_smaller_than_a_first_window()
    {
        // Object 5, a string of 2 KB that holds on a line of its own a second object 9, starts a thousand bytes before the end
        // of the scan's first piece, under a MaxObjectLength of 4 KB. The scan gave a value its piece cuts up before reading
        // any window, as though the window it was about to read reached the bound: object 5 was not delimited, and object 9
        // defined again. It reads from the header as far as the bound, and object 5, within it, hides the header (#171).
        var file = new HandWrittenFile();
        file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        file.Object(9, "(real nine)");
        file.Raw("%" + new string('-', (1024 * 1024) - 1000 - (int)file.Position) + "\n");
        file.Object(5, "(" + new string('x', 900) + "\n9 0 obj\n(inner)\n" + new string('x', 1000) + ")");
        file.Raw("trailer\n<< /Root 1 0 R >>\nstartxref\n9\n%%EOF\n");
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = 4096 } };
        using var document = PdfDocument.Open(file.Bytes, options);

        document.WasRepaired.Should().BeTrue();
        document.GetObject(new PdfObjectId(9)).AsText().Should().Be("real nine");
    }

    [Fact]
    public void Objects_written_on_the_line_of_a_string_an_unbalanced_parenthesis_runs_on_are_the_file_s()
    {
        // Object 5's string leaves a parenthesis open, and the objects after it are written on the same line. The string runs
        // past object 5's end and delimits nothing; the sweep that hides a header in a string between objects then read the
        // rest of the line as that string, and hid them. What a runaway string runs into is read as the bytes give it (#171).
        var file = new HandWrittenFile();
        file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        file.Raw("5 0 obj (Report :-( draft) endobj 2 0 obj << /Type /Pages /Kids [3 0 R] /Count 1 >> endobj "
            + "3 0 obj << /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] >> endobj 6 0 obj (six) endobj\n");
        file.Raw("trailer\n<< /Root 1 0 R >>\nstartxref\n9\n%%EOF\n");
        using var document = PdfDocument.Open(file.Bytes);

        document.WasRepaired.Should().BeTrue();
        document.GetObject(new PdfObjectId(2)).AsDictionary().Required().GetArray(PdfName.Kids).Required().Count.Should().Be(1);
        document.GetObject(new PdfObjectId(3)).AsDictionary().Required().IsOfType(PdfName.Page).Should().BeTrue();
        document.GetObject(new PdfObjectId(6)).AsText().Should().Be("six");
    }

    [Fact]
    public void What_the_scan_reads_past_declared_lengths_leaves_the_loads_their_searches_for_endstream()
    {
        // Thousands of stream headers of object 9, each with a direct /Length ending at a different offset of one run of
        // spaces, follow stream 3, whose /Length says 5 of its 100,000 bytes. The scan read the white space after each length
        // with the loads' searches for endstream, and spent what they may read: stream 3 then kept its declared length
        // unsearched. The scan's reads past declared lengths are bounded apart, and stream 3 is searched to its endstream
        // (#171).
        var text = new StringBuilder("%PDF-1.7\n1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n");
        text.Append("3 0 obj\n<< /Length 5 >>\nstream\n").Append('d', 100_000).Append("\nendstream\nendobj\n");

        for (var i = 0; i < 5000; i++)
        {
            text.Append(CultureInfo.InvariantCulture, $"9 0 obj<</Length {200_000 + (i * 37):D10}>>stream\n");
        }

        text.Append(' ', 256 * 1024).Append("x\ntrailer\n<< /Root 1 0 R >>\nstartxref\n9\n%%EOF\n");
        using var document = PdfDocument.Open(Encoding.Latin1.GetBytes(text.ToString()));

        document.WasRepaired.Should().BeTrue();
        document.GetObject(new PdfObjectId(3)).Should().BeOfType<PdfStream>().Which.Data.Length.Should().Be(100_000);
    }

    [Theory]
    [InlineData(0, "real seven")]
    [InlineData(70, "inner")]
    public void A_run_of_letters_glued_to_an_endstream_confirms_no_length_once_the_scan_has_read_what_it_may_past_lengths(int headers, string seven)
    {
        // Stream headers of object 9, each with a direct /Length ending in one run of 12,000 spaces, come before stream 4,
        // whose length is right and whose endstream is glued to 300,000 letters, its data spelling object 7 again. Alone,
        // stream 4 is confirmed, the run read to its end. After the headers, the scan's reads past declared lengths reach
        // what they may while they read the run: the length is not confirmed, and the header its data spells is taken.
        var file = new HandWrittenFile();
        file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        file.Object(7, "(real seven)");

        for (var i = 0; i < headers; i++)
        {
            file.Raw(string.Create(CultureInfo.InvariantCulture, $"9 0 obj<</Length {headers * 36:D10}>>stream\n"));
        }

        file.Raw(new string(' ', 12_000) + "x\n4 0 obj\n<< /Length 23 >>\nstream\n7 0 obj (inner) endobj\nendstream" + new string('X', 300_000));
        file.Raw("\nendobj\ntrailer\n<< /Root 1 0 R >>\nstartxref\n9\n%%EOF\n");
        using var document = PdfDocument.Open(file.Bytes);

        document.WasRepaired.Should().BeTrue();
        document.GetObject(new PdfObjectId(7)).AsText().Should().Be(seven);
    }

    [Theory]
    [InlineData(4096, null)]
    [InlineData(16 * 1024 * 1024, "nine")]
    public void A_header_whose_tokens_run_back_past_MaxObjectLength_from_its_obj_is_none(int maxObjectLength, string? nine)
    {
        // Object 9's number lies more than a megabyte of spaces before its generation, past the start of the scan's piece:
        // the scan reads it again from further back, no further than MaxObjectLength from its obj, as a load reads an
        // object no further. Under the default it is object 9; under 4 KB it is none, unreported (#171).
        var file = new HandWrittenFile();
        file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        file.Raw("9" + new string(' ', 1100 * 1024) + "0 obj\n(nine)\nendobj\n");
        file.Raw("trailer\n<< /Root 1 0 R >>\nstartxref\n9\n%%EOF\n");
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = maxObjectLength } };
        using var document = PdfDocument.Open(file.Bytes, options);

        var text = (document.GetObject(new PdfObjectId(9)) as PdfString)?.ToText();

        document.WasRepaired.Should().BeTrue();
        text.Should().Be(nine);
    }

    [Theory]
    [InlineData(64 * 1024, "injected six")]
    [InlineData(16 * 1024 * 1024, "six")]
    public void An_object_longer_than_MaxObjectLength_delimits_nothing(int maxObjectLength, string six)
    {
        // Object 5, an array of 100 KB that the end of the scan's first piece cuts, holds on a line of its own inside a string
        // a second object 6. The scan reads a value its piece cuts again from its header, in windows eight times larger each
        // time, within MaxObjectLength: under the default, object 5 is delimited and hides the header; under 64 KB it is
        // not, and what follows its header is read as the bytes give it: object 6 is defined again (#171).
        var file = new HandWrittenFile();
        file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        file.Object(6, "(six)");
        file.Raw("%" + new string('-', (1024 * 1024) - 300 - (int)file.Position) + "\n");
        file.Object(5, "[ (\n6 0 obj\n(injected six)\n) " + string.Concat(Enumerable.Repeat("1 ", 50 * 1024)) + "]");
        file.Raw("trailer\n<< /Root 1 0 R >>\nstartxref\n9\n%%EOF\n");
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = maxObjectLength } };
        using var document = PdfDocument.Open(file.Bytes, options);

        document.WasRepaired.Should().BeTrue();
        document.GetObject(new PdfObjectId(6)).AsText().Should().Be(six);
    }

    [Theory]
    [InlineData(0, "nine")]
    [InlineData(-1, "inner nine")]
    public void An_object_that_ends_the_file_is_delimited_under_a_MaxObjectLength_as_long_as_it(int slack, string nine)
    {
        // Object 5, the last in a file cut after its dictionary, holds on a line of its own inside a string a second object 9.
        // Under a MaxObjectLength as long as object 5, the scan lexes it to the end of the file and delimits it: it hides the
        // header. A byte shorter, object 5 runs past the bound, and object 9 is defined again (#171).
        var file = new HandWrittenFile();
        file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        file.Object(9, "(nine)");
        file.Raw("trailer\n<< /Root 1 0 R >>\n");
        var five = file.Position;
        file.Raw("5 0 obj\n<< /S (" + new string('x', 200) + "\n9 0 obj\n(inner nine)\n) >>\n");
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = (int)(file.Position - five) + slack } };
        using var document = PdfDocument.Open(file.Bytes, options);

        document.WasRepaired.Should().BeTrue();
        document.GetObject(new PdfObjectId(9)).AsText().Should().Be(nine);
    }

    [Fact]
    public void A_stream_whose_tail_MaxObjectLength_ends_on_is_confirmed_from_the_bytes_the_scan_lexed()
    {
        // Stream 4's data ends as many bytes short of MaxObjectLength from its header as the bytes after a stream's data a
        // load reads to tell its endstream: the scan reads them in what it lexed of the object, as under the default, and
        // the rebuild reads the file no more (#171).
        var file = new HandWrittenFile();
        file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        var four = file.Position;
        file.Raw("4 0 obj\n<< /Length 10000 >>\nstream\n");
        var tail = (int)(file.Position - four) + 10_000 + PdfObjectParser.EndObjLookahead;
        file.Raw(new string('d', 10_000) + "\nendstream\nendobj\n");
        file.Object(6, "(six)");
        file.Raw("trailer\n<< /Root 1 0 R >>\nstartxref\n9\n%%EOF\n");

        Read(PdfReaderLimits.Default with { MaxObjectLength = tail }).Should().Be(Read(PdfReaderLimits.Default));

        long Read(PdfReaderLimits limits)
        {
            using var source = new CountingSource(file.Bytes);
            using var document = PdfDocument.Open(source, new PdfReaderOptions { Limits = limits }, ownsSource: false);
            document.WasRepaired.Should().BeTrue();
            return source.BytesRead;
        }
    }

    [Fact]
    public void An_obj_that_starts_the_data_has_no_header_before_it()
    {
        // Bytes before the file's header open with "obj": the scan meets it at the very start of the data, where no number
        // lies before it, and takes no header there; the objects after are the file's (#171).
        var file = new HandWrittenFile(prefix: "obj\n");
        file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        file.Raw("trailer\n<< /Root 1 0 R >>\nstartxref\n9\n%%EOF\n");
        using var document = PdfDocument.Open(file.Bytes);

        document.WasRepaired.Should().BeTrue();
        document.Catalog.IsOfType(PdfName.Catalog).Should().BeTrue();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5000)]
    public void A_header_whose_line_starts_in_the_object_before_it_is_read_from_where_that_object_ends(int spaces)
    {
        // Stream 4's data runs on one line, longer than MaxObjectLength, lowered to 4 KB, and past the scan's first piece,
        // and its line goes on past the data: " endstream endobj 5", object 5's generation and obj on the next line. The
        // scan, which goes on past stream 4 from the next piece, read a header back from its obj as far as the line its
        // number ends starts, which lies in stream 4's data: in windows eight times larger each time up to MaxObjectLength,
        // then gave the header up, and object 5 was lost. A header starts no further back than where the object before it
        // ends, from where a load's lexer reads tokens anew: the line is read from there (#171), in the piece that holds
        // it, however many spaces part the number from the stream's end, more than MaxObjectLength too.
        var data = new string('x', 1100 * 1024);
        var file = new HandWrittenFile();
        file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        file.Raw(string.Create(CultureInfo.InvariantCulture, $"4 0 obj\n<< /Length {data.Length} >>\nstream\n{data} endstream endobj{new string(' ', spaces)}5\n0 obj\n(five)\nendobj\n"));
        file.Raw("trailer\n<< /Root 1 0 R >>\nstartxref\n9\n%%EOF\n");
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = 4096 } };
        using var document = PdfDocument.Open(file.Bytes, options);

        document.WasRepaired.Should().BeTrue();
        document.GetObject(new PdfObjectId(5)).AsText().Should().Be("five");
        document.GetObject(new PdfObjectId(4)).Should().BeOfType<PdfStream>().Which.Data.Length.Should().Be(data.Length);
    }

    [Theory]
    [InlineData("stream data", false)]
    [InlineData("a string", false)]
    [InlineData("a comment between objects", false)]
    [InlineData("the end of a name between objects", false)]
    [InlineData("nothing: written between objects", true)]
    public void A_rebuild_merges_no_trailer_another_object_or_a_comment_holds(string where, bool merged)
    {
        // The file's startxref names nothing, and its trailer gives /Root alone. A trailer keyword, or a name that ends with
        // its letters, lies where the case says, before a dictionary that gives /Info and /Encrypt. A trailer a stream's
        // data, a string or a comment holds is none, nor a name's last letters: the rebuild merges nothing of it, and the
        // document is not encrypted (#171, #154).
        const string Dictionary = "<< /Info 4 0 R /Encrypt << /Filter /Standard /V 1 /R 2 /O <00> /U <00> /P -4 >> >>";
        var file = new HandWrittenFile();
        file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        file.Object(4, "<< /Title (info) >>");
        var keyword = "trailer\n" + Dictionary;

        switch (where)
        {
            case "stream data":
                file.Object(5, string.Create(CultureInfo.InvariantCulture, $"<< /Length {keyword.Length} >>\nstream\n{keyword}\nendstream"));
                break;
            case "a string":
                file.Object(5, "(" + keyword + ")");
                break;
            case "a comment between objects":
                file.Raw("% trailer " + Dictionary + "\n");
                break;
            case "the end of a name between objects":
                file.Raw("/Mytrailer " + Dictionary + "\n");
                break;
            default:
                file.Raw(keyword + "\n");
                break;
        }

        file.Raw("trailer\n<< /Root 1 0 R >>\nstartxref\n9\n%%EOF\n");
        using var document = PdfDocument.Open(file.Bytes, new PdfReaderOptions { ThrowOnEncrypted = false });

        document.WasRepaired.Should().BeTrue();
        document.Trailer.ContainsKey(PdfName.Info).Should().Be(merged);
        document.IsEncrypted.Should().Be(merged);
    }

    [Theory]
    [InlineData("4 0 obj", true)]
    [InlineData("4 +0 obj", true)]
    [InlineData("4 -0 obj", true)]
    [InlineData("+4 0 obj", true)]
    [InlineData("++4 0 obj", true)]
    [InlineData("+-4 0 obj", true)]
    [InlineData("-4 0 obj", false)]
    [InlineData("-+4 0 obj", false)]
    [InlineData("4 -1 obj", false)]
    public void A_header_whose_numbers_carry_signs_is_read_as_the_lexer_reads_them_at_its_row_and_by_a_rebuild(string header, bool four)
    {
        // ISO 32000-1 lets an integer carry a sign (7.3.3), and the lexer reads a run of signs with the number, the first sign
        // deciding. A load at the row, the search near it and the rebuild's scan read a header so: 4 +0 obj and +4 0 obj are
        // object 4's, -4 0 obj is none, its number below 1, nor the 4 0 obj after its sign (#327).
        using var atItsRow = PdfDocument.Open(NearbyFiles.FourWrittenAs(header));
        using var rebuilt = PdfDocument.Open(NearbyFiles.FourWrittenAs(header, startxrefLost: true));

        Text(atItsRow).Should().Be(four ? "four" : null);
        atItsRow.Diagnostics.Contains(PdfDiagnosticCodes.XRefOffsetAdjusted).Should().BeFalse();
        Text(rebuilt).Should().Be(four ? "four" : null);
        rebuilt.WasRepaired.Should().BeTrue();

        static string? Text(PdfDocument document) => document.GetObject(new PdfObjectId(4)) is PdfString text ? text.ToText() : null;
    }

    [Theory]
    [InlineData("as written")]
    [InlineData("the later stream's number met first")]
    public void A_rebuild_takes_no_member_from_an_object_stream_whose_number_a_later_object_stream_lists(string order)
    {
        // Object 6 is written directly, then in object stream 10, and object stream 20, written after both, lists an object
        // 10: 10 is no object stream of the rebuilt index, and 6 is the one written directly. Object 7 is in object stream 30,
        // then in 10: 30's. Taken in as their numbers were met, 10's members kept their place once 20 replaced it, and read as
        // null; and where 20's number came first, 10 was skipped, so that the result followed the order the numbers were met
        // in. The rebuild takes the streams in from the last written (#348). An earlier definition of 20, written first, makes
        // its number the first met.
        var file = new HandWrittenFile(version: "1.5");

        if (order != "as written")
        {
            file.Object(20, "null");
        }

        file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        file.Object(6, "(six written directly)");
        file.ObjectStream(30, [(7, "(seven in 30)")]);
        file.ObjectStream(10, [(6, "(six in 10)"), (7, "(seven in 10)")]);
        file.ObjectStream(20, [(10, "(ten in 20)")]);
        file.Raw("trailer\n<< /Root 1 0 R >>\nstartxref\n9\n%%EOF\n");
        using var document = PdfDocument.Open(file.Bytes);

        document.WasRepaired.Should().BeTrue();
        document.GetObject(new PdfObjectId(6)).AsText().Should().Be("six written directly");
        document.GetObject(new PdfObjectId(7)).AsText().Should().Be("seven in 30");
        document.GetObject(new PdfObjectId(10)).AsText().Should().Be("ten in 20");
    }

    [Fact]
    public void A_stream_whose_length_names_an_object_a_later_object_stream_defines_again_is_read_with_the_length_the_rebuild_serves()
    {
        // Stream 8's /Length is object 9, written directly as 3, then again, as 10, in object stream 20, written after it. The
        // expansion loads stream 8, to tell whether it is an object stream, before 20 replaces 9: read through the definition
        // it replaces, 8 stayed cached with 3 bytes of its 10. What was read through a replaced definition is read again from
        // the rebuilt index (#348).
        var file = new HandWrittenFile(version: "1.5");
        file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        file.Object(9, "3");
        file.Object(8, "<< /Length 9 0 R >>\nstream\n0123456789\nendstream");
        file.ObjectStream(20, [(9, "10")]);
        file.Raw("trailer\n<< /Root 1 0 R >>\nstartxref\n9\n%%EOF\n");
        using var document = PdfDocument.Open(file.Bytes);

        var stream = document.GetObject(new PdfObjectId(8)).Should().BeOfType<PdfStream>().Subject;

        document.WasRepaired.Should().BeTrue();
        document.GetObject(new PdfObjectId(9)).AsInteger().Should().Be(10);
        stream.Data.Length.Should().Be(10);
        document.Reader.TryGetStreamLengthFault(8, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("a long comment")]
    [InlineData("white space after a stream's direct length")]
    [InlineData("a run of letters glued to the endstream after a stream's direct length")]
    [InlineData("one line holding a header at each piece's start")]
    public void A_rebuild_reads_a_file_in_pieces_no_larger_than_a_megabyte(string shape)
    {
        // The scan read each megabyte with the byte past it, which a file source rents from a pool that rounds 1,048,577 up
        // to 2 MiB (#350). It reads a piece of a megabyte, the byte past what it searches its last. The white space after a
        // stream's direct length was read in one window as long as it ran, and the line of a header found further back than
        // its piece in one window as long as the line: memory followed the file. Both are read as a piece or less at a time,
        // the white space as a load reads it, and so is a run of letters glued to the endstream after a length (#171).
        var text = new StringBuilder("%PDF-1.7\n1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n");

        switch (shape)
        {
            case "a long comment":
                text.Append('%').Append('-', 3 * 1024 * 1024).Append('\n');
                break;
            case "white space after a stream's direct length":
                text.Append("3 0 obj\n<< /Length 4 >>\nstream\nabcd").Append(' ', 3 * 1024 * 1024).Append('\n');
                break;
            case "a run of letters glued to the endstream after a stream's direct length":
                text.Append("3 0 obj\n<< /Length 4 >>\nstream\nabcdendstream").Append('X', 4 * 1024 * 1024).Append("\nendobj\n");
                break;
            default:
                // Each piece starts most likely between a number and its obj, a thousand spaces apart, on one line.
                while (text.Length < 6 * 1024 * 1024)
                {
                    text.Append("9 ").Append(' ', 1000).Append("0 obj > ");
                }

                text.Append('\n');
                break;
        }

        text.Append("trailer\n<< /Root 1 0 R >>\nstartxref\n9\n%%EOF\n");
        using var source = new CountingSource(Encoding.Latin1.GetBytes(text.ToString()));

        using var document = PdfDocument.Open(source, options: null, ownsSource: false);

        document.WasRepaired.Should().BeTrue();
        document.Catalog.IsOfType(PdfName.Catalog).Should().BeTrue();
        source.LargestRead.Should().BeLessThanOrEqualTo(1024 * 1024);
        source.BytesRead.Should().BeLessThan(8L * source.Length);
    }

    [Fact(Timeout = 60_000)]
    public async Task A_rebuild_goes_on_where_the_source_gives_no_byte_of_what_its_scan_sweeps_again_from_an_earlier_piece()
    {
        // The trailer keyword lies past a comment longer than the scan's piece: to tell whether a comment holds it, the scan
        // sweeps the bytes before its piece from where it last stood, past object 2. The source gives no byte of that read,
        // as a caller's may (#52): the sweep stands where it was, and the scan goes on rather than asking again forever.
        var text = "%PDF-1.7\n1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n" +
            "%" + new string('-', 3 * 1024 * 1024) + "\ntrailer\n<< /Root 1 0 R >>\nstartxref\n9\n%%EOF\n";
        var two = text.IndexOf("2 0 obj", StringComparison.Ordinal);
        var source = new ShortCountSource(Encoding.Latin1.GetBytes(text), (offset, held) => offset > two && offset < two + 4096 ? 0 : held);

        using var document = await Task.Run(
            () => PdfDocument.Open(source, options: null, ownsSource: true), TestContext.Current.CancellationToken);

        document.WasRepaired.Should().BeTrue();
        document.Trailer.GetRaw(PdfName.Root).Should().NotBeNull();
        document.Diagnostics.Should().NotContain(d => d.Code == PdfDiagnosticCodes.TrailerRootRecovered);
        source.ShortReads.Should().BePositive();
    }

    [Theory]
    [InlineData("small streams whose length is an object")]
    [InlineData("headers inside one string left open")]
    [InlineData("headers whose lengths end in one run of white space")]
    public void A_rebuild_reads_the_file_a_few_times_at_most_whatever_its_objects_repeat(string shape)
    {
        // Each stream whose length the scan cannot confirm read a megabyte from the file to find its endstream however near
        // it lay; each header inside a string left open had its value lexed, from the file, in windows up to MaxObjectLength;
        // each stream whose direct length ends in a run of white space read the whole run. Each read again what the one
        // before had read, and a file of 256 KB read gigabytes. The scan searches the bytes it holds first, lexes no byte
        // for one object after another, and reads the white space after a length as a load does, once for each place, with
        // the searches for endstream: what it reads follows the file (#171).
        var text = new StringBuilder("%PDF-1.7\n1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n");
        var (unit, until, tail) = shape switch
        {
            "small streams whose length is an object" =>
                ("3 0 obj <</Length 4 0 R>>stream\nx\nendstream\nendobj\n4 0 obj 1 endobj\n", 256 * 1024, string.Empty),
            "headers inside one string left open" => ("3 0 obj [(\n", 1024 * 1024, new string('x', 2 * 1024 * 1024)),
            _ => ("3 0 obj << /Length 262144 >>\nstream\n", 256 * 1024, new string(' ', 512 * 1024)),
        };

        // The headers inside one string left open end the first piece, which the string runs past.
        if (until > 256 * 1024)
        {
            text.Append('%').Append('-', until - (16 * 1024)).Append('\n');
        }

        while (text.Length < until)
        {
            text.Append(unit);
        }

        text.Append(tail).Append("\ntrailer\n<< /Root 1 0 R >>\nstartxref\n9\n%%EOF\n");
        using var source = new CountingSource(Encoding.Latin1.GetBytes(text.ToString()));

        using var document = PdfDocument.Open(source, options: null, ownsSource: false);

        document.WasRepaired.Should().BeTrue();
        document.Catalog.IsOfType(PdfName.Catalog).Should().BeTrue();
        document.GetObject(new PdfObjectId(3)).Should().NotBeNull();
        source.BytesRead.Should().BeLessThan(8L * source.Length);
    }

    [Theory]
    [InlineData("a comment", 6)]
    [InlineData("a comment whose word trailer ends two bytes before the bytes searched end", 2)]
    public void Each_piece_of_the_scan_reads_again_only_the_lead_before_where_the_search_of_the_piece_before_stopped(string shape, int resumed)
    {
        // The scan's first piece holds the file's first megabyte and searches it but for its last byte. The next piece's
        // search resumes `resumed` bytes before the bytes searched end, at the first place a keyword that runs past them may
        // start: a trailer keyword's length but one, or, past the word trailer that ends two bytes before their end, where
        // that word ends, an obj able to start there. The piece starts the 64 bytes a short header's walk back reads before
        // that place, and reads again nothing before them.
        const int Searched = (1024 * 1024) - 1;
        var text = new StringBuilder("%PDF-1.7\n1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n%");

        if (shape != "a comment")
        {
            text.Append('-', Searched - 2 - "trailer".Length - text.Length).Append("trailer");
        }

        text.Append('-', (1024 * 1024) + (64 * 1024) - text.Length).Append("\ntrailer\n<< /Root 1 0 R >>\nstartxref\n9\n%%EOF\n");
        using var source = new CountingSource(Encoding.Latin1.GetBytes(text.ToString()));

        using var document = PdfDocument.Open(source, options: null, ownsSource: false);

        document.Catalog.IsOfType(PdfName.Catalog).Should().BeTrue();
        source.Offsets.Should().ContainSingle(offset => offset > Searched - 1024 && offset <= Searched)
            .Which.Should().Be(Searched - resumed - 64);
    }

    [Fact]
    public void A_rebuild_goes_on_past_a_stream_whose_data_runs_past_a_piece_its_source_gives_short()
    {
        // The source gives at most 10,000 bytes a read from the file's start, as one over a store of blocks may: the scan's
        // first piece holds those, and ends inside stream 3's data. A piece shorter than asked holds the end of the data, but
        // the stream's direct length, which its endstream confirms past the piece, takes the scan on past it, to object 7.
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .Stream(3, string.Empty, new string('x', 20_000))
            .WithObject(7, "(seven)")
            .BuildClassic(rootNumber: 1, includeXRef: false);
        var source = new ShortCountSource(file, (offset, held) => offset == 0 ? Math.Min(held, 10_000) : held);

        using var document = PdfDocument.Open(source, options: null, ownsSource: true);

        document.GetObject(new PdfObjectId(7)).Should().BeOfType<PdfString>().Which.ToText().Should().Be("seven");
        source.ShortReads.Should().BePositive();
    }

    [Fact]
    public void A_rebuild_reads_a_file_cut_short_at_the_same_places_whether_the_cut_ends_an_object_s_value_or_falls_past_it()
    {
        // Another process has cut the file right after object 3's value, or a byte later: the scan's piece comes back short,
        // holding the end of the data, which the object's value ends or not. The scan stops there either way, and reads no
        // piece more.
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(3, "(three)")
            .WithObject(4, "(" + new string('x', 5000) + ")")
            .BuildClassic(rootNumber: 1, includeXRef: false);
        var end = PdfTemplate.OffsetOf(file, "(three)") + "(three)".Length;

        List<long> ReadsCutAt(long cut)
        {
            var reads = new List<long>();
            using var source = new ShortCountSource(file, (offset, held) =>
            {
                reads.Add(offset);
                return (int)Math.Clamp(cut - offset, 0, held);
            });
            using var document = PdfDocument.Open(source, options: null, ownsSource: false);
            document.Catalog.IsOfType(PdfName.Catalog).Should().BeTrue();
            return reads;
        }

        ReadsCutAt(end).Should().Equal(ReadsCutAt(end + 1));
    }

    private sealed class CountingSource : PdfFileSource
    {
        private readonly PdfFileSource _inner;

        public CountingSource(byte[] data) => _inner = FromMemory(data);

        public long BytesRead { get; private set; }

        public int LargestRead { get; private set; }

        public List<long> Offsets { get; } = [];

        public int Disposals { get; private set; }

        public override long Length => _inner.Length;

        public override int Read(long offset, Span<byte> buffer)
        {
            var read = _inner.Read(offset, buffer);
            Offsets.Add(offset);
            BytesRead += read;
            LargestRead = Math.Max(LargestRead, read);
            return read;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Disposals++;
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    [Theory]
    [InlineData(28, 12)]
    [InlineData(21, 12)]
    [InlineData(49, 12)]
    [InlineData(56, 12)]
    [InlineData(21, 2)]
    [InlineData(28, 2)]
    public void Reads_a_cross_reference_stream_whose_length_row_a_spelled_endstream_parts_the_predicted_row_of(int columns, int predictor)
    {
        // Stream 7's /Length names object 5, which only its own row places, and its rows spell an endstream right after that
        // row, which parts the predicted row it lies in: the data up to it decodes, as data whose end the file gives, to the
        // rows before that one, the parted one dropped — or, the first parted, to data the PNG predictor leaves as decoded,
        // its tag bytes in the rows; or, under the TIFF predictor, to that row still differenced —, and the row for the
        // /Length was not found, or found garbled. The section was charged with the rows it lacked and the index rebuilt
        // (#220).
        // The rows that spell the endstream, of reserved types, are read once the data is, and reported (#221).
        var file = ChainFiles.DeferredLengthRowPartedBySpelledEndStream(columns, predictor);

        using var document = PdfDocument.Open(file);

        document.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.XRefEntryReserved);
        document.WasRepaired.Should().BeFalse();
        document.GetObject(new PdfObjectId(3)).AsDictionary().Required().IsOfType(PdfName.Page).Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(6)]
    public void Reads_a_cross_reference_stream_whose_rows_a_length_it_places_runs_past_spell_an_endstream_before_the_first(int prefix)
    {
        // Stream 7's /Length names object 5, which only its own row places, and its data spells an endstream before its first
        // row: the data up to it holds no whole row, which the row for the /Length cannot lie in, nor a predicted row undone,
        // and the section lacks every row, the file's, which is reported (#210). Asked to look for that row again, the reader
        // would divide by the length of a row it took to be none.
        var file = ChainFiles.DeferredLengthEndStreamSpelledBeforeRows(prefix);

        using var document = PdfDocument.Open(file);

        document.WasRepaired.Should().BeTrue();
        document.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.XRefSectionUnreadable, PdfDiagnosticCodes.XRefRebuilt);
        document.GetObject(new PdfObjectId(3)).AsDictionary().Required().IsOfType(PdfName.Page).Should().BeTrue();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_decoding_guard_a_deferred_length_s_data_reaches_once_its_predicted_row_is_undone_is_reached_as_read_whole(bool rowBefore)
    {
        // Stream 7's /Length names object 5, which only its own row places, 1,820 bytes or more into the rows a RunLength step
        // decodes after a predicted Flate one; its rows spell an endstream that parts a predicted row, past object 5's row or
        // before it. MaxDecodedStreamLength, 1,100 bytes, stops the RunLength step read whole, the rows past it the guard's.
        // The data up to the endstream, its end the file's, decodes within the bound; decoded again, its predicted row undone
        // as far as it goes, to look for the /Length's row, it reached the bound, which the reader dropped with what that
        // decoding reported: the section was charged with the rows it lacked and the index rebuilt, where ThrowOnLimit threw.
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxDecodedStreamLength = 1100 } };

        using (var whole = PdfDocument.Open(ChainFiles.DeferredLengthRowPastRunLengthRuns(rowBefore, direct: true), options))
        {
            whole.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.LimitDecodedStream);
            whole.WasRepaired.Should().BeFalse();
        }

        var file = ChainFiles.DeferredLengthRowPastRunLengthRuns(rowBefore);

        // Unbounded, the row before the endstream is found in the data decoded again, which is taken again to the length; one
        // past it is not among the rows the data holds, which a length checked once the chain is read says (#220).
        using (var unbounded = PdfDocument.Open(file))
        {
            unbounded.WasRepaired.Should().Be(!rowBefore);
        }

        using var document = PdfDocument.Open(file, options);

        document.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.LimitDecodedStream);
        document.WasRepaired.Should().BeFalse();
        document.Reader.Structure.Sections[0].CutByLimit.Should().BeTrue();
        document.GetObject(new PdfObjectId(3)).AsDictionary().Required().IsOfType(PdfName.Page).Should().BeTrue();

        var throwing = () => PdfDocument.Open(file, options with { ThrowOnLimit = true });
        throwing.Should().Throw<PdfLimitExceededException>().Which.LimitName.Should().Be(nameof(PdfReaderLimits.MaxDecodedStreamLength));
    }

    [Fact]
    public void Rows_a_decoding_guard_stops_once_a_parted_predicted_row_is_undone_are_read_as_far_as_the_guard_lets_them()
    {
        // As above, but the page, object 155, has its row in the predicted row the spelled endstream parts, the 152nd, 1,057
        // bytes into the rows, within the 1,100 bytes MaxDecodedStreamLength lets the RunLength step decode, and object 5's
        // row past them. Read whole, the rows past the bound are the guard's, the page's row read. The data decoded again,
        // its predicted row undone as far as it goes, stops at the bound short of the /Length's row: where the rows end is
        // the guard's, the rows it holds read — not only those the decoding before, which dropped the parted row, held.
        var options = new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxDecodedStreamLength = 1100 } };

        using (var whole = PdfDocument.Open(ChainFiles.DeferredLengthPageRowInPartedRunLengthRow(direct: true), options))
        {
            whole.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.LimitDecodedStream);
            whole.GetObject(new PdfObjectId(155)).AsDictionary().Required().IsOfType(PdfName.Page).Should().BeTrue();
        }

        var file = ChainFiles.DeferredLengthPageRowInPartedRunLengthRow();

        // Unbounded, every row is read, those that spell the endstream, of reserved types, reported (#221).
        using (var unbounded = PdfDocument.Open(file))
        {
            unbounded.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.XRefEntryReserved);
        }

        using var document = PdfDocument.Open(file, options);

        document.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.LimitDecodedStream);
        document.WasRepaired.Should().BeFalse();
        document.GetObject(new PdfObjectId(155)).AsDictionary().Required().IsOfType(PdfName.Page).Should().BeTrue();
    }

    [Fact]
    public void A_deferred_length_no_row_numbers_is_not_looked_for_in_data_decoded_again()
    {
        // Stream 7's /Length names object 5, which no section places and no subsection of its own numbers, and its rows spell
        // an endstream that parts a predicted row. The data up to it, its end the file's, decodes within the 1,100 bytes
        // MaxDecodedStreamLength lets the RunLength step decode; decoded again, its predicted row undone, it would pass them,
        // but no row could give the length there: what is read is what an unbounded reading reads, no guard reached.
        var file = ChainFiles.DeferredLengthNoRowNumbersPastRunLengthRuns();
        string[] unboundedCodes;

        using (var unbounded = PdfDocument.Open(file))
        {
            unboundedCodes = [.. unbounded.Diagnostics.Select(entry => entry.Code)];
        }

        using var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxDecodedStreamLength = 1100 } });

        document.Diagnostics.Select(entry => entry.Code).Should().Equal(unboundedCodes).And.NotContain(PdfDiagnosticCodes.LimitDecodedStream);
    }
}
