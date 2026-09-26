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
    public static byte[] Decode(
        ReadOnlySpan<byte> data, out bool limited, int maxLength = PdfFilterLimits.MaxDecodedLength)
    {
        limited = false;
        var output = new PdfBoundedOutput(data.Length * 2L, maxLength);
        var index = 0;

        while (index < data.Length)
        {
            var control = data[index++];

            if (control == 128)
            {
                break;
            }

            if (control < 128)
            {
                var count = control + 1;
                var available = Math.Min(count, data.Length - index);

                if (!output.TryWrite(data.Slice(index, available)))
                {
                    limited = true;
                    break;
                }

                index += available;
                continue;
            }

            if (index >= data.Length)
            {
                break;
            }

            var repeat = 257 - control;

            if (!output.TryFill(data[index++], repeat))
            {
                limited = true;
                break;
            }
        }

        return output.ToArray();
    }
}
