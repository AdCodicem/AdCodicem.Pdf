namespace AdCodicem.Pdf.Diagnostics;

/// <summary>
/// A position in a <see cref="PdfDiagnostics"/>: how many entries it held, how many it had dropped, how many guards had
/// been reached, how many of the entries dropped were guards' reports, how far its note of what it dropped ran, and where
/// it looked in that note before the mark was taken.
/// </summary>
/// <param name="Count">The number of entries kept.</param>
/// <param name="Suppressed">The number of entries dropped for want of capacity.</param>
/// <param name="Limits">The number of guards reached, kept or dropped.</param>
/// <param name="LimitsSuppressed">The number of the entries dropped for want of capacity that were guards' reports.</param>
/// <param name="Dropped">How many severities and codes of dropped entries the instance noted.</param>
/// <param name="DecodingReports">
/// Which of the entries kept decodings of streams moved in, each with how many copies of it, for a mark
/// <see cref="PdfDiagnostics.GetMarkWithDecodingReports"/> took; null for one <see cref="PdfDiagnostics.GetMark"/> took.
/// </param>
internal readonly record struct PdfDiagnosticsMark(
    int Count,
    int Suppressed,
    int Limits,
    int LimitsSuppressed,
    int Dropped,
    IReadOnlyDictionary<PdfDiagnostic, int>? DecodingReports = null)
{
    /// <summary>
    /// Gets where the instance looked in its note of what it dropped before a pair was noted, as the mark was taken: the
    /// start of the enclosing mark still open, where it looks again once this mark is closed for good.
    /// </summary>
    public int DroppedFrom { get; init; }
}
