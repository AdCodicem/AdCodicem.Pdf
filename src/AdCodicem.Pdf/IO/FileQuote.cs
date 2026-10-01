using System.Buffers;
using System.Globalization;
using System.Text;
using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.IO;

/// <summary>
/// Quotes in a message what the file wrote — a name, a keyword — as a PDF writer writes a name: on one line, in printable
/// ASCII, every byte it cannot write as itself as <c>#xx</c>, and cut past <see cref="MaxBytes"/>.
/// </summary>
/// <remarks>
/// A message repeats what it quotes, once for every report, and a host prints it: a quote must neither carry what the
/// file put there — a line feed, an escape sequence, a byte a terminal takes for one — nor grow with it. A quote reads
/// back as the name it quotes: a number sign, a delimiter, white space and every byte outside printable ASCII are written
/// <c>#xx</c>, as ISO 32000-1, 7.3.5, has a writer do. A keyword's bytes follow the same rule, its <c>#xx</c> standing
/// for a byte as in a name. A string from the file is never quoted: it is content, not syntax.
/// </remarks>
internal static class FileQuote
{
    /// <summary>
    /// The most bytes of a name or keyword a message repeats: 127, the longest name ISO 32000-1's Annex C and PDF/A allow,
    /// so that every name such a file holds is quoted whole.
    /// </summary>
    /// <remarks>
    /// No guard (ADR 34): it bounds what a message repeats, not what is read. The whole name stays in the document, in
    /// <see cref="PdfName.Value"/>; a longer quote ends with how many bytes the whole holds.
    /// </remarks>
    internal const int MaxBytes = 127;

    /// <summary>
    /// The longest quote: a solidus, nine characters for each of <see cref="MaxBytes"/> characters — a character above
    /// U+00FF, which only a caller's name can hold, writes up to three bytes —, and the note of a cut.
    /// </summary>
    private const int BufferLength = 1 + (9 * MaxBytes) + 64;

    /// <summary>Writes <paramref name="name"/> as a PDF writer would, with its solidus.</summary>
    public static string Name(PdfName name)
    {
        Span<char> buffer = stackalloc char[BufferLength];
        return new string(buffer[..Write('/', name.Value, name.Value.Length, buffer)]);
    }

    /// <summary>Appends <paramref name="name"/> to <paramref name="text"/> as a PDF writer would, with its solidus.</summary>
    public static void AppendName(StringBuilder text, PdfName name)
    {
        Span<char> buffer = stackalloc char[BufferLength];
        text.Append(buffer[..Write('/', name.Value, name.Value.Length, buffer)]);
    }

    /// <summary>Writes a keyword's bytes, each outside printable ASCII as <c>#xx</c>.</summary>
    public static string Keyword(ReadOnlySpan<byte> keyword)
    {
        Span<char> shown = stackalloc char[MaxBytes];
        var count = Encoding.Latin1.GetChars(keyword[..Math.Min(keyword.Length, MaxBytes)], shown);
        Span<char> buffer = stackalloc char[BufferLength];
        return new string(buffer[..Write(null, shown[..count], keyword.Length, buffer)]);
    }

    /// <summary>
    /// Writes into <paramref name="destination"/> the lead, at most <see cref="MaxBytes"/> characters of
    /// <paramref name="value"/>, one per byte, and, when the whole is longer, the note that says how long, and returns how
    /// many characters it wrote.
    /// </summary>
    private static int Write(char? lead, ReadOnlySpan<char> value, int whole, Span<char> destination)
    {
        var length = 0;

        if (lead is { } solidus)
        {
            destination[length++] = solidus;
        }

        var shown = Math.Min(value.Length, MaxBytes);
        Span<byte> encoded = stackalloc byte[4];

        for (var index = 0; index < shown; index++)
        {
            var current = value[index];

            if (current <= 0xFF)
            {
                length += Write((byte)current, destination[length..]);
                continue;
            }

            // Only a caller's PdfName.Get makes such a name: its UTF-8 bytes, a lone surrogate as U+FFFD's.
            if (Rune.DecodeFromUtf16(value[index..shown], out var rune, out var consumed) != OperationStatus.Done)
            {
                rune = Rune.ReplacementChar;
            }

            index += Math.Max(consumed, 1) - 1;
            var count = rune.EncodeToUtf8(encoded);

            for (var b = 0; b < count; b++)
            {
                length += Write(encoded[b], destination[length..]);
            }
        }

        if (whole > shown)
        {
            // A space no quote holds: the note cannot be read as the name's.
            destination[length..].TryWrite(CultureInfo.InvariantCulture, $" (the first {shown} of {whole:N0} bytes)", out var written);
            length += written;
        }

        return length;
    }

    /// <summary>Writes a byte as itself when a writer would, as <c>#xx</c> otherwise, and returns how many characters it wrote.</summary>
    private static int Write(byte value, Span<char> destination)
    {
        if (PdfCharacters.NameVerbatim.Contains(value))
        {
            destination[0] = (char)value;
            return 1;
        }

        destination[0] = '#';
        destination[1] = HexDigit(value >> 4);
        destination[2] = HexDigit(value & 0xF);
        return 3;
    }

    private static char HexDigit(int value) => (char)(value < 10 ? '0' + value : 'A' + value - 10);
}
