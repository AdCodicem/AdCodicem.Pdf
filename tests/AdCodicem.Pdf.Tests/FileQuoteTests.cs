using System.Text;
using AdCodicem.Pdf.IO;
using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// How a message quotes what the file wrote (#159): as a PDF writer writes a name, in printable ASCII on one line, and
/// no longer than <see cref="FileQuote.MaxBytes"/> bytes and a note of the whole's length.
/// </summary>
public class FileQuoteTests
{
    [Fact]
    public void Writes_printable_ascii_but_the_number_sign_and_the_delimiters_as_itself()
    {
        const string Verbatim = "!\"$&'*+,-.0123456789:;=?@ABCDEFGHIJKLMNOPQRSTUVWXYZ\\^_`abcdefghijklmnopqrstuvwxyz|~";

        FileQuote.Name(PdfName.Get(Verbatim)).Should().Be("/" + Verbatim);
    }

    [Theory]
    [InlineData('\0', "#00")]
    [InlineData('\t', "#09")]
    [InlineData('\n', "#0A")]
    [InlineData('\r', "#0D")]
    [InlineData('\u001B', "#1B")]
    [InlineData(' ', "#20")]
    [InlineData('#', "#23")]
    [InlineData('%', "#25")]
    [InlineData('(', "#28")]
    [InlineData(')', "#29")]
    [InlineData('/', "#2F")]
    [InlineData('<', "#3C")]
    [InlineData('>', "#3E")]
    [InlineData('[', "#5B")]
    [InlineData(']', "#5D")]
    [InlineData('{', "#7B")]
    [InlineData('}', "#7D")]
    [InlineData('\u007F', "#7F")]
    [InlineData('\u0080', "#80")]
    [InlineData('\u009B', "#9B")]
    [InlineData('é', "#E9")]
    [InlineData('ÿ', "#FF")]
    public void Writes_every_other_byte_as_a_number_sign_and_two_uppercase_hex_digits(char value, string written)
    {
        FileQuote.Name(PdfName.Get("a" + value + "b")).Should().Be("/a" + written + "b");
    }

    [Fact]
    public void Quotes_a_name_of_127_bytes_whole()
    {
        var value = new string('n', FileQuote.MaxBytes);

        FileQuote.Name(PdfName.Get(value)).Should().Be("/" + value);
    }

    [Fact]
    public void Quotes_the_first_127_bytes_of_a_longer_name_and_says_how_many_it_has()
    {
        var name = PdfName.Get(new string('n', 1_000_000));

        FileQuote.Name(name).Should().Be("/" + new string('n', 127) + " (the first 127 of 1,000,000 bytes)");
    }

    [Fact]
    public void Cuts_a_name_between_escapes_never_inside_one()
    {
        // 127 line feeds read back as 127 bytes: the quote writes each whole, and only then says where it stopped.
        var name = PdfName.Get(new string('\n', 200));

        FileQuote.Name(name).Should().Be("/" + string.Concat(Enumerable.Repeat("#0A", 127)) + " (the first 127 of 200 bytes)");
    }

    [Fact]
    public void Quotes_a_keyword_by_the_same_rule_with_no_solidus()
    {
        FileQuote.Keyword(Encoding.Latin1.GetBytes("tra\u001Bil#er\u009B")).Should().Be("tra#1Bil#23er#9B");
        FileQuote.Keyword(Encoding.ASCII.GetBytes(new string('k', 300))).Should().Be(new string('k', 127) + " (the first 127 of 300 bytes)");
    }

    [Theory]
    [InlineData("€", "#E2#82#AC")]
    [InlineData("\U0001F600", "#F0#9F#98#80")]
    public void Writes_a_character_no_file_can_hold_as_its_utf8_bytes(string value, string written)
    {
        // A name read from a file holds bytes, one per character up to U+00FF; a caller's name can hold more.
        FileQuote.Name(PdfName.Get("a" + value + "b")).Should().Be("/a" + written + "b");
    }

    [Fact]
    public void Writes_a_lone_surrogate_as_the_replacement_character_s_utf8_bytes()
    {
        FileQuote.Name(PdfName.Get("a" + '\uD800' + "b")).Should().Be("/a#EF#BF#BDb");
    }

    [Fact]
    public void Appends_a_name_as_it_writes_it()
    {
        var text = new StringBuilder("under ");

        FileQuote.AppendName(text, PdfName.Get("Key\n"));

        text.ToString().Should().Be("under /Key#0A");
    }

    [Fact]
    public void Quoting_a_name_of_64_million_characters_allocates_only_the_quote()
    {
        var name = PdfName.Get(new string('A', 64 * 1024 * 1024));
        _ = FileQuote.Name(name);

        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var quote = FileQuote.Name(name);
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;

        quote.Should().EndWith(" (the first 127 of 67,108,864 bytes)");
        allocated.Should().BeLessThan(1024, "the quote is written through a buffer on the stack, and only it is allocated");
    }
}
