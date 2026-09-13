using System.Buffers;

namespace AdCodicem.Pdf.IO.Filters;

/// <summary>Decodes the <c>ASCII85Decode</c> filter.</summary>
internal static class Ascii85Filter
{
    public static byte[] Decode(ReadOnlySpan<byte> data)
    {
        var output = new ArrayBufferWriter<byte>(Math.Max(16, data.Length * 4 / 5));
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
                output.Write(zeros);
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

            WriteGroup(output, tuple, 4, group);
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

            WriteGroup(output, tuple, count - 1, group);
        }

        return output.WrittenSpan.ToArray();
    }

    private static void WriteGroup(ArrayBufferWriter<byte> output, uint tuple, int bytes, Span<byte> group)
    {
        group[0] = (byte)(tuple >> 24);
        group[1] = (byte)(tuple >> 16);
        group[2] = (byte)(tuple >> 8);
        group[3] = (byte)tuple;
        output.Write(group[..bytes]);
    }
}
