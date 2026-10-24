namespace AdCodicem.Pdf.IO.Filters;

/// <summary>Decodes the <c>ASCII85Decode</c> filter.</summary>
internal static class Ascii85Filter
{
    /// <summary>
    /// Decodes <paramref name="data"/>, keeping at most <paramref name="maxLength"/> bytes;
    /// <paramref name="limited"/> says whether it decodes to more. A <c>z</c> alone decodes to four bytes.
    /// </summary>
    /// <remarks>
    /// What the format does not allow is decoded as other readers decode it, and counted in <paramref name="faults"/>:
    /// a byte outside the alphabet, or a <c>z</c> inside a group, is passed over; a group whose value does not fit in four
    /// bytes decodes to its four low-order bytes; a <c>~</c> ends the data, though no <c>&gt;</c> follows it; a last group
    /// of a single digit, which encodes no byte, is dropped; and data without its end marker decodes to its end.
    /// </remarks>
    /// <param name="data">The encoded data.</param>
    /// <param name="limited">Whether the data decodes to more than <paramref name="maxLength"/> bytes.</param>
    /// <param name="faults">What the data holds that the format does not allow.</param>
    /// <param name="maxLength">The most the data may decode to.</param>
    /// <param name="cut">
    /// Whether the data ends where the file's may not — the reader cut it short at one of its limits, or a guard hides
    /// where it ends —: a group left partial at its end, before any <c>~</c>, is one the cut parted, and is dropped rather
    /// than completed, though the bytes it encodes count against <paramref name="maxLength"/>, which
    /// <paramref name="limited"/> then says.
    /// </param>
    public static byte[] Decode(
        ReadOnlySpan<byte> data, out bool limited, out DataFaults faults, int maxLength, bool cut = false)
    {
        limited = false;
        faults = default;
        var ended = false;
        var output = new PdfBoundedOutput(Math.Max(16, data.Length * 4L / 5), maxLength);
        Span<byte> group = stackalloc byte[4];

        // The 'z' shortcut writes four zero bytes; allocated once here rather than inside the loop.
        Span<byte> zeros = stackalloc byte[4];

        // Five digits reach 85^5 - 1, past what four bytes hold: the group is summed in 64 bits, and a value that does
        // not fit is told from one that does.
        var tuple = 0UL;
        var count = 0;
        var last = 0;
        var index = 0;

        // Some producers keep the "<~" introducer even though PDF does not use it.
        if (data.Length >= 2 && data[0] == (byte)'<' && data[1] == (byte)'~')
        {
            faults.Introducer = true;
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
                // The end marker is ~ then >, white space allowed between them. A ~ that something else follows ends the
                // data all the same, as other readers end it.
                ended = true;
                var next = index + 1;

                while (next < data.Length && PdfCharacters.IsWhitespace(data[next]))
                {
                    next++;
                }

                if (next == data.Length)
                {
                    faults.UnfinishedMarker = true;
                }
                else if (data[next] != (byte)'>')
                {
                    faults.BrokenMarker = index + 1;
                }

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
                // A byte outside the alphabet, or a z inside a group, where it stands for nothing.
                faults.Pass(index);
                continue;
            }

            tuple = (tuple * 85) + (uint)(current - '!');
            count++;
            last = index;

            if (count != 5)
            {
                continue;
            }

            if (tuple > uint.MaxValue)
            {
                faults.Overflow(index);
            }

            if (!WriteGroup(output, (uint)tuple, 4, group))
            {
                limited = true;
                return output.ToArray();
            }

            tuple = 0;
            count = 0;
        }

        if (count == 1)
        {
            // A single digit encodes no byte: a group needs two at least.
            faults.LoneDigit = ended;
        }
        else if (count > 1 && (ended || !cut))
        {
            // A partial group encodes count-1 bytes; the missing digits count as 'u'.
            for (var i = count; i < 5; i++)
            {
                tuple = (tuple * 85) + 84;
            }

            if (tuple > uint.MaxValue)
            {
                faults.Overflow(last);
            }

            limited = !WriteGroup(output, (uint)tuple, count - 1, group);
        }
        else if (count > 1)
        {
            // Data that ends where the file's may not leaves a group it parts, whose bytes the missing digits decide: none is
            // written. The whole data holds at least the count-1 bytes it encodes, which the bound counts all the same.
            limited = count - 1 > maxLength - output.Count;
        }

        if (!ended)
        {
            faults.Ending = count switch
            {
                0 => DataEnding.Unmarked,
                1 => DataEnding.UnmarkedAfterLoneDigit,
                _ => DataEnding.UnmarkedInsideUnit,
            };
            faults.UnitLength = count;
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
