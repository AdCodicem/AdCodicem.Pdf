namespace AdCodicem.Pdf.Diagnostics;

/// <summary>A position in a <see cref="PdfDiagnostics"/>: how many entries it held, and how many it had dropped.</summary>
/// <param name="Count">The number of entries kept.</param>
/// <param name="Suppressed">The number of entries dropped for want of capacity.</param>
internal readonly record struct PdfDiagnosticsMark(int Count, int Suppressed);
