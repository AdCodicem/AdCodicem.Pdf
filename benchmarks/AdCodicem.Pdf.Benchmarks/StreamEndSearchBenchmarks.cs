using System.Globalization;
using System.Text;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.Objects;
using BenchmarkDotNet.Attributes;

namespace AdCodicem.Pdf.Benchmarks;

/// <summary>
/// Measures the search for a lost <c>endstream</c> over 4 MB of data, shaped against the rule for object headers that
/// stops it, which reads comments between a header's tokens as a load does (#261).
/// </summary>
/// <remarks>
/// <para>
/// The stream's <c>/Length</c> runs past its data, so that the reader searches the file for its <c>endstream</c>, which
/// follows the data. The search carries into each read what the reads before hold of a header: the runs of white space
/// and digits that end where it starts, and the line it starts in, lexed from its start as a load reads it. A line of text
/// or binary data costs what a search costs; a long token — a keyword, a number, the letters of a string —, a long run of
/// white space or a hexadecimal string is passed with one search each, each arm of the line's lexer that passes a run
/// held by a shape of its own; a long line of one-byte tokens, of strings opened one inside the other, or a run of comment
/// lines is lexed a token at a time, as a load's lexer reads it, which is the most the search costs. Every shape costs time
/// linear in the data, and allocates nothing for it.
/// </para>
/// <para>
/// Each run opens the document, from memory, and reads the stream: the search runs once a document.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class StreamEndSearchBenchmarks
{
    /// <summary>How many bytes the stream's data holds.</summary>
    private const int Size = 4 * 1024 * 1024;

    private byte[] _document = [];

    /// <summary>
    /// Gets or sets what the data is made of: lines of text, as content streams hold them; bytes of every value but
    /// <c>o</c>, <c>b</c>, <c>j</c> and <c>e</c>, so that they spell no keyword; one token of letters; one number; one line
    /// of one-byte tokens; a string never closed, its letters ending in <c>%</c> and a line <c>0 obj</c>; strings opened
    /// one inside the other and never closed, ending so too; lines that hold only <c>%</c>; one run of white space; a line
    /// of one hexadecimal string.
    /// </summary>
    [Params(
        "text-lines",
        "binary",
        "one-token",
        "digits",
        "one-byte-tokens",
        "open-string",
        "nested-strings",
        "comment-lines",
        "white-space",
        "hex-string")]
    public string Shape { get; set; } = "text-lines";

    [GlobalSetup]
    public void Setup()
    {
        var data = Shape switch
        {
            "text-lines" => Repeat("BT /F1 12 Tf 72 712 Td (Hello, world) Tj ET\n"),
            "binary" => Binary(),
            "one-token" => new string('x', Size),
            "digits" => new string('1', Size),
            "one-byte-tokens" => Repeat("1 "),
            "open-string" => "(" + new string('a', Size - 8) + "%\n0 obj",
            "nested-strings" => new string('(', Size - 7) + "%\n0 obj",
            "comment-lines" => Repeat("%\n"),
            "white-space" => new string(' ', Size),
            "hex-string" => "<" + new string('A', Size - 2) + ">",
            _ => throw new InvalidOperationException($"No shape {Shape}."),
        };

        var text = new StringBuilder("%PDF-1.7\n");
        var catalog = text.Length;
        text.Append("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        var pages = text.Length;
        text.Append("2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n");
        var stream = text.Length;
        text.Append(CultureInfo.InvariantCulture, $"3 0 obj\n<< /Length {data.Length + 1000} >>\nstream\n")
            .Append(data)
            .Append("\nendstream\nendobj\n");
        var xref = text.Length;
        text.Append(CultureInfo.InvariantCulture, $"xref\n0 4\n0000000000 65535 f \n{catalog:D10} 00000 n \n{pages:D10} 00000 n \n{stream:D10} 00000 n \n")
            .Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size 4 /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        _document = Encoding.Latin1.GetBytes(text.ToString());
    }

    [Benchmark(Description = "Search 4 MB of data for a lost endstream")]
    public int Search()
    {
        using var document = PdfDocument.Open(_document);
        return document.GetObject(new PdfObjectId(3)) is PdfStream stream ? stream.RawLength : -1;
    }

    /// <summary>Repeats <paramref name="unit"/> up to <see cref="Size"/> bytes.</summary>
    private static string Repeat(string unit)
    {
        var text = new StringBuilder(Size + unit.Length);

        while (text.Length < Size)
        {
            text.Append(unit);
        }

        return text.ToString(0, Size);
    }

    /// <summary>
    /// Draws <see cref="Size"/> bytes from a fixed seed, every value but <c>o</c>, <c>b</c>, <c>j</c> and <c>e</c>, so that
    /// the data spells no keyword: neither <c>obj</c>, <c>endobj</c> nor <c>endstream</c>.
    /// </summary>
    private static string Binary()
    {
        var random = new Random(1729);
        var chars = new char[Size];

        for (var i = 0; i < Size; i++)
        {
            var value = random.Next(256);
            chars[i] = value is 'o' or 'b' or 'j' or 'e' ? 'z' : (char)value;
        }

        return new string(chars);
    }
}
