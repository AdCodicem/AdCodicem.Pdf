using System.Globalization;
using System.Text;

namespace AdCodicem.Pdf.Objects;

/// <summary>
/// Represents a PDF string: a sequence of bytes whose interpretation depends on where it appears.
/// </summary>
/// <remarks>
/// The original notation is kept so that a document can be rewritten without gratuitous differences.
/// Text strings are either PDFDocEncoded or UTF-16BE with a byte order mark; binary strings, such as the
/// entries of a document identifier, must never be run through a text conversion.
/// </remarks>
public sealed class PdfString : PdfObject
{
    /// <summary>
    /// The most characters of its text <see cref="ToString"/> shows: as many as a name's quote shows bytes, enough for a
    /// title or a label, and few enough for a log line.
    /// </summary>
    private const int ShownCharacters = IO.FileQuote.MaxBytes;

    /// <summary>Initializes a string from its raw bytes.</summary>
    /// <param name="bytes">The bytes, already unescaped.</param>
    /// <param name="hexadecimal">Whether the string was written in hexadecimal notation.</param>
    public PdfString(ReadOnlyMemory<byte> bytes, bool hexadecimal = false)
    {
        Bytes = bytes;
        IsHexadecimal = hexadecimal;
    }

    /// <summary>Gets the raw bytes of the string.</summary>
    public ReadOnlyMemory<byte> Bytes { get; }

    /// <summary>Gets a value indicating whether the string was written in hexadecimal notation.</summary>
    public bool IsHexadecimal { get; }

    /// <summary>Gets the number of bytes in the string.</summary>
    public int Length => Bytes.Length;

    /// <summary>
    /// Creates a text string: <paramref name="text"/> in ASCII, a byte per character, when it holds only printable
    /// ASCII, from space to tilde; otherwise in UTF-16BE with a byte order mark, in hexadecimal notation.
    /// </summary>
    /// <remarks>
    /// The choice is made for the whole text: one character outside printable ASCII — an accent, a tab, a line
    /// break — writes all of it in two bytes per UTF-16 code unit, after the two of the mark, so <c>é</c> alone
    /// takes four bytes. PDFDocEncoding, which holds it in one, is not used.
    /// </remarks>
    public static PdfString FromText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (IsAsciiPrintable(text))
        {
            var ascii = new byte[text.Length];
            for (var i = 0; i < text.Length; i++)
            {
                ascii[i] = (byte)text[i];
            }

            return new PdfString(ascii);
        }

        // Anything outside printable ASCII goes out as UTF-16BE with a byte order mark. Accented French
        // text written as raw bytes is the classic way to produce metadata that no reader displays correctly.
        var utf16 = Encoding.BigEndianUnicode.GetBytes(text);
        var buffer = new byte[utf16.Length + 2];
        buffer[0] = 0xFE;
        buffer[1] = 0xFF;
        utf16.CopyTo(buffer, 2);
        return new PdfString(buffer, hexadecimal: true);
    }

    /// <summary>
    /// Interprets the string as text.
    /// </summary>
    /// <remarks>
    /// A UTF-16 byte order mark selects UTF-16; otherwise the bytes are read as Latin-1, which matches
    /// PDFDocEncoding over the printable range. Full PDFDocEncoding is tracked as issue #36.
    /// </remarks>
    public string ToText() => TextEncoding(out var text).GetString(text);

    private static bool IsAsciiPrintable(string text)
    {
        foreach (var c in text)
        {
            if (c is < ' ' or > '~')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Describes the string for a debugger or a log: its text, as <see cref="ToText"/> reads it, between double quotes,
    /// with every character outside printable ASCII escaped as C# writes it in a literal — <c>\n</c>, <c>\u001B</c>,
    /// <c>\u202E</c> —, a backslash and a double quote too, and cut past 127 characters, with how many the whole holds.
    /// </summary>
    /// <remarks>
    /// The text is content the file chose, which <see cref="ToText"/> gives as it is. What this gives is on one line, in
    /// printable ASCII, and bounded whatever the file wrote: a line feed, an escape sequence or a mark that reverses the
    /// text after it reaches no terminal or log through it, and a string of megabytes is not copied whole.
    /// </remarks>
    public override string ToString()
    {
        var encoding = TextEncoding(out var bytes);
        var whole = encoding.GetCharCount(bytes);

        // One character more than is shown is decoded, so that a surrogate pair the cut falls in is still read as one.
        Span<char> text = stackalloc char[ShownCharacters + 1];
        var take = Math.Min(bytes.Length, encoding.IsSingleByte ? text.Length : 2 * text.Length);
        var shown = Math.Min(encoding.GetChars(bytes[..take], text), ShownCharacters);

        var described = new StringBuilder(2 + (6 * shown) + 48).Append('"');

        foreach (var c in text[..shown])
        {
            _ = c switch
            {
                '"' => described.Append("\\\""),
                '\\' => described.Append("\\\\"),
                '\0' => described.Append("\\0"),
                '\t' => described.Append("\\t"),
                '\n' => described.Append("\\n"),
                '\r' => described.Append("\\r"),
                >= ' ' and <= '~' => described.Append(c),
                _ => described.Append(CultureInfo.InvariantCulture, $"\\u{(int)c:X4}"),
            };
        }

        described.Append('"');

        if (whole > shown)
        {
            described.Append(CultureInfo.InvariantCulture, $" (the first {shown} of {whole:N0} characters)");
        }

        return described.ToString();
    }

    /// <summary>
    /// Says how the string's text is encoded, and where it starts: past a UTF-16 byte order mark, in UTF-16; otherwise,
    /// from its first byte, in Latin-1.
    /// </summary>
    private Encoding TextEncoding(out ReadOnlySpan<byte> text)
    {
        var span = Bytes.Span;

        if (span.Length >= 2 && span[0] == 0xFE && span[1] == 0xFF)
        {
            text = span[2..];
            return Encoding.BigEndianUnicode;
        }

        if (span.Length >= 2 && span[0] == 0xFF && span[1] == 0xFE)
        {
            text = span[2..];
            return Encoding.Unicode;
        }

        text = span;
        return Encoding.Latin1;
    }
}
