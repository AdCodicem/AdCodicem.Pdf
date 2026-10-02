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
