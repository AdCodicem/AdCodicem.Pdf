namespace AdCodicem.Pdf.Diagnostics;

/// <summary>
/// A position in a <see cref="PdfDiagnostics"/>: how many entries it held, how many it had dropped, how many guards had
/// been reached, and how many of the entries dropped were guards' reports.
/// </summary>
/// <param name="Count">The number of entries kept.</param>
/// <param name="Suppressed">The number of entries dropped for want of capacity.</param>
/// <param name="Limits">The number of guards reached, kept or dropped.</param>
/// <param name="LimitsSuppressed">The number of the entries dropped for want of capacity that were guards' reports.</param>
/// <param name="DecodingReports">
/// Which of the entries kept decodings of streams moved in, each with how many copies of it, for a mark
/// <see cref="PdfDiagnostics.GetMarkWithDecodingReports"/> took; null for one <see cref="PdfDiagnostics.GetMark"/> took.
/// </param>
internal readonly record struct PdfDiagnosticsMark(
    int Count, int Suppressed, int Limits, int LimitsSuppressed, IReadOnlyDictionary<PdfDiagnostic, int>? DecodingReports = null);
