using System.Text;
using AdCodicem.Pdf.IO;

namespace AdCodicem.Pdf.Tests;

public class LexerTests
{
    [Fact]
    public void Reads_the_structural_delimiters()
    {
        var kinds = Tokenise("[ ] << >> { }");

        kinds.ShouldBe(
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
        var kinds = Tokenise("% a comment\n42");

        kinds.ShouldBe([PdfTokenKind.Integer]);
    }

    [Theory]
    [InlineData("0", 0L)]
    [InlineData("42", 42L)]
    [InlineData("-17", -17L)]
    [InlineData("+17", 17L)]
    [InlineData("0000000016", 16L)]
    public void Reads_integers(string text, long expected)
    {
        var lexer = new PdfLexer(Encoding.ASCII.GetBytes(text));
        var token = lexer.Read();

        token.Kind.ShouldBe(PdfTokenKind.Integer);
        token.Integer.ShouldBe(expected);
    }

    [Theory]
    [InlineData("34.5", 34.5)]
    [InlineData("-3.62", -3.62)]
    [InlineData("4.", 4.0)]
    [InlineData("-.002", -0.002)]
    [InlineData(".5", 0.5)]
    public void Reads_reals_including_the_forms_the_framework_rejects(string text, double expected)
    {
        var lexer = new PdfLexer(Encoding.ASCII.GetBytes(text));
        var token = lexer.Read();

        token.Kind.ShouldBe(PdfTokenKind.Real);
        token.Real.ShouldBe(expected, 1e-9);
    }

    [Fact]
    public void Reads_a_run_that_is_not_a_number_as_a_keyword()
    {
        var lexer = new PdfLexer("12abc"u8.ToArray());
        var token = lexer.Read();

        token.Kind.ShouldBe(PdfTokenKind.Keyword);
    }

    [Fact]
    public void Reads_a_name_with_its_escapes_intact()
    {
        var lexer = new PdfLexer("/Name#20With#20Spaces"u8.ToArray());
        var token = lexer.Read();

        token.Kind.ShouldBe(PdfTokenKind.Name);
        PdfStringDecoder.DecodeName(token.Text).ShouldBe("Name With Spaces");
    }

    [Fact]
    public void Reads_an_empty_name()
    {
        var lexer = new PdfLexer("/ 1"u8.ToArray());
        var token = lexer.Read();

        token.Kind.ShouldBe(PdfTokenKind.Name);
        token.Text.Length.ShouldBe(0);
    }

    [Fact]
    public void Reads_a_literal_string_with_balanced_parentheses()
    {
        var lexer = new PdfLexer("(outer (inner) still outer)"u8.ToArray());
        var token = lexer.Read();

        token.Kind.ShouldBe(PdfTokenKind.LiteralString);
        Encoding.ASCII.GetString(PdfStringDecoder.DecodeLiteral(token.Text))
            .ShouldBe("outer (inner) still outer");
    }

    [Fact]
    public void Reads_a_literal_string_whose_parenthesis_is_escaped()
    {
        var lexer = new PdfLexer(@"(a \) b)"u8.ToArray());
        var token = lexer.Read();

        Encoding.ASCII.GetString(PdfStringDecoder.DecodeLiteral(token.Text)).ShouldBe("a ) b");
    }

    [Fact]
    public void Recovers_from_an_unterminated_literal_string()
    {
        var lexer = new PdfLexer("(never closed"u8.ToArray());
        var token = lexer.Read();

        token.Kind.ShouldBe(PdfTokenKind.LiteralString);
        lexer.IsAtEnd.ShouldBeTrue();
    }

    [Fact]
    public void Pads_an_odd_hexadecimal_string_with_a_trailing_zero()
    {
        var lexer = new PdfLexer("<901FA>"u8.ToArray());
        var token = lexer.Read();

        token.Kind.ShouldBe(PdfTokenKind.HexString);
        PdfStringDecoder.DecodeHex(token.Text).ShouldBe([0x90, 0x1F, 0xA0]);
    }

    [Fact]
    public void Ignores_whitespace_inside_a_hexadecimal_string()
    {
        var lexer = new PdfLexer("<48 65\n6C>"u8.ToArray());
        var token = lexer.Read();

        PdfStringDecoder.DecodeHex(token.Text).ShouldBe([0x48, 0x65, 0x6C]);
    }

    [Fact]
    public void Decodes_the_octal_and_control_escapes_of_a_literal_string()
    {
        var lexer = new PdfLexer(@"(tab:\t octal:\101 continued:\
end)"u8.ToArray());
        var token = lexer.Read();

        Encoding.ASCII.GetString(PdfStringDecoder.DecodeLiteral(token.Text))
            .ShouldBe("tab:\t octal:A continued:end");
    }

    [Fact]
    public void Never_loops_on_a_stray_delimiter()
    {
        var kinds = Tokenise(")))");

        kinds.ShouldBe([PdfTokenKind.Unknown, PdfTokenKind.Unknown, PdfTokenKind.Unknown]);
    }

    private static List<PdfTokenKind> Tokenise(string text)
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
