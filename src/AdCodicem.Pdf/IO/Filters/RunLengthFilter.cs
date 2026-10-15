namespace AdCodicem.Pdf.IO.Filters;

/// <summary>Decodes the <c>RunLengthDecode</c> filter.</summary>
/// <remarks>
/// Two bytes in can decode to 128 out, so the output is bounded like every filter's: a stream of a few
/// megabytes would otherwise decode to whatever a hostile file asked for.
/// </remarks>
internal static class RunLengthFilter
{
    /// <summary>
    /// Decodes <paramref name="data"/>, keeping at most <paramref name="maxLength"/> bytes;
    /// <paramref name="limited"/> says whether it decodes to more.
    /// </summary>
    /// <remarks>
    /// Data without its end-of-data byte, 128, decodes to its end, and <paramref name="faults"/> says how it ended: after
    /// a whole run; inside a run of bytes written as they are, those it holds kept; or after the length of a run that
    /// repeats a byte, without that byte.
    /// </remarks>
    /// <param name="data">The encoded data.</param>
    /// <param name="limited">Whether the data decodes to more than <paramref name="maxLength"/> bytes.</param>
    /// <param name="faults">How the data ended, when it ended without its end-of-data byte.</param>
    /// <param name="maxLength">The most the data may decode to.</param>
    public static byte[] Decode(
        ReadOnlySpan<byte> data, out bool limited, out DataFaults faults, int maxLength)
    {
        limited = false;
        faults = default;
        var output = new PdfBoundedOutput(data.Length * 2L, maxLength);
        var index = 0;

        while (index < data.Length)
        {
            var control = data[index++];

            if (control == 128)
            {
                return output.ToArray();
            }

            if (control < 128)
            {
                var count = control + 1;
                var available = Math.Min(count, data.Length - index);

                if (!output.TryWrite(data.Slice(index, available)))
                {
                    limited = true;
                    return output.ToArray();
                }

                index += available;

                if (available < count)
                {
                    faults.Ending = DataEnding.UnmarkedInsideRun;
                    faults.UnitLength = count;
                    faults.UnitHeld = available;
                    return output.ToArray();
                }

                continue;
            }

            if (index >= data.Length)
            {
                faults.Ending = DataEnding.UnmarkedBeforeRepeatedByte;
                return output.ToArray();
            }

            var repeat = 257 - control;

            if (!output.TryFill(data[index++], repeat))
            {
                limited = true;
                return output.ToArray();
            }
        }

        faults.Ending = DataEnding.Unmarked;
        return output.ToArray();
    }
}
