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
    /// <summary>Initialises a string from its raw bytes.</summary>
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
    /// Creates a text string, choosing the narrowest encoding that can represent <paramref name="text"/>.
    /// </summary>
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
    /// PDFDocEncoding over the printable range. Full PDFDocEncoding is tracked as debt T06.
    /// </remarks>
    public string ToText()
    {
        var span = Bytes.Span;

        if (span.Length >= 2 && span[0] == 0xFE && span[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(span[2..]);
        }

        if (span.Length >= 2 && span[0] == 0xFF && span[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(span[2..]);
        }

        return Encoding.Latin1.GetString(span);
    }

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

    /// <inheritdoc/>
    public override string ToString() => ToText();
}
