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
        dictionary.ToString().Should().Be("<</Type /Page ... 2 entries>>");

        dictionary.Remove(PdfName.Count);
        dictionary.ToString().Should().Be("<</Type /Page ... 1 entry>>");

        dictionary.Set(PdfName.Type, PdfInteger.Create(3));
        dictionary.ToString().Should().Be("<<dictionary of 1>>");
    }

    [Fact]
    public void A_name_describes_itself_as_a_writer_writes_it_whatever_it_holds()
    {
        PdfName.Get("Esc\u001B[31mred\nline").ToString().Should().Be("/Esc#1B#5B31mred#0Aline");
        PdfName.Get("A B#").ToString().Should().Be("/A#20B#23");
        PdfName.Get("caf\u00E9").ToString().Should().Be("/caf#E9");
        PdfName.Get("\u20AC").ToString().Should().Be("/#E2#82#AC", "a name a caller builds past U+00FF is written as its UTF-8 bytes");
    }

    [Fact]
    public void A_name_of_megabytes_and_what_it_types_describe_themselves_in_a_few_hundred_characters()
    {
        var name = PdfName.Get(new string('A', 4 * 1024 * 1024));
        var dictionary = new PdfDictionary { [PdfName.Type] = name };
        var stream = new PdfStream(new PdfDictionary { [PdfName.Subtype] = name }, PdfStreamData.FromMemory(new byte[1]));
        var quote = "/" + new string('A', 127) + " (the first 127 of 4,194,304 bytes)";

        // Warmed up first: what is measured is the describing, not the first call's own costs. Measured as the least of three
        // measures no collection paused, as every budget of a few kilobytes is: a collection another test triggers can fall in
        // the window and have the thread's count take an allocation quantum it never used, some 8 KB (#253).
        _ = (name.ToString(), dictionary.ToString(), stream.ToString());
        var allocated = Allocations.Least(
            () => (Name: name.ToString(), Dictionary: dictionary.ToString(), Stream: stream.ToString()),
            out var described);

        described.Name.Should().Be(quote);
        described.Dictionary.Should().Be("<</Type " + quote + " ... 1 entry>>");
        described.Stream.Should().Be("<<" + quote + " stream of 1 byte>>");
        allocated.Should().BeLessThan(4096, "a description copies none of the name it cuts");
    }

    [Fact]
    public void Objects_read_from_a_file_describe_themselves_in_printable_ascii_on_one_line()
    {
        var dictionary = Parse(@"<< /Type /T#1B#5B2J#0A /Title (text\033[31m\nline) >>").Should().BeOfType<PdfDictionary>().Subject;
        var stream = Parse("<< /Length 3 /Subtype /S#1B#5B31m >>\nstream\nabc\nendstream").Should().BeOfType<PdfStream>().Subject;

        dictionary.GetName(PdfName.Type)!.ToString().Should().Be("/T#1B#5B2J#0A");
        dictionary.ToString().Should().Be("<</Type /T#1B#5B2J#0A ... 2 entries>>");
        dictionary[PdfName.Get("Title")]!.ToString().Should().Be("\"text\\u001B[31m\\nline\"");
        stream.ToString().Should().Be("<</S#1B#5B31m stream of 3 bytes>>");

        static PdfObject Parse(string text) => new IO.PdfObjectParser(System.Text.Encoding.Latin1.GetBytes(text)).ParseObject();
    }

    [Theory]
    [InlineData("plain text", "\"plain text\"")]
    [InlineData("say \"hi\" \\ bye", "\"say \\\"hi\\\" \\\\ bye\"")]
    [InlineData("tab\tcr\rlf\nnul\0end", "\"tab\\tcr\\rlf\\nnul\\0end\"")]
    [InlineData("\u001B[2J\u007F\u0085\u009B", "\"\\u001B[2J\\u007F\\u0085\\u009B\"")]
    [InlineData("", "\"\"")]
    public void A_string_describes_its_text_quoted_with_what_is_not_printable_ascii_escaped(string text, string described)
    {
        new PdfString(System.Text.Encoding.Latin1.GetBytes(text)).ToString().Should().Be(described);
    }

    [Theory]
    [InlineData(0xFE, 0xD8, 0x3D)]
    [InlineData(0xFE, 0xDC, 0x00)]
    [InlineData(0xFE, 0x00, 0x41)]
    [InlineData(0xFF, 0x3D, 0xD8)]
    public void A_utf16_string_of_megabytes_is_described_without_decoding_what_is_cut(byte mark, byte first, byte second)
    {
        // A lone half of a pair, the other half, and a plain letter, repeated over two megabytes, and a dangling byte.
        var bytes = new byte[2 + (2 * 1024 * 1024) + 1];
        bytes[0] = mark;
        bytes[1] = (byte)(mark ^ 0x01);
        for (var i = 2; i + 1 < bytes.Length; i += 2)
        {
            bytes[i] = first;
            bytes[i + 1] = second;
        }

        var text = new PdfString(bytes);

        // Measured as the least of three measures no collection paused: counting two megabytes lasts long enough for a
        // collection another test triggers to fall in the window, and have the thread's count take an allocation quantum it
        // never used, some 8 KB, which the budget has no room for above a description of 10 KB (#253).
        _ = text.ToString();
        var allocated = Allocations.Least(text.ToString, out var described);

        described.Should().EndWith(string.Create(CultureInfo.InvariantCulture, $"\" (the first 127 of {text.ToText().Length:N0} characters)"));
        allocated.Should().BeLessThan(16 * 1024, "the whole is counted, not decoded");
    }

    [Fact]
    public void A_utf16_string_describes_its_marks_and_separators_escaped()
    {
        var text = PdfString.FromText("abc\u202Edef\u2028caf\u00E9 \U0001F600");

        text.ToText().Should().Be("abc\u202Edef\u2028caf\u00E9 \U0001F600");
        text.ToString().Should().Be("\"abc\\u202Edef\\u2028caf\\u00E9 \\uD83D\\uDE00\"");
    }

    [Fact]
    public void A_string_of_megabytes_describes_its_first_127_characters_and_how_many_it_holds()
    {
        var latin = new PdfString(System.Text.Encoding.ASCII.GetBytes(new string('A', 4 * 1024 * 1024)));
        var utf16 = PdfString.FromText(new string('a', 126) + "\U0001F600b");

        // Measured as the least of three measures no collection paused, as every budget of a few kilobytes is: a collection
        // another test triggers can fall in the window and have the thread's count take an allocation quantum it never used,
        // some 8 KB (#253).
        _ = latin.ToString();
        var allocated = Allocations.Least(latin.ToString, out var described);

        described.Should().Be("\"" + new string('A', 127) + "\" (the first 127 of 4,194,304 characters)");
        allocated.Should().BeLessThan(4096, "a description decodes no more of the text than it shows");
        utf16.ToString().Should().Be(
            "\"" + new string('a', 126) + "\\uD83D\" (the first 127 of 129 characters)",
            "the cut falls inside the pair, whose first half is still read as the half it is");
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
    [InlineData(4.0, "4.0")]
    [InlineData(0.0, "0.0")]
    [InlineData(-0.0, "-0.0")]
    [InlineData(2.0 / 3, "0.6666666666666666")]
    [InlineData(0.1 + 0.2, "0.30000000000000004")]
    [InlineData(1e20, "100000000000000000000.0")]
    [InlineData(1.2345678901234568E+20, "123456789012345680000.0")]
    [InlineData(1e-7, "0.0000001")]
    [InlineData(-1e-7, "-0.0000001")]
    [InlineData(1.5e-5, "0.000015")]
    [InlineData(double.NaN, "NaN")]
    [InlineData(double.PositiveInfinity, "Infinity")]
    [InlineData(double.NegativeInfinity, "-Infinity")]
    public void A_real_describes_itself_as_the_shortest_decimal_that_reads_back_with_a_point(double value, string described)
    {
        new PdfReal(value).ToString().Should().Be(described);
    }

    [Theory]
    [InlineData(double.Epsilon)]
    [InlineData(-double.Epsilon)]
    [InlineData(double.MaxValue)]
    [InlineData(double.MinValue)]
    public void A_real_at_the_ends_of_a_double_is_described_whole_within_327_characters(double value)
    {
        var described = new PdfReal(value).ToString();

        described.Should().NotContainAny("E", "e");
        described.Should().Contain(".");
        described.Length.Should().BeLessThanOrEqualTo(327);
        double.Parse(described, CultureInfo.InvariantCulture).Should().Be(value);
    }

    [Theory]
    [InlineData(4.0, 4L)]
    [InlineData(4.5, null)]
    [InlineData(1e300, null)]
    [InlineData(9223372036854775808.0, null)]
    [InlineData(-9223372036854775808.0, long.MinValue)]
    [InlineData(9223372036854774784.0, 9223372036854774784L)]
    [InlineData(double.PositiveInfinity, null)]
    [InlineData(double.NaN, null)]
    public void A_real_is_an_integer_only_when_it_has_no_fractional_part_and_fits(double value, long? expected)
    {
        // 2^63, the first double past long.MaxValue, once read as long.MaxValue; 9223372036854774784 is the last below it.
        new PdfReal(value).AsInteger().Should().Be(expected);
    }

    [Fact]
    public void Resolving_no_object_gives_none()
    {
        ((PdfObject?)null).Resolved().Should().BeNull();
        PdfName.Crypt.Resolved().Should().BeSameAs(PdfName.Crypt);
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
    public void A_reference_with_no_source_is_the_last_object_a_chain_leads_to()
    {
        // A reference built with no source reads as null, as one to an object no file defines: no chain goes on from it, so
        // the object whose value it gives is the one it names, alone or at the end of a chain.
        var source = Substitute.For<IPdfObjectSource>();
        source.GetObject(new PdfObjectId(1)).Returns(new PdfReference(new PdfObjectId(2, 4)));

        new PdfReference(new PdfObjectId(7, 2)).Target().Should().Be(new PdfObjectId(7, 2));
        new PdfReference(new PdfObjectId(1), source).Target().Should().Be(new PdfObjectId(2, 4));
        new PdfReference(new PdfObjectId(1), source).Resolve().Should().BeSameAs(PdfNull.Instance);
    }

    [Fact]
    public void A_string_marked_utf16_little_endian_reads_as_text()
    {
        var text = new PdfString(new byte[] { 0xFF, 0xFE, (byte)'h', 0, (byte)'i', 0 });

        text.ToText().Should().Be("hi");
        text.ToString().Should().Be("\"hi\"");
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
