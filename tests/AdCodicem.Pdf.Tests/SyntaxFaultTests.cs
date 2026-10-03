using System.Globalization;
using System.Text;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.Objects;
using AdCodicem.Pdf.Validation;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// The faults of the object syntax the reader recovers from, in documents: each is reported where it lies, the value kept
/// as the reader recovered it, rather than read in silence (#119, #172).
/// </summary>
public class SyntaxFaultTests
{
    private const string Header = "4 0 obj\n";

    [Theory]
    [InlineData("[1 2 3", "an array, which was never closed")]
    [InlineData("<< /A 1 /B 2", "a dictionary, which was never closed")]
    [InlineData("(abc", "a literal string, which takes the 4 bytes from where it opens to that end")]
    [InlineData("<414243", "a hexadecimal string, which takes the 7 bytes from where it opens to that end")]
    public void A_construct_the_file_ends_inside_is_reported_where_it_opens(string value, string what)
    {
        var file = PdfTemplate.Build(PdfTemplate.SoundEndingWith(Header + value));
        using var document = PdfDocument.Open(file);

        document.GetObject(new PdfObjectId(4)).Should().NotBeSameAs(PdfNull.Instance);

        var report = document.Diagnostics.Should().ContainSingle().Subject;
        report.Code.Should().Be(PdfDiagnosticCodes.SyntaxTruncatedObject);
        report.Position.Should().Be(PdfTemplate.OffsetOf(file, Header) + Header.Length);
        report.Message.Should().Be($"The file ended inside {what}.");
        new PdfValidator().Validate(document).Findings.Should().ContainSingle()
            .Which.RuleId.Should().Be(PdfValidationRuleIds.ObjectEndObjMissing);
    }

    [Theory]
    [InlineData("[1 2 3", "an array, which was never closed")]
    [InlineData("<< /A 1 /B 2", "a dictionary, which was never closed")]
    [InlineData("(abc", "a literal string, which takes the 5 bytes from where it opens to that end")]
    [InlineData("<414243", "a hexadecimal string, which takes the 8 bytes from where it opens to that end")]
    public void A_construct_the_end_of_an_object_stream_s_data_leaves_open_is_reported_in_its_member(string value, string what)
    {
        // The member is the stream's last, its data ending with the line feed after it: no endobj follows a member, and the
        // report is all that says it was cut short.
        var file = Packed(value, last: true);
        var opens = PdfTemplate.OffsetOf(file, value) - DataStart(file);
        using var document = PdfDocument.Open(file);

        document.GetObject(new PdfObjectId(4)).Should().NotBeSameAs(PdfNull.Instance);

        var report = document.Diagnostics.Should().ContainSingle().Subject;
        report.Code.Should().Be(PdfDiagnosticCodes.SyntaxTruncatedObject);
        report.Position.Should().Be(DataStart(file));
        report.Message.Should().Be(string.Create(
            CultureInfo.InvariantCulture,
            $"The object stream's decoded data ended inside {what}. It was met in object 4, at byte {opens} of object stream 7's decoded data."));
    }

    [Theory]
    [InlineData("[1 2 3")]
    [InlineData("(abc")]
    public void A_member_that_takes_the_members_after_it_to_the_end_of_the_data_leaves_them_readable(string value)
    {
        // The members after it are read from where the header places them; the one left open takes their bytes to the end of
        // the data, as the reader has no bound but the data's end for a member yet (#160).
        var file = Packed(value, last: false);
        using var document = PdfDocument.Open(file);

        var swallowing = document.GetObject(new PdfObjectId(4));

        document.GetObject(new PdfObjectId(5)).AsDictionary().Required().GetInteger(PdfName.Get("X")).Should().Be(5);
        document.GetObject(new PdfObjectId(6)).AsString().Required().ToText().Should().Be("six");
        (swallowing is PdfArray { Count: 5 } || swallowing.AsString()?.ToText() == "abc\n<< /X 5 >>\n(six)\n").Should().BeTrue();
        document.Diagnostics.Should().ContainSingle().Which.Code.Should().Be(PdfDiagnosticCodes.SyntaxTruncatedObject);
    }

    [Theory]
    [InlineData("/K", " 1 /Info 3 0 R >>\n", "a key whose value lies past the window")]
    [InlineData("/K [1 2", " 3] /Info 3 0 R >>\n", "an array that runs past the window")]
    [InlineData("/K (ab", "c) /Info 3 0 R >>\n", "a string that runs past the window")]
    public void A_rebuild_reports_no_fault_where_its_window_cuts_a_trailer(string cut, string rest, string shape)
    {
        // The rebuild reads each trailer through a window of 64 KB, whose edge is not the file's end (#49): what the edge
        // cuts short of a sound trailer is no fault of the file's, and no longer reported as one.
        const string Opening = "\n<< /Root 1 0 R /Size 4 /Pad (";
        var padding = (64 * 1024) - Opening.Length - ") ".Length - cut.Length;
        var file = Rebuilt("trailer" + Opening + new string('x', padding) + ") " + cut + rest);
        using var document = PdfDocument.Open(file);

        document.WasRepaired.Should().BeTrue();
        document.Diagnostics.Should().NotContain(entry => entry.Code.StartsWith("syntax.", StringComparison.Ordinal), shape);
        document.Catalog.Required().IsOfType(PdfName.Catalog).Should().BeTrue();
    }

    [Fact]
    public void A_rebuild_reports_a_trailer_the_file_ends_inside()
    {
        var file = Rebuilt("trailer\n<< /Size 4 /Root 1 0 R");
        using var document = PdfDocument.Open(file);

        document.WasRepaired.Should().BeTrue();
        var report = document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.SyntaxTruncatedObject).Subject;
        report.Position.Should().Be(PdfTemplate.OffsetOf(file, "<< /Size 4"));
        report.Message.Should().Be("The file ended inside a dictionary, which was never closed.");
        document.Catalog.Required().IsOfType(PdfName.Catalog).Should().BeTrue();
    }

    [Theory]
    [InlineData("[1 2 3", "an array, which was never closed")]
    [InlineData("<< /Title (t)", "a dictionary, which was never closed")]
    public void A_container_an_endobj_ends_takes_nothing_of_the_objects_after_it(string value, string what)
    {
        // The object's container is never closed, but its endobj is there: the object ends at it, and object 5 keeps its
        // entries, rather than lend them to object 4 or be read into it with the table and the trailer (#119).
        var file = PdfTemplate.Build(PdfTemplate.Sound
            .Replace("<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Catalog /Pages 2 0 R /Test 4 0 R /Other 5 0 R >>", StringComparison.Ordinal)
            .Replace("xref\n0 4\n", $"4 0 obj\n{value}\nendobj\n5 0 obj\n<< /Foo 7 /Author (a) >>\nendobj\nxref\n0 6\n", StringComparison.Ordinal)
            .Replace("{row:3}\n", "{row:3}\n{row:4}\n{row:5}\n", StringComparison.Ordinal)
            .Replace("/Size 4", "/Size 6", StringComparison.Ordinal));
        using var document = PdfDocument.Open(file);

        var read = document.GetObject(new PdfObjectId(4));

        (read is PdfArray { Count: 3 } || read is PdfDictionary { Count: 1 }).Should().BeTrue();
        document.GetObject(new PdfObjectId(5)).AsDictionary().Required().Should().HaveCount(2);
        var report = document.Diagnostics.Should().ContainSingle().Subject;
        report.Code.Should().Be(PdfDiagnosticCodes.SyntaxTruncatedObject);
        report.Position.Should().Be(PdfTemplate.OffsetOf(file, "4 0 obj\n") + "4 0 obj\n".Length);
        report.Message.Should().Be($"An endobj ended the object inside {what}.");
        new PdfValidator().Validate(document).Findings.Should().BeEmpty();
    }

    [Fact]
    public void A_hexadecimal_string_that_lost_its_closing_bracket_is_reported_for_what_it_takes_of_the_next_member()
    {
        // The string runs on to the first > it meets, the next member's: what it takes that is no digit is reported, and the
        // next member is still read from where the header places it.
        var file = Packed("<414243", last: false);
        using var document = PdfDocument.Open(file);

        document.GetObject(new PdfObjectId(4)).AsString().Required().Bytes.ToArray().Should().Equal((byte)0x41, 0x42, 0x43, 0x50);
        document.GetObject(new PdfObjectId(5)).AsDictionary().Required().GetInteger(PdfName.Get("X")).Should().Be(5);

        var report = document.Diagnostics.Should().ContainSingle().Subject;
        report.Code.Should().Be(PdfDiagnosticCodes.SyntaxHexStringInvalid);
        report.Message.Should().StartWith("A hexadecimal string holds 4 bytes that are neither hexadecimal digits nor white space");
    }

    [Theory]
    [InlineData("/ID [<41zz42> <4142>]", PdfDiagnosticCodes.SyntaxHexStringInvalid)]
    [InlineData("/Info#zz 3 0 R", PdfDiagnosticCodes.SyntaxNameEscapeInvalid)]
    public void A_trailer_holding_a_malformed_string_or_name_is_malformed(string entry, string code)
    {
        // A trailer read despite a fault of its syntax may hold another value than its writer meant: the trailer is malformed,
        // whichever fault it is.
        var file = PdfTemplate.SoundWith("<< /Size 4 /Root 1 0 R >>", $"<< /Size 4 /Root 1 0 R {entry} >>");
        using var document = PdfDocument.Open(file);

        var report = new PdfValidator().Validate(document);

        document.Diagnostics.Should().ContainSingle().Which.Code.Should().Be(code);
        report.Findings.Should().ContainSingle().Which.RuleId.Should().Be(PdfValidationRuleIds.FileTrailerMalformed);
    }

    [Theory]
    [InlineData("/Filter /FlateDecode /F#69lter /ASCIIHexDecode", "Hello")]
    [InlineData("/F#69lter /FlateDecode /Filter /ASCIIHexDecode", "Hello")]
    public void A_stream_whose_filter_is_given_twice_decodes_with_the_last_and_says_so(string filters, string decoded)
    {
        // The data is ASCII hexadecimal: the filter given last decodes it, as every reader but pypdf decodes it.
        var file = PdfTemplate.Build(PdfTemplate.SoundEndingWith($"4 0 obj\n<< {filters} /Length 11 >>\nstream\n48656C6C6F>\nendstream\nendobj\n"));
        using var document = PdfDocument.Open(file);

        var stream = document.GetObject(new PdfObjectId(4)).AsStream().Required();

        Encoding.ASCII.GetString(stream.Decode(document.Diagnostics).Span).Should().Be(decoded);
        document.Diagnostics.Should().ContainSingle().Which.Code.Should().Be(PdfDiagnosticCodes.SyntaxKeyRepeated);
    }

    [Fact]
    public void A_trailer_that_gives_a_key_twice_is_malformed_even_when_both_values_agree()
    {
        // A writer that gives a key twice may have meant either value; one that agrees with itself is still read despite a
        // fault of its syntax.
        var file = PdfTemplate.SoundWith("<< /Size 4 /Root 1 0 R >>", "<< /Size 4 /Root 1 0 R /Root 1 0 R >>");
        using var document = PdfDocument.Open(file);

        var report = new PdfValidator().Validate(document);

        document.Diagnostics.Should().ContainSingle().Which.Code.Should().Be(PdfDiagnosticCodes.SyntaxKeyRepeated);
        report.Findings.Should().ContainSingle().Which.RuleId.Should().Be(PdfValidationRuleIds.FileTrailerMalformed);
    }

    [Fact]
    public void A_fault_is_reported_once_however_often_the_cache_lets_its_object_go()
    {
        var builder = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(3, "<< /A 1 ) /B 2 >>");
        for (var number = 4; number < 80; number++)
        {
            builder.WithObject(number, "null");
        }

        using var document = PdfDocument.Open(builder.BuildClassic(rootNumber: 1), new PdfReaderOptions { ObjectCacheCapacity = 64 });

        for (var pass = 0; pass < 3; pass++)
        {
            for (var number = 3; number < 80; number++)
            {
                _ = document.GetObject(new PdfObjectId(number));
            }
        }

        document.Diagnostics.Should().ContainSingle().Which.Code.Should().Be(PdfDiagnosticCodes.SyntaxUnexpectedToken);
    }

    [Fact]
    public void A_fault_of_an_object_stream_s_dictionary_is_reported_once_though_the_validator_reads_it_again()
    {
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(4, "<< /X 4 >>")
            .BuildWithXRefStream(rootNumber: 1, compressedObjects: [4], objectStreamEntries: "/Junk )");
        using var document = PdfDocument.Open(file);

        _ = document.GetObject(new PdfObjectId(4));
        _ = new PdfValidator().Validate(document);

        document.Diagnostics.Should().ContainSingle(entry => entry.Code.StartsWith("syntax.", StringComparison.Ordinal))
            .Which.Code.Should().Be(PdfDiagnosticCodes.SyntaxUnexpectedToken);
    }

    [Fact]
    public void A_fault_of_an_object_read_before_and_after_a_rebuild_is_reported_once()
    {
        // /Root names object 4, which is no catalog; the catalog's row places it 1,500 bytes off, past where the reader looks
        // for it, so looking for it rebuilds the index, and object 4 is read again.
        var file = PdfTemplate.Build(PdfTemplate.Sound
            .Replace("xref\n0 4\n", "4 0 obj\n<< /Foo 1 ) >>\nendobj\n%" + new string('x', 2000) + "\nxref\n0 5\n", StringComparison.Ordinal)
            .Replace("{row:1}\n", "{row:1:0:1500}\n", StringComparison.Ordinal)
            .Replace("{row:3}\n", "{row:3}\n{row:4}\n", StringComparison.Ordinal)
            .Replace("<< /Size 4 /Root 1 0 R >>", "<< /Size 5 /Root 4 0 R >>", StringComparison.Ordinal));
        using var document = PdfDocument.Open(file);

        document.WasRepaired.Should().BeTrue();
        document.Diagnostics.Should().ContainSingle(entry => entry.Code.StartsWith("syntax.", StringComparison.Ordinal))
            .Which.Position.Should().Be(PdfTemplate.OffsetOf(file, ") >>"));
    }

    [Fact]
    public void A_fault_of_a_trailer_the_chain_and_the_rebuild_both_read_is_reported_once_and_makes_it_malformed()
    {
        // The catalog's row places it outside the file: the index is rebuilt, and the rebuild reads the trailer again.
        var file = PdfTemplate.Build(PdfTemplate.Sound
            .Replace("{row:1}\n", "{row:1:0:999999}\n", StringComparison.Ordinal)
            .Replace("<< /Size 4 /Root 1 0 R >>", "<< /Size 4 /Root 1 0 R ) >>", StringComparison.Ordinal));
        using var document = PdfDocument.Open(file);

        var report = new PdfValidator().Validate(document);

        document.WasRepaired.Should().BeTrue();
        document.Diagnostics.Should().ContainSingle(entry => entry.Code.StartsWith("syntax.", StringComparison.Ordinal))
            .Which.Position.Should().Be(PdfTemplate.OffsetOf(file, ") >>"));
        report.Findings.Select(finding => finding.RuleId).Should().Contain(PdfValidationRuleIds.FileTrailerMalformed);
    }

    /// <summary>
    /// The catalog and the page tree written directly, and objects 5, 6 and 4 — 4 holding <paramref name="value"/> — packed
    /// in object stream 7, 4 last or first.
    /// </summary>
    private static byte[] Packed(string value, bool last)
    {
        var builder = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>");

        if (!last)
        {
            builder.WithObject(4, value);
        }

        builder.WithObject(5, "<< /X 5 >>").WithObject(6, "(six)");

        if (last)
        {
            builder.WithObject(4, value);
        }

        return builder.BuildWithXRefStream(rootNumber: 1, compressedObjects: [4, 5, 6]);
    }

    /// <summary>Where the data of the object stream <see cref="Packed"/> writes starts in the file.</summary>
    private static long DataStart(byte[] file)
    {
        var text = Encoding.Latin1.GetString(file);
        var dictionary = text.IndexOf("/Type /ObjStm", StringComparison.Ordinal);
        return text.IndexOf("stream\n", dictionary, StringComparison.Ordinal) + "stream\n".Length;
    }

    /// <summary>The catalog, the page tree and a page, with no table and no <c>startxref</c>, then <paramref name="tail"/>.</summary>
    private static byte[] Rebuilt(string tail) => Encoding.Latin1.GetBytes(
        "%PDF-1.7\n" +
        "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n" +
        "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n" +
        "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << >> >>\nendobj\n" +
        tail);
}
