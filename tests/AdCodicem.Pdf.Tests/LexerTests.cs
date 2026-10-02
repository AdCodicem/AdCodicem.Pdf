using System.Text;
using AdCodicem.Pdf.IO;

namespace AdCodicem.Pdf.Tests;

public class LexerTests
{
    [Fact]
    public void Reads_the_structural_delimiters()
    {
        var kinds = Tokenize("[ ] << >> { }");

        kinds.Should().Equal(
        [
            PdfTokenKind.ArrayStart,
            PdfTokenKind.ArrayEnd,
            PdfTokenKind.DictionaryStart,
            PdfTokenKind.DictionaryEnd,
            PdfTokenKind.BraceOpen,
            PdfTokenKind.BraceClose,
        ]);
    }

    [Fact]
    public void Treats_a_comment_as_whitespace()
    {
        var kinds = Tokenize("% a comment\n42");

        kinds.Should().Equal(PdfTokenKind.Integer);
    }

    [Fact]
    public void Separates_tokens_by_every_white_space_byte_and_either_end_of_line()
    {
        // NUL, tab, form feed, a lone carriage return, CR LF, and a comment that a lone carriage return ends.
        var lexer = new PdfLexer("1\u00002\t3\f4\r5\r\n6 % a comment\r7"u8.ToArray());
        var integers = new List<long>();

        for (var token = lexer.Read(); token.Kind != PdfTokenKind.EndOfInput; token = lexer.Read())
        {
            token.Kind.Should().Be(PdfTokenKind.Integer);
            integers.Add(token.Integer);
        }

        integers.Should().Equal(1, 2, 3, 4, 5, 6, 7);
    }

    [Theory]
    [InlineData("0", 0L)]
    [InlineData("42", 42L)]
    [InlineData("-17", -17L)]
    [InlineData("+17", 17L)]
    [InlineData("--17", -17L)]
    [InlineData("+-17", 17L)]
    [InlineData("0000000016", 16L)]
    [InlineData("9223372036854775807", long.MaxValue)]
    [InlineData("-9223372036854775807", -long.MaxValue)]
    [InlineData("-9223372036854775808", long.MinValue)]
    public void Reads_integers(string text, long expected)
    {
        var lexer = new PdfLexer(Encoding.ASCII.GetBytes(text));
        var token = lexer.Read();

        token.Kind.Should().Be(PdfTokenKind.Integer);
        token.Integer.Should().Be(expected);
    }

    [Theory]
    [InlineData("34.5", 34.5)]
    [InlineData("-3.62", -3.62)]
    [InlineData("4.", 4.0)]
    [InlineData("-.002", -0.002)]
    [InlineData(".5", 0.5)]
    [InlineData("-0.0", -0.0)]
    [InlineData("0.3", 0.3)]
    [InlineData("0.0123457", 0.0123457)]
    [InlineData("--0.7071", -0.7071)]
    [InlineData("+-841.920044", 841.920044)]
    [InlineData("0.9007199254740992", 0.9007199254740992)]
    [InlineData("0.9007199254740993", 0.9007199254740993)]
    [InlineData("9007199254740993.0", 9007199254740992.0)]
    [InlineData("0.0000000000000000000001", 1e-22)]
    [InlineData("0.00000000000000000000001", 1e-23)]
    public void Reads_a_real_as_the_double_nearest_to_its_decimal(string text, double expected)
    {
        // Either side of 2^53 and of 22 decimals, where the parser's exact division gives way to the framework's parse.
        // Equal bit for bit: -0.0 is not 0.0.
        var lexer = new PdfLexer(Encoding.ASCII.GetBytes(text));
        var token = lexer.Read();

        token.Kind.Should().Be(PdfTokenKind.Real);
        BitConverter.DoubleToInt64Bits(token.Real).Should().Be(BitConverter.DoubleToInt64Bits(expected));
    }

    [Theory]
    [InlineData("9223372036854775808", 9223372036854775808d)]
    [InlineData("-9223372036854775809", -9223372036854775809d)]
    [InlineData("92233720368547758085", 92233720368547758085d)]
    [InlineData("18446744073709551616", 18446744073709551616d)]
    [InlineData("123456789012345678901234", 1.23456789012345678901234e23)]
    [InlineData("-99999999999999999999.5", -99999999999999999999.5)]
    public void Reads_an_integer_too_large_for_a_long_as_a_real(string text, double expected)
    {
        // Never wrapped: 92233720368547758085 once read as 5, and named object 5 in "92233720368547758085 0 R".
        var lexer = new PdfLexer(Encoding.ASCII.GetBytes(text));
        var token = lexer.Read();

        token.Kind.Should().Be(PdfTokenKind.Real);
        token.Real.Should().Be(expected);
    }

    [Theory]
    [InlineData("", double.PositiveInfinity)]
    [InlineData("-", double.NegativeInfinity)]
    public void Reads_a_number_past_the_largest_double_as_an_infinity_its_reader_can_tell(string sign, double expected)
    {
        var lexer = new PdfLexer(Encoding.ASCII.GetBytes(sign + "1" + new string('0', 309)));
        var token = lexer.Read();

        token.Kind.Should().Be(PdfTokenKind.Real);
        token.Real.Should().Be(expected);
    }

    [Fact]
    public void Reads_a_real_below_the_smallest_double_as_zero()
    {
        var lexer = new PdfLexer(Encoding.ASCII.GetBytes("0." + new string('0', 400) + "5"));
        var token = lexer.Read();

        token.Kind.Should().Be(PdfTokenKind.Real);
        BitConverter.DoubleToInt64Bits(token.Real).Should().Be(0);
    }

    [Fact]
    public void Reads_no_number_in_nothing()
    {
        PdfNumberParser.TryParse([], out _, out _, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(".")]
    [InlineData("-")]
    [InlineData("+-")]
    [InlineData("-.")]
    [InlineData("1.2.3")]
    [InlineData("1..2")]
    [InlineData("5-")]
    [InlineData("1e5")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public void Reads_no_number_in_a_run_that_is_not_digits_around_at_most_one_period(string text)
    {
        // The framework would read NaN and Infinity, whatever its style: the parser checks the shape before asking it.
        PdfNumberParser.TryParse(Encoding.ASCII.GetBytes(text), out _, out _, out var isReal).Should().BeFalse();
        isReal.Should().BeFalse();
    }

    [Fact]
    public void Parsing_a_number_allocates_nothing_however_long_it_is()
    {
        // A million digits, read once as an integer past a long and once as a real, both through the framework's parse.
        // The least of a few runs, after a first one: the framework's first parse in a process allocates once, 1,344
        // bytes, as the runtime can on this thread outside the parse.
        var integer = Encoding.ASCII.GetBytes(new string('7', 1_000_000));
        var real = Encoding.ASCII.GetBytes("0." + new string('7', 1_000_000));
        var least = long.MaxValue;

        for (var run = 0; run < 5 && least > 0; run++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            var parsed = PdfNumberParser.TryParse(integer, out _, out var large, out _) &
                PdfNumberParser.TryParse(real, out _, out var small, out _);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            parsed.Should().BeTrue();
            large.Should().Be(double.PositiveInfinity);
            small.Should().Be(0.7777777777777778);
            least = Math.Min(least, allocated);
        }

        least.Should().Be(0);
    }

    [Fact]
    public void Reads_a_run_that_is_not_a_number_as_a_keyword()
    {
        var lexer = new PdfLexer("12abc"u8.ToArray());
        var token = lexer.Read();

        token.Kind.Should().Be(PdfTokenKind.Keyword);
    }

    [Fact]
    public void Reads_a_name_with_its_escapes_intact()
    {
        var lexer = new PdfLexer("/Name#20With#20Spaces"u8.ToArray());
        var token = lexer.Read();

        token.Kind.Should().Be(PdfTokenKind.Name);
        PdfStringDecoder.DecodeName(token.Text).Should().Be("Name With Spaces");
    }

    [Fact]
    public void Reads_an_empty_name()
    {
        var lexer = new PdfLexer("/ 1"u8.ToArray());
        var token = lexer.Read();

        token.Kind.Should().Be(PdfTokenKind.Name);
        token.Text.Length.Should().Be(0);
    }

    [Fact]
    public void Reads_a_literal_string_with_balanced_parentheses()
    {
        var lexer = new PdfLexer("(outer (inner) still outer)"u8.ToArray());
        var token = lexer.Read();

        token.Kind.Should().Be(PdfTokenKind.LiteralString);
        Encoding.ASCII.GetString(PdfStringDecoder.DecodeLiteral(token.Text))
            .Should().Be("outer (inner) still outer");
    }

    [Fact]
    public void Reads_a_literal_string_whose_parenthesis_is_escaped()
    {
        var lexer = new PdfLexer(@"(a \) b)"u8.ToArray());
        var token = lexer.Read();

        Encoding.ASCII.GetString(PdfStringDecoder.DecodeLiteral(token.Text)).Should().Be("a ) b");
    }

    [Fact]
    public void Recovers_from_an_unterminated_literal_string()
    {
        var lexer = new PdfLexer("(never closed"u8.ToArray());
        var token = lexer.Read();

        token.Kind.Should().Be(PdfTokenKind.LiteralString);
        lexer.IsAtEnd.Should().BeTrue();
    }

    [Fact]
    public void Pads_an_odd_hexadecimal_string_with_a_trailing_zero()
    {
        var lexer = new PdfLexer("<901FA>"u8.ToArray());
        var token = lexer.Read();

        token.Kind.Should().Be(PdfTokenKind.HexString);
        PdfStringDecoder.DecodeHex(token.Text).Should().Equal((byte)0x90, 0x1F, 0xA0);
    }

    [Fact]
    public void Ignores_whitespace_inside_a_hexadecimal_string()
    {
        var lexer = new PdfLexer("<48 65\n6C>"u8.ToArray());
        var token = lexer.Read();

        PdfStringDecoder.DecodeHex(token.Text).Should().Equal((byte)0x48, 0x65, 0x6C);
    }

    [Fact]
    public void Decodes_the_octal_and_control_escapes_of_a_literal_string()
    {
        var lexer = new PdfLexer(@"(tab:\t octal:\101 continued:\
end)"u8.ToArray());
        var token = lexer.Read();

        Encoding.ASCII.GetString(PdfStringDecoder.DecodeLiteral(token.Text))
            .Should().Be("tab:\t octal:A continued:end");
    }

    [Fact]
    public void Decodes_a_backspace_and_a_line_continuation_at_either_end_of_line()
    {
        var lexer = new PdfLexer("(back\\bspace cr:\\\rcrlf:\\\r\nend)"u8.ToArray());
        var token = lexer.Read();

        Encoding.ASCII.GetString(PdfStringDecoder.DecodeLiteral(token.Text)).Should().Be("back\bspace cr:crlf:end");
    }

    [Fact]
    public void Drops_a_backslash_that_ends_a_literal_string()
    {
        // An unterminated string can end in the middle of an escape.
        Encoding.ASCII.GetString(PdfStringDecoder.DecodeLiteral("cut\\"u8)).Should().Be("cut");
    }

    [Fact]
    public void Never_loops_on_a_stray_delimiter()
    {
        var kinds = Tokenize(")))");

        kinds.Should().Equal(PdfTokenKind.Unknown, PdfTokenKind.Unknown, PdfTokenKind.Unknown);
    }

    private static List<PdfTokenKind> Tokenize(string text)
    {
        var lexer = new PdfLexer(Encoding.ASCII.GetBytes(text));
        var kinds = new List<PdfTokenKind>();

        while (true)
        {
            var token = lexer.Read();
            if (token.Kind == PdfTokenKind.EndOfInput)
            {
                return kinds;
            }

            kinds.Add(token.Kind);
        }
    }
}
