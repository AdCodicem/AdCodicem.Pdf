using System.Collections;
using System.Globalization;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.Objects;
using NSubstitute;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// The object model as a caller builds and reads it: arrays and dictionaries edited in place, values read through
/// the typed accessors, references followed, and each object described for a debugger.
/// </summary>
public class ObjectModelTests
{
    [Fact]
    public void An_array_is_edited_in_place()
    {
        var array = new PdfArray(capacity: 4);
        array.Add(PdfInteger.Create(1));
        array.Add(PdfInteger.Create(3));

        array.Insert(1, PdfInteger.Create(2));
        array.Set(2, PdfName.Get("Three"));
        array.RemoveAt(0);

        array.Should().Equal(PdfInteger.Create(2), PdfName.Get("Three"));
        array.ToString().Should().Be("[array of 2]");

        array.Clear();
        array.Count.Should().Be(0);
    }

    [Fact]
    public void An_array_refuses_a_null_entry()
    {
        var array = new PdfArray([PdfInteger.Create(1)]);

        FluentActions.Invoking(() => array.Insert(0, null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => array.Set(0, null!)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void An_array_enumerates_through_every_interface_it_implements()
    {
        var array = new PdfArray([PdfInteger.Create(1), PdfInteger.Create(2)]);

        ((IEnumerable<PdfObject>)array).Should().Equal(PdfInteger.Create(1), PdfInteger.Create(2));
        ((IEnumerable)array).Cast<PdfObject>().Should().Equal(PdfInteger.Create(1), PdfInteger.Create(2));
    }

    [Fact]
    public void A_dictionary_entry_set_to_null_is_removed()
    {
        var dictionary = new PdfDictionary(capacity: 2);
        dictionary[PdfName.Type] = PdfName.Get("Font");
        dictionary[PdfName.Length] = PdfInteger.Create(4);

        dictionary[PdfName.Length] = null;

        dictionary.Count.Should().Be(1);
        dictionary.Remove(PdfName.Type).Should().BeTrue();
        dictionary.Remove(PdfName.Type).Should().BeFalse();
        FluentActions.Invoking(() => dictionary[null!] = PdfNull.Instance).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void A_dictionary_enumerates_its_entries_and_describes_itself_by_its_type()
    {
        var dictionary = new PdfDictionary();
        dictionary.Set(PdfName.Type, PdfName.Get("Page"));
        dictionary.Set(PdfName.Count, PdfInteger.Create(3));

        ((IEnumerable<KeyValuePair<PdfName, PdfObject>>)dictionary).Select(entry => entry.Key.Value)
            .Should().BeEquivalentTo("Type", "Count");
        dictionary.ToString().Should().Be("<</Type /Page … 2 entries>>");

        dictionary.Remove(PdfName.Type);
        dictionary.ToString().Should().Be("<<dictionary of 1>>");
    }

    [Theory]
    [InlineData("sv-SE")]
    [InlineData("fa-IR")]
    public void Writes_an_object_identifier_the_same_whatever_the_culture(string name)
    {
        // A caller can build an identifier of negative numbers, which Swedish and Persian would write with U+2212.
        var culture = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(name);

            new PdfReference(new PdfObjectId(-1, -2)).ToString().Should().Be("-1 -2 R");
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    [Fact]
    public void A_scalar_describes_itself_in_PDF_syntax()
    {
        PdfNull.Instance.ToString().Should().Be("null");
        PdfBoolean.True.ToString().Should().Be("true");
        PdfBoolean.False.ToString().Should().Be("false");
        PdfInteger.Create(42).ToString().Should().Be("42");
        PdfInteger.Create(-17).ToString().Should().Be("-17");
        PdfInteger.Create(long.MaxValue).ToString().Should().Be("9223372036854775807");
        new PdfReference(new PdfObjectId(12)).ToString().Should().Be("12 0 R");
        new PdfReference(new PdfObjectId(12, 3)).ToString().Should().Be("12 3 R");
    }

    [Theory]
    [InlineData(3.5, "3.5")]
    [InlineData(-0.25, "-0.25")]
    [InlineData(4.0, "4")]
    [InlineData(2.0 / 3, "0.666667")]
    [InlineData(1e20, "100000000000000000000")]
    [InlineData(1e-7, "0")]
    [InlineData(-1e-7, "-0")]
    public void A_real_describes_itself_without_an_exponent_and_to_six_decimals(double value, string described)
    {
        new PdfReal(value).ToString().Should().Be(described);
    }

    [Theory]
    [InlineData(4.0, 4L)]
    [InlineData(4.5, null)]
    [InlineData(1e300, null)]
    public void A_real_is_an_integer_only_when_it_has_no_fractional_part_and_fits(double value, long? expected)
    {
        new PdfReal(value).AsInteger().Should().Be(expected);
    }

    [Fact]
    public void Typed_accessors_read_what_the_value_is_and_nothing_else()
    {
        var dictionary = new PdfDictionary();
        dictionary.Set(PdfName.Get("Text"), PdfString.FromText("é"));
        dictionary.Set(PdfName.Get("Integer"), PdfInteger.Create(7));
        dictionary.Set(PdfName.Get("Real"), new PdfReal(2.5));
        dictionary.Set(PdfName.Get("Flag"), PdfBoolean.Get(true));

        dictionary.GetText(PdfName.Get("Text")).Should().Be("é");
        dictionary.Get(PdfName.Get("Text")).AsString().Should().NotBeNull();
        dictionary.GetNumber(PdfName.Get("Integer")).Should().Be(7);
        dictionary.GetNumber(PdfName.Get("Real")).Should().Be(2.5);
        dictionary.GetNumber(PdfName.Get("Flag")).Should().BeNull();
        dictionary.GetNumber(PdfName.Get("Missing"), 1.5).Should().Be(1.5);
        dictionary.GetBoolean(PdfName.Get("Flag"), false).Should().BeTrue();
        dictionary.GetBoolean(PdfName.Get("Integer"), false).Should().BeFalse();
        dictionary.GetStream(PdfName.Get("Integer")).Should().BeNull();
        ((PdfDictionary?)null).Get(PdfName.Type).Should().BeNull();
    }

    [Fact]
    public void Typed_accessors_answer_null_for_no_value()
    {
        PdfObject? none = null;

        none.AsStream().Should().BeNull();
        none.AsString().Should().BeNull();
        none.AsBoolean().Should().BeNull();
        none.AsText().Should().BeNull();
    }

    [Fact]
    public void Containers_enumerate_directly_and_without_generics()
    {
        var array = new PdfArray([PdfInteger.Create(1)]);
        var dictionary = new PdfDictionary();
        dictionary.Set(PdfName.Type, PdfName.Page);
        var diagnostics = new PdfDiagnostics();
        diagnostics.Warn("a.warning", "Worrying.");

        Count(array).Should().Be(1);
        Count(dictionary).Should().Be(1);
        Count(diagnostics).Should().Be(1);

        var direct = 0;
        foreach (var entry in diagnostics)
        {
            entry.Code.Should().Be("a.warning");
            direct++;
        }

        direct.Should().Be(1);

        static int Count(IEnumerable items)
        {
            var count = 0;
            foreach (var item in items)
            {
                item.Should().NotBeNull();
                count++;
            }

            return count;
        }
    }

    [Fact]
    public void A_chain_of_references_that_loops_resolves_to_null()
    {
        var source = Substitute.For<IPdfObjectSource>();
        source.GetObject(new PdfObjectId(1)).Returns(new PdfReference(new PdfObjectId(2), source));
        source.GetObject(new PdfObjectId(2)).Returns(new PdfReference(new PdfObjectId(1), source));

        new PdfReference(new PdfObjectId(1), source).Resolve().Should().BeSameAs(PdfNull.Instance);
    }

    [Fact]
    public void A_string_marked_utf16_little_endian_reads_as_text()
    {
        var text = new PdfString(new byte[] { 0xFF, 0xFE, (byte)'h', 0, (byte)'i', 0 });

        text.ToText().Should().Be("hi");
        text.ToString().Should().Be("hi");
    }

    [Fact]
    public void Names_compare_by_value()
    {
        var name = PdfName.Get("Font");

        name.Equals((object)PdfName.Get("Font")).Should().BeTrue();
        name.Equals((object)"Font").Should().BeFalse();
        (name != PdfName.Get("Page")).Should().BeTrue();
        (name != PdfName.Get("Font")).Should().BeFalse();
    }

    [Fact]
    public void Shared_values_are_what_they_say()
    {
        PdfBoolean.Get(true).Should().BeSameAs(PdfBoolean.True);
        PdfBoolean.Get(false).Should().BeSameAs(PdfBoolean.False);
        PdfReal.Zero.Value.Should().Be(0d);
        new PdfObjectId(0).IsEmpty.Should().BeTrue();
        new PdfObjectId(1).IsEmpty.Should().BeFalse();
    }

    [Theory]
    [InlineData("/Subtype /Image /Type /XObject", "<</Image stream of 4 bytes>>")]
    [InlineData("/Type /XRef", "<</XRef stream of 4 bytes>>")]
    [InlineData("", "<<stream of 4 bytes>>")]
    public void A_stream_describes_itself_by_its_subtype_or_its_type(string entries, string described)
    {
        var dictionary = new PdfDictionary();
        var parts = entries.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i + 1 < parts.Length; i += 2)
        {
            dictionary.Set(PdfName.Get(parts[i][1..]), PdfName.Get(parts[i + 1][1..]));
        }

        var stream = new PdfStream(dictionary, PdfStreamData.FromMemory(new byte[4]));

        stream.RawLength.Should().Be(4);
        stream.ToString().Should().Be(described);
    }

    [Fact]
    public void An_exception_keeps_the_one_it_wraps()
    {
        var cause = new InvalidOperationException("cause");

        new PdfException("outer", cause).InnerException.Should().BeSameAs(cause);
        new PdfFormatException("outer", cause).InnerException.Should().BeSameAs(cause);
    }
}
