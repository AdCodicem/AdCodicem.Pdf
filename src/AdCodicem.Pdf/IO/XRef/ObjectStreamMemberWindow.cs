namespace AdCodicem.Pdf.IO.XRef;

/// <summary>Where an object stream's member is read, in the stream's decoded data, as the reader and the validator read it.</summary>
/// <param name="Start">Where the member starts.</param>
/// <param name="End">Where it is read to.</param>
/// <param name="EndsData">
/// Whether <paramref name="End"/> is where the member must end — the next member's start, or the end of the data —, rather
/// than where a guard cut it: only then is what the end cuts short the data's fault.
/// </param>
/// <param name="EndsAtNextMember">Whether <paramref name="End"/> is the next member's start.</param>
/// <param name="Bounded">Whether <see cref="Documents.PdfReaderLimits.MaxObjectLength"/> brought <paramref name="End"/> nearer.</param>
internal readonly record struct ObjectStreamMemberWindow(int Start, int End, bool EndsData, bool EndsAtNextMember, bool Bounded)
{
    /// <summary>Gives the window of the member at <paramref name="index"/> of a header's pairs.</summary>
    /// <remarks>
    /// A member is read to the next one's start, the offsets increasing, and the last to the end of the data: the members'
    /// reads together cover the data once (#200). An internal bound (ADR 34): a valid file's members lie each before the
    /// next one's start, and the reads of every member of the corpus end there. A member is read no further than
    /// <paramref name="maxLength"/>, as no object of the file is (#160). The member is taken to start within the data: a
    /// header keeps a pair whose member starts past it only when a guard cut the data, which leaves the member before it the
    /// rest of the data.
    /// </remarks>
    /// <param name="offsets">The pairs' offsets, from <paramref name="first"/>, increasing.</param>
    /// <param name="index">The member's place among them.</param>
    /// <param name="first">The stream's <c>/First</c>.</param>
    /// <param name="dataLength">How many bytes the stream decodes to.</param>
    /// <param name="cutByGuard">Whether a guard cut the data: its end is then the limit's, not the file's.</param>
    /// <param name="maxLength">The most of the data a member is read over, <see cref="Documents.PdfReaderLimits.MaxObjectLength"/>.</param>
    public static ObjectStreamMemberWindow Of(int[] offsets, int index, int first, int dataLength, bool cutByGuard, int maxLength)
    {
        // Both are at most int.MaxValue, so their sum is computed as a long, where it cannot wrap; within the data, it fits
        // an int.
        var start = (int)((long)first + offsets[index]);
        var end = index + 1 < offsets.Length ? (int)Math.Min((long)first + offsets[index + 1], dataLength) : dataLength;
        var nextMember = end < dataLength;

        return end - start > maxLength
            ? new ObjectStreamMemberWindow(start, start + maxLength, EndsData: false, EndsAtNextMember: false, Bounded: true)
            : new ObjectStreamMemberWindow(start, end, nextMember || !cutByGuard, nextMember, Bounded: false);
    }
}
