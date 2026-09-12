using System.Buffers;

namespace AdCodicem.Pdf.IO;

/// <summary>Character classes of the PDF syntax, as defined by the specification's lexical rules.</summary>
internal static class PdfCharacters
{
    /// <summary>The six bytes the specification treats as white space.</summary>
    public static readonly SearchValues<byte> Whitespace = SearchValues.Create([0x00, 0x09, 0x0A, 0x0C, 0x0D, 0x20]);

    /// <summary>The bytes that delimit a token regardless of what surrounds them.</summary>
    public static readonly SearchValues<byte> Delimiters = SearchValues.Create("()<>[]{}/%"u8);

    public static bool IsWhitespace(byte value) => Whitespace.Contains(value);

    public static bool IsDelimiter(byte value) => Delimiters.Contains(value);

    /// <summary>A regular character is anything that is neither white space nor a delimiter.</summary>
    public static bool IsRegular(byte value) => !Whitespace.Contains(value) && !Delimiters.Contains(value);

    public static bool IsDigit(byte value) => value is >= (byte)'0' and <= (byte)'9';

    /// <summary>Returns the value of a hexadecimal digit, or -1 if the byte is not one.</summary>
    public static int HexValue(byte value) => value switch
    {
        >= (byte)'0' and <= (byte)'9' => value - '0',
        >= (byte)'A' and <= (byte)'F' => value - 'A' + 10,
        >= (byte)'a' and <= (byte)'f' => value - 'a' + 10,
        _ => -1,
    };
}
