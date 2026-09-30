namespace AdCodicem.Pdf.IO.Filters;

/// <summary>How a Flate stream's data ended, as far as decoding it could tell.</summary>
internal enum FlateEnding
{
    /// <summary>
    /// The data ended where its format says it does — or decoding stopped at its bound, before its end could
    /// be seen.
    /// </summary>
    Whole,

    /// <summary>
    /// The data ended after its last block, but before the whole of the zlib checksum that follows it: nothing
    /// was lost, and nothing could be checked.
    /// </summary>
    ChecksumMissing,

    /// <summary>
    /// The data decoded to the end of its last block, but the zlib checksum that follows it disagrees with what
    /// it decoded to: all of it was kept, as other readers keep it, and some of it may be wrong.
    /// </summary>
    ChecksumMismatch,

    /// <summary>
    /// The data ended before the end of its last block: its tail was lost, and what decoded before the end was
    /// kept.
    /// </summary>
    TailLost,

    /// <summary>
    /// The data turned corrupt. Decoding stopped at the fault, and what decoded before the byte it lies in was kept —
    /// what that one byte decoded ahead of the fault is lost with it —, which may be wrong too, since damage can lie
    /// before the point where decoding found it.
    /// </summary>
    Corrupt,
}
