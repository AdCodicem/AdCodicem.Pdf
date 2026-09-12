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
        Parse("true").ShouldBeSameAs(PdfBoolean.True);
        Parse("false").ShouldBeSameAs(PdfBoolean.False);
        Parse("null").ShouldBeSameAs(PdfNull.Instance);
        Parse("42").ShouldBeOfType<PdfInteger>().Value.ShouldBe(42);
        Parse("3.5").ShouldBeOfType<PdfReal>().Value.ShouldBe(3.5);
        Parse("/Type").ShouldBeOfType<PdfName>().Value.ShouldBe("Type");
        Parse("(text)").ShouldBeOfType<PdfString>().ToText().ShouldBe("text");
    }

    [Fact]
    public void Parses_an_array()
    {
        var array = Parse("[1 2 /Three (four)]").ShouldBeOfType<PdfArray>();

        array.Count.ShouldBe(4);
        array[0].AsInteger().ShouldBe(1);
        array[2].AsName()!.Value.ShouldBe("Three");
    }

    [Fact]
    public void Parses_a_dictionary()
    {
        var dictionary = Parse("<< /Type /Page /Count 3 /Nested << /A true >> >>").ShouldBeOfType<PdfDictionary>();

        dictionary.IsOfType(PdfName.Page).ShouldBeTrue();
        dictionary.GetInteger(PdfName.Count).ShouldBe(3);
        dictionary.GetDictionary(PdfName.Get("Nested")).ShouldNotBeNull();
    }

    [Fact]
    public void Distinguishes_a_reference_from_two_integers()
    {
        var reference = Parse("12 0 R").ShouldBeOfType<PdfReference>();
        reference.Id.ShouldBe(new PdfObjectId(12));

        var array = Parse("[1 2]").ShouldBeOfType<PdfArray>();
        array.Count.ShouldBe(2);
    }

    [Fact]
    public void Backtracks_when_two_integers_are_not_followed_by_R()
    {
        var array = Parse("[1 2 3]").ShouldBeOfType<PdfArray>();

        array.Count.ShouldBe(3);
        array[2].AsInteger().ShouldBe(3);
    }

    [Fact]
    public void Reads_an_indirect_object_definition()
    {
        var bytes = Encoding.ASCII.GetBytes("7 0 obj\n<< /Length 0 >>\nendobj\n");
        var parser = new PdfObjectParser(bytes);

        parser.TryReadIndirectObject(out var id, out var value).ShouldBeTrue();

        id.ShouldBe(new PdfObjectId(7));
        value.ShouldBeOfType<PdfDictionary>();
    }

    [Fact]
    public void Reads_a_stream_whose_declared_length_is_correct()
    {
        var stream = ParseStream("<< /Length 11 >>\nstream\nHello World\nendstream");

        Encoding.ASCII.GetString(stream.GetRawBytes().Span).ShouldBe("Hello World");
    }

    [Fact]
    public void Recovers_a_stream_whose_declared_length_is_wrong()
    {
        var diagnostics = new PdfDiagnostics();
        var stream = ParseStream("<< /Length 3 >>\nstream\nHello World\nendstream", diagnostics);

        Encoding.ASCII.GetString(stream.GetRawBytes().Span).ShouldBe("Hello World");
        diagnostics.Contains(PdfDiagnosticCodes.StreamLengthInvalid).ShouldBeTrue();
    }

    [Fact]
    public void Recovers_a_stream_that_never_ends()
    {
        var diagnostics = new PdfDiagnostics();
        var stream = ParseStream("<< /Length 99 >>\nstream\ntruncated", diagnostics);

        Encoding.ASCII.GetString(stream.GetRawBytes().Span).ShouldBe("truncated");
        diagnostics.Contains(PdfDiagnosticCodes.StreamTruncated).ShouldBeTrue();
    }

    [Fact]
    public void Resolves_a_length_given_as_an_indirect_reference()
    {
        var source = new FixedObjectSource(new PdfObjectId(9), PdfInteger.Create(5));
        var bytes = Encoding.ASCII.GetBytes("<< /Length 9 0 R >>\nstream\nABCDE\nendstream");
        var parser = new PdfObjectParser(bytes, source: source);

        var stream = parser.ParseObject().ShouldBeOfType<PdfStream>();

        Encoding.ASCII.GetString(stream.GetRawBytes().Span).ShouldBe("ABCDE");
    }

    [Fact]
    public void Reports_a_truncated_object_instead_of_throwing()
    {
        var diagnostics = new PdfDiagnostics();
        var bytes = Encoding.ASCII.GetBytes("<< /Type /Page /Kids [1 0 R");
        var parser = new PdfObjectParser(bytes, diagnostics: diagnostics);

        parser.ParseObject().ShouldBeOfType<PdfDictionary>();

        parser.IsTruncated.ShouldBeTrue();
    }

    [Fact]
    public void Refuses_to_follow_nesting_deeper_than_its_limit()
    {
        var diagnostics = new PdfDiagnostics();
        var bytes = Encoding.ASCII.GetBytes(new string('[', 5000) + new string(']', 5000));
        var parser = new PdfObjectParser(bytes, diagnostics: diagnostics);

        parser.ParseObject();

        diagnostics.Contains(PdfDiagnosticCodes.SyntaxDepthExceeded).ShouldBeTrue();
    }

    [Fact]
    public void Drops_entries_whose_value_is_null_as_the_specification_requires()
    {
        var dictionary = Parse("<< /A 1 /B null >>").ShouldBeOfType<PdfDictionary>();

        dictionary.Count.ShouldBe(1);
        dictionary.ContainsKey(PdfName.Get("B")).ShouldBeFalse();
    }

    [Fact]
    public void Terminates_on_a_dictionary_that_is_never_closed()
    {
        var bytes = Encoding.ASCII.GetBytes("<< /A /B /C");
        var parser = new PdfObjectParser(bytes);

        parser.ParseObject().ShouldBeOfType<PdfDictionary>().Count.ShouldBe(1);
        parser.IsTruncated.ShouldBeTrue();
    }

    [Fact]
    public void Treats_an_unresolvable_reference_as_null()
    {
        var reference = Parse("3 0 R").ShouldBeOfType<PdfReference>();

        reference.Resolve().ShouldBeSameAs(PdfNull.Instance);
    }

    private static PdfObject Parse(string text)
    {
        var parser = new PdfObjectParser(Encoding.ASCII.GetBytes(text));
        return parser.ParseObject();
    }

    private static PdfStream ParseStream(string text, PdfDiagnostics? diagnostics = null)
    {
        var parser = new PdfObjectParser(Encoding.ASCII.GetBytes(text), diagnostics: diagnostics);
        return parser.ParseObject().ShouldBeOfType<PdfStream>();
    }

    private sealed class FixedObjectSource(PdfObjectId id, PdfObject value) : IPdfObjectSource
    {
        public PdfObject GetObject(PdfObjectId requested) => requested == id ? value : PdfNull.Instance;
    }
}
