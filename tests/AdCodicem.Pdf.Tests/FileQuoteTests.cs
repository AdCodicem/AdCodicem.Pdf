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

    [Theory]
    [InlineData(42, 126, 126)]
    [InlineData(43, 126, 129)]
    [InlineData(200, 126, 600)]
    public void Cuts_a_caller_s_name_at_127_bytes_never_inside_a_character_and_counts_the_whole_in_bytes(int euros, int shown, int whole)
    {
        // A euro sign is three bytes: 42 make 126, and a 43rd would pass 127.
        var quote = FileQuote.Name(PdfName.Get(new string('€', euros)));

        var written = "/" + string.Concat(Enumerable.Repeat("#E2#82#AC", Math.Min(euros, 42)));
        quote.Should().Be(whole == shown ? written : written + $" (the first {shown} of {whole} bytes)");
    }

    [Theory]
    [InlineData(123, "#F0#9F#98#80", "")]
    [InlineData(124, "", " (the first 124 of 128 bytes)")]
    [InlineData(126, "", " (the first 126 of 130 bytes)")]
    public void Never_splits_a_surrogate_pair_at_the_cut(int prefix, string pair, string note)
    {
        // The pair is four bytes: after 123 others it ends at byte 127; after more, the quote stops before it.
        var quote = FileQuote.Name(PdfName.Get(new string('a', prefix) + "\U0001F600"));

        quote.Should().Be("/" + new string('a', prefix) + pair + note);
    }

    [Fact]
    public void Counts_a_caller_s_lone_surrogates_as_the_replacement_character_s_bytes()
    {
        FileQuote.Name(PdfName.Get(new string('\uDC00', 50))).Should().EndWith(" (the first 126 of 150 bytes)");
    }

    [Fact]
    public void Counts_a_caller_s_characters_up_to_u00ff_as_one_byte_each_as_it_writes_them()
    {
        // A name read from a file holds such a character for its byte: the quote writes #E9, and counts what it writes.
        var quote = FileQuote.Name(PdfName.Get(new string('é', 200) + "€"));

        quote.Should().Be("/" + string.Concat(Enumerable.Repeat("#E9", 127)) + " (the first 127 of 203 bytes)");
    }

    [Theory]
    [InlineData("Type", true)]
    [InlineData("\0\u00FF", true)]
    [InlineData("a€", false)]
    [InlineData("\uD800", false)]
    public void Knows_whether_a_name_holds_one_byte_per_character(string value, bool isBytes)
    {
        PdfName.Get(value).IsBytes.Should().Be(isBytes);
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
