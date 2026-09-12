namespace AdCodicem.Pdf.IO.Filters;

/// <summary>Decodes the <c>ASCIIHexDecode</c> filter.</summary>
internal static class AsciiHexFilter
{
    public static byte[] Decode(ReadOnlySpan<byte> data)
    {
        var buffer = new byte[(data.Length / 2) + 1];
        var length = 0;
        var high = -1;

        foreach (var current in data)
        {
            if (current == (byte)'>')
            {
                break;
            }

            var value = PdfCharacters.HexValue(current);
            if (value < 0)
            {
                // White space is allowed anywhere; anything else is skipped rather than fatal.
                continue;
            }

            if (high < 0)
            {
                high = value;
            }
            else
            {
                buffer[length++] = (byte)((high << 4) | value);
                high = -1;
            }
        }

        if (high >= 0)
        {
            buffer[length++] = (byte)(high << 4);
        }

        return buffer.AsSpan(0, length).ToArray();
    }
}
