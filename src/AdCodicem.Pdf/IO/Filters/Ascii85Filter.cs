namespace AdCodicem.Pdf.IO.Filters;

/// <summary>Decodes the <c>ASCII85Decode</c> filter.</summary>
internal static class Ascii85Filter
{
    /// <summary>
    /// Decodes <paramref name="data"/>, keeping at most <paramref name="maxLength"/> bytes;
    /// <paramref name="limited"/> says whether it decodes to more. A <c>z</c> alone decodes to four bytes.
    /// </summary>
    public static byte[] Decode(
        ReadOnlySpan<byte> data, out bool limited, int maxLength)
    {
        limited = false;
        var output = new PdfBoundedOutput(Math.Max(16, data.Length * 4L / 5), maxLength);
        Span<byte> group = stackalloc byte[4];

        // The 'z' shortcut writes four zero bytes; allocated once here rather than inside the loop.
        Span<byte> zeros = stackalloc byte[4];
        var tuple = 0u;
        var count = 0;
        var index = 0;

        // Some producers keep the "<~" introducer even though PDF does not use it.
        if (data.Length >= 2 && data[0] == (byte)'<' && data[1] == (byte)'~')
        {
            index = 2;
        }

        for (; index < data.Length; index++)
        {
            var current = data[index];

            if (PdfCharacters.IsWhitespace(current))
            {
                continue;
            }

            if (current == (byte)'~')
            {
                break;
            }

            if (current == (byte)'z' && count == 0)
            {
                if (!output.TryWrite(zeros))
                {
                    limited = true;
                    return output.ToArray();
                }

                continue;
            }

            if (current is < (byte)'!' or > (byte)'u')
            {
                continue;
            }

            tuple = (tuple * 85) + (uint)(current - '!');
            count++;

            if (count != 5)
            {
                continue;
            }

            if (!WriteGroup(output, tuple, 4, group))
            {
                limited = true;
                return output.ToArray();
            }

            tuple = 0;
            count = 0;
        }

        if (count > 1)
        {
            // A partial group encodes count-1 bytes; the missing digits count as 'u'.
            for (var i = count; i < 5; i++)
            {
                tuple = (tuple * 85) + 84;
            }

            limited = !WriteGroup(output, tuple, count - 1, group);
        }

        return output.ToArray();
    }

    private static bool WriteGroup(PdfBoundedOutput output, uint tuple, int bytes, Span<byte> group)
    {
        group[0] = (byte)(tuple >> 24);
        group[1] = (byte)(tuple >> 16);
        group[2] = (byte)(tuple >> 8);
        group[3] = (byte)tuple;
        return output.TryWrite(group[..bytes]);
    }
}
