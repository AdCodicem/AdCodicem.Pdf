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
    public void Drops_entries_whose_value_is_null_as_the_specification_requires()
    {
        var dictionary = Parse("<< /A 1 /B null >>").Should().BeOfType<PdfDictionary>().Subject;

        dictionary.Count.Should().Be(1);
        dictionary.ContainsKey(PdfName.Get("B")).Should().BeFalse();
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
