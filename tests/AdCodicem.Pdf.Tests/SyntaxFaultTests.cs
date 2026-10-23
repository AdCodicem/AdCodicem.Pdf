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
    [InlineData("[1 2 3", "an array, which was never closed")]
    [InlineData("(abc", "a literal string, which takes the 5 bytes from where it opens to that end")]
    [InlineData("<414243", "a hexadecimal string, which takes the 8 bytes from where it opens to that end")]
    public void A_member_left_open_ends_at_the_next_member_s_start(string value, string what)
    {
        // A member is read to the next one's start (#200): one left open takes nothing of the members after it, which are
        // read from where the header places them, and its report names what cut it short.
        var file = Packed(value, last: false);
        var opens = PdfTemplate.OffsetOf(file, value) - DataStart(file);
        using var document = PdfDocument.Open(file);

        var open = document.GetObject(new PdfObjectId(4));

        document.GetObject(new PdfObjectId(5)).AsDictionary().Required().GetInteger(PdfName.Get("X")).Should().Be(5);
        document.GetObject(new PdfObjectId(6)).AsString().Required().ToText().Should().Be("six");
        (value[0] switch
        {
            '[' => open is PdfArray { Count: 3 },
            '(' => open.AsString()?.ToText() == "abc\n",
            _ => open.AsString()?.Bytes.ToArray() is [0x41, 0x42, 0x43],
        }).Should().BeTrue();
        var report = document.Diagnostics.Should().ContainSingle().Subject;
        report.Code.Should().Be(PdfDiagnosticCodes.SyntaxTruncatedObject);
        report.Position.Should().Be(DataStart(file));
        report.Message.Should().Be(string.Create(
            CultureInfo.InvariantCulture,
            $"The next member of the object stream started inside {what}. It was met in object 4, at byte {opens} of object stream 7's decoded data."));
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

    [Theory]
    [InlineData("[1 2 3", "an array, which was never closed")]
    [InlineData("<< /A 1", "a dictionary, which was never closed")]
    [InlineData("<< /A", "a dictionary, which was never closed, before the value of its last key")]
    public void An_object_that_lost_its_closing_delimiter_and_its_endobj_ends_at_the_next_object_s_header(string value, string what)
    {
        // Object 4 lost both its closing ] or >> and its endobj: its container took in object 5's header and value up to
        // object 5's endobj, and the object read as ending with one, its missing endobj unreported (#230). The next object's
        // header ends it, the cut reported once, and the object lacks its endobj.
        var file = FourLostItsEnd(value);
        using var document = PdfDocument.Open(file);

        var read = document.GetObject(new PdfObjectId(4));

        (read is PdfArray { Count: 3 } || read is PdfDictionary { Count: <= 1 }).Should().BeTrue();
        (read is not PdfDictionary dictionary || dictionary.ContainsKey(PdfName.Get("A")) == !value.EndsWith("/A", StringComparison.Ordinal)).Should().BeTrue();
        document.GetObject(new PdfObjectId(5)).AsDictionary().Required().Should().HaveCount(2);
        var report = document.Diagnostics.Should().ContainSingle().Subject;
        report.Code.Should().Be(PdfDiagnosticCodes.SyntaxTruncatedObject);
        report.Position.Should().Be(PdfTemplate.OffsetOf(file, "4 0 obj\n") + "4 0 obj\n".Length);
        report.Message.Should().Be($"An object header ended the object inside {what}.");
        document.Reader.IsEndObjMissing(4, out _).Should().BeTrue();
        new PdfValidator().Validate(document).Findings.Should().ContainSingle()
            .Which.RuleId.Should().Be(PdfValidationRuleIds.ObjectEndObjMissing);
    }

    [Fact]
    public void The_next_object_s_header_ends_every_container_left_open_and_is_reported_at_the_innermost()
    {
        // The innermost dictionary took object 5's /Foo and /Author and closed at its >>: it ends at the header, which is
        // reported where it opens, and the containers around it end there too (#230).
        var file = FourLostItsEnd("<< /A [1 << /B 2");
        using var document = PdfDocument.Open(file);

        var outer = document.GetObject(new PdfObjectId(4)).AsDictionary().Required();
        var array = outer.GetArray(PdfName.Get("A")).Required();
        array.Should().HaveCount(2);
        array[1].Should().BeOfType<PdfDictionary>().Which.Should().HaveCount(1, "the inner dictionary takes nothing of object 5");
        var report = document.Diagnostics.Should().ContainSingle().Subject;
        report.Position.Should().Be(PdfTemplate.OffsetOf(file, "<< /B 2"));
        report.Message.Should().Be("An object header ended the object inside a dictionary, which was never closed; the 2 arrays or dictionaries around it were never closed either.");
        document.Reader.IsEndObjMissing(4, out _).Should().BeTrue();
    }

    [Theory]
    [InlineData("[1 (x 6 0 obj y) 2", 3, 0)]
    [InlineData("[1 6 0 R", 2, 0)]
    [InlineData("[1 6 65536 obj", 4, 1)]
    [InlineData("[1 2147483648 0 obj", 4, 1)]
    [InlineData("[1 6 %a comment\n0 obj", 4, 1)]
    [InlineData("[1 +6 0 obj", 4, 1)]
    public void Text_that_is_no_object_header_ends_no_container(string value, int count, int strays)
    {
        // A header in a string is no token; a reference is decided first; a generation or a number out of range, a comment
        // between the tokens, a sign before the number: none is a header, as the search for a stream's endstream takes
        // one, and the array goes on, a stray obj reported, to object 5's header (#230).
        var file = FourLostItsEnd(value);
        using var document = PdfDocument.Open(file);

        document.GetObject(new PdfObjectId(4)).AsArray().Required().Should().HaveCount(count);
        document.Diagnostics.Count(entry => entry.Code == PdfDiagnosticCodes.SyntaxUnexpectedToken).Should().Be(strays);
        document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.SyntaxTruncatedObject)
            .Which.Message.Should().Be("An object header ended the object inside an array, which was never closed.");
    }

    [Fact]
    public void A_header_inside_a_closed_container_ends_it_there_and_its_endobj_is_judged_missing()
    {
        // The rule's cost: text an object holds that is a header ends the object there, though its container closes and
        // its endobj follows; what follows the header is not read (#230). No document of the corpus holds one.
        var file = PdfTemplate.Build(PdfTemplate.Sound
            .Replace("<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Catalog /Pages 2 0 R /Test 4 0 R >>", StringComparison.Ordinal)
            .Replace("xref\n0 4\n", "4 0 obj\n[1 2 6 0 obj 3]\nendobj\nxref\n0 5\n", StringComparison.Ordinal)
            .Replace("{row:3}\n", "{row:3}\n{row:4}\n", StringComparison.Ordinal)
            .Replace("/Size 4", "/Size 5", StringComparison.Ordinal));
        using var document = PdfDocument.Open(file);

        document.GetObject(new PdfObjectId(4)).AsArray().Required().Should().HaveCount(2);
        document.Diagnostics.Should().ContainSingle().Which.Message.Should().Be("An object header ended the object inside an array, which was never closed.");
        document.Reader.IsEndObjMissing(4, out _).Should().BeTrue();
    }

    [Fact]
    public void An_object_stream_member_holding_a_header_reads_it_as_tokens()
    {
        // A member has no header, and is followed by none: text in it that reads as one is a stray obj, as it was (#230).
        var file = Packed("[1 2 5 0 obj 3]", last: true);
        using var document = PdfDocument.Open(file);

        document.GetObject(new PdfObjectId(4)).AsArray().Required().Should().HaveCount(6);
        document.Diagnostics.Should().ContainSingle().Which.Code.Should().Be(PdfDiagnosticCodes.SyntaxUnexpectedToken);
    }

    [Fact]
    public void Nesting_deeper_than_the_reader_follows_ends_at_the_next_object_s_header_too()
    {
        // Past the depth the parser follows, the containers are skipped to their end: an endobj, and now the next object's
        // header, ends them (#230).
        var file = FourLostItsEnd(new string('[', 200));
        using var document = PdfDocument.Open(file);

        document.GetObject(new PdfObjectId(4)).Should().BeOfType<PdfArray>();
        document.GetObject(new PdfObjectId(5)).AsDictionary().Required().Should().HaveCount(2);
        document.Diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.SyntaxDepthExceeded, PdfDiagnosticCodes.SyntaxTruncatedObject);
        document.Reader.IsEndObjMissing(4, out _).Should().BeTrue();
    }

    [Fact]
    public void A_trailer_that_lost_its_closing_delimiter_takes_nothing_of_the_update_after_it()
    {
        // An older trailer that lost its >>: it took the update's object 4, /Type and /Info, into the trailer the reader
        // merged, and /Info named an object the file lacks. The update's header ends it (#230).
        var file = TestPdfBuilder.AppendIncrementalUpdate(
            new TestPdfBuilder().WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>").WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>").BuildClassic(rootNumber: 1),
            rootNumber: 1,
            [(4, "<< /Type /Foo /Info 9 0 R >>")]);
        var text = Encoding.Latin1.GetString(file);
        var trailer = text.IndexOf("/Root 1 0 R >>", StringComparison.Ordinal) + "/Root 1 0 R".Length;
        Encoding.ASCII.GetBytes("   ").CopyTo(file, trailer);

        using var document = PdfDocument.Open(file);

        document.Trailer.ContainsKey(PdfName.Info).Should().BeFalse();
        document.Trailer.ContainsKey(PdfName.Type).Should().BeFalse();
        new PdfValidator().Validate(document).Contains(PdfValidationRuleIds.ObjectReferenceMissing).Should().BeFalse();

        // A trailer is no object: the report said the header ended "the object" at the trailer's <<.
        document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.SyntaxTruncatedObject)
            .Which.Message.Should().Be("An object header came inside a dictionary, which was never closed.");
    }

    [Theory]
    [InlineData(" /A 1\n\n6 0 obj")]
    [InlineData(" /A 1\n\n6 0 ")]
    [InlineData(" /A 1 /A\n\n6 0 obj")]
    public void A_header_the_guard_s_cut_leaves_undecided_is_the_guard_s(string tail)
    {
        // Object 4 lost its >> and its endobj, and the next object's header comes where MaxObjectLength cuts it: the header's
        // number and generation were kept as keys that are not names, or as the value of the key before them, where a larger
        // limit reads the header (#230).
        const int Limit = 128;
        var head = "4 0 obj\n<< /Pad (";
        var body = "<< /Pad (" + new string('x', Limit - head.Length - (")" + tail).Length) + ")" + tail + "\n<< /Foo 7 >>";
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"),
            [4] = file.Object(4, body),
        };
        file.Table(5, rows, "/Size 5 /Root 1 0 R");
        using var document = PdfDocument.Open(file.Bytes, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = Limit } });

        document.GetObject(new PdfObjectId(4));

        document.Diagnostics.Should().NotContain(entry => entry.Code == PdfDiagnosticCodes.SyntaxUnexpectedToken);
        document.Diagnostics.Should().Contain(entry => entry.Code == PdfDiagnosticCodes.LimitObject);
    }

    [Fact]
    public void A_header_the_guard_s_cut_leaves_undecided_gives_the_key_before_it_no_value()
    {
        // "6" after the second /A may be the value it is given, or the next object's header, which a larger limit reads, the
        // key then left out: the guard's cut decides neither, the key keeps the value the file gave first, and the repeat
        // is not said to keep a last one (#230).
        var document = CutAt(" /A 1 /A\n\n6 0 obj", 128);

        document.GetObject(new PdfObjectId(4)).AsDictionary().Required().GetInteger(PdfName.Get("A")).Should().Be(1);
        document.Diagnostics.Should().NotContain(entry => entry.Code == PdfDiagnosticCodes.SyntaxKeyRepeated);
        document.Dispose();
    }

    [Theory]
    [InlineData(" /A 1\n\n0 0 obj", 2)]
    [InlineData(" /A 1 /B 6 70000 obj", 1)]
    [InlineData(" /A 1\n\n+6 0 obj", 2)]
    [InlineData(" /A 1\n\n6 %c\n0 obj", 2)]
    public void Tokens_no_header_begins_with_are_reported_at_the_guard_s_cut_as_past_it(string tail, int keys)
    {
        // An object number 0, or a generation past 65,535, rules a header out whatever the cut hides: the keys that are not
        // names are the file's, reported as under a larger limit (#175, #230).
        using var guarded = CutAt(tail, 128);
        using var whole = CutAt(tail, 100_000);
        guarded.GetObject(new PdfObjectId(4));
        whole.GetObject(new PdfObjectId(4));

        var reported = guarded.Diagnostics.Where(entry => entry.Code == PdfDiagnosticCodes.SyntaxUnexpectedToken).Select(entry => entry.Position).ToArray();
        reported.Should().HaveCount(keys);
        whole.Diagnostics.Where(entry => entry.Code == PdfDiagnosticCodes.SyntaxUnexpectedToken).Select(entry => entry.Position).Should().Contain(reported);
    }

    [Theory]
    [InlineData(" /A 1 /B +6 0 obj")]
    [InlineData(" /A 1 /B 6 %c\n0 obj")]
    public void A_value_no_header_can_begin_with_keeps_its_place_at_the_guard_s_cut(string tail)
    {
        // A sign, or a comment between the tokens, rules a header out whatever the cut hides: "+6" is the value of /B, as a
        // larger limit reads it, not the start of a header the edge left undecided (#230).
        using var document = CutAt(tail, 128);

        document.GetObject(new PdfObjectId(4)).AsDictionary().Required().GetInteger(PdfName.Get("B")).Should().Be(6);
    }

    [Theory]
    [InlineData("<< /Pad ({0}) /A 1 /B 6 0 objx >>", "obj")]
    [InlineData("<< /Pad ({0}) /A 1 /B 6 0 objx >>", "ob")]
    [InlineData("<< /Pad ({0}) /A 1 /B 6 0 objx >>", "o")]
    [InlineData("<< /Pad ({0}) /A 1 /B [3 6 0 objx] >>", "obj")]
    [InlineData("<< /Pad ({0}) /A 1 /B << /D 6 0 objx >> >>", "ob")]
    public void An_object_a_guard_cuts_at_a_header_it_leaves_undecided_is_not_said_to_lack_its_endobj(string body, string kept)
    {
        // Object 4 is whole, its endobj after it; MaxObjectLength ends the window inside "objx", which may be the next object's
        // header: the guard's cut, which says nothing of what follows the object (#230).
        var text = string.Format(CultureInfo.InvariantCulture, body, new string('x', 40));
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R /Extra 4 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"),
            [4] = file.Object(4, text),
        };
        file.Table(5, rows, "/Size 5 /Root 1 0 R");
        var limit = "4 0 obj\n".Length + text.IndexOf("objx", StringComparison.Ordinal) + kept.Length;
        using var document = PdfDocument.Open(file.Bytes, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = limit } });

        document.GetObject(new PdfObjectId(4));

        document.Diagnostics.Should().Contain(entry => entry.Code == PdfDiagnosticCodes.LimitObject);
        document.Reader.IsEndObjMissing(4, out _).Should().BeFalse();
        new PdfValidator().Validate(document).Contains(PdfValidationRuleIds.ObjectEndObjMissing).Should().BeFalse();
    }

    [Theory]
    [InlineData("(abc)")]
    [InlineData("<< /A 1 >>")]
    [InlineData("[1 2 3]")]
    public void An_object_exactly_as_long_as_MaxObjectLength_reads_whole(string body)
    {
        // The window MaxObjectLength allows ends right after the object's endobj: the file says no regular character goes on
        // past it, and the object is whole, as under a larger limit (#174).
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R /Extra 4 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"),
            [4] = file.Object(4, body),
        };
        file.Table(5, rows, "/Size 5 /Root 1 0 R");
        var limit = ("4 0 obj\n" + body + "\nendobj").Length;
        using var document = PdfDocument.Open(file.Bytes, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = limit } });

        document.GetObject(new PdfObjectId(4));

        // The catalog, longer than the bound, is the guard's; object 4 is not.
        document.Diagnostics.Should().NotContain(entry => entry.Position == rows[4]);
        document.Reader.IsCutAtLimit(4).Should().BeFalse();
        document.Reader.IsEndObjMissing(4, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("Prev", 9)]
    [InlineData("XRefStm", 6)]
    [InlineData("W", 5)]
    [InlineData("W unclosed", 5)]
    [InlineData("Prev empty", 9)]
    public void An_object_the_chain_reads_exactly_as_long_as_MaxObjectLength_reads_whole(string key, int number)
    {
        // The chain reads object N — a /Prev, an /XRefStm, a cross-reference stream's /W — through a window MaxObjectLength
        // ends right after its endobj: the file says no regular character goes on past it, and the object is whole, as a
        // load reads it, whether the endobj follows its value, ends an array it left open, or is all it holds. It ended the
        // chain for the guard, which no load then reached (#174, #263).
        var file = key switch
        {
            "Prev" => ChainFiles.UpdateNamingItsPrevious("9 0 R", padding: 200),
            "XRefStm" => ChainFiles.HybridNamingItsStream("6 0 R", padding: 200),
            "W" => ChainFiles.UnderAnUpdate("W", placed: true, padding: 200),
            "W unclosed" => ChainFiles.UnderAnUpdate(
                "/Type /XRef /Size 8 /W 5 0 R /Length {length}", "[1 4 2" + new string(' ', 200), compressed: false, predicted: false, placed: true),
            _ => Blanked(ChainFiles.UpdateNamingItsPrevious("9 0 R", padding: 200)),
        };
        var text = Encoding.Latin1.GetString(file);
        var header = text.IndexOf(string.Create(CultureInfo.InvariantCulture, $"\n{number} 0 obj\n"), StringComparison.Ordinal) + 1;
        var limit = text.IndexOf("endobj", header, StringComparison.Ordinal) + "endobj".Length - header;
        using var whole = PdfDocument.Open(file);

        using (var document = PdfDocument.Open(file, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = limit } }))
        {
            document.GetObject(new PdfObjectId(3)).AsDictionary().Should().NotBeNull();
            document.Reader.Structure.Sections.Should().OnlyContain(section => !section.CutByLimit && section.ChainHiddenByGuard == null);
            document.Diagnostics.Select(entry => entry.Code).Should().Equal(whole.Diagnostics.Select(entry => entry.Code));
        }

        using (var cut = PdfDocument.Open(file, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = limit - 1 } }))
        {
            cut.Diagnostics.Select(entry => entry.Code).Should().Contain(PdfDiagnosticCodes.LimitObject);
        }

        var throwing = () => PdfDocument.Open(
            file, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = limit }, ThrowOnLimit = true }).Dispose();
        throwing.Should().NotThrow();

        // Object 9's value, the first table's offset, given to spaces: the object holds nothing but its endobj.
        static byte[] Blanked(byte[] file)
        {
            var text = Encoding.Latin1.GetString(file);
            var value = text.IndexOf("\nendobj", text.IndexOf("\n9 0 obj\n", StringComparison.Ordinal), StringComparison.Ordinal);
            var start = text.LastIndexOf(' ', value) + 1;
            return Encoding.Latin1.GetBytes(text[..start] + new string(' ', value - start) + text[value..]);
        }
    }

    [Theory]
    [InlineData("[1 2 3")]
    [InlineData("<< /A 1 /B 2")]
    [InlineData("<< /A 1 /B")]
    [InlineData("<< /A [1 2")]
    [InlineData("")]
    public void An_object_whose_endobj_ends_its_containers_at_MaxObjectLength_reads_as_under_a_larger_bound(string body)
    {
        // Object 4 left an array or a dictionary open, or holds nothing, and MaxObjectLength's window ends right after its
        // endobj: the file says no regular character follows that endobj, which ends the object as it does under a larger
        // bound — the containers it left open reported as the file's —, where it was taken for the guard's cut (#174). Its
        // value comes after 100 spaces, so that no other object is as long.
        var padded = new string(' ', 100) + body;
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R /Extra 4 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"),
            [4] = file.Object(4, padded),
        };
        file.Table(5, rows, "/Size 5 /Root 1 0 R");
        var limit = ("4 0 obj\n" + padded + "\nendobj").Length;

        using var exact = PdfDocument.Open(file.Bytes, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = limit } });
        using var larger = PdfDocument.Open(file.Bytes, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = limit + 1 } });

        exact.GetObject(new PdfObjectId(4)).ToString().Should().Be(larger.GetObject(new PdfObjectId(4)).ToString());
        exact.Reader.IsCutAtLimit(4).Should().BeFalse();
        Of4(exact).Should().Equal(Of4(larger));
        exact.Reader.IsEndObjMissing(4, out _).Should().Be(larger.Reader.IsEndObjMissing(4, out _));

        var throwing = () => PdfDocument.Open(
            file.Bytes, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = limit }, ThrowOnLimit = true }).GetObject(new PdfObjectId(4));
        throwing.Should().NotThrow();

        // What is reported of object 4, which starts at rows[4].
        IEnumerable<(string, long)> Of4(PdfDocument document) =>
            document.Diagnostics.Where(entry => entry.Position >= rows[4]).Select(entry => (entry.Code, entry.Position));
    }

    /// <summary>
    /// Opens a document whose object 4 lost its <c>&gt;&gt;</c> and its endobj, its padding setting <paramref name="tail"/>'s
    /// end where a <c>MaxObjectLength</c> of 128 cuts it, under <paramref name="limit"/>.
    /// </summary>
    private static PdfDocument CutAt(string tail, int limit)
    {
        const int Edge = 128;
        var head = "4 0 obj\n<< /Pad (";
        var body = "<< /Pad (" + new string('x', Edge - head.Length - (")" + tail).Length) + ")" + tail + "\n<< /Foo 7 >>";
        var file = new HandWrittenFile();
        var rows = new Dictionary<int, long>
        {
            [1] = file.Object(1, "<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = file.Object(2, "<< /Type /Pages /Kids [] /Count 0 >>"),
            [4] = file.Object(4, body),
        };
        file.Table(5, rows, "/Size 5 /Root 1 0 R");
        return PdfDocument.Open(file.Bytes, new PdfReaderOptions { Limits = PdfReaderLimits.Default with { MaxObjectLength = limit } });
    }

    /// <summary>
    /// A sound document whose catalog names objects 4 and 5, object 4 holding <paramref name="value"/> and no endobj, then
    /// object 5 holding <paramref name="five"/> and its endobj.
    /// </summary>
    private static byte[] FourLostItsEnd(string value, string five = "<< /Foo 7 /Author (a) >>") =>
        PdfTemplate.Build(PdfTemplate.Sound
            .Replace("<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Catalog /Pages 2 0 R /Test 4 0 R /Other 5 0 R >>", StringComparison.Ordinal)
            .Replace("xref\n0 4\n", $"4 0 obj\n{value}\n5 0 obj\n{five}\nendobj\nxref\n0 6\n", StringComparison.Ordinal)
            .Replace("{row:3}\n", "{row:3}\n{row:4}\n{row:5}\n", StringComparison.Ordinal)
            .Replace("/Size 4", "/Size 6", StringComparison.Ordinal));

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

    [Theory]
    [InlineData(1000, 600, 0)]
    [InlineData(400, 400, 800)]
    public void Many_faults_of_an_object_read_again_are_each_kept_once_and_read_the_same(int capacity, int kept, int suppressed)
    {
        // Six hundred repeats of one key, read, let go by the cache and read again: the second reading's reports are the
        // first's, word for word, and none is kept twice. Once the diagnostics are full, each reading's reports are counted
        // and dropped: 200 the first time, all 600 the second.
        var builder = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(3, "<< /A 0 " + string.Concat(Enumerable.Repeat("/A 1 ", 600)) + ">>");
        for (var number = 4; number < 80; number++)
        {
            builder.WithObject(number, "null");
        }

        using var document = PdfDocument.Open(
            builder.BuildClassic(rootNumber: 1), new PdfReaderOptions { ObjectCacheCapacity = 64, DiagnosticCapacity = capacity });

        for (var pass = 0; pass < 2; pass++)
        {
            for (var number = 3; number < 80; number++)
            {
                _ = document.GetObject(new PdfObjectId(number));
            }
        }

        document.Diagnostics.Should().HaveCount(kept).And.OnlyHaveUniqueItems()
            .And.OnlyContain(entry => entry.Message == "The dictionary gives the key /A more than once; the last value given is kept.");
        document.Diagnostics.SuppressedCount.Should().Be(suppressed);
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_trailer_a_source_cut_short_ends_inside_is_reported_as_the_end_of_the_data(bool chained)
    {
        // A source whose reads stop before the length it gives, as a file another process truncates: a window shorter than
        // asked holds the end of the data, whether the chain's table or the rebuild's scan reads it, as it does for objects.
        var tail = "trailer\n<< /Size 4 /Root 1 0 R";
        var file = chained
            ? PdfTemplate.Build(PdfTemplate.Sound.Replace("xref\n0 4\n", "startxref\n{xref:1}\n%%EOF\nxref\n0 4\n", StringComparison.Ordinal)
                .Replace("trailer\n<< /Size 4 /Root 1 0 R >>\nstartxref\n{xref:1}\n%%EOF\n", tail + " >>\n" + new string(' ', 200), StringComparison.Ordinal))
            : Rebuilt(tail + " >>\n" + new string(' ', 200));
        using var source = new CutShortSource(file);
        source.CutAt((int)PdfTemplate.OffsetOf(file, tail) + tail.Length);
        using var document = PdfDocument.Open(source, options: null, ownsSource: false);

        document.WasRepaired.Should().Be(!chained);
        document.Diagnostics.Should().ContainSingle(entry => entry.Code == PdfDiagnosticCodes.SyntaxTruncatedObject)
            .Which.Position.Should().Be(PdfTemplate.OffsetOf(file, "<< /Size 4 /Root"));
    }

    [Theory]
    [InlineData("[1 2 3", PdfDiagnosticCodes.SyntaxTruncatedObject, "An endobj ended the object inside an array, which was never closed.")]
    [InlineData("<< /A 1", PdfDiagnosticCodes.SyntaxTruncatedObject, "An endobj ended the object inside a dictionary, which was never closed.")]
    [InlineData("<< /A", PdfDiagnosticCodes.SyntaxTruncatedObject, "An endobj ended the object inside a dictionary, which was never closed, before the value of its last key.")]
    [InlineData("[1 2 >>", PdfDiagnosticCodes.SyntaxUnexpectedToken, "An array was closed by a dictionary end.")]
    [InlineData("<< /A >>", PdfDiagnosticCodes.SyntaxUnexpectedToken, "A dictionary key had no value.")]
    public void A_fault_of_an_object_read_through_a_window_the_file_goes_on_past_is_reported(string value, string code, string message)
    {
        // Ten kilobytes follow the object, so the window it is read through does not end the data: what the parse meets
        // inside the window, whole, is reported all the same.
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(3, value)
            .WithObject(4, "(" + new string('x', 10_000) + ")")
            .BuildClassic(rootNumber: 1);
        using var document = PdfDocument.Open(file);

        _ = document.GetObject(new PdfObjectId(3));

        var report = document.Diagnostics.Should().ContainSingle().Subject;
        report.Code.Should().Be(code);
        report.Message.Should().Be(message);
    }

    [Fact]
    public void A_fault_of_a_member_is_reported_once_however_often_the_member_is_parsed_again()
    {
        // The cache lets the member go and its object stream serves it again: the fault, named by its member and byte, is
        // reported once.
        var builder = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(3, "<< /A 1 /A 2 >>");
        for (var number = 4; number < 80; number++)
        {
            builder.WithObject(number, "null");
        }

        var file = builder.BuildWithXRefStream(rootNumber: 1, compressedObjects: [.. Enumerable.Range(3, 77)]);
        using var document = PdfDocument.Open(file, new PdfReaderOptions { ObjectCacheCapacity = 64 });

        for (var pass = 0; pass < 3; pass++)
        {
            for (var number = 3; number < 80; number++)
            {
                _ = document.GetObject(new PdfObjectId(number));
            }
        }

        document.Diagnostics.Should().ContainSingle().Which.Message.Should().StartWith(
            "The dictionary gives the key /A more than once; the last value given is kept. It was met in object 3, at byte ");
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
