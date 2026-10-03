using System.Text;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.IO;
using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.Tests;

public class ParserTests
{
    [Fact]
    public void Parses_the_primitive_values()
    {
        Parse("true").Should().BeSameAs(PdfBoolean.True);
        Parse("false").Should().BeSameAs(PdfBoolean.False);
        Parse("null").Should().BeSameAs(PdfNull.Instance);
        Parse("42").Should().BeOfType<PdfInteger>().Subject.Value.Should().Be(42);
        Parse("3.5").Should().BeOfType<PdfReal>().Subject.Value.Should().Be(3.5);
        Parse("/Type").Should().BeOfType<PdfName>().Subject.Value.Should().Be("Type");
        Parse("(text)").Should().BeOfType<PdfString>().Subject.ToText().Should().Be("text");
    }

    [Fact]
    public void Keeps_whether_a_string_was_written_in_hexadecimal()
    {
        var hexadecimal = Parse("<74657874>").Should().BeOfType<PdfString>().Subject;
        var literal = Parse("(text)").Should().BeOfType<PdfString>().Subject;

        hexadecimal.IsHexadecimal.Should().BeTrue();
        literal.IsHexadecimal.Should().BeFalse();
        hexadecimal.ToText().Should().Be(literal.ToText());
    }

    [Fact]
    public void Parses_an_array()
    {
        var array = Parse("[1 2 /Three (four)]").Should().BeOfType<PdfArray>().Subject;

        array.Count.Should().Be(4);
        array[0].AsInteger().Should().Be(1);
        array[2].AsName()!.Value.Should().Be("Three");
    }

    [Fact]
    public void Parses_a_dictionary()
    {
        var dictionary = Parse("<< /Type /Page /Count 3 /Nested << /A true >> >>").Should().BeOfType<PdfDictionary>().Subject;

        dictionary.IsOfType(PdfName.Page).Should().BeTrue();
        dictionary.GetInteger(PdfName.Count).Should().Be(3);
        dictionary.GetDictionary(PdfName.Get("Nested")).Required();
    }

    [Fact]
    public void Distinguishes_a_reference_from_two_integers()
    {
        var reference = Parse("12 0 R").Should().BeOfType<PdfReference>().Subject;
        reference.Id.Should().Be(new PdfObjectId(12));

        var array = Parse("[1 2]").Should().BeOfType<PdfArray>().Subject;
        array.Count.Should().Be(2);
    }

    [Theory]
    [InlineData("[0 0 R]", 0)]
    [InlineData("[0 65535 R]", 65535)]
    public void Reads_a_reference_to_object_0_as_one_value(string text, int generation)
    {
        var diagnostics = new PdfDiagnostics();
        var parser = new PdfObjectParser(Encoding.ASCII.GetBytes(text), diagnostics: diagnostics);

        var array = parser.ParseObject().Should().BeOfType<PdfArray>().Subject;

        array.Count.Should().Be(1, "object 0 heads the free list, and a reference to it is one value (ISO 32000-1, 7.5.4)");
        array[0].Should().BeOfType<PdfReference>().Subject.Id.Should().Be(new PdfObjectId(0, generation));
        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void Keeps_a_dictionary_s_pairs_around_a_reference_to_object_0()
    {
        var diagnostics = new PdfDiagnostics();
        var parser = new PdfObjectParser(Encoding.ASCII.GetBytes("<< /Parent 0 0 R /X 1 >>"), diagnostics: diagnostics);

        var dictionary = parser.ParseObject().Should().BeOfType<PdfDictionary>().Subject;

        dictionary.Count.Should().Be(2);
        dictionary.GetRaw(PdfName.Parent).Should().BeOfType<PdfReference>().Subject.Id.Should().Be(new PdfObjectId(0));
        dictionary.GetInteger(PdfName.Get("X")).Should().Be(1);
        diagnostics.Should().BeEmpty();
    }

    [Theory]
    [InlineData("[3000000000 0 R]", "an object number past int.MaxValue")]
    [InlineData("[4294967301 0 R]", "an object number that would wrap to 5 if narrowed to an int")]
    [InlineData("[-1 0 R]", "a negative object number")]
    [InlineData("[-4294967291 0 R]", "a negative object number that would wrap to 5 if narrowed to an int")]
    [InlineData("[5 -1 R]", "a negative generation")]
    [InlineData("[5 65536 R]", "a generation past 65535")]
    [InlineData("[92233720368547758085 0 R]", "an object number past a long, which once wrapped to 5")]
    [InlineData("[5 92233720368547758080 R]", "a generation past a long, which once wrapped to 0")]
    public void Makes_no_reference_of_numbers_an_object_identifier_cannot_hold(string text, string because)
    {
        var target = new PdfDictionary();
        var parser = new PdfObjectParser(Encoding.ASCII.GetBytes(text), source: ObjectSourceReturning(new PdfObjectId(5), target));

        var array = parser.ParseObject().Should().BeOfType<PdfArray>().Subject;

        array.Should().NotContain(value => value is PdfReference, because);
        array.Should().NotContain(value => ReferenceEquals(value.Resolve(), target), "nothing in it may lead to object 5");
    }

    [Fact]
    public void Backtracks_when_two_integers_are_not_followed_by_R()
    {
        var array = Parse("[1 2 3]").Should().BeOfType<PdfArray>().Subject;

        array.Count.Should().Be(3);
        array[2].AsInteger().Should().Be(3);
    }

    [Fact]
    public void Reads_an_indirect_object_definition()
    {
        var bytes = Encoding.ASCII.GetBytes("7 0 obj\n<< /Length 0 >>\nendobj\n");
        var parser = new PdfObjectParser(bytes);

        parser.TryReadIndirectObject(out var id, out var value).Should().BeTrue();

        id.Should().Be(new PdfObjectId(7));
        value.Should().BeOfType<PdfDictionary>();
    }

    [Theory]
    [InlineData("0 0 obj\nnull\nendobj\n")]
    [InlineData("3000000000 0 obj\nnull\nendobj\n")]
    [InlineData("7 -1 obj\nnull\nendobj\n")]
    [InlineData("7 65536 obj\nnull\nendobj\n")]
    [InlineData("7 0.5 obj\nnull\nendobj\n")]
    [InlineData("92233720368547758082 0 obj\nnull\nendobj\n")]
    [InlineData("4 92233720368547758080 obj\nnull\nendobj\n")]
    public void Refuses_an_object_header_whose_number_or_generation_is_out_of_range(string text)
    {
        var parser = new PdfObjectParser(Encoding.ASCII.GetBytes(text));

        parser.TryReadIndirectObject(out _, out _).Should().BeFalse();
    }

    [Fact]
    public void Reads_a_stream_whose_declared_length_is_correct()
    {
        var stream = ParseStream("<< /Length 11 >>\nstream\nHello World\nendstream");

        Encoding.ASCII.GetString(stream.GetRawBytes().Span).Should().Be("Hello World");
    }

    [Fact]
    public void Recovers_a_stream_whose_declared_length_is_wrong()
    {
        var diagnostics = new PdfDiagnostics();
        var stream = ParseStream("<< /Length 3 >>\nstream\nHello World\nendstream", diagnostics);

        Encoding.ASCII.GetString(stream.GetRawBytes().Span).Should().Be("Hello World");
        diagnostics.Contains(PdfDiagnosticCodes.StreamLengthInvalid).Should().BeTrue();
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(-1, false)]
    [InlineData(11, false)]
    public void Looks_for_endstream_only_inside_the_bytes_it_is_given(long position, bool expected)
    {
        PdfObjectParser.IsEndStreamAt("\nendstream"u8, position).Should().Be(expected);
    }

    [Fact]
    public void Recovers_a_stream_that_never_ends()
    {
        var diagnostics = new PdfDiagnostics();
        var stream = ParseStream("<< /Length 99 >>\nstream\ntruncated", diagnostics);

        Encoding.ASCII.GetString(stream.GetRawBytes().Span).Should().Be("truncated");
        diagnostics.Contains(PdfDiagnosticCodes.StreamTruncated).Should().BeTrue();
    }

    [Fact]
    public void Resolves_a_length_given_as_an_indirect_reference()
    {
        var source = ObjectSourceReturning(new PdfObjectId(9), PdfInteger.Create(5));
        var bytes = Encoding.ASCII.GetBytes("<< /Length 9 0 R >>\nstream\nABCDE\nendstream");
        var parser = new PdfObjectParser(bytes, source: source);

        var stream = parser.ParseObject().Should().BeOfType<PdfStream>().Subject;

        Encoding.ASCII.GetString(stream.GetRawBytes().Span).Should().Be("ABCDE");
    }

    [Fact]
    public void Asks_the_object_source_for_an_indirect_length_exactly_once()
    {
        var source = ObjectSourceReturning(new PdfObjectId(9), PdfInteger.Create(5));
        var bytes = Encoding.ASCII.GetBytes("<< /Length 9 0 R >>\nstream\nABCDE\nendstream");
        var parser = new PdfObjectParser(bytes, source: source);

        parser.ParseObject();

        // Resolving a length reaches back into the reader, which is re-entrant and cached. Asking twice
        // would mean the parser re-resolves what it already knows, on the hottest path there is.
        source.Received(1).GetObject(new PdfObjectId(9));
    }

    [Fact]
    public void Reports_a_truncated_object_instead_of_throwing()
    {
        var diagnostics = new PdfDiagnostics();
        var bytes = Encoding.ASCII.GetBytes("<< /Type /Page /Kids [1 0 R");
        var parser = new PdfObjectParser(bytes, diagnostics: diagnostics);

        parser.ParseObject().Should().BeOfType<PdfDictionary>();

        parser.IsTruncated.Should().BeTrue();
    }

    [Fact]
    public void Refuses_to_follow_nesting_deeper_than_its_limit()
    {
        var diagnostics = new PdfDiagnostics();
        var bytes = Encoding.ASCII.GetBytes(new string('[', 5000) + new string(']', 5000));
        var parser = new PdfObjectParser(bytes, diagnostics: diagnostics);

        parser.ParseObject();

        diagnostics.Contains(PdfDiagnosticCodes.SyntaxDepthExceeded).Should().BeTrue();
    }

    [Fact]
    public void Refuses_to_follow_dictionaries_nested_deeper_than_its_limit()
    {
        var diagnostics = new PdfDiagnostics();
        var bytes = Encoding.ASCII.GetBytes(
            string.Concat(Enumerable.Repeat("<< /A ", 5000)) + "0" + string.Concat(Enumerable.Repeat(" >>", 5000)));
        var parser = new PdfObjectParser(bytes, diagnostics: diagnostics);

        parser.ParseObject().Should().BeOfType<PdfDictionary>();

        diagnostics.Contains(PdfDiagnosticCodes.SyntaxDepthExceeded).Should().BeTrue();

        // The levels past the limit are skipped whole, their delimiters balanced: the parse ends with the input.
        parser.Position.Should().Be(bytes.Length);
    }

    [Fact]
    public void Drops_entries_whose_value_is_null_as_the_specification_requires()
    {
        var dictionary = Parse("<< /A 1 /B null >>").Should().BeOfType<PdfDictionary>().Subject;

        dictionary.Count.Should().Be(1);
        dictionary.ContainsKey(PdfName.Get("B")).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("-")]
    public void Reads_a_number_past_the_largest_real_as_null_and_reports_it(string sign)
    {
        var number = sign + "1" + new string('0', 309);
        var diagnostics = new PdfDiagnostics();
        var parser = new PdfObjectParser(Encoding.ASCII.GetBytes("[1 " + number + " 2]"), diagnostics: diagnostics);

        var array = parser.ParseObject().Should().BeOfType<PdfArray>().Subject;

        array.Count.Should().Be(3);
        array[1].Should().BeSameAs(PdfNull.Instance);
        var report = diagnostics.Should().ContainSingle().Subject;
        report.Code.Should().Be(PdfDiagnosticCodes.SyntaxNumberOutOfRange);
        report.Severity.Should().Be(PdfDiagnosticSeverity.Warning);
        report.Position.Should().Be(3);
        report.Message.Should().Be(
            $"The number {number[..127]} (the first 127 of {number.Length} bytes) is beyond what a real can hold; it was read as null.");
    }

    [Fact]
    public void Reads_a_number_past_the_largest_real_as_null_with_no_diagnostics_to_report_to()
    {
        var parser = new PdfObjectParser(Encoding.ASCII.GetBytes("1" + new string('0', 400)));

        parser.ParseObject().Should().BeSameAs(PdfNull.Instance);
    }

    [Fact]
    public void Counts_a_number_past_the_largest_real_once_the_diagnostics_are_full()
    {
        var diagnostics = new PdfDiagnostics { Capacity = 0 };
        var parser = new PdfObjectParser(Encoding.ASCII.GetBytes("1" + new string('0', 400)), diagnostics: diagnostics);

        parser.ParseObject().Should().BeSameAs(PdfNull.Instance);

        diagnostics.Should().BeEmpty();
        diagnostics.SuppressedCount.Should().Be(1);
    }

    [Fact]
    public void Keeps_no_entry_for_a_number_past_the_largest_real_as_for_any_null()
    {
        var diagnostics = new PdfDiagnostics();
        var parser = new PdfObjectParser(Encoding.ASCII.GetBytes("<< /A 1" + new string('0', 400) + " /B 2 >>"), diagnostics: diagnostics);

        var dictionary = parser.ParseObject().Should().BeOfType<PdfDictionary>().Subject;

        dictionary.ContainsKey(PdfName.Get("A")).Should().BeFalse();
        dictionary.GetInteger(PdfName.Get("B")).Should().Be(2);
        diagnostics.Should().ContainSingle().Which.Code.Should().Be(PdfDiagnosticCodes.SyntaxNumberOutOfRange);
    }

    [Fact]
    public void Reads_the_largest_real_a_double_holds()
    {
        var parser = new PdfObjectParser(Encoding.ASCII.GetBytes("179769313486231570" + new string('0', 291) + ".0"));

        parser.ParseObject().Should().BeOfType<PdfReal>().Which.Value.Should().Be(double.MaxValue);
    }

    [Fact]
    public void Reports_no_number_its_double_holds_however_it_rounds()
    {
        // An integer past a long reads as the real nearest to it; a decimal below the smallest double rounds to 0, and
        // one just past double.MaxValue, short of the halfway point to the next power, rounds back to it. None is a fault.
        var diagnostics = new PdfDiagnostics();
        var parser = new PdfObjectParser(
            Encoding.ASCII.GetBytes("[92233720368547758085 0." + new string('0', 400) + "5 179769313486231580" + new string('0', 291) + "]"),
            diagnostics: diagnostics);

        var array = parser.ParseObject().Should().BeOfType<PdfArray>().Subject;

        array.Select(value => value.Should().BeOfType<PdfReal>().Subject.Value).Should().Equal(92233720368547758085d, 0d, double.MaxValue);
        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void Terminates_on_a_dictionary_that_is_never_closed()
    {
        var bytes = Encoding.ASCII.GetBytes("<< /A /B /C");
        var parser = new PdfObjectParser(bytes);

        parser.ParseObject().Should().BeOfType<PdfDictionary>().Subject.Count.Should().Be(1);
        parser.IsTruncated.Should().BeTrue();
    }

    [Fact]
    public void Treats_an_unresolvable_reference_as_null()
    {
        var reference = Parse("3 0 R").Should().BeOfType<PdfReference>().Subject;

        reference.Resolve().Should().BeSameAs(PdfNull.Instance);
    }

    [Theory]
    [InlineData("[1 2 3", 0, "The file ended inside an array, which was never closed.")]
    [InlineData("<< /A 1 /B 2", 0, "The file ended inside a dictionary, which was never closed.")]
    [InlineData("<< /A", 0, "The file ended inside a dictionary, which was never closed, before the value of its last key.")]
    [InlineData("(abc", 0, "The file ended inside a literal string, which takes the 4 bytes from where it opens to that end.")]
    [InlineData("(abc\\", 0, "The file ended inside a literal string, which takes the 5 bytes from where it opens to that end.")]
    [InlineData("(a(b)c", 0, "The file ended inside a literal string, which takes the 6 bytes from where it opens to that end.")]
    [InlineData("<414243", 0, "The file ended inside a hexadecimal string, which takes the 7 bytes from where it opens to that end.")]
    [InlineData("<", 0, "The file ended inside a hexadecimal string, which takes the 1 bytes from where it opens to that end.")]
    [InlineData("[ << /A 1", 2, "The file ended inside a dictionary, which was never closed; the array or dictionary around it was never closed either.")]
    [InlineData("<< /A [1 2 (abc", 11, "The file ended inside a literal string, which takes the 4 bytes from where it opens to that end; the 2 arrays or dictionaries around it were never closed either.")]
    public void Reports_once_where_it_opens_the_innermost_construct_the_end_of_the_data_leaves_open(string text, int opens, string message)
    {
        // #119 and #172: whatever the end of the data cuts short is kept as read and reported once, for the innermost
        // construct, where it opens; the containers around it are counted in the message, not reported again.
        var diagnostics = new PdfDiagnostics();
        var parser = new PdfObjectParser(Encoding.ASCII.GetBytes(text), 100, diagnostics: diagnostics);

        _ = parser.ParseObject();

        parser.IsTruncated.Should().BeTrue();
        var report = diagnostics.Should().ContainSingle().Subject;
        report.Code.Should().Be(PdfDiagnosticCodes.SyntaxTruncatedObject);
        report.Severity.Should().Be(PdfDiagnosticSeverity.Warning);
        report.Position.Should().Be(100 + opens);
        report.Message.Should().Be(message);
    }

    [Theory]
    [InlineData("[1 2 3")]
    [InlineData("<< /A 1 /B 2")]
    [InlineData("<< /A")]
    [InlineData("(abc")]
    [InlineData("<414243")]
    [InlineData("<< /A [1 2 (abc")]
    [InlineData("")]
    public void Reports_nothing_a_window_s_edge_cuts_short(string text)
    {
        // A buffer that ends at a window's edge rather than where the data does says nothing of what lies past it: the
        // reader reads the object again through a larger window, or the guard is reported in its place.
        var diagnostics = new PdfDiagnostics();
        var parser = new PdfObjectParser(Encoding.ASCII.GetBytes(text), diagnostics: diagnostics, endsData: false);

        _ = parser.ParseObject();

        parser.IsTruncated.Should().BeTrue();
        diagnostics.Should().BeEmpty();
    }

    [Theory]
    [InlineData("[1 2 3]")]
    [InlineData("<< /A 1 >>")]
    [InlineData("(abc)")]
    [InlineData("(a\\\\)")]
    [InlineData("<414243>")]
    [InlineData("<>")]
    public void Reports_nothing_of_a_construct_closed_at_the_end_of_the_data(string text)
    {
        var diagnostics = new PdfDiagnostics();
        var parser = new PdfObjectParser(Encoding.ASCII.GetBytes(text), diagnostics: diagnostics);

        _ = parser.ParseObject();

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void Keeps_what_the_end_of_the_data_cut_short_as_far_as_it_was_read()
    {
        var array = Parse("[1 2 3").Should().BeOfType<PdfArray>().Subject;
        var dictionary = Parse("<< /A 1 /B [2").Should().BeOfType<PdfDictionary>().Subject;
        var literal = Parse("(abc\\").Should().BeOfType<PdfString>().Subject;
        var hexadecimal = Parse("<41424").Should().BeOfType<PdfString>().Subject;

        array.Select(item => item.AsInteger()).Should().Equal(1, 2, 3);
        dictionary.GetInteger(PdfName.Get("A")).Should().Be(1);
        dictionary.GetArray(PdfName.Get("B")).Required().Should().ContainSingle();
        literal.ToText().Should().Be("abc");
        hexadecimal.Bytes.ToArray().Should().Equal((byte)0x41, 0x42, 0x40);
    }

    [Fact]
    public void Says_an_object_stream_s_data_rather_than_the_file_ended_inside_a_member_s_construct()
    {
        var diagnostics = new PdfDiagnostics();
        var data = Encoding.ASCII.GetBytes("5 0 [1 2 (abc");
        var parser = PdfObjectParser.ForObjectStreamMember(data, 9, 1000, 5, ObjectSourceReturning(new PdfObjectId(1), PdfNull.Instance), diagnostics);
        parser.Position = 4;

        _ = parser.ParseObject();

        var report = diagnostics.Should().ContainSingle().Subject;
        report.Code.Should().Be(PdfDiagnosticCodes.SyntaxTruncatedObject);
        report.Position.Should().Be(1000);
        report.Message.Should().Be(
            "The object stream's decoded data ended inside a literal string, which takes the 4 bytes from where it opens to that end; " +
            "the array or dictionary around it was never closed either. It was met in object 5, at byte 9 of object stream 9's decoded data.");
    }

    [Fact]
    public void Reports_nothing_a_guard_cut_short_in_an_object_stream_s_data()
    {
        var diagnostics = new PdfDiagnostics();
        var data = Encoding.ASCII.GetBytes("5 0 [1 2 (abc");
        var parser = PdfObjectParser.ForObjectStreamMember(
            data, 9, 1000, 5, ObjectSourceReturning(new PdfObjectId(1), PdfNull.Instance), diagnostics, endsData: false);
        parser.Position = 4;

        _ = parser.ParseObject();

        parser.IsTruncated.Should().BeTrue();
        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void Reports_the_end_of_a_container_skipped_for_its_depth_once_besides_the_depth()
    {
        // Past the depth the parser follows, the container is skipped by counting brackets; the end of the data inside it is
        // still reported, once, where the skipped container opens.
        var text = new string('[', 130) + "1 2";
        var diagnostics = new PdfDiagnostics();
        var parser = new PdfObjectParser(Encoding.ASCII.GetBytes(text), diagnostics: diagnostics);

        _ = parser.ParseObject();

        diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.SyntaxDepthExceeded, PdfDiagnosticCodes.SyntaxTruncatedObject);
        diagnostics[1].Position.Should().Be(128);
        diagnostics[1].Message.Should().Be(
            "The file ended inside an array, which was never closed; the 128 arrays or dictionaries around it were never closed either.");
    }

    [Theory]
    [InlineData("[1 2 3 endobj", 0, 7, "An endobj ended the object inside an array, which was never closed.")]
    [InlineData("<< /A 1 endobj", 0, 8, "An endobj ended the object inside a dictionary, which was never closed.")]
    [InlineData("<< /A endobj", 0, 6, "An endobj ended the object inside a dictionary, which was never closed, before the value of its last key.")]
    [InlineData("[ << /A [1] endobj", 2, 12, "An endobj ended the object inside a dictionary, which was never closed; the array or dictionary around it was never closed either.")]
    public void Ends_every_container_an_endobj_finds_open_and_reports_the_innermost_once(string text, int opens, int endobj, string message)
    {
        // An endobj where a value or a key should be ends the object: the containers it finds open end there, as PDFBox and
        // pdfium read them, rather than take the objects after it (#119). The endobj itself is left for the object to end on.
        var diagnostics = new PdfDiagnostics();
        var parser = new PdfObjectParser(Encoding.ASCII.GetBytes(text + " 5 0 obj << /X 1 >> endobj"), diagnostics: diagnostics);

        _ = parser.ParseObject();

        parser.Position.Should().Be(endobj);
        parser.IsTruncated.Should().BeFalse();
        var report = diagnostics.Should().ContainSingle().Subject;
        report.Code.Should().Be(PdfDiagnosticCodes.SyntaxTruncatedObject);
        report.Position.Should().Be(opens);
        report.Message.Should().Be(message);
    }

    [Fact]
    public void Reads_an_object_whose_array_an_endobj_ends_as_ending_there()
    {
        var diagnostics = new PdfDiagnostics();
        var parser = new PdfObjectParser(Encoding.ASCII.GetBytes("4 0 obj [1 2 3 endobj 5 0 obj << /X 1 >> endobj"), diagnostics: diagnostics);

        parser.TryReadIndirectObject(out var id, out var value).Should().BeTrue();

        id.Should().Be(new PdfObjectId(4));
        value.Should().BeOfType<PdfArray>().Which.Select(item => item.AsInteger()).Should().Equal(1, 2, 3);
        parser.EndObj.Should().Be(EndObjState.Present);
        diagnostics.Should().ContainSingle().Which.Code.Should().Be(PdfDiagnosticCodes.SyntaxTruncatedObject);
    }

    [Theory]
    [InlineData("(abc endobj")]
    [InlineData("[(a endobj")]
    public void Leaves_an_endobj_inside_a_string_to_the_string(string text)
    {
        // Inside a string, endobj is data: the string takes it, and the end of the data is what leaves the string open.
        var diagnostics = new PdfDiagnostics();
        var parser = new PdfObjectParser(Encoding.ASCII.GetBytes(text), diagnostics: diagnostics);

        _ = parser.ParseObject();

        diagnostics.Should().ContainSingle().Which.Message.Should().StartWith("The file ended inside a literal string");
    }

    [Fact]
    public void Takes_no_endobj_a_window_s_edge_may_have_cut_for_one()
    {
        // At the edge of a window, "endobj" may be the start of a longer keyword: the array is read again through a larger
        // window rather than ended there.
        var diagnostics = new PdfDiagnostics();
        var parser = new PdfObjectParser(Encoding.ASCII.GetBytes("[1 2 endobj"), diagnostics: diagnostics, endsData: false);

        var array = parser.ParseObject().Should().BeOfType<PdfArray>().Subject;

        parser.IsTruncated.Should().BeTrue();
        array.Should().HaveCount(3);
        diagnostics.Should().NotContain(entry => entry.Code == PdfDiagnosticCodes.SyntaxTruncatedObject);
    }

    [Fact]
    public void Ends_a_container_skipped_for_its_depth_at_an_endobj()
    {
        var text = new string('[', 130) + "1 2 endobj";
        var diagnostics = new PdfDiagnostics();
        var parser = new PdfObjectParser(Encoding.ASCII.GetBytes(text), diagnostics: diagnostics);

        _ = parser.ParseObject();

        parser.Position.Should().Be(text.Length - "endobj".Length);
        diagnostics.Select(entry => entry.Code).Should().Equal(PdfDiagnosticCodes.SyntaxDepthExceeded, PdfDiagnosticCodes.SyntaxTruncatedObject);
        diagnostics[1].Message.Should().Be(
            "An endobj ended the object inside an array, which was never closed; the 128 arrays or dictionaries around it were never closed either.");
    }

    [Theory]
    [InlineData("<FFxyz00GG1>", 3, "A hexadecimal string holds 5 bytes that are neither hexadecimal digits nor white space, the first here; they were skipped.")]
    [InlineData("[1 <41%42>]", 6, "A hexadecimal string holds a byte that is neither a hexadecimal digit nor white space; it was skipped.")]
    public void Reports_once_the_bytes_of_a_hexadecimal_string_that_are_neither_digits_nor_white_space(string text, int position, string message)
    {
        // #172: the bytes are skipped, as pdf.js and pdfium skip them, and the string is reported once, where the first lies.
        var diagnostics = new PdfDiagnostics();
        var parser = new PdfObjectParser(Encoding.ASCII.GetBytes(text), diagnostics: diagnostics);

        _ = parser.ParseObject();

        var report = diagnostics.Should().ContainSingle().Subject;
        report.Code.Should().Be(PdfDiagnosticCodes.SyntaxHexStringInvalid);
        report.Severity.Should().Be(PdfDiagnosticSeverity.Warning);
        report.Position.Should().Be(position);
        report.Message.Should().Be(message);
    }

    [Theory]
    [InlineData("/A#zzB#4", 2, "/A#23zzB#234")]
    [InlineData("<< /F#zz 1 >>", 5, "/F#23zz")]
    [InlineData("[/Ok /A#4]", 7, "/A#234")]
    public void Reports_once_a_name_whose_number_sign_two_hexadecimal_digits_do_not_follow(string text, int position, string quoted)
    {
        // #172: the number sign is kept as the byte it is, as PDF 1.1 read it and most readers still do; the name is reported
        // once, where that number sign lies, and quoted as it reads, the kept sign written #23 as a writer writes it.
        var diagnostics = new PdfDiagnostics();
        var parser = new PdfObjectParser(Encoding.ASCII.GetBytes(text), diagnostics: diagnostics);

        _ = parser.ParseObject();

        var report = diagnostics.Should().ContainSingle().Subject;
        report.Code.Should().Be(PdfDiagnosticCodes.SyntaxNameEscapeInvalid);
        report.Position.Should().Be(position);
        report.Message.Should().Be(
            $"A name holds a number sign that two hexadecimal digits do not follow, kept as the byte it is: the name reads as {quoted}.");
    }

    [Theory]
    [InlineData("/A#00B")]
    [InlineData("<41 42\n43\t\f>")]
    [InlineData("/A#20B")]
    public void Reports_nothing_of_a_name_or_a_hexadecimal_string_that_breaks_no_rule_of_the_syntax(string text)
    {
        var diagnostics = new PdfDiagnostics();
        var parser = new PdfObjectParser(Encoding.ASCII.GetBytes(text), diagnostics: diagnostics);

        _ = parser.ParseObject();

        diagnostics.Should().BeEmpty();
    }

    [Theory]
    [InlineData("[/A#4")]
    [InlineData("[<41zz")]
    public void Reports_nothing_of_a_name_or_a_hexadecimal_string_a_window_s_edge_cuts(string text)
    {
        // At the edge of a window, "#4" may be the start of "#41", and the string may close past the edge.
        var diagnostics = new PdfDiagnostics();
        var parser = new PdfObjectParser(Encoding.ASCII.GetBytes(text), diagnostics: diagnostics, endsData: false);

        _ = parser.ParseObject();

        parser.IsTruncated.Should().BeTrue();
        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void Counts_without_quoting_a_name_whose_report_the_diagnostics_drop()
    {
        var diagnostics = new PdfDiagnostics { Capacity = 0 };
        var parser = new PdfObjectParser(Encoding.ASCII.GetBytes("[/A#zz <41zz42zz>]"), diagnostics: diagnostics);

        _ = parser.ParseObject();

        diagnostics.Should().BeEmpty();
        diagnostics.SuppressedCount.Should().Be(2);
    }

    [Fact]
    public void Says_in_which_member_a_malformed_name_and_hexadecimal_string_lie()
    {
        var diagnostics = new PdfDiagnostics();
        var data = Encoding.ASCII.GetBytes("5 0 [/A#zz <4G>]");
        var parser = PdfObjectParser.ForObjectStreamMember(data, 9, 1000, 5, ObjectSourceReturning(new PdfObjectId(1), PdfNull.Instance), diagnostics);
        parser.Position = 4;

        _ = parser.ParseObject();

        diagnostics.Select(entry => (entry.Code, entry.Position, entry.Message)).Should().Equal(
            (PdfDiagnosticCodes.SyntaxNameEscapeInvalid, 1000L,
                "A name holds a number sign that two hexadecimal digits do not follow, kept as the byte it is: the name reads as /A#23zz. It was met in object 5, at byte 7 of object stream 9's decoded data."),
            (PdfDiagnosticCodes.SyntaxHexStringInvalid, 1000L,
                "A hexadecimal string holds a byte that is neither a hexadecimal digit nor white space; it was skipped. It was met in object 5, at byte 13 of object stream 9's decoded data."));
    }

    private static PdfObject Parse(string text)
    {
        var parser = new PdfObjectParser(Encoding.ASCII.GetBytes(text));
        return parser.ParseObject();
    }

    private static PdfStream ParseStream(string text, PdfDiagnostics? diagnostics = null)
    {
        var parser = new PdfObjectParser(Encoding.ASCII.GetBytes(text), diagnostics: diagnostics);
        return parser.ParseObject().Should().BeOfType<PdfStream>().Subject;
    }

    /// <summary>
    /// An object source that knows one object. Substituted rather than hand-written, so that tests can
    /// also assert how the parser uses it, not only what it gets back.
    /// </summary>
    private static IPdfObjectSource ObjectSourceReturning(PdfObjectId id, PdfObject value)
    {
        var source = Substitute.For<IPdfObjectSource>();

        // An unconfigured substitute answers null, which is exactly the kind of surprise the reader must
        // never meet: absence in PDF is the null object.
        source.GetObject(Arg.Any<PdfObjectId>()).Returns(PdfNull.Instance);
        source.GetObject(id).Returns(value);
        return source;
    }
}
