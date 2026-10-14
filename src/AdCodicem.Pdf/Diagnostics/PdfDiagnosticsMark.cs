namespace AdCodicem.Pdf.Diagnostics;

/// <summary>
/// A position in a <see cref="PdfDiagnostics"/>: how many entries it held, how many it had dropped, and how many guards had
/// been reached.
/// </summary>
/// <param name="Count">The number of entries kept.</param>
/// <param name="Suppressed">The number of entries dropped for want of capacity.</param>
/// <param name="Limits">The number of guards reached, kept or dropped.</param>
internal readonly record struct PdfDiagnosticsMark(int Count, int Suppressed, int Limits);
